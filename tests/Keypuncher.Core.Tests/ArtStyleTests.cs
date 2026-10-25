using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class ArtStyleTests
{
    private static readonly ArtStyle[] Levels = [ArtStyle.For(CalmLevel.Baby), ArtStyle.For(CalmLevel.Toddler), ArtStyle.For(CalmLevel.BigToddler)];

    /// <summary>Every scene the director can choose: each landscape at each time of day in each weather.</summary>
    private static IEnumerable<Scene> Scenes() =>
        from l in Enum.GetValues<Landscape>()
        from t in Enum.GetValues<TimeOfDay>()
        from w in Enum.GetValues<Weather>()
        where !(w == Weather.Snow && Scene.Hot(l))
        select new Scene(l, t, w);

    /// <summary>The style's own ground and every scene's ground at that style.</summary>
    private static IEnumerable<Ground> Grounds(ArtStyle style) => Scenes().Select(s => SceneGround.For(style, s)).Prepend(style.Ground);

    [Fact]
    public void Art_gets_finer_with_age()
    {
        foreach (var (younger, older) in Levels.Zip(Levels.Skip(1)))
        {
            Assert.True(younger.SizeScale > older.SizeScale);
            Assert.True(younger.MinDiameter > older.MinDiameter);
            Assert.True(younger.MinStroke > older.MinStroke);
            Assert.True(younger.StrokeOfRadius > older.StrokeOfRadius);
            Assert.True(younger.EyeScale > older.EyeScale);
            Assert.True(younger.MinPupil > older.MinPupil);
            Assert.True(younger.Highlight < older.Highlight);
            Assert.True(younger.MaxLobes < older.MaxLobes);
            Assert.True(younger.MaxStripes < older.MaxStripes);
            Assert.True(younger.MaxDots < older.MaxDots);
            Assert.True(younger.Colors.Count <= older.Colors.Count);
        }
        Assert.All(Levels, s => Assert.True(s.MinDiameter < s.MaxDiameter));
        Assert.All(Levels, s => Assert.InRange(s.MaxEyes, s.MinEyes, 4));
        Assert.All(Levels, s => Assert.True(s.MaxDots >= 4 && s.MaxLobes >= 4));
    }

    [Fact]
    public void A_baby_sees_six_degree_bodies_and_half_degree_lines()
    {
        // From a lap in front of a 27-inch screen one degree is about 0.03 of the shorter side.
        var baby = ArtStyle.Baby;
        Assert.InRange(baby.MinDiameter / 0.03, 5.5, 8);
        Assert.InRange(baby.MinStroke / 0.03, 0.5, 0.8);
        Assert.InRange(baby.MinPupil / 0.03, 0.9, 1.2);
        Assert.Equal(2, baby.MinEyes);
        Assert.Equal(0, baby.MaxStripes);
    }

    [Fact]
    public void Outlines_stand_out_from_the_ground_and_the_fill()
    {
        // WCAG contrast ratios. The outline carries the luminance contrast, since a yellow body can
        // never differ much from a pale sky; the fill's job is saturated colour.
        foreach (var style in Levels)
        foreach (var scene in Grounds(style))
        {
            var ground = new[] { scene.SkyTop, scene.SkyBottom, scene.FarHill, scene.NearHill };
            var floor = style == ArtStyle.Baby ? 9 : style == ArtStyle.Toddler ? 7 : 4.5;
            foreach (var colour in style.Colors)
            {
                Assert.All(ground, g => Assert.True(Contrast(colour.Outline, g) >= floor, $"{colour.Outline:X6} on {g:X6}: {Contrast(colour.Outline, g):0.0}"));
                Assert.True(Contrast(colour.Outline, colour.Fill) >= 1.5, $"{colour.Outline:X6} on {colour.Fill:X6}");
            }
            Assert.All(ground, g => Assert.True(Contrast(style.Ink, g) >= floor));
            Assert.All(ground, g => Assert.True(Contrast(style.Stem, g) >= 3));
        }
    }

    [Fact]
    public void The_ground_stays_quiet()
    {
        // Sky and hills close in tone, so a saturated thing is what stands out, in every scene.
        foreach (var style in Levels)
        foreach (var g in Grounds(style))
        {
            Assert.True(Contrast(g.SkyTop, g.SkyBottom) < 1.5);
            Assert.True(Contrast(g.SkyBottom, g.FarHill) < 1.6);
            Assert.True(Contrast(g.FarHill, g.NearHill) < 1.5);
            Assert.True(Luminance(g.NearHill) > 0.4);
        }
    }

    [Fact]
    public void A_scene_keeps_the_brightness_of_the_ground_and_only_snow_and_fog_lighten_it()
    {
        foreach (var style in Levels)
        foreach (var scene in Scenes())
        {
            var g = SceneGround.For(style, scene);
            var lighter = scene.Weather is Weather.Snow or Weather.Fog || scene.Landscape == Landscape.SnowyHills;
            Assert.InRange(Luminance(g.SkyTop), Luminance(style.Ground.SkyTop) - 0.01, Luminance(style.Ground.SkyTop) + 0.01);
            Assert.InRange(Luminance(g.SkyBottom), Luminance(style.Ground.SkyBottom) - 0.01, Luminance(style.Ground.SkyBottom) + 0.01);
            foreach (var (hill, own) in new[] { (g.FarHill, style.Ground.FarHill), (g.NearHill, style.Ground.NearHill) })
                if (lighter) Assert.True(Luminance(hill) >= Luminance(own) - 0.01, $"{scene}");
                else Assert.InRange(Luminance(hill), Luminance(own) - 0.01, Luminance(own) + 0.01);
        }
        Assert.Equal(ArtStyle.Toddler.Ground, SceneGround.For(ArtStyle.Toddler, Scene.Default));
        Assert.NotEqual(ArtStyle.Toddler.Ground, SceneGround.For(ArtStyle.Toddler, new Scene(Landscape.Beach, TimeOfDay.Evening)));
    }

    [Fact]
    public void No_palette_colour_is_a_saturated_red()
    {
        // Things pop in and fade out; a flash to or from saturated red is the riskiest kind (WCAG 2.3.1).
        foreach (var rgb in Levels.SelectMany(s => s.Colors).SelectMany(c => new[] { c.Fill, c.Outline }))
        {
            double r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            Assert.True(r / (r + g + b) < 0.8, $"{rgb:X6}");
        }
    }

    [Fact]
    public void Hues_snap_to_the_nearest_palette_colour()
    {
        var baby = ArtStyle.Baby;
        Assert.Equal(0xE53935u, baby.Snap(0).Fill);
        Assert.Equal(0xE53935u, baby.Snap(355).Fill);
        Assert.Equal(0xE53935u, baby.Snap(-5).Fill);
        Assert.Equal(0x1E6FD9u, baby.Snap(200).Fill);
        Assert.Equal(0xFFC400u, baby.Snap(50).Fill);
        Assert.Equal(0x1FA34Au, baby.Snap(120).Fill);
        Assert.NotEqual(baby.Snap(50).Fill, baby.Companion(50).Fill);
        // Toddler red sits at 1 degree, brown at 23 and orange at 29: the boundaries fall where the hues say.
        Assert.Equal(0x8D5A3Bu, ArtStyle.Toddler.Snap(20).Fill);
        Assert.Equal(0xFF7A00u, ArtStyle.Toddler.Snap(30).Fill);
        foreach (var style in Levels)
            foreach (var hue in Enumerable.Range(0, 360))
            {
                // Worked out independently: the smallest distance round the hue circle.
                var nearest = style.Colors.MinBy(c => Math.Min(Math.Abs(c.Hue - hue), 360 - Math.Abs(c.Hue - hue)))!;
                Assert.Equal(nearest, style.Snap(hue));
                Assert.NotEqual(style.Snap(hue), style.Companion(hue));
            }
    }

    [Fact]
    public void Bursts_and_sparkles_are_yellow_at_every_level()
    {
        // They flash in and out, and yellow is as far from red as a warm hue gets.
        Assert.All(Levels, s => Assert.Equal(0xFFC400u, s.Snap(BuiltInRecipes.Burst.Body.Hue).Fill));
        Assert.All(Levels, s => Assert.Equal(0xFFC400u, s.Snap(BuiltInRecipes.Sparkle.Body.Hue).Fill));
    }

    [Fact]
    public void Palette_hues_are_read_from_the_fill()
    {
        Assert.InRange(new ArtColor(0xFF0000, 0).Hue, 0, 0.01);
        Assert.InRange(new ArtColor(0x00FF00, 0).Hue, 119.99, 120.01);
        Assert.InRange(new ArtColor(0x0000FF, 0).Hue, 239.99, 240.01);
        Assert.InRange(new ArtColor(0x1E6FD9, 0).Hue, 210, 220);
        Assert.Equal(0, new ArtColor(0x808080, 0).Hue);
        Assert.InRange((new ArtColor(0x1E6FD9, 0) with { Fill = 0x00FF00 }).Hue, 119.99, 120.01);
    }

    /// <summary>WCAG 2 contrast ratio between two 0xRRGGBB colours.</summary>
    private static double Contrast(uint a, uint b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint rgb)
    {
        static double Channel(uint v)
        {
            var c = v / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Channel((rgb >> 16) & 0xFF)) + (0.7152 * Channel((rgb >> 8) & 0xFF)) + (0.0722 * Channel(rgb & 0xFF));
    }
}
