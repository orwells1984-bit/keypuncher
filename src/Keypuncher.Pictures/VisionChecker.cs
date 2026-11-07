using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>Shows a picture to a vision model and asks whether it is the thing. The answer only flags: a picture
/// that fails stays in play with the flag shown on the grown-ups' page, until the checker has earned the right to
/// hide anything.</summary>
public interface IVisionChecker
{
    string Model { get; }
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
    /// short verdict in a fixed shape with that sentence already in the conversation. About twelve seconds a picture.</summary>
    public async Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default)
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
        return Read(saw.Trim(), verdict, model, DateTime.Now, name, category);
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

    /// <summary>The first question: what is in the picture, in one sentence, with the things that matter named so
    /// the sentence mentions them. It avoids the word "toy", which the model echoed back as what it saw.</summary>
    public static string Question(string name, RealCategory category) =>
        $"This is a cut-out picture for a young child's picture book. It should show exactly one whole {name} ({Describe(category)}), " +
        $"complete and alive, with nothing else. Describe what you actually see in one or two sentences: what it is, how many " +
        $"there are, and whether it is the whole thing or a part, a slice, a skeleton, a drawing or a diagram.";

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
            catch (JsonException) { }
        }
        var kept = saw.Length > 300 ? saw[..300] : saw;
        var why = answered ? Fault(saw, count, isThing, whole, name, category) : "no verdict";
        return new VisionCheck(model, why is null, why is null ? 1 : 0, why is null ? kept : $"{kept} [{why}]", at);
    }

    /// <summary>Why the answer fails, or null when it passes. The count is trusted only for animals.</summary>
    public static string? Fault(string saw, int? count, bool isThing, bool whole, string name, RealCategory category)
    {
        if (!isThing) return "not the thing";
        if (!whole) return "not whole";
        if (category is RealCategory.Animal or RealCategory.ExtinctAnimal && count is > 1) return $"counted {count}";
        if (GiveAway(saw, name, category) is { } word) return $"says \"{word}\"";
        return null;
    }

    /// <summary>A word in the description that gives a bad picture away, whatever the verdict said: a number above
    /// one or a group word (several things), a skeleton or fossil (not alive), a half or slice (not whole), a dish or
    /// container (food, not the thing), a drawing or diagram, and for a plant a branch or sprig (a part). Words the
    /// model is likely to use about a good picture ("cut-out", "can see", "both ears", "stem") are kept off the
    /// lists. Null when the description is clean.</summary>
    public static string? GiveAway(string saw, string name, RealCategory category)
    {
        var words = Words().Matches(saw.ToLowerInvariant()).Select(m => m.Value).ToList();
        var own = Words().Matches(name.ToLowerInvariant()).Select(m => m.Value).ToHashSet();
        foreach (var word in words)
        {
            if (own.Contains(word)) continue;
            if (Many.Contains(word) || NotAlive.Contains(word) || NotWhole.Contains(word) || NotTheThing.Contains(word)) return word;
            if (category == RealCategory.Plant && PlantPart.Contains(word)) return word;
        }
        for (var i = 0; i + 1 < words.Count; i++)
        {
            var pair = $"{words[i]} {words[i + 1]}";
            if (ManyPairs.Contains(pair) || NotWholePairs.Contains(pair)) return pair;
        }
        return null;
    }

    private static readonly HashSet<string> Many =
    [
        "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "dozen", "dozens",
        "several", "many", "multiple", "numerous", "pair", "couple", "group", "flock", "herd", "pack", "pod",
        "school", "swarm", "litter", "family", "bunch", "stack", "pile", "heap", "collection", "pieces", "montage", "collage",
    ];
    private static readonly HashSet<string> ManyPairs = ["a lot", "lots of", "a number", "a few", "a series", "a set", "a variety"];
    private static readonly HashSet<string> NotAlive = ["skeleton", "skeletons", "skull", "skulls", "fossil", "fossils", "fossilized", "fossilised", "bones", "taxidermy", "stuffed"];
    private static readonly HashSet<string> NotWhole = ["half", "halved", "halves", "slice", "sliced", "slices", "peeled", "partial", "fragment", "decaying", "rotten"];
    private static readonly HashSet<string> NotWholePairs = ["cut in", "cut open", "cut up", "cross section", "close up"];
    private static readonly HashSet<string> NotTheThing =
    [
        "pie", "pies", "soup", "jam", "juice", "canned", "tin", "jar", "plate", "bowl", "crate", "basket", "dish",
        "drawing", "drawn", "sketch", "painting", "cartoon", "illustration", "diagram", "logo", "ticket", "label", "text", "words",
        "map", "chart", "poster",
    ];
    private static readonly HashSet<string> PlantPart = ["branch", "branches", "twig", "twigs", "sprig", "sprigs", "acorn", "acorns", "cone", "cones"];

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Words();
}
