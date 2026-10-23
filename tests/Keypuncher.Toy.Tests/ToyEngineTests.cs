using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Inference;
using Keypuncher.Rendering;
using Keypuncher.Storage;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

public sealed class ToyEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-tests").FullName;
    private readonly World _world = new();
    private readonly Mixer _mixer = new();
    private readonly ToyEngine _engine;
    private DateTimeOffset _clock = DateTimeOffset.UnixEpoch;

    public ToyEngineTests()
    {
        var icelandicOnly = ToySettings.Default with { LanguageWeights = new() { ["is"] = 1 } };
        _engine = new ToyEngine(_world, _mixer, new SpeechLibrary(new ClipStore(_dir), null), icelandicOnly, new Random(7));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_letter_key_shows_its_glyph_and_makes_its_family_thing()
    {
        Press(0x25, "k");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "K");
        var thing = Assert.Single(_world.Live, e => e.Role == EntityRole.Thing);

        Press(0x25, "k");
        Assert.Equal(2, _world.Live.Count(e => e.Role == EntityRole.Thing && e.Recipe == thing.Recipe));
    }

    [Fact]
    public void A_word_follows_a_letter_only_after_a_pause()
    {
        Press(0x1F, "s");          // sól, ský, stjarna
        Step(0.3);
        Press(0x1E, "a");          // interrupts: no word for s
        Step(2);
        Assert.DoesNotContain(_world.Live, e => e.Role == EntityRole.Word);

        Press(0x1F, "s");
        Step(1.2);
        var word = Assert.Single(_world.Live, e => e.Role == EntityRole.Word);
        Assert.StartsWith("s", word.Text);
    }

    [Fact]
    public void A_digit_counts_things_one_at_a_time()
    {
        Press(0x04, "3");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "3");
        Assert.Equal(1, _world.LiveThingCount);   // the first one appears at once
        Step(0.6);
        Assert.Equal(2, _world.LiveThingCount);
        Step(0.6);
        Assert.Equal(3, _world.LiveThingCount);
        Step(2);
        Assert.Equal(3, _world.LiveThingCount);
    }

    [Fact]
    public void The_accent_key_sparkles_and_then_accents_the_vowel()
    {
        _engine.OnKey(new KeyPress(_clock, 0x28, "´", true, true));
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Effect);
        _engine.OnKey(new KeyPress(_clock, 0x28, "´", true, false));
        Step(0.2);
        Press(0x18, "o");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "Ó");
    }

    [Fact]
    public void A_palm_mash_makes_one_burst_not_five_letters()
    {
        foreach (var scan in new[] { 0x1E, 0x1F, 0x20, 0x21, 0x22 })
        {
            _engine.OnKey(new KeyPress(_clock, scan, "a", false, true));
            _clock = _clock.AddMilliseconds(8);
        }
        Assert.Single(_world.Live, e => e.Role == EntityRole.Letter);   // only the first key spoke
        Assert.Single(_world.Live, e => e.Recipe == BuiltInRecipes.Burst);
    }

    [Fact]
    public void Grown_up_gate_needs_the_chord_and_then_the_word()
    {
        var opened = 0;
        _engine.GrownUpRequested += () => opened++;

        Type("lamp");
        Assert.Equal(0, opened);

        _engine.OnKey(new KeyPress(_clock, 0x10, "q", false, true, Ctrl: true, Shift: true));
        var word = _engine.Gate.Challenge!;
        Assert.NotNull(word);
        Type("xx" + word);
        Assert.Equal(1, opened);
        Assert.Null(_engine.Gate.Challenge);
    }

    [Fact]
    public void Grown_up_challenge_expires()
    {
        _engine.OnKey(new KeyPress(_clock, 0x10, "q", false, true, Ctrl: true, Shift: true));
        var word = _engine.Gate.Challenge!;
        Step(GrownUpGate.Timeout + 1);
        var opened = 0;
        _engine.GrownUpRequested += () => opened++;
        Type(word);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task Recorded_clip_is_spoken_and_missing_letter_stays_quiet()
    {
        var clips = new ClipStore(_dir);
        clips.Save(new ClipId("is", ClipKind.Letter, "m"), s => WavFile.Write(s, Enumerable.Repeat(0.3f, 4800).ToArray(), 48000));
        var speech = new SpeechLibrary(clips, new SilentVoice());
        await speech.PreloadAsync([new ClipId("is", ClipKind.Letter, "m")]);
        Assert.NotNull(speech.TryGet(new ClipId("is", ClipKind.Letter, "m"), null));
        Assert.Null(speech.TryGet(new ClipId("is", ClipKind.Letter, "n"), null));
    }

    [Fact]
    public void World_survives_a_save_and_load()
    {
        Press(0x25, "k");
        Press(0x04, "3");
        Step(2);
        var path = Path.Combine(_dir, "world.db");
        using (var store = new WorldStore(path)) store.Save(_world);
        Assert.False(_world.Dirty);

        var loaded = new World();
        using (var store = new WorldStore(path)) store.Load(loaded);
        Assert.Equal(_world.LiveThingCount, loaded.LiveThingCount);
        Assert.Equal(_world.Time, loaded.Time, 6);
        Assert.Equal(
            _world.Live.Where(e => e.IsPersistent).Select(e => e.Recipe!.Name),
            loaded.Live.Select(e => e.Recipe!.Name));
        Assert.True(loaded.NextId() > _world.Live.Max(e => e.Id));
    }

    [Fact]
    public void Renderer_draws_a_busy_world()
    {
        foreach (var (scan, ch) in new[] { (0x25, "k"), (0x04, "3"), (0x1F, "s"), (0x2C, "z"), (0x16, "u") })
        {
            Press(scan, ch);
            Step(0.4);
        }
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        using var surface = SKSurface.Create(new SKImageInfo(640, 360));
        renderer.Draw(surface.Canvas, 640, 360, _world, "Grown-up? Type: lamp");
        using var pixels = surface.Snapshot().PeekPixels();
        Assert.NotEqual(pixels.GetPixelColor(10, 10), pixels.GetPixelColor(320, 300));
    }

    private void Press(int scan, string ch)
    {
        _engine.OnKey(new KeyPress(_clock, scan, ch, false, true));
        _clock = _clock.AddMilliseconds(80);
        _engine.OnKey(new KeyPress(_clock, scan, ch, false, false));
        _clock = _clock.AddMilliseconds(80);
    }

    private void Type(string text)
    {
        foreach (var c in text) Press(0x1E + (c % 9), c.ToString());
    }

    private void Step(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.05) _engine.Update(0.05);
        _clock = _clock.AddSeconds(seconds);
    }

    private sealed class SilentVoice : ISpeech
    {
        public Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }
}
