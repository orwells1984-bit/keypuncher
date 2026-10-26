using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Rendering;
using Keypuncher.Storage;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

/// <summary>The art rules as drawn: every shape at every level, bolder for the youngest, and never a flash
/// rate that broadcast and web guidelines forbid.</summary>
public sealed class ArtRulesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-art-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void Every_recipe_draws_at_every_level(CalmLevel level)
    {
        var world = new World { Style = ArtStyle.For(level), Limits = CalmLimits.BigToddler with { LiveThings = 100 } };
        var recipes = BuiltInRecipes.Palette.Concat(BuiltInRecipes.Concepts.Values).ToList();
        for (var i = 0; i < recipes.Count; i++)
            world.Add(new Entity
            {
                Id = world.NextId(), Role = recipes[i].Kind == RecipeKind.Effect ? EntityRole.Effect : EntityRole.Thing, Recipe = recipes[i],
                X = ((i % 8) + 0.5) / 8, Y = 0.15 + (0.2 * (i / 8)), Born = 0, Lifetime = recipes[i].Kind == RecipeKind.Effect ? 6 : double.PositiveInfinity,
            });
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Effect, Recipe = BuiltInRecipes.Burst, X = 0.5, Y = 0.5, Born = 0, Lifetime = 1.4 });
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Effect, Recipe = BuiltInRecipes.Sparkle, X = 0.3, Y = 0.5, Born = 0, Lifetime = 1 });
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Letter, Text = "Þ", X = 0.5, Y = 0.3, Born = 0, Lifetime = 2.2 });
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Word, Text = "fiðrildi", X = 0.5, Y = 0.62, Born = 0, Lifetime = 3.5 });
        world.Tick(0.5);

        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var frame = Render(renderer, world, 640, 360);
        var empty = Render(renderer, new World { Style = ArtStyle.For(level) }, 640, 360);
        Assert.InRange(Changed(frame, empty), 0.2, 1.0);
    }

    // Things born a second ago are fully popped in, and untouched by Tick they keep full energy, so they draw opaque.
    [Fact]
    public void A_baby_gets_bigger_things_with_thicker_outlines()
    {
        const int width = 1280, height = 720;   // big enough that the finest outline is a few pixels, not an antialiased one
        var pixels = new Dictionary<CalmLevel, (double Body, int Outline)>();
        foreach (var level in new[] { CalmLevel.Baby, CalmLevel.Toddler, CalmLevel.BigToddler })
        {
            var style = ArtStyle.For(level);
            var recipe = BuiltInRecipes.Palette[0];
            var world = new World { Style = style };
            world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = recipe, X = 0.5, Y = 0.5, Born = -1, Phase = 0.05 });
            using var renderer = new SceneRenderer(Fonts.LoadAndika());
            var frame = Render(renderer, world, width, height);
            var empty = Render(renderer, new World { Style = style }, width, height);
            // Walk right from the centre of the body until the outline colour starts, then measure its run.
            var outline = Rgb(style.Snap(recipe.Body.Hue).Outline);
            var row = Enumerable.Range(0, width / 2).Select(i => frame[(height / 2 * width) + (width / 2) + i]).ToList();
            var first = row.FindIndex(c => Near(c, outline));
            Assert.True(first >= 0, $"{level}: no outline found");
            var run = row.Skip(first).TakeWhile(c => Near(c, outline)).Count();
            pixels[level] = (Changed(frame, empty), run);
            Assert.True(run >= (style.MinStroke * height) - 1.5, $"{level}: outline {run} px, finest line {style.MinStroke * height:0.0} px");
        }
        Assert.True(pixels[CalmLevel.Baby].Body > pixels[CalmLevel.Toddler].Body * 1.5);
        Assert.True(pixels[CalmLevel.Toddler].Body > pixels[CalmLevel.BigToddler].Body * 1.5);
        Assert.True(pixels[CalmLevel.Baby].Outline > pixels[CalmLevel.Toddler].Outline);
        Assert.True(pixels[CalmLevel.Toddler].Outline > pixels[CalmLevel.BigToddler].Outline);
        // A baby's thing spans at least 6 degrees of view (0.18 of the shorter side): a disc of that
        // size is about 2.5 percent of a 16:9 frame, and the puffbird is wider than it is tall.
        Assert.True(pixels[CalmLevel.Baby].Body > 0.025);
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void Bodies_and_letters_take_the_level_s_palette(CalmLevel level)
    {
        var style = ArtStyle.For(level);
        using var renderer = new SceneRenderer(Fonts.LoadAndika());

        // A star is flat-filled, so with no eyes in the way its middle is exactly the snapped colour.
        var star = BuiltInRecipes.Palette[7] with { Eyes = 0 };
        var world = new World { Style = style };
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = star, X = 0.5, Y = 0.5, Born = -1, Phase = 0.05 });
        var frame = Render(renderer, world, 640, 360);
        Assert.Equal(Rgb(style.Snap(star.Body.Hue).Fill), frame[(180 * 640) + 320]);
        Assert.NotEqual(SKColor.FromHsv(star.Body.Hue, 45, 97), frame[(180 * 640) + 320]);

        // A letter is its own snapped colour with that colour's outline, not white any more.
        var letters = new World { Style = style };
        letters.Add(new Entity { Id = 1, Role = EntityRole.Letter, Text = "Þ", X = 0.5, Y = 0.5, Born = 0, Lifetime = 2.2 });
        letters.Tick(0.5);
        frame = Render(renderer, letters, 640, 360);
        var colour = style.Snap("Þ".Sum(ch => ch * 47) % 360);
        Assert.Contains(frame, c => c == Rgb(colour.Fill));
        Assert.Contains(frame, c => c == Rgb(colour.Outline));
        Assert.DoesNotContain(frame, c => c == SKColors.White);
    }

    [Fact]
    public void A_baby_s_creature_gets_a_pair_of_eyes_but_a_ball_stays_faceless()
    {
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        SKColor[] Eyes(CalmLevel level, Recipe recipe)
        {
            var world = new World { Style = ArtStyle.For(level) };
            world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = recipe, X = 0.5, Y = 0.5, Born = -1, Phase = 0.05 });
            return Render(renderer, world, 640, 360);
        }
        bool White(SKColor[] frame, Func<int, bool> column) =>
            frame.Where((c, i) => column(i % 640)).Any(c => c == SKColors.White);

        var fish = BuiltInRecipes.Palette[4];   // the striped fish shape, one eye in its recipe
        Assert.Equal(1, fish.Eyes);
        var baby = Eyes(CalmLevel.Baby, fish);
        Assert.True(White(baby, x => x < 310) && White(baby, x => x > 330), "a baby's fish has an eye each side of the middle");
        var big = Eyes(CalmLevel.BigToddler, fish);
        Assert.True(White(big, x => x is > 300 and < 340) && !White(big, x => x < 300), "an older child's fish keeps its one eye");

        var ball = BuiltInRecipes.Concepts["ball"];
        Assert.Equal(0, ball.Eyes);
        Assert.False(White(Eyes(CalmLevel.Baby, ball), _ => true), "a ball has no face at any level");
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void Every_animal_draws_with_a_face_even_if_its_recipe_says_no_eyes(CalmLevel level)
    {
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var style = ArtStyle.For(level);
        var empty = Render(renderer, new World { Style = style }, 640, 360);
        foreach (var animal in BuiltInRecipes.Animals.All)
        {
            var world = new World { Style = style };
            world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = animal with { Eyes = 0 }, X = 0.5, Y = 0.5, Born = -1, Phase = 0.05 });
            var frame = Render(renderer, world, 640, 360);
            Assert.True(frame.Any(c => c == SKColors.White), $"{animal.Name} has an eye at {level}");
            Assert.InRange(Changed(frame, empty), 0.001, 0.5);
        }
    }

    [Fact]
    public void Changing_the_level_repaints_the_sky_and_the_history()
    {
        var world = new World { Style = ArtStyle.BigToddler };
        world.Load(100, [], [new Entity { Id = 1, Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[9], X = 0.5, Y = 0.4, Born = 0 }]);
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var before = Render(renderer, world, 400, 300);
        world.Style = ArtStyle.Baby;
        var after = Render(renderer, world, 400, 300);
        // The sky and the near hill take the new style, and the whole frame, history layer included,
        // matches what a renderer that never saw the old style draws.
        foreach (var (x, y) in new[] { (10, 10), (200, 290) })
            Assert.NotEqual(before[(y * 400) + x], after[(y * 400) + x]);
        using var fresh = new SceneRenderer(Fonts.LoadAndika());
        Assert.Equal(Render(fresh, world, 400, 300), after);
    }

    [Fact]
    public void A_new_scene_fades_in_slowly_and_is_saved_with_the_world()
    {
        var world = new World { Style = ArtStyle.Toddler, Limits = CalmLimits.Toddler };
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var meadow = Render(renderer, world, 400, 300);
        world.SetScene(new Scene(Landscape.Beach, TimeOfDay.Evening, Weather.Clear, "Reynisfjara, Iceland", "A black sand beach."));
        Assert.Equal(meadow, Render(renderer, world, 400, 300));

        var half = CalmLimits.Toddler.SceneFadeSeconds / 2;
        world.Tick(half);
        var midway = Render(renderer, world, 400, 300);
        world.Tick(half);
        var beach = Render(renderer, world, 400, 300);
        using (var fresh = new SceneRenderer(Fonts.LoadAndika()))
            Assert.Equal(Render(fresh, world, 400, 300), beach);
        // The evening sky top and the sea where the meadow's far hill was are new colours, reached gradually.
        foreach (var (x, y) in new[] { (200, 5), (200, 200) })
        {
            var (a, m, b) = (meadow[(y * 400) + x], midway[(y * 400) + x], beach[(y * 400) + x]);
            Assert.NotEqual(a, b);
            Assert.InRange(m.Red, Math.Min(a.Red, b.Red), Math.Max(a.Red, b.Red));
            Assert.InRange(m.Blue, Math.Min(a.Blue, b.Blue), Math.Max(a.Blue, b.Blue));
            Assert.NotEqual(a, m);
            Assert.NotEqual(b, m);
        }

        var path = Path.Combine(_dir, "scene.db");
        using (var store = new WorldStore(path)) store.Save(world);
        var loaded = new World();
        using (var store = new WorldStore(path)) store.Load(loaded);
        Assert.Equal(world.Scene, loaded.Scene);
        Assert.Null(loaded.PreviousScene);
        loaded.Clear();
        Assert.Equal(Scene.Default, loaded.Scene);
    }

    [Fact]
    public void Every_landscape_and_weather_draws_its_own_backdrop()
    {
        var drawn = new HashSet<string>();
        foreach (var landscape in Enum.GetValues<Landscape>())
            foreach (var weather in Enum.GetValues<Weather>())
            {
                if (weather == Weather.Snow && Scene.Hot(landscape)) continue;
                var world = new World { Style = ArtStyle.Baby };
                world.Load(0, [], [], scene: new Scene(landscape, TimeOfDay.Midday, weather));
                using var renderer = new SceneRenderer(Fonts.LoadAndika());
                var pixels = Render(renderer, world, 160, 90);
                Assert.True(drawn.Add(string.Join(",", pixels.Select(p => (uint)p))), $"{landscape} {weather}");
            }
    }

    [Fact]
    public void A_new_landscape_or_time_of_day_changes_the_backdrop_at_a_glance()
    {
        // Stephen couldn't see the move to a forest: recolouring two hills was not enough. Each landscape now
        // has its own silhouettes, and the sun moves across the sky with the time of day.
        static SKColor[] Backdrop(Scene scene)
        {
            var world = new World { Style = ArtStyle.Toddler };
            world.Load(0, [], [], scene: scene);
            using var renderer = new SceneRenderer(Fonts.LoadAndika());
            return Render(renderer, world, 320, 180);
        }
        var landscapes = Enum.GetValues<Landscape>().Select(l => (l, Backdrop(new Scene(l)))).ToList();
        foreach (var (a, pixels) in landscapes)
            foreach (var (b, other) in landscapes.Where(o => o.l > a))
                Assert.True(Changed(pixels, other) > 0.05, $"{a} and {b} differ in only {Changed(pixels, other):P1} of the screen");
        var times = Enum.GetValues<TimeOfDay>().Select(t => (t, Backdrop(new Scene(Landscape.Meadow, t)))).ToList();
        foreach (var (a, pixels) in times)
            foreach (var (b, other) in times.Where(o => o.t > a))
                Assert.True(Changed(pixels, other) > 0.01, $"{a} and {b} differ in only {Changed(pixels, other):P1} of the screen");
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void The_backdrop_is_never_darker_than_its_ground(CalmLevel level)
    {
        // Trees, barns and dunes are drawn in the ground's own colours or lighter (pines in snow at the level's own
        // hill brightness), so every outline keeps the contrast the Art Rules set, whatever stands behind it.
        var style = ArtStyle.For(level);
        foreach (var landscape in Enum.GetValues<Landscape>())
            foreach (var time in Enum.GetValues<TimeOfDay>())
            {
                var scene = new Scene(landscape, time);
                var g = SceneGround.For(style, scene);
                var floor = new[] { g.SkyTop, g.SkyBottom, g.FarHill, g.NearHill, style.Ground.NearHill }.Min(SceneGround.Luminance) - 0.02;
                var world = new World { Style = style };
                world.Load(0, [], [], scene: scene);
                using var renderer = new SceneRenderer(Fonts.LoadAndika());
                var darkest = Render(renderer, world, 320, 180).Min(p => SceneGround.Luminance((uint)((p.Red << 16) | (p.Green << 8) | p.Blue)));
                Assert.True(darkest >= floor, $"{scene} at {level}: {darkest:0.000} below {floor:0.000}");
            }
    }

    [Fact]
    public void A_settled_thing_fades_towards_the_sky_not_towards_its_outline()
    {
        // Everything at rest is drawn at six tenths; the outline under a fill must not darken the fill.
        var star = BuiltInRecipes.Palette[7] with { Eyes = 0 };
        var world = new World { Style = ArtStyle.Baby };
        var thing = world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = star, X = 0.5, Y = 0.5, Born = -1, Phase = 0.05 });
        thing.Energy = 0;
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var frame = Render(renderer, world, 640, 360);
        var sky = Render(renderer, new World { Style = ArtStyle.Baby }, 640, 360)[(180 * 640) + 320];
        var fill = Rgb(ArtStyle.Baby.Snap(star.Body.Hue).Fill);
        var middle = frame[(180 * 640) + 320];
        Assert.InRange(middle.Red, Blend(fill.Red, sky.Red) - 3, Blend(fill.Red, sky.Red) + 3);
        Assert.InRange(middle.Green, Blend(fill.Green, sky.Green) - 3, Blend(fill.Green, sky.Green) + 3);
        Assert.InRange(middle.Blue, Blend(fill.Blue, sky.Blue) - 3, Blend(fill.Blue, sky.Blue) + 3);

        static int Blend(byte fill, byte sky) => (int)Math.Round((0.6 * fill) + (0.4 * sky));
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void A_hammering_child_sees_no_more_than_three_flashes_a_second(CalmLevel level)
    {
        // WCAG 2.3.1 and the Ofcom guidance: no more than three flashes (pairs of opposing changes of
        // at least a tenth in relative luminance, the darker side below 0.8) in any second, in any
        // patch about ten degrees across, which from a lap is about 0.3 of the screen height.
        var world = new World();
        var settings = ToySettings.Default with { Calm = level, LanguageWeights = new() { ["is"] = 1 } };
        var engine = new ToyEngine(world, new Mixer(), new SpeechLibrary(new ClipStore(_dir), null), settings, new Random(3));
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        const int width = 320, height = 180, fps = 30;
        var tile = (int)(0.3 * height);
        var tiles = new List<(int X, int Y)>();
        for (var y = 0; y + tile <= height; y += tile / 2)
            for (var x = 0; x + tile <= width; x += tile / 2)
                tiles.Add((x, y));
        var luminance = tiles.Select(_ => new List<double>()).ToList();

        var clock = DateTimeOffset.UnixEpoch;
        var table = Enumerable.Range(0, 256).Select(v => Linear(v / 255.0)).ToArray();
        for (var frame = 0; frame < 5 * fps; frame++)
        {
            var t = frame / (double)fps;
            // The same letter four times a second, a palm mash every half second, and other keys between.
            // Keys only: a tap is stamped with the wall clock, which would fight this scripted one.
            if (frame % (fps / 4) == 0 && t < 3) Press(engine, ref clock, 0x25, "k");
            if (frame % (fps / 2) == 0 && t is >= 1 and < 4) Mash(engine, ref clock, 0x1E);
            if (frame % (fps / 3) == 5) Press(engine, ref clock, 0x10 + ((frame / 10) % 10), "q");
            engine.Update(1.0 / fps);
            clock = clock.AddSeconds(1.0 / fps);

            var pixels = Render(renderer, world, width, height);
            for (var i = 0; i < tiles.Count; i++)
            {
                var (x0, y0) = tiles[i];
                var sum = 0.0;
                for (var y = y0; y < y0 + tile; y++)
                    for (var x = x0; x < x0 + tile; x++)
                    {
                        var c = pixels[(y * width) + x];
                        sum += (0.2126 * table[c.Red]) + (0.7152 * table[c.Green]) + (0.0722 * table[c.Blue]);
                    }
                luminance[i].Add(sum / (tile * tile));
            }
        }

        Assert.True(world.Live.Count + world.Baked.Count > 3, "the script put things on screen");
        var worst = luminance.Max(series => MostTransitionsInASecond(series, fps));
        Assert.True(worst <= 6, $"{worst} luminance transitions in one second in one patch; the limit is six (three flashes)");
    }

    [Fact]
    public void The_flash_counter_reads_a_series_the_way_the_guidelines_do()
    {
        const int fps = 30;
        List<double> Square(double low, double high, int framesPerHalf, int halves) =>
            Enumerable.Range(0, halves).SelectMany(h => Enumerable.Repeat(h % 2 == 0 ? high : low, framesPerHalf)).ToList();
        Assert.Equal(0, MostTransitionsInASecond(Square(0.85, 0.99, 5, 12), fps));   // all brighter than 0.8: not a flash
        Assert.Equal(0, MostTransitionsInASecond(Square(0.60, 0.68, 5, 12), fps));   // under a tenth: not a flash
        Assert.Equal(6, MostTransitionsInASecond(Square(0.3, 0.6, 5, 12), fps));     // 3 Hz: six changes a second
        Assert.Equal(8, MostTransitionsInASecond(Square(0.3, 0.6, 4, 12), fps));     // 3.75 Hz
        Assert.Equal(7, MostTransitionsInASecond(Square(0.3, 0.6, 4, 8), fps));      // seven changes within a second, the first one counted
        Assert.Equal(1, MostTransitionsInASecond([.. Enumerable.Repeat(0.7, 10), .. Enumerable.Repeat(0.4, 50)], fps));
    }

    /// <summary>Counts changes of at least 0.1 in relative luminance with the darker side below 0.8, as a
    /// flash tester does, and returns the most of them in any one-second window.</summary>
    private static int MostTransitionsInASecond(List<double> series, int fps)
    {
        var transitions = new List<int>();
        var extreme = series[0];
        var rising = 0;
        for (var i = 1; i < series.Count; i++)
        {
            var delta = series[i] - extreme;
            var direction = Math.Sign(delta);
            if (direction == 0) continue;
            if (direction == rising) { extreme = series[i]; continue; }
            if (Math.Abs(delta) < 0.1 || Math.Min(series[i], extreme) >= 0.8) continue;
            transitions.Add(i);
            rising = direction;
            extreme = series[i];
        }
        return transitions.Count == 0 ? 0 : transitions.Max(at => transitions.Count(other => other > at - fps && other <= at));
    }

    private static double Linear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static void Press(ToyEngine engine, ref DateTimeOffset clock, int scan, string ch)
    {
        engine.OnKey(new KeyPress(clock, scan, ch, false, true));
        clock = clock.AddMilliseconds(60);
        engine.OnKey(new KeyPress(clock, scan, ch, false, false));
    }

    private static void Mash(ToyEngine engine, ref DateTimeOffset clock, int firstScan)
    {
        for (var i = 0; i < 5; i++)
        {
            engine.OnKey(new KeyPress(clock, firstScan + i, null, false, true));
            clock = clock.AddMilliseconds(8);
        }
        for (var i = 0; i < 5; i++)
            engine.OnKey(new KeyPress(clock, firstScan + i, null, false, false));
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.BigToddler)]
    public void The_Esc_hold_cue_is_a_small_ring_in_the_top_left_corner(CalmLevel level)
    {
        const int width = 1280, height = 720;
        var world = new World { Style = ArtStyle.For(level) };
        using var renderer = new SceneRenderer(Fonts.LoadAndika());
        var plain = Render(renderer, world, width, height);
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) renderer.Draw(canvas, width, height, world, holdCue: (0.75, 1));
        var cued = bitmap.Pixels;

        var changed = Enumerable.Range(0, cued.Length).Where(i => !Near(cued[i], plain[i])).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.True(i % width < width * 0.1 && i / width < height * 0.15, "the cue stays in the corner"));
        Assert.True(changed.Count < cued.Length * 0.01, "the cue is small");
        Assert.Contains(changed, i => Near(cued[i], Rgb(world.Style.Ink)));
    }

    private static SKColor[] Render(SceneRenderer renderer, World world, int width, int height)

    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, width, height, world);
        return bitmap.Pixels;
    }

    /// <summary>The share of pixels that differ noticeably between two frames of the same size.</summary>
    private static double Changed(SKColor[] a, SKColor[] b) =>
        a.Zip(b).Count(p => Math.Abs(p.First.Red - p.Second.Red) + Math.Abs(p.First.Green - p.Second.Green) + Math.Abs(p.First.Blue - p.Second.Blue) > 24) / (double)a.Length;

    private static SKColor Rgb(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static bool Near(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) <= 40;
}
