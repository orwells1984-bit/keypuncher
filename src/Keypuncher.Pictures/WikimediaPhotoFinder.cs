using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>
/// Looks for a photo of a real thing the way a careful person would: the English Wikipedia article first (a
/// disambiguation page sends us to a search for the article whose description fits the category), then the
/// pictures Wikidata says show the thing, the article's lead picture, an iNaturalist photo for animals and plants,
/// and the next two pictures in the article. An article about something of another kind (the aircraft when a bird
/// called kite is wanted) is passed over for a search hit whose description fits. For an extinct animal, Commons is searched for files that depict it,
/// with life restorations before skeletons. Every Commons file is looked up and vetted by <see cref="PhotoRules"/>:
/// a hard no moves on, a soft no (a painting) is kept in case nothing better turns up. Only reads: nothing here
/// writes to disk.
/// </summary>
public sealed partial class WikimediaPhotoFinder(HttpClient http) : IPhotoFinder
{
    private const string Wikipedia = "https://en.wikipedia.org";
    private const string Commons = "https://commons.wikimedia.org";
    private const string Wikidata = "https://www.wikidata.org";
    private const string INaturalist = "https://api.inaturalist.org";
    private const string ImageInfoProps = "iiprop=url|size|mime|mediatype|sha1|extmetadata&iiurlwidth=1280"
        + "&iiextmetadatafilter=LicenseShortName|LicenseUrl|Artist|Credit|Attribution|AttributionRequired|Copyrighted|UsageTerms&iiextmetadatalanguage=en";
    private static readonly string[] BitmapExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>The article and what it points at; nothing found is an article with nothing in it.</summary>
    private sealed record Article(string? Title, string? Canonical, string? Item, string? LeadFileTitle, string? LeadUrl)
    {
        public static readonly Article None = new(null, null, null, null, null);
    }

    private sealed record Candidate(FileFacts Facts, string DescriptionUrl, string? ThumbUrl, string? Artist, string? Credit, string? LicenseUrl, string? Attribution, string? Sha1);

    public async Task<PhotoSearch> FindAsync(string name, RealCategory category, IReadOnlyCollection<string> exclude, CancellationToken ct = default)
    {
        var notes = new List<string>();
        try
        {
            var article = await ArticleAsync(name, category, notes, ct);
            FoundPhoto? soft = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Looks one Commons file up and vets it; the first clean one wins, a doubtful one waits in reserve.
            async Task<FoundPhoto?> Consider(string fileTitle, string how)
            {
                fileTitle = NormalTitle(fileTitle);
                if (!seen.Add(fileTitle)) return null;
                if (exclude.Any(x => NormalTitle(x).Equals(fileTitle, StringComparison.OrdinalIgnoreCase))) { notes.Add($"{fileTitle}: turned down before"); return null; }
                var candidate = await CommonsFileAsync(fileTitle, ct);
                if (candidate is null) { notes.Add($"{fileTitle}: not on Commons"); return null; }
                var verdict = PhotoRules.Vet(candidate.Facts, category, name);
                if (verdict is null) return await ReadyAsync(candidate, article, how, null, ct);
                notes.Add($"{fileTitle}: {verdict.Value.Why}");
                if (verdict.Value.Kind == Rejection.Soft) soft ??= await ReadyAsync(candidate, article, how, verdict.Value.Why, ct);
                return null;
            }

            if (article.Item is { } item)
                foreach (var title in await WikidataImagesAsync(item, ct))
                    if (await Consider(title, "Wikidata's picture of it") is { } found) return PhotoSearch.Found(found, notes);

            if (category == RealCategory.ExtinctAnimal && article.Item is { } depicted)
                foreach (var title in await CommonsDepictingAsync(depicted, ct))
                    if (await Consider(title, "a Commons file depicting it") is { } found) return PhotoSearch.Found(found, notes);

            if (article.LeadFileTitle is { } lead)
            {
                if (article.LeadUrl?.Contains("/wikipedia/en/", StringComparison.OrdinalIgnoreCase) == true) notes.Add($"{lead}: a local Wikipedia upload, not Commons");
                else if (await Consider(lead, "the article's lead picture") is { } found) return PhotoSearch.Found(found, notes);
            }

            if (category is RealCategory.Animal or RealCategory.Plant && await INaturalistAsync(name, exclude, notes, ct) is { } inat)
                return PhotoSearch.Found(inat, notes);

            if (article.Title is { } page)
                foreach (var title in (await ArticleImagesAsync(article.Canonical ?? page.Replace(' ', '_'), ct)).Where(t => !NormalTitle(t).Equals(NormalTitle(article.LeadFileTitle ?? ""), StringComparison.OrdinalIgnoreCase)).Take(2))
                    if (await Consider(title, "a picture in the article") is { } found) return PhotoSearch.Found(found, notes);

            if (soft is not null) return PhotoSearch.Found(soft, notes);
            return PhotoSearch.Nothing(article.Title is null ? "no article" : "no photo found", notes);
        }
        catch (Exception e) when (PictureHttp.IsNetworkTrouble(e, ct))
        {
            notes.Add($"no network: {e.Message}");
            return PhotoSearch.NoNetwork(e.Message, notes);
        }
        catch (JsonException e)
        {
            notes.Add($"an answer could not be read: {e.Message}");
            return PhotoSearch.Nothing("an answer could not be read", notes);
        }
    }

