using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Turns the recipe shape vocabulary into geometry. Everything is round and soft on purpose: the model
/// (and Phase 1's hand-written recipes) only choose among these shapes, so nothing sharp or scary can
/// be drawn whatever a recipe says. The <see cref="ArtStyle"/> says how bold: every body is a palette
/// colour with a dark outline, and the younger the child the fewer and bigger its parts. Bodies are
/// drawn opaque; the caller fades a whole thing at once, so an outline never shows through a fill.
/// </summary>
internal static class RecipePainter
{
    /// <summary>Draws a recipe's body centred on the canvas origin with radius <paramref name="r"/>.</summary>
    /// <param name="t">Seconds, for wobble and blinking.</param>
    /// <param name="unit">The shorter screen side in pixels, which the style's widths are fractions of.</param>
    public static void Paint(SKCanvas c, Recipe recipe, float r, double t, ArtStyle style, int unit)
    {
        var body = recipe.Body;
        var colour = style.Snap(body.Hue);
        var stroke = Math.Max(MinDetail(style, unit), r * (float)style.StrokeOfRadius);
        // MaxLobes is at least 4 at every level, so each shape's own minimum below always fits under it.
        var lobes = Math.Clamp(body.Lobes, 1, style.MaxLobes);

        using var fill = new SKPaint { IsAntialias = true, Color = Rgb(colour.Fill) };
        using var line = new SKPaint
        {
            IsAntialias = true, Color = Rgb(colour.Outline), Style = SKPaintStyle.Stroke,
            StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };
        var brush = new Brush(fill, line, stroke, style, unit);

        switch (body.Shape)
        {
            case Shape.Petal: Petals(c, r, lobes, body.Hue, brush); break;
            case Shape.Star: Star(c, r, lobes, brush); break;
            case Shape.Wave: Wave(c, r, lobes, t, brush); break;
            case Shape.Cloud: Cloud(c, r, lobes, brush); break;
            case Shape.Stripe: Stripe(c, r, lobes, brush); break;
            case Shape.DotField: Dots(c, r, lobes, body.Hue, brush); break;
            default: Blob(c, r, lobes, t, brush); break;
        }

        // Babies attend to faces, so at the calmest level a creature with any eyes gets a proper pair.
        // A thing drawn without eyes (a ball, an egg, the sun) stays faceless at every level.
        var eyes = recipe.Kind == RecipeKind.Creature && recipe.Eyes > 0 ? Math.Max(recipe.Eyes, style.MinEyes) : recipe.Eyes;
        eyes = Math.Clamp(eyes, 0, style.MaxEyes);
        if (eyes > 0) Eyes(c, r, eyes, t, brush);
    }

    /// <summary>The finest line the style draws, in pixels: outlines, stems and letter edges are never thinner.</summary>
    public static float MinDetail(ArtStyle style, int unit) => (float)(style.MinStroke * unit);

