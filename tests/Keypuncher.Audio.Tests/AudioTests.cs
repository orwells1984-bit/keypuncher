using Keypuncher.Audio;
using Keypuncher.Core;

namespace Keypuncher.Audio.Tests;

public class AudioTests
{
    private static readonly Voice Pluck = new(Waveform.Square, 0.005, 0.5, "pentatonic", "C4");

    [Theory]
    [InlineData("C4", 60)]
    [InlineData("A4", 69)]
    [InlineData("F#3", 54)]
    [InlineData("nonsense", 60)]
    public void Note_names_parse_to_midi(string name, int midi) => Assert.Equal(midi, Notes.Parse(name));

    [Fact]
    public void Scale_degrees_climb_through_octaves()
    {
        Assert.Equal(60, Notes.Degree("pentatonic", 60, 0));
        Assert.Equal(69, Notes.Degree("pentatonic", 60, 4));
        Assert.Equal(72, Notes.Degree("pentatonic", 60, 5));
        Assert.Equal(440, Notes.Frequency(69), 6);
    }

    [Fact]
    public void Mixer_starts_silent_and_ramps_up()
    {
        var m = new Mixer { Volume = Mixer.MaxVolume };
        m.Note(440, Pluck, 1);
        var first = Render(m, 0.01);
        Assert.True(Peak(first) < 0.01, "the first 10 ms should be nearly silent while the start ramp runs");
    }

    [Fact]
    public void Many_loud_notes_never_exceed_the_ceiling()
    {
        var m = new Mixer { Volume = 5f }; // clamped to MaxVolume
        Assert.Equal(Mixer.MaxVolume, m.Volume);
        Render(m, 3.1); // past the start ramp
        for (var i = 0; i < 40; i++) m.Note(100 + (i * 37), Pluck with { Wave = Waveform.Saw }, 1);
        m.Speak(Enumerable.Repeat(1f, Mixer.SampleRate).ToArray());
        var loud = Render(m, 0.5);
        Assert.True(Peak(loud) <= 0.98f);
        // The limiter holds the level near its threshold rather than at the hard clamp.
        Assert.True(loud.Skip(Mixer.SampleRate / 5).Max(Math.Abs) < 0.75f);
    }

    [Fact]
    public void Speech_reports_when_it_is_done()
    {
        var m = new Mixer();
        m.Speak(new float[Mixer.SampleRate / 10]);
        Assert.True(m.SpeechPlaying);
        Render(m, 0.2);
        Assert.False(m.SpeechPlaying);
    }

    [Fact]
    public void Wav_round_trips_as_16_bit_mono()
    {
        var samples = Enumerable.Range(0, 480).Select(i => (float)Math.Sin(i / 10.0) * 0.5f).ToArray();
        using var ms = new MemoryStream();
        WavFile.Write(ms, samples, 48000);
        ms.Position = 0;
        var (mono, rate) = WavFile.Read(ms);
        Assert.Equal(48000, rate);
        Assert.Equal(samples.Length, mono.Length);
        Assert.All(samples.Zip(mono), p => Assert.InRange(Math.Abs(p.First - p.Second), 0, 1e-4));
    }

    [Fact]
    public void Recording_is_trimmed_and_levelled()
    {
        const int rate = 48000;
        var raw = new float[rate * 2];
        var noise = new Random(1);
        for (var i = 0; i < raw.Length; i++) raw[i] = (float)(noise.NextDouble() - 0.5) * 0.002f; // room hiss
        for (var i = rate / 2; i < rate; i++) raw[i] += (float)Math.Sin(i * 0.05) * 0.1f;            // half a second of "voice"

        var clip = ClipProcessing.TrimAndNormalize(raw, rate);

        Assert.InRange(clip.Length, rate / 2, (rate / 2) + (rate / 5));
        Assert.Equal(Math.Pow(10, -3 / 20.0), clip.Max(Math.Abs), 2);
        Assert.Empty(ClipProcessing.TrimAndNormalize(new float[rate], rate));
    }

    [Fact]
    public void Resample_keeps_duration()
    {
        var input = new float[44100];
        Assert.Equal(48000, ClipProcessing.Resample(input, 44100, 48000).Length);
    }

    [Theory]
    [InlineData(Shape.Cow)]
    [InlineData(Shape.Dog)]
    [InlineData(Shape.Cat)]
    [InlineData(Shape.Rex)]
    [InlineData(Shape.Whale)]
    [InlineData(Shape.Duck)]
    public void Known_animals_have_a_call(Shape shape) => Assert.NotNull(AnimalCalls.For(shape, gentle: false));

    [Theory]
    [InlineData(Shape.Blob)]
    [InlineData(Shape.Shark)]
    [InlineData(Shape.Rabbit)]
    [InlineData(Shape.Starfish)]
    public void Things_with_no_known_noise_stay_silent(Shape shape)
    {
        Assert.Null(AnimalCalls.For(shape, gentle: false));
        Assert.False(AnimalCalls.HasCall(shape));
    }

    [Fact]
    public void Every_call_is_short_bounded_and_soft_edged()
    {
        Assert.NotEmpty(AnimalCalls.Callers);
        foreach (var gentle in new[] { false, true })
            foreach (var shape in AnimalCalls.Callers)
            {
                var call = AnimalCalls.For(shape, gentle)!;
                Assert.All(call, s => Assert.True(float.IsFinite(s)));
                Assert.InRange(call.Length, Mixer.SampleRate / 10, Mixer.SampleRate * 5 / 2);
                Assert.InRange(Peak(call), 0.05f, AnimalCalls.MaxPeak + 1e-4f);
                Assert.True(Math.Abs(call[0]) < 0.01 && Math.Abs(call[^1]) < 0.01, $"{shape} should start and end in silence");
                Assert.True(call.Length / (double)Mixer.SampleRate < CalmLimits.BigToddler.CallGapSeconds, $"{shape} outlasts the gap between calls");
            }
    }

