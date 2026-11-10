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
        // The director files some things wrongly (a sailboat as an animal), and the search is steered by the kind:
        // where a grown-up has set it, theirs is used.
        if (_folder.Cards.Get(slug)?.Category is { } set && set != category)
        {
            Note($"{name}: looked for as {set}, as a grown-up set, not {category}");
            category = set;
        }
        var wanted = _folder.Wanted.Get(slug);
        var asked = wanted?.Exclude ?? [];
        var inPlay = _folder.HasPicture(slug) || _folder.IsHidden(slug);
        // A hidden picture still on the wanted list is one the checker turned down; one a grown-up hid is left hidden.
        if (asked.Count == 0 && (_folder.HasPicture(slug) || (_folder.IsHidden(slug) && wanted is null)))
        {
            _folder.Wanted.Remove(slug);
            return FetchOutcome.AlreadyHave;
        }
        wanted ??= _folder.Wanted.Add(name, category, now);
        var sidecar = _folder.ReadSidecar(slug);
        // Everything turned down so far: what the wanted entry says, what earlier tries left in the sidecar (the entry
        // goes when a try succeeds), and, when another is asked for, the photo in play itself.
        var exclude = asked.Concat(sidecar?.Alternatives ?? []).Concat(inPlay && sidecar?.Photo?.FileTitle is { } shown ? [shown] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // A photo kept from an earlier visit, when the cut-out model was missing, is cut now rather than fetched again.
        if (!inPlay && sidecar is { Source: PictureSource.Photo, Photo: { } kept }
            && _folder.FindRaw(slug) is { } rawPath && !exclude.Contains(kept.FileTitle, StringComparer.OrdinalIgnoreCase) && _cutOut.IsAvailable)
        {
            using var raw = Decode(File.ReadAllBytes(rawPath));
            if (raw is not null) { Note($"{name}: cutting the photo kept earlier"); return Cut(sidecar, raw, now); }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        PhotoSearch search;
        try { search = await _finder.FindAsync(name, category, exclude, cts.Token); }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
        {
            Log.Warn("pictures", $"looking for a photo of {name} took longer than {Limit.TotalSeconds:0} s", e);
            return Offline(slug, name, "took too long", now);
        }
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
        catch (Exception e) when (PictureHttp.IsNetworkTrouble(e, ct))
        {
            Log.Warn("pictures", $"downloading the photo of {name} from {photo.DownloadUrl} failed", e);
            return Offline(slug, name, e.Message, now);
        }
        if (download is not { } got) return TurnedDown(slug, name, photo.Provenance.FileTitle, "could not be downloaded", now);
        using var bitmap = Decode(got.Bytes);
        if (bitmap is null) return TurnedDown(slug, name, photo.Provenance.FileTitle, "is not a picture we can read", now);

        var mime = got.Mime is { } m && m.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? m : photo.Provenance.Mime;
        var fresh = new PictureSidecar(slug, name, category, PictureSource.Photo, now)
        {
            Id = _folder.NextId(slug),
            Photo = photo.Provenance with { Mime = mime },
            Alternatives = exclude,
            CutOut = photo.Doubt is { } doubt ? $"taken with doubt: {doubt}" : null,
        };
        Note($"{name}: took {photo.Provenance.FileTitle} ({photo.Provenance.How}; {photo.Provenance.LicenseShortName})");
        // With a picture in play, its photo and story stay on disk until the new cut-out lands: a failed cut must
        // not leave the old picture credited to a photo nobody sees.
        if (inPlay) return Cut(fresh, bitmap, now, pending: (ExtensionFor(mime), got.Bytes));
        _folder.WriteRaw(slug, ExtensionFor(mime), got.Bytes);
        _folder.WriteSidecar(fresh);
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

    /// <param name="pending">The downloaded photo not yet written under <c>raw/</c>, when an earlier picture is in play:
    /// it and the sidecar are written only once the cut succeeds, and the old picture's files are left alone otherwise.</param>
    private FetchOutcome Cut(PictureSidecar sidecar, SKBitmap photo, DateTime now, (string Extension, byte[] Bytes)? pending = null)
    {
        if (!_cutOut.IsAvailable)
        {
            _folder.Wanted.Bump(sidecar.Slug, ModelMissing, now, countTry: false);
            Note(pending is null ? $"{sidecar.Name}: photo kept; {ModelMissing}" : $"{sidecar.Name}: the old picture stays; {ModelMissing}");
            return FetchOutcome.RawOnly;
        }
        var result = _cutOut.Cut(photo);
        if (result.Picture is not { } picture)
        {
            var why = result.Reason ?? "no matte";
            if (pending is null) _folder.WriteSidecar(sidecar with { CutOut = $"{result.Model} failed: {why}" });
            _folder.Wanted.Bump(sidecar.Slug, $"cut-out failed: {why}", now, exclude: sidecar.Photo?.FileTitle);
            Note($"{sidecar.Name}: cut-out by {result.Model} failed: {why}");
            return FetchOutcome.RawOnly;
        }
        using (picture)
        {
            var covers = ((int)Math.Round(result.Coverage * 100)).ToString(CultureInfo.InvariantCulture);
            if (pending is { } raw)
            {
                // The picture this one takes the place of is kept, for a grown-up to choose from or go back to.
                if (_folder.Keep(sidecar.Slug) is { } kept) Note($"{sidecar.Name}: the picture before this one is kept as number {kept}");
                _folder.WriteRaw(sidecar.Slug, raw.Extension, raw.Bytes);
            }
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

    /// <summary>A photo as the cut-out wants it: RGBA, unpremultiplied, and the right way up. A camera stores a photo
    /// as the sensor saw it and says in its EXIF how to turn it; Commons' thumbnails and sizes are already turned,
    /// but an original downloaded whole is not, so the turn is made here. Null when the bytes are not a picture.</summary>
    public static SKBitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
        var bitmap = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (bitmap is null || codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bitmap;
        using (bitmap) return Upright(bitmap, codec.EncodedOrigin);
    }

    /// <summary>The bitmap turned and flipped as its EXIF origin says, pixel by pixel; the four sideways origins
    /// swap width and height.</summary>
    public static SKBitmap Upright(SKBitmap stored, SKEncodedOrigin origin)
    {
        int w = stored.Width, h = stored.Height;
        var sideways = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var upright = new SKBitmap(new SKImageInfo(sideways ? h : w, sideways ? w : h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(stored.GetPixelSpan());
        var dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(upright.GetPixelSpan());
        int uw = upright.Width, uh = upright.Height, srcStride = stored.RowBytes / 4, dstStride = upright.RowBytes / 4;
        for (var y = 0; y < uh; y++)
            for (var x = 0; x < uw; x++)
            {
                // Where the pixel the upright picture shows at (x, y) sits in the stored one.
                var (sx, sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (w - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (w - 1 - x, h - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, h - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, h - 1 - x),
                    SKEncodedOrigin.RightBottom => (w - 1 - y, h - 1 - x),
                    SKEncodedOrigin.LeftBottom => (w - 1 - y, x),
                    _ => (x, y),
                };
                dst[y * dstStride + x] = src[sy * srcStride + sx];
            }
        return upright;
    }

    /// <summary>The file extension a photo is kept under: by its MIME type, JPEG when unsure.</summary>
    public static string ExtensionFor(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".jpg",
    };
}
