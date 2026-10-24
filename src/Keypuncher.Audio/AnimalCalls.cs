using System.Collections.Concurrent;
using Keypuncher.Core;

namespace Keypuncher.Audio;

/// <summary>
/// The noise each animal is known for (moo, woof, roar, whale song), made here from oscillators, formants
/// and filtered noise rather than from recordings, in keeping with the toy making its own sounds. Animals
/// with no noise a child would know (shark, octopus, rabbit, starfish...) have none.
/// <para>Calls are mono clips at <see cref="Mixer.SampleRate"/>, peaking at most at <see cref="MaxPeak"/>,
/// with soft edges so they never click. The gentle versions, for babies, are quieter and slower to start,
/// and the big ones (roar, growl, trumpet) are softer and darker still: nothing should startle.</para>
/// <para>Each call is made once and kept; <see cref="Warm"/> makes them all on a background thread so the
/// first one costs nothing at play time. Any thread may call any method.</para>
/// </summary>
public static class AnimalCalls
{
    public const float MaxPeak = 0.8f;
    private const int Rate = Mixer.SampleRate;
    /// <summary>Spectral envelopes are worked out this often, in samples; formants move slowly next to that.</summary>
    private const int EnvelopeStep = 64;

    private static readonly ConcurrentDictionary<(Shape, bool), float[]?> Cache = new();

    /// <summary>The shapes that have a call, in <see cref="Shape"/> order.</summary>
    public static IReadOnlyList<Shape> Callers { get; } = Enum.GetValues<Shape>().Where(s => Design(s, false) is not null).ToList();

    public static bool HasCall(Shape shape) => Callers.Contains(shape);

    /// <summary>The call for <paramref name="shape"/>, or null if it has none.</summary>
    /// <param name="gentle">The baby version: quieter, slower to start, big noises softened.</param>
    public static float[]? For(Shape shape, bool gentle) => Cache.GetOrAdd((shape, gentle), k => Make(k.Item1, k.Item2));

    /// <summary>Makes every call ahead of time on the thread pool.</summary>
    public static Task Warm(bool gentle) => Task.Run(() =>
    {
        foreach (var shape in Callers) For(shape, gentle);
    });

    private static float[]? Make(Shape shape, bool gentle)
    {
        if (Design(shape, gentle) is not { } call) return null;
        var samples = call.Render();
        Finish(samples, call.Loudness * (gentle ? (call.Big ? 0.5f : 0.75f) : 1f), gentle ? 0.06 : 0.012);
        if (gentle) Darken(samples, call.Big ? 1800 : 3500);
        return samples;
    }

    /// <summary>A call: how it is made, how loud it ends up next to the others, and whether it is a big
    /// noise that babies get much softer.</summary>
    private sealed record Call(Func<float[]> Render, float Loudness, bool Big = false);

