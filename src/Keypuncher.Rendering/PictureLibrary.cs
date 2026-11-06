using System.Globalization;
using System.Text;
using System.Text.Json;
using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>How realistically a thing with a picture is drawn, from today's stock shape up to its photo.</summary>
public enum PictureStage
{
    /// <summary>The recipe's stock body, as if there were no picture.</summary>
    Shape,
    /// <summary>The picture's real outline, filled with the thing's palette colour and outlined like every shape.</summary>
    Silhouette,
    /// <summary>The picture reduced to a few flat colours, outlined: a picture-book drawing of the real thing.</summary>
    Flat,
    /// <summary>The picture itself, as a cut-out with a pale rim and the outline round it.</summary>
    Photo,
}

/// <summary>
/// Realistic pictures of real things, one per name, in a folder: <c>sable-antelope.png</c> for "sable antelope".
/// A picture is a cut-out, a PNG or WebP whose background is transparent. Each thing climbs a realism ladder
/// (<see cref="PictureStage"/>) as the child meets it: a few meetings as its stock shape, then its silhouette, then
/// flat colours, then the picture, so a new thing starts out as plain as everything else and becomes real over
/// days. A thing with no picture keeps its stock shape. Meetings are counted in <c>meetings.json</c> in the same
/// folder. Use from one thread.
/// </summary>
public sealed class PictureLibrary : IDisposable
{
    /// <summary>Meetings at each stage before the next one: a thing is its shape for its first three meetings,
    /// its silhouette for the next three, flat colours for three more, then the picture.</summary>
    public const int MeetingsPerStage = 3;
    /// <summary>Pictures are scaled down to this on their longest side when loaded; a thing is never drawn bigger.</summary>
    private const int MaxSide = 512;
    private const string MeetingsFile = "meetings.json";
    private static readonly string[] Extensions = [".png", ".webp"];

    private readonly string _folder;
    private readonly Dictionary<string, Picture?> _pictures = [];
    private readonly Dictionary<string, int> _meetings;

    public PictureLibrary(string folder)
    {
        _folder = folder;
        _meetings = LoadMeetings(Path.Combine(folder, MeetingsFile));
    }

    /// <summary>When set, every thing with a picture is drawn at this stage, whatever its meetings: for the
    /// Preview sheet and for trying pictures out.</summary>
    public PictureStage? Forced { get; set; }

    /// <summary>The file name, without extension, that a thing's picture is kept under: lower case, words joined by
    /// hyphens, only letters and digits kept, so "Sable antelope" and "sable  antelope!" share one picture.</summary>
    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Trim().ToLower(CultureInfo.InvariantCulture).Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>The stage a thing met this many times is drawn at.</summary>
    public static PictureStage StageFor(int meetings) =>
        (PictureStage)Math.Clamp(meetings / MeetingsPerStage, 0, (int)PictureStage.Photo);

    /// <summary>How many times the child has met a thing of this name.</summary>
    public int Meetings(string name) => _meetings.GetValueOrDefault(Slug(name));

