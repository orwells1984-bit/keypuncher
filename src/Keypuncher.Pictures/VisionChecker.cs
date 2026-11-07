using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class OllamaVision(HttpClient http, string model = OllamaVision.DefaultModel) : IVisionChecker
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
        return content is null ? null : Read(content, model, DateTime.Now);
    }

    /// <summary>The one question, phrased for a toddler's toy so the model judges whole-and-alone, not art.</summary>
    public static string Question(string name, RealCategory category) =>
        $"This is a cut-out picture for a toddler's toy. Does it show exactly one whole {name} ({Describe(category)}) and nothing else? Say what you see.";

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
            ["is_the_thing"] = new JsonObject { ["type"] = "boolean" },
            ["one_whole_thing"] = new JsonObject { ["type"] = "boolean" },
            ["confidence"] = new JsonObject { ["type"] = "number" },
        },
        ["required"] = new JsonArray("shows", "is_the_thing", "one_whole_thing", "confidence"),
    };

    /// <summary>Reads the model's answer: it passes when it says this is the thing, whole and alone, with confidence
    /// at or above <see cref="PassMark"/>. An answer that is not the JSON asked for fails with what it said.</summary>
    public static VisionCheck Read(string content, string model, DateTime at)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var saw = root.TryGetProperty("shows", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
            var isThing = root.TryGetProperty("is_the_thing", out var i) && i.ValueKind == JsonValueKind.True;
            var whole = root.TryGetProperty("one_whole_thing", out var w) && w.ValueKind == JsonValueKind.True;
            var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? Math.Clamp(c.GetDouble(), 0, 1) : 0;
            return new VisionCheck(model, isThing && whole && confidence >= PassMark, confidence, saw, at);
        }
        catch (JsonException)
        {
            return new VisionCheck(model, false, 0, content.Length > 200 ? content[..200] : content, at);
        }
    }
}
