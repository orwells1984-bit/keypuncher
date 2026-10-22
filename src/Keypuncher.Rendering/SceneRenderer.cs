using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Draws the world: a soft sky and hills, the baked history behind everything, then live things,
/// effects, letters and words. Keeps two cached layers (background and baked) so a frame with a few
/// hundred live things stays cheap. Use from one thread.
/// </summary>
public sealed class SceneRenderer(SKTypeface typeface) : IDisposable
{
    private SKSurface? _baked;
    private SKPicture? _background;
    private int _width, _height, _bakedDrawn, _bakedGeneration = -1;

    /// <param name="overlay">Small text for grown-ups at the top-left, e.g. the exit challenge.</param>
    public void Draw(SKCanvas canvas, int width, int height, World world, string? overlay = null)
    {
        if (width <= 0 || height <= 0) return;
        if (width != _width || height != _height) Resize(width, height);

        canvas.DrawPicture(_background);
        UpdateBaked(world);
        canvas.DrawSurface(_baked, 0, 0);

        var unit = Math.Min(width, height);
        // Plants first so creatures pass in front of them, then effects, then text on top.
        foreach (var e in world.Live)
            if (e.Recipe?.Kind == RecipeKind.Plant && e.Role == EntityRole.Thing) DrawThing(canvas, e, world.Time, width, height, unit);
        foreach (var e in world.Live)
            if (e.Recipe?.Kind != RecipeKind.Plant && e.Role == EntityRole.Thing) DrawThing(canvas, e, world.Time, width, height, unit);
        foreach (var e in world.Live)
            if (e.Role == EntityRole.Effect) DrawEffect(canvas, e, world.Time, width, height, unit);
        foreach (var e in world.Live)
            if (e.Role is EntityRole.Letter or EntityRole.Word) DrawText(canvas, e, world.Time, width, height);

        if (overlay is not null) DrawOverlay(canvas, overlay, height);
    }

    public void Dispose()
    {
        _baked?.Dispose();
        _background?.Dispose();
    }

    private void Resize(int width, int height)
    {
        (_width, _height) = (width, height);
        _background?.Dispose();
        _background = PaintBackground(width, height);
        _baked?.Dispose();
        _baked = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _bakedGeneration = -1;
    }

    private void UpdateBaked(World world)
    {
        var canvas = _baked!.Canvas;
        if (_bakedGeneration != world.BakedGeneration)
        {
            canvas.Clear(SKColors.Transparent);
            _bakedDrawn = 0;
            _bakedGeneration = world.BakedGeneration;
        }
        var unit = Math.Min(_width, _height);
        for (; _bakedDrawn < world.Baked.Count; _bakedDrawn++)
            DrawThing(canvas, world.Baked[_bakedDrawn], double.NaN, _width, _height, unit);
    }

    /// <param name="time">World time, or NaN to draw the thing still and faded into the background.</param>
    private static void DrawThing(SKCanvas canvas, Entity e, double time, int width, int height, int unit)
    {
        var recipe = e.Recipe!;
        var baked = double.IsNaN(time);
        var age = baked ? 1000 : e.Age(time);
        var t = baked ? e.Phase * 10 : time + (e.Phase * 10);
        var pop = baked ? 1 : EaseOutBack(Math.Min(age / 0.35, 1));
        var growth = 1 + (Math.Min(age / 600, 1) * 0.25);
        var r = (float)(recipe.Body.Size * 0.11 * unit * e.Scale * pop * growth);
        if (r < 0.5f) return;

        var energy = baked ? 0 : e.Energy;
        var alpha = (byte)(baked ? 150 : 255 * (0.55 + (0.45 * energy)));
        var liveliness = 0.4 + (0.6 * energy);

        var x = (float)(e.X * width);
        var y = (float)(e.Y * height);
        float dx = 0, dy = 0, rotation = 0;
        var speed = recipe.Motion.Speed;
        if (!baked)
        {
            switch (recipe.Motion.Type)
            {
                case MotionType.Hop:
                    dy = -(float)(Math.Abs(Math.Sin(t * (2 + (speed * 4)))) * r * 0.5 * liveliness);
                    break;
                case MotionType.Drift:
                    dx = (float)(Math.Sin((t * 0.4 * (0.5 + speed)) + (e.Phase * 6)) * unit * 0.03 * liveliness);
                    dy = (float)(Math.Cos(t * 0.3 * (0.5 + speed)) * unit * 0.012 * liveliness);
                    break;
                case MotionType.Sway:
                    rotation = (float)(Math.Sin((t * (1 + (speed * 2))) + (e.Phase * 6)) * 8 * liveliness);
                    break;
                case MotionType.Spin:
                    rotation = (float)(t * (20 + (speed * 70)) % 360);
                    break;
            }
        }

        canvas.Save();
        if (recipe.Kind == RecipeKind.Plant)
        {
            // The entity's Y is where the stem meets the ground; the plant sways from there.
            canvas.Translate(x + dx, y);
            canvas.RotateDegrees(rotation);
            var stem = r * 1.6f;
            using var stemPaint = new SKPaint
            {
                IsAntialias = true, Color = new SKColor(70, 150, 80, alpha), Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(2, r * 0.16f), StrokeCap = SKStrokeCap.Round,
            };
            canvas.DrawLine(0, 0, 0, -stem, stemPaint);
            canvas.Translate(0, -stem);
            RecipePainter.Paint(canvas, recipe, r, t, alpha);
        }
        else
        {
            canvas.Translate(x + dx, y + dy);
            canvas.RotateDegrees(rotation);
            RecipePainter.Paint(canvas, recipe, r, t, alpha);
        }
        canvas.Restore();
    }

