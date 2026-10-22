using Keypuncher.Inference;
using Windows.Media.SpeechSynthesis;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// The last voice in the plan's order: whatever Windows speech voices are installed. Used only for
/// numbers and words that have no recording yet. Returns null for a language with no installed voice
/// (Icelandic usually has none; Bulgarian is worth checking in Settings, Time and language, Speech).
/// </summary>
public sealed class WindowsVoices : ISpeech
{
    public async Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default)
    {
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v =>
            v.Language.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase));
        if (voice is null) return null;

        using var synth = new SpeechSynthesizer { Voice = voice };
        using var stream = await synth.SynthesizeTextToStreamAsync(text).AsTask(cancellationToken);
        using var input = stream.AsStreamForRead();
        using var copy = new MemoryStream();
        await input.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    /// <summary>Installed voice languages, for the parent screen.</summary>
    public static IReadOnlyList<string> InstalledLanguages() =>
        [.. SpeechSynthesizer.AllVoices.Select(v => v.Language).Distinct().Order()];
}
