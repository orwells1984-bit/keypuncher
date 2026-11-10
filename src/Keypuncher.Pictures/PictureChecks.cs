using Keypuncher.Core;
using Keypuncher.Director;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>
/// Shows pictures in play to the vision model and acts on what it says. A picture it passes stays. A picture it
/// fails (a school of fish for "fish", a mare and foal for "horse", the plaid cloth from a photo of coconut sellers
/// for "coconut") is taken out of play at once, so the toy draws its own shape again, and the thing goes back on
/// the wanted list with that photo turned down, so the next fetch brings another photo. A grown-up can put a
/// wrongly failed picture back from the grown-ups' page; that also stops the search for another. A picture a
/// grown-up chose for one of the thing's rungs is never taken out: its verdict is recorded and shown, no more.
/// </summary>
/// <remarks>Until November 2026 a failed check only flagged. On the family desktop gemma3 flagged about 30 of 158
/// pictures, two of them wrongly, and the toy went on showing the 28 bad ones. A wrong flag costs one good photo,
/// which the next fetch usually replaces with another good one; a missed flag shows a baby a heap of animals as one
/// thing. Ollama holds one model on the card, so this never runs while the director is choosing: the console tool
/// runs it at night, and the toy after its goodnight.</remarks>
public sealed class PictureChecks(PictureFolder folder, IVisionChecker checker, Func<DateTime>? clock = null, Action<string>? note = null)
{
    /// <summary>The wanted reason for a thing whose picture the checker turned down.</summary>
    public const string FailedCheck = "failed check";
    /// <summary>The photo is shown to the checker at most this big on its long side: the model sees less anyway, and an
    /// 8000-pixel original would be tens of megabytes of base64.</summary>
    public const int MaxPhotoSide = 1024;
    /// <summary>The "model" recorded on a check the cut-out floor made without asking the checker.</summary>
    public const string CutOutRule = "cut-out rule";

    public string Model => checker.Model;

    /// <summary>The pictures in play the checker has not seen yet, newest first: what a play has just fetched.</summary>
    public IReadOnlyList<PictureSidecar> Unchecked() =>
        [.. folder.All().Where(p => p.Check is null && folder.HasPicture(p.Slug))];

    /// <summary>Shows a picture (in play or hidden) to the checker, records its verdict and, on a fail, takes it out of
    /// play and asks for another. Null when there is no such picture or the checker did not answer.</summary>
    public async Task<VisionCheck?> CheckAsync(string slug, CancellationToken ct = default)
    {
        if (folder.ReadSidecar(slug) is not { } sidecar) return null;
        var path = folder.HasPicture(slug) ? folder.PicturePath(slug) : folder.IsHidden(slug) ? folder.HiddenPath(slug) : null;
        if (path is null) return null;
        return await CheckAsync(sidecar, await File.ReadAllBytesAsync(path, ct), ct);
    }

    /// <summary>As <see cref="CheckAsync(string, CancellationToken)"/>, for a picture just written whose PNG is at hand.</summary>
    public async Task<VisionCheck?> CheckAsync(PictureSidecar sidecar, byte[] png, CancellationToken ct = default)
    {
        var (real, realCategory) = ToyNames.LookUp(sidecar.Name, folder.Cards.Get(sidecar.Slug)?.Category ?? sidecar.Category);
        var now = (clock ?? (() => DateTime.Now))();
        VisionCheck? check;
        if (CutOutChain.ZoomedTooLittle(sidecar.CutOut))
        {
            // Cut out before the floor on closer looks: what would not be cut out today needs no model to turn it down.
            check = new VisionCheck(CutOutRule, false, 0, $"a closer look kept too little of the crop ({sidecar.CutOut}) [a part or another thing]", now);
        }
        else
        {
            try { check = await checker.CheckAsync(png, real, realCategory, Photo(sidecar.Slug), ct); }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                Note($"{sidecar.Name}: the checker did not answer ({e.Message})");
                Log.Warn("pictures", $"the picture checker did not answer for {sidecar.Name}", e);
                return null;
            }
            if (check is null) { Note($"{sidecar.Name}: the checker did not answer"); return null; }
        }
        var current = folder.ReadSidecar(sidecar.Slug) ?? sidecar;
        folder.WriteSidecar(current with { Check = check });
        if (check.Passed)
        {
            Note($"{sidecar.Name}: {check.Model} says it looks right: {check.Saw}");
            return check;
        }
        if (folder.Cards.ChoseByHand(sidecar.Slug, current.Id))
        {
            // A grown-up looked at this picture and chose it; the checker's doubt is shown to them, and that is all.
            Note($"{sidecar.Name}: {check.Model} says it looks WRONG: {check.Saw}; a grown-up chose this picture, so it stays in play");
            return check;
        }
        folder.Hide(sidecar.Slug);
        folder.Wanted.Add(sidecar.Name, sidecar.Category, now);
        folder.Wanted.Bump(sidecar.Slug, FailedCheck, now, exclude: sidecar.Photo?.FileTitle);
        Note($"{sidecar.Name}: {check.Model} says it looks WRONG: {check.Saw}; taken out of play, another picture is wanted");
        return check;
    }

    /// <summary>The photo (or made image) the cut-out came from, as a JPEG no bigger than <see cref="MaxPhotoSide"/>;
    /// null when there is none or it cannot be read, and the check then goes without the comparison.</summary>
    private byte[]? Photo(string slug)
    {
        try
        {
            if (folder.FindRaw(slug) is not { } path) return null;
            using var photo = PhotoFetcher.Decode(File.ReadAllBytes(path));
            if (photo is null) return null;
            var scale = Math.Min(1.0, MaxPhotoSide / (double)Math.Max(photo.Width, photo.Height));
            var info = new SKImageInfo(Math.Max(1, (int)Math.Round(photo.Width * scale)), Math.Max(1, (int)Math.Round(photo.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var small = scale < 1 ? photo.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell)) : photo.Copy();
            if (small is null) return null;
            using var data = small.Encode(SKEncodedImageFormat.Jpeg, 90);
            return data?.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"could not read the photo of {slug} to compare", e);
            return null;
        }
    }

    private void Note(string line) => note?.Invoke(line);
}
