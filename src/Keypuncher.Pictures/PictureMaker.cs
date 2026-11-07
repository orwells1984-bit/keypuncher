using System.Globalization;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>What making a picture came to.</summary>
public enum MakeOutcome
{
    /// <summary>A cut-out of the made picture is in play.</summary>
    Made,
    /// <summary>A picture or a photo is there already.</summary>
    AlreadyHave,
    /// <summary>The model made a picture but the cut-out would not take it; another seed is tried next time.</summary>
    CutOutFailed,
    /// <summary>The image model or the cut-out model is not on disk.</summary>
    ModelMissing,
    /// <summary>The model ran and failed (out of memory, a crash); not counted as a try.</summary>
    Failed,
}

/// <summary>
/// Fills the gaps the photo sources leave: for a thing the finder found nothing for, asks the image model for a
/// picture, cuts it out like a photo, writes the sidecar with how it was made, and, when a checker is given, shows
/// the cut-out to the vision model and records what it said. The check only flags. Everything here takes the graphics
/// card, so it runs from the console tool at night, never from the toy.
/// </summary>
public sealed class PictureMaker(PictureFolder folder, IImageMaker maker, ICutOut cutOut, IVisionChecker? checker = null, Func<DateTime>? clock = null, Action<string>? note = null)
{
    /// <summary>The wanted reasons that mean the photo sources are done with a thing, so the image model may try.</summary>
    public static readonly string[] GapReasons = ["no photo found", "no article"];

    /// <summary>Whether a wanted thing is one the image model should draw: the photo sources gave up on it, or it
    /// has been tried at least three times for any reason but the network.</summary>
    public static bool IsGap(WantedEntry wanted) =>
        !ToyNames.IsInvented(wanted.Name)
        && (GapReasons.Contains(wanted.Reason ?? "", StringComparer.OrdinalIgnoreCase)
            || (wanted.Tries >= 3 && wanted.Reason is not ("offline" or PhotoFetcher.ModelMissing)));

    /// <summary>Makes a picture for a thing with none. The seed is the number of tries so far unless given, so the
    /// next try after a failed cut-out is a different picture.</summary>
    public async Task<MakeOutcome> MakeAsync(string name, RealCategory category, long? seed = null, CancellationToken ct = default)
    {
        var slug = Slugs.For(name);
        var now = (clock ?? (() => DateTime.Now))();
        if (folder.HasPicture(slug) || folder.IsHidden(slug)) { folder.Wanted.Remove(slug); return MakeOutcome.AlreadyHave; }
        var wanted = folder.Wanted.Get(slug) ?? folder.Wanted.Add(name, category, now);
        if (!maker.IsAvailable) { Note($"{name}: image model missing"); return MakeOutcome.ModelMissing; }
        if (!cutOut.IsAvailable) { folder.Wanted.Bump(slug, PhotoFetcher.ModelMissing, now, countTry: false); Note($"{name}: {PhotoFetcher.ModelMissing}"); return MakeOutcome.ModelMissing; }

        var (real, realCategory) = ToyNames.LookUp(name, category);
        var prompt = GenerationPrompt.For(real, realCategory);
        var useSeed = seed ?? wanted.Tries + 1;
        Note($"{name}: asking {maker.Name} (seed {useSeed})");
        var (image, reason) = await maker.MakeAsync(prompt, useSeed, ct);
        if (image is null)
        {
            Note($"{name}: {reason ?? "the model made nothing"}");
            return MakeOutcome.Failed;
        }
        using var bitmap = PhotoFetcher.Decode(image.Png);
        if (bitmap is null) { Note($"{name}: the model's picture could not be read"); return MakeOutcome.Failed; }

        var sidecar = new PictureSidecar(slug, name, category, PictureSource.Generated, now) { Generated = image.Provenance };
        folder.WriteRaw(slug, ".png", image.Png);
        folder.WriteSidecar(sidecar);
        var result = cutOut.Cut(bitmap);
        if (result.Picture is not { } picture)
        {
            var why = result.Reason ?? "no matte";
            folder.WriteSidecar(sidecar with { CutOut = $"{result.Model} failed: {why}" });
            folder.Wanted.Bump(slug, $"cut-out failed: {why}", now);
            Note($"{name}: cut-out by {result.Model} failed: {why}");
            return MakeOutcome.CutOutFailed;
        }
        byte[] cutPng;
        using (picture)
        {
            var covers = ((int)Math.Round(result.Coverage * 100)).ToString(CultureInfo.InvariantCulture);
            sidecar = sidecar with { Hidden = false, CutOut = $"{result.Model}, covers {covers}% of the picture" };
            folder.WriteSidecar(sidecar);
            folder.WritePicture(slug, picture);
            using var encoded = picture.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            cutPng = encoded.ToArray();
        }
        folder.Wanted.Remove(slug);
        Note($"{name}: picture in play ({maker.Name}, {result.Model})");
        if (checker is not null) await CheckAsync(sidecar, cutPng, ct);
        return MakeOutcome.Made;
    }

    /// <summary>Shows a picture already in play to the checker and records its verdict; null when there is no checker,
    /// no such picture, or the checker did not answer.</summary>
    public async Task<VisionCheck?> CheckAsync(string slug, CancellationToken ct = default)
    {
        if (checker is null || folder.ReadSidecar(slug) is not { } sidecar) return null;
        var path = folder.HasPicture(slug) ? folder.PicturePath(slug) : folder.IsHidden(slug) ? folder.HiddenPath(slug) : null;
        if (path is null) return null;
        return await CheckAsync(sidecar, await File.ReadAllBytesAsync(path, ct), ct);
    }

    private async Task<VisionCheck?> CheckAsync(PictureSidecar sidecar, byte[] png, CancellationToken ct)
    {
        if (checker is null) return null;
        VisionCheck? check;
        try { check = await checker.CheckAsync(png, sidecar.Name, sidecar.Category, ct); }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            Note($"{sidecar.Name}: the checker did not answer ({e.Message})");
            return null;
        }
        if (check is null) { Note($"{sidecar.Name}: the checker did not answer"); return null; }
        folder.WriteSidecar((folder.ReadSidecar(sidecar.Slug) ?? sidecar) with { Check = check });
        Note($"{sidecar.Name}: {checker.Model} says {(check.Passed ? "looks right" : "looks WRONG")} ({check.Confidence:0.00}): {check.Saw}");
        return check;
    }

    private void Note(string line) => note?.Invoke(line);
}
