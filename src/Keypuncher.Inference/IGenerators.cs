namespace Keypuncher.Inference;

/// <summary>Speech for letters, numbers and words: Vanya's recordings first, Piper, then Windows voices.</summary>
public interface ISpeech
{
    Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default);
}
