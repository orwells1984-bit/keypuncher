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

    [Fact]
    public void A_baby_gets_bigger_things_with_thicker_outlines()
    {
        var pixels = new Dictionary<CalmLevel, (double Body, double Ink)>();
        foreach (var level in new[] { CalmLevel.Baby, CalmLevel.Toddler, CalmLevel.BigToddler })
        {
            var style = ArtStyle.For(level);
            var world = new World { Style = style };
            world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[0], X = 0.5, Y = 0.4, Born = 0 });
            world.Tick(1);
            using var renderer = new SceneRenderer(Fonts.LoadAndika());
            var frame = Render(renderer, world, 640, 360);
            var empty = Render(renderer, new World { Style = style }, 640, 360);
            pixels[level] = (Changed(frame, empty), Share(frame, c => c.Red + c.Green + c.Blue < 200));
        }
        Assert.True(pixels[CalmLevel.Baby].Body > pixels[CalmLevel.Toddler].Body * 1.5);
        Assert.True(pixels[CalmLevel.Toddler].Body > pixels[CalmLevel.BigToddler].Body * 1.5);
        Assert.True(pixels[CalmLevel.Baby].Ink > pixels[CalmLevel.Toddler].Ink * 1.5);
        Assert.True(pixels[CalmLevel.Toddler].Ink > pixels[CalmLevel.BigToddler].Ink * 1.5);
        // A baby's thing spans at least 6 degrees of view (0.18 of the shorter side): a disc of that
        // size is about 2.5 percent of a 16:9 frame, and the puffbird is wider than it is tall.
        Assert.True(pixels[CalmLevel.Baby].Body > 0.025);
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
        // The sky, the near hill and the faded thing in the middle all take the new style.
        foreach (var (x, y) in new[] { (10, 10), (200, 290), (200, 120) })
            Assert.NotEqual(before[(y * 400) + x], after[(y * 400) + x]);
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
            // The same letter four times a second, a palm mash every half second, and taps all over.
            if (frame % (fps / 4) == 0 && t < 3) Press(engine, ref clock, 0x25, "k");
            if (frame % (fps / 2) == 0 && t is >= 1 and < 4) Mash(engine, ref clock, 0x1E);
            if (frame % (fps / 3) == 0) engine.OnTouch(0.2 + (0.6 * ((frame / 10) % 4) / 3), 0.3, 1);
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

        var worst = luminance.Max(series => MostTransitionsInASecond(series, fps));
        Assert.True(worst <= 6, $"{worst} luminance transitions in one second in one patch; the limit is six (three flashes)");
    }

    /// <summary>Counts changes of at least 0.1 that reverse direction, as a flash tester does, and returns the
    /// most of them in any one-second window.</summary>
    private static int MostTransitionsInASecond(List<double> series, int fps)
    {
        var transitions = new List<int>();
        var extreme = series[0];
        var rising = 0;
        for (var i = 1; i < series.Count; i++)
        {
            var delta = series[i] - extreme;
            var direction = Math.Sign(delta);
            if (direction == 0 || direction == rising) { if (direction == rising) extreme = series[i]; continue; }
            if (Math.Abs(delta) < 0.1 || Math.Min(series[i], extreme) >= 0.8) continue;
            if (rising != 0) transitions.Add(i);
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

    private static double Share(SKColor[] frame, Func<SKColor, bool> test) => frame.Count(test) / (double)frame.Length;
}