    private static Call? Design(Shape shape, bool gentle) => shape switch
    {
        // Moo: lips closed, then opening to a long low "oo" that rises a little and falls away.
        Shape.Cow => new(() => Voiced(1.4,
            pitch: t => 112 + (22 * Hump(t, 0.3)) - (14 * t),
            loud: t => Envelope(t, 0.14, 0.3),
            vowel: t => t < 0.18 ? Blend(Hum, Oo, t / 0.18) : Blend(Oo, Oh, (t - 0.18) / 0.82 * 0.4),
            breath: 0.04), 0.9f),

        // Baa: a bleat, the voice shaking quickly on a bright "aa".
        Shape.Sheep => new(() => Voiced(0.9,
            pitch: t => 330 * (1 + (0.05 * Math.Sin(2 * Math.PI * 7 * t * 0.9))) * (1 - (0.08 * t)),
            loud: t => Envelope(t, 0.06, 0.35) * (0.7 + (0.3 * Math.Sin(2 * Math.PI * 9 * t * 0.9))),
            vowel: t => Blend(Ae, Ah, t),
            breath: 0.08), 0.75f),

        // Meow: "mee-ow", up then down.
        Shape.Cat => new(() => Voiced(0.8,
            pitch: t => 430 + (190 * Hump(t, 0.4)) - (60 * t),
            loud: t => Envelope(t, 0.1, 0.35),
            vowel: t => t < 0.12 ? Blend(Hum, Ee, t / 0.12) : t < 0.55 ? Blend(Ee, Ah, (t - 0.12) / 0.43) : Blend(Ah, Oo, (t - 0.55) / 0.45),
            breath: 0.06), 0.7f),

        // Woof woof: two short round barks that drop in pitch.
        Shape.Dog => new(() => Repeat(2, 0.14, () => Voiced(0.24,
            pitch: t => 250 - (90 * t),
            loud: t => Envelope(t, gentle ? 0.2 : 0.08, 0.5),
            vowel: t => t < 0.3 ? Blend(Oo, Ah, t / 0.3) : Blend(Ah, Oo, (t - 0.3) / 0.7),
            breath: 0.18, rough: 0.2, roughHz: 45)), 0.85f),

        // Neigh: a high whinny that shakes and falls.
        Shape.Horse => new(() => Voiced(1.1,
            pitch: t => (820 - (330 * t) + (120 * Hump(t, 0.15))) * (1 + (0.07 * Math.Sin(2 * Math.PI * 11 * t * 1.1))),
            loud: t => Envelope(t, 0.08, 0.4) * (0.75 + (0.25 * Math.Sin(2 * Math.PI * 11 * t * 1.1))),
            vowel: t => Blend(Ee, Eh, t),
            breath: 0.15), 0.6f),

        // Quack quack: short, nasal and buzzy.
        Shape.Duck => new(() => Repeat(2, 0.12, () => Voiced(0.2,
            pitch: t => 300 - (40 * t),
            loud: t => Envelope(t, gentle ? 0.15 : 0.05, 0.45),
            vowel: _ => Quack,
            breath: 0.1, rough: 0.5, roughHz: 70)), 0.7f),

        // Tweet tweet tweet: three quick rising chirps.
        Shape.Bird => new(() => Repeat(3, 0.07, () => Whistle(0.08,
            pitch: t => 2400 + (1300 * t * t),
            loud: t => Envelope(t, 0.2, 0.5),
            overtone: 0.08)), 0.45f),

        // Squeak squeak: tiny and quiet.
        Shape.Mouse => new(() => Repeat(2, 0.1, () => Whistle(0.1,
            pitch: t => 3100 + (500 * Hump(t, 0.4)),
            loud: t => Envelope(t, 0.25, 0.45),
            overtone: 0.05)), 0.35f),

        // Trumpet: a brassy rising blast through the trunk.
        Shape.Elephant => new(() => Voiced(1.2,
            pitch: t => 330 + (200 * Math.Min(1, t * 2.5)) - (50 * t),
            loud: t => Envelope(t, 0.12, 0.3),
            vowel: _ => Brass,
            breath: 0.12, rough: 0.15, roughHz: 30), 0.75f, Big: true),

        // Grrr: a low, rumbling growl.
        Shape.Bear => new(() => Voiced(1.0,
            pitch: t => 88 + (8 * Math.Sin(2 * Math.PI * 3 * t)),
            loud: t => Envelope(t, 0.2, 0.3),
            vowel: t => Blend(Oh, Oo, t),
            breath: 0.3, rough: 0.6, roughHz: 24), 0.85f, Big: true),

        // Roar: the picture-book dinosaur, wide open then closing.
        Shape.Rex => new(() => Voiced(gentle ? 1.0 : 1.4,
            pitch: t => 110 + (35 * Hump(t, 0.3)) - (35 * t),
            loud: t => Envelope(t, gentle ? 0.3 : 0.12, 0.35),
            vowel: t => t < 0.4 ? Blend(Ah, Oh, t / 0.4) : Blend(Oh, Oo, (t - 0.4) / 0.6),
            breath: 0.35, rough: 0.7, roughHz: 32), 0.9f, Big: true),

        // The long-necked dinosaur hums a deep, gentle "hooo".
        Shape.LongNeck => new(() => Voiced(1.6,
            pitch: t => 72 + (16 * Hump(t, 0.45)),
            loud: t => Envelope(t, 0.3, 0.35),
            vowel: t => Blend(Hum, Oo, Math.Min(1, t * 3)),
            breath: 0.08), 0.85f),

        // Hmph hmph: two snorting grunts.
        Shape.Triceratops => new(() => Repeat(2, 0.16, () => Mix(
            Voiced(0.28, pitch: t => 105 - (25 * t), loud: t => Envelope(t, 0.12, 0.5), vowel: _ => Oh, breath: 0.2, rough: 0.3, roughHz: 28),
            Noise(0.28, centre: 700, width: 0.8, loud: t => Envelope(t, 0.05, 0.7)), 0.5)), 0.85f, Big: true),

        // A short, low grumble.
        Shape.Stegosaurus => new(() => Voiced(0.9,
            pitch: t => 78 - (10 * t),
            loud: t => Envelope(t, 0.15, 0.4),
            vowel: t => Blend(Oh, Hum, t),
            breath: 0.2, rough: 0.45, roughHz: 20), 0.8f, Big: true),

        // Whale song: a slow, deep glide up and back down, then a second, higher phrase.
        Shape.Whale => new(() => Concat(
            Whistle(1.3, pitch: t => 170 + (230 * Hump(t, 0.55)), loud: t => Envelope(t, 0.3, 0.4), overtone: 0.25, vibrato: 0.012),
            Silence(0.15),
            Whistle(0.9, pitch: t => 320 - (110 * t), loud: t => Envelope(t, 0.3, 0.5), overtone: 0.2, vibrato: 0.015)), 0.8f),

        // Clicks, then a quick whistle.
        Shape.Dolphin => new(() => Concat(
            Repeat(5, 0.045, () => Noise(0.006, centre: 1800, width: 1.2, loud: _ => 1)),
            Silence(0.08),
            Whistle(0.45, pitch: t => 2300 + (1400 * Hump(t, 0.6)), loud: t => Envelope(t, 0.15, 0.4), overtone: 0.05)), 0.5f),

        // Arf arf.
        Shape.Seal => new(() => Repeat(2, 0.15, () => Voiced(0.22,
            pitch: t => 380 - (110 * t),
            loud: t => Envelope(t, gentle ? 0.2 : 0.08, 0.45),
            vowel: t => Blend(Ah, Oo, t),
            breath: 0.2, rough: 0.3, roughHz: 40)), 0.75f),

        // Blub blub blub: bubbles.
        Shape.Fish => new(() => Repeat(3, 0.09, () => Whistle(0.07,
            pitch: t => 420 * Math.Pow(2.2, t),
            loud: t => Math.Exp(-5 * t) * Envelope(t, 0.1, 0.2))), 0.5f),

        _ => null,
    };

