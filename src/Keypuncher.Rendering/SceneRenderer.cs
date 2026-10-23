using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Draws the world: a soft sky and hills, the faded history behind everything, then live things,
/// effects, letters and words. Keeps two cached layers (background and history) so a frame stays cheap.
/// Only the few things the world marks awake move; the rest ease to a stop, and everything else dims a
/// little while a letter or word is showing. Sizes, outlines and colours come from the world's
/// <see cref="ArtStyle"/>, so a calmer level draws bigger and bolder. Use from one thread.
/// </summary>
public sealed class SceneRenderer(SKTypeface typeface) : IDisposable
{
    /// <summary>How fast a thing eases between moving and still, in full swings a second.</summary>
    private const double MotionEase = 1.5;
    /// <summary>History is drawn with this much of its colour, so it stays a quiet backdrop.</summary>
    private const float HistorySaturation = 0.35f;
    /// <summary>Bigger moves are capped at this share of the shorter screen side.</summary>
    private const double MaxMotionAmplitude = 0.03;
    /// <summary>A word label's height as a share of the screen height; it is for the grown-up, so it stays small.</summary>
    private const double WordHeight = 0.085;

    private readonly Dictionary<long, (double Clock, double Motion)> _motion = [];
    private SKSurface? _baked;
    private SKPicture? _background;
    private SKColorFilter? _historyFilter;
    private ArtStyle? _backgroundStyle, _bakedStyle;
    private int _width, _height, _bakedDrawn = -1, _bakedGeneration = -1, _bakedShown = -1;
    private double _lastTime = double.NaN;

    /// <param name="overlay">Small text for grown-ups at the top-left, e.g. the exit challenge.</param>
    public void Draw(SKCanvas canvas, int width, int height, World world, string? overlay = null)
    {
        if (width <= 0 || height <= 0) return;
        if (width != _width || height != _height) Resize(width, height);

        var dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(world.Time - _lastTime, 0, 0.25);
        _lastTime = world.Time;

        if (_background is null || !Equals(_backgroundStyle, world.Style))
        {
            _background?.Dispose();
            _background = PaintBackground(width, height, world.Style.Ground);
            _backgroundStyle = world.Style;
        }
        canvas.DrawPicture(_background);
        UpdateBaked(world);
        using (var history = new SKPaint { ColorFilter = _historyFilter, Color = SKColors.White.WithAlpha((byte)(255 * Math.Clamp(world.Limits.HistoryOpacity, 0, 1))) })
            canvas.DrawSurface(_baked, 0, 0, history);

        var unit = Math.Min(width, height);
        var (spotlight, spotlightSince) = Spotlight(world);
        AdvanceMotion(world, dt, spotlight);

        // Plants first so creatures pass in front of them, then effects, then text on top.
        foreach (var e in world.Live)
            if (e.Recipe?.Kind == RecipeKind.Plant && e.Role == EntityRole.Thing) DrawLive(canvas, e, world, width, height, unit, spotlight, spotlightSince);
        foreach (var e in world.Live)
            if (e.Recipe?.Kind != RecipeKind.Plant && e.Role == EntityRole.Thing) DrawLive(canvas, e, world, width, height, unit, spotlight, spotlightSince);
        foreach (var e in world.Live)
            if (e.Role == EntityRole.Effect) DrawEffect(canvas, e, world.Time, width, height, unit, world.Style);

        if (world.Night > 0)
        {
            using var dusk = new SKPaint { Color = new SKColor(0x1A, 0x24, 0x4A, (byte)(255 * 0.5 * Math.Clamp(world.Night, 0, 1))) };
            canvas.DrawRect(0, 0, width, height, dusk);
        }

        foreach (var e in world.Live)
            if (e.Role is EntityRole.Letter or EntityRole.Word) DrawText(canvas, e, world.Time, width, height, unit, world.Style);

        if (overlay is not null) DrawOverlay(canvas, overlay, height);
    }

    public void Dispose()
    {
        _baked?.Dispose();
        _background?.Dispose();
        _historyFilter?.Dispose();
    }

    /// <summary>How strongly a showing letter or word holds the stage (0..1), and the birth time from which
    /// things count as part of that moment and are left bright.</summary>
    private static (double Strength, double Since) Spotlight(World world)
    {
        double strength = 0, since = double.PositiveInfinity;
        foreach (var e in world.Live)
        {
            if (e.Role is not (EntityRole.Letter or EntityRole.Word)) continue;
            var age = e.Age(world.Time);
            var s = Math.Clamp(age / 0.2, 0, 1) * Math.Clamp((e.Lifetime - age) / 0.6, 0, 1);
            strength = Math.Max(strength, s);
            since = Math.Min(since, e.Born - 0.1);
        }
        return (strength, since);
    }

