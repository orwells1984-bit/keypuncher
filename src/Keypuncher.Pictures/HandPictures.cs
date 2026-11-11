using Keypuncher.Core;
using Keypuncher.Director;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>What adding a grown-up's own picture came to.</summary>
public enum HandOutcome
{
    /// <summary>The picture is in play, and the one it took the place of is kept.</summary>
    Added,
    /// <summary>The file is not a picture that can be read.</summary>
    NotAPicture,
    /// <summary>The picture has a background and the cut-out models are not downloaded.</summary>
    NeedsCutOutModel,
    /// <summary>The cut-out models could not find one whole thing in the picture.</summary>
    CutOutFailed,
}

/// <param name="Id">The new picture's number, when it was added.</param>
/// <param name="Reason">Why the cut-out failed, when it did.</param>
public sealed record HandResult(HandOutcome Outcome, int? Id = null, string? Reason = null);

/// <summary>Puts a picture a grown-up chose from their own files in play for a thing. A picture that already has a
/// transparent background is taken as the cut-out it is; any other is cut out like a fetched photo. The picture it
/// takes the place of is kept, as with a fetched one. The check never takes a grown-up's own picture out of play.</summary>
public static class HandPictures
{
    /// <summary>A picture counts as cut out already when at least this share of it is see-through, and at least
    /// <see cref="TransparentEdgeShare"/> of its edge: a thing cut out stands clear of most of its frame, while a
    /// photo with rounded corners or a logo with a hole in it does not.</summary>
    public const double TransparentShare = 0.1;
    public const double TransparentEdgeShare = 0.5;

    /// <summary>Blocking, and slow when the picture needs cutting out: call it off the UI thread.</summary>
    /// <param name="extension">The file's extension, with its dot, for the copy kept under <c>raw/</c>.</param>
    public static HandResult Add(PictureFolder folder, ICutOut cutOut, string name, RealCategory category, byte[] bytes, string extension, DateTime now)
    {
        var slug = Slugs.For(name);
        using var photo = PhotoFetcher.Decode(bytes);
        if (photo is null || slug.Length == 0) return new HandResult(HandOutcome.NotAPicture);

        SKBitmap picture;
        string how;
        if (IsCutOut(photo))
        {
            var k = Math.Min(1.0, ICutOut.MaxSide / (double)Math.Max(photo.Width, photo.Height));
            picture = CutOutPost.Scale(photo, Math.Max(1, (int)Math.Round(photo.Width * k)), Math.Max(1, (int)Math.Round(photo.Height * k)));
            how = "already a cut-out";
        }
        else
        {
            if (!cutOut.IsAvailable) return new HandResult(HandOutcome.NeedsCutOutModel);
            var result = cutOut.Cut(photo);
            if (result.Picture is null) return new HandResult(HandOutcome.CutOutFailed, Reason: result.Reason ?? "no matte");
            picture = result.Picture;
            how = $"{result.Model}, covers {(int)Math.Round(result.Coverage * 100)}% of the picture";
        }
        using (picture)
        {
            var id = folder.NextId(slug);
            folder.Keep(slug);
            var kept = PictureFolder.RawExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ? extension.ToLowerInvariant() : ".png";
            folder.WriteRaw(slug, kept, bytes);
            folder.WriteSidecar(new PictureSidecar(slug, name, category, PictureSource.Hand, now) { Id = id, CutOut = how });
            folder.WritePicture(slug, picture);
            folder.Wanted.Remove(slug);
            return new HandResult(HandOutcome.Added, id);
        }
    }

    /// <summary>Whether a decoded picture (RGBA, unpremultiplied) has a see-through background already.</summary>
    public static bool IsCutOut(SKBitmap rgba)
    {
        var pixels = rgba.GetPixelSpan();
        int clear = 0, seen = 0, clearEdge = 0, edge = 0;
        // Every fourth pixel of every fourth row is plenty to tell a background from none.
        for (var y = 0; y < rgba.Height; y += 4)
        {
            var row = y * rgba.RowBytes;
            for (var x = 0; x < rgba.Width; x += 4, seen++)
            {
                var isClear = pixels[row + (x * 4) + 3] < 16;
                if (isClear) clear++;
                if (y < 4 || x < 4 || y >= rgba.Height - 4 || x >= rgba.Width - 4)
                {
                    edge++;
                    if (isClear) clearEdge++;
                }
            }
        }
        return seen > 0 && clear >= seen * TransparentShare && clearEdge >= edge * TransparentEdgeShare;
    }
}