    private static void DrawEffect(SKCanvas canvas, Entity e, double time, int width, int height, int unit)
    {
        var recipe = e.Recipe!;
        var p = Math.Clamp(e.Age(time) / e.Lifetime, 0, 1);
        if (recipe.Motion.Type != MotionType.Burst)
        {
            // A concept drawn as an effect (sun, moon, star, cloud): a thing that fades at the end.
            var fade = p < 0.8 ? 1 : (1 - p) / 0.2;
            canvas.Save();
            canvas.Translate((float)(e.X * width), (float)(e.Y * height));
            var r = (float)(recipe.Body.Size * 0.11 * unit * e.Scale * EaseOutBack(Math.Min(e.Age(time) / 0.35, 1)));
            if (recipe.Motion.Type == MotionType.Spin) canvas.RotateDegrees((float)(time * 30 % 360));
            if (r > 0.5f) RecipePainter.Paint(canvas, recipe, r, time, (byte)(255 * fade));
            canvas.Restore();
            return;
        }

        var radius = (float)(recipe.Body.Size * 0.11 * unit * e.Scale * (0.6 + (2.2 * EaseOut(p))));
        canvas.Save();
        canvas.Translate((float)(e.X * width), (float)(e.Y * height));
        canvas.RotateDegrees((float)(e.Phase * 360) + (float)(p * 60));
        RecipePainter.Paint(canvas, recipe, radius, time, (byte)(255 * (1 - p)));
        canvas.Restore();
    }

    private void DrawText(SKCanvas canvas, Entity e, double time, int width, int height)
    {
        var age = e.Age(time);
        var letter = e.Role == EntityRole.Letter;
        var fadeStart = e.Lifetime - 0.6;
        var alpha = (byte)(255 * (age < fadeStart ? 1 : Math.Max(0, (e.Lifetime - age) / 0.6)));
        var pop = EaseOutBack(Math.Min(age / 0.25, 1));
        var size = (float)((letter ? 0.26 : 0.085) * height * pop);
        if (size < 1) return;

        var x = (float)(e.X * width);
        var y = (float)((e.Y * height) - (letter ? age * 0.03 * height : 0));
        var hue = (e.Text ?? "").Sum(ch => ch * 47) % 360;

        using var font = new SKFont(typeface, size);
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = size * 0.09f, StrokeJoin = SKStrokeJoin.Round,
            Color = SKColor.FromHsv(hue, 65, 70).WithAlpha(alpha),
        };
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha) };
        var text = e.Text ?? "";
        var baseline = y + (size * 0.35f);
        canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, stroke);
        canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, fill);
    }

    private void DrawOverlay(SKCanvas canvas, string text, int height)
    {
        using var font = new SKFont(typeface, Math.Max(14, height * 0.025f));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70, 200) };
        canvas.DrawText(text, height * 0.02f, height * 0.04f, SKTextAlign.Left, font, paint);
    }

    private static SKPicture PaintBackground(int width, int height)
    {
        using var recorder = new SKPictureRecorder();
        var c = recorder.BeginRecording(new SKRect(0, 0, width, height));

        using (var sky = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height * 0.75f),
            [new SKColor(0xB8, 0xE2, 0xFF), new SKColor(0xFF, 0xF2, 0xD8)], [0, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = sky })
        {
            c.DrawRect(0, 0, width, height, paint);
        }

        Hill(c, width, height, 0.66, 0.035, 1.3, 0.4, new SKColor(0xA8, 0xDC, 0x9F));
        Hill(c, width, height, 0.74, 0.03, 2.1, 1.7, new SKColor(0x86, 0xCB, 0x80));
        return recorder.EndRecording();
    }

    private static void Hill(SKCanvas c, int width, int height, double level, double amplitude, double waves, double offset, SKColor color)
    {
        using var b = new SKPathBuilder();
        b.MoveTo(0, height);
        const int steps = 48;
        for (var i = 0; i <= steps; i++)
        {
            var u = i / (double)steps;
            var y = (level + (Math.Sin((u * waves * Math.PI) + offset) * amplitude)) * height;
            b.LineTo((float)(u * width), (float)y);
        }
        b.LineTo(width, height);
        b.Close();
        using var path = b.Detach();
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        c.DrawPath(path, paint);
    }

    private static double EaseOut(double p) => 1 - Math.Pow(1 - p, 3);

    private static double EaseOutBack(double p)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + (c3 * Math.Pow(p - 1, 3)) + (c1 * Math.Pow(p - 1, 2));
    }
}
