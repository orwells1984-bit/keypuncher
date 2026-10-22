using System.Text.Json;

namespace Keypuncher.Inference;

/// <summary>
/// A local text model that answers a prompt with JSON matching a schema.
/// Implementations: Ollama (Windows, shipped), ONNX Runtime GenAI (tablets, later).
/// </summary>
public interface ILanguageModel
{
    string ModelName { get; }

    /// <summary>
    /// Generates JSON constrained to <paramref name="jsonSchema"/>. The result's
    /// <see cref="GenerationResult.Json"/> has been parsed, but not validated against the schema;
    /// callers validate and fall back if it is wrong.
    /// </summary>
    Task<GenerationResult> GenerateJsonAsync(
        string systemPrompt,
        string userPrompt,
        JsonElement jsonSchema,
        CancellationToken cancellationToken = default);
}

public sealed record GenerationResult(
    string RawText,
    JsonDocument? Json,
    GenerationTiming Timing);

/// <summary>Timings as the runtime reports them; used by the benchmark and for adaptive scheduling.</summary>
public sealed record GenerationTiming(
    TimeSpan Total,
    TimeSpan Load,
    TimeSpan PromptEval,
    TimeSpan Eval,
    int PromptTokens,
    int OutputTokens)
{
    public double OutputTokensPerSecond =>
        Eval > TimeSpan.Zero ? OutputTokens / Eval.TotalSeconds : 0;
}
