using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Keypuncher.Director;
using Keypuncher.Core;

namespace Keypuncher.Pictures;

/// <summary>Shows a picture to a vision model and asks whether it is the thing. The answer only flags: a picture
/// that fails stays in play with the flag shown on the grown-ups' page, until the checker has earned the right to
/// hide anything.</summary>
public interface IVisionChecker
{
    string Model { get; }
    /// <summary>Checks a cut-out: shows it to the model and reads what it says.</summary>
    Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default);
}

/// <summary>The check through Ollama's chat API with a multimodal model (gemma3:4b by default), asking for a JSON
/// answer in a fixed shape. Ollama holds one model at a time on the card, so this runs only outside play, after the
/// image model has let go.</summary>
/// <remarks>The first run on the desktop (158 pictures) showed gemma3's yes/no contradicting its own description:
/// "two white mice" passed, "a skeleton of a parasaurolophus" passed, "one branch of an oak tree" passed. The second
/// showed that a forced JSON shape ruins the description (":[{", "{count} bilberries"). So the model first describes
/// freely, then answers a short verdict, and the verdict is made here from the count, the wholeness, the match and
/// the words it used. Its count is trusted for animals only: it counted an oak tree's leaves (7), a fire engine's
/// parts (13) and a carrot's roots (31).</remarks>
public sealed partial class OllamaVision(HttpClient http, string model = OllamaVision.DefaultModel) : IVisionChecker
{
    public const string DefaultModel = "gemma3:4b";
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    public string Model => model;

