using System.Text.Json;
using Keypuncher.Inference;
using OllamaSharp;
using OllamaSharp.Models;

namespace Keypuncher.Inference.Ollama;

/// <summary>
/// <see cref="ILanguageModel"/> over a local Ollama server. Uses Ollama's structured output
/// (<c>format</c> = a JSON schema) so the model is constrained at decode time.
/// </summary>
public sealed class OllamaLanguageModel : ILanguageModel
{
    private readonly OllamaApiClient _client;
    private readonly int _numCtx;
    private readonly float _temperature;

    public OllamaLanguageModel(Uri baseUri, string modelName, int numCtx = 4096, float temperature = 0.8f)
    {
        _client = new OllamaApiClient(baseUri, modelName);
        ModelName = modelName;
        _numCtx = numCtx;
        _temperature = temperature;
    }

    public string ModelName { get; }

    public async Task<GenerationResult> GenerateJsonAsync(
        string systemPrompt, string userPrompt, JsonElement jsonSchema, CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest
        {
            Model = ModelName,
            System = systemPrompt,
            Prompt = userPrompt,
            Format = jsonSchema,
            Stream = true,
            // Reasoning models (qwen3 and friends) otherwise spend thousands of tokens thinking
            // before the ~150 tokens of JSON we want; the bench measured 60 to 130 s per recipe.
            Think = false,
            Options = new RequestOptions { NumCtx = _numCtx, Temperature = _temperature },
        };

        var text = new System.Text.StringBuilder();
        GenerateDoneResponseStream? done = null;

        await foreach (var chunk in _client.GenerateAsync(request, cancellationToken))
        {
            if (chunk is null) continue;
            text.Append(chunk.Response);
            if (chunk is GenerateDoneResponseStream d) done = d;
        }

        var raw = text.ToString();
        JsonDocument? json = null;
        try { json = JsonDocument.Parse(raw); }
        catch (JsonException) { /* caller treats null as a failed generation */ }

        var timing = new GenerationTiming(
            Total: Nanos(done?.TotalDuration),
            Load: Nanos(done?.LoadDuration),
            PromptEval: Nanos(done?.PromptEvalDuration),
            Eval: Nanos(done?.EvalDuration),
            PromptTokens: done?.PromptEvalCount ?? 0,
            OutputTokens: done?.EvalCount ?? 0);

        return new GenerationResult(raw, json, timing);
    }

    /// <summary>Lists models already pulled on the server, so the bench can skip ones that are missing.</summary>
    public async Task<IReadOnlyList<string>> ListLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _client.ListLocalModelsAsync(cancellationToken);
        return models.Select(m => m.Name).ToList();
    }

    private static TimeSpan Nanos(long? ns) => ns is > 0 ? TimeSpan.FromTicks(ns.Value / 100) : TimeSpan.Zero;
}
