using System.Globalization;
using Keypuncher.Core;
using Keypuncher.Director;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>What one try at a thing's picture came to.</summary>
public enum FetchOutcome
{
    /// <summary>A cut-out is in the folder and the thing is off the wanted list.</summary>
    Pictured,
    /// <summary>A photo was saved under <c>raw/</c> but there is no cut-out: the model is missing, or its matte failed the gate.</summary>
    RawOnly,
    /// <summary>The web was reached and no usable photo was found, or what was found was no good; the try counts.</summary>
    NothingSuitable,
    /// <summary>The web could not be reached; the try does not count.</summary>
    Offline,
    /// <summary>There is a picture already (in play or hidden) and nothing asked for another.</summary>
    AlreadyHave,
}

/// <summary>Fetches a picture of one real thing; what <see cref="PictureJobs"/> runs in the background.</summary>
public interface IPictureFetcher
{
    Task<FetchOutcome> FetchAsync(string name, RealCategory category, CancellationToken ct = default);
}

/// <summary>
/// Gets a real thing its picture: asks the finder for a photo, downloads it into <c>raw/</c>, writes the sidecar,
/// cuts the subject out and puts the cut-out in play. Keeps the wanted list honest at every step: an offline try is
/// not counted, a missing cut-out model keeps the raw photo for later, a failed matte turns that file down so the
/// next try takes another. The whole thing is over in <see cref="Limit"/> or it gives up as offline. Blocking work:
/// run it off the UI thread.
/// </summary>
public sealed class PhotoFetcher : IPictureFetcher
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    public const string ModelMissing = "cut-out model missing";

    private readonly PictureFolder _folder;
    private readonly IPhotoFinder _finder;
    private readonly ICutOut _cutOut;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _clock;
    private readonly Action<string>? _note;

    /// <param name="http">The client downloads go through; <see cref="PictureHttp.NewClient"/> unless a test says otherwise.</param>
    /// <param name="note">Where to send a line about each step, for the director log; null for silence.</param>
    public PhotoFetcher(PictureFolder folder, IPhotoFinder finder, ICutOut cutOut, HttpClient? http = null, Func<DateTime>? clock = null, Action<string>? note = null)
    {
        _folder = folder;
        _finder = finder;
        _cutOut = cutOut;
        _http = http ?? PictureHttp.NewClient();
        _clock = clock ?? (() => DateTime.Now);
        _note = note;
    }

    public async Task<FetchOutcome> FetchAsync(string name, RealCategory category, CancellationToken ct = default)
    {
        var slug = Slugs.For(name);
        var now = _clock();
        var wanted = _folder.Wanted.Get(slug);
        var exclude = wanted?.Exclude ?? [];
        if ((_folder.HasPicture(slug) || _folder.IsHidden(slug)) && exclude.Count == 0)
        {
            _folder.Wanted.Remove(slug);
            return FetchOutcome.AlreadyHave;
        }
        wanted ??= _folder.Wanted.Add(name, category, now);

        // A photo kept from an earlier visit, when the cut-out model was missing, is cut now rather than fetched again.
        if (!_folder.HasPicture(slug) && _folder.ReadSidecar(slug) is { Source: PictureSource.Photo, Photo: { } kept } sidecar
            && _folder.FindRaw(slug) is { } rawPath && !exclude.Contains(kept.FileTitle) && _cutOut.IsAvailable)
        {
            using var raw = Decode(File.ReadAllBytes(rawPath));
            if (raw is not null) { Note($"{name}: cutting the photo kept earlier"); return Cut(sidecar, raw, now); }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        PhotoSearch search;
        try { search = await _finder.FindAsync(name, category, exclude, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Offline(slug, name, "took too long", now); }
        foreach (var line in search.Notes) Note($"{name}: {line}");
        if (search.Offline) return Offline(slug, name, search.Reason ?? "no network", now);
        if (search.Photo is not { } photo)
        {
            var reason = search.Reason ?? "no photo found";
            _folder.Wanted.Bump(slug, reason, now);
            Note($"{name}: {reason}");
            return FetchOutcome.NothingSuitable;
        }

        (byte[] Bytes, string? Mime)? download;
        try { download = await PictureHttp.DownloadAsync(_http, photo.DownloadUrl, cts.Token); }
        catch (Exception e) when (PictureHttp.IsNetworkTrouble(e, ct)) { return Offline(slug, name, e.Message, now); }
        if (download is not { } got) return TurnedDown(slug, name, photo.Provenance.FileTitle, "could not be downloaded", now);
        using var bitmap = Decode(got.Bytes);
        if (bitmap is null) return TurnedDown(slug, name, photo.Provenance.FileTitle, "is not a picture we can read", now);

        var mime = got.Mime is { } m && m.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? m : photo.Provenance.Mime;
        _folder.WriteRaw(slug, ExtensionFor(mime), got.Bytes);
        var fresh = new PictureSidecar(slug, name, category, PictureSource.Photo, now)
        {
            Photo = photo.Provenance with { Mime = mime },
            Alternatives = exclude,
            CutOut = photo.Doubt is { } doubt ? $"taken with doubt: {doubt}" : null,
        };
        _folder.WriteSidecar(fresh);
        Note($"{name}: took {photo.Provenance.FileTitle} ({photo.Provenance.How}; {photo.Provenance.LicenseShortName})");
        return Cut(fresh, bitmap, now);
    }

    /// <summary>Makes the cut-out again from the photo kept under <c>raw/</c>, for the console tool and the grown-ups' page.</summary>
    public FetchOutcome Recut(string slug)
    {
        var sidecar = _folder.ReadSidecar(slug);
        var rawPath = _folder.FindRaw(slug);
        if (sidecar is null || rawPath is null) return FetchOutcome.NothingSuitable;
        using var raw = Decode(File.ReadAllBytes(rawPath));
        if (raw is null) return FetchOutcome.NothingSuitable;
        _folder.Wanted.Add(sidecar.Name, sidecar.Category, _clock());
        return Cut(sidecar, raw, _clock());
    }

    private FetchOutcome Cut(PictureSidecar sidecar, SKBitmap photo, DateTime now)
    {
        if (!_cutOut.IsAvailable)
        {
            _folder.Wanted.Bump(sidecar.Slug, ModelMissing, now, countTry: false);
            Note($"{sidecar.Name}: photo kept; {ModelMissing}");
            return FetchOutcome.RawOnly;
        }
        var result = _cutOut.Cut(photo);
        if (result.Picture is not { } picture)
        {
            var why = result.Reason ?? "no matte";
            _folder.WriteSidecar(sidecar with { CutOut = $"{result.Model} failed: {why}" });
            _folder.Wanted.Bump(sidecar.Slug, $"cut-out failed: {why}", now, exclude: sidecar.Photo?.FileTitle);
            Note($"{sidecar.Name}: cut-out by {result.Model} failed: {why}");
            return FetchOutcome.RawOnly;
        }
        using (picture)
        {
            var covers = ((int)Math.Round(result.Coverage * 100)).ToString(CultureInfo.InvariantCulture);
            _folder.WriteSidecar(sidecar with { Hidden = false, CutOut = $"{result.Model}, covers {covers}% of the photo" });
            _folder.WritePicture(sidecar.Slug, picture);
        }
        _folder.Wanted.Remove(sidecar.Slug);
        Note($"{sidecar.Name}: picture in play ({result.Model})");
        return FetchOutcome.Pictured;
    }

    private FetchOutcome Offline(string slug, string name, string why, DateTime now)
    {
        _folder.Wanted.Bump(slug, "offline", now, countTry: false);
        Note($"{name}: offline ({why})");
        return FetchOutcome.Offline;
    }

    private FetchOutcome TurnedDown(string slug, string name, string fileTitle, string why, DateTime now)
    {
        _folder.Wanted.Bump(slug, $"{fileTitle} {why}", now, exclude: fileTitle);
        Note($"{name}: {fileTitle} {why}");
        return FetchOutcome.NothingSuitable;
    }

    private void Note(string line) => _note?.Invoke(line);

    /// <summary>A photo as the cut-out wants it: RGBA, unpremultiplied. Null when the bytes are not a picture.</summary>
    public static SKBitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
        var bitmap = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        return bitmap;
    }

    /// <summary>The file extension a photo is kept under: by its MIME type, JPEG when unsure.</summary>
    public static string ExtensionFor(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };
}
