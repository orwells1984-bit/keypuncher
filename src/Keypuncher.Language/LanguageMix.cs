namespace Keypuncher.Language;

/// <summary>How often each language is picked for a play session. Icelandic leads; the parent sets the rest.</summary>
public sealed record LanguageMix(IReadOnlyDictionary<string, double> Weights)
{
    public static LanguageMix Default { get; } = new(new Dictionary<string, double>
    {
        [KeyMeanings.Icelandic] = 0.7,
        [KeyMeanings.English] = 0.15,
        [KeyMeanings.Bulgarian] = 0.15,
    });

    public string Pick(Random random)
    {
        var total = Weights.Values.Where(w => w > 0).Sum();
        if (total <= 0) return KeyMeanings.Icelandic;

        var r = random.NextDouble() * total;
        foreach (var (tag, w) in Weights)
        {
            if (w <= 0) continue;
            if (r < w) return tag;
            r -= w;
        }
        return Weights.Last(kv => kv.Value > 0).Key;
    }
}
