using Keypuncher.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Keypuncher.Platform.Windows;

/// <summary>Plays the <see cref="Mixer"/> through WASAPI in shared mode at 48 kHz stereo float, with a
/// short buffer so a key press is heard within a few tens of milliseconds.</summary>
public sealed class AudioOutput : IDisposable
{
    private readonly WasapiPlayer _player;

    public AudioOutput(Mixer mixer, int latencyMilliseconds = 30)
    {
        _player = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithLowLatency(true)
            .WithLatency(latencyMilliseconds)
            .Build();
        _player.Init(new SampleToWaveProvider(new MixerSampleProvider(mixer)));
        _player.Play();
    }

    /// <summary>The latency WASAPI actually gave us, for the parent screen.</summary>
    public int LatencyMilliseconds => _player.LatencyMilliseconds;

    public void Dispose()
    {
        _player.Stop();
        _player.Dispose();
    }

    private sealed class MixerSampleProvider(Mixer mixer) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Mixer.SampleRate, Mixer.Channels);

        public int Read(Span<float> buffer) => mixer.Read(buffer);
    }
}
