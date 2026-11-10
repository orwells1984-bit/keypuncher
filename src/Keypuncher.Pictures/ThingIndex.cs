using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>Everything the pictures folder knows about one thing, for the grown-ups' page: its picture in play (or
/// hidden), the pictures kept from before, why it is wanted if it is, and what the grown-ups chose for it.</summary>
/// <param name="Picture">The story of the thing's current picture, in play or hidden; null when it has none.</param>
/// <param name="InPlay">Whether that picture is in play (not hidden, and cut out).</param>
/// <param name="Kept">The pictures another took the place of, by number.</param>
/// <param name="Starter">Whether it is one of the toy's own starter things, which have words whether or not they have a picture.</param>
public sealed record ThingEntry(string Slug, string Name, PictureSidecar? Picture, bool InPlay, IReadOnlyList<PictureSidecar> Kept,
    WantedEntry? Wanted, ThingCard? Card, bool Starter)
{
    /// <summary>The thing's kind: as a grown-up set it, else as its picture or its wanted entry has it.</summary>
    public RealCategory? Category => Card?.Category ?? Picture?.Category ?? Kept.FirstOrDefault()?.Category ?? Wanted?.Category;

    /// <summary>Whether the picture is hidden: there is one, and it is out of play.</summary>
    public bool Hidden => Picture is not null && !InPlay && Picture.Hidden;

    /// <summary>Every picture a grown-up can choose from: the one in play, then the kept ones, by number.</summary>
    public IReadOnlyList<PictureSidecar> Choosable => [.. (InPlay && Picture is not null ? new[] { Picture } : []).Concat(Kept).OrderBy(p => p.Id)];

    /// <summary>When the thing last got a picture or was first wanted, for putting the newest first; null for a
    /// starter thing nothing has happened to.</summary>
    public DateTime? Touched => Picture?.MadeAt ?? Wanted?.Since ?? Card?.UpdatedAt;

    /// <summary>Whether a grown-up should look at it: its picture failed the check and is still the one drawn, it
    /// is hidden, or it is wanted and has no picture in play.</summary>
    public bool NeedsALook =>
        (InPlay && Picture?.Check is { Passed: false }) || Hidden || (Wanted is not null && !InPlay);
}

/// <summary>Which things the grown-ups' page lists.</summary>
public enum ThingFilter
{
    All,
    NeedsALook,
    Starter,
}

/// <summary>The things the grown-ups' page lists, put together from the pictures folder: every thing with a
/// picture, a kept picture, a wanted entry or a card, and the toy's starter things. Reads the folder each time.</summary>
public static class ThingIndex
{
    /// <param name="starters">The names of the toy's own starter things ("sun", "cat"), listed even when the folder has nothing on them.</param>
    public static IReadOnlyList<ThingEntry> Build(PictureFolder folder, IEnumerable<string> starters)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var pictures = folder.All().ToDictionary(p => p.Slug);
        var wanted = folder.Wanted.All().ToDictionary(w => w.Slug);
        var cards = folder.Cards.All().ToDictionary(c => c.Slug);
        var starterSlugs = new HashSet<string>(StringComparer.Ordinal);
        // The picture's own name first, as it is the one the director gave.
        foreach (var p in pictures.Values) names.TryAdd(p.Slug, p.Name);
        foreach (var w in wanted.Values) names.TryAdd(w.Slug, w.Name);
        foreach (var c in cards.Values) names.TryAdd(c.Slug, c.Name);
        foreach (var starter in starters)
        {
            var slug = Slugs.For(starter);
            if (slug.Length == 0) continue;
            starterSlugs.Add(slug);
            names.TryAdd(slug, starter);
        }
        foreach (var slug in folder.KeptSlugs()) names.TryAdd(slug, folder.Kept(slug).FirstOrDefault()?.Name ?? slug);

        return [.. names.Select(n => new ThingEntry(n.Key, n.Value, pictures.GetValueOrDefault(n.Key), folder.HasPicture(n.Key), folder.Kept(n.Key),
                wanted.GetValueOrDefault(n.Key), cards.GetValueOrDefault(n.Key), starterSlugs.Contains(n.Key)))
            .OrderByDescending(t => t.Touched ?? DateTime.MinValue).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The things a filter and a search leave, in the order given. The search matches any part of the
    /// name or of a word a grown-up wrote for the thing, whatever the case.</summary>
    public static IReadOnlyList<ThingEntry> Select(IEnumerable<ThingEntry> things, ThingFilter filter, string? search)
    {
        var text = search?.Trim() ?? "";
        return [.. things.Where(t => filter switch
            {
                ThingFilter.NeedsALook => t.NeedsALook,
                ThingFilter.Starter => t.Starter,
                _ => true,
            })
            .Where(t => text.Length == 0 || t.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || (t.Card?.Words.Values.Any(w => w.Contains(text, StringComparison.CurrentCultureIgnoreCase)) ?? false))];
    }
}