    // Vowels as three formants (frequency, bandwidth, level). Adult values from the phonetics tables; animal
    // calls sit near enough to them that a child hears "moo" and "baa".
    private static readonly Formant[] Oo = [new(300, 80, 1), new(870, 100, 0.35), new(2240, 150, 0.08)];
    private static readonly Formant[] Oh = [new(500, 90, 1), new(840, 100, 0.5), new(2400, 150, 0.1)];
    private static readonly Formant[] Ah = [new(730, 110, 1), new(1090, 120, 0.6), new(2440, 160, 0.2)];
    private static readonly Formant[] Ae = [new(660, 110, 1), new(1720, 140, 0.5), new(2410, 160, 0.25)];
    private static readonly Formant[] Eh = [new(530, 100, 1), new(1840, 140, 0.45), new(2480, 160, 0.25)];
    private static readonly Formant[] Ee = [new(300, 80, 1), new(2290, 150, 0.35), new(3010, 200, 0.2)];
    /// <summary>A closed mouth: "mmm".</summary>
    private static readonly Formant[] Hum = [new(250, 60, 1), new(1000, 200, 0.05), new(2200, 300, 0.02)];
    private static readonly Formant[] Quack = [new(600, 150, 0.8), new(1500, 200, 1), new(2600, 250, 0.5)];
    private static readonly Formant[] Brass = [new(600, 300, 0.7), new(1300, 400, 1), new(2400, 500, 0.6)];

