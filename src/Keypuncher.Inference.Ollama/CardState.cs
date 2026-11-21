namespace Keypuncher.Inference.Ollama;

/// <summary>A model Ollama holds in memory now.</summary>
/// <param name="Name">As Ollama names it, tag included.</param>
/// <param name="SizeVram">Bytes of it on the graphics card.</param>
public sealed record LoadedModel(string Name, long SizeVram);

/// <summary>
/// What the graphics card and Ollama are doing just before the toy loads its model. On 2026-11-21 another job held
/// Ollama with its own model all through a play: the toy's requests queued behind it, one answer came in six
/// minutes, and only the log said why. The toy asks at start-up and warns the grown-up before play.
/// </summary>
/// <param name="OllamaReachable">False when Ollama did not say what it has loaded; then only the card's free memory is known.</param>
/// <param name="Loaded">Every model Ollama holds in memory.</param>
/// <param name="FreeMiB">The card's free memory, or null when it cannot be asked (no nvidia-smi).</param>
public sealed record CardState(bool OllamaReachable, IReadOnlyList<LoadedModel> Loaded, int? FreeMiB)
{
    /// <summary>Nothing known: no warning comes of it.</summary>
    public static readonly CardState Unknown = new(false, [], null);

    /// <summary>What the director's model is taken to need on the card when Ollama does not give its size:
    /// qwen3:4b takes 3.2 GB loaded.</summary>
    public const int DefaultNeededMiB = 3300;

    private const double MiB = 1024 * 1024;

    /// <summary>Roughly what a model of <paramref name="bytesOnDisk"/> needs on the card: its weights, and about a
    /// third again for its working memory (qwen3:4b is 2.5 GB on disk and 3.2 GB loaded).</summary>
    public static int NeededMiB(long? bytesOnDisk) =>
        bytesOnDisk is > 0 ? (int)Math.Ceiling(bytesOnDisk.Value * 1.3 / MiB) : DefaultNeededMiB;

    /// <summary>The loaded models that are not the toy's own: another job is using Ollama, and as Ollama holds one
    /// model at a time the toy's requests will wait behind it.</summary>
    public IReadOnlyList<string> Others(IEnumerable<string> ours) =>
        [.. Loaded.Where(m => !ours.Any(o => OllamaModels.Same(o, m.Name))).Select(m => m.Name)];

    /// <summary>Why the director's model will be slow or silent, in words for a grown-up; null when all looks well
    /// or nothing is known.</summary>
    /// <param name="director">The model that chooses real things.</param>
    /// <param name="ours">Every model the toy uses; Ollama swaps between these itself, so one of them being
    /// loaded is no trouble and its memory counts as free.</param>
    /// <param name="directorBytesOnDisk">The director model's size as Ollama lists it, when known.</param>
    public string? Trouble(string director, IReadOnlyCollection<string> ours, long? directorBytesOnDisk = null)
    {
        var others = Others(ours);
        if (others.Count > 0)
            return $"Ollama is running {string.Join(" and ", others)} for something else. Ollama keeps one model in memory at a time, "
                 + $"so the toy's model ({director}) waits its turn behind every answer of that job, and new real things and places come rarely or not at all.";
        // The director's model already loaded (a play a few minutes ago) needs no room.
        if (Loaded.Any(m => OllamaModels.Same(m.Name, director)) || FreeMiB is not { } free) return null;
        var mine = (int)(Loaded.Where(m => ours.Any(o => OllamaModels.Same(o, m.Name))).Sum(m => m.SizeVram) / MiB);
        var needed = NeededMiB(directorBytesOnDisk);
        if (free + mine >= needed) return null;
        // English words, so English numbers whatever the computer's own language.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"Only {(free + mine).ToString("N0", inv)} MiB of the graphics card's memory is free, and the toy's model ({director}) needs about {needed.ToString("N0", inv)} MiB. "
            + "Another program is using the card. The model then loads slowly, runs partly on the processor, or does not answer, "
            + "and new real things and places come rarely or not at all.";
    }
}
