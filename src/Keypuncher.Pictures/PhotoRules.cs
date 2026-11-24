using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>What Commons says about one file, as much as the vetting needs.</summary>
/// <param name="Made">When the picture was made, as Commons words it ("2011-05-03 12:22", "between 1577 and 1580",
/// "13th century"); null when it does not say.</param>
public sealed record FileFacts(
    string FileTitle,
    string Mime,
    string MediaType,
    int Width,
    int Height,
    IReadOnlyList<string> Categories,
    string LicenseShortName,
    string Copyrighted,
    string? FileUrl = null,
    string? Made = null);

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
public static partial class PhotoRules
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
        // "Parasaurolophus holotype skulls" and "Points of a horse" (a labelled diagram) were taken on 2026-11-15.
        "skeletons", "skulls", "fossils", "holotype", "paratype", "points", "parts", "labelled", "labeled", "annotated", "schematic",
        // A page of an old book is not the animal, even an extinct one: "Der naturen bloeme - Jacob van Maerlant - KB KA 16
        // - 110v.jpg", a 13th-century manuscript, went into play as Steller's sea cow on 2026-11-22.
        "manuscript", "manuscripts", "codex", "folio", "bestiary", "psalter", "illuminated", "incunabulum", "page",
    };

    /// <summary>Category name beginnings (lower case) that mean the same.</summary>
    private static readonly string[] BadCategories =
    [
        "taxiderm", "skeleton", "skull", "fossil", "bone", "mounted", "map", "diagram", "logo", "icon", "flag", "coat of arms",
        "chart", "anatomy", "herbari", "stamp", "coin", "screenshot", "collage", "silhouette", "cutaway", "blueprint",
        "scale model", "toy", "model of", "interior", "engine", "cockpit", "botanical illustration", "seeds of", "leaves of",
        "wood of", "bark of", "svg", "range map", "distribution map", "locator map", "size comparison",
        "manuscript", "medieval manuscript", "illuminated manuscript", "bestiar", "codex", "codices", "incunab", "der naturen bloeme",
        "books of hours", "pages of",
    ];

    /// <summary>Words in a category name (lower case), wherever they stand, that mean the file is a page of an old book:
    /// "Der naturen bloeme - KB KA 16", "Visboek by Adriaen Coenen", "Medieval manuscripts".
    /// Both pages that went into play as Steller's sea cow were tagged on Commons as depicting it; neither book can
    /// show an animal first described in 1741.</summary>
    private static readonly string[] BookCategories =
    [
        "manuscript", "bestiar", "codex", "codices", "incunab", "illuminat", "visboek", "visboeck", "der naturen bloeme",
        "book of hours", "books of hours", "folios",
    ];

    /// <summary>A picture said to be made before this year is a page or a painting from before anyone drew animals from
    /// life for a book of nature; the dodo's portraits (1620s) and everything since are let through.</summary>
    public const int OldestYear = 1600;

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
    private static readonly string[] ExtinctHardWords = ["skeleton", "skull", "fossil", "bone", "bones", "mounted", "mummy", "mummified", "carcass", "permafrost", "specimen", "skeletal", "skeletons", "skulls", "fossils", "holotype", "paratype"];
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

        var words = WithoutName(Words(facts.FileTitle).ToList(), name is null ? [] : Words(name).ToList());
        var categories = facts.Categories.Select(c => c.StartsWith("Category:") ? c[9..] : c).Select(c => c.ToLowerInvariant()).ToList();

        // Remains are never the animal: a living one's skull is no more its picture than a dinosaur's is. Categories
        // are read word by word, not just by their start, because Commons names them "Phoca vitulina skulls" as often
        // as "Skulls of ...". And a museum's catalogue number marks a specimen in a drawer.
        if (category is RealCategory.Animal or RealCategory.ExtinctAnimal)
        {
            if (words.FirstOrDefault(w => ExtinctHardWords.Contains(w)) is { } remains) return (Rejection.Hard, $"file name says {remains}: remains, not the living animal");
            if (categories.FirstOrDefault(c => Words(c).Any(w => ExtinctHardWords.Contains(w)) || c.Contains("taxiderm")) is { } remainsCategory)
                return (Rejection.Hard, $"in category {remainsCategory}: remains, not the living animal");
            if (MuseumNumber(facts.FileTitle) is { } number) return (Rejection.Hard, $"file name says {number}: a museum specimen");
            if (category == RealCategory.Animal && categories.FirstOrDefault(c => Words(c).Any(w => w is "museum" or "museums" or "specimens")) is { } museum)
                return (Rejection.Hard, $"in category {museum}: a museum specimen");
        }

        if (Group(words, categories, name) is { } group) return (Rejection.Hard, group);
        // "110v", "12r": a manuscript leaf, recto or verso.
        if (words.FirstOrDefault(IsFolio) is { } leaf) return (Rejection.Hard, $"file name says {leaf}: a page of a book");
        if (ShelfMark(facts.FileTitle) is { } mark) return (Rejection.Hard, $"file name says {mark}: a library's scan of a book");
        if (categories.FirstOrDefault(c => BookCategories.Any(c.Contains)) is { } book) return (Rejection.Hard, $"in category {book}: a page of an old book");
        if (YearMade(facts.Made) is { } year && year < OldestYear) return (Rejection.Hard, $"made around {year}: an old book or painting");

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

    /// <summary>Whether a word numbers a manuscript leaf: digits and then r or v ("110v").</summary>
    public static bool IsFolio(string word) =>
        word.Length >= 2 && word[^1] is 'r' or 'v' && word[..^1].All(char.IsAsciiDigit);

    /// <summary>A library's shelf mark in a file name, or null: the library's letters and then a number ("KB 78 E 54",
    /// "KB KA 16", "BL Royal 12 C", "BnF Latin 3630"), or "MS 1234", "Cod. 12", "fol. 12". Case matters: "kb" in a
    /// camera's file name is not a library, and "MS Estonia" is a ship.</summary>
    public static string? ShelfMark(string fileTitle) =>
        ShelfMarks().Match(fileTitle) is { Success: true } m ? m.Value.Trim() : null;

    /// <summary>The year a picture was made, from Commons' wording of it: a century ("13th century" is 1200) or the
    /// first year from 1000 on ("between 1577 and 1580", "2011-05-03"); null when there is neither.</summary>
    public static int? YearMade(string? made)
    {
        if (string.IsNullOrWhiteSpace(made)) return null;
        if (Century().Match(made) is { Success: true } c && int.TryParse(c.Groups[1].Value, out var nth) && nth is >= 1 and <= 21) return (nth - 1) * 100;
        return Year().Match(made) is { Success: true } y && int.TryParse(y.Value, out var year) ? year : null;
    }

    /// <summary>Why a photo already in the folder would not be taken today, going by its file name alone (what the
    /// sidecar keeps), or null: the group, book-page and bad-word rules of <see cref="Vet"/>, never size or licence.
    /// A soft doubt (a painting) is not a reason: the checker has seen the picture.</summary>
    public static string? NameFault(string fileTitle, RealCategory category, string? name = null) =>
        Vet(new FileFacts(fileTitle, "image/jpeg", "", MinLongSide, MinLongSide, [], "kept", ""), category, name) is var (kind, why) && kind == Rejection.Hard ? why : null;

    /// <summary>Whether, for an extinct animal, a file is the kind we hope for: a life restoration.</summary>
    public static bool IsRestoration(FileFacts facts) =>
        Words(facts.FileTitle).Any(w => ExtinctGoodWords.Contains(w)) || facts.Categories.Any(c => c.Contains("restoration", StringComparison.OrdinalIgnoreCase));

    /// <summary>The words less the thing's own name. Where the name is spelt out whole, only that run goes, and a word
    /// of it elsewhere still counts ("Northern map turtle map.png" is a map, "Fire engine engine.jpg" an engine). Where it
    /// is not, the file names the thing another way ("Common Seal Phoca vitulina.jpg" for a harbour seal), and the
    /// name's last word, the kind of thing it is, is never held against it: on 2026-11-24 "seal" (a stamp) turned down
    /// every photo of a harbour seal but one, a museum skull, which went into play.</summary>
    private static List<string> WithoutName(List<string> words, List<string> name)
    {
        var kept = WithoutPhrase(words, name);
        if (name.Count == 0 || kept.Count != words.Count) return kept;
        var head = name[^1];
        return [.. kept.Where(w => !w.Equals(head, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>A natural history museum's catalogue number in a file name ("MWNH 1464", "NHMUK 1881.5.4", "USNM 49440"),
    /// or null. Only known collections' letters count, since a camera's "IMG 1234" or "DSC 0042" looks the same.</summary>
    public static string? MuseumNumber(string fileTitle) => MuseumNumbers().Match(fileTitle) is { Success: true } m ? m.Value : null;

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:MWNH|NHMUK|BMNH|MNHN|USNM|AMNH|NMNH|FMNH|MCZ|ZMB|ZMUC|NHMW|RMNH|SMF|ZSM|SMNS|ZFMK|NHMD|MfN|NHMO|NRM|MZLU|ZMH)[\s_.-]?(?:[A-Z]{1,4}[\s_.-]?)?\d+")]
    private static partial System.Text.RegularExpressions.Regex MuseumNumbers();

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

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(?:KB|KBR|BL|BnF|BNF|ONB|ÖNB|BSB)\s(?:[A-Z][A-Za-z]*\.?\s){0,2}\d+|\b(?:MS|Ms|Cod|Hs)\.?\s?\d+|\b[Ff]ol\.\s?\d+")]
    private static partial System.Text.RegularExpressions.Regex ShelfMarks();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b(\d{1,2})(?:st|nd|rd|th)[\s-]century", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Century();

    [System.Text.RegularExpressions.GeneratedRegex(@"\b[12]\d{3}\b")]
    private static partial System.Text.RegularExpressions.Regex Year();
}
