using System.Net.Http.Json;
using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Inference.Ollama;

/// <summary>A model Ollama has on this computer.</summary>
/// <param name="Name">As Ollama names it, tag included ("qwen3:4b").</param>
/// <param name="Size">Bytes on disk.</param>
/// <param name="Capabilities">What Ollama says it can do ("completion", "vision", "embedding"); empty when Ollama
/// did not say, which is then taken to mean anything.</param>
public sealed record InstalledModel(string Name, long Size, IReadOnlyList<string> Capabilities)
{
    /// <summary>Whether it can write text; false only when Ollama lists what it can do and this is not there.</summary>
    public bool Writes => Capabilities.Count == 0 || Capabilities.Contains("completion");

    /// <summary>Whether it can be shown a picture; false only when Ollama lists what it can do and this is not there.</summary>
    public bool Sees => Capabilities.Count == 0 || Capabilities.Contains("vision");
}

/// <summary>What a look at Ollama's models found.</summary>
/// <param name="Reachable">False when Ollama did not answer; then nothing is known and nothing is called missing.</param>
/// <param name="Installed">Every model Ollama has.</param>
/// <param name="Missing">Those of the wanted models Ollama does not have, in the order asked.</param>
public sealed record ModelCheck(bool Reachable, IReadOnlyList<InstalledModel> Installed, IReadOnlyList<string> Missing)
{
    public static readonly ModelCheck Unreachable = new(false, [], []);
}

/// <summary>How far a download has come, summed over the model's files.</summary>
/// <param name="Status">Ollama's own word for the step ("pulling manifest", "verifying sha256 digest").</param>
public sealed record PullProgress(string Model, string Status, long Completed, long Total);

/// <summary>
/// The models an Ollama server has, and downloading one it lacks. The toy asks at start-up whether the models it
/// wants are there: on 2026-11-11 both had been removed by a clean-up, and every request of a whole play came back
/// 404 with nothing on screen to say why.
/// </summary>
/// <param name="http">Its BaseAddress is the server. A download takes minutes, so it should have no timeout of its
/// own; the short calls here bring theirs.</param>
public sealed class OllamaModels(HttpClient http)
{
    /// <summary>How long a list or a look at one model may take; Ollama answers these from memory.</summary>
    public static readonly TimeSpan ShortCall = TimeSpan.FromSeconds(5);

    /// <summary>Whether two names mean the same model: case does not matter, and no tag means ":latest".</summary>
    public static bool Same(string a, string b) => string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="wanted"/> is among <paramref name="installed"/>.</summary>
    public static bool Has(IEnumerable<InstalledModel> installed, string wanted) => installed.Any(m => Same(m.Name, wanted));

    private static string Full(string name)
    {
        name = name.Trim();
        // A tag follows the last colon, unless that colon belongs to a registry's port ("host:5000/model").
        var colon = name.LastIndexOf(':');
        return colon > name.LastIndexOf('/') ? name : name + ":latest";
    }

    /// <summary>Every model Ollama has, with what each can do when <paramref name="withCapabilities"/> is set (one
    /// more short call a model). Throws when Ollama does not answer.</summary>
    public async Task<IReadOnlyList<InstalledModel>> ListAsync(bool withCapabilities = false, CancellationToken cancellationToken = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(ShortCall);
        using var doc = JsonDocument.Parse(await http.GetStringAsync("api/tags", limit.Token).ConfigureAwait(false));
        var found = new List<InstalledModel>();
        if (doc.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            foreach (var m in models.EnumerateArray())
            {
                var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                found.Add(new InstalledModel(name, m.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : 0, []));
            }
        if (!withCapabilities) return found;
        var able = await Task.WhenAll(found.Select(m => CapabilitiesAsync(m.Name, cancellationToken))).ConfigureAwait(false);
        return [.. found.Select((m, i) => m with { Capabilities = able[i] })];
    }

    /// <summary>What Ollama says one model can do, from <c>/api/show</c>; empty when it does not say or does not answer.</summary>
    public async Task<IReadOnlyList<string>> CapabilitiesAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(ShortCall);
            using var response = await http.PostAsJsonAsync("api/show", new { model = name }, limit.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("capabilities", out var list) || list.ValueKind != JsonValueKind.Array) return [];
            return [.. list.EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c.Length > 0)];
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested && e is HttpRequestException or JsonException or OperationCanceledException)
        {
            Log.Debug("ollama", $"could not ask what {name} can do: {e.Message}");
            return [];
        }
    }

    /// <summary>Looks whether the <paramref name="wanted"/> models are there. Never throws: an Ollama that does not
    /// answer gives <see cref="ModelCheck.Unreachable"/>.</summary>
    public async Task<ModelCheck> CheckAsync(IEnumerable<string> wanted, bool withCapabilities = false, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InstalledModel> installed;
        try { installed = await ListAsync(withCapabilities, cancellationToken).ConfigureAwait(false); }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException)
        {
            Log.Warn("ollama", $"Ollama did not list its models at {http.BaseAddress}; is it running?", e);
            return ModelCheck.Unreachable;
        }
        var missing = wanted.Where(w => !Has(installed, w)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new ModelCheck(true, installed, missing);
    }

    /// <summary>Downloads a model, as <c>ollama pull</c> does, telling <paramref name="progress"/> how far it is.
    /// Throws <see cref="InvalidOperationException"/> with Ollama's own words when it refuses (no such model, no
    /// internet, no room on the disk).</summary>
    public async Task PullAsync(string name, IProgress<PullProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Log.Info("ollama", $"downloading {name} through Ollama at {http.BaseAddress}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/pull") { Content = JsonContent.Create(new { model = name, stream = true }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorIn(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) ?? $"Ollama answered {(int)response.StatusCode}");

        // One line of JSON a step. A model is several files, each with its own total; they are summed.
        var files = new Dictionary<string, (long Completed, long Total)>();
        var succeeded = false;
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (doc)
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException(error.GetString() ?? "Ollama reported an error without words");
                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                if (status == "success") succeeded = true;
                if (root.TryGetProperty("digest", out var d) && d.GetString() is { Length: > 0 } digest
                    && root.TryGetProperty("total", out var t) && t.TryGetInt64(out var total))
                    files[digest] = (root.TryGetProperty("completed", out var c) && c.TryGetInt64(out var completed) ? completed : 0, total);
                progress?.Report(new PullProgress(name, status, files.Values.Sum(f => f.Completed), files.Values.Sum(f => f.Total)));
            }
        }
        if (!succeeded) throw new InvalidOperationException("the download ended before Ollama said it was done");
        Log.Info("ollama", $"{name} downloaded ({files.Values.Sum(f => f.Total) / 1e9:0.0} GB)");
    }

    private static string? ErrorIn(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
