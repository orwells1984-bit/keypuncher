using System.Diagnostics;
using System.Text.Json;
using Keypuncher.Core;
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

    private readonly HttpClient _http;
    private readonly OllamaApiClient _client;
    private readonly int _numCtx;
    private readonly float _temperature;
    private readonly bool _think;
    private readonly string _role;
    private readonly HashSet<string> _systemsLogged = [];
    private static int _calls;

    /// <param name="think">Whether a reasoning model (qwen3) may think before it answers. Off for the toy: the bench
    /// measured 60 to 130 s a recipe with it on. The bench turns it on to see what it buys the checker.</param>
    /// <param name="role">What this model is used for ("author", "checker"), so the log can tell the calls apart.</param>
    public OllamaLanguageModel(Uri baseUri, string modelName, int numCtx = 4096, float temperature = 0.8f, bool think = false, string? role = null)
    {
        _role = role ?? "model";
        _http = new HttpClient { BaseAddress = baseUri, Timeout = CallTimeout };
        _client = new OllamaApiClient(_http, modelName);
        ModelName = modelName;
        _numCtx = numCtx;
        _temperature = temperature;
        _think = think;
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
            Think = _think,
            KeepAlive = KeepAlive,
            Options = new RequestOptions { NumCtx = _numCtx, Temperature = _temperature },
        };

        var call = $"#{Interlocked.Increment(ref _calls)} {_role} ({ModelName})";
        Log.Info("ollama", $"{call} asking: system prompt {systemPrompt.Length} chars, prompt {userPrompt.Length} chars, temperature {_temperature}, think {(_think ? "on" : "off")}");
        // The system prompts are the same every time; each is logged in full once per run, the prompt every time.
        bool firstSystem;
        lock (_systemsLogged) firstSystem = _systemsLogged.Add(systemPrompt);
        if (firstSystem) Log.Debug("ollama", $"{call} system prompt:{Environment.NewLine}{systemPrompt}");
        Log.Debug("ollama", $"{call} prompt:{Environment.NewLine}{userPrompt}");

        var text = new System.Text.StringBuilder();
        GenerateDoneResponseStream? done = null;
        var clock = Stopwatch.StartNew();
        TimeSpan? firstToken = null;
        try
        {
            await foreach (var chunk in _client.GenerateAsync(request, cancellationToken))
            {
                if (chunk is null) continue;
                firstToken ??= clock.Elapsed;
                text.Append(chunk.Response);
                if (chunk is GenerateDoneResponseStream d) done = d;
            }
        }
        catch (OperationCanceledException e) when (cancellationToken.IsCancellationRequested)
        {
            Log.Info("ollama", $"{call} cancelled after {clock.Elapsed.TotalSeconds:0.0} s (the toy is closing or the request was given up)", e);
            throw;
        }
        catch (OperationCanceledException e)
        {
            // HttpClient's timeout surfaces as a cancellation nobody asked for.
            Log.Error("ollama", $"{call} no answer within {CallTimeout.TotalMinutes:0} min (gave up after {clock.Elapsed.TotalSeconds:0.0} s{(firstToken is { } f ? $"; first words came at {f.TotalSeconds:0.0} s" : "; not a word came back")}). Is the model loading, or is something else using the card?", e);
            throw;
        }
        catch (Exception e)
        {
            Log.Error("ollama", $"{call} failed after {clock.Elapsed.TotalSeconds:0.0} s, {text.Length} chars received", e);
            throw;
        }

        var raw = text.ToString();
        JsonDocument? json = null;
        try { json = JsonDocument.Parse(raw); }
        catch (JsonException e)
        {
            // The caller treats null as a failed generation.
            Log.Warn("ollama", $"{call} answer is not JSON: {Log.Short(raw, 500)}", e);
        }
        if (done is null) Log.Warn("ollama", $"{call} the answer ended without Ollama's closing message (no timings)");

        var timing = new GenerationTiming(
            Total: Nanos(done?.TotalDuration),
            Load: Nanos(done?.LoadDuration),
            PromptEval: Nanos(done?.PromptEvalDuration),
            Eval: Nanos(done?.EvalDuration),
            PromptTokens: done?.PromptEvalCount ?? 0,
            OutputTokens: done?.EvalCount ?? 0);

        Log.Info("ollama", $"{call} answered in {clock.Elapsed.TotalSeconds:0.0} s (load {timing.Load.TotalSeconds:0.0} s, "
            + $"reading {timing.PromptTokens} tokens {timing.PromptEval.TotalSeconds:0.0} s, writing {timing.OutputTokens} tokens {timing.Eval.TotalSeconds:0.0} s, "
            + $"{timing.OutputTokensPerSecond:0} tokens/s): {Log.Short(raw, 400)}");
        if (raw.Length > 400) Log.Debug("ollama", $"{call} whole answer:{Environment.NewLine}{raw}");
        return new GenerationResult(raw, json, timing);
    }

    /// <summary>Loads the model into memory without generating anything (Ollama loads on an empty prompt), so the
    /// first real request does not wait for the load. Returns how long the load took.</summary>
    public async Task<TimeSpan> WarmUpAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        Log.Info("ollama", $"loading {ModelName} at {_http.BaseAddress} (context {_numCtx}, keep_alive {KeepAlive})");
        // The same context size as the real calls, or Ollama loads it once now and again on the first call.
        var request = new GenerateRequest { Model = ModelName, Prompt = "", Stream = true, KeepAlive = KeepAlive, Options = new RequestOptions { NumCtx = _numCtx } };
        try
        {
            await foreach (var _ in _client.GenerateAsync(request, cancellationToken)) { }
        }
        catch (Exception e)
        {
            Log.Error("ollama", $"loading {ModelName} failed after {started.Elapsed.TotalSeconds:0.0} s", e);
            throw;
        }
        Log.Info("ollama", $"{ModelName} loaded in {started.Elapsed.TotalSeconds:0.0} s");
        return started.Elapsed;
    }

    /// <summary>Asks Ollama to drop the model from memory now (a call with <c>keep_alive</c> 0), so the next call pays
    /// a cold load; the bench uses it to measure that load.</summary>
    public async Task UnloadAsync(CancellationToken cancellationToken = default)
    {
        var request = new GenerateRequest { Model = ModelName, Prompt = "", Stream = true, KeepAlive = "0" };
        await foreach (var _ in _client.GenerateAsync(request, cancellationToken)) { }
    }

    /// <summary>The names of every model Ollama has loaded now, from <c>/api/ps</c>; the bench will not start while
    /// another job's model is in memory, since with one model at a time the two would queue behind each other.</summary>
    public async Task<IReadOnlyList<string>> LoadedModelsAsync(CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("api/ps", cancellationToken));
        if (!doc.RootElement.TryGetProperty("models", out var models)) return [];
        return [.. models.EnumerateArray().Select(m => m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Where(n => n.Length > 0)];
    }

    /// <summary>How much memory the loaded model takes and how much of it is on the GPU, from <c>/api/ps</c>; null
    /// if it is not loaded. On a 6 GB card a model that does not fit shows SizeVram below Size and runs partly on
    /// the CPU, several times slower.</summary>
    public async Task<(long Size, long SizeVram)?> LoadedSizeAsync(CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("api/ps", cancellationToken));
        if (!doc.RootElement.TryGetProperty("models", out var models)) return null;
        foreach (var m in models.EnumerateArray())
        {
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name != ModelName && name != ModelName + ":latest") continue;
            return (m.TryGetProperty("size", out var size) ? size.GetInt64() : 0, m.TryGetProperty("size_vram", out var vram) ? vram.GetInt64() : 0);
        }
        return null;
    }

    /// <summary>The Ollama server's version, from <c>/api/version</c>; for the log.</summary>
    public async Task<string?> ServerVersionAsync(CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("api/version", cancellationToken));
        return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
    }

    /// <summary>Lists models already pulled on the server, so the bench can skip ones that are missing.</summary>
    public async Task<IReadOnlyList<string>> ListLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await _client.ListLocalModelsAsync(cancellationToken);
        return models.Select(m => m.Name).ToList();
    }

    private static TimeSpan Nanos(long? ns) => ns is > 0 ? TimeSpan.FromTicks(ns.Value / 100) : TimeSpan.Zero;
}
