using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Realistic pictures of real things, one per name, in a folder: <c>sable-antelope.png</c> for "sable antelope".
/// A picture is a cut-out, a PNG or WebP whose background is transparent. Each thing climbs a realism ladder
/// (<see cref="PictureStage"/>) as the child meets it: a few meetings as its stock shape, then its silhouette, then
/// flat colours, then the picture, so a new thing starts out as plain as everything else and becomes real over
/// days. A meeting is a thing of that name appearing, at most once every <see cref="MeetingGap"/>, so a key pressed
/// nine times in ten seconds is one meeting. Meetings are counted for every name, picture or not, so a thing picked
/// today starts counting before its picture lands; they are kept in <c>meetings.json</c> in the same folder and
/// saved by <see cref="Save"/>. A thing with no picture keeps its stock shape, and the calm level caps the stage.
/// <para>
/// All decoding and drawing of pictures happens on the thread pool: <see cref="Stage"/> says Shape and
/// <see cref="Paint"/> draws nothing until the work is done, and <see cref="Pump"/>, called once a frame, adopts what
/// finished. A picture that is missing is looked for again whenever the folder changes, so one written during play
/// is seen within seconds. Writers must write to a temporary name without the picture extension and rename into
/// place, so a half-written file is never read. The public surface is for one thread.
/// </para>
/// </summary>
public sealed class PictureLibrary : IDisposable
{
    /// <summary>Meetings at each stage before the next one: a thing is its shape for its first three meetings,
    /// its silhouette for the next three, flat colours for three more, then the picture.</summary>
    public const int MeetingsPerStage = 3;
    /// <summary>Appearances of a name within this time count as one meeting: the ladder is climbed over separate
    /// plays, not within one burst of pressing.</summary>
    public static readonly TimeSpan MeetingGap = TimeSpan.FromMinutes(20);
    /// <summary>Names met this long ago with no picture are forgotten when the counts are loaded.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromDays(60);
    /// <summary>A missing picture is looked for again at least this often, and sooner when the folder changes.</summary>
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);
    /// <summary>Pictures are scaled down to this on their longest side when loaded; a thing is never drawn bigger.</summary>
    internal const int MaxSide = 512;
    /// <summary>At most this many pictures stay decoded; the least recently drawn go first.</summary>
    private const int MaxLoaded = 48;
    private const string MeetingsFile = "meetings.json";
    private static readonly string[] Extensions = [".png", ".webp"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly ConcurrentQueue<(string Slug, Picture? Picture)> _loaded = new();
    private readonly ConcurrentQueue<(Picture Picture, CompositeKey Key, SKImage? Image)> _composed = new();
    private readonly Dictionary<string, Meeting> _meetings;
    private DateTime _folderStamp;
    private long _uses;
    private int _pending;

    /// <param name="clock">The wall clock meetings are spaced by; the real one unless a test says otherwise.</param>
    public PictureLibrary(string folder, Func<DateTime>? clock = null)
    {
        _folder = folder;
        _clock = clock ?? (() => DateTime.UtcNow);
        _meetings = LoadMeetings(Path.Combine(folder, MeetingsFile), _clock());
        _folderStamp = FolderStamp();
    }

    /// <summary>When set, every thing with a picture is drawn at this stage (within the level's cap), whatever
    /// its meetings: for the Preview sheet and for trying pictures out.</summary>
    public PictureStage? Forced { get; set; }

    /// <summary>Whether <see cref="Save"/> has something to write.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Whether any picture is still being decoded or drawn on the pool.</summary>
    public bool Busy => _pending > 0;

    /// <summary>How many times a picture has been drawn afresh at some size and stage, for tests of the caching.</summary>
    public int CompositeDraws { get; private set; }

    /// <summary>The file name, without extension, that a thing's picture is kept under; see <see cref="Slugs.For"/>.</summary>
    public static string Slug(string name) => Slugs.For(name);

    /// <summary>The stage a thing met this many times is drawn at, before the level's cap.</summary>
    public static PictureStage StageFor(int meetings) =>
        (PictureStage)Math.Clamp(meetings / MeetingsPerStage, 0, (int)PictureStage.Photo);

    /// <summary>How many times the child has met a thing of this name.</summary>
    public int Meetings(string name) => _meetings.TryGetValue(Slug(name), out var m) ? m.N : 0;

    /// <summary>Counts a meeting with a thing, unless one was counted within <see cref="MeetingGap"/>. Returns
    /// whether it counted.</summary>
    public bool Meet(string name)
    {
        var slug = Slug(name);
        if (slug.Length == 0) return false;
        var now = _clock();
        var had = _meetings.TryGetValue(slug, out var m) ? m : new Meeting(0, null);
        if (had.Last is { } last && now - last < MeetingGap) return false;
        _meetings[slug] = new Meeting(had.N + 1, now);
        Dirty = true;
        return true;
    }

    /// <summary>Writes the meeting counts, if they changed, to a temporary file renamed into place.</summary>
    public void Save()
    {
        if (!Dirty) return;
        Dirty = false;
        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, MeetingsFile);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(_meetings, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException) { Dirty = true; }
        catch (UnauthorizedAccessException) { Dirty = true; }
    }

    /// <summary>Whether this name's picture is loaded and ready to draw.</summary>
    public bool Has(string name) => Lookup(Slug(name)) is not null;

    /// <summary>Whether this name's picture file is still being read.</summary>
    public bool IsLoading(string name) => _entries.TryGetValue(Slug(name), out var e) && e.Loading;

    /// <summary>The stage this recipe would be drawn at now: Shape without a loaded picture, otherwise its
    /// meetings' rung, capped by the level.</summary>
    public PictureStage Stage(Recipe recipe, ArtStyle style)
    {
        TryStage(recipe, style, out var stage);
        return stage;
    }

    /// <summary>As <see cref="Stage"/>, but false while the picture is still being read: the answer is Shape for
    /// now and may change in a moment.</summary>
    public bool TryStage(Recipe recipe, ArtStyle style, out PictureStage stage)
    {
        var slug = Slug(recipe.Name);
        stage = PictureStage.Shape;
        if (Lookup(slug) is null) return !(_entries.TryGetValue(slug, out var e) && e.Loading);
        var rung = Forced ?? StageFor(_meetings.TryGetValue(slug, out var m) ? m.N : 0);
        stage = (PictureStage)Math.Min((int)rung, (int)style.MaxPictureStage);
        return true;
    }

    /// <summary>Forgets what is known about a name, so its file is read again: for when a picture is written,
    /// replaced or hidden while the toy runs.</summary>
    public void Invalidate(string name)
    {
        if (_entries.Remove(Slug(name), out var e)) e.Picture?.Dispose();
    }

    /// <summary>Forgets every missing picture, so the folder is looked at again.</summary>
    public void Refresh()
    {
        foreach (var slug in _entries.Where(kv => kv.Value.Picture is null && !kv.Value.Loading).Select(kv => kv.Key).ToList())
            _entries.Remove(slug);
    }

    /// <summary>Adopts pictures and drawings the pool has finished and looks for missing pictures again when the
    /// folder has changed. Call once a frame, before drawing. Returns whether anything new arrived.</summary>
    public bool Pump()
    {
        var arrived = false;
        while (_loaded.TryDequeue(out var l))
        {
            Interlocked.Decrement(ref _pending);
            if (!_entries.TryGetValue(l.Slug, out var e) || !e.Loading) { l.Picture?.Dispose(); continue; }
            e.Loading = false;
            e.Picture = l.Picture;
            e.CheckedAt = _clock();
            arrived |= l.Picture is not null;
        }
        while (_composed.TryDequeue(out var c))
        {
            Interlocked.Decrement(ref _pending);
            if (c.Picture.Adopt(c.Key, c.Image)) arrived = true;
        }
        var stamp = FolderStamp();
        if (stamp != _folderStamp)
        {
            _folderStamp = stamp;
            Refresh();
        }
        if (_entries.Count > MaxLoaded)
            foreach (var kv in _entries.Where(kv => kv.Value.Picture is not null).OrderBy(kv => kv.Value.LastUsed).Take(_entries.Count - MaxLoaded).ToList())
            {
                kv.Value.Picture!.Dispose();
                _entries.Remove(kv.Key);
            }
        return arrived;
    }

    /// <summary>Runs <see cref="Pump"/> until nothing is loading or drawing, for tools and tests; the toy never waits.</summary>
    public void WaitIdle(TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (true)
        {
            Pump();
            if (!Busy || DateTime.UtcNow > until) return;
            Thread.Sleep(5);
        }
    }

    /// <summary>Draws the recipe's picture at a stage, centred on the origin in the same box a stock shape fills
    /// (about one radius each way, resting on its bottom edge) and returns true. Returns false, drawing nothing,
    /// at the Shape stage, without a loaded picture, or while this size's drawing is still being made, so the
    /// caller draws the stock shape instead.</summary>
    public bool Paint(SKCanvas c, Recipe recipe, PictureStage stage, float r, ArtStyle style, int unit)
    {
        if (stage == PictureStage.Shape || Lookup(Slug(recipe.Name)) is not { } picture) return false;
        var colour = style.Snap(recipe.Body.Hue);
        var stroke = Math.Max(RecipePainter.MinDetail(style, unit), r * (float)style.StrokeOfRadius);
        var scale = picture.Scale(r);
        var key = new CompositeKey(stage, colour, style, Picture.Bucket(stroke / scale));
        var (image, drawnFor) = picture.Composite(key);
        if (image is null || drawnFor != key)
        {
            if (picture.Request(key))
            {
                Interlocked.Increment(ref _pending);
                CompositeDraws++;
                Task.Run(() => _composed.Enqueue((picture, key, picture.Draw(key))));
            }
            // Until this width's drawing exists, the nearest width already drawn stands in, so a growing thing never
            // falls back to its stock shape between drawings.
            if (image is null) return false;
        }
        // The drawing has a margin of its outline all round; its picture rests on the box's bottom edge.
        var margin = Picture.Margin(drawnFor) * scale;
        var w = picture.Width * scale;
        var h = picture.Height * scale;
        var dest = new SKRect((-w / 2) - margin, (0.95f * r) - h - margin, (w / 2) + margin, (0.95f * r) + margin);
        c.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return true;
    }

    /// <summary>The loaded picture for a slug, or null; starts loading it, or looking for it again, as needed.</summary>
    private Picture? Lookup(string slug)
    {
        if (slug.Length == 0) return null;
        var now = _clock();
        if (_entries.TryGetValue(slug, out var e))
        {
            e.LastUsed = ++_uses;
            if (e.Picture is not null || e.Loading || now - e.CheckedAt < Recheck) return e.Picture;
        }
        else _entries[slug] = e = new Entry { LastUsed = ++_uses };

        e.CheckedAt = now;
        var path = Extensions.Select(ext => Path.Combine(_folder, slug + ext)).FirstOrDefault(File.Exists);
        if (path is null) return null;
        e.Loading = true;
        Interlocked.Increment(ref _pending);
        Task.Run(() =>
        {
            Picture? picture = null;
            try { picture = Picture.FromFile(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { }
            _loaded.Enqueue((slug, picture));
        });
        return null;
    }

    private DateTime FolderStamp()
    {
        try { return Directory.Exists(_folder) ? Directory.GetLastWriteTimeUtc(_folder) : DateTime.MinValue; }
        catch (IOException) { return DateTime.MinValue; }
        catch (UnauthorizedAccessException) { return DateTime.MinValue; }
    }

    private static Dictionary<string, Meeting> LoadMeetings(string path, DateTime now)
    {
        try
        {
            if (!File.Exists(path)) return [];
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var result = new Dictionary<string, Meeting>();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                // The first version kept a bare count per name.
                var m = p.Value.ValueKind == JsonValueKind.Number
                    ? new Meeting(p.Value.GetInt32(), null)
                    : p.Value.Deserialize<Meeting>();
                if (m is null) continue;
                if (m.Last is { } last && now - last > Forget && !Extensions.Any(ext => File.Exists(Path.Combine(Path.GetDirectoryName(path)!, p.Name + ext)))) continue;
                result[p.Name] = m;
            }
            return result;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; }
    }

    public void Dispose()
    {
        foreach (var e in _entries.Values) e.Picture?.Dispose();
        _entries.Clear();
        while (_loaded.TryDequeue(out var l)) l.Picture?.Dispose();
        while (_composed.TryDequeue(out var c)) c.Image?.Dispose();
    }

    /// <summary>One name's count and when it was last met.</summary>
    private sealed record Meeting([property: JsonPropertyName("n")] int N, [property: JsonPropertyName("last")] DateTime? Last);

    private sealed class Entry
    {
        public Picture? Picture;
        public bool Loading;
        public DateTime CheckedAt;
        public long LastUsed;
    }

    /// <summary>What one drawing of a picture is for: its stage, the thing's palette colour, the level (which sets
    /// the flat colours and ink) and the outline's width in picture pixels.</summary>
    internal readonly record struct CompositeKey(PictureStage Stage, ArtColor Colour, ArtStyle Style, int OutlinePx);

    /// <summary>One loaded cut-out, scaled to at most <see cref="MaxSide"/>, with its drawings at each stage and outline
    /// width kept, since a picture does not animate and the same thing is drawn many times a second. Drawings are made
    /// in the picture's own pixels and scaled to the screen when drawn, so a thing growing or popping in reuses one.</summary>
    internal sealed class Picture : IDisposable
    {
        /// <summary>Drawings kept per picture: a few outline widths at a stage or two.</summary>
        private const int MaxComposites = 8;
        /// <summary>The outline's share of the ink band at the photo stage, where a pale rim covers its inside.</summary>
        private const float PhotoRing = 1.8f, PhotoRim = 0.8f;

        private readonly SKBitmap _pixels;
        private readonly SKImage _photo;
        private readonly Dictionary<CompositeKey, SKImage?> _composites = [];
        private readonly Queue<CompositeKey> _order = new();
        private readonly HashSet<CompositeKey> _requested = [];
        private readonly Dictionary<(int Levels, bool Palette, ArtStyle Style), SKImage> _flat = [];
        private readonly Lock _draw = new();

        private Picture(SKBitmap pixels)
        {
            _pixels = pixels;
            _photo = SKImage.FromBitmap(pixels);
        }

        public int Width => _pixels.Width;
        public int Height => _pixels.Height;

        /// <summary>Reads a cut-out, decoding big files at a reduced size where the codec can, and trims it to the part
        /// that is not transparent. Null when the file is not a picture or is all transparent.</summary>
        public static Picture? FromFile(string path)
        {
            using var codec = SKCodec.Create(path);
            if (codec is null) return null;
            var full = codec.Info;
            if (full.Width <= 0 || full.Height <= 0) return null;
            var wanted = Math.Min(1f, MaxSide / (float)Math.Max(full.Width, full.Height));
            var dims = codec.GetScaledDimensions(wanted);
            using var decoded = SKBitmap.Decode(codec, new SKImageInfo(dims.Width, dims.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            if (decoded is null) return null;
            SKBitmap scaled;
            if (Math.Max(decoded.Width, decoded.Height) > MaxSide)
            {
                var k = MaxSide / (float)Math.Max(decoded.Width, decoded.Height);
                scaled = new SKBitmap(new SKImageInfo(Math.Max(1, (int)(decoded.Width * k)), Math.Max(1, (int)(decoded.Height * k)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
                if (!decoded.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))) { scaled.Dispose(); return null; }
            }
            else scaled = decoded.Copy(SKColorType.Rgba8888);
            using (scaled) return FromBitmap(scaled);
        }

        /// <summary>Takes a cut-out (RGBA, unpremultiplied), trimmed to the part that is not transparent.</summary>
        public static Picture? FromBitmap(SKBitmap bitmap)
        {
            var src = bitmap.ColorType == SKColorType.Rgba8888 && bitmap.AlphaType == SKAlphaType.Unpremul ? bitmap : bitmap.Copy(SKColorType.Rgba8888);
            try
            {
                var span = src.GetPixelSpan();
                var (l, t, r, b) = (src.Width, src.Height, -1, -1);
                for (var y = 0; y < src.Height; y++)
                {
                    var row = span.Slice(y * src.RowBytes, src.Width * 4);
                    for (var x = 0; x < src.Width; x++)
                        if (row[(x * 4) + 3] > 24)
                            (l, t, r, b) = (Math.Min(l, x), Math.Min(t, y), Math.Max(r, x), Math.Max(b, y));
                }
                if (r < l) return null;
                var trimmed = new SKBitmap(new SKImageInfo(r - l + 1, b - t + 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                var dst = trimmed.GetPixelSpan();
                for (var y = t; y <= b; y++)
                    span.Slice((y * src.RowBytes) + (l * 4), trimmed.Width * 4).CopyTo(dst.Slice((y - t) * trimmed.RowBytes, trimmed.Width * 4));
                return new Picture(trimmed);
            }
            finally { if (!ReferenceEquals(src, bitmap)) src.Dispose(); }
        }

        /// <summary>Pixels of the picture per pixel on screen: its longest side spans 2.1 radii, its height at most 1.9.</summary>
        public float Scale(float r) => Math.Min(2.1f * r / Math.Max(Width, Height), 1.9f * r / Height);

        /// <summary>An outline width in picture pixels, in steps of a quarter, so the pop-in, the slow growth and a
        /// sibling a little bigger share one drawing.</summary>
        public static int Bucket(float outlinePx) =>
            Math.Max(1, (int)Math.Round(Math.Pow(1.25, Math.Round(Math.Log(Math.Max(outlinePx, 1)) / Math.Log(1.25)))));

        /// <summary>The space round the picture, in picture pixels, that its rim and outline need.</summary>
        public static int Margin(CompositeKey key) => (int)MathF.Ceiling(key.OutlinePx * (key.Stage == PictureStage.Photo ? PhotoRing : 1f)) + 2;

        /// <summary>The kept drawing for a key, or failing that the drawing at the same stage and colours whose outline
        /// width is nearest, with the key it was made for; null when there is none.</summary>
        public (SKImage? Image, CompositeKey Key) Composite(CompositeKey key)
        {
            if (_composites.TryGetValue(key, out var exact) && exact is not null) return (exact, key);
            SKImage? best = null;
            var bestKey = key;
            var bestDistance = double.MaxValue;
            foreach (var (k, image) in _composites)
            {
                if (image is null || k.Stage != key.Stage || k.Colour != key.Colour || !Equals(k.Style, key.Style)) continue;
                var distance = Math.Abs(Math.Log(k.OutlinePx) - Math.Log(key.OutlinePx));
                if (distance < bestDistance) (best, bestKey, bestDistance) = (image, k, distance);
            }
            return (best, bestKey);
        }

        /// <summary>Marks a drawing as wanted; true the first time, so it is made once.</summary>
        public bool Request(CompositeKey key) => !_composites.ContainsKey(key) && _requested.Add(key);

        /// <summary>Keeps a finished drawing, dropping the oldest past the cap. Returns whether it was kept.</summary>
        public bool Adopt(CompositeKey key, SKImage? image)
        {
            _requested.Remove(key);
            if (image is null) return false;
            if (_composites.TryGetValue(key, out var old)) { old?.Dispose(); _order.Clear(); foreach (var k in _composites.Keys) _order.Enqueue(k); }
            _composites[key] = image;
            _order.Enqueue(key);
            while (_order.Count > MaxComposites)
            {
                var drop = _order.Dequeue();
                if (_composites.Remove(drop, out var dropped)) dropped?.Dispose();
            }
            return true;
        }

        /// <summary>The thing drawn at a stage in picture pixels, outline and all. Pool-thread work; one at a time per picture.</summary>
        public SKImage? Draw(CompositeKey key)
        {
            lock (_draw)
            {
                var margin = Margin(key);
                var w = Width + (2 * margin);
                var h = Height + (2 * margin);
                using var surface = SKSurface.Create(new SKImageInfo(w, h));
                if (surface is null) return null;
                var c = surface.Canvas;
                c.Clear(SKColors.Transparent);
                var dest = new SKRect(margin, margin, margin + Width, margin + Height);
                var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
                var outline = RecipePainter.Rgb(key.Colour.Outline);

                // The outline is the picture's own shape grown by the stroke, in ink; at the photo stage a pale rim
                // inside it, like a cut-out sticker, so a photo stands off any landscape.
                var photo = key.Stage == PictureStage.Photo;
                Grown(c, dest, sampling, outline, key.OutlinePx * (photo ? PhotoRing : 1f));
                if (photo) Grown(c, dest, sampling, SKColors.White, key.OutlinePx * PhotoRim);

                switch (key.Stage)
                {
                    case PictureStage.Silhouette:
                        using (var fill = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(RecipePainter.Rgb(key.Colour.Fill), SKBlendMode.SrcIn) })
                            c.DrawImage(_photo, dest, sampling, fill);
                        break;
                    case PictureStage.Flat:
                        c.DrawImage(Flat(key.Style), dest, sampling);
                        break;
                    default:
                        c.DrawImage(_photo, dest, sampling);
                        break;
                }
                return surface.Snapshot();
            }
        }

        /// <summary>The picture's shape in one colour, grown by <paramref name="width"/> pixels all round with rounded
        /// corners: a square dilation softened by a blur and cut back to a hard edge.</summary>
        private void Grown(SKCanvas c, SKRect dest, SKSamplingOptions sampling, SKColor colour, float width)
        {
            if (width <= 0) return;
            var dilate = Math.Max(0.5f, width * 0.7f);
            var soften = Math.Max(0.25f, width * 0.3f);
            using var dilated = SKImageFilter.CreateDilate(dilate, dilate);
            using var blurred = SKImageFilter.CreateBlur(soften, soften, dilated);
            using var ink = new SKPaint { ColorFilter = SKColorFilter.CreateBlendMode(colour, SKBlendMode.SrcIn), ImageFilter = blurred };
            using var edge = new SKPaint { ColorFilter = HardEdge() };
            c.SaveLayer(edge);
            c.DrawImage(_photo, dest, sampling, ink);
            c.Restore();
        }

        /// <summary>An alpha curve that turns a blurred edge back into a crisp one, a little anti-aliased.</summary>
        private static SKColorFilter HardEdge()
        {
            var alpha = new byte[256];
            var keep = new byte[256];
            for (var i = 0; i < 256; i++)
            {
                keep[i] = (byte)i;
                alpha[i] = (byte)Math.Clamp((i - 100) * 255 / 56, 0, 255);
            }
            return SKColorFilter.CreateTable(alpha, keep, keep, keep);
        }

        /// <summary>Fewer flat colours for the youngest, as with every other detail.</summary>
        internal static int FlatLevels(ArtStyle style) => Math.Clamp(style.MaxLobes - 2, 3, 6);

        /// <summary>The picture in a few flat colours, found by k-means over its opaque pixels and, where the level
        /// says so, snapped to the palette plus ink and white: a picture-book version of the photo.</summary>
        internal SKImage Flat(ArtStyle style)
        {
            var levels = FlatLevels(style);
            var flatKey = (levels, style.PalettePictures, style);
            if (_flat.TryGetValue(flatKey, out var known)) return known;
            // Pixels are matched to the picture's own colours, which are then shown as the level's: matching them to
            // the palette directly would send every brown to the ink.
            var colours = Quantise(_pixels, levels);
            var shown = style.PalettePictures ? Snapped(colours, MiddleLightness(_pixels), style) : colours;
            var flat = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var src = _pixels.GetPixelSpan();
            var dst = flat.GetPixelSpan();
            for (var y = 0; y < Height; y++)
                for (var x = 0; x < Width; x++)
                {
                    var i = (y * _pixels.RowBytes) + (x * 4);
                    var o = (y * flat.RowBytes) + (x * 4);
                    if (src[i + 3] < 128) { dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0; continue; }
                    var n = shown[Nearest(colours, src[i], src[i + 1], src[i + 2])];
                    (dst[o], dst[o + 1], dst[o + 2], dst[o + 3]) = (n.Red, n.Green, n.Blue, 255);
                }
            var image = SKImage.FromBitmap(flat);
            flat.Dispose();
            _flat[flatKey] = image;
            return image;
        }

        /// <summary>Each colour moved into the level's palette: the tones well darker than the picture's middle to its
        /// ink, pale greys well lighter than it to white, and the rest to the fill nearest in hue, so a brown animal is
        /// drawn in the level's own few colours with its shadows and its pale belly kept.</summary>
        private static SKColor[] Snapped(SKColor[] colours, float middleLightness, ArtStyle style)
        {
            var dark = Math.Max(10, middleLightness * 0.6f);
            var pale = Math.Min(88, middleLightness + 30);
            return [.. colours.Select(c =>
            {
                c.ToHsl(out var h, out var sat, out var light);
                return light < dark ? RecipePainter.Rgb(style.Ink)
                    : light > pale && sat < 45 ? SKColors.White
                    : RecipePainter.Rgb(style.Snap((int)Math.Round(h)).Fill);
            })];
        }

        /// <summary>The median lightness (0 to 100) of the picture's opaque pixels, sampled.</summary>
        private static float MiddleLightness(SKBitmap bitmap)
        {
            var span = bitmap.GetPixelSpan();
            var lights = new List<float>();
            var step = Math.Max(1, (int)Math.Sqrt(bitmap.Width * bitmap.Height / 6000.0));
            for (var y = 0; y < bitmap.Height; y += step)
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var i = (y * bitmap.RowBytes) + (x * 4);
                    if (span[i + 3] < 128) continue;
                    int max = Math.Max(span[i], Math.Max(span[i + 1], span[i + 2])), min = Math.Min(span[i], Math.Min(span[i + 1], span[i + 2]));
                    lights.Add((max + min) / 2f * 100 / 255);
                }
            if (lights.Count == 0) return 50;
            lights.Sort();
            return lights[lights.Count / 2];
        }

        /// <summary>A few colours that stand for the whole picture: plain k-means on a sample of its opaque pixels,
        /// started from evenly spaced brightnesses so the result is the same every time.</summary>
        internal static SKColor[] Quantise(SKBitmap bitmap, int k)
        {
            var span = bitmap.GetPixelSpan();
            var sample = new List<(float R, float G, float B)>();
            var step = Math.Max(1, (int)Math.Sqrt(bitmap.Width * bitmap.Height / 6000.0));
            for (var y = 0; y < bitmap.Height; y += step)
                for (var x = 0; x < bitmap.Width; x += step)
                {
                    var i = (y * bitmap.RowBytes) + (x * 4);
                    if (span[i + 3] >= 128) sample.Add((span[i], span[i + 1], span[i + 2]));
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

        private static int Nearest(SKColor[] colours, int r, int g, int b)
        {
            var (best, bestD) = (0, int.MaxValue);
            for (var j = 0; j < colours.Length; j++)
            {
                var d = Sq(r - colours[j].Red) + Sq(g - colours[j].Green) + Sq(b - colours[j].Blue);
                if (d < bestD) (best, bestD) = (j, d);
            }
            return best;
        }

        private static float Sq(float v) => v * v;
        private static int Sq(int v) => v * v;

        public void Dispose()
        {
            lock (_draw)
            {
                _photo.Dispose();
                _pixels.Dispose();
                foreach (var c in _composites.Values) c?.Dispose();
                _composites.Clear();
                foreach (var f in _flat.Values) f.Dispose();
                _flat.Clear();
            }
        }
    }
}
