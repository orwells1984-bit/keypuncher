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
    /// <summary>Checks a cut-out. With <paramref name="photo"/> (the photo or made image it was cut from), a cut-out that
    /// passes is also compared with it, to catch a cut-out that kept only a part of the thing or something else in the photo.</summary>
    Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, byte[]? photo = null, CancellationToken ct = default);
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
    /// short verdict in a fixed shape with that sentence already in the conversation. About twelve seconds a picture.
    /// With the photo, a cut-out that passes is shown again beside it in a fresh conversation (see <see cref="Compare"/>),
    /// a few seconds more.</summary>
    public async Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, byte[]? photo = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        var ask = new JsonObject { ["role"] = "user", ["content"] = Question(name, category), ["images"] = new JsonArray(Convert.ToBase64String(png)) };
        var saw = await AskAsync(new JsonArray(ask), null, cts.Token);
        if (saw is null) return null;
        var follow = new JsonArray(
            ask.DeepClone(),
            new JsonObject { ["role"] = "assistant", ["content"] = saw },
            new JsonObject { ["role"] = "user", ["content"] = Verdict(name) });
        var verdict = await AskAsync(follow, Schema(), cts.Token);
        var check = Read(saw.Trim(), verdict, model, DateTime.Now, name, category);
        if (!check.Passed || photo is null) return check;
        var both = new JsonObject
        {
            ["role"] = "user",
            ["content"] = Compare(name),
            ["images"] = new JsonArray(Convert.ToBase64String(photo), Convert.ToBase64String(png)),
        };
        var compared = await AskAsync(new JsonArray(both), CompareSchema(), cts.Token);
        return Compared(check, compared, name);
    }

    /// <summary>The comparison, with the photo first and the cut-out second. The octopus photo's cut-out was the white
    /// shell in its arms, and the description call, shown the shell alone, saw "a single, complete, live octopus"; beside
    /// the photo the difference is plain.</summary>
    public static string Compare(string name) =>
        $"The first image is a photo. The second image is a cut-out made from that photo, meant to show the whole {name} from the photo on its own. " +
        $"Answer in JSON. kept: in a few words, what the cut-out kept from the photo. " +
        $"all_of_it: whether the cut-out kept the whole {name} from the photo, and not only a part of it or something else from the photo.";

    /// <summary>The JSON shape the comparison must answer in.</summary>
    public static JsonObject CompareSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["kept"] = new JsonObject { ["type"] = "string" },
            ["all_of_it"] = new JsonObject { ["type"] = "boolean" },
        },
        ["required"] = new JsonArray("kept", "all_of_it"),
    };

    /// <summary>A passed check, failed when the comparison says the cut-out did not keep all of the thing. A comparison
    /// that did not answer, or not in JSON, leaves the check as it was: it only ever takes a pass away, on a clear no.</summary>
    public static VisionCheck Compared(VisionCheck check, string? answer, string name)
    {
        if (!check.Passed || answer is null) return check;
        string? kept = null;
        bool? allOfIt = null;
        try
        {
            using var doc = JsonDocument.Parse(answer);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return check;
            kept = root.TryGetProperty("kept", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
            allOfIt = root.TryGetProperty("all_of_it", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False ? a.GetBoolean() : null;
        }
        catch (JsonException e)
        {
            Log.Warn("pictures", $"the picture checker's comparison for {name} was not JSON: {Log.Short(answer)}", e);
            return check;
        }
        if (allOfIt != false) return check;
        var what = string.IsNullOrWhiteSpace(kept) ? "" : $"kept {kept.Trim().TrimEnd('.')}, ";
        return check with { Passed = false, Confidence = 0, Saw = $"{check.Saw} [{what}not all of the {name} in the photo]" };
    }

    /// <summary>The count asked of the photo itself (not the cut-out): how many of the thing it shows. On the family
    /// desktop gemma3, shown a cut-out, called a school of surgeonfish "a single blue tang", eight merged sheep "a single
    /// sheep" and two whales "one complete whale"; a photo of a herd or a mother with her young is what makes those
    /// cut-outs, and counting in a plain photo is what such a model does best.</summary>
    public static string CountQuestion(string name) =>
        $"This photo is meant to show a {name}. Answer in JSON. count: how many separate {name} you can see in the photo, " +
        $"counting each one, also young, small, far away or partly hidden ones. Count only whole {name}, not their parts.";

    /// <summary>The JSON shape the count must answer in.</summary>
    public static JsonObject CountSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["count"] = new JsonObject { ["type"] = "integer" } },
        ["required"] = new JsonArray("count"),
    };

    /// <summary>One raw question, for the console tool's <c>probe</c>, which tries questions on a set of pictures without
    /// acting on any answer.</summary>
    public Task<string?> AskRawAsync(string content, IEnumerable<byte[]> images, JsonObject? format, CancellationToken ct = default) =>
        AskAsync(new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content, ["images"] = new JsonArray([.. images.Select(i => (JsonNode)Convert.ToBase64String(i))]) }), format, ct);

    /// <summary>As <see cref="AskRawAsync"/>, with the description asked first and the verdict after it, as a check does.</summary>
    public async Task<(string? Saw, string? Verdict)> AskDescribedAsync(string question, byte[] png, string name, CancellationToken ct = default)
    {
        var ask = new JsonObject { ["role"] = "user", ["content"] = question, ["images"] = new JsonArray(Convert.ToBase64String(png)) };
        var saw = await AskAsync(new JsonArray(ask), null, ct);
        if (saw is null) return (null, null);
        var verdict = await AskAsync(new JsonArray(ask.DeepClone(), new JsonObject { ["role"] = "assistant", ["content"] = saw }, new JsonObject { ["role"] = "user", ["content"] = Verdict(name) }), Schema(), ct);
        return (saw, verdict);
    }

    /// <summary>The first question without saying what the picture should show (only what it is meant to be), for the
    /// probe: whether "should show exactly one whole" leads the model to see one.</summary>
    public static string PlainQuestion(string name, RealCategory category) =>
        $"This cut-out picture is meant to be a {name} ({Describe(category)}). " +
        $"Describe what you actually see in one or two sentences: what it is, how many there are, whether anything else is in the picture, " +
        $"and whether it is cut, cooked, a model, a fossil or a drawing. Answer with the description only.";

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

    /// <summary>The first question: what is in the picture, in a sentence or two. It names what matters (one, whole,
    /// alive) without listing the faults, since the model echoed a list of faults back in a negation ("not a slice,
    /// drawing or diagram") and the word "picture book" came back as "an illustration for a children's book"; and it
    /// avoids "toy", which came back as what was seen.</summary>
    public static string Question(string name, RealCategory category) =>
        $"This cut-out picture should show exactly one whole {name} ({Describe(category)}), complete and alive, with nothing else. " +
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
                if (category == RealCategory.ExtinctAnimal && Art.Contains(word)) continue;
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
    /// <summary>Words that say a vehicle or an object is in fact a living thing (the "paddle boat" that was a duck).</summary>
    private static readonly HashSet<string> Living = ["animal", "creature", "bird", "mammal", "reptile", "insect", "fur", "feathers", "beak", "paws", "snout", "whiskers"];
    private static readonly HashSet<string> PlantPart = ["branch", "twig", "sprig"];
    /// <summary>Art words that do not count against an extinct animal, which is shown by a life restoration by nature
    /// (a Lodge drawing of a great auk, a palaeo-painting of a brachiosaurus).</summary>
    private static readonly HashSet<string> Art = ["drawing", "drawn", "sketch", "cartoon", "painting", "illustration"];

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Words();
}