    /// <summary>Eases each thing's motion towards awake or still, and runs its own clock at that speed, so
    /// a thing that falls asleep slows to a stop instead of freezing mid-hop.</summary>
    private void AdvanceMotion(World world, double dt, double spotlight)
    {
        var seen = new HashSet<long>();
        foreach (var e in world.Live)
        {
            if (e.Role != EntityRole.Thing) continue;
            seen.Add(e.Id);
            var target = e.Awake ? (0.3 + (0.7 * e.Energy)) * (1 - (0.7 * spotlight)) * (1 - world.Night) : 0;
            var (clock, motion) = _motion.TryGetValue(e.Id, out var m) ? m : (e.Phase * 10, target);
            motion += Math.Clamp(target - motion, -MotionEase * dt, MotionEase * dt);
            _motion[e.Id] = (clock + (dt * motion), motion);
        }
        if (_motion.Count > seen.Count * 2 + 16)
            foreach (var id in _motion.Keys.Where(id => !seen.Contains(id)).ToList()) _motion.Remove(id);
    }

    private void DrawLive(SKCanvas canvas, Entity e, World world, int width, int height, int unit, double spotlight, double spotlightSince)
    {
        var (clock, motion) = _motion.TryGetValue(e.Id, out var m) ? m : (e.Phase * 10, 0);
        var dim = e.Born >= spotlightSince ? 0 : spotlight;
        var a = (0.6 + (0.4 * e.Energy)) * (1 - (0.3 * dim));
        if (e.RetiredAt is { } at)
        {
            // Fading out to the history's own opacity, so joining the background is seamless.
            var k = Math.Clamp((world.Time - at) / World.RetireSeconds, 0, 1);
            a += (world.Limits.HistoryOpacity - a) * k;
        }
        DrawThing(canvas, e, e.Age(world.Time), clock, motion, (byte)(255 * Math.Clamp(a, 0, 1)), world.Limits.MaxMotionHz, width, height, unit, world.Style);
    }

    private void Resize(int width, int height)
    {
        (_width, _height) = (width, height);
        _background?.Dispose();
        _background = null;
        _baked?.Dispose();
        _baked = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _historyFilter ??= Desaturate(HistorySaturation);
        _bakedGeneration = -1;
    }

    /// <summary>Repaints the history layer when something joins it or the style changes: only the most
    /// recent few, drawn still.</summary>
    private void UpdateBaked(World world)
    {
        var shown = Math.Max(0, world.Limits.HistoryShown);
        if (_bakedGeneration == world.BakedGeneration && _bakedDrawn == world.Baked.Count && _bakedShown == shown && Equals(_bakedStyle, world.Style)) return;
        (_bakedGeneration, _bakedDrawn, _bakedShown, _bakedStyle) = (world.BakedGeneration, world.Baked.Count, shown, world.Style);

        var canvas = _baked!.Canvas;
        canvas.Clear(SKColors.Transparent);
        var unit = Math.Min(_width, _height);
        for (var i = Math.Max(0, world.Baked.Count - shown); i < world.Baked.Count; i++)
        {
            var e = world.Baked[i];
            DrawThing(canvas, e, 1000, e.Phase * 10, 0, 255, 0, _width, _height, unit, world.Style);
        }
    }

