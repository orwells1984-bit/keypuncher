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
/// the cut-out to the vision model, which takes a picture it fails out of play (see <see cref="PictureChecks"/>). Everything here takes the graphics
/// card, so it runs from the console tool at night, never from the toy.
/// </summary>
public sealed class PictureMaker(PictureFolder folder, IImageMaker maker, ICutOut cutOut, IVisionChecker? checker = null, Func<DateTime>? clock = null, Action<string>? note = null)
{
    private readonly PictureChecks? _checks = checker is null ? null : new PictureChecks(folder, checker, clock, note);

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
        // A hidden picture still on the wanted list is one the checker turned down: another is drawn.
        if (folder.HasPicture(slug) || (folder.IsHidden(slug) && folder.Wanted.Get(slug) is null)) { folder.Wanted.Remove(slug); return MakeOutcome.AlreadyHave; }
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

        var sidecar = new PictureSidecar(slug, name, category, PictureSource.Generated, now) { Id = folder.NextId(slug), Generated = image.Provenance };
        // A hidden picture this one is drawn in place of is kept to choose from, as a fetched photo keeps it. Its
        // files are left alone until the new cut-out is there, so a try that fails does not mix the two up.
        var replacing = folder.IsHidden(slug);
        if (!replacing)
        {
            folder.WriteRaw(slug, ".png", image.Png);
            folder.WriteSidecar(sidecar);
        }
        var result = cutOut.Cut(bitmap);
        if (result.Picture is not { } picture)
        {
            var why = result.Reason ?? "no matte";
            if (!replacing) folder.WriteSidecar(sidecar with { CutOut = $"{result.Model} failed: {why}" });
            folder.Wanted.Bump(slug, $"cut-out failed: {why}", now);
            Note($"{name}: cut-out by {result.Model} failed: {why}");
            return MakeOutcome.CutOutFailed;
        }
        if (replacing)
        {
            if (folder.Keep(slug) is { } kept) Note($"{name}: the picture before this one is kept as number {kept}");
            folder.WriteRaw(slug, ".png", image.Png);
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
        if (_checks is not null) await _checks.CheckAsync(sidecar, cutPng, ct);
        return MakeOutcome.Made;
    }

    /// <summary>Shows a picture (in play or hidden) to the checker and acts on its verdict (see <see cref="PictureChecks"/>);
    /// null when there is no checker, no such picture, or the checker did not answer.</summary>
    public Task<VisionCheck?> CheckAsync(string slug, CancellationToken ct = default) =>
        _checks?.CheckAsync(slug, ct) ?? Task.FromResult<VisionCheck?>(null);

    private void Note(string line) => note?.Invoke(line);
}
