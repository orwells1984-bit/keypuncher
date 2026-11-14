using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>What Commons says about one file, as much as the vetting needs.</summary>
public sealed record FileFacts(
    string FileTitle,
    string Mime,
    string MediaType,
    int Width,
    int Height,
    IReadOnlyList<string> Categories,
    string LicenseShortName,
    string Copyrighted,
    string? FileUrl = null);

/// <summary>How strongly a file was turned down.</summary>
public enum Rejection
{
    /// <summary>Not a usable photo of anything: wrong kind of file, too small, a map, a skeleton.</summary>
    Hard,
    /// <summary>Probably a drawing or painting: another candidate is better, but this one will do if none is found.</summary>
    Soft,
}

/// <summary>
/// Decides whether a Commons file can be the picture of a real thing: a bitmap photo of a sensible size and shape, not a
/// map, diagram, skeleton, logo, stamp or part of the thing. The lists are heuristics over file names and categories;
/// turning down a good photo costs little (the thing keeps its stock shape, or the image model draws it), while a wrong
/// picture is the one failure that matters, so they err on the side of no.
/// </summary>
public static class PhotoRules
{
    public const int MinLongSide = 600;
    public const double MinAspect = 0.5, MaxAspect = 2.0;
    private static readonly HashSet<string> Mimes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    /// <summary>Words in a file name that mean the file is not a plain picture of the thing.</summary>
    private static readonly HashSet<string> BadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "map", "maps", "range", "distribution", "distrib", "locator", "diagram", "scheme", "logo", "icon", "flag", "emblem",
        "seal", "chart", "graph", "plot", "anatomy", "dissection", "section", "stamp", "coin", "banknote", "poster", "cover",
        "screenshot", "collage", "montage", "comparison", "silhouette", "outline", "drawing", "sketch", "engraving",
        "lithograph", "illustration", "plate", "herbarium", "egg", "eggs", "nest", "track", "tracks", "footprint", "scat",
        "pelt", "skin", "horn", "antler", "feather", "seed", "seeds", "pollen", "interior", "cockpit", "engine", "dashboard",
        "badge", "wheel", "blueprint", "cutaway", "skeleton", "skull", "fossil", "bones", "taxidermy", "mounted", "specimen",
    };

    /// <summary>Category name beginnings (lower case) that mean the same.</summary>
    private static readonly string[] BadCategories =
    [
        "taxiderm", "skeleton", "skull", "fossil", "bone", "mounted", "map", "diagram", "logo", "icon", "flag", "coat of arms",
        "chart", "anatomy", "herbari", "stamp", "coin", "screenshot", "collage", "silhouette", "cutaway", "blueprint",
        "scale model", "toy", "model of", "interior", "engine", "cockpit", "botanical illustration", "seeds of", "leaves of",
        "wood of", "bark of", "svg", "range map", "distribution map", "locator map", "size comparison",
    ];

    /// <summary>Words in a file name that mean the photo shows more than one of the thing, a mother with her young, the
    /// thing with people, or the thing cut open. These photos cut out as one blob of several animals (the "Flock of
    /// sheep", "Anas platyrhynchos (mixed pair)", "Eubalaena glacialis with calf" and "Kangaroo and joey" in play in
    /// November 2026) or as a montage ("Bird Diversity 2011", "Leaf Varieties (15 sp)"), and the checker, shown the
    /// cut-out, called most of them one animal.</summary>
    private static readonly HashSet<string> GroupWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "flock", "flocks", "herd", "herds", "pair", "pairs", "couple", "group", "groups", "pack", "school", "shoal", "swarm",
        "colony", "family", "families", "together", "mating", "mother", "mom", "calf", "calves", "joey", "foal", "foals",
        "cub", "cubs", "chicks", "ducklings", "goslings", "puppies", "kittens", "lambs", "litter", "young", "offspring",
        "diversity", "varieties", "variety", "assortment", "selection", "halved", "half", "halves", "sliced", "slices",
        "market", "vendor", "vendors", "seller", "sellers", "people", "woman", "women", "girl", "girls", "boy", "boys",
        "children", "child", "farmer", "farmers", "holding", "hand", "hands",
    };

    /// <summary>Category name beginnings that mean the same.</summary>
    private static readonly string[] GroupCategories =
    [
        "groups of", "flocks of", "herds of", "pairs of", "schools of", "shoals of", "mothers and", "animals with young",
        "people with", "women with", "men with", "children with", "markets", "street vendors", "fruit vendors",
    ];

    /// <summary>For an extinct animal there is no photo of the living thing: a life restoration is what we want, and the
    /// usual lead image (a skeleton, a mummified find) is what we do not.</summary>
    private static readonly string[] ExtinctHardWords = ["skeleton", "skull", "fossil", "bone", "bones", "mounted", "mummy", "mummified", "carcass", "permafrost", "specimen", "skeletal"];
    private static readonly string[] ExtinctGoodWords = ["restoration", "reconstruction", "paleoart", "palaeoart", "life"];

    /// <summary>Words that make a living thing's picture a drawing rather than a photo: a soft no.</summary>
    private static readonly string[] ArtWords = ["painting", "paintings", "drawing", "drawings", "illustration", "illustrations", "engraving", "engravings", "lithograph", "lithographs", "artwork", "artworks"];

    /// <summary>Null when the file will do; otherwise how firmly it is turned down and why.</summary>
    /// <param name="name">The thing's own name, which is never held against a file where it is spelt out whole: a
    /// seal is not a stamp, a hornbill horn is not a part of something. A name word on its own elsewhere in the
    /// file name still counts, so a map turtle's range map is still a map.</param>
    public static (Rejection Kind, string Why)? Vet(FileFacts facts, RealCategory category, string? name = null)
    {
        if (!Mimes.Contains(facts.Mime)) return (Rejection.Hard, $"{facts.Mime} is not a photo");
        if (!string.IsNullOrEmpty(facts.MediaType) && !facts.MediaType.Equals("BITMAP", StringComparison.OrdinalIgnoreCase)) return (Rejection.Hard, $"media type {facts.MediaType}");
        if (facts.FileUrl is { } url && url.Contains("/wikipedia/en/", StringComparison.OrdinalIgnoreCase)) return (Rejection.Hard, "a local Wikipedia upload, not Commons");
        if (Math.Max(facts.Width, facts.Height) < MinLongSide) return (Rejection.Hard, $"only {facts.Width}x{facts.Height}");
        if (facts.Height > 0 && (facts.Width / (double)facts.Height < MinAspect || facts.Width / (double)facts.Height > MaxAspect)) return (Rejection.Hard, $"shape {facts.Width}x{facts.Height}");
        if (string.IsNullOrWhiteSpace(facts.LicenseShortName) && facts.Copyrighted.Equals("True", StringComparison.OrdinalIgnoreCase)) return (Rejection.Hard, "no licence tag");

        var words = WithoutPhrase(Words(facts.FileTitle).ToList(), name is null ? [] : Words(name).ToList());
        var categories = facts.Categories.Select(c => c.StartsWith("Category:") ? c[9..] : c).Select(c => c.ToLowerInvariant()).ToList();

        if (Group(words, categories, name) is { } group) return (Rejection.Hard, group);

        if (category == RealCategory.ExtinctAnimal)
        {
            if (words.Any(w => ExtinctHardWords.Contains(w)) || categories.Any(c => ExtinctHardWords.Any(c.Contains)))
                return (Rejection.Hard, "remains, not the living animal");
            // A restoration is welcome here, so the drawing words below do not apply; the other bad words still do.
            if (words.Any(w => BadWords.Contains(w) && !ArtWords.Contains(w) && w is not ("plate" or "illustration")))
                return (Rejection.Hard, $"file name says {words.First(w => BadWords.Contains(w) && !ArtWords.Contains(w) && w is not ("plate" or "illustration"))}");
            if (categories.Any(c => BadCategories.Any(c.StartsWith) && !c.StartsWith("botanical illustration")))
                return (Rejection.Hard, $"in category {categories.First(c => BadCategories.Any(c.StartsWith))}");
            return null;
        }

        if (words.FirstOrDefault(w => BadWords.Contains(w)) is { } bad) return (Rejection.Hard, $"file name says {bad}");
        if (categories.FirstOrDefault(c => BadCategories.Any(c.StartsWith)) is { } badCategory) return (Rejection.Hard, $"in category {badCategory}");
        if (words.Any(w => ArtWords.Contains(w)) || categories.Any(c => ArtWords.Any(a => c.Contains(a)))) return (Rejection.Soft, "a drawing or painting rather than a photo");
        return null;
    }

    /// <summary>Why the file shows more than one of the thing (or the thing with people, or cut open), or null: a group
    /// word, a group category, or the name's own last word in the plural ("Mangos - single and halved", "Zebras in
    /// Etosha").</summary>
    private static string? Group(List<string> words, List<string> categories, string? name)
    {
        // A number run onto a word ("joey03") is the uploader's numbering.
        words = [.. words.Select(w => w.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')).Where(w => w.Length > 0)];
        if (words.FirstOrDefault(GroupWords.Contains) is { } word) return $"file name says {word}: more than one, or not on its own";
        if (categories.FirstOrDefault(c => GroupCategories.Any(c.StartsWith)) is { } category) return $"in category {category}: more than one, or not on its own";
        if (name is not null && Words(name).LastOrDefault() is { Length: >= 3 } last
            && words.FirstOrDefault(w => !w.Equals(last, StringComparison.OrdinalIgnoreCase) && OllamaVision.Singular(w) == last) is { } plural)
            return $"file name says {plural}: more than one";
        return null;
    }

    /// <summary>Whether, for an extinct animal, a file is the kind we hope for: a life restoration.</summary>
    public static bool IsRestoration(FileFacts facts) =>
        Words(facts.FileTitle).Any(w => ExtinctGoodWords.Contains(w)) || facts.Categories.Any(c => c.Contains("restoration", StringComparison.OrdinalIgnoreCase));

    /// <summary>The words less every run of them that spells the phrase, in order; a word of the phrase on its own stays.</summary>
    private static List<string> WithoutPhrase(List<string> words, List<string> phrase)
    {
        if (phrase.Count == 0 || phrase.Count > words.Count) return words;
        var kept = new List<string>(words.Count);
        for (var i = 0; i < words.Count;)
        {
            var at = i + phrase.Count <= words.Count && Enumerable.Range(0, phrase.Count).All(k => words[i + k].Equals(phrase[k], StringComparison.OrdinalIgnoreCase));
            if (at) i += phrase.Count;
            else kept.Add(words[i++]);
        }
        return kept;
    }

    /// <summary>The words of a file name, lower case, split on the separators Commons names use.</summary>
    public static IEnumerable<string> Words(string fileTitle)
    {
        var name = fileTitle.StartsWith("File:") ? fileTitle[5..] : fileTitle;
        return name.ToLowerInvariant().Split([' ', '_', '-', '.', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries);
    }
}