    private readonly record struct Formant(double Frequency, double Bandwidth, double Level);

    /// <summary>A voice: harmonics of the pitch, each as loud as the vowel's formants say, with optional breath
    /// (noise shaped by the same formants) and roughness (a fast wobble in loudness, for growls).</summary>
    /// <param name="pitch">Fundamental in Hz, given time 0..1 through the call.</param>
    /// <param name="loud">0..1 over time.</param>
    private static float[] Voiced(double seconds, Func<double, double> pitch, Func<double, double> loud,
        Func<double, Formant[]> vowel, double breath = 0, double rough = 0, double roughHz = 30)
    {
        var n = (int)(seconds * Rate);
        var output = new float[n];
        const int maxHarmonics = 60;
        var weights = new double[maxHarmonics];
        var harmonics = 0;
        double phase = 0;
        var noise = new NoiseSource(0x5EED);
        var breathFilter = new Resonator();
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)n;
            var f0 = Math.Max(30, pitch(t));
            if (i % EnvelopeStep == 0)
            {
                var formants = vowel(t);
                harmonics = Math.Min(maxHarmonics, (int)(5000 / f0));
                for (var k = 1; k <= harmonics; k++)
                    weights[k - 1] = Spectrum(formants, k * f0) / Math.Sqrt(k);
                var f1 = formants[0];
                breathFilter.Tune(f1.Frequency * 1.5, f1.Frequency * 2);
            }
            phase += f0 / Rate;
            if (phase >= 1) phase -= 1;
            double s = 0;
            var angle = 2 * Math.PI * phase;
            for (var k = 1; k <= harmonics; k++) s += weights[k - 1] * Math.Sin(k * angle);
            if (breath > 0) s += breath * 3 * breathFilter.Next(noise.Next());
            if (rough > 0) s *= 1 - (rough * 0.5 * (1 + Math.Sin(2 * Math.PI * roughHz * i / Rate + (0.6 * noise.Next()))));
            output[i] = (float)(s * loud(t));
        }
        return output;
    }

    /// <summary>A whistle: a pure tone following <paramref name="pitch"/>, for birds, mice, whales and bubbles.</summary>
    private static float[] Whistle(double seconds, Func<double, double> pitch, Func<double, double> loud, double overtone = 0, double vibrato = 0)
    {
        var n = (int)(seconds * Rate);
        var output = new float[n];
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)n;
            var f = pitch(t) * (1 + (vibrato * Math.Sin(2 * Math.PI * 4.5 * i / Rate)));
            phase += f / Rate;
            if (phase >= 1) phase -= 1;
            var angle = 2 * Math.PI * phase;
            output[i] = (float)((Math.Sin(angle) + (overtone * Math.Sin(2 * angle)) + (overtone * 0.4 * Math.Sin(3 * angle))) * loud(t));
        }
        return output;
    }

    /// <summary>Noise through a band-pass at <paramref name="centre"/>; <paramref name="width"/> is the band
    /// as a fraction of the centre.</summary>
    private static float[] Noise(double seconds, double centre, double width, Func<double, double> loud)
    {
        var n = Math.Max(1, (int)(seconds * Rate));
        var output = new float[n];
        var noise = new NoiseSource(0xB0B);
        var filter = new Resonator();
        filter.Tune(centre, centre * width);
        for (var i = 0; i < n; i++) output[i] = (float)(filter.Next(noise.Next()) * 4 * loud(i / (double)n));
        return output;
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static float[] Repeat(int times, double gap, Func<float[]> make)
    {
        var one = make();
        Normalise(one);
        var parts = new List<float[]>();
        for (var i = 0; i < times; i++)
        {
            if (i > 0) parts.Add(Silence(gap));
            parts.Add(i == 0 ? one : Scaled(one, 1 - (0.12 * i)));
        }
        return Concat([.. parts]);
    }

    /// <summary>Two parts laid over each other, the second at <paramref name="level"/> of the first's peak.</summary>
    private static float[] Mix(float[] a, float[] b, double level)
    {
        Normalise(a);
        Normalise(b);
        var output = new float[Math.Max(a.Length, b.Length)];
        for (var i = 0; i < output.Length; i++)
            output[i] = (i < a.Length ? a[i] : 0) + (float)((i < b.Length ? b[i] : 0) * level);
        return output;
    }

    private static float[] Scaled(float[] samples, double gain) => samples.Select(s => (float)(s * gain)).ToArray();

    private static double Spectrum(Formant[] formants, double frequency)
    {
        double level = 0;
        foreach (var f in formants)
        {
            var x = (frequency - f.Frequency) / (f.Bandwidth / 2);
            level += f.Level / (1 + (x * x));
        }
        return level;
    }

    private static Formant[] Blend(Formant[] a, Formant[] b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        var result = new Formant[a.Length];
        for (var i = 0; i < a.Length; i++)
            result[i] = new(Lerp(a[i].Frequency, b[i].Frequency, t), Lerp(a[i].Bandwidth, b[i].Bandwidth, t), Lerp(a[i].Level, b[i].Level, t));
        return result;
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>0 at both ends, 1 at <paramref name="peak"/>; a smooth rise and fall.</summary>
    private static double Hump(double t, double peak) =>
        t < peak ? Math.Sin(t / peak * Math.PI / 2) : Math.Cos((t - peak) / (1 - peak) * Math.PI / 2);

    /// <summary>Rises over the first <paramref name="attack"/> of the call and falls over the last <paramref name="release"/>.</summary>
    private static double Envelope(double t, double attack, double release)
    {
        if (t < attack) return Math.Sin(t / attack * Math.PI / 2);
        if (t > 1 - release) return Math.Cos((t - (1 - release)) / release * Math.PI / 2);
        return 1;
    }

    private static void Normalise(float[] samples, float to = 1)
    {
        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        if (peak <= 0) return;
        var g = to / peak;
        for (var i = 0; i < samples.Length; i++) samples[i] *= g;
    }

    /// <summary>Sets the peak to <paramref name="loudness"/> of <see cref="MaxPeak"/> and fades both ends.</summary>
    private static void Finish(float[] samples, float loudness, double fadeSeconds)
    {
        Normalise(samples, MaxPeak * Math.Clamp(loudness, 0, 1));
        var fade = Math.Min(samples.Length / 2, (int)(fadeSeconds * Rate));
        for (var i = 0; i < fade; i++)
        {
            var g = i / (float)fade;
            samples[i] *= g;
            samples[^(i + 1)] *= g;
        }
    }

    /// <summary>A gentle two-pole low-pass, taking the edge off for babies.</summary>
    private static void Darken(float[] samples, double cutoff)
    {
        var a = (float)(1 - Math.Exp(-2 * Math.PI * cutoff / Rate));
        float y1 = 0, y2 = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            y1 += a * (samples[i] - y1);
            y2 += a * (y1 - y2);
            samples[i] = y2;
        }
    }

    private struct NoiseSource(uint seed)
    {
        private uint _state = seed;

        public double Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return ((_state & 0xFFFF) / 32768.0) - 1;
        }
    }

    /// <summary>A two-pole band-pass (the RBJ cookbook's constant-peak form).</summary>
    private sealed class Resonator
    {
        private double _b0, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        public void Tune(double centre, double bandwidth)
        {
            var w = 2 * Math.PI * Math.Min(centre, Rate * 0.45) / Rate;
            var q = Math.Max(0.3, centre / Math.Max(1, bandwidth));
            var alpha = Math.Sin(w) / (2 * q);
            var a0 = 1 + alpha;
            _b0 = alpha / a0;
            _b2 = -alpha / a0;
            _a1 = -2 * Math.Cos(w) / a0;
            _a2 = (1 - alpha) / a0;
        }

        public double Next(double x)
        {
            var y = (_b0 * x) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            (_x2, _x1) = (_x1, x);
            (_y2, _y1) = (_y1, y);
            return y;
        }
    }
}
