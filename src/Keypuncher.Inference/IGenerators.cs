namespace Keypuncher.Inference;

/// <summary>Tier B: a diffusion model that paints a background or texture. Desktop only.</summary>
public interface IImageGenerator
{
    Task<byte[]> GenerateAsync(string prompt, int width, int height, CancellationToken cancellationToken = default);
}

/// <summary>Tier B: a text-to-audio model that produces an ambient loop.</summary>
public interface IAudioGenerator
{
    Task<byte[]> GenerateAsync(string prompt, TimeSpan length, CancellationToken cancellationToken = default);
}

/// <summary>Speech for letters, numbers and words: Vanya's recordings first, Piper, then Windows voices.</summary>
public interface ISpeech
{
    Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default);
}