    /// <summary>Two calls: first the picture with the question and no answer shape, which gets a real sentence
    /// (forcing JSON made gemma3 write placeholders like ":[{" or "{count} bilberries" for what it saw), then the
    /// short verdict in a fixed shape with that sentence already in the conversation.</summary>
    /// <remarks>Until November 2026 a third call showed the cut-out beside the photo it came from and asked whether it
    /// kept all of the thing. On a copy of the family desktop's 47 pictures it said yes to every one, the school of fish
    /// and the two mice included, so it went.</remarks>
    public async Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var (saw, verdict) = await AskDescribedAsync(Question(name, category), png, name, cts.Token);
        Log.Info("pictures", $"{model} looked at {name} in {watch.Elapsed.TotalSeconds:0.0} s: {Log.Short(saw ?? "(no answer)")} {verdict}");
        if (saw is null) return null;
        return Read(saw.Trim(), verdict, model, DateTime.Now, name, category);
    }

    /// <summary>The description asked first and the verdict after it, with the description in the conversation; for
    /// a check and for the console tool's <c>probe</c>.</summary>
    public async Task<(string? Saw, string? Verdict)> AskDescribedAsync(string question, byte[] png, string name, CancellationToken ct = default)
    {
        var ask = new JsonObject { ["role"] = "user", ["content"] = question, ["images"] = new JsonArray(Convert.ToBase64String(png)) };
        var saw = await AskAsync(new JsonArray(ask), null, ct);
        if (saw is null) return (null, null);
        var verdict = await AskAsync(new JsonArray(ask.DeepClone(), new JsonObject { ["role"] = "assistant", ["content"] = saw }, new JsonObject { ["role"] = "user", ["content"] = Verdict(name) }), Schema(), ct);
        return (saw, verdict);
    }

    private async Task<string?> AskAsync(JsonArray messages, JsonObject? format, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["keep_alive"] = "10m",
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["messages"] = messages,
        };
        if (format is not null) body["format"] = format;
        using var response = await http.PostAsJsonAsync("api/chat", body, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var c) ? c.GetString() : null;
    }

    /// <summary>The first question: what is in the picture, in a sentence or two. It says what the picture is meant
    /// to be, and names what matters (how many, anything else, cut, cooked, a model, a fossil, a drawing) without listing
    /// faults, since the model echoed a list of faults back in a negation ("not a slice, drawing or diagram"), and
    /// avoids "toy", which came back as what was seen.</summary>
    /// <remarks>Until November 2026 it began "This cut-out picture should show exactly one whole {name}", and the
    /// model saw what it was told it should: on a copy of the family desktop's pictures it called a school of 20-30
    /// surgeonfish "a single blue tang", ten sheep "a single sheep", and two dolphins and two whales one each. Told only
    /// what the picture is meant to be, it counted all of those right and said more than one for none of the single
    /// animals.</remarks>
    public static string Question(string name, RealCategory category) =>
        $"This cut-out picture is meant to be a {name} ({Describe(category)}). " +
        $"Describe what you actually see in one or two sentences: what it is, how many there are, whether anything else is in the picture, " +
        $"and whether it is cut, cooked, a model, a fossil or a drawing. Answer with the description only.";

    /// <summary>The second question, asked after the description, answered in the shape of <see cref="Schema"/>.</summary>
    public static string Verdict(string name) =>
        $"Now answer in JSON. count: how many separate things (animals, plants, objects) the picture shows, not their parts. " +
        $"is_the_thing: whether it shows a {name}. whole: whether it is the whole, complete, living thing and not a part, slice, skeleton or drawing.";

    private static string Describe(RealCategory category) => category switch
    {
        RealCategory.Animal => "a living animal",
        RealCategory.ExtinctAnimal => "an extinct animal, as it looked alive",
        RealCategory.Plant => "a plant",
        RealCategory.Vehicle => "a vehicle",
        _ => "an everyday object",
    };

    /// <summary>The JSON shape the model must answer in.</summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["count"] = new JsonObject { ["type"] = "integer" },
            ["is_the_thing"] = new JsonObject { ["type"] = "boolean" },
            ["whole"] = new JsonObject { ["type"] = "boolean" },
        },
        ["required"] = new JsonArray("count", "is_the_thing", "whole"),
    };

    /// <summary>Makes the verdict from the description and the model's answer: it passes when the model says it is the
    /// thing and whole, counted one (animals only: for plants it counts leaves and berries, for vehicles their parts),
    /// and described nothing that gives the picture away (a number above one, a group, a skeleton, a half, a pie, a
    /// drawing). The verdict missing or not JSON fails as "no verdict". What is kept is the description, with the
    /// reason for a fail in brackets after it.</summary>
    public static VisionCheck Read(string saw, string? verdict, string model, DateTime at, string name, RealCategory category)
    {
        int? count = null;
        bool isThing = false, whole = false, answered = false;
        if (verdict is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(verdict);
                var root = doc.RootElement;
                answered = root.ValueKind == JsonValueKind.Object;
                count = root.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var i) ? i : null;
                isThing = root.TryGetProperty("is_the_thing", out var t) && t.ValueKind == JsonValueKind.True;
                whole = root.TryGetProperty("whole", out var w) && w.ValueKind == JsonValueKind.True;
            }
            catch (JsonException e) { Log.Warn("pictures", $"the picture checker's verdict on {name} was not JSON: {Log.Short(verdict)}", e); }
        }
        saw = Sentence(saw);
        var kept = saw.Length > 300 ? saw[..300] : saw;
        var why = answered ? Fault(saw, count, isThing, whole, name, category) : "no verdict";
        return new VisionCheck(model, why is null, why is null ? 1 : 0, why is null ? kept : $"{kept} [{why}]", at);
    }

    /// <summary>The description without a preface line such as "Here's a description of the image:" (one answer in six
    /// began that way), and without the quotes or stars the model sometimes wraps it in.</summary>
    public static string Sentence(string saw)
    {
        var text = saw.Trim();
        var newline = text.IndexOf('\n');
        if (newline > 0 && text[..newline].TrimEnd().EndsWith(':')) text = text[(newline + 1)..].Trim();
        return text.Trim('*', '"', ' ', '\n', '\r');
    }

    /// <summary>Why the answer fails, or null when it passes. The count is trusted only for animals, and only when
    /// the description bears it out (see <see cref="SaysMoreThanOne"/>).</summary>
    public static string? Fault(string saw, int? count, bool isThing, bool whole, string name, RealCategory category)
    {
        if (!isThing) return "not the thing";
        if (!whole) return "not whole";
        if (category is RealCategory.Animal or RealCategory.ExtinctAnimal && count is > 1 && SaysMoreThanOne(saw)) return $"counted {count}";
        if (GiveAway(saw, name, category) is { } word) return $"says \"{word}\"";
        if (!Named(saw, name)) return "does not name it";
        return null;
    }

    /// <summary>Whether the description itself speaks of more than one thing: a number above one in words or digits, a
    /// group word, "second", "another", "together", or something "in the background", "behind it" or "beside it".
    /// The model's count alone is not enough: on 2026-11-15 it counted 2 for "a large, brown grizzly bear ... with a
    /// leaf in front of it", for "a single seahorse attached to a ... stem" and for a kangaroo with its joey, while
    /// every real pair or group that morning (two dolphins, two mice, ten sheep, "a second flamingo", three huskies)
    /// was also described as one.</summary>
    public static bool SaysMoreThanOne(string saw)
    {
        var text = saw.ToLowerInvariant();
        if (Digits().Matches(text).Any(m => int.TryParse(m.Value, out var n) && n > 1 && !Dimensional(text, m.Index + m.Length))) return true;
        foreach (Match m in Words().Matches(text))
        {
            if (Hyphenated(text, m)) continue;
            if (Many.Contains(m.Value) || More.Contains(m.Value)) return true;
        }
        return MorePhrases.Any(text.Contains);
    }

    /// <summary>Whether a word is the first half of a hyphenated word: the "five" of "five-armed", the "three" of
    /// "three-dimensional", which say how the thing is made, not how many there are.</summary>
    private static bool Hyphenated(string text, Match word) => word.Index + word.Length < text.Length && text[word.Index + word.Length] == '-';

    /// <summary>"3D" and "3-dimensional" are not a count.</summary>
    private static bool Dimensional(string text, int after) => after < text.Length && (text[after] is 'd' or '-');

    /// <summary>Whether the thing grows in clusters, so that "three blueberries on a stem" or "a bunch of sorghum" is
    /// what one of it looks like: berries, and grains and other plants that grow as many stalks. A grown-up kept the
    /// blueberries and the sorghum the checker had turned down for their number (2026-11-15).</summary>
    public static bool GrowsInClusters(string name, RealCategory category)
    {
        if (category != RealCategory.Plant) return false;
        var words = Words().Matches(name.ToLowerInvariant()).Select(m => Singular(m.Value)).ToList();
        return words.Any(w => w.EndsWith("berry") || Clustered.Contains(w));
    }

    /// <summary>Whether the description uses any word of the name (three letters or more, singular or plural): the
    /// "house" that was a peace lily and the "squirrel" that was a chipmunk did not. The model is asked about the real
    /// name, so a nursery name never reaches here.</summary>
    public static bool Named(string saw, string name)
    {
        var own = Words().Matches(name.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length >= 3).ToList();
        if (own.Count == 0) return true;
        var words = Words().Matches(saw.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        return own.Any(w => words.Contains(w) || words.Contains(w + "s") || words.Contains(w + "es") || words.Any(x => Singular(x) == w));
    }

    /// <summary>A word in the description that gives a bad picture away, whatever the verdict said. A number above one
    /// or a group word counts only when the thing itself follows within three words ("two white mice", "a flock of
    /// sheep", "several animals"), since the model also counts legs, petals and wheels; a skeleton, fossil, jaws or skull
    /// (not alive, not whole; "head and skull" is a living seal's); a half or slice; a dish or container (food, not the
    /// thing); a drawing, diagram or manuscript (but a drawing is how an extinct animal is shown); an animal word for a vehicle or object; and for a plant "a branch" or
    /// "a sprig", or any single "branch" for a tree (a part; trees have branches). A number that is the first half of a
    /// hyphenated word ("five-armed", "three-dimensional") is not a count, and no number or group word counts against
    /// a plant that grows in clusters (<see cref="GrowsInClusters"/>). Nothing after a negation in the same sentence
    /// counts, since the model repeats the question back ("not a slice or a drawing"). Null when the description is
    /// clean.</summary>
    public static string? GiveAway(string saw, string name, RealCategory category)
    {
        var own = Words().Matches(name.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        var clustered = GrowsInClusters(name, category);
        foreach (var sentence in saw.ToLowerInvariant().Split(['.', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            var matches = Words().Matches(sentence);
            var words = matches.Select(m => m.Value).ToList();
            for (var i = 0; i < words.Count; i++)
            {
                var word = words[i];
                if (Negations.Contains(word)) break;
                if (own.Contains(word)) continue;
                if (category == RealCategory.ExtinctAnimal && (Art.Contains(word) || Made.Contains(word))) continue;
                if (Made.Contains(word)) return word;
                if (category != RealCategory.Object && Cloth.Contains(word)) return word;
                if (NotAlive.Contains(word) || NotWhole.Contains(word) || NotTheThing.Contains(word)) return word;
                if (word is "skull" && !(i > 0 && words[i - 1] is "and")) return word;
                if (category is RealCategory.Vehicle or RealCategory.Object && Living.Contains(word)) return word;
                if (category == RealCategory.Plant && PlantPart.Contains(word) && (own.Contains("tree") || i > 0 && words[i - 1] is "a" or "one" or "single")) return word;
                var pair = i + 1 < words.Count ? $"{word} {words[i + 1]}" : null;
                if (pair is not null && NotWholePairs.Contains(pair)) return pair;
                // "five-armed starfish", "three-dimensional": the number says how the thing is made, not how many.
                if (Hyphenated(sentence, matches[i]) || pair is "three dimensional" or "two dimensional") continue;
                if (clustered) continue;
                if (Many.Contains(word) && Followed(words, i + 1, own)) return word;
                if (pair is not null && ManyPairs.Contains(pair) && Followed(words, i + 2, own)) return pair;
            }
        }
        return null;
    }

    /// <summary>Whether the thing itself (a word of its name, plural or not) or a generic word for things ("animals",
    /// "things") is among the next three words.</summary>
    private static bool Followed(List<string> words, int from, HashSet<string> own)
    {
        for (var i = from; i < words.Count && i < from + 3; i++)
        {
            var word = words[i];
            if (Generic.Contains(word) || own.Contains(word) || own.Contains(Singular(word))) return true;
        }
        return false;
    }

    /// <summary>A rough singular: "mice" to "mouse", "horses" to "horse", "puppies" to "puppy", "foxes" to "fox".</summary>
    public static string Singular(string word)
    {
        if (Irregular.TryGetValue(word, out var one)) return one;
        if (word.EndsWith("ies") && word.Length > 4) return word[..^3] + "y";
        if (word.EndsWith("ches") || word.EndsWith("shes") || word.EndsWith("xes") || word.EndsWith("sses")) return word[..^2];
        if (word.EndsWith('s') && !word.EndsWith("ss") && word.Length > 3) return word[..^1];
        return word;
    }

    private static readonly Dictionary<string, string> Irregular = new()
    {
        ["mice"] = "mouse", ["geese"] = "goose", ["oxen"] = "ox", ["wolves"] = "wolf", ["calves"] = "calf", ["leaves"] = "leaf",
        ["lives"] = "life", ["hooves"] = "hoof", ["cacti"] = "cactus", ["fungi"] = "fungus", ["octopi"] = "octopus", ["deer"] = "deer",
        ["sheep"] = "sheep", ["fish"] = "fish", ["moose"] = "moose", ["children"] = "child", ["people"] = "person",
    };
    private static readonly HashSet<string> Negations = ["not", "no", "nor", "rather", "instead", "without", "isn", "doesn", "aren", "never"];
    private static readonly HashSet<string> Generic = ["animals", "creatures", "beasts", "things", "objects", "items", "specimens", "individuals", "figures", "pieces", "copies"];
    private static readonly HashSet<string> Many =
    [
        "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "dozen", "dozens",
        "several", "many", "multiple", "numerous", "pair", "couple", "group", "flock", "herd", "pack", "pod",
        "school", "swarm", "litter", "family", "bunch", "stack", "pile", "heap", "collection", "montage", "collage",
    ];
    /// <summary>Words that, with the model's count above one, say a second thing is in the picture.</summary>
    private static readonly HashSet<string> More = ["second", "another", "others", "twin", "twins", "together"];
    private static readonly string[] MorePhrases = ["in the background", "behind it", "beside it", "next to it", "side-by-side", "side by side"];
    /// <summary>Plants that grow as many stalks or stems; berries are known by their name's ending.</summary>
    private static readonly HashSet<string> Clustered =
    [
        "sorghum", "wheat", "barley", "rice", "oat", "rye", "millet", "grass", "reed", "bamboo", "heather", "clover", "moss",
        "thyme", "cress", "watercress", "lavender", "grape", "currant",
    ];
    private static readonly HashSet<string> ManyPairs = ["a lot", "lots of", "a number", "a few", "a series", "a set", "a variety"];
    private static readonly HashSet<string> NotAlive = ["skeleton", "skeletons", "skulls", "fossil", "fossils", "fossilized", "fossilised", "bones", "taxidermy", "stuffed", "statue"];
    private static readonly HashSet<string> NotWhole = ["half", "halved", "halves", "slice", "sliced", "slices", "peeled", "partial", "fragment", "decaying", "rotten", "jaws"];
    private static readonly HashSet<string> NotWholePairs = ["cut in", "cut open", "cut up", "cross section", "close up"];
    private static readonly HashSet<string> NotTheThing =
    [
        "pie", "pies", "soup", "jam", "juice", "canned", "tin", "jar", "plate", "crate", "basket", "dish",
        "drawing", "drawn", "sketch", "cartoon", "diagram", "logo", "ticket", "label", "map", "chart", "poster", "manuscript", "illuminated",
    ];
    /// <summary>Words that say the picture is a made copy of the thing, not the thing: the plaid cloth that was "a coconut
    /// palm, depicted as a 3D model" passed before these counted. A model is how an extinct animal is shown, so not for it.</summary>
    private static readonly HashSet<string> Made = ["model", "models", "figurine", "figurines", "replica", "toy", "toys", "plush", "render", "rendered", "rendering", "cgi", "sculpture"];
    /// <summary>Words that say a living thing or a vehicle is in fact cloth (the coconut sellers' plaid cloth); an object may be cloth.</summary>
    private static readonly HashSet<string> Cloth = ["fabric", "cloth", "textile", "plaid", "tartan", "blanket", "quilt"];
    /// <summary>Words that say a vehicle or an object is in fact a living thing (the "paddle boat" that was a duck).</summary>
    private static readonly HashSet<string> Living = ["animal", "creature", "bird", "mammal", "reptile", "insect", "fur", "feathers", "beak", "paws", "snout", "whiskers"];
    private static readonly HashSet<string> PlantPart = ["branch", "twig", "sprig"];
    /// <summary>Art words that do not count against an extinct animal, which is shown by a life restoration by nature
    /// (a Lodge drawing of a great auk, a palaeo-painting of a brachiosaurus).</summary>
    private static readonly HashSet<string> Art = ["drawing", "drawn", "sketch", "cartoon", "painting", "illustration"];

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Words();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}
