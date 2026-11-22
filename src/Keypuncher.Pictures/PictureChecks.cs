using Keypuncher.Core;
using Keypuncher.Director;

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
    /// <summary>The "model" recorded on a check the cut-out floor made without asking the checker.</summary>
    public const string CutOutRule = "cut-out rule";
    /// <summary>The "model" recorded on a check failed because another thing in play has the same photo.</summary>
    public const string SharedPhotoRule = "same-photo rule";

    /// <summary>The rules checks are made under now. Raised when the questions or the reading of the answers change
    /// enough that old verdicts should not stand: 1 (November 2026) stopped telling the model the picture "should show
    /// exactly one" and failed "model" and cloth descriptions; before it, a school of fish and a flock of sheep had passed.
    /// 2 (later that month) fails a counted heap ("50-60 mangoes", "approximately 20 tulips") and things in bowls.
    /// 3 (2026-11-20) fails a single bone, a labelled anatomy picture and a second animal "alongside", which had passed
    /// (a parasaurolophus crest bone, the points of a horse), and no longer fails "close-up" unless it is of a part.
    /// 4 (2026-11-22) fails a second animal "next to a" or "perched on" the first (a thylacine on a dingo), hand-made
    /// pictures of living things (illustration, painting, engraving), a plant "in various stages", and a photo another
    /// thing in play already has (the coconut and the coconut palm shared one).</summary>
    public const int Rules = 4;

    public string Model => checker.Model;

    /// <summary>The pictures the checker has not seen yet, or saw under older rules: first those held out of play
    /// until it has (<see cref="PictureSidecar.AwaitsCheck"/>), then what a play has just fetched, then what the old
    /// rules let through; newest first within each.</summary>
    public IReadOnlyList<PictureSidecar> Unchecked() =>
        [.. folder.All().Where(p => Stale(p.Check) && (folder.HasPicture(p.Slug) || (p.AwaitsCheck && folder.IsHidden(p.Slug))))
            .OrderBy(p => p.AwaitsCheck ? 0 : p.Check is null ? 1 : 2)];

    /// <summary>Whether a verdict is missing or was made under older rules.</summary>
    public static bool Stale(VisionCheck? check) => check is null || check.Rules < Rules;

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
        if (SharedWith(sidecar) is { } owner)
        {
            // Two things cannot both be this photo; the one whose name it fits better keeps it, and this one gets another.
            check = new VisionCheck(SharedPhotoRule, false, 0, $"the same photo as {owner.Name} ({sidecar.Photo!.FileTitle}) [another thing's photo]", now);
        }
        else if (CutOutChain.ZoomedTooLittle(sidecar.CutOut))
        {
            // Cut out before the floor on closer looks: what would not be cut out today needs no model to turn it down.
            check = new VisionCheck(CutOutRule, false, 0, $"a closer look kept too little of the crop ({sidecar.CutOut}) [a part or another thing]", now);
        }
        else
        {
            try { check = await checker.CheckAsync(png, real, realCategory, ct); }
            catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                Note($"{sidecar.Name}: the checker did not answer ({e.Message})");
                Log.Warn("pictures", $"the picture checker did not answer for {sidecar.Name}", e);
                return null;
            }
            if (check is null) { Note($"{sidecar.Name}: the checker did not answer"); return null; }
        }
        check = check with { Rules = Rules };
        var current = folder.ReadSidecar(sidecar.Slug) ?? sidecar;
        if (current.Id != sidecar.Id)
        {
            // Another picture took its place while the checker was looking (a fetch landed, a grown-up added their
            // own): the verdict is about a picture that is no longer the one in play, and must not hide the new one.
            Note($"{sidecar.Name}: picture {sidecar.Id} was replaced by picture {current.Id} while it was being checked; the verdict is dropped");
            return null;
        }
        folder.WriteSidecar(current with { Check = check, AwaitsCheck = false });
        if (check.Passed)
        {
            if (current.AwaitsCheck) folder.Release(sidecar.Slug);
            Note($"{sidecar.Name}: {check.Model} says it looks right: {check.Saw}{(current.AwaitsCheck ? "; it goes into play" : "")}");
            return check;
        }
        if (current.Source == PictureSource.Hand || current.ShownByGrownUp || folder.Cards.ChoseByHand(sidecar.Slug, current.Id))
        {
            // A grown-up looked at this picture and chose it, or put it back; the checker's doubt is shown to them, and that is all.
            Note($"{sidecar.Name}: {check.Model} says it looks WRONG: {check.Saw}; a grown-up chose this picture, so it stays in play");
            return check;
        }
        folder.Hide(sidecar.Slug);
        folder.Wanted.Add(sidecar.Name, sidecar.Category, now);
        folder.Wanted.Bump(sidecar.Slug, FailedCheck, now, exclude: sidecar.Photo?.FileTitle);
        Note($"{sidecar.Name}: {check.Model} says it looks WRONG: {check.Saw}; taken out of play, another picture is wanted");
        return check;
    }

    /// <summary>The other thing in play (or held for the check) whose picture comes from the same photo and has the
    /// better claim to it, or null. The better claim is the name the file title spells out more of ("Coconut 4.jpg" is
    /// the coconut's, not the coconut palm's), then the picture made first.</summary>
    public PictureSidecar? SharedWith(PictureSidecar sidecar)
    {
        if (sidecar.Photo?.FileTitle is not { Length: > 0 } title) return null;
        var rivals = folder.All().Where(p => p.Slug != sidecar.Slug
                && string.Equals(p.Photo?.FileTitle, title, StringComparison.OrdinalIgnoreCase)
                && (folder.HasPicture(p.Slug) || (p.AwaitsCheck && folder.IsHidden(p.Slug))))
            .ToList();
        if (rivals.Count == 0) return null;
        var mine = Claim(sidecar.Name, title);
        return rivals.Where(r => Claim(r.Name, title) is var theirs && (theirs > mine || (theirs == mine && r.MadeAt < sidecar.MadeAt)))
            .OrderByDescending(r => Claim(r.Name, title)).ThenBy(r => r.MadeAt).FirstOrDefault();
    }

    /// <summary>How well a file title fits a name: the share of the name's words (singular) it spells out.</summary>
    public static double Claim(string name, string fileTitle)
    {
        var words = PhotoRules.Words(fileTitle).Select(OllamaVision.Singular).ToHashSet();
        var own = PhotoRules.Words(name).Select(OllamaVision.Singular).ToList();
        if (own.Count == 0) return 0;
        var found = own.Count(words.Contains);
        return found / (double)own.Count;
    }

    private void Note(string line) => note?.Invoke(line);
}
