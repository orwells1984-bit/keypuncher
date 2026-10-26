using Keypuncher.Core;
using System.Diagnostics;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Draws the world: a soft sky and hills for the world's <see cref="Scene"/> (faded in slowly when it changes),
/// the faded history behind everything, then live things,
/// effects, letters and words. The backdrop and the history are drawn once into pixels when they change, and
/// between changes are one picture together, so a frame costs one copy of the screen plus what moves.
/// Only the few things the world marks awake move; the rest ease to a stop, and everything else dims a
/// little while a letter or word is showing. Sizes, outlines and colours come from the world's
/// <see cref="ArtStyle"/>, so a calmer level draws bigger and bolder. Use from one thread.
/// </summary>
public sealed partial class SceneRenderer(SKTypeface typeface) : IDisposable
{
    /// <summary>How fast a thing eases between moving and still, in full swings a second.</summary>
    private const double MotionEase = 1.5;
    /// <summary>History is drawn with this much of its colour, so it stays a quiet backdrop.</summary>
    private const float HistorySaturation = 0.3f;
    /// <summary>History keeps this share of its contrast and is lifted towards white for the rest, so dark
    /// outlines become pale ghosts that read as part of the sky, not as more things.</summary>
    private const float HistoryContrast = 0.45f;
    /// <summary>Bigger moves are capped at this share of the shorter screen side.</summary>
    private const double MaxMotionAmplitude = 0.03;
    /// <summary>How often a live thing's own animation (blinking, wobbling) is redrawn; its moves are every frame.</summary>
    private const double SpriteHz = 15;
    /// <summary>A word label's height as a share of the screen height; it is for the grown-up, so it stays small.</summary>
    private const double WordHeight = 0.085;

    private readonly Dictionary<long, (double Clock, double Motion)> _motion = [];
    private readonly Dictionary<long, Sprite> _sprites = [];
    private SKSurface? _baked, _still;
    private SKImage? _background, _previous, _history;
    private bool _stillDirty = true;
    private byte _stillOpacity, _stillFade;
    private SKColorFilter? _historyFilter;
    private ArtStyle? _backgroundStyle, _bakedStyle;
    private Scene? _backgroundScene;
    private int _width, _height, _bakedDrawn = -1, _bakedGeneration = -1, _bakedShown = -1;
    private double _lastTime = double.NaN;

    /// <param name="overlay">Small text for grown-ups at the top-left, e.g. the exit challenge.</param>
    /// <param name="holdCue">A ring at the top-left that fills while the grown-up holds Esc; see
    /// <c>GrownUpGate.EscCue</c>. Nothing is drawn at zero opacity.</param>
    public void Draw(SKCanvas canvas, int width, int height, World world, string? overlay = null,
        (double Progress, double Opacity) holdCue = default)
    {
        if (width <= 0 || height <= 0) return;
        if (width != _width || height != _height) Resize(width, height);

        var dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(world.Time - _lastTime, 0, 0.25);
        _lastTime = world.Time;

        var start = Stopwatch.GetTimestamp();
        var fade = UpdateBackdrop(world, width, height);
        var backdrop = Stopwatch.GetTimestamp();
        UpdateBaked(world);
        DrawStill(canvas, world, fade);

        var unit = Math.Min(width, height);
        var (spotlight, spotlightSince) = Spotlight(world);
        AdvanceMotion(world, dt, spotlight);
        var baked = Stopwatch.GetTimestamp();

        // Plants first so creatures pass in front of them, then effects, then text on top.
        foreach (var e in world.Live)
            if (e.Recipe?.Kind == RecipeKind.Plant && e.Role == EntityRole.Thing) DrawLive(canvas, e, world, width, height, unit, spotlight, spotlightSince);
        foreach (var e in world.Live)
            if (e.Recipe?.Kind != RecipeKind.Plant && e.Role == EntityRole.Thing) DrawLive(canvas, e, world, width, height, unit, spotlight, spotlightSince);
        foreach (var e in world.Live)
            if (e.Role == EntityRole.Effect) DrawEffect(canvas, e, world.Time, width, height, unit, world.Style);
        var things = Stopwatch.GetTimestamp();

        if (world.Night > 0)
        {
            using var dusk = new SKPaint { Color = new SKColor(0x1A, 0x24, 0x4A, (byte)(255 * 0.5 * Math.Clamp(world.Night, 0, 1))) };
            canvas.DrawRect(0, 0, width, height, dusk);
        }

        foreach (var e in world.Live)
            if (e.Role is EntityRole.Letter or EntityRole.Word) DrawText(canvas, e, world.Time, width, height, unit, world.Style);

        if (overlay is not null) DrawOverlay(canvas, overlay, width, height);
        if (holdCue.Opacity > 0) DrawHoldCue(canvas, holdCue.Progress, holdCue.Opacity, height, unit, world.Style);
        Timings.Add(start, backdrop, baked, things, Stopwatch.GetTimestamp());
    }