    public static SKColor Rgb(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>What one body is drawn with: its palette fill, the outline around it, and the style's limits.</summary>
    private readonly record struct Brush(SKPaint Fill, SKPaint Line, float Stroke, ArtStyle Style, int Unit)
    {
        public SKColor FillColor => Fill.Color;
        public SKColor Outline => Line.Color;
        /// <summary>Nothing finer than this is drawn.</summary>
        public float MinDetail => RecipePainter.MinDetail(Style, Unit);
    }

    private static void Blob(SKCanvas c, float r, int lobes, double t, Brush b)
    {
        using var builder = new SKPathBuilder();
        const int points = 72;
        for (var i = 0; i <= points; i++)
        {
            var a = i * Math.PI * 2 / points;
            var k = lobes <= 1 ? 1 : 1 + (0.07 * Math.Sin((lobes * a) + (t * 0.8)));
            var p = new SKPoint((float)(Math.Cos(a) * r * k), (float)(Math.Sin(a) * r * k * 0.92));
            if (i == 0) builder.MoveTo(p); else builder.LineTo(p);
        }
        builder.Close();
        using var path = builder.Detach();
        // A soft highlight at the top-left; faint for the youngest, whose eyes sit there and must not wash out.
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(-r * 0.3f, -r * 0.35f), r * 1.4f,
            [Lighten(b.FillColor, (float)b.Style.Highlight), b.FillColor], [0, 1], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        c.DrawPath(path, paint);
        c.DrawPath(path, b.Line);
    }

    private static void Petals(SKCanvas c, float r, int lobes, int hue, Brush b)
    {
        var count = Math.Max(3, lobes);
        for (var i = 0; i < count; i++)
        {
            c.Save();
            c.RotateDegrees(i * 360f / count);
            var petal = new SKRect(-r * 0.26f, -r, r * 0.26f, -r * 0.15f);
            c.DrawOval(petal, b.Fill);
            c.DrawOval(petal, b.Line);
            c.Restore();
        }
        using var centre = new SKPaint { IsAntialias = true, Color = Rgb(b.Style.Companion(hue).Fill) };
        c.DrawCircle(0, 0, r * 0.32f, centre);
        c.DrawCircle(0, 0, r * 0.32f, b.Line);
    }

    private static void Star(SKCanvas c, float r, int lobes, Brush b)
    {
        var points = Math.Max(4, lobes);
        using var builder = new SKPathBuilder();
        for (var i = 0; i < points * 2; i++)
        {
            var a = (i * Math.PI / points) - (Math.PI / 2);
            var rr = i % 2 == 0 ? r * 0.88f : r * 0.48f;
            var p = new SKPoint((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr));
            if (i == 0) builder.MoveTo(p); else builder.LineTo(p);
        }
        builder.Close();
        using var path = builder.Detach();
        // Every point and notch is rounded off, then filled and outlined like any other body.
        using var corner = SKPathEffect.CreateCorner(r * 0.2f);
        using var fill = new SKPaint { IsAntialias = true, Color = b.FillColor, PathEffect = corner };
        using var line = new SKPaint
        {
            IsAntialias = true, Color = b.Outline, Style = SKPaintStyle.Stroke, StrokeWidth = b.Stroke,
            StrokeJoin = SKStrokeJoin.Round, PathEffect = corner,
        };
        c.DrawPath(path, fill);
        c.DrawPath(path, line);
    }

    private static void Wave(SKCanvas c, float r, int lobes, double t, Brush b)
    {
        using var builder = new SKPathBuilder();
        const int steps = 40;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (float)steps;
            var x = (u * 2 * r) - r;
            var y = (float)Math.Sin((u * lobes * Math.PI) + (t * 2)) * r * 0.25f;
            if (i == 0) builder.MoveTo(x, y); else builder.LineTo(x, y);
        }
        using var path = builder.Detach();
        // A ribbon a few times wider than the finest visible line, with the outline either side of it.
        var width = Math.Max(r * 0.3f, 3.5f * b.MinDetail);
        using var outer = new SKPaint
        {
            IsAntialias = true, Color = b.Outline, Style = SKPaintStyle.Stroke, StrokeWidth = width + (2 * b.Stroke), StrokeCap = SKStrokeCap.Round,
        };
        using var inner = new SKPaint
        {
            IsAntialias = true, Color = b.FillColor, Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round,
        };
        c.DrawPath(path, outer);
        c.DrawPath(path, inner);
    }

    private static void Cloud(SKCanvas c, float r, int lobes, Brush b)
    {
        var count = Math.Max(2, lobes);
        using var builder = new SKPathBuilder();
        for (var i = 0; i < count; i++)
        {
            var u = i / (double)(count - 1);
            var x = (float)((u * 1.3) - 0.65) * r;
            var y = (float)(-Math.Sin(u * Math.PI) * 0.3) * r;
            builder.AddCircle(x, y, r * 0.42f, SKPathDirection.Clockwise);
        }
        builder.AddRoundRect(new SKRect(-r * 0.85f, -r * 0.05f, r * 0.85f, r * 0.5f), r * 0.27f, r * 0.27f, SKPathDirection.Clockwise);
        using var path = builder.Detach();
        using var merged = path.Simplify() ?? path;
        c.DrawPath(merged, b.Fill);
        c.DrawPath(merged, b.Line);
    }

