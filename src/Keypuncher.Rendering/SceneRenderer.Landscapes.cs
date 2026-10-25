using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// The land in the backdrop: hill lines shaped for each landscape and the things that make it that landscape at
/// a glance (a line of trees, the sea, dunes, snow on the peaks, a barn and a fence, acacias, a volcano), and the
/// sun's place in the sky. All of it is silhouettes in the ground's own four colours, a mix of them, or white,
/// so a new scene changes the backdrop's shape and hue but never its tone: the Art Rules' contrast holds in every
/// scene. Trees and other tall things stand towards the sides, leaving the middle for the things.
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>How a hill line runs.</summary>
    private enum Profile { Rolling, Peaks, Flat, Canopy, Dunes }

    private readonly record struct HillLine(Profile Profile, double Level, double Amplitude, double Waves, double Offset)
    {
        /// <summary>The line's height at <paramref name="u"/> across, as a fraction of the screen height.</summary>
        public double At(double u)
        {
            var a = (u * Waves * Math.PI) + Offset;
            var rise = Profile switch
            {
                // Soft-topped peaks: a raised sine, sharpened a little, never to a point.
                Profile.Peaks => (Math.Pow((Math.Sin(a) + 1) / 2, 1.6) * 2) - 1,
                // A tree line: round bumps of uneven height.
                Profile.Canopy => (Math.Abs(Math.Sin(a)) * (0.7 + (0.3 * Math.Sin(a * 0.37))) * 2) - 1,
                // Dunes lean one way: a long gentle side and a steeper one.
                Profile.Dunes => Math.Sin(a + (0.5 * Math.Sin(a))),
                Profile.Flat => 0,
                _ => Math.Sin(a),
            };
            return Level - (rise * Amplitude);
        }
    }

    /// <summary>The far and near hill lines for a landscape. The near hill's top stays near three quarters of the
    /// way down everywhere, because plants are rooted there.</summary>
    private static (HillLine Far, HillLine Near) Hills(Landscape landscape) => landscape switch
    {
        Landscape.Mountains => (new(Profile.Peaks, 0.57, 0.17, 3, 0.3), new(Profile.Rolling, 0.74, 0.03, 2.1, 1.7)),
        Landscape.Beach => (new(Profile.Flat, 0.63, 0, 0, 0), new(Profile.Rolling, 0.76, 0.012, 1.2, 0.8)),
        Landscape.Lake => (new(Profile.Flat, 0.65, 0, 0, 0), new(Profile.Rolling, 0.75, 0.02, 1.6, 2.2)),
        Landscape.Forest => (new(Profile.Canopy, 0.63, 0.02, 11, 0.5), new(Profile.Rolling, 0.74, 0.03, 2.1, 1.7)),
        Landscape.Jungle => (new(Profile.Canopy, 0.6, 0.04, 7, 1.1), new(Profile.Canopy, 0.735, 0.015, 14, 0.2)),
        Landscape.Desert => (new(Profile.Dunes, 0.64, 0.07, 1.6, 0.6), new(Profile.Dunes, 0.75, 0.04, 2.2, 1.9)),
        Landscape.Savanna => (new(Profile.Rolling, 0.68, 0.01, 1.1, 0.2), new(Profile.Rolling, 0.745, 0.012, 1.7, 1.3)),
        Landscape.LavaField => (new(Profile.Canopy, 0.67, 0.014, 15, 0.9), new(Profile.Canopy, 0.745, 0.012, 21, 0.4)),
        Landscape.SnowyHills => (new(Profile.Rolling, 0.62, 0.06, 1.5, 0.9), new(Profile.Rolling, 0.74, 0.03, 2.1, 1.7)),
        _ => (new(Profile.Rolling, 0.66, 0.035, 1.3, 0.4), new(Profile.Rolling, 0.74, 0.03, 2.1, 1.7)),
    };

    /// <summary>The ground's colours as paints' colours, plus a distant tone halfway between the sky and the far hill.</summary>
    private readonly record struct Tones(SKColor Distant, SKColor Far, SKColor Near, SKColor Between);

    private static void Land(SKCanvas c, int width, int height, Scene scene, ArtStyle style, Ground ground, Random random)
    {
        var tones = new Tones(
            Mix(ground.SkyBottom, ground.FarHill, 0.5), RecipePainter.Rgb(ground.FarHill), RecipePainter.Rgb(ground.NearHill),
            Mix(ground.FarHill, ground.NearHill, 0.5));
        var (far, near) = Hills(scene.Landscape);
        var unit = Math.Min(width, height);
        var landscape = scene.Landscape;

        // Behind the far hill.
        switch (landscape)
        {
            case Landscape.Lake:
                using (var p = HillPath(new HillLine(Profile.Rolling, 0.6, 0.045, 1.7, 0.6), width, height)) Fill(c, p, tones.Distant);
                break;
            case Landscape.Beach:
                using (var p = HillPath(new HillLine(Profile.Rolling, 0.635, 0.05, 1, 0), width * 0.4f, height)) Fill(c, p, tones.Distant);
                break;
            case Landscape.Desert:
                using (var p = HillPath(new HillLine(Profile.Dunes, 0.6, 0.04, 2.6, 1.2), width, height)) Fill(c, p, tones.Distant);
                break;
            case Landscape.LavaField:
                Volcano(c, width, height, tones.Distant);
                break;
            case Landscape.Mountains:
                using (var p = HillPath(new HillLine(Profile.Peaks, 0.55, 0.1, 3.6, 1.4), width, height)) Fill(c, p, tones.Distant);
                break;
        }

        using var farPath = HillPath(far, width, height);
        Fill(c, farPath, tones.Far, snowCap: landscape == Landscape.Mountains && scene.Snows ? 0.5 : null, height: height);

        // On the far hill.
        switch (landscape)
        {
            case Landscape.Forest:
                TreeLine(c, width, height, far, random, tones.Far, count: 26, tall: 0.11, pines: 0.6);
                break;
            case Landscape.SnowyHills:
                TreeLine(c, width, height, far, random, SnowyTree(style), count: 14, tall: 0.08, pines: 1, snowy: true);
                break;
            case Landscape.Meadow:
                foreach (var u in new[] { 0.12, 0.83 }) RoundTree(c, (float)(u * width), (float)(far.At(u) * height), (float)(height * 0.08), tones.Far);
                break;
            case Landscape.Farm:
                Fields(c, farPath, width, height, tones);
                Barn(c, width, height, far, tones.Near);
                break;
            case Landscape.Jungle:
                for (var i = 0; i < 9; i++)
                {
                    var u = (i + 0.5 + ((random.NextDouble() - 0.5) * 0.5)) / 9;
                    Crown(c, (float)(u * width), (float)((far.At(u) - 0.02) * height), (float)(height * (0.035 + (random.NextDouble() * 0.02))), tones.Far);
                }
                break;
            case Landscape.Beach or Landscape.Lake:
                Ripples(c, width, height, far.Level, near.Level - 0.02, random, lines: landscape == Landscape.Beach ? 14 : 9);
                break;
        }

        using (var nearPath = HillPath(near, width, height)) Fill(c, nearPath, tones.Near);

        // On the near hill, mostly at the sides.
        switch (landscape)
        {
            case Landscape.Forest:
                RoundTree(c, width * 0.05f, (float)(near.At(0.05) * height), (float)(height * 0.2), tones.Near);
                Pine(c, width * 0.95f, (float)(near.At(0.95) * height), (float)(height * 0.26), tones.Near);
                Pine(c, width * 0.88f, (float)(near.At(0.88) * height), (float)(height * 0.18), tones.Near);
                break;
            case Landscape.Jungle:
                Palm(c, width * 0.06f, (float)(near.At(0.06) * height), (float)(height * 0.3), unit, tones.Near, lean: 1);
                Palm(c, width * 0.93f, (float)(near.At(0.93) * height), (float)(height * 0.24), unit, tones.Near, lean: -1);
                break;
            case Landscape.Savanna:
                foreach (var (u, tall) in new[] { (0.1, 0.15), (0.78, 0.11), (0.92, 0.17) })
                    Acacia(c, (float)(u * width), (float)(near.At(u) * height), (float)(tall * height), unit, tones.Near);
                Tufts(c, width, height, near, random, tones.Far, unit);
                break;
            case Landscape.Farm:
                Fence(c, width, height, near, unit, tones.Far);
                break;
            case Landscape.Meadow:
                Flowers(c, width, height, near, random, unit);
                break;
            case Landscape.Beach:
                Shore(c, width, height, near, unit);
                break;
            case Landscape.Desert:
                DuneLines(c, width, height, near, random, unit, tones.Far);
                break;
            case Landscape.LavaField:
                Mounds(c, width, height, near, random, unit, tones.Between);
                break;
            case Landscape.SnowyHills:
                Pine(c, width * 0.94f, (float)(near.At(0.94) * height), (float)(height * 0.2), SnowyTree(style), snowy: true);
                break;
        }
    }

    /// <summary>The sun, low on the left at dawn, high at midday and low on the right in the evening, with a warm
    /// glow at either end of the day. Pale and soft: lighter than the sky, never a bright spot.</summary>
    private static void Sun(SKCanvas c, int width, int height, TimeOfDay time)
    {
        var (u, v, warm) = time switch
        {
            TimeOfDay.Dawn => (0.1, 0.6, 1.0),
            TimeOfDay.Morning => (0.24, 0.3, 0.35),
            TimeOfDay.Midday => (0.5, 0.11, 0),
            TimeOfDay.Afternoon => (0.76, 0.3, 0.35),
            _ => (0.9, 0.6, 1.0),
        };
        var unit = Math.Min(width, height);
        var centre = new SKPoint((float)(u * width), (float)(v * height));
        var glow = new SKColor(255, (byte)(236 - (50 * warm)), (byte)(190 - (100 * warm)));

        // Near the ends of the day the whole horizon warms.
        if (warm > 0.5)
        {
            using var band = SKShader.CreateRadialGradient(centre, width * 0.7f, [glow.WithAlpha(110), glow.WithAlpha(0)], [0, 1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = band };
            c.DrawRect(0, 0, width, height, paint);
        }

        var r = unit * 0.06f;
        using (var halo = SKShader.CreateRadialGradient(centre, r * 3.2f, [glow.WithAlpha(150), glow.WithAlpha(0)], [0.3f, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = halo, IsAntialias = true })
        {
            c.DrawCircle(centre, r * 3.2f, paint);
        }
        using var disc = new SKPaint { IsAntialias = true, Color = new SKColor(255, (byte)(250 - (25 * warm)), (byte)(228 - (60 * warm)), 235) };
        c.DrawCircle(centre, r, disc);
    }

    private static SKPath HillPath(HillLine line, float width, int height)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(0, height);
        const int steps = 120;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (double)steps;
            b.LineTo((float)(u * width), (float)(line.At(u) * height));
        }
        b.LineTo(width, height);
        b.Close();
        return b.Detach();
    }

    /// <summary>Fills a hill; with <paramref name="snowCap"/>, whitens it above that fraction of the way down
    /// from the top of the screen to the hill's foot, with a soft wavy edge.</summary>
    private static void Fill(SKCanvas c, SKPath path, SKColor color, double? snowCap = null, int height = 0)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        c.DrawPath(path, paint);
        if (snowCap is not { } cap) return;
        var top = path.Bounds.Top;
        var edge = top + ((path.Bounds.Bottom - top) * (float)(cap * 0.35));
        c.Save();
        c.ClipPath(path, antialias: true);
        using var b = new SKPathBuilder();
        b.MoveTo(0, 0);
        const int steps = 60;
        for (var i = 0; i <= steps; i++)
        {
            var x = path.Bounds.Width * i / steps;
            b.LineTo(x, edge + (float)(Math.Sin(i * 1.7) * height * 0.012));
        }
        b.LineTo(path.Bounds.Width, 0);
        b.Close();
        using var snow = b.Detach();
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(215) };
        c.DrawPath(snow, white);
        c.Restore();
    }

    /// <summary>A pine green (or grey-green) at the style's own near-hill brightness, for trees standing in snow.</summary>
    private static SKColor SnowyTree(ArtStyle style) =>
        RecipePainter.Rgb(SceneGround.WithLuminance(150, 0.22, SceneGround.Luminance(style.Ground.NearHill)));

    private static void TreeLine(SKCanvas c, int width, int height, HillLine line, Random random, SKColor color, int count, double tall, double pines, bool snowy = false)
    {
        for (var i = 0; i < count; i++)
        {
            var u = (i + 0.5 + ((random.NextDouble() - 0.5) * 0.7)) / count;
            var h = (float)(height * tall * (0.65 + (random.NextDouble() * 0.5)));
            var x = (float)(u * width);
            var y = (float)((line.At(u) + 0.01) * height);
            if (random.NextDouble() < pines) Pine(c, x, y, h, color, snowy);
            else RoundTree(c, x, y, h, color);
        }
    }

    /// <summary>A fir of three soft tiers on a short trunk, its foot at (x, y); snowy ones have white tops.</summary>
    private static void Pine(SKCanvas c, float x, float y, float h, SKColor color, bool snowy = false)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = h * 0.04f, Style = SKPaintStyle.StrokeAndFill };
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(220) };
        c.DrawRect(x - (h * 0.04f), y - (h * 0.2f), h * 0.08f, h * 0.2f, paint);
        for (var tier = 0; tier < 3; tier++)
        {
            var bottom = y - (h * (0.12f + (tier * 0.26f)));
            var top = bottom - (h * 0.42f);
            var half = h * (0.3f - (tier * 0.07f));
            using var b = new SKPathBuilder();
            b.MoveTo(x - half, bottom);
            b.LineTo(x, top);
            b.LineTo(x + half, bottom);
            b.Close();
            using var tri = b.Detach();
            c.DrawPath(tri, paint);
            if (!snowy) continue;
            using var cap = new SKPathBuilder();
            cap.MoveTo(x - (half * 0.45f), top + ((bottom - top) * 0.45f));
            cap.LineTo(x, top);
            cap.LineTo(x + (half * 0.45f), top + ((bottom - top) * 0.45f));
            cap.Close();
            using var capPath = cap.Detach();
            c.DrawPath(capPath, white);
        }
    }

    /// <summary>A round leafy tree on a trunk, its foot at (x, y).</summary>
    private static void RoundTree(SKCanvas c, float x, float y, float h, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        c.DrawRect(x - (h * 0.05f), y - (h * 0.55f), h * 0.1f, h * 0.55f, paint);
        c.DrawCircle(x, y - (h * 0.68f), h * 0.3f, paint);
        c.DrawCircle(x - (h * 0.2f), y - (h * 0.52f), h * 0.2f, paint);
        c.DrawCircle(x + (h * 0.2f), y - (h * 0.54f), h * 0.21f, paint);
    }

    /// <summary>A big rainforest crown rising out of the canopy.</summary>
    private static void Crown(SKCanvas c, float x, float y, float r, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        c.DrawCircle(x, y - r, r, paint);
        c.DrawCircle(x - (r * 1.1f), y - (r * 0.4f), r * 0.8f, paint);
        c.DrawCircle(x + (r * 1.1f), y - (r * 0.5f), r * 0.85f, paint);
        c.DrawRect(x - (r * 1.6f), y - (r * 0.5f), r * 3.2f, r, paint);
    }

    /// <summary>A palm: a gently curved trunk and drooping fronds, leaning <paramref name="lean"/> (1 right, -1 left).</summary>
    private static void Palm(SKCanvas c, float x, float y, float h, int unit, SKColor color, int lean)
    {
        var top = new SKPoint(x + (lean * h * 0.18f), y - h);
        using var trunk = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.016f, StrokeCap = SKStrokeCap.Round };
        using (var b = new SKPathBuilder())
        {
            b.MoveTo(x, y);
            b.QuadTo(x + (lean * h * 0.02f), y - (h * 0.6f), top.X, top.Y);
            using var path = b.Detach();
            c.DrawPath(path, trunk);
        }
        using var frond = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.022f, StrokeCap = SKStrokeCap.Round };
        foreach (var angle in new[] { -170.0, -135, -95, -60, -15 })
        {
            var a = angle * Math.PI / 180;
            var reach = h * 0.42f;
            var end = new SKPoint(top.X + (float)(Math.Cos(a) * reach), top.Y + (float)(Math.Sin(a) * reach * 0.5) + (h * 0.16f));
            var mid = new SKPoint(top.X + (float)(Math.Cos(a) * reach * 0.6), top.Y + (float)(Math.Sin(a) * reach * 0.6) - (h * 0.04f));
            using var b = new SKPathBuilder();
            b.MoveTo(top);
            b.QuadTo(mid, end);
            using var path = b.Detach();
            c.DrawPath(path, frond);
        }
    }

    /// <summary>An acacia: a thin forked trunk under a wide flat crown.</summary>
    private static void Acacia(SKCanvas c, float x, float y, float h, int unit, SKColor color)
    {
        using var stroke = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.009f, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(x, y, x, y - (h * 0.55f), stroke);
        c.DrawLine(x, y - (h * 0.5f), x - (h * 0.35f), y - (h * 0.85f), stroke);
        c.DrawLine(x, y - (h * 0.5f), x + (h * 0.3f), y - (h * 0.88f), stroke);
        using var fill = new SKPaint { IsAntialias = true, Color = color };
        c.DrawOval(x, y - (h * 0.92f), h * 0.85f, h * 0.13f, fill);
        c.DrawOval(x - (h * 0.25f), y - (h * 1.0f), h * 0.45f, h * 0.09f, fill);
    }

    /// <summary>Tufts of grass on the near hill, away from the middle.</summary>
    private static void Tufts(SKCanvas c, int width, int height, HillLine line, Random random, SKColor color, int unit)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.005f, StrokeCap = SKStrokeCap.Round };
        for (var i = 0; i < 40; i++)
        {
            var u = random.NextDouble();
            var x = (float)(u * width);
            var y = (float)((line.At(u) + 0.02 + (random.NextDouble() * 0.2)) * height);
            var h = unit * 0.025f;
            c.DrawLine(x, y, x - (h * 0.4f), y - h, paint);
            c.DrawLine(x, y, x, y - (h * 1.2f), paint);
            c.DrawLine(x, y, x + (h * 0.4f), y - h, paint);
        }
    }

    /// <summary>Fields on the far hill: slanted strips in two of the ground's colours.</summary>
    private static void Fields(SKCanvas c, SKPath hill, int width, int height, Tones tones)
    {
        c.Save();
        c.ClipPath(hill, antialias: true);
        using var paint = new SKPaint { IsAntialias = true, Color = tones.Between };
        for (var i = 0; i < 6; i++)
        {
            using var b = new SKPathBuilder();
            var x = width * ((i * 0.19f) - 0.05f);
            b.MoveTo(x, height);
            b.LineTo(x + (width * 0.2f), height * 0.5f);
            b.LineTo(x + (width * 0.28f), height * 0.5f);
            b.LineTo(x + (width * 0.08f), height);
            b.Close();
            using var strip = b.Detach();
            c.DrawPath(strip, paint);
        }
        c.Restore();
    }

    /// <summary>A barn with a pitched roof and a round-topped silo, on the far hill towards the right.</summary>
    private static void Barn(SKCanvas c, int width, int height, HillLine line, SKColor color)
    {
        const double u = 0.8;
        var x = (float)(u * width);
        var y = (float)((line.At(u) + 0.015) * height);
        var h = height * 0.075f;
        using var paint = new SKPaint { IsAntialias = true, Color = color, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = h * 0.06f, Style = SKPaintStyle.StrokeAndFill };
        c.DrawRect(x - h, y - (h * 0.65f), h * 1.6f, h * 0.65f, paint);
        using (var b = new SKPathBuilder())
        {
            b.MoveTo(x - (h * 1.08f), y - (h * 0.62f));
            b.LineTo(x - (h * 0.2f), y - (h * 1.1f));
            b.LineTo(x + (h * 0.68f), y - (h * 0.62f));
            b.Close();
            using var roof = b.Detach();
            c.DrawPath(roof, paint);
        }
        c.DrawRoundRect(new SKRect(x + (h * 0.75f), y - (h * 1.15f), x + (h * 1.1f), y), h * 0.17f, h * 0.17f, paint);
    }

    /// <summary>A post-and-rail fence along the near hill, at both sides.</summary>
    private static void Fence(SKCanvas c, int width, int height, HillLine line, int unit, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.008f, StrokeCap = SKStrokeCap.Round };
        foreach (var (from, to) in new[] { (0.0, 0.32), (0.68, 1.0) })
        {
            SKPoint? last = null;
            for (var u = from; u <= to + 1e-9; u += 0.04)
            {
                var x = (float)(u * width);
                var y = (float)((line.At(u) + 0.03) * height);
                var post = unit * 0.05f;
                c.DrawLine(x, y, x, y - post, paint);
                if (last is { } p)
                {
                    c.DrawLine(p.X, p.Y - (post * 0.4f), x, y - (post * 0.4f), paint);
                    c.DrawLine(p.X, p.Y - (post * 0.85f), x, y - (post * 0.85f), paint);
                }
                last = new SKPoint(x, y);
            }
        }
    }

    /// <summary>Little pale flowers in the meadow grass.</summary>
    private static void Flowers(SKCanvas c, int width, int height, HillLine line, Random random, int unit)
    {
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(190) };
        using var yellow = new SKPaint { IsAntialias = true, Color = new SKColor(255, 240, 170, 210) };
        for (var i = 0; i < 46; i++)
        {
            var u = random.NextDouble();
            var y = (float)((line.At(u) + 0.03 + (random.NextDouble() * 0.2)) * height);
            c.DrawCircle((float)(u * width), y, unit * (0.006f + ((float)random.NextDouble() * 0.004f)), i % 3 == 0 ? yellow : white);
        }
    }

    /// <summary>Pale wave lines across water between two heights.</summary>
    private static void Ripples(SKCanvas c, int width, int height, double top, double bottom, Random random, int lines)
    {
        var unit = Math.Min(width, height);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(120), Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.004f, StrokeCap = SKStrokeCap.Round };
        for (var i = 0; i < lines; i++)
        {
            var depth = random.NextDouble();
            var y = (float)((top + 0.01 + ((bottom - top - 0.01) * depth)) * height);
            var x = (float)(random.NextDouble() * width);
            var length = (float)(width * (0.03 + (0.06 * depth)));
            c.DrawLine(x, y, x + length, y, paint);
        }
    }

    /// <summary>Foam where the sea meets the sand.</summary>
    private static void Shore(SKCanvas c, int width, int height, HillLine line, int unit)
    {
        using var b = new SKPathBuilder();
        for (var i = 0; i <= 80; i++)
        {
            var u = i / 80.0;
            var y = (float)((line.At(u) + 0.004 + (Math.Sin(u * 31) * 0.003)) * height);
            if (i == 0) b.MoveTo(0, y);
            else b.LineTo((float)(u * width), y);
        }
        using var path = b.Detach();
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(170), Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.008f, StrokeCap = SKStrokeCap.Round };
        c.DrawPath(path, paint);
    }

    /// <summary>Wind ripples on the near dune.</summary>
    private static void DuneLines(SKCanvas c, int width, int height, HillLine line, Random random, int unit, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = unit * 0.005f, StrokeCap = SKStrokeCap.Round };
        for (var i = 0; i < 14; i++)
        {
            var u = random.NextDouble() * 0.9;
            var y = (float)((line.At(u) + 0.04 + (random.NextDouble() * 0.18)) * height);
            var x = (float)(u * width);
            using var b = new SKPathBuilder();
            b.MoveTo(x, y);
            b.QuadTo(x + (width * 0.04f), y - (unit * 0.015f), x + (width * 0.09f), y);
            using var path = b.Detach();
            c.DrawPath(path, paint);
        }
    }

    /// <summary>Mossy lava mounds half sunk in the near ground.</summary>
    private static void Mounds(SKCanvas c, int width, int height, HillLine line, Random random, int unit, SKColor color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        for (var i = 0; i < 22; i++)
        {
            var u = random.NextDouble();
            var y = (float)((line.At(u) + 0.03 + (random.NextDouble() * 0.2)) * height);
            var r = unit * (0.02f + ((float)random.NextDouble() * 0.025f));
            c.DrawOval((float)(u * width), y, r * 1.5f, r * 0.7f, paint);
        }
    }

    /// <summary>A far-off volcano: a broad cone with a flat top, on the right.</summary>
    private static void Volcano(SKCanvas c, int width, int height, SKColor color)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(width * 0.45f, height * 0.7f);
        b.QuadTo(width * 0.6f, height * 0.62f, width * 0.66f, height * 0.44f);
        b.LineTo(width * 0.74f, height * 0.44f);
        b.QuadTo(width * 0.8f, height * 0.62f, width * 0.98f, height * 0.7f);
        b.Close();
        using var path = b.Detach();
        using var paint = new SKPaint { IsAntialias = true, Color = color, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = height * 0.01f, Style = SKPaintStyle.StrokeAndFill };
        c.DrawPath(path, paint);
    }

    private static SKColor Mix(uint a, uint b, double t)
    {
        byte Channel(int shift) => (byte)Math.Round((((a >> shift) & 0xFF) * (1 - t)) + (((b >> shift) & 0xFF) * t));
        return new SKColor(Channel(16), Channel(8), Channel(0));
    }
}
