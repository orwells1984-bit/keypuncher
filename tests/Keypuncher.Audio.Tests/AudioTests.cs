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
