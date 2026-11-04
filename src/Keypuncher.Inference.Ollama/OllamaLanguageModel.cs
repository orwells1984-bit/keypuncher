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
    /// <summary>How long one call may take, loading the model included. HttpClient's default of 100 s was too short
    /// for a cold load on the desktop (a 1060 loading qwen3:4b from disk), and the timeout looked like a silent cancel.</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long Ollama keeps the model loaded after a call. Its default of 5 minutes let the model unload
    /// between the director's requests, so each one paid the load again.</summary>
    public const string KeepAlive = "30m";

    private readonly OllamaApiClient _client;
    private readonly int _numCtx;
    private readonly float _temperature;

    public OllamaLanguageModel(Uri baseUri, string modelName, int numCtx = 4096, float temperature = 0.8f)
    {
        _client = new OllamaApiClient(new HttpClient { BaseAddress = baseUri, Timeout = CallTimeout }, modelName);
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
            KeepAlive = KeepAlive,
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

    /// <summary>Loads the model into memory without generating anything (Ollama loads on an empty prompt), so the
    /// first real request does not wait for the load. Returns how long the load took.</summary>
    public async Task<TimeSpan> WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var request = new GenerateRequest { Model = ModelName, Prompt = "", Stream = true, KeepAlive = KeepAlive };
        await foreach (var _ in _client.GenerateAsync(request, cancellationToken)) { }
        return started.Elapsed;
    }

    /// <summary>Lists models already pulled on the server, so the bench can skip ones that are missing.</summary>
    public async Task<IReadOnlyList<string>> ListLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _client.ListLocalModelsAsync(cancellationToken);
        return models.Select(m => m.Name).ToList();
    }

    private static TimeSpan Nanos(long? ns) => ns is > 0 ? TimeSpan.FromTicks(ns.Value / 100) : TimeSpan.Zero;
}
