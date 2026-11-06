using System.Buffers;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>
/// Turns a photo and the matte a cut-out model made for it into the picture the toy draws. The matte says, pixel by
/// pixel, how much of the photo is the thing; this keeps one thing (the biggest solid patch of matte, plus any sizeable
/// patch almost touching it, such as a second horn), throws the specks and the background away, trims to the thing and
/// shrinks it to the toy's size. It also says no when the matte looks wrong: a thing too small to be the subject, one
/// that fills the whole photo, or one running off three edges, which is a background rather than a thing.
/// Pure arithmetic, no model: the tests drive it with hand-made mattes.
/// </summary>
public static class CutOutPost
{
    /// <summary>Matte values at or above this are the thing; below, background or a soft edge.</summary>
    public const int Threshold = 128;
    /// <summary>Alpha below this is noise from the model and is made fully transparent.</summary>
    public const int MinAlpha = 25;
    /// <summary>A patch this big relative to the main one, and this close to it, is part of the thing.</summary>
    public const double AttachShare = 0.05;
    public const int AttachDistance = 3;
    public const double MinCoverage = 0.08, MaxCoverage = 0.92;
    public const int MaxEdges = 2;
    /// <summary>Transparent pixels kept round the thing, so an outline has room.</summary>
    public const int Border = 2;

    /// <summary>The number of photo edges the thing may touch before it is called a background.</summary>
    private const int TooManyEdges = MaxEdges + 1;

    /// <summary>
    /// Makes the picture: RGBA unpremultiplied, trimmed to the thing plus <see cref="Border"/> px, long side at most
    /// <see cref="ICutOut.MaxSide"/>. The matte is one byte per photo pixel, row by row, 255 meaning fully the thing.
    /// Null, with the reason, when the matte fails its gate; <paramref name="coverage"/> is the share of the photo the kept
    /// thing covers either way.
    /// </summary>
    public static SKBitmap? Finish(SKBitmap rgba, byte[] matte, out double coverage, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(matte);
        int w = rgba.Width, h = rgba.Height;
        if (matte.Length != w * h) throw new ArgumentException($"The matte has {matte.Length} bytes for a {w}x{h} photo.", nameof(matte));
        coverage = 0;
        if (w == 0 || h == 0) { reason = "too small"; return null; }

        var pool = ArrayPool<int>.Shared;
        var labels = pool.Rent(w * h);
        var stack = pool.Rent(w * h);
        var region = ArrayPool<byte>.Shared.Rent(w * h);
        try
        {
            Array.Clear(labels, 0, w * h);
            var patches = Label(matte, w, h, labels, stack);
            if (patches.Count == 0) { reason = "too small"; return null; }

            // The biggest patch is the thing; patches nearly touching it and not tiny beside it belong to it too.
            var main = 0;
            for (var i = 1; i < patches.Count; i++) if (patches[i].Area > patches[main].Area) main = i;
            var kept = new bool[patches.Count];
            var only = new bool[patches.Count];
            kept[main] = only[main] = true;
            var attached = false;
            Dilate(labels, w, h, only, region);
            for (var i = 0; i < patches.Count; i++)
            {
                if (i == main || patches[i].Area < AttachShare * patches[main].Area) continue;
                if (Overlaps(labels, region, w, patches[i], i + 1)) { kept[i] = true; attached = true; }
            }

            var box = patches[main];
            var area = 0L;
            var edges = 0;
            for (var i = 0; i < patches.Count; i++)
            {
                if (!kept[i]) continue;
                area += patches[i].Area;
                edges |= patches[i].Edges;
                box = box.Union(patches[i]);
            }
            coverage = area / (double)(w * h);
            if (coverage < MinCoverage) { reason = "too small"; return null; }
            if (coverage > MaxCoverage) { reason = "fills the frame"; return null; }
            if (System.Numerics.BitOperations.PopCount((uint)edges) >= TooManyEdges) { reason = "touches three edges"; return null; }

            // The soft edge round the kept patches keeps its alpha; everything else goes.
            if (attached) Dilate(labels, w, h, kept, region);
            int left = Math.Max(0, box.Left - Border), top = Math.Max(0, box.Top - Border);
            int right = Math.Min(w - 1, box.Right + Border), bottom = Math.Min(h - 1, box.Bottom + Border);
            var trimmed = Compose(rgba, matte, region, left, top, right - left + 1, bottom - top + 1);
            reason = null;
            return Shrink(trimmed);
        }
        finally
        {
            pool.Return(labels);
            pool.Return(stack);
            ArrayPool<byte>.Shared.Return(region);
        }
    }

