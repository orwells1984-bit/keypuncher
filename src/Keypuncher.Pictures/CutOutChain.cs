using SkiaSharp;
using Keypuncher.Core;

namespace Keypuncher.Pictures;

/// <summary>Tries each cut-out in turn and keeps the first matte that passes its gate: the quick model first, the
/// slower one when it fails. A thing found but too small to pass (a bird in a wide shot) is looked at again from a
/// crop round it, as a photographer would zoom in. Only the models on disk are asked, and one closed while the chain
/// runs (the toy is quitting) is passed over.</summary>
public sealed class CutOutChain(params ICutOut[] cutOuts) : ICutOut
{
    /// <summary>How much room the crop leaves round the box the thing was found in, as a share of the box's size each side.</summary>
    public const double ZoomMargin = 0.5;
    /// <summary>A crop is never smaller than this on either side, so a tiny find still gives the model something to see.</summary>
    public const int MinZoomSide = 256;

    public string Name => string.Join("+", cutOuts.Where(c => c.IsAvailable).Select(c => c.Name)) is { Length: > 0 } names ? names : "none";

    public bool IsAvailable => cutOuts.Any(c => c.IsAvailable);

    public CutOutResult Cut(SKBitmap photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        CutOutResult? last = null;
        foreach (var cutOut in cutOuts.Where(c => c.IsAvailable))
        {
            CutOutResult result;
            try
            {
                result = cutOut.Cut(photo);
                if (!result.Succeeded && result.Reason == "too small" && result.Box is { } box && Zoom(photo, box) is { } crop)
                {
                    using (crop)
                    {
                        var closer = cutOut.Cut(crop);
                        if (closer.Succeeded) return closer with { Model = closer.Model + ", zoomed in" };
                        result = closer with { Box = null };
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                // The models are closed as the toy shuts down.
                Log.Debug("pictures", $"cut-out model {cutOut.Name} was closed mid-cut", e);
                continue;
            }
            if (result.Succeeded) return result;
            last = result;
        }
        return last ?? new CutOutResult(null, Name, 0, "model missing");
    }

    /// <summary>The crop to look at a small find from: the box grown by <see cref="ZoomMargin"/> each way, at least
    /// <see cref="MinZoomSide"/> square, kept inside the photo. Null when the crop would be the whole photo anyway.</summary>
    public static SKBitmap? Zoom(SKBitmap photo, SKRectI box)
    {
        var width = Math.Max(MinZoomSide, (int)Math.Round(box.Width * (1 + (2 * ZoomMargin))));
        var height = Math.Max(MinZoomSide, (int)Math.Round(box.Height * (1 + (2 * ZoomMargin))));
        width = Math.Min(width, photo.Width);
        height = Math.Min(height, photo.Height);
        if (width >= photo.Width && height >= photo.Height) return null;
        var left = Math.Clamp(box.MidX - (width / 2), 0, photo.Width - width);
        var top = Math.Clamp(box.MidY - (height / 2), 0, photo.Height - height);
        var crop = new SKBitmap(new SKImageInfo(width, height, photo.ColorType, photo.AlphaType));
        if (!photo.ExtractSubset(crop, new SKRectI(left, top, left + width, top + height))) { crop.Dispose(); return null; }
        // A subset shares the photo's pixels; a copy stands on its own.
        var copy = crop.Copy();
        crop.Dispose();
        return copy;
    }
}