    /// <param name="age">Seconds since birth, for the pop-in and the slow growth.</param>
    /// <param name="clock">The thing's own clock, which only runs while it moves.</param>
    /// <param name="motion">0 for still, up to 1 for fully lively.</param>
    private static void DrawThing(SKCanvas canvas, Entity e, double age, double clock, double motion, byte alpha, double maxHz, int width, int height, int unit, ArtStyle style)
    {
        var recipe = e.Recipe!;
        var pop = EaseOutBack(Math.Min(age / 0.35, 1));
        var growth = 1 + (Math.Min(age / 600, 1) * 0.25);
        var r = (float)(BodyRadius(e, style, unit) * pop * growth);
        if (r < 0.5f) return;

        // Kept whole on the screen: a baby's thing is big enough to hang off the edge where its key put it.
        var plant = recipe.Kind == RecipeKind.Plant;
        var x = Math.Clamp((float)(e.X * width), r * 1.1f, width - (r * 1.1f));
        var y = plant ? (float)(e.Y * height) : Math.Clamp((float)(e.Y * height), r * 1.1f, height - (r * 1.1f));
        var t = clock;
        float dx = 0, dy = 0, rotation = 0;
        var speed = Math.Clamp(recipe.Motion.Speed, 0, 1);
        var maxAngular = 2 * Math.PI * Math.Max(0.05, maxHz);
        var reach = unit * MaxMotionAmplitude;
        if (motion > 0)
        {
            switch (recipe.Motion.Type)
            {
                case MotionType.Hop:
                    // |sin| bounces twice per cycle of sin, so its frequency is w / π.
                    var hop = Math.Min(2 + (speed * 4), maxAngular / 2);
                    dy = -(float)(Math.Abs(Math.Sin(t * hop)) * Math.Min(r * 0.5, reach) * motion);
                    break;
                case MotionType.Drift:
                    var drift = Math.Min(0.4 * (0.5 + speed), maxAngular);
                    dx = (float)(Math.Sin((t * drift) + (e.Phase * 6)) * reach * motion);
                    dy = (float)(Math.Cos(t * drift * 0.75) * reach * 0.4 * motion);
                    break;
                case MotionType.Sway:
                    rotation = (float)(Math.Sin((t * Math.Min(1 + (speed * 2), maxAngular)) + (e.Phase * 6)) * 8 * motion);
                    break;
                case MotionType.Spin:
                    // A slow rock rather than a spin: continuous rotation is too much for a baby's screen.
                    rotation = (float)(Math.Sin((t * Math.Min(0.5 + speed, maxAngular)) + (e.Phase * 6)) * 20 * motion);
                    break;
            }
        }

        var stem = plant ? r * 1.6f : 0;
        var reachOut = (r * 2.2f) + RecipePainter.MinDetail(style, unit);
        Faded(canvas, new SKRect(x + dx - reachOut, y + dy - stem - reachOut, x + dx + reachOut, y + dy + reachOut), alpha, () =>
        {
            canvas.Save();
            if (plant)
            {
                // The entity's Y is where the stem meets the ground; the plant sways from there.
                canvas.Translate(x + dx, y);
                canvas.RotateDegrees(rotation);
                using var stemPaint = new SKPaint
                {
                    IsAntialias = true, Color = RecipePainter.Rgb(style.Stem), Style = SKPaintStyle.Stroke,
                    StrokeWidth = Math.Max(RecipePainter.MinDetail(style, unit), r * 0.16f), StrokeCap = SKStrokeCap.Round,
                };
                canvas.DrawLine(0, 0, 0, -stem, stemPaint);
                canvas.Translate(0, -stem);
            }
            else
            {
                canvas.Translate(x + dx, y + dy);
                canvas.RotateDegrees(rotation);
            }
            RecipePainter.Paint(canvas, recipe, r, t, style, unit);
            canvas.Restore();
        });
    }

    /// <summary>Draws something that may be translucent as one layer, so that its outline never shows through
    /// its fill and overlapping parts fade together. Opaque things are drawn straight to the canvas.</summary>
    private static void Faded(SKCanvas canvas, SKRect bounds, byte alpha, Action draw)
    {
        if (alpha >= 255)
        {
            draw();
            return;
        }
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        canvas.SaveLayer(bounds, layer);
        draw();
        canvas.Restore();
    }

    /// <summary>The radius a recipe's size means before any style: 0.11 of the shorter side at size 1.</summary>
    private static double BaseRadius(Entity e, int unit) => e.Recipe!.Body.Size * 0.11 * unit * e.Scale;

    /// <summary>A thing's resting radius: the recipe's size scaled up for younger eyes, kept between the
    /// smallest the child can make out and the biggest that still fits the screen.</summary>
    private static double BodyRadius(Entity e, ArtStyle style, int unit) =>
        Math.Clamp(BaseRadius(e, unit) * style.SizeScale, style.MinDiameter * unit / 2, style.MaxDiameter * unit / 2);

    private static void DrawEffect(SKCanvas canvas, Entity e, double time, int width, int height, int unit, ArtStyle style)
    {
        var recipe = e.Recipe!;
        var p = Math.Clamp(e.Age(time) / e.Lifetime, 0, 1);
        var x = (float)(e.X * width);
        var y = (float)(e.Y * height);
        float r;
        float rotation;
        byte alpha;
        if (recipe.Motion.Type != MotionType.Burst)
        {
            // A concept drawn as an effect (sun, moon, star, cloud): a thing that fades at the end.
            r = (float)(BodyRadius(e, style, unit) * EaseOutBack(Math.Min(e.Age(time) / 0.35, 1)));
            rotation = recipe.Motion.Type == MotionType.Spin ? (float)(time * 8 % 360) : 0;
            alpha = (byte)(255 * (p < 0.8 ? 1 : (1 - p) / 0.2));
        }
        else
        {
            // A burst is already big at every level, so it only takes the style's colours and outlines.
            r = (float)(BaseRadius(e, unit) * (0.6 + (2.2 * EaseOut(p))));
            rotation = (float)(e.Phase * 360) + (float)(p * 60);
            alpha = (byte)(255 * (1 - p));
        }
        if (r < 0.5f) return;

        var reachOut = (r * 2.2f) + RecipePainter.MinDetail(style, unit);
        Faded(canvas, new SKRect(x - reachOut, y - reachOut, x + reachOut, y + reachOut), alpha, () =>
        {
            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees(rotation);
            RecipePainter.Paint(canvas, recipe, r, time, style, unit);
            canvas.Restore();
        });
    }