    private static void Stripe(SKCanvas c, float r, int lobes, Brush b)
    {
        using var body = new SKRoundRect(new SKRect(-r, -r * 0.5f, r * 0.75f, r * 0.5f), r * 0.5f, r * 0.5f);
        using (var tb = new SKPathBuilder())
        {
            tb.MoveTo(r * 0.6f, 0);
            tb.LineTo(r * 1.05f, -r * 0.35f);
            tb.LineTo(r * 1.05f, r * 0.35f);
            tb.Close();
            using var tail = tb.Detach();
            // The tail's corners are rounded off and it is outlined like the body, which then covers its root.
            using var corner = SKPathEffect.CreateCorner(r * 0.1f);
            using var tailFill = new SKPaint { IsAntialias = true, Color = b.FillColor, PathEffect = corner };
            using var tailLine = new SKPaint
            {
                IsAntialias = true, Color = b.Outline, Style = SKPaintStyle.Stroke, StrokeWidth = b.Stroke,
                StrokeJoin = SKStrokeJoin.Round, PathEffect = corner,
            };
            c.DrawPath(tail, tailFill);
            c.DrawPath(tail, tailLine);
        }
        c.DrawRoundRect(body, b.Fill);
        var count = Math.Min(lobes, b.Style.MaxStripes);
        if (count > 0)
        {
            c.Save();
            c.ClipRoundRect(body, antialias: true);
            using var stripe = new SKPaint { IsAntialias = true, Color = b.Outline.WithAlpha(140) };
            var width = Math.Max(r * 0.14f, 1.4f * b.MinDetail);
            for (var i = 0; i < count; i++)
            {
                var x = -r * 0.55f + (i * r * 1.1f / count);
                c.DrawRect(new SKRect(x, -r, x + width, r), stripe);
            }
            c.Restore();
        }
        c.DrawRoundRect(body, b.Line);
    }

    private static void Dots(SKCanvas c, float r, int lobes, int hue, Brush b)
    {
        // Fewer dots are bigger, so the ring never turns into speckle at a baby's acuity.
        var count = Math.Min(Math.Max(lobes * 2, 4), b.Style.MaxDots);
        var dotRadius = Math.Max(r * Math.Clamp(1.4f / count, 0.12f, 0.3f), b.MinDetail);
        using var dot = new SKPaint { IsAntialias = true };
        using var dotLine = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = b.Stroke, Color = b.Outline,
        };
        for (var i = 0; i < count; i++)
        {
            var a = i * Math.PI * 2 / count;
            var colour = b.Style.MultiHueDots ? b.Style.Snap(hue + (i * 25)) : null;
            dot.Color = colour is null ? b.FillColor : Rgb(colour.Fill);
            dotLine.Color = colour is null ? b.Outline : Rgb(colour.Outline);
            var x = (float)(Math.Cos(a) * r * 0.7);
            var y = (float)(Math.Sin(a) * r * 0.7);
            c.DrawCircle(x, y, dotRadius, dot);
            c.DrawCircle(x, y, dotRadius, dotLine);
        }
        c.DrawCircle(0, 0, r * 0.22f, b.Fill);
        c.DrawCircle(0, 0, r * 0.22f, b.Line);
    }

    private static void Eyes(SKCanvas c, float r, int count, double t, Brush b)
    {
        var blink = (t % 4.7) < 0.13;
        var scale = (float)b.Style.EyeScale;
        var pupil = Math.Max(r * 0.075f * scale, (float)(b.Style.MinPupil * b.Unit) / 2);
        var white = Math.Max(r * 0.15f * scale, pupil * 2);
        var spacing = Math.Max(r * 0.36f * scale, white * 2.1f);
        var start = -spacing * (count - 1) / 2;
        var y = -r * 0.12f * scale;
        using var whitePaint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        // A ring just outside the white makes it read as an eye on a pale body too; the pupil is the darkest thing drawn.
        using var ring = new SKPaint
        {
            IsAntialias = true, Color = Rgb(b.Style.Ink), Style = SKPaintStyle.Stroke, StrokeWidth = b.MinDetail,
        };
        using var dark = new SKPaint
        {
            IsAntialias = true, Color = Rgb(b.Style.Ink),
            StrokeWidth = Math.Max(r * 0.05f * scale, b.MinDetail), StrokeCap = SKStrokeCap.Round,
        };
        for (var i = 0; i < count; i++)
        {
            var x = start + (i * spacing);
            if (blink)
            {
                c.DrawLine(x - (white * 0.7f), y, x + (white * 0.7f), y, dark);
                continue;
            }
            c.DrawCircle(x, y, white, whitePaint);
            c.DrawCircle(x, y, white + (b.MinDetail / 2), ring);
            c.DrawCircle(x + (r * 0.03f * scale), y + (r * 0.02f * scale), pupil, dark);
        }
    }

    private static SKColor Lighten(SKColor c, float amount) => new(
        (byte)(c.Red + ((255 - c.Red) * amount)),
        (byte)(c.Green + ((255 - c.Green) * amount)),
        (byte)(c.Blue + ((255 - c.Blue) * amount)),
        c.Alpha);
}