    [Theory]
    [InlineData(Shape.Rex)]
    [InlineData(Shape.Bear)]
    [InlineData(Shape.Elephant)]
    [InlineData(Shape.Dog)]
    public void Baby_calls_are_quieter_and_start_slower(Shape shape)
    {
        var bold = AnimalCalls.For(shape, gentle: false)!;
        var gentle = AnimalCalls.For(shape, gentle: true)!;
        Assert.True(Peak(gentle) < Peak(bold) * 0.8, $"{shape} should be clearly quieter for a baby");
        var opening = Mixer.SampleRate / 20;
        Assert.True(Rms(gentle.AsSpan(0, opening)) < Rms(bold.AsSpan(0, opening)), $"{shape} should not jump out for a baby");
    }

    [Fact]
    public void Roars_are_softest_of_all_for_babies() =>
        Assert.True(Peak(AnimalCalls.For(Shape.Rex, gentle: true)!) <= 0.5f * AnimalCalls.MaxPeak);

    [Fact]
    public void A_call_plays_on_its_own_channel_and_ends()
    {
        var m = new Mixer { Volume = Mixer.MaxVolume };
        Render(m, 3.1); // past the start ramp
        var moo = AnimalCalls.For(Shape.Cow, gentle: false)!;
        m.Call(moo);
        Assert.True(m.CallPlaying);
        Assert.True(Peak(Render(m, 0.5)) > 0.05);
        Render(m, (moo.Length / (double)Mixer.SampleRate) + 0.1);
        Assert.False(m.CallPlaying);
        Assert.False(m.SpeechPlaying);
    }

    [Theory]
    [InlineData(Waveform.Sine)]
    [InlineData(Waveform.Triangle)]
    public void A_warm_note_peaks_no_higher_than_a_plain_oscillator_would(Waveform wave)
    {
        var m = new Mixer { Volume = 0.5f };
        Render(m, 3.1); // past the start ramp
        m.Note(440, new Voice(wave, 0.005, 1.0, "pentatonic", "A4"), 1);
        var peak = Peak(Render(m, 0.5));
        // A plain oscillator at full velocity peaks at the note gain (0.3) times the volume; the harmonics must
        // not push past that, nor leave the note much quieter.
        Assert.InRange(peak, 0.12f, 0.152f);
    }

    [Theory]
    [InlineData(Waveform.Sine, true)]
    [InlineData(Waveform.Triangle, true)]
    [InlineData(Waveform.Square, false)]
    [InlineData(Waveform.Saw, false)]
    public void Sine_and_triangle_notes_fall_quickly_to_half_then_take_the_long_tail(Waveform wave, bool twoStage)
    {
        var m = new Mixer { Volume = 0.5f };
        Render(m, 3.1);
        m.Note(440, new Voice(wave, 0.005, 1.0, "pentatonic", "A4"), 1);
        var note = Render(m, 0.25);
        var early = Rms(note.AsSpan(Mixer.Channels * Mixer.SampleRate / 100, Mixer.Channels * Mixer.SampleRate / 50));
        var later = Rms(note.AsSpan(Mixer.Channels * Mixer.SampleRate / 5, Mixer.Channels * Mixer.SampleRate / 50));
        // One exponential over a 1 s decay keeps about 0.5 of its level from 20 ms to 200 ms; the two-stage
        // decay keeps only about half of that.
        var kept = later / early;
        if (twoStage) Assert.InRange(kept, 0.15, 0.32);
        else Assert.InRange(kept, 0.4, 0.6);
    }

    [Fact]
    public void A_swept_note_starts_high_and_falls_to_its_pitch()
    {
        static (int Early, int Late) Crossings(double sweepFrom)
        {
            var m = new Mixer { Volume = 0.5f };
            Render(m, 3.1);
            m.Note(150, new Voice(Waveform.Sine, 0.002, 0.5, "pentatonic", "D3"), 1, sweepFrom: sweepFrom, sweepSeconds: 0.12);
            var left = Render(m, 0.2).Where((_, i) => i % Mixer.Channels == 0).ToArray();
            int Count(int from, int to) => Enumerable.Range(from + 1, to - from - 1).Count(i => (left[i - 1] < 0) != (left[i] < 0));
            var ms = Mixer.SampleRate / 1000;
            return (Count(2 * ms, 22 * ms), Count(160 * ms, 180 * ms));
        }

        var swept = Crossings(6);
        var held = Crossings(1);
        Assert.True(swept.Early > 3 * swept.Late, $"swept: {swept.Early} crossings early, {swept.Late} late");
        Assert.InRange(swept.Late, held.Late - 1, held.Late + 1);   // it ends at the note's own pitch
        Assert.InRange(held.Early, held.Late - 1, held.Late + 1);   // and without a sweep the pitch holds
    }

    private static float[] Render(Mixer m, double seconds)


    {
        var buffer = new float[(int)(seconds * Mixer.SampleRate) * Mixer.Channels];
        m.Read(buffer);
        return buffer;
    }

    private static float Peak(float[] buffer) => buffer.Max(Math.Abs);

    private static double Rms(ReadOnlySpan<float> samples)
    {
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return Math.Sqrt(sum / samples.Length);
    }
}