    private void DrawText(SKCanvas canvas, Entity e, double time, int width, int height, int unit, ArtStyle style)
    {
        var age = e.Age(time);
        var letter = e.Role == EntityRole.Letter;
        var fadeStart = e.Lifetime - 0.6;
        var alpha = (byte)(255 * (age < fadeStart ? 1 : Math.Max(0, (e.Lifetime - age) / 0.6)));
        var pop = EaseOutBack(Math.Min(age / 0.25, 1));
        var size = (float)((letter ? style.LetterHeight : WordHeight) * height * pop);
        if (size < 1) return;

        var x = (float)(e.X * width);
        var y = (float)((e.Y * height) - (letter ? age * 0.03 * height : 0));
        var hue = (e.Text ?? "").Sum(ch => ch * 47) % 360;
        var colour = style.Snap(hue);

        using var font = new SKFont(typeface, size);
        // A saturated glyph in dark outline, like the things. The outline is at least the finest visible
        // line, except that a word label is small and would close up, so its outline stays a share of its size.
        var edge = Math.Min(Math.Max(size * 0.09f, RecipePainter.MinDetail(style, unit)), size * 0.12f);
        using var stroke = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = edge, StrokeJoin = SKStrokeJoin.Round,
            Color = RecipePainter.Rgb(colour.Outline),
        };
        using var fill = new SKPaint { IsAntialias = true, Color = RecipePainter.Rgb(colour.Fill) };
        var text = e.Text ?? "";
        var baseline = y + (size * 0.35f);
        var reach = (font.MeasureText(text) / 2) + edge;
        Faded(canvas, new SKRect(x - reach, baseline - (size * 1.2f), x + reach, baseline + (size * 0.5f)), alpha, () =>
        {
            canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, stroke);
            canvas.DrawText(text, x, baseline, SKTextAlign.Center, font, fill);
        });
    }

    private void DrawOverlay(SKCanvas canvas, string text, int height)
    {
        using var font = new SKFont(typeface, Math.Max(14, height * 0.025f));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70, 200) };
        canvas.DrawText(text, height * 0.02f, height * 0.04f, SKTextAlign.Left, font, paint);
    }

    /// <summary>The backdrop: a smooth sky and two smooth hills in the style's quiet colours, nothing finer.</summary>
    private static SKPicture PaintBackground(int width, int height, Ground ground)
    {
        using var recorder = new SKPictureRecorder();
        var c = recorder.BeginRecording(new SKRect(0, 0, width, height));

        using (var sky = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height * 0.75f),
            [RecipePainter.Rgb(ground.SkyTop), RecipePainter.Rgb(ground.SkyBottom)], [0, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = sky })
        {
            c.DrawRect(0, 0, width, height, paint);
        }

        Hill(c, width, height, 0.66, 0.035, 1.3, 0.4, RecipePainter.Rgb(ground.FarHill));
        Hill(c, width, height, 0.74, 0.03, 2.1, 1.7, RecipePainter.Rgb(ground.NearHill));
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

    /// <summary>Keeps <paramref name="saturation"/> of each colour's saturation (Rec. 709 luminance weights).</summary>
    private static SKColorFilter Desaturate(float saturation)
    {
        const float r = 0.213f, g = 0.715f, b = 0.072f;
        var s = saturation;
        return SKColorFilter.CreateColorMatrix(
        [
            r + ((1 - r) * s), g - (g * s), b - (b * s), 0, 0,
            r - (r * s), g + ((1 - g) * s), b - (b * s), 0, 0,
            r - (r * s), g - (g * s), b + ((1 - b) * s), 0, 0,
            0, 0, 0, 1, 0,
        ]);
    }

    private static double EaseOut(double p) => 1 - Math.Pow(1 - p, 3);

    private static double EaseOutBack(double p)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + (c3 * Math.Pow(p - 1, 3)) + (c1 * Math.Pow(p - 1, 2));
    }
}
