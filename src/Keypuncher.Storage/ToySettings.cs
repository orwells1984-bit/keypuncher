using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Storage;

/// <summary>What the parent screen controls.</summary>
/// <param name="LanguageWeights">Share of key presses per language tag; Icelandic leads by default.</param>
/// <param name="UpperCase">Show letters as capitals (the keycaps) or small letters.</param>
/// <param name="Volume">Master volume 0..1; the mixer caps it lower.</param>
/// <param name="SayWords">After a letter, say a word that starts with it when the child pauses.</param>
/// <param name="Calm">How busy the screen may get; see <see cref="CalmLimits"/>.</param>
/// <param name="SessionMinutes">Play time before the goodnight: null for the calm level's default, 0 for never.</param>
public sealed record ToySettings(
    Dictionary<string, double> LanguageWeights,
    bool UpperCase = true,
    float Volume = 0.5f,
    bool SayWords = true,
    CalmLevel Calm = CalmLevel.Toddler,
    int? SessionMinutes = null)
{
    public static ToySettings Default => new(new Dictionary<string, double> { ["is"] = 0.7, ["en"] = 0.15, ["bg"] = 0.15 });

    public CalmLimits Limits => CalmLimits.For(Calm);

    /// <summary>The goodnight comes after this much play; null means never.</summary>
    public TimeSpan? SessionLength => (SessionMinutes ?? Limits.SessionMinutes) is > 0 and var m ? TimeSpan.FromMinutes(m) : null;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ToySettings Load()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<ToySettings>(File.ReadAllText(path), Options) is { } s)
                return s;
        }
        catch (JsonException) { /* a damaged file falls back to defaults */ }
        return ToySettings.Default;
    }

    public void Save(ToySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