    /// <summary>Counts one more meeting with a thing, if it has a picture (there is nothing to climb otherwise),
    /// and saves the count.</summary>
    public void Meet(string name)
    {
        var slug = Slug(name);
        if (slug.Length == 0 || Load(slug) is null) return;
        _meetings[slug] = _meetings.GetValueOrDefault(slug) + 1;
        try
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, MeetingsFile), JsonSerializer.Serialize(_meetings));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Whether there is a picture for this name.</summary>
    public bool Has(string name) => Load(Slug(name)) is not null;

    /// <summary>The stage this recipe would be drawn at now; <see cref="PictureStage.Shape"/> without a picture.</summary>
    public PictureStage Stage(Recipe recipe)
    {
        var slug = Slug(recipe.Name);
        if (Load(slug) is null) return PictureStage.Shape;
        return Forced ?? StageFor(_meetings.GetValueOrDefault(slug));
    }

    /// <summary>Draws the recipe's picture centred on the origin in the same box a stock shape fills (about one radius
    /// each way, resting on its bottom edge) and returns true; returns false, drawing nothing, when the thing
    /// is still at its shape stage or has no picture, so the caller draws the stock shape instead.</summary>
    public bool Paint(SKCanvas c, Recipe recipe, float r, ArtStyle style, int unit)
    {
        var stage = Stage(recipe);
        if (stage == PictureStage.Shape || Load(Slug(recipe.Name)) is not { } picture) return false;
        var colour = style.Snap(recipe.Body.Hue);
        var stroke = Math.Max(RecipePainter.MinDetail(style, unit), r * (float)style.StrokeOfRadius);
        // Kept by the picture until something changes, so not disposed here.
        var image = picture.Composite(stage, colour, r, stroke, style);
        if (image is null) return false;
        var scale = picture.Scale(r);
        // The composite has a margin of its outline all round; its picture rests on the box's bottom edge.
        var margin = Picture.Margin(stroke);
        var w = picture.Width * scale;
        var h = picture.Height * scale;
        var dest = new SKRect(-w / 2 - margin, (0.95f * r) - h - margin, (w / 2) + margin, (0.95f * r) + margin);
        c.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return true;
    }

    private Picture? Load(string slug)
    {
        if (slug.Length == 0) return null;
        if (_pictures.TryGetValue(slug, out var known)) return known;
        Picture? picture = null;
        foreach (var ext in Extensions)
        {
            var path = Path.Combine(_folder, slug + ext);
            if (!File.Exists(path)) continue;
            try { picture = Picture.FromFile(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { }
            if (picture is not null) break;
        }
        _pictures[slug] = picture;
        return picture;
    }

    private static Dictionary<string, int> LoadMeetings(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) is { } saved)
                return saved;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return [];
    }

    public void Dispose()
    {
        foreach (var p in _pictures.Values) p?.Dispose();
        _pictures.Clear();
    }

    /// <summary>One loaded cut-out, with its flat-colour versions worked out once and its last drawing kept, since a
    /// picture does not animate and the same thing is redrawn many times a second.</summary>
    internal sealed class Picture : IDisposable
    {
        private readonly SKImage _photo;
        private readonly Dictionary<int, SKImage> _flat = [];
        private (PictureStage Stage, ArtColor Colour, int Px, int Stroke, int Levels)? _lastKey;
        private SKImage? _last;

        private Picture(SKImage photo) => _photo = photo;

        public int Width => _photo.Width;
        public int Height => _photo.Height;

        public static Picture? FromFile(string path)
        {
            using var decoded = SKBitmap.Decode(path);
            if (decoded is null || decoded.Width == 0 || decoded.Height == 0) return null;
            var k = Math.Min(1f, MaxSide / (float)Math.Max(decoded.Width, decoded.Height));
            var info = new SKImageInfo(Math.Max(1, (int)(decoded.Width * k)), Math.Max(1, (int)(decoded.Height * k)), SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var scaled = new SKBitmap(info);
            if (!decoded.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))) return null;
            return FromBitmap(scaled);
        }

        /// <summary>Takes a cut-out, trimmed to the part that is not transparent.</summary>
        public static Picture? FromBitmap(SKBitmap bitmap)
        {
            var (l, t, r, b) = (bitmap.Width, bitmap.Height, -1, -1);
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                    if (bitmap.GetPixel(x, y).Alpha > 24)
                        (l, t, r, b) = (Math.Min(l, x), Math.Min(t, y), Math.Max(r, x), Math.Max(b, y));
            if (r < l) return null;
            using var trimmed = new SKBitmap();
            if (!bitmap.ExtractSubset(trimmed, new SKRectI(l, t, r + 1, b + 1))) return null;
            using var copy = trimmed.Copy();
            return new Picture(SKImage.FromBitmap(copy));
        }

        /// <summary>Pixels of the picture per pixel on screen: its longest side spans 2.1 radii, its height at most 1.9.</summary>
        public float Scale(float r) => Math.Min(2.1f * r / Math.Max(Width, Height), 1.9f * r / Height);

        /// <summary>The space round the picture, in screen pixels, that its rim and outline need.</summary>
        public static float Margin(float stroke) => (stroke * 1.6f) + 2;

        /// <summary>The thing drawn at a stage at its on-screen size, outline and all; kept until something changes.</summary>
        public SKImage? Composite(PictureStage stage, ArtColor colour, float r, float stroke, ArtStyle style)
        {
            var scale = Scale(r);
            var levels = FlatLevels(style);
            var key = (stage, colour, (int)MathF.Round(r), (int)MathF.Round(stroke), levels);
            if (_lastKey != key || _last is null)
            {
                _last?.Dispose();
                _last = Draw(stage, colour, scale, stroke, levels);
                _lastKey = key;
            }
            return _last;
        }

        /// <summary>Fewer flat colours for the youngest, as with every other detail.</summary>
        private static int FlatLevels(ArtStyle style) => Math.Clamp(style.MaxLobes - 2, 3, 6);

        private SKImage? Draw(PictureStage stage, ArtColor colour, float scale, float stroke, int levels)
        {
            var margin = Margin(stroke);
            var w = (int)MathF.Ceiling((Width * scale) + (2 * margin));
            var h = (int)MathF.Ceiling((Height * scale) + (2 * margin));
            if (w <= 0 || h <= 0) return null;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            var c = surface.Canvas;
            c.Clear(SKColors.Transparent);
            var dest = new SKRect(margin, margin, margin + (Width * scale), margin + (Height * scale));
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            var outline = RecipePainter.Rgb(colour.Outline);

            // The outline is the picture's own shape, grown by the stroke all round and filled with ink; then, for the
            // photo, a pale rim inside it like a cut-out sticker, so a photo stands off any landscape.
            Ring(c, dest, sampling, outline, stage == PictureStage.Photo ? stroke * 1.6f : stroke);
            if (stage == PictureStage.Photo) Ring(c, dest, sampling, SKColors.White, stroke * 0.8f);

            switch (stage)
            {
                case PictureStage.Silhouette:
                    using (var fill = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(RecipePainter.Rgb(colour.Fill), SKBlendMode.SrcIn) })
                        c.DrawImage(_photo, dest, sampling, fill);
                    break;
                case PictureStage.Flat:
                    c.DrawImage(Flat(levels), dest, sampling);
                    break;
                default:
                    c.DrawImage(_photo, dest, sampling);
                    break;
            }
            return surface.Snapshot();
        }

        /// <summary>The picture's shape in one colour, stamped in a circle round its place so it reaches out by
        /// <paramref name="width"/> pixels on every side.</summary>
        private void Ring(SKCanvas c, SKRect dest, SKSamplingOptions sampling, SKColor colour, float width)
        {
            using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(colour, SKBlendMode.SrcIn) };
            // Stamped at a few distances, not only the full width, so thin parts (a horn, a tail) grow a solid edge
            // rather than a comb of separate copies.
            const int steps = 24;
            for (var ring = 1; ring <= 3; ring++)
                for (var i = 0; i < steps; i++)
                {
                    var a = (i + (ring * 0.5f)) * MathF.Tau / steps;
                    var at = dest;
                    at.Offset(MathF.Cos(a) * width * ring / 3, MathF.Sin(a) * width * ring / 3);
                    c.DrawImage(_photo, at, sampling, paint);
                }
        }

        /// <summary>The picture in <paramref name="levels"/> flat colours, found by k-means over its opaque pixels, with
        /// its edge kept: a picture-book version of the photo.</summary>
        internal SKImage Flat(int levels)
        {
            if (_flat.TryGetValue(levels, out var known)) return known;
            using var bitmap = SKBitmap.FromImage(_photo);
            var colours = Quantise(bitmap, levels);
            using var flat = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            for (var y = 0; y < bitmap.Height; y++)
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var p = bitmap.GetPixel(x, y);
                    var n = colours[Nearest(colours, p)];
                    flat.SetPixel(x, y, p.Alpha < 128 ? SKColors.Transparent : new SKColor(n.Red, n.Green, n.Blue, 255));
                }
            var image = SKImage.FromBitmap(flat);
            _flat[levels] = image;
            return image;
        }

        /// <summary>A few colours that stand for the whole picture: plain k-means on a sample of its opaque pixels,
        /// started from evenly spaced brightnesses so the result is the same every time.</summary>
        internal static SKColor[] Quantise(SKBitmap bitmap, int k)
        {
            var sample = new List<(float R, float G, float B)>();
            var step = Math.Max(1, (int)Math.Sqrt(bitmap.Width * bitmap.Height / 6000.0));
            for (var y = 0; y < bitmap.Height; y += step)
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var p = bitmap.GetPixel(x, y);
                    if (p.Alpha >= 128) sample.Add((p.Red, p.Green, p.Blue));
                }
            if (sample.Count == 0) return [SKColors.Gray];
            sample.Sort((a, b) => (a.R + a.G + a.B).CompareTo(b.R + b.G + b.B));
            k = Math.Min(k, sample.Count);
            var centres = Enumerable.Range(0, k).Select(i => sample[(int)((i + 0.5) * sample.Count / k)]).ToArray();
            var assign = new int[sample.Count];
            for (var round = 0; round < 12; round++)
            {
                for (var i = 0; i < sample.Count; i++)
                {
                    var (best, bestD) = (0, float.MaxValue);
                    for (var j = 0; j < k; j++)
                    {
                        var d = Sq(sample[i].R - centres[j].R) + Sq(sample[i].G - centres[j].G) + Sq(sample[i].B - centres[j].B);
                        if (d < bestD) (best, bestD) = (j, d);
                    }
                    assign[i] = best;
                }
                for (var j = 0; j < k; j++)
                {
                    float r = 0, g = 0, b = 0;
                    var n = 0;
                    for (var i = 0; i < sample.Count; i++)
                        if (assign[i] == j) { r += sample[i].R; g += sample[i].G; b += sample[i].B; n++; }
                    if (n > 0) centres[j] = (r / n, g / n, b / n);
                }
            }
            return [.. centres.Select(c => new SKColor((byte)c.R, (byte)c.G, (byte)c.B)).Distinct()];
        }

        private static int Nearest(SKColor[] colours, SKColor p)
        {
            var (best, bestD) = (0, int.MaxValue);
            for (var j = 0; j < colours.Length; j++)
            {
                var d = Sq(p.Red - colours[j].Red) + Sq(p.Green - colours[j].Green) + Sq(p.Blue - colours[j].Blue);
                if (d < bestD) (best, bestD) = (j, d);
            }
            return best;
        }

        private static float Sq(float v) => v * v;
        private static int Sq(int v) => v * v;

        public void Dispose()
        {
            _photo.Dispose();
            _last?.Dispose();
            foreach (var f in _flat.Values) f.Dispose();
        }
    }
}
