using System.Collections.Concurrent;
using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Inference;
using Keypuncher.Storage;

namespace Keypuncher.Toy;

/// <summary>
/// Finds something to say for a clip, in the plan's order: Vanya's recording, then a synthetic voice
/// for the clip's fallback text (numbers and words only; voices cannot say letter sounds). Lookups
/// never block the reflex loop: a clip that is not in memory yet starts loading and is played next time.
/// </summary>
public sealed class SpeechLibrary(ClipStore clips, ISpeech? fallbackVoice)
{
    private readonly ConcurrentDictionary<string, float[]?> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _loading = new();

    /// <summary>Returns the samples at <see cref="Mixer.SampleRate"/> if they are ready, otherwise starts
    /// loading them and returns null.</summary>
    public float[]? TryGet(ClipId id, string? fallbackText)
    {
        var key = id.ToString();
        if (_cache.TryGetValue(key, out var recorded) && recorded is not null) return recorded;

        if (fallbackText is not null && _cache.TryGetValue(VoiceKey(id.Language, fallbackText), out var voiced) && voiced is not null)
            return voiced;

        if (!_cache.ContainsKey(key)) Load(id);
        if (fallbackText is not null && fallbackVoice is not null && !clips.Exists(id)) Synthesize(id.Language, fallbackText);
        return null;
    }

    /// <summary>Loads every recorded clip in the background so that the first press of each key already speaks.</summary>
    public Task PreloadAsync(IEnumerable<ClipId> ids) =>
        Task.Run(() => { foreach (var id in ids) if (clips.Exists(id)) LoadNow(id); });

    /// <summary>Forgets a clip after it was re-recorded or deleted.</summary>
    public void Invalidate(ClipId id) => _cache.TryRemove(id.ToString(), out _);

    private void Load(ClipId id)
    {
        if (!_loading.TryAdd(id.ToString(), 0)) return;
        Task.Run(() =>
        {
            try { LoadNow(id); }
            finally { _loading.TryRemove(id.ToString(), out _); }
        });
    }

    private void LoadNow(ClipId id)
    {
        float[]? samples = null;
        try
        {
            if (clips.Exists(id))
            {
                using var s = clips.OpenRead(id);
                var (mono, rate) = WavFile.Read(s);
                samples = ClipProcessing.Resample(mono, rate, Mixer.SampleRate);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A damaged or locked file behaves like a missing one.
            Log.Warn("speech", $"the recording {id} could not be read; it stays silent", e);
        }
        _cache[id.ToString()] = samples;
    }

    private void Synthesize(string language, string text)
    {
        var key = VoiceKey(language, text);
        if (_cache.ContainsKey(key) || !_loading.TryAdd(key, 0)) return;
        Task.Run(async () =>
        {
            float[]? samples = null;
            try
            {
                if (await fallbackVoice!.SpeakAsync(language, text) is { } wav)
                {
                    using var s = new MemoryStream(wav);
                    var (mono, rate) = WavFile.Read(s);
                    samples = ClipProcessing.Resample(mono, rate, Mixer.SampleRate);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // No voice for this language, or the voice failed: stay silent rather than retry every press.
                Log.Warn("speech", $"the {language} voice could not say \"{text}\"; it stays silent", e);
            }
            finally
            {
                _cache[key] = samples;
                _loading.TryRemove(key, out _);
            }
        });
    }

    private static string VoiceKey(string language, string text) => $"voice/{language}/{text}";
}
