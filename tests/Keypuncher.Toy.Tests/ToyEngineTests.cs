using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Inference;
using Keypuncher.Rendering;
using Keypuncher.Language;
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
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "k");
        var thing = Assert.Single(_world.Live, e => e.Role == EntityRole.Thing);

        Press(0x25, "k");
        Assert.Equal(2, _world.Live.Count(e => e.Role == EntityRole.Thing && e.Recipe == thing.Recipe));
    }

    [Fact]
    public void The_clock_keeps_to_real_time_however_slowly_frames_come()
    {
        // At 5 frames a second each frame is 0.2 s; the clock once took only 0.1 s of it, and ran at half speed.
        for (var i = 0; i < 50; i++) _engine.Advance(0.2);
        Assert.Equal(10, _world.Time, 6);

        // A long stall (the PC asleep) moves it on by a second at most.
        _engine.Advance(3600);
        Assert.Equal(11, _world.Time, 6);
    }

    [Fact]
    public void A_word_follows_a_letter_after_a_short_pause()
    {
        Press(0x1F, "s");          // sól, ský, stjarna
        Step(0.5);
        Assert.DoesNotContain(_world.Live, e => e.Role == EntityRole.Word);
        Step(0.6);
        var word = Assert.Single(_world.Live, e => e.Role == EntityRole.Word);
        Assert.StartsWith("s", word.Text);
    }

    [Fact]
    public void Keys_without_a_word_do_not_drop_the_waiting_word()
    {
        Press(0x1F, "s");          // sól, ský, stjarna
        Step(0.3);
        Press(0x1E, "a");          // no Icelandic starter word begins with a
        Step(0.3);
        Press(0x33, ",");
        Step(0.5);
        var word = Assert.Single(_world.Live, e => e.Role == EntityRole.Word);
        Assert.StartsWith("s", word.Text);
    }

    [Fact]
    public void A_later_letter_with_a_word_takes_the_earlier_ones_place()
    {
        Press(0x1F, "s");
        Step(0.3);
        Press(0x23, "h");          // hús, hundur
        Step(2);
        var word = Assert.Single(_world.Live, e => e.Role == EntityRole.Word);
        Assert.StartsWith("h", word.Text);
    }

    [Fact]
    public void A_mash_or_a_digit_drops_the_waiting_word()
    {
        Press(0x1F, "s");
        Step(0.3);
        foreach (var scan in new[] { 0x1E, 0x1F, 0x20 })
        {
            _engine.OnKey(new KeyPress(_clock, scan, "a", false, true));
            _clock = _clock.AddMilliseconds(8);
        }
        Step(2);
        Assert.DoesNotContain(_world.Live, e => e.Role == EntityRole.Word);

        Press(0x1F, "s");
        Step(0.3);
        Press(0x03, "2");
        Step(2);
        Assert.DoesNotContain(_world.Live, e => e.Role == EntityRole.Word);
    }

    [Fact]
    public void A_word_whose_voice_is_late_is_said_once_it_is_ready()
    {
        var voice = new SlowVoice();
        var mixer = new Mixer();
        var world = new World();
        var engine = new ToyEngine(world, mixer, new SpeechLibrary(new ClipStore(_dir), voice),
            ToySettings.Default with { LanguageWeights = new() { ["en"] = 1 } }, new Random(7));
        engine.OnKey(new KeyPress(_clock, 0x1F, "s", false, true));
        engine.OnKey(new KeyPress(_clock.AddMilliseconds(80), 0x1F, "s", false, false));
        for (var t = 0.0; t < 1.0; t += 0.05) engine.Update(0.05);
        Assert.Contains(world.Live, e => e.Role == EntityRole.Word);
        Assert.False(mixer.SpeechPlaying);

        voice.Ready.Set();
        var spoke = false;
        for (var t = 0.0; t < 1.0 && !spoke; t += 0.05)
        {
            Thread.Sleep(20);
            engine.Update(0.05);
            spoke = mixer.SpeechPlaying;
        }
        Assert.True(spoke);
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
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "ó");
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
    public void Holding_Esc_for_three_seconds_also_shows_the_challenge()
    {
        _engine.OnKey(new KeyPress(_clock, 0x01, null, false, true));
        for (var ms = 500; ms <= 2500; ms += 500)
            _engine.OnKey(new KeyPress(_clock.AddMilliseconds(ms), 0x01, null, false, true));
        Assert.Null(_engine.Gate.Challenge);

        _engine.OnKey(new KeyPress(_clock.AddMilliseconds(3050), 0x01, null, false, true));
        Assert.NotNull(_engine.Gate.Challenge);
    }

    [Fact]
    public void Tapping_Esc_does_not_add_up_to_a_hold()
    {
        for (var i = 0; i < 10; i++)
        {
            _engine.OnKey(new KeyPress(_clock, 0x01, null, false, true));
            _clock = _clock.AddMilliseconds(400);
            _engine.OnKey(new KeyPress(_clock, 0x01, null, false, false));
        }
        Assert.Null(_engine.Gate.Challenge);
    }

    [Fact]
    public void Holding_Esc_fills_a_cue_that_a_tap_never_shows()
    {
        var gate = _engine.Gate;
        _engine.OnKey(new KeyPress(_clock, 0x01, null, false, true));
        Assert.Equal((0, 0), gate.EscCue(_clock.AddMilliseconds(300)));   // a tap's worth of hold: nothing shows

        for (var ms = 33; ms <= 1500; ms += 33)
            _engine.OnKey(new KeyPress(_clock.AddMilliseconds(ms), 0x01, null, false, true));
        var (progress, opacity) = gate.EscCue(_clock.AddMilliseconds(1500));
        Assert.Equal(0.5, progress, 3);
        Assert.Equal(1, opacity, 3);

        // Auto-repeat stopped with no key-up seen: the cue goes rather than sit in the corner.
        Assert.Equal((0, 0), gate.EscCue(_clock.AddMilliseconds(2200)));

        for (var ms = 1533; ms <= 3050; ms += 33)
            _engine.OnKey(new KeyPress(_clock.AddMilliseconds(ms), 0x01, null, false, true));
        Assert.NotNull(gate.Challenge);
        Assert.Equal((0, 0), gate.EscCue(_clock.AddMilliseconds(3050)));  // the word is up; the ring is done

        _engine.OnKey(new KeyPress(_clock.AddMilliseconds(3100), 0x01, null, false, false));
        Assert.Equal((0, 0), gate.EscCue(_clock.AddMilliseconds(3100)));
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
    public void A_key_makes_its_restyled_family_and_the_change_is_saved()
    {
        var puffin = BuiltInRecipes.Animals.Bird with { Name = "puffin", Body = BuiltInRecipes.Animals.Bird.Body with { Hue = 0 } };
        Assert.True(_world.Families.Restyle(0, puffin, 1));   // 0x26 (L) is family 0
        Assert.True(_world.Dirty);
        Press(0x26, "l");
        Assert.Same(puffin, Assert.Single(_world.Live, e => e.Role == EntityRole.Thing).Recipe);

        var path = Path.Combine(_dir, "families.db");
        using (var store = new WorldStore(path)) store.Save(_world);
        var loaded = new World();
        using (var store = new WorldStore(path)) store.Load(loaded);
        Assert.Equal("puffin", loaded.Families[0].Name);
        Assert.Equal(1, loaded.Families.ChangedAt(0));
        Assert.Null(loaded.Families.ChangedAt(1));

        loaded.Clear();
        Assert.Same(BuiltInRecipes.Palette[0], loaded.Families[0]);
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

    [Fact]
    public void Presses_faster_than_the_calm_rate_wake_the_newest_thing_instead_of_adding()
    {
        _engine.Settings = _engine.Settings with { Calm = CalmLevel.Baby, SayWords = false };
        Press(0x25, "k");
        var first = Assert.Single(_world.Live, e => e.Role == EntityRole.Thing);
        Step(CalmLimits.Baby.SettleSeconds + 1);
        Assert.Equal(0, first.Energy);
        Assert.False(first.Awake);

        Press(0x25, "k");   // seconds later: allowed
        Press(0x25, "k");   // 0.16 s later: too soon
        var things = _world.Live.Where(e => e.Role == EntityRole.Thing).ToList();
        Assert.Equal(2, things.Count);
        Assert.True(things[1].Awake);
        Assert.Equal(2, _world.Live.Count(e => e.Role == EntityRole.Letter));   // still says and shows every letter
    }

    [Fact]
    public void A_burst_of_presses_stays_under_the_live_cap()
    {
        _engine.Settings = _engine.Settings with { Calm = CalmLevel.Baby };
        for (var i = 0; i < 40; i++)
        {
            Press(0x10 + (i % 10), "q");
            Step(1.5);
        }
        Assert.Equal(CalmLimits.Baby.LiveThings, _world.LiveThingCount);
        Assert.True(_world.Live.Count(e => e.Awake) <= CalmLimits.Baby.Moving);
        Assert.NotEmpty(_world.Baked);
    }

    [Fact]
    public void A_digit_above_the_calm_limit_makes_one_thing()
    {
        _engine.Settings = _engine.Settings with { Calm = CalmLevel.Baby };
        Press(0x06, "5");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "5");
        Step(4);
        Assert.Equal(1, _world.LiveThingCount);

        Press(0x04, "3");
        Step(2);
        Assert.Equal(4, _world.LiveThingCount);
    }

    [Fact]
    public void Mashes_close_together_make_one_burst()
    {
        Mash(0x1E);
        Step(0.2);
        Mash(0x2C);
        Assert.Single(_world.Live, e => e.Recipe == BuiltInRecipes.Burst);
        Step(1);
        Mash(0x1E);
        Assert.Equal(2, _world.Live.Count(e => e.Recipe == BuiltInRecipes.Burst));
    }

    [Fact]
    public void The_session_ends_with_a_goodnight_and_a_new_session_wakes_up()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 1 };
        PlayFor(59);
        Assert.False(_engine.IsAsleep);
        Step(ToyEngine.PauseBeforeGoodnight + 2);
        Assert.True(_engine.IsAsleep);
        Step(ToyEngine.GoodnightSeconds);
        Assert.Equal(1, _world.Night);

        var before = _world.Live.Count;
        Press(0x1F, "s");
        Assert.Equal(before, _world.Live.Count);   // only a soft note
        Assert.DoesNotContain(_world.Live, e => e.Awake);

        _engine.StartSession();
        Assert.False(_engine.IsAsleep);
        Assert.Equal(0, _world.Night);
        Press(0x1F, "s");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "s");
    }

    [Fact]
    public void The_goodnight_waits_for_a_pause()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 1 };
        for (var i = 0; i < 70; i++)
        {
            Press(0x25, "k");
            Step(1);
        }
        Assert.False(_engine.IsAsleep);   // still playing ten seconds after time was up
        Assert.True(_engine.IsWindingDown);
        Assert.True(_world.Quiet);

        Step(ToyEngine.PauseBeforeGoodnight - 1);
        Assert.False(_engine.IsAsleep);
        Step(1.5);
        Assert.True(_engine.IsAsleep);
    }

    [Fact]
    public void The_goodnight_stops_waiting_after_a_few_minutes()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 1 };
        PlayFor(59);
        var pressedFor = 0.0;
        while (!_engine.IsAsleep && pressedFor < 600)
        {
            Press(0x25, "k");
            Step(1);
            pressedFor++;
        }
        Assert.True(_engine.IsAsleep);
        Assert.InRange(pressedFor, ToyEngine.MaxWindDown, ToyEngine.MaxWindDown + 3);
    }

    [Fact]
    public void A_toy_left_alone_does_not_use_up_the_play_time()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 1 };
        Press(0x25, "k");
        Step(30 * 60);
        Assert.False(_engine.IsAsleep);
        Assert.False(_engine.IsWindingDown);

        PlayFor(30);
        Assert.False(_engine.IsWindingDown);   // 20 s after the first press, then 30 s: the quiet half hour did not count
        PlayFor(14);
        Assert.True(_engine.IsWindingDown);
    }

    [Fact]
    public void After_a_goodnight_a_long_rest_wakes_the_toy_on_the_next_press()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 1 };
        PlayFor(60);
        Step(ToyEngine.PauseBeforeGoodnight + ToyEngine.GoodnightSeconds + 1);
        Assert.True(_engine.IsAsleep);

        Step(30);
        Press(0x1F, "s");
        Assert.True(_engine.IsAsleep);   // pressing on through the night keeps it asleep

        Step(60);
        Press(0x1F, "s");
        Assert.False(_engine.IsAsleep);
        Assert.Equal(0, _world.Night);
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "s");
    }

    [Fact]
    public void A_session_keeps_one_language()
    {
        var engine = MixedLanguageEngine(new Random(1));
        var seen = new HashSet<string>();
        for (var session = 0; session < 40; session++)
        {
            engine.StartSession();
            seen.Add(engine.SessionLanguage);
        }
        Assert.Equal(3, seen.Count);   // all three come up over many sessions...

        engine.StartSession();
        var language = engine.SessionLanguage;
        for (var i = 0; i < 30; i++)
        {
            engine.OnKey(new KeyPress(_clock, 0x24, "j", false, true));
            engine.OnKey(new KeyPress(_clock = _clock.AddMilliseconds(80), 0x24, "j", false, false));
            _clock = _clock.AddMilliseconds(400);
            engine.Update(0.5);
        }
        Assert.Equal(language, engine.SessionLanguage);   // ...but never within one
        var expected = KeyMeanings.ApplyCase(KeyMeanings.For(language, "j")!.Display, upperCase: false);
        Assert.All(engine.World.Live.Where(e => e.Role == EntityRole.Letter), e => Assert.Equal(expected, e.Text));
    }

    [Fact]
    public void Turning_a_language_off_moves_the_session_to_another()
    {
        var engine = MixedLanguageEngine(new Random(3));
        var current = engine.SessionLanguage;
        var weights = new Dictionary<string, double>(engine.Settings.LanguageWeights) { [current] = 0 };
        engine.Settings = engine.Settings with { LanguageWeights = weights };
        Assert.NotEqual(current, engine.SessionLanguage);
        Assert.True(weights[engine.SessionLanguage] > 0);
    }

    [Fact]
    public void Test_mode_makes_the_director_go_flat_out_with_no_goodnight_and_shows_each_new_thing()
    {
        var test = ToySettings.Default with { Calm = CalmLevel.Baby, DirectorTest = true };
        Assert.Equal((0.0, 0.0, 3.0), (test.Limits.RestyleMinutes, test.Limits.SceneMinutes, test.Limits.SceneFadeSeconds));
        Assert.Null(test.SessionLength);
        Assert.Equal(CalmLimits.Baby.LiveThings, test.Limits.LiveThings);
        Assert.Equal(CalmLimits.Baby, (test with { DirectorTest = false }).Limits);

        _engine.Settings = test;
        Assert.Equal(0, _world.Limits.SceneMinutes);
        var hedgehog = BuiltInRecipes.Palette[0] with { Name = "hedgehog" };
        _engine.Show(hedgehog);
        Assert.Contains(_world.Live, e => e.Recipe == hedgehog);
    }

    [Fact]
    public void Letters_are_small_by_default()
    {
        Assert.False(ToySettings.Default.UpperCase);
        _engine.Settings = _engine.Settings with { UpperCase = true };
        Press(0x25, "k");
        Assert.Contains(_world.Live, e => e.Role == EntityRole.Letter && e.Text == "K");
    }

    [Theory]
    [InlineData("sun")]
    [InlineData("moon")]
    [InlineData("house")]
    public void Things_that_are_not_alive_have_no_face(string concept) =>
        Assert.Equal(0, BuiltInRecipes.Concepts[concept].Eyes);

    [Fact]
    public void Every_starter_word_has_its_own_picture() =>
        Assert.All(StarterConcepts.All, c => Assert.True(BuiltInRecipes.Concepts.ContainsKey(c.Id), c.Id));

    [Theory]
    [InlineData("cat")]
    [InlineData("dog")]
    [InlineData("cow")]
    [InlineData("elephant")]
    [InlineData("rex")]
    [InlineData("stegosaurus")]
    [InlineData("whale")]
    [InlineData("octopus")]
    [InlineData("starfish")]
    public void An_animal_word_shows_the_real_animal(string concept)
    {
        var recipe = BuiltInRecipes.Concepts[concept];
        Assert.True(recipe.Body.Shape.IsAnimal());
        Assert.Equal(RecipeKind.Creature, recipe.Kind);
        Assert.True(recipe.Eyes > 0);
    }

    [Fact]
    public void Keys_can_make_every_animal() =>
        Assert.All(BuiltInRecipes.Animals.All, a => Assert.Contains(a, BuiltInRecipes.Palette));

    [Fact]
    public void Session_zero_never_says_goodnight()
    {
        _engine.Settings = _engine.Settings with { SessionMinutes = 0 };
        Assert.Null(_engine.Settings.SessionLength);
        Step(20 * 60);
        Assert.False(_engine.IsAsleep);
    }

    [Fact]
    public void Calm_level_sets_the_world_limits()
    {
        Assert.Equal(CalmLimits.Toddler, _world.Limits);
        _engine.Settings = _engine.Settings with { Calm = CalmLevel.BigToddler };
        Assert.Equal(CalmLimits.BigToddler, _world.Limits);
        Assert.Equal(TimeSpan.FromMinutes(15), _engine.Settings.SessionLength);
    }

    [Fact]
    public void Settings_from_before_calm_levels_load_as_toddler()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "LanguageWeights": { "is": 1 }, "UpperCase": false, "Volume": 0.3, "SayWords": true }""");
        var loaded = new SettingsStore(path).Load();
        Assert.Equal(CalmLevel.Toddler, loaded.Calm);
        Assert.Null(loaded.SessionMinutes);
        Assert.False(loaded.UpperCase);
        Assert.True(loaded.RecordPlay);   // settings saved before the switch existed record play

        var store = new SettingsStore(path);
        store.Save(loaded with { Calm = CalmLevel.Baby, SessionMinutes = 0 });
        Assert.Contains("\"Baby\"", File.ReadAllText(path));
        var again = store.Load();
        Assert.Equal(CalmLevel.Baby, again.Calm);
        Assert.Equal(0, again.SessionMinutes);
    }

    [Fact]
    public void History_is_drawn_faintly()
    {
        var bare = new World();
        var history = new World();
        var thing = new Entity { Id = 1, Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[9], X = 0.5, Y = 0.4, Born = 0, Scale = 2 };
        history.Load(100, [], [thing]);
        var live = new World();
        live.Load(100, [new Entity { Id = 1, Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[9], X = 0.5, Y = 0.4, Born = 0, Scale = 2 }], []);

        var empty = Render(bare);
        var faint = Render(history);
        var full = Render(live);
        var faintDelta = Delta(empty, faint);
        var fullDelta = Delta(empty, full);
        Assert.InRange(faintDelta, 1, fullDelta * 0.5);
    }

    [Fact]
    public void History_is_a_pale_ghost_with_no_dark_outlines()
    {
        // Dark outlines piled up in the background were what made the screen look busy, so even right
        // on top of a retired thing no pixel may differ much from the bare sky.
        var bare = RenderAll(new World());
        var history = new World();
        history.Load(100, [], [Thing(1, BuiltInRecipes.Palette[0], 0.5, 0.4)]);
        var ghost = RenderAll(history);
        var worst = Enumerable.Range(0, bare.Length).Max(i => Delta(bare[i], ghost[i]));
        Assert.InRange(worst, 1, 40);
    }

    [Fact]
    public void Only_the_last_few_retired_things_are_drawn_and_older_ones_fade_more()
    {
        var shown = CalmLimits.Toddler.HistoryShown;
        // One more than are shown, spread across the screen, oldest on the left.
        var baked = Enumerable.Range(0, shown + 1)
            .Select(i => Thing(i + 1, BuiltInRecipes.Palette[0], 0.08 + (i * 0.84 / shown), 0.3))
            .ToList();
        var world = new World { Limits = CalmLimits.Toddler };
        world.Load(100, [], baked);
        var bare = RenderAll(new World());
        var ghost = RenderAll(world);
        int At(Entity e) => Delta(bare[Index(e.X, e.Y)], ghost[Index(e.X, e.Y)]);

        Assert.Equal(0, At(baked[0]));            // past the cap: not drawn at all
        Assert.True(At(baked[1]) < At(baked[^1])); // oldest shown is fainter than the newest
        Assert.True(At(baked[^1]) > 0);
    }

    private static Entity Thing(long id, Recipe recipe, double x, double y) =>
        new() { Id = id, Role = EntityRole.Thing, Recipe = recipe, X = x, Y = y, Born = 0 };

    private static int Index(double x, double y) => ((int)(y * 300) * 400) + (int)(x * 400);

    private static SKColor[] RenderAll(World world)
    {
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        renderer.Draw(surface.Canvas, 400, 300, world);
        using var bitmap = SKBitmap.FromImage(surface.Snapshot());
        return bitmap.Pixels;
    }

    private static SKColor Render(World world)
    {
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        renderer.Draw(surface.Canvas, 400, 300, world);
        using var pixels = surface.Snapshot().PeekPixels();
        return pixels.GetPixelColor(200, 120);
    }

    private static int Delta(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);

    private void Mash(int firstScan)
    {
        for (var i = 0; i < 5; i++)
        {
            _engine.OnKey(new KeyPress(_clock, firstScan + i, null, false, true));
            _clock = _clock.AddMilliseconds(8);
        }
        for (var i = 0; i < 5; i++)
            _engine.OnKey(new KeyPress(_clock, firstScan + i, null, false, false));
    }

    private ToyEngine MixedLanguageEngine(Random random) =>
        new(new World(), new Mixer(), new SpeechLibrary(new ClipStore(_dir), null),
            ToySettings.Default with { LanguageWeights = new() { ["is"] = 0.4, ["en"] = 0.3, ["bg"] = 0.3 } }, random);

    [Fact]
    public void Animals_sometimes_make_their_noise_but_not_every_time()
    {
        var (engine, calls) = CallingEngine(ToySettings.Default);
        var (x, y) = TouchFor(AnimalCalls.HasCall);
        for (var i = 0; i < 60; i++)
        {
            engine.OnTouch(x, y, 1);
            StepEngine(engine, CalmLimits.Toddler.CallGapSeconds + 1);
        }
        Assert.InRange(calls.Count, 8, 35); // about one in three
    }

    [Fact]
    public void Animal_noises_keep_the_calm_levels_gap()
    {
        var (engine, calls) = CallingEngine(ToySettings.Default with { Calm = CalmLevel.Baby });
        var times = new List<double>();
        engine.AnimalCalled += _ => times.Add(engine.World.Time);
        var (x, y) = TouchFor(AnimalCalls.HasCall);
        for (var i = 0; i < 200; i++)
        {
            engine.OnTouch(x, y, 1);
            StepEngine(engine, 0.5);
        }
        Assert.NotEmpty(calls);
        Assert.All(times.Zip(times.Skip(1)), p => Assert.True(p.Second - p.First >= CalmLimits.Baby.CallGapSeconds - 1e-6));
    }

    [Fact]
    public void Animals_with_no_known_noise_and_switched_off_noises_stay_quiet()
    {
        var (quiet, none) = CallingEngine(ToySettings.Default);
        var (sx, sy) = TouchFor(s => s.IsAnimal() && !AnimalCalls.HasCall(s));
        var (off, offCalls) = CallingEngine(ToySettings.Default with { AnimalNoises = false });
        var (x, y) = TouchFor(AnimalCalls.HasCall);
        for (var i = 0; i < 40; i++)
        {
            quiet.OnTouch(sx, sy, 1);
            off.OnTouch(x, y, 1);
            StepEngine(quiet, 7);
            StepEngine(off, 7);
        }
        Assert.Empty(none);
        Assert.Empty(offCalls);
    }

    [Fact]
    public void A_mash_drops_a_waiting_noise()
    {
        var (engine, calls) = CallingEngine(ToySettings.Default, roll: 0);
        var (x, y) = TouchFor(AnimalCalls.HasCall);
        engine.OnTouch(x, y, 1);
        engine.OnTouch(0.5, 0.5, 4);
        StepEngine(engine, 4);
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData(5, TimeOfDay.Dawn)]
    [InlineData(9, TimeOfDay.Morning)]
    [InlineData(12, TimeOfDay.Midday)]
    [InlineData(15, TimeOfDay.Afternoon)]
    [InlineData(19, TimeOfDay.Evening)]
    public void A_session_starts_with_the_sky_the_clock_shows_and_keeps_the_place(int hour, TimeOfDay expected)
    {
        var world = new World();
        var vik = new Scene(Landscape.Beach, TimeOfDay.Evening, Weather.Rain, "Reynisfjara, Iceland", "A black sand beach.");
        world.Load(0, [], [], scene: vik);
        var now = new DateTime(2026, 10, 26, hour, 30, 0);
        _ = new ToyEngine(world, new Mixer(), new SpeechLibrary(new ClipStore(_dir), null), ToySettings.Default, new Random(1), clock: () => now);

        Assert.Equal(vik with { TimeOfDay = expected }, world.Scene);
    }

    [Fact]
    public void The_time_of_day_fades_in_rather_than_jumping()
    {
        var world = new World();
        var hour = 9;
        var engine = new ToyEngine(world, new Mixer(), new SpeechLibrary(new ClipStore(_dir), null), ToySettings.Default, new Random(1),
            clock: () => new DateTime(2026, 10, 26, hour, 0, 0));
        Assert.Equal(TimeOfDay.Morning, world.Scene.TimeOfDay);

        world.Tick(30);
        hour = 18;
        engine.StartSession();
        Assert.Equal(TimeOfDay.Evening, world.Scene.TimeOfDay);
        Assert.Equal(TimeOfDay.Morning, world.PreviousScene?.TimeOfDay);
        Assert.Equal(world.Time, world.SceneChangedAt);
    }

    [Fact]
    public void Without_a_clock_the_scene_keeps_its_time()
    {
        var world = new World();
        var evening = Scene.Default with { TimeOfDay = TimeOfDay.Evening };
        world.Load(0, [], [], scene: evening);
        _ = new ToyEngine(world, new Mixer(), new SpeechLibrary(new ClipStore(_dir), null), ToySettings.Default, new Random(1));
        Assert.Equal(evening, world.Scene);
    }

    /// <summary>An engine whose noise dice always roll <paramref name="roll"/> (0 always calls), and the shapes it called.</summary>
    private (ToyEngine Engine, List<Shape> Calls) CallingEngine(ToySettings settings, double? roll = null)
    {
        var engine = new ToyEngine(new World(), new Mixer(), new SpeechLibrary(new ClipStore(_dir), null),
            settings with { LanguageWeights = new() { ["is"] = 1 } }, new Random(3), roll is { } r ? new FixedRandom(r) : new Random(11));
        var calls = new List<Shape>();
        engine.AnimalCalled += calls.Add;
        return (engine, calls);
    }

    /// <summary>A screen spot whose tap makes an animal matching <paramref name="wanted"/>; see <see cref="ToyEngine.OnTouch"/>.</summary>
    private static (double X, double Y) TouchFor(Func<Shape, bool> wanted)
    {
        for (var row = 0; row < 4; row++)
            for (var column = 0; column < 6; column++)
            {
                var recipe = BuiltInRecipes.Palette[(column + (6 * row)) % BuiltInRecipes.Palette.Count];
                if (wanted(recipe.Body.Shape)) return ((column + 0.5) / 6, (row + 0.5) / 4);
            }
        throw new InvalidOperationException("no tap spot makes that kind of animal");
    }

    private static void StepEngine(ToyEngine engine, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.05) engine.Update(0.05);
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
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

    /// <summary>A press every two seconds for this long.</summary>
    private void PlayFor(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 2)
        {
            Press(0x25, "k");
            Step(Math.Min(2, seconds - t));
        }
    }

    private void Step(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.05) _engine.Update(0.05);
        _clock = _clock.AddSeconds(seconds);
    }

    /// <summary>A voice that takes as long as the test wants to make its first word.</summary>
    private sealed class SlowVoice : ISpeech
    {
        public ManualResetEventSlim Ready { get; } = new();

        public Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default) =>
            Task.Run(() =>
            {
                Ready.Wait(cancellationToken);
                using var s = new MemoryStream();
                WavFile.Write(s, new float[4800], Mixer.SampleRate);
                return (byte[]?)s.ToArray();
            }, cancellationToken);
    }

    private sealed class SilentVoice : ISpeech
    {
        public Task<byte[]?> SpeakAsync(string languageTag, string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }
}
