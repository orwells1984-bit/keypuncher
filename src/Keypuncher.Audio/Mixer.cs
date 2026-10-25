using System.Collections.Concurrent;
using Keypuncher.Core;

namespace Keypuncher.Audio;

/// <summary>
/// The whole audio path: a small polyphonic synth for recipe voices, one speech channel for recorded
/// or synthesised clips, a channel for animal calls, and a master stage that caps volume, ramps up gently at start and limits
/// peaks. Any thread may call the public methods; <see cref="Read"/> runs on the audio thread and is
/// the only place state changes, so there are no locks on the audio path.
/// </summary>
public sealed class Mixer
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    /// <summary>The loudest the parent can set; the limiter holds peaks below this anyway.</summary>
    public const float MaxVolume = 0.8f;

    private const int MaxVoices = 24;
    private const float NoteGain = 0.3f;
    private const float SpeechGain = 0.9f;
    /// <summary>Animal calls sit under speech, and duck under it like the notes do.</summary>
    private const float CallGain = 0.55f;
    private const float DuckWhileSpeaking = 0.45f;
    private const float LimiterThreshold = 0.5f;
    private static readonly float LimiterRelease = (float)Math.Exp(-1.0 / (0.2 * SampleRate));
    private static readonly int StartRampSamples = 3 * SampleRate;
    private static readonly int SpeechFadeSamples = SampleRate / 100;

    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly SynthVoice[] _voices = new SynthVoice[MaxVoices];
    private float[]? _speech;
    private int _speechPos;
    private float _speechPanL = 1, _speechPanR = 1;
    private float[]? _fadingSpeech;
    private int _fadingPos, _fadingLeft;
    private float[]? _call;
    private int _callPos;
    private float _callPanL = 1, _callPanR = 1;
    private float _limiterEnv;
    private long _samplesPlayed;
    private float _volume = 0.5f;
    private volatile bool _speechPlaying;
    private volatile bool _callPlaying;

    /// <summary>Master volume, 0 to 1, capped at <see cref="MaxVolume"/>.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0, MaxVolume);
    }

    public bool SpeechPlaying => _speechPlaying;

    public bool CallPlaying => _callPlaying;

    /// <summary>Plays one note of a recipe's voice.</summary>
    /// <param name="pan">-1 left to 1 right.</param>
    /// <param name="velocity">0 to 1.</param>
    public void Note(double frequency, Voice voice, double velocity = 0.8, double pan = 0)
    {
        var v = new SynthVoice(frequency, voice.Wave, voice.Attack, voice.Decay, velocity, pan);
        _commands.Enqueue(() => StartVoice(v));
    }

    /// <summary>Plays a mono clip at <see cref="SampleRate"/> on the speech channel, cutting off (with a
    /// short fade) whatever was being said.</summary>
    public void Speak(float[] samples, double pan = 0)
    {
        _speechPlaying = true;
        var (l, r) = PanGains(pan * 0.5);
        _commands.Enqueue(() =>
        {
            if (_speech is not null && _speechPos < _speech.Length)
            {
                _fadingSpeech = _speech;
                _fadingPos = _speechPos;
                _fadingLeft = SpeechFadeSamples;
            }
            _speech = samples;
            _speechPos = 0;
            (_speechPanL, _speechPanR) = (l, r);
        });
    }

    /// <summary>Plays a mono animal call at <see cref="SampleRate"/>. One call at a time: a new one waits
    /// for nothing and replaces the old, so callers should leave a gap between calls.</summary>
    public void Call(float[] samples, double pan = 0)
    {
        _callPlaying = true;
        var (l, r) = PanGains(pan * 0.6);
        _commands.Enqueue(() =>
        {
            _call = samples;
            _callPos = 0;
            (_callPanL, _callPanR) = (l, r);
        });
    }

    public void StopSpeech() => _commands.Enqueue(() =>
    {
        if (_speech is not null && _speechPos < _speech.Length)
        {
            _fadingSpeech = _speech;
            _fadingPos = _speechPos;
            _fadingLeft = SpeechFadeSamples;
        }
        _speech = null;
        _speechPlaying = false;
    });

    /// <summary>Fills <paramref name="buffer"/> with interleaved stereo floats and returns its length:
    /// the mixer never ends.</summary>
    public int Read(Span<float> buffer)
    {
        var count = buffer.Length;
        while (_commands.TryDequeue(out var command)) command();

        var volume = _volume;
        for (var i = 0; i < count; i += Channels)
        {
            float l = 0, r = 0;
            for (var v = 0; v < MaxVoices; v++)
            {
                ref var voice = ref _voices[v];
                if (!voice.Active) continue;
                var s = voice.Next();
                l += s * voice.PanL;
                r += s * voice.PanR;
            }

            float speech = 0;
            if (_speech is not null)
            {
                if (_speechPos < _speech.Length) speech = _speech[_speechPos++];
                else { _speech = null; _speechPlaying = false; }
            }
            if (_fadingSpeech is not null)
            {
                if (_fadingLeft > 0 && _fadingPos < _fadingSpeech.Length)
                    speech += _fadingSpeech[_fadingPos++] * (_fadingLeft-- / (float)SpeechFadeSamples);
                else _fadingSpeech = null;
            }

            float call = 0;
            if (_call is not null)
            {
                if (_callPos < _call.Length) call = _call[_callPos++] * CallGain;
                else { _call = null; _callPlaying = false; }
            }

            var duck = _speech is not null ? DuckWhileSpeaking : 1f;
            l = (((l * NoteGain) + (call * _callPanL)) * duck) + (speech * SpeechGain * _speechPanL);
            r = (((r * NoteGain) + (call * _callPanR)) * duck) + (speech * SpeechGain * _speechPanR);

            var ramp = _samplesPlayed < StartRampSamples ? _samplesPlayed / (float)StartRampSamples : 1f;
            _samplesPlayed++;
            var gain = volume * ramp;
            l *= gain;
            r *= gain;

            var peak = Math.Max(Math.Abs(l), Math.Abs(r));
            _limiterEnv = peak > _limiterEnv ? peak : (_limiterEnv * LimiterRelease) + (peak * (1 - LimiterRelease));
            if (_limiterEnv > LimiterThreshold)
            {
                var g = LimiterThreshold / _limiterEnv;
                l *= g;
                r *= g;
            }

            buffer[i] = Math.Clamp(l, -0.98f, 0.98f);
            if (i + 1 < count) buffer[i + 1] = Math.Clamp(r, -0.98f, 0.98f);
        }
        return count;
    }

    private void StartVoice(SynthVoice v)
    {
        var slot = -1;
        var quietest = float.MaxValue;
        for (var i = 0; i < MaxVoices; i++)
        {
            if (!_voices[i].Active) { slot = i; break; }
            if (_voices[i].Level < quietest) { quietest = _voices[i].Level; slot = i; }
        }
        _voices[slot] = v;
    }

    internal static (float L, float R) PanGains(double pan)
    {
        var angle = (Math.Clamp(pan, -1, 1) + 1) * Math.PI / 4;
        return ((float)(Math.Cos(angle) * Math.Sqrt(2)), (float)(Math.Sin(angle) * Math.Sqrt(2)));
    }

    /// <summary>One sounding note: an oscillator through a gentle low-pass, with a linear attack and an
    /// exponential decay. Square, saw and noise are filtered harder so nothing is harsh.</summary>
    private struct SynthVoice
    {
        public bool Active;
        public float PanL, PanR;
        public float Level;
        private readonly Waveform _wave;
        private readonly double _increment;
        private readonly int _attack;
        private readonly double _decayPerSample;
        private readonly float _velocity;
        private readonly float _lowpass;
        private double _phase;
        private int _t;
        private float _env;
        private float _filtered;
        private uint _noise;

        public SynthVoice(double frequency, Waveform wave, double attack, double decay, double velocity, double pan)
        {
            Active = true;
            (PanL, PanR) = PanGains(pan);
            _wave = wave;
            _increment = frequency / SampleRate;
            _attack = Math.Max(1, (int)(Math.Clamp(attack, 0.002, 0.5) * SampleRate));
            // About -35 dB at the end of the recipe's decay time.
            _decayPerSample = Math.Exp(-4.0 / (Math.Clamp(decay, 0.05, 2.0) * SampleRate));
            _velocity = (float)Math.Clamp(velocity, 0, 1);
            var cutoff = wave switch
            {
                Waveform.Sine => 12000.0,
                Waveform.Triangle => 6000.0,
                Waveform.Noise => 1500.0,
                _ => 2500.0,
            };
            _lowpass = (float)(1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate));
            _phase = 0;
            _t = 0;
            _env = 0;
            _filtered = 0;
            _noise = 0x9E3779B9u ^ (uint)(frequency * 1000);
            Level = 1;
        }

        public float Next()
        {
            float osc;
            switch (_wave)
            {
                case Waveform.Triangle: osc = (float)((4 * Math.Abs(_phase - 0.5)) - 1); break;
                case Waveform.Square: osc = _phase < 0.5 ? 0.6f : -0.6f; break;
                case Waveform.Saw: osc = (float)((2 * _phase) - 1) * 0.6f; break;
                case Waveform.Noise:
                    _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
                    osc = ((_noise & 0xFFFF) / 32768f) - 1f;
                    break;
                default: osc = (float)Math.Sin(2 * Math.PI * _phase); break;
            }
            _phase += _increment;
            if (_phase >= 1) _phase -= 1;

            if (_t < _attack) _env = _t / (float)_attack;
            else _env *= (float)_decayPerSample;
            _t++;

            _filtered += _lowpass * (osc - _filtered);
            Level = _env;
            if (_t > _attack && _env < 0.0005f) Active = false;
            return _filtered * _env * _velocity;
        }
    }
}