    /// <summary>One patch of matte at or above the threshold, 4-connected; Edges has a bit per photo edge it touches.</summary>
    private readonly record struct Patch(int Area, int Left, int Top, int Right, int Bottom, int Edges)
    {
        /// <summary>The box and edges round both patches (the area is this one's).</summary>
        public Patch Union(Patch o) => new(Area, Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom), Edges | o.Edges);
    }

    /// <summary>Numbers every patch from 1 in <paramref name="labels"/> and measures it. Iterative, so a patch the size of
    /// the photo does not overflow the stack.</summary>
    private static List<Patch> Label(byte[] matte, int w, int h, int[] labels, int[] stack)
    {
        var patches = new List<Patch>();
        for (var start = 0; start < w * h; start++)
        {
            if (labels[start] != 0 || matte[start] < Threshold) continue;
            var label = patches.Count + 1;
            int area = 0, left = w, top = h, right = -1, bottom = -1, edges = 0;
            var depth = 0;
            labels[start] = label;
            stack[depth++] = start;
            while (depth > 0)
            {
                var p = stack[--depth];
                int x = p % w, y = p / w;
                area++;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
                if (x == 0) edges |= 1;
                if (x == w - 1) edges |= 2;
                if (y == 0) edges |= 4;
                if (y == h - 1) edges |= 8;
                if (x > 0 && labels[p - 1] == 0 && matte[p - 1] >= Threshold) { labels[p - 1] = label; stack[depth++] = p - 1; }
                if (x < w - 1 && labels[p + 1] == 0 && matte[p + 1] >= Threshold) { labels[p + 1] = label; stack[depth++] = p + 1; }
                if (y > 0 && labels[p - w] == 0 && matte[p - w] >= Threshold) { labels[p - w] = label; stack[depth++] = p - w; }
                if (y < h - 1 && labels[p + w] == 0 && matte[p + w] >= Threshold) { labels[p + w] = label; stack[depth++] = p + w; }
            }
            patches.Add(new Patch(area, left, top, right, bottom, edges));
        }
        return patches;
    }

    /// <summary>Marks in <paramref name="region"/> every pixel within <see cref="AttachDistance"/> (a square) of a pixel
    /// of a wanted patch (<paramref name="wanted"/> is indexed by label less one). Two passes, along rows then columns.</summary>
    private static void Dilate(int[] labels, int w, int h, bool[] wanted, byte[] region)
    {
        const int d = AttachDistance;
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            // Along the row: a pixel is marked when any pixel up to d away on the row is wanted.
            var last = -(d + 1);
            for (var x = 0; x < w; x++)
            {
                if (labels[row + x] is var l && l > 0 && wanted[l - 1]) last = x;
                region[row + x] = (byte)(x - last <= d ? 1 : 0);
            }
            last = w + d;
            for (var x = w - 1; x >= 0; x--)
            {
                if (labels[row + x] is var l && l > 0 && wanted[l - 1]) last = x;
                if (last - x <= d) region[row + x] = 1;
            }
        }
        // Down each column, over the row result; a mark from the row pass is turned into 2 so the way back up can tell
        // it from the 1s this pass adds.
        for (var x = 0; x < w; x++)
        {
            var last = -(d + 1);
            for (var y = 0; y < h; y++)
            {
                var i = (y * w) + x;
                if (region[i] == 1) { last = y; region[i] = 2; }
                else if (y - last <= d) region[i] = 1;
            }
            last = h + d;
            for (var y = h - 1; y >= 0; y--)
            {
                var i = (y * w) + x;
                if (region[i] == 2) last = y;
                else if (last - y <= d) region[i] = 1;
            }
        }
    }

    /// <summary>Whether any pixel of the patch with this label lies in the marked region.</summary>
    private static bool Overlaps(int[] labels, byte[] region, int w, Patch patch, int label)
    {
        for (var y = patch.Top; y <= patch.Bottom; y++)
        {
            var row = y * w;
            for (var x = patch.Left; x <= patch.Right; x++)
                if (labels[row + x] == label && region[row + x] != 0) return true;
        }
        return false;
    }

    /// <summary>The photo's colours with the matte as alpha inside the region, transparent outside, cut to the box.</summary>
    private static SKBitmap Compose(SKBitmap rgba, byte[] matte, byte[] region, int left, int top, int width, int height)
    {
        var src = rgba.ColorType == SKColorType.Rgba8888 ? rgba : rgba.Copy(SKColorType.Rgba8888);
        try
        {
            var picture = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
            var dst = picture.GetPixelSpan();
            var pixels = src.GetPixelSpan();
            var w = rgba.Width;
            for (var y = 0; y < height; y++)
            {
                var from = pixels.Slice(((top + y) * src.RowBytes) + (left * 4), width * 4);
                var to = dst.Slice(y * picture.RowBytes, width * 4);
                from.CopyTo(to);
                var i = ((top + y) * w) + left;
                for (var x = 0; x < width; x++, i++)
                {
                    var a = region[i] != 0 ? matte[i] : 0;
                    to[(x * 4) + 3] = (byte)(a < MinAlpha ? 0 : a);
                }
            }
            return picture;
        }
        finally { if (!ReferenceEquals(src, rgba)) src.Dispose(); }
    }

    /// <summary>Brings the long side down to the toy's size; a smaller picture is left as it is.</summary>
    private static SKBitmap Shrink(SKBitmap picture)
    {
        var longSide = Math.Max(picture.Width, picture.Height);
        if (longSide <= ICutOut.MaxSide) return picture;
        var k = ICutOut.MaxSide / (float)longSide;
        var small = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Round(picture.Width * k)), Math.Max(1, (int)Math.Round(picture.Height * k)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (picture)
        {
            if (picture.ScalePixels(small, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))) return small;
        }
        small.Dispose();
        throw new InvalidOperationException("The cut-out could not be scaled.");
    }
}
