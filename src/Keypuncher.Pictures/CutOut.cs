using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>Turns a photo (or a generated image) of one thing into the transparent cut-out the toy draws: RGBA,
/// unpremultiplied, the subject trimmed to its box, long side at most <see cref="MaxSide"/> px.</summary>
public interface ICutOut
{
    /// <summary>The long side of a finished cut-out, the size <c>PictureLibrary</c> draws at.</summary>
    const int MaxSide = 512;

    /// <summary>Which model made the cut: "isnet", "birefnet-lite", "fake".</summary>
    string Name { get; }

    /// <summary>False while the model file is not on disk; the fetcher then keeps the raw photo for later.</summary>
    bool IsAvailable { get; }

    /// <summary>Cuts the subject out. Slow (under a second to ten seconds on the CPU): call it off the UI thread.</summary>
    CutOutResult Cut(SKBitmap photo);
}

/// <summary>What a cut-out attempt produced: the picture, or why there is none.</summary>
/// <param name="Picture">The finished cut-out, owned by the caller; null when the matte failed its gate.</param>
/// <param name="Model">The cut-out model that ran.</param>
/// <param name="Coverage">The share of the photo the kept subject covers, 0 to 1.</param>
/// <param name="Reason">Why there is no picture: "too small", "fills the frame", "touches three edges", "model missing".</param>
/// <param name="Box">Where in the photo the thing was found, in photo pixels, whether or not it passed; null when
/// nothing was found at all. A thing too small to pass is cut out again from a closer crop round this box.</param>
public sealed record CutOutResult(SKBitmap? Picture, string Model, double Coverage, string? Reason, SKRectI? Box = null)
{
    public bool Succeeded => Picture is not null;
}