    /// <summary>How long each part of drawing has taken lately, for the grown-ups' page and the benchmark.</summary>
    public RenderTimings Timings { get; } = new();

    /// <summary>A small ring in the corner where the exit word will appear: a pale track, and an arc in the level's ink
    /// that grows clockwise from the top as the hold goes on. It changes smoothly and only after a deliberate hold,
    /// so it never flashes.</summary>
    private static void DrawHoldCue(SKCanvas canvas, double progress, double opacity, int height, float unit, ArtStyle style)
    {
        var radius = unit * 0.035f;
        var stroke = Math.Max(unit * (float)style.MinStroke, unit * 0.008f);
        var margin = height * 0.02f;
        var centre = new SKPoint(margin + radius + stroke, margin + radius + stroke);
        var alpha = (byte)(255 * Math.Clamp(opacity, 0, 1));
        var ink = new SKColor(style.Ink | 0xFF000000u);
        using var track = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, Color = SKColors.White.WithAlpha((byte)(alpha * 0.7)) };
        using var arc = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round, Color = ink.WithAlpha(alpha) };
        canvas.DrawCircle(centre, radius, track);
        var bounds = new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);
        canvas.DrawArc(bounds, -90, (float)(360 * Math.Clamp(progress, 0, 1)), useCenter: false, arc);

    }


    public void Dispose()
    {
        _baked?.Dispose();
        _still?.Dispose();
        _background?.Dispose();
        _previous?.Dispose();
        _history?.Dispose();
        _historyFilter?.Dispose();
        foreach (var sprite in _sprites.Values) sprite.Dispose();
        _sprites.Clear();
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
        if (_sprites.Count > seen.Count + 8)
            foreach (var id in _sprites.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _sprites[id].Dispose();
                _sprites.Remove(id);
            }
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
        if (Place(e, e.Age(world.Time), clock, motion, world.Limits.MaxMotionHz, width, height, unit, world.Style) is { } p)
            DrawSprite(canvas, e, p, clock, (byte)(255 * Math.Clamp(a, 0, 1)), unit, world.Style);
    }

    private void Resize(int width, int height)
    {
        (_width, _height) = (width, height);
        _background?.Dispose();
        _background = null;
        _previous?.Dispose();
        _previous = null;
        _baked?.Dispose();
        _baked = SKSurface.Create(new SKImageInfo(width, height));
        _still?.Dispose();
        // Opaque, so drawing it is a straight copy rather than a blend.
        _still = SKSurface.Create(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Opaque));
        _stillDirty = true;
        _historyFilter ??= Ghost(HistorySaturation, HistoryContrast);
        _bakedGeneration = -1;
    }

    /// <summary>Repaints the history layer when something joins it or the style changes: only the most
    /// recent few, drawn still, each one fainter the older it is, so the oldest fades away rather than
    /// dropping out.</summary>
    private void UpdateBaked(World world)
    {
        var shown = Math.Max(0, world.Limits.HistoryShown);
        if (_bakedGeneration == world.BakedGeneration && _bakedDrawn == world.Baked.Count && _bakedShown == shown && Equals(_bakedStyle, world.Style)) return;
        (_bakedGeneration, _bakedDrawn, _bakedShown, _bakedStyle) = (world.BakedGeneration, world.Baked.Count, shown, world.Style);

        var canvas = _baked!.Canvas;
        canvas.Clear(SKColors.Transparent);
        var unit = Math.Min(_width, _height);
        // Ghosted as it is drawn, so the colour filter runs once per change rather than over the screen every frame.
        using (var ghost = new SKPaint { ColorFilter = _historyFilter })
        {
            canvas.SaveLayer(ghost);
            for (var i = Math.Max(0, world.Baked.Count - shown); i < world.Baked.Count; i++)
            {
                var e = world.Baked[i];
                var rank = world.Baked.Count - 1 - i;   // 0 for the most recently retired
                DrawThing(canvas, e, 1000, e.Phase * 10, 0, HistoryFade(rank, shown), 0, _width, _height, unit, world.Style);
            }
            canvas.Restore();
        }
        _history?.Dispose();
        _history = _baked.Snapshot();
        _stillDirty = true;
    }

    /// <summary>The backdrop with the history over it, as one picture in <see cref="_still"/>. It is put together again
    /// only when one of them changes, or while a new scene fades in, when the fade has moved on by a visible step:
    /// at most 255 times over the whole fade rather than every frame.</summary>
    private void DrawStill(SKCanvas canvas, World world, double fade)
    {
        var opacity = (byte)(255 * Math.Clamp(world.Limits.HistoryOpacity, 0, 1));
        // Eased at both ends, so neither the start nor the end of the fade can be seen as a moment.
        var fadeIn = _previous is null ? (byte)255 : (byte)(255 * fade * fade * (3 - (2 * fade)));
        if (_stillDirty || opacity != _stillOpacity || fadeIn != _stillFade)
        {
            var still = _still!.Canvas;
            using var history = new SKPaint { Color = SKColors.White.WithAlpha(opacity) };
            if (_previous is not null)
            {
                using var paint = new SKPaint { Color = SKColors.White.WithAlpha(fadeIn) };
                still.DrawImage(_previous, 0, 0, SKSamplingOptions.Default);
                still.DrawImage(_background!, 0, 0, SKSamplingOptions.Default, paint);
            }
            else still.DrawImage(_background!, 0, 0, SKSamplingOptions.Default);
            still.DrawImage(_history!, 0, 0, SKSamplingOptions.Default, history);
            (_stillDirty, _stillOpacity, _stillFade) = (false, opacity, fadeIn);
        }
        canvas.DrawSurface(_still, 0, 0);
    }

    /// <summary>Where and how big a thing is this frame: the point it is drawn about (a plant's foot, a creature's
    /// centre), its rock, its radius, a plant's stem, and how far its parts can reach from that point.</summary>
    private readonly record struct Placement(float X, float Y, float Rotation, float R, float Stem, float ReachOut, bool Plant)
    {
        /// <summary>Everything the thing can cover, about the point it is drawn about.</summary>
        public SKRect Local => new(-ReachOut, -Stem - ReachOut, ReachOut, ReachOut);
    }

    /// <param name="age">Seconds since birth, for the pop-in and the slow growth.</param>
    /// <param name="clock">The thing's own clock, which only runs while it moves.</param>
    /// <param name="motion">0 for still, up to 1 for fully lively.</param>
    private static Placement? Place(Entity e, double age, double clock, double motion, double maxHz, int width, int height, int unit, ArtStyle style)
    {
        var recipe = e.Recipe!;
        var pop = EaseOutBack(Math.Min(age / 0.35, 1));
        var growth = 1 + (Math.Min(age / 600, 1) * 0.25);
        var r = (float)(BodyRadius(e, style, unit) * pop * growth);
        if (r < 0.5f) return null;

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

        // The entity's Y is where a plant's stem meets the ground; the plant sways from there.
        var stem = plant ? r * 1.6f : 0;
        var reachOut = (r * 2.2f) + RecipePainter.MinDetail(style, unit);
        return new Placement(x + dx, plant ? y : y + dy, rotation, r, stem, reachOut, plant);
    }

    /// <summary>Draws a thing about its own point, unrotated: a plant's stem from its foot and the body on top, or a
    /// creature's body centred there.</summary>
    private static void PaintLocal(SKCanvas canvas, Recipe recipe, Placement p, double t, ArtStyle style, int unit)
    {
        canvas.Save();
        if (p.Plant)
        {
            using var stemPaint = new SKPaint
            {
                IsAntialias = true, Color = RecipePainter.Rgb(style.Stem), Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(RecipePainter.MinDetail(style, unit), p.R * 0.16f), StrokeCap = SKStrokeCap.Round,
            };
            canvas.DrawLine(0, 0, 0, -p.Stem, stemPaint);
            canvas.Translate(0, -p.Stem);
        }
        RecipePainter.Paint(canvas, recipe, p.R, t, style, unit);
        canvas.Restore();
    }

    /// <summary>Draws a thing straight onto the canvas, as one layer when it is translucent.</summary>
    private static void DrawThing(SKCanvas canvas, Entity e, double age, double clock, double motion, byte alpha, double maxHz, int width, int height, int unit, ArtStyle style)
    {
        if (Place(e, age, clock, motion, maxHz, width, height, unit, style) is not { } p) return;
        var local = p.Local;
        Faded(canvas, new SKRect(p.X + local.Left, p.Y + local.Top, p.X + local.Right, p.Y + local.Bottom), alpha, () =>
        {
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            canvas.RotateDegrees(p.Rotation);
            PaintLocal(canvas, e.Recipe!, p, clock, style, unit);
            canvas.Restore();
        });
    }

    /// <summary>A live thing's last drawing, in pixels, and what it was drawn for.</summary>
    private sealed class Sprite : IDisposable
    {
        public SKImage? Image;
        public SKPoint Offset;
        public Recipe? Recipe;
        public ArtStyle? Style;
        public float R, Rotation;
        public long Tick = -1;

        public void Dispose() => Image?.Dispose();
    }

    /// <summary>Draws a live thing from its sprite, which is repainted only when the thing's own animation (its
    /// blink, its wobble, its rock) has moved on by a step of <see cref="SpriteHz"/>, or its size has changed; moving
    /// it is only where the sprite is put. Drawing an image with an opacity fades it as one piece,
    /// so a translucent thing needs no layer of its own.</summary>
    private void DrawSprite(SKCanvas canvas, Entity e, Placement p, double clock, byte alpha, int unit, ArtStyle style)
    {
        if (!_sprites.TryGetValue(e.Id, out var sprite)) _sprites[e.Id] = sprite = new Sprite();
        var tick = (long)Math.Floor(clock * SpriteHz);
        if (sprite.Image is null || sprite.Tick != tick || Math.Abs(sprite.R - p.R) > 0.25f || Math.Abs(sprite.Rotation - p.Rotation) > 3
            || !ReferenceEquals(sprite.Recipe, e.Recipe) || !Equals(sprite.Style, style))
        {
            // Recorded first, so the pixels cover only what was drawn rather than everything the thing could reach.
            // The rock is drawn into the sprite too, so it can be put down square, pixel for pixel, which is far
            // quicker than turning and smoothing an image every frame.
            var local = p.Local;
            var reach = MathF.Sqrt(MathF.Max(local.Left * local.Left, local.Right * local.Right) + MathF.Max(local.Top * local.Top, local.Bottom * local.Bottom));
            var bounds = new SKRect(-reach, -reach, reach, reach);
            using var recorder = new SKPictureRecorder();
            var recording = recorder.BeginRecording(bounds, true);
            recording.RotateDegrees(p.Rotation);
            PaintLocal(recording, e.Recipe!, p, tick / SpriteHz, style, unit);
            using var picture = recorder.EndRecording();
            var cull = SKRect.Intersect(picture.CullRect, bounds);
            var area = new SKRectI((int)Math.Floor(cull.Left) - 1, (int)Math.Floor(cull.Top) - 1, (int)Math.Ceiling(cull.Right) + 1, (int)Math.Ceiling(cull.Bottom) + 1);
            sprite.Image?.Dispose();
            sprite.Image = null;
            if (area.Width <= 0 || area.Height <= 0) return;
            using (var surface = SKSurface.Create(new SKImageInfo(area.Width, area.Height)))
            {
                surface.Canvas.Translate(-area.Left, -area.Top);
                surface.Canvas.DrawPicture(picture);
                sprite.Image = surface.Snapshot();
            }
            (sprite.Offset, sprite.Recipe, sprite.Style, sprite.R, sprite.Rotation, sprite.Tick) = (new SKPoint(area.Left, area.Top), e.Recipe, style, p.R, p.Rotation, tick);
        }

        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha) };
        canvas.DrawImage(sprite.Image, MathF.Round(p.X + sprite.Offset.X), MathF.Round(p.Y + sprite.Offset.Y), SKSamplingOptions.Default, paint);
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

    /// <summary>Text for the grown-up at the top left: lines split on newlines and wrapped to the screen's width, on
    /// a pale panel so it reads over anything, at a size that still reads in a test-mode video.</summary>
    private void DrawOverlay(SKCanvas canvas, string text, int width, int height)
    {
        using var font = new SKFont(typeface, Math.Max(14, height * 0.03f));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70, 230) };
        using var panel = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(170) };
        var margin = height * 0.02f;
        var lines = Wrap(text, font, width - (4 * margin)).Take(5).ToList();
        var lineHeight = font.Size * 1.25f;
        var widest = lines.Count == 0 ? 0 : lines.Max(l => font.MeasureText(l));
        canvas.DrawRoundRect(new SKRect(margin * 0.5f, margin * 0.5f, (margin * 1.5f) + widest, margin + (lineHeight * lines.Count)), margin * 0.5f, margin * 0.5f, panel);
        for (var i = 0; i < lines.Count; i++)
            canvas.DrawText(lines[i], margin, margin + (lineHeight * (i + 0.8f)), SKTextAlign.Left, font, paint);
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && font.MeasureText(next) > width)
                {
                    yield return line;
                    line = word;
                }
                else line = next;
            }
            if (line.Length > 0) yield return line;
        }
    }

    /// <summary>The scene's backdrop. A change of scene fades the new one in over the calm level's
    /// <see cref="CalmLimits.SceneFadeSeconds"/>, so the whole screen never changes at once; a change of style
    /// (the calm level) is a grown-up's choice and is redrawn straight away.</summary>
    /// <returns>How far the fade has come, 1 when there is none.</returns>
    private double UpdateBackdrop(World world, int width, int height)
    {
        if (_background is null || !Equals(_backgroundStyle, world.Style) || _backgroundScene != world.Scene)
        {
            var fading = _background is not null && Equals(_backgroundStyle, world.Style) && world.PreviousScene is not null;
            _previous?.Dispose();
            _previous = fading ? _background : null;
            if (!fading) _background?.Dispose();
            // Drawn into pixels once: the landscape's many shapes cost far more than a copy, every frame.
            using (var picture = PaintBackground(width, height, world.Scene, world.Style))
            using (var surface = SKSurface.Create(new SKImageInfo(width, height)))
            {
                surface.Canvas.DrawPicture(picture);
                _background = surface.Snapshot();
            }
            (_backgroundStyle, _backgroundScene) = (world.Style, world.Scene);
            _stillDirty = true;
        }

        var k = Math.Clamp((world.Time - world.SceneChangedAt) / Math.Max(0.1, world.Limits.SceneFadeSeconds), 0, 1);
        if (_previous is not null && k >= 1)
        {
            _previous.Dispose();
            _previous = null;
        }
        return k;
    }

    /// <summary>The backdrop: a smooth sky and two hills in the scene's colours (<see cref="SceneGround"/>), shaped
    /// for its landscape and dressed with what makes it that landscape (trees, water, dunes, snow on the peaks,
    /// a barn), a sun that moves across the sky with the time of day, and its weather drawn still: soft clouds,
    /// a few pale raindrops or snowflakes, a veil of fog, a faint rainbow. Everything is drawn in the ground's own
    /// colours or lighter, so the backdrop changes shape but never tone, and nothing in it moves.</summary>
    public static SKPicture PaintBackground(int width, int height, Scene scene, ArtStyle style)
    {
        var ground = SceneGround.For(style, scene);
        using var recorder = new SKPictureRecorder();
        var c = recorder.BeginRecording(new SKRect(0, 0, width, height));

        using (var sky = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, height * 0.75f),
            [RecipePainter.Rgb(ground.SkyTop), RecipePainter.Rgb(ground.SkyBottom)], [0, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = sky })
        {
            c.DrawRect(0, 0, width, height, paint);
        }

        // The same scene always draws the same, so a saved world looks as it did.
        var random = new Random(HashCode.Combine(scene.Landscape, scene.Weather, scene.Place?.Length ?? 0));
        if (scene.Weather is Weather.Clear or Weather.Cloudy or Weather.Rainbow) Sun(c, width, height, scene.TimeOfDay);
        if (scene.Weather == Weather.Rainbow) Rainbow(c, width, height);

        Land(c, width, height, scene, style, ground, random);

        if (scene.Weather is Weather.Cloudy or Weather.Rain or Weather.Snow) Clouds(c, width, height, random, scene.Weather == Weather.Cloudy ? 3 : 4);
        if (scene.Weather == Weather.Rain) Drops(c, width, height, random, snow: false);
        if (scene.Weather == Weather.Snow) Drops(c, width, height, random, snow: true);
        if (scene.Weather == Weather.Fog)
        {
            using var veil = SKShader.CreateLinearGradient(
                new SKPoint(0, height * 0.45f), new SKPoint(0, height),
                [SKColors.White.WithAlpha(0), SKColors.White.WithAlpha(120)], [0, 1], SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = veil };
            c.DrawRect(0, 0, width, height, paint);
        }
        return recorder.EndRecording();
    }

    /// <summary>A few soft clouds high in the sky, each a cluster of white circles, lighter than any sky.</summary>
    private static void Clouds(SKCanvas c, int width, int height, Random random, int count)
    {
        var unit = Math.Min(width, height);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(190) };
        for (var i = 0; i < count; i++)
        {
            var x = ((i + 0.5) / count * width) + ((random.NextDouble() - 0.5) * width / count * 0.6);
            var y = height * (0.12 + (random.NextDouble() * 0.16));
            var r = unit * (0.05 + (random.NextDouble() * 0.025));
            c.Save();
            c.Translate((float)x, (float)y);
            using var cloud = new SKPathBuilder();
            cloud.AddCircle(0, 0, (float)r);
            cloud.AddCircle((float)(-r * 1.1), (float)(r * 0.35), (float)(r * 0.7));
            cloud.AddCircle((float)(r * 1.1), (float)(r * 0.3), (float)(r * 0.75));
            cloud.AddRoundRect(new SKRect((float)(-r * 1.6), 0, (float)(r * 1.7), (float)(r * 0.95)), (float)(r * 0.45), (float)(r * 0.45));
            using var shape = cloud.Detach();
            shape.FillType = SKPathFillType.Winding;
            c.DrawPath(shape, paint);
            c.Restore();
        }
    }

    /// <summary>A sparse sprinkle of pale, still raindrops or snowflakes: enough to say what the weather is,
    /// too faint and few to read as things.</summary>
    private static void Drops(SKCanvas c, int width, int height, Random random, bool snow)
    {
        var unit = Math.Min(width, height);
        using var paint = new SKPaint
        {
            IsAntialias = true, Color = SKColors.White.WithAlpha((byte)(snow ? 200 : 150)),
            Style = snow ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = unit * 0.004f, StrokeCap = SKStrokeCap.Round,
        };
        var count = snow ? 46 : 40;
        for (var i = 0; i < count; i++)
        {
            var x = (float)(random.NextDouble() * width);
            var y = (float)(height * (0.3 + (random.NextDouble() * 0.6)));
            if (snow) c.DrawCircle(x, y, unit * (0.005f + (float)(random.NextDouble() * 0.004)), paint);
            else c.DrawLine(x, y, x - (unit * 0.006f), y + (unit * 0.022f), paint);
        }
    }

    /// <summary>A faint rainbow behind the hills, its colours mostly sky.</summary>
    private static void Rainbow(SKCanvas c, int width, int height)
    {
        uint[] bands = [0xE57373, 0xFFB74D, 0xFFF176, 0x81C784, 0x64B5F6, 0x9575CD];
        var unit = Math.Min(width, height);
        var band = unit * 0.022f;
        var radius = width * 0.36f;
        var centre = new SKPoint(width * 0.62f, height * 0.78f);
        for (var i = 0; i < bands.Length; i++)
        {
            using var paint = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = band,
                Color = RecipePainter.Rgb(bands[i]).WithAlpha(70),
            };
            var r = radius - (i * band);
            c.DrawArc(new SKRect(centre.X - r, centre.Y - r, centre.X + r, centre.Y + r), 180, 180, false, paint);
        }
    }

    /// <summary>Keeps <paramref name="saturation"/> of each colour's saturation (Rec. 709 luminance weights).</summary>
    /// <summary>The opacity of the history item <paramref name="rank"/> places from the newest, out of
    /// <paramref name="shown"/>: full for the newest, falling evenly towards nothing for the oldest.</summary>
    private static byte HistoryFade(int rank, int shown) =>
        shown <= 0 ? (byte)0 : (byte)Math.Round(255 * Math.Clamp(1 - ((double)rank / shown), 0, 1));

    /// <summary>Keeps <paramref name="saturation"/> of each colour's saturation (Rec. 709 luminance weights),
    /// then keeps <paramref name="contrast"/> of its distance from white.</summary>
    private static SKColorFilter Ghost(float saturation, float contrast)
    {
        const float r = 0.213f, g = 0.715f, b = 0.072f;
        var s = saturation;
        var k = contrast;
        var lift = 1 - k;
        return SKColorFilter.CreateColorMatrix(
        [
            k * (r + ((1 - r) * s)), k * (g - (g * s)), k * (b - (b * s)), 0, lift,
            k * (r - (r * s)), k * (g + ((1 - g) * s)), k * (b - (b * s)), 0, lift,
            k * (r - (r * s)), k * (g - (g * s)), k * (b + ((1 - b) * s)), 0, lift,
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