    /// <summary>The article for the name: its summary, or, when that is a disambiguation page or an article whose
    /// description does not fit the category (the name's primary topic is something else), the first search hit
    /// that is an article whose description fits.</summary>
    private async Task<Article> ArticleAsync(string name, RealCategory category, List<string> notes, CancellationToken ct)
    {
        string? skip = null;
        using var summary = await PictureHttp.GetJsonAsync(http, $"{Wikipedia}/api/rest_v1/page/summary/{Uri.EscapeDataString(TitleFor(name))}", ct);
        if (summary is null) { notes.Add("no article"); }
        else if (summary.RootElement.TryGetProperty("type", out var type) && type.GetString() == "disambiguation")
        {
            skip = summary.RootElement.TryGetProperty("title", out var dt) ? dt.GetString() : null;
            notes.Add("a disambiguation page; searching");
        }
        else
        {
            var described = Text(summary.RootElement, "description");
            if (Fits(described, category)) return Read(summary.RootElement);
            skip = summary.RootElement.TryGetProperty("title", out var st) ? st.GetString() : null;
            notes.Add($"{skip ?? name} ({described ?? "no description"}) is not {Describe(category)}; searching");
        }

        // The search is nudged towards the kind of thing wanted ("kite animal" rather than "kite"), and a hit is taken
        // only when it is plainly about the name: its title shares a word with the name, or its snippet says the
        // name whole. Without that, "arctic thyme" takes the first plant the search ranks highly (Subshrub).
        var hint = SearchHint(category);
        var terms = hint is null ? name : $"{name} {hint}";
        using var search = await PictureHttp.GetJsonAsync(http, $"{Wikipedia}/w/api.php?action=query&format=json&formatversion=2&list=search&srsearch={Uri.EscapeDataString(terms)}&srlimit=5", ct);
        if (search is null || !search.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("search", out var hits)) return Article.None;
        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("title", out var t) || t.GetString() is not { } title || title == skip) continue;
            if (!Named(title, Text(hit, "snippet"), name)) { notes.Add($"{title} does not say {name}"); continue; }
            using var page = await PictureHttp.GetJsonAsync(http, $"{Wikipedia}/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}", ct);
            if (page is null) continue;
            var root = page.RootElement;
            if (root.TryGetProperty("type", out var kind) && kind.GetString() == "disambiguation") continue;
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            if (!Fits(description, category)) { notes.Add($"{title} ({description ?? "no description"}) is not {Describe(category)}"); continue; }
            notes.Add($"took the article {title} ({description})");
            return Read(root);
        }
        return Article.None;

        static Article Read(JsonElement root)
        {
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var canonical = root.TryGetProperty("titles", out var titles) && titles.TryGetProperty("canonical", out var c) ? c.GetString() : title?.Replace(' ', '_');
            var item = root.TryGetProperty("wikibase_item", out var q) ? q.GetString() : null;
            string? leadTitle = null, leadUrl = null;
            if (root.TryGetProperty("originalimage", out var image) && image.TryGetProperty("source", out var s) && s.GetString() is { } source)
            {
                // The summary sometimes tags the URL with tracking parameters; the file is the path alone.
                var query = source.IndexOf('?');
                if (query >= 0) source = source[..query];
                leadUrl = source;
                leadTitle = "File:" + Uri.UnescapeDataString(source[(source.LastIndexOf('/') + 1)..]).Replace('_', ' ');
            }
            return new Article(title, canonical, item, leadTitle, leadUrl);
        }
    }

    /// <summary>The Commons files Wikidata lists as pictures of the item (P18): preferred ones first, deprecated ones not at all.</summary>
    private async Task<IReadOnlyList<string>> WikidataImagesAsync(string item, CancellationToken ct)
    {
        using var doc = await PictureHttp.GetJsonAsync(http, $"{Wikidata}/wiki/Special:EntityData/{item}.json", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("entities", out var entities)) return [];
        using var entity = entities.EnumerateObject();
        if (!entity.MoveNext()) return [];
        if (!entity.Current.Value.TryGetProperty("claims", out var claims) || !claims.TryGetProperty("P18", out var images)) return [];
        var found = new List<(int Order, string Title)>();
        foreach (var claim in images.EnumerateArray())
        {
            var rank = claim.TryGetProperty("rank", out var r) ? r.GetString() : "normal";
            if (rank == "deprecated") continue;
            if (claim.TryGetProperty("mainsnak", out var snak) && snak.TryGetProperty("datavalue", out var value)
                && value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } file)
                found.Add((rank == "preferred" ? 0 : 1, "File:" + file));
        }
        return [.. found.OrderBy(f => f.Order).Select(f => f.Title)];
    }

    /// <summary>Commons files tagged as depicting the item, life restorations first: for extinct animals.</summary>
    private async Task<IReadOnlyList<string>> CommonsDepictingAsync(string item, CancellationToken ct)
    {
        var search = Uri.EscapeDataString($"haswbstatement:P180={item} filetype:bitmap");
        using var doc = await PictureHttp.GetJsonAsync(http, $"{Commons}/w/api.php?action=query&format=json&formatversion=2&generator=search&gsrnamespace=6&gsrlimit=10&gsrsearch={search}&prop=imageinfo&{ImageInfoProps}", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return [];
        var titles = new List<(int Index, bool Restoration, string Title)>();
        foreach (var page in pages.EnumerateArray())
        {
            if (!page.TryGetProperty("title", out var t) || t.GetString() is not { } title || !IsBitmapName(title)) continue;
            var index = page.TryGetProperty("index", out var i) ? i.GetInt32() : int.MaxValue;
            var facts = new FileFacts(title, "", "", 0, 0, [], "", "");
            titles.Add((index, PhotoRules.IsRestoration(facts), title));
        }
        return [.. titles.OrderBy(t => t.Restoration ? 0 : 1).ThenBy(t => t.Index).Select(t => t.Title)];
    }

    /// <summary>The bitmap pictures the article shows, in page order, less its lead picture and the icons templates
    /// bring in: the REST media list. (The Action API's <c>prop=images</c> lists every file the page transcludes in
    /// title order, which says nothing about which picture comes next.)</summary>
    private async Task<IReadOnlyList<string>> ArticleImagesAsync(string canonicalTitle, CancellationToken ct)
    {
        using var doc = await PictureHttp.GetJsonAsync(http, $"{Wikipedia}/api/rest_v1/page/media-list/{Uri.EscapeDataString(canonicalTitle)}", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var titles = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (Text(item, "type") != "image") continue;
            if (item.TryGetProperty("leadImage", out var lead) && lead.ValueKind == JsonValueKind.True) continue;
            if (!item.TryGetProperty("showInGallery", out var shown) || shown.ValueKind != JsonValueKind.True) continue;
            if (Text(item, "title") is { } file && IsBitmapName(file)) titles.Add(file);
        }
        return titles;
    }

    /// <summary>An iNaturalist taxon's default photo, when a taxon is named as the thing is: its common name, its
    /// scientific name or the term the search matched, a plural allowed. The search itself is fuzzy and ranked by
    /// observations, so its first hit is not taken on trust. The photo is the 1024 px "large" copy, which the taxa
    /// answer does not list but which sits beside the medium one; a photo smaller or narrower than
    /// <see cref="PhotoRules"/> allow is passed over. Only a Creative Commons licence that allows the cut-out is
    /// accepted: no licence means all rights reserved, and a no-derivatives one forbids the cut-out.</summary>
    private async Task<FoundPhoto?> INaturalistAsync(string name, IReadOnlyCollection<string> exclude, List<string> notes, CancellationToken ct)
    {
        using var doc = await PictureHttp.GetJsonAsync(http, $"{INaturalist}/v1/taxa?q={Uri.EscapeDataString(name)}&per_page=3", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results)) return null;
        var taxa = results.EnumerateArray().ToList();
        bool Matches(JsonElement taxon) =>
            Same(Text(taxon, "preferred_common_name"), name) || Same(Text(taxon, "name"), name) || Same(Text(taxon, "matched_term"), name);
        var named = taxa.Where(Matches).ToList();
        if (named.Count == 0) { notes.Add($"iNaturalist: no taxon named {name}"); return null; }
        foreach (var taxon in named)
        {
            if (!taxon.TryGetProperty("default_photo", out var photo) || photo.ValueKind != JsonValueKind.Object) continue;
            var medium = Text(photo, "medium_url");
            var url = Text(photo, "large_url") ?? (medium?.Contains("/medium.", StringComparison.Ordinal) == true ? medium.Replace("/medium.", "/large.") : medium);
            if (url is null) continue;
            var id = photo.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt64().ToString(CultureInfo.InvariantCulture) : url;
            var fileTitle = $"inaturalist:{id}";
            if (exclude.Any(x => x.Equals(fileTitle, StringComparison.OrdinalIgnoreCase))) { notes.Add($"{fileTitle}: turned down before"); continue; }
            var taxonName = Text(taxon, "preferred_common_name") ?? Text(taxon, "name") ?? name;
            var taxonId = taxon.TryGetProperty("id", out var ti) && ti.ValueKind == JsonValueKind.Number ? ti.GetInt64().ToString(CultureInfo.InvariantCulture) : "";
            var licence = Text(photo, "license_code");
            if (licence is null || !INaturalistLicences.Contains(licence)) { notes.Add($"{fileTitle}: licence {licence ?? "all rights reserved"}"); continue; }
            int width = 0, height = 0;
            if (photo.TryGetProperty("original_dimensions", out var dims) && dims.ValueKind == JsonValueKind.Object)
            {
                width = dims.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : 0;
                height = dims.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;
            }
            if (Math.Max(width, height) < PhotoRules.MinLongSide) { notes.Add($"{fileTitle}: only {width}x{height}"); continue; }
            if (width / (double)height is < PhotoRules.MinAspect or > PhotoRules.MaxAspect) { notes.Add($"{fileTitle}: shape {width}x{height}"); continue; }
            var provenance = new PhotoProvenance(
                PageTitle: taxonName, PageUrl: $"https://www.inaturalist.org/taxa/{taxonId}", FileTitle: fileTitle, FileUrl: url,
                DescriptionUrl: $"https://www.inaturalist.org/photos/{id}", Artist: Text(photo, "attribution"), Credit: null,
                LicenseShortName: licence.ToUpperInvariant().Replace("-", " "),
                LicenseUrl: null, Attribution: Text(photo, "attribution"), Sha1: null, Width: width, Height: height,
                Mime: url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg", How: "iNaturalist's photo of the taxon");
            notes.Add($"took iNaturalist's photo of {taxonName}");
            return new FoundPhoto(provenance, url);
        }
        return null;
    }

    /// <summary>What Commons knows about one file; null when the file is not there.</summary>
    private async Task<Candidate?> CommonsFileAsync(string fileTitle, CancellationToken ct)
    {
        using var doc = await PictureHttp.GetJsonAsync(http, $"{Commons}/w/api.php?action=query&format=json&formatversion=2&titles={Uri.EscapeDataString(fileTitle)}&prop=imageinfo|categories&{ImageInfoProps}&clshow=!hidden&cllimit=max", ct);
        if (doc is null || !doc.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages)) return null;
        foreach (var page in pages.EnumerateArray())
        {
            if (page.TryGetProperty("missing", out var missing) && missing.ValueKind == JsonValueKind.True) return null;
            if (!page.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0) return null;
            var info = infos[0];
            var title = page.TryGetProperty("title", out var t) ? t.GetString() ?? fileTitle : fileTitle;
            var categories = page.TryGetProperty("categories", out var cats)
                ? cats.EnumerateArray().Select(c => c.TryGetProperty("title", out var ct2) ? ct2.GetString() : null).Where(c => c is not null).Select(c => c!).ToList()
                : [];
            var meta = info.TryGetProperty("extmetadata", out var m) ? m : default;
            string? Meta(string key) => meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(key, out var e) && e.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var facts = new FileFacts(
                title, Text(info, "mime") ?? "", Text(info, "mediatype") ?? "",
                info.TryGetProperty("width", out var w) ? w.GetInt32() : 0, info.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                categories, Meta("LicenseShortName") ?? "", Meta("Copyrighted") ?? "", Text(info, "url"));
            return new Candidate(facts, Text(info, "descriptionurl") ?? $"{Commons}/wiki/{title.Replace(' ', '_')}", Text(info, "thumburl"),
                Tidy(Meta("Artist")), Tidy(Meta("Credit")), Meta("LicenseUrl"), Tidy(Meta("Attribution")), Text(info, "sha1"));
        }
        return null;
    }

    /// <summary>Turns a vetted file into the photo to download: a 1280 px copy when the original is at least that
    /// wide, a 960 px copy when it is at least that, else the original.</summary>
    private async Task<FoundPhoto> ReadyAsync(Candidate c, Article article, string how, string? doubt, CancellationToken ct)
    {
        var original = c.Facts.FileUrl ?? "";
        var download = original;
        if (c.Facts.Width >= 1280 && c.ThumbUrl is { Length: > 0 } thumb) download = thumb;
        else if (c.Facts.Width >= 960)
        {
            using var doc = await PictureHttp.GetJsonAsync(http, $"{Commons}/w/api.php?action=query&format=json&formatversion=2&titles={Uri.EscapeDataString(c.Facts.FileTitle)}&prop=imageinfo&iiprop=url&iiurlwidth=960", ct);
            if (doc is not null && doc.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("pages", out var pages))
                foreach (var page in pages.EnumerateArray())
                    if (page.TryGetProperty("imageinfo", out var infos) && infos.GetArrayLength() > 0 && Text(infos[0], "thumburl") is { Length: > 0 } smaller) download = smaller;
        }
        var provenance = new PhotoProvenance(
            PageTitle: article.Title ?? "", PageUrl: article.Canonical is { } canonical ? $"{Wikipedia}/wiki/{canonical}" : "",
            FileTitle: c.Facts.FileTitle, FileUrl: original, DescriptionUrl: c.DescriptionUrl, Artist: c.Artist, Credit: c.Credit,
            LicenseShortName: c.Facts.LicenseShortName, LicenseUrl: c.LicenseUrl, Attribution: c.Attribution, Sha1: c.Sha1,
            Width: c.Facts.Width, Height: c.Facts.Height, Mime: c.Facts.Mime, How: how);
        return new FoundPhoto(provenance, download, doubt);
    }

    /// <summary>The article title the REST summary wants: first letter up, spaces as underscores.</summary>
    public static string TitleFor(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return trimmed;
        return (char.ToUpperInvariant(trimmed[0]) + trimmed[1..]).Replace(' ', '_');
    }

    /// <summary>"File:Sable antelope.jpg" however it was spelt: with the prefix, spaces not underscores.</summary>
    public static string NormalTitle(string fileTitle)
    {
        var t = fileTitle.Trim().Replace('_', ' ');
        return t.StartsWith("File:", StringComparison.OrdinalIgnoreCase) ? "File:" + t[5..] : "File:" + t;
    }

    /// <summary>Whether an article's one-line description can be a thing of this category: a species for an
    /// animal, a plant for a plant, a vehicle for a vehicle; for an object, anything that is not a person, place or work.</summary>
    public static bool Fits(string? description, RealCategory category)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        var words = description.ToLowerInvariant().Split([' ', ',', '.', ';', ':', '(', ')', '-', '/'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        bool Has(string w) => w.Contains(' ') ? description.Contains(w, StringComparison.OrdinalIgnoreCase) : words.Contains(w) || words.Contains(w + "s") || words.Contains(w + "es");
        bool Any(string[] list) => list.Any(Has);
        return category switch
        {
            RealCategory.Animal => Any(AnimalWords) || (Any(TaxonWords) && !Any(PlantWords)),
            RealCategory.ExtinctAnimal => Any(ExtinctWords) || Any(AnimalWords) || (Any(TaxonWords) && !Any(PlantWords)),
            RealCategory.Plant => Any(PlantWords) || (Any(TaxonWords) && !Any(AnimalWords)),
            RealCategory.Vehicle => Any(VehicleWords),
            _ => !Any(NotObjectWords),
        };
    }

    /// <summary>Words a description of a living thing of either kingdom may use.</summary>
    private static readonly string[] TaxonWords = ["species", "subspecies", "genus", "family", "subfamily", "order", "suborder", "infraorder", "tribe", "clade", "breed", "cultivar", "variety", "hybrid"];
    private static readonly string[] AnimalWords =
    [
        "animal", "mammal", "bird", "fish", "reptile", "amphibian", "insect", "spider", "arachnid", "mollusc", "mollusk", "crustacean", "cephalopod",
        "rodent", "primate", "cetacean", "butterfly", "moth", "beetle", "frog", "toad", "snake", "lizard", "turtle", "shark", "whale", "dolphin", "seal",
        "bat", "bear", "deer", "antelope", "monkey", "ape", "parrot", "owl", "eagle", "penguin", "cat", "dog", "horse", "pony", "marsupial", "pinniped",
        "canid", "felid", "ungulate", "bovid", "wasp", "bee", "ant", "worm", "snail", "crab", "squid", "octopus", "jellyfish", "sponge", "coral",
        "bovine", "cattle", "livestock", "poultry", "domesticated", "pet", "equine", "canine", "feline", "porcine", "ovine", "caprine",
        "fowl", "waterfowl", "seabird", "songbird", "raptor",
    ];
    private static readonly string[] ExtinctWords = ["extinct", "dinosaur", "fossil", "prehistoric", "pterosaur", "mammoth", "ichthyosaur", "plesiosaur", "trilobite", "ammonite"];
    private static readonly string[] PlantWords =
    [
        "plant", "tree", "flower", "shrub", "fruit", "vegetable", "grass", "herb", "fungus", "fungi", "mushroom", "vine", "flowering", "conifer",
        "palm", "fern", "moss", "cactus", "cacti", "berry", "cereal", "crop", "legume", "orchid", "bush", "weed", "seaweed", "alga", "algae", "lichen",
    ];
    private static readonly string[] VehicleWords =
    [
        "vehicle", "car", "automobile", "aircraft", "airplane", "aeroplane", "helicopter", "ship", "boat", "train", "locomotive", "truck",
        "lorry", "tractor", "bicycle", "motorcycle", "bus", "tram", "submarine", "rocket", "spacecraft", "van", "scooter", "airliner", "ferry",
        "sailing", "steamship", "railway", "carriage", "wagon", "cart", "sled", "balloon", "glider", "jet", "tank", "excavator", "digger",
    ];
    private static readonly string[] NotObjectWords =
    [
        "footballer", "politician", "actor", "actress", "singer", "musician", "born", "writer", "player", "band", "album", "film", "song",
        "novel", "village", "town", "city", "river", "company", "surname", "given name", "character", "television", "series", "episode",
        "game", "poet", "painter", "author", "composer", "comedian", "athlete", "wrestler", "boxer", "cyclist", "swimmer", "lake", "mountain",
        "island", "county", "district", "municipality", "neighbourhood", "neighborhood", "street", "magazine", "newspaper", "journal",
    ];

    /// <summary>The word added to a search so that the kind of thing wanted ranks first; none for an object, which can be anything.</summary>
    private static string? SearchHint(RealCategory category) => category switch
    {
        RealCategory.Animal => "animal",
        RealCategory.ExtinctAnimal => "extinct animal",
        RealCategory.Plant => "plant",
        RealCategory.Vehicle => "vehicle",
        _ => null,
    };

    /// <summary>Whether a search hit is plainly about the name: its title shares a word of three letters or more with
    /// the name (a plural allowed), or its snippet says the whole name.</summary>
    public static bool Named(string title, string? snippet, string name)
    {
        static string[] Words(string text) => text.ToLowerInvariant().Split([' ', ',', '.', ';', ':', '(', ')', '-', '/', '\''], StringSplitOptions.RemoveEmptyEntries);
        static bool SameWord(string a, string b) => a == b || a == b + "s" || b == a + "s" || a == b + "es" || b == a + "es";
        var wanted = Words(name).Where(w => w.Length >= 3).ToList();
        if (wanted.Count == 0) wanted = Words(name).ToList();
        if (Words(title).Any(t => wanted.Any(w => SameWord(t, w)))) return true;
        if (Plain(snippet) is not { } text) return false;
        var said = Words(text);
        var all = Words(name);
        for (var i = 0; i + all.Length <= said.Length; i++)
            if (all.Select((w, j) => SameWord(said[i + j], w)).All(same => same)) return true;
        return false;
    }

    /// <summary>The iNaturalist licence codes under which a photo may be cut out and shown.</summary>
    private static readonly HashSet<string> INaturalistLicences = new(StringComparer.OrdinalIgnoreCase) { "cc0", "cc-by", "cc-by-sa", "cc-by-nc", "cc-by-nc-sa" };

    private static string Describe(RealCategory category) => category switch
    {
        RealCategory.Animal => "an animal",
        RealCategory.ExtinctAnimal => "an extinct animal",
        RealCategory.Plant => "a plant",
        RealCategory.Vehicle => "a vehicle",
        _ => "an object",
    };

    private static bool IsBitmapName(string title) => BitmapExtensions.Any(e => title.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether two names are the same, give or take a plural s.</summary>
    private static bool Same(string? a, string b)
    {
        if (a is null) return false;
        a = a.Trim();
        b = b.Trim();
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        return a.Length > 1 && a.EndsWith('s') && a[..^1].Equals(b, StringComparison.OrdinalIgnoreCase)
            || b.Length > 1 && b.EndsWith('s') && b[..^1].Equals(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Commons credits are HTML: the words without the tags.</summary>
    public static string? Plain(string? html)
    {
        if (html is null) return null;
        var text = WebUtility.HtmlDecode(Tags().Replace(html, " "));
        text = Spaces().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>A credit fit for a line on the grown-ups' page: the plain words, Commons' "no machine-readable author"
    /// boilerplate cut down to the name it assumed, and no more than about 120 characters.</summary>
    public static string? Tidy(string? html)
    {
        if (Plain(html) is not { } text) return null;
        text = Assumed().Replace(text, "$1 (assumed)").Trim();
        if (text.Length > 120)
        {
            var cut = text.LastIndexOf(' ', 117);
            text = text[..(cut >= 60 ? cut : 117)] + "...";
        }
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"No machine-readable (?:author|source) provided\.\s*(.+?)\s+assumed \(based on copyright claims\)\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Assumed();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
