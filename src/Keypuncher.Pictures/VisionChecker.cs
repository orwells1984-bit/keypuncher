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
/// "two white mice" passed, "a skeleton of a parasaurolophus" passed, "one branch of an oak tree" passed. So the
/// model is asked to count and describe, and the verdict is made here from the count, the wholeness, the match and
/// the words it used.</remarks>
public sealed partial class OllamaVision(HttpClient http, string model = OllamaVision.DefaultModel) : IVisionChecker
{
    public const string DefaultModel = "gemma3:4b";
    public const double PassMark = 0.6;
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    public string Model => model;

    public async Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
            ["keep_alive"] = "10m",
            ["format"] = Schema(),
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = Question(name, category),
                ["images"] = new JsonArray(Convert.ToBase64String(png)),
            }),
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Limit);
        using var response = await http.PostAsJsonAsync("api/chat", body, cts.Token);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token));
        var content = doc.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var c) ? c.GetString() : null;
        return content is null ? null : Read(content, model, DateTime.Now, name, category);
    }

    /// <summary>The one question. It asks for a count and a description rather than a verdict, since the model's
    /// verdicts did not follow its own descriptions, and it avoids the word "toy", which the model echoed back as
    /// what it saw.</summary>
    public static string Question(string name, RealCategory category) =>
        $"This is a cut-out picture for a young child's picture book. It should show exactly one whole {name} ({Describe(category)}), " +
        $"complete and alive, with nothing else. Count how many separate things are in the picture. Is the thing shown a {name}? " +
        $"Is it the whole thing, not a part, a slice, a skeleton, a drawing or a diagram? Describe what you see in one sentence.";

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
            ["shows"] = new JsonObject { ["type"] = "string" },
            ["count"] = new JsonObject { ["type"] = "integer" },
            ["is_the_thing"] = new JsonObject { ["type"] = "boolean" },
            ["whole"] = new JsonObject { ["type"] = "boolean" },
            ["confidence"] = new JsonObject { ["type"] = "number" },
        },
        ["required"] = new JsonArray("shows", "count", "is_the_thing", "whole", "confidence"),
    };

    /// <summary>Reads the model's answer: it passes when the model counted one thing, says it is the thing and whole,
    /// is confident to <see cref="PassMark"/>, and described nothing that gives the picture away (a number above one,
    /// a group, a skeleton, a half, a pie, a different named thing). An answer that is not the JSON asked for fails
    /// with what it said. The description comes first in what is kept, with the reason for a fail after it.</summary>
    public static VisionCheck Read(string content, string model, DateTime at, string name, RealCategory category)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var saw = root.TryGetProperty("shows", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
            var count = root.TryGetProperty("count", out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var i) ? i : (int?)null;
            var isThing = root.TryGetProperty("is_the_thing", out var t) && t.ValueKind == JsonValueKind.True;
            var whole = root.TryGetProperty("whole", out var w) && w.ValueKind == JsonValueKind.True;
            var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? Math.Clamp(c.GetDouble(), 0, 1) : 0;
            var why = Fault(saw, count, isThing, whole, confidence, name, category);
            return new VisionCheck(model, why is null, confidence, why is null ? saw : $"{saw} [{why}]", at);
        }
        catch (JsonException)
        {
            return new VisionCheck(model, false, 0, content.Length > 200 ? content[..200] : content, at);
        }
    }

    /// <summary>Why the answer fails, or null when it passes.</summary>
    public static string? Fault(string saw, int? count, bool isThing, bool whole, double confidence, string name, RealCategory category)
    {
        if (!isThing) return "not the thing";
        if (!whole) return "not whole";
        if (count is not 1) return count is null ? "no count" : $"counted {count}";
        if (confidence < PassMark) return "not sure";
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
