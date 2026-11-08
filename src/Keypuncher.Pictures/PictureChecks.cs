using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>
/// Shows pictures in play to the vision model and acts on what it says. A picture it passes stays. A picture it
/// fails (a school of fish for "fish", a mare and foal for "horse", the plaid cloth from a photo of coconut sellers
/// for "coconut") is taken out of play at once, so the toy draws its own shape again, and the thing goes back on
/// the wanted list with that photo turned down, so the next fetch brings another photo. A grown-up can put a
/// wrongly failed picture back from the grown-ups' page; that also stops the search for another.
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
        var (real, realCategory) = ToyNames.LookUp(sidecar.Name, sidecar.Category);
        VisionCheck? check;
        try { check = await checker.CheckAsync(png, real, realCategory, ct); }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            Note($"{sidecar.Name}: the checker did not answer ({e.Message})");
            Log.Warn("pictures", $"the picture checker did not answer for {sidecar.Name}", e);
            return null;
        }
        if (check is null) { Note($"{sidecar.Name}: the checker did not answer"); return null; }
        var now = (clock ?? (() => DateTime.Now))();
        folder.WriteSidecar((folder.ReadSidecar(sidecar.Slug) ?? sidecar) with { Check = check });
        if (check.Passed)
        {
            Note($"{sidecar.Name}: {checker.Model} says it looks right: {check.Saw}");
            return check;
        }
        folder.Hide(sidecar.Slug);
        folder.Wanted.Add(sidecar.Name, sidecar.Category, now);
        folder.Wanted.Bump(sidecar.Slug, FailedCheck, now, exclude: sidecar.Photo?.FileTitle);
        Note($"{sidecar.Name}: {checker.Model} says it looks WRONG: {check.Saw}; taken out of play, another picture is wanted");
        return check;
    }

    private void Note(string line) => note?.Invoke(line);
}
