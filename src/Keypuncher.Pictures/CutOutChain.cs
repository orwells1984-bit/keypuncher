using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>Tries each cut-out in turn and keeps the first matte that passes its gate: the quick model first, the
/// slower one when it fails. Only the models on disk are asked.</summary>
public sealed class CutOutChain(params ICutOut[] cutOuts) : ICutOut
{
    public string Name => string.Join("+", cutOuts.Where(c => c.IsAvailable).Select(c => c.Name)) is { Length: > 0 } names ? names : "none";

    public bool IsAvailable => cutOuts.Any(c => c.IsAvailable);

    public CutOutResult Cut(SKBitmap photo)
    {
        CutOutResult? last = null;
        foreach (var cutOut in cutOuts.Where(c => c.IsAvailable))
        {
            var result = cutOut.Cut(photo);
            if (result.Succeeded) return result;
            last = result;
        }
        return last ?? new CutOutResult(null, Name, 0, "model missing");
    }
}
