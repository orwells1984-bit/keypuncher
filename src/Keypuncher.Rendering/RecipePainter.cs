using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Turns the recipe shape vocabulary into geometry. Everything is round and soft on purpose: the model
/// (and Phase 1's hand-written recipes) only choose among these shapes, so nothing sharp or scary can
/// be drawn whatever a recipe says.
/// </summary>
internal static class RecipePainter
{
    /// <summary>Draws a recipe's body centred on the canvas origin with radius <paramref name="r"/>.</summary>
    /// <param name="t">Seconds, for wobble and blinking.</param>
    public static void Paint(SKCanvas c, Recipe recipe, float r, double t, byte alpha)
    {
        var body = recipe.Body;
        var hue = body.Hue % 360;
        var fill = SKColor.FromHsv(hue, 45, 97).WithAlpha(alpha);
        var deep = SKColor.FromHsv(hue, 60, 75).WithAlpha(alpha);
        var lobes = Math.Clamp(body.Lobes, 1, 12);

        using var fillPaint = new SKPaint { IsAntialias = true, Color = fill };
        using var line = new SKPaint
        {
            IsAntialias = true, Color = deep, Style = SKPaintStyle.Stroke,
            StrokeWidth = Math.Max(1.5f, r * 0.07f), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        switch (body.Shape)
        {
            case Shape.Petal: Petals(c, r, lobes, hue, alpha, fillPaint, line); break;
            case Shape.Star: Star(c, r, lobes, fillPaint, line); break;
            case Shape.Wave: Wave(c, r, lobes, t, fill, deep); break;
            case Shape.Cloud: Cloud(c, r, lobes, fillPaint, line); break;
            case Shape.Stripe: Stripe(c, r, lobes, fillPaint, line, deep); break;
            case Shape.DotField: Dots(c, r, lobes, hue, alpha); break;
            default: Blob(c, r, lobes, t, fill, line); break;
        }

        if (recipe.Eyes > 0) Eyes(c, r, Math.Clamp(recipe.Eyes, 0, 4), t, alpha);
    }

    private static void Blob(SKCanvas c, float r, int lobes, double t, SKColor fill, SKPaint line)
    {
        using var b = new SKPathBuilder();
        const int points = 72;
        for (var i = 0; i <= points; i++)
        {
            var a = i * Math.PI * 2 / points;
            var k = lobes <= 1 ? 1 : 1 + (0.07 * Math.Sin((lobes * a) + (t * 0.8)));
            var p = new SKPoint((float)(Math.Cos(a) * r * k), (float)(Math.Sin(a) * r * k * 0.92));
            if (i == 0) b.MoveTo(p); else b.LineTo(p);
        }
        b.Close();
        using var path = b.Detach();
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(-r * 0.3f, -r * 0.35f), r * 1.4f,
            [Lighten(fill, 0.45f), fill], [0, 1], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        c.DrawPath(path, paint);
        c.DrawPath(path, line);
    }

    private static void Petals(SKCanvas c, float r, int lobes, int hue, byte alpha, SKPaint fill, SKPaint line)
    {
        var count = Math.Max(3, lobes);
        for (var i = 0; i < count; i++)
        {
            c.Save();
            c.RotateDegrees(i * 360f / count);
            var petal = new SKRect(-r * 0.26f, -r, r * 0.26f, -r * 0.15f);
            c.DrawOval(petal, fill);
            c.DrawOval(petal, line);
            c.Restore();
        }
        using var centre = new SKPaint { IsAntialias = true, Color = SKColor.FromHsv((hue + 60) % 360, 60, 98).WithAlpha(alpha) };
        c.DrawCircle(0, 0, r * 0.32f, centre);
        c.DrawCircle(0, 0, r * 0.32f, line);
    }

    private static void Star(SKCanvas c, float r, int lobes, SKPaint fill, SKPaint line)
    {
        var points = Math.Max(4, lobes);
        using var b = new SKPathBuilder();
        for (var i = 0; i < points * 2; i++)
        {
            var a = (i * Math.PI / points) - (Math.PI / 2);
            var rr = i % 2 == 0 ? r * 0.88f : r * 0.48f;
            var p = new SKPoint((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
            if (i == 0) b.MoveTo(p); else b.LineTo(p);
        }
        b.Close();
        using var path = b.Detach();
        // A thick stroke in the fill colour rounds every point.
        using var round = new SKPaint
        {
            IsAntialias = true, Color = fill.Color, Style = SKPaintStyle.StrokeAndFill,
            StrokeWidth = r * 0.22f, StrokeJoin = SKStrokeJoin.Round,
        };
        c.DrawPath(path, round);
    }

    private static void Wave(SKCanvas c, float r, int lobes, double t, SKColor fill, SKColor deep)
    {
        using var b = new SKPathBuilder();
        const int steps = 40;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (float)steps;
            var x = (u * 2 * r) - r;
            var y = (float)Math.Sin((u * lobes * Math.PI) + (t * 2)) * r * 0.25f;
            if (i == 0) b.MoveTo(x, y); else b.LineTo(x, y);
        }
        using var path = b.Detach();
        using var outer = new SKPaint
        {
            IsAntialias = true, Color = deep, Style = SKPaintStyle.Stroke, StrokeWidth = r * 0.42f, StrokeCap = SKStrokeCap.Round,
        };
        using var inner = new SKPaint
        {
            IsAntialias = true, Color = fill, Style = SKPaintStyle.Stroke, StrokeWidth = r * 0.3f, StrokeCap = SKStrokeCap.Round,
        };
        c.DrawPath(path, outer);
        c.DrawPath(path, inner);
    }

    private static void Cloud(SKCanvas c, float r, int lobes, SKPaint fill, SKPaint line)
    {
        var count = Math.Max(2, lobes);
        using var b = new SKPathBuilder();
        for (var i = 0; i < count; i++)
        {
            var u = count == 1 ? 0.5 : i / (double)(count - 1);
            var x = (float)((u * 1.3) - 0.65) * r;
            var y = (float)(-Math.Sin(u * Math.PI) * 0.3) * r;
            b.AddCircle(x, y, r * 0.42f, SKPathDirection.Clockwise);
        }
        b.AddRoundRect(new SKRect(-r * 0.85f, -r * 0.05f, r * 0.85f, r * 0.5f), r * 0.27f, r * 0.27f, SKPathDirection.Clockwise);
        using var path = b.Detach();
        using var merged = path.Simplify() ?? path;
        c.DrawPath(merged, fill);
        c.DrawPath(merged, line);
    }

    private static void Stripe(SKCanvas c, float r, int lobes, SKPaint fill, SKPaint line, SKColor deep)
    {
        var body = new SKRoundRect(new SKRect(-r, -r * 0.5f, r * 0.75f, r * 0.5f), r * 0.5f, r * 0.5f);
        using (var tb = new SKPathBuilder())
        {
            tb.MoveTo(r * 0.6f, 0);
            tb.LineTo(r * 1.05f, -r * 0.35f);
            tb.LineTo(r * 1.05f, r * 0.35f);
            tb.Close();
            using var tail = tb.Detach();
            using var tailPaint = new SKPaint { IsAntialias = true, Color = deep, Style = SKPaintStyle.StrokeAndFill, StrokeWidth = r * 0.12f, StrokeJoin = SKStrokeJoin.Round };
            c.DrawPath(tail, tailPaint);
        }
        c.DrawRoundRect(body, fill);
        c.Save();
        c.ClipRoundRect(body, antialias: true);
        using var stripe = new SKPaint { IsAntialias = true, Color = deep.WithAlpha((byte)(deep.Alpha / 2)) };
        var count = Math.Clamp(lobes, 1, 6);
        for (var i = 0; i < count; i++)
        {
            var x = -r * 0.55f + (i * r * 1.1f / Math.Max(1, count - 1 + 1));
            c.DrawRect(new SKRect(x, -r, x + (r * 0.14f), r), stripe);
        }
        c.Restore();
        c.DrawRoundRect(body, line);
    }

    private static void Dots(SKCanvas c, float r, int lobes, int hue, byte alpha)
    {
        var count = Math.Max(4, lobes * 2);
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < count; i++)
        {
            var a = i * Math.PI * 2 / count;
            paint.Color = SKColor.FromHsv((hue + (i * 25)) % 360, 50, 98).WithAlpha(alpha);
            c.DrawCircle((float)(Math.Cos(a) * r * 0.7), (float)(Math.Sin(a) * r * 0.7), r * 0.15f, paint);
        }
        paint.Color = SKColor.FromHsv(hue, 40, 100).WithAlpha(alpha);
        c.DrawCircle(0, 0, r * 0.22f, paint);
    }

    private static void Eyes(SKCanvas c, float r, int count, double t, byte alpha)
    {
        var blink = (t % 4.7) < 0.13;
        var spacing = r * 0.36f;
        var start = -spacing * (count - 1) / 2;
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha) };
        using var dark = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 60, alpha), StrokeWidth = r * 0.05f, StrokeCap = SKStrokeCap.Round };
        for (var i = 0; i < count; i++)
        {
            var x = start + (i * spacing);
            var y = -r * 0.12f;
            if (blink)
            {
                c.DrawLine(x - (r * 0.1f), y, x + (r * 0.1f), y, dark);
                continue;
            }
            c.DrawCircle(x, y, r * 0.15f, white);
            c.DrawCircle(x + (r * 0.03f), y + (r * 0.02f), r * 0.075f, dark);
        }
    }

    private static SKColor Lighten(SKColor c, float amount) => new(
        (byte)(c.Red + ((255 - c.Red) * amount)),
        (byte)(c.Green + ((255 - c.Green) * amount)),
        (byte)(c.Blue + ((255 - c.Blue) * amount)),
        c.Alpha);
}
