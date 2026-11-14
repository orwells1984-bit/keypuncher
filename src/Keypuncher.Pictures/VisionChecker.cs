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

    /// <summary>Why the answer fails, or null when it passes. The count is trusted only for animals.</summary>
    public static string? Fault(string saw, int? count, bool isThing, bool whole, string name, RealCategory category)
    {
        if (!isThing) return "not the thing";
        if (!whole) return "not whole";
        if (category is RealCategory.Animal or RealCategory.ExtinctAnimal && count is > 1) return $"counted {count}";
        if (GiveAway(saw, name, category) is { } word) return $"says \"{word}\"";
        if (!Named(saw, name)) return "does not name it";
        return null;
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
    /// "a sprig", or any single "branch" for a tree (a part; trees have branches). Nothing after a negation in the same sentence
    /// counts, since the model repeats the question back ("not a slice or a drawing"). Null when the description is
    /// clean.</summary>
    public static string? GiveAway(string saw, string name, RealCategory category)
    {
        var own = Words().Matches(name.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        foreach (var sentence in saw.ToLowerInvariant().Split(['.', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries))
        {
            var words = Words().Matches(sentence).Select(m => m.Value).ToList();
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
                if (pair is "three dimensional" or "two dimensional") continue;
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
}
