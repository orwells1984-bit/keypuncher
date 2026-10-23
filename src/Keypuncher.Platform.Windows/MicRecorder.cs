using NAudio.Wave;

namespace Keypuncher.Platform.Windows;

/// <summary>Records mono 48 kHz from the default microphone for the recording screen.</summary>
public sealed class MicRecorder : IDisposable
{
    public const int SampleRate = 48000;
    private readonly List<float> _samples = [];
    private WaveIn? _wave;

    /// <summary>Peak level 0..1 of each 50 ms buffer, for a level meter. Raised on a background thread.</summary>
    public event Action<float>? Level;

    public bool Recording => _wave is not null;

    public void Start()
    {
        if (_wave is not null) return;
        lock (_samples) _samples.Clear();
        _wave = new WaveIn { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 50 };
        _wave.DataAvailable += OnData;
        _wave.StartRecording();
    }

    /// <summary>Stops and returns everything recorded since <see cref="Start"/>.</summary>
    public float[] Stop()
    {
        if (_wave is null) return [];
        _wave.StopRecording();
        _wave.DataAvailable -= OnData;
        _wave.Dispose();
        _wave = null;
        lock (_samples) return [.. _samples];
    }

    public void Dispose() => Stop();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float peak = 0;
        lock (_samples)
        {
            var bytes = e.BufferSpan[..e.BytesRecorded];
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                var s = BitConverter.ToInt16(bytes[i..]) / 32768f;
                _samples.Add(s);
                peak = Math.Max(peak, Math.Abs(s));
            }
        }
        Level?.Invoke(peak);
    }
}
