using SkiaSharp;

namespace Keypuncher.Rendering;

public static class Fonts
{
    /// <summary>Andika Bold, embedded in this assembly.</summary>
    public static SKTypeface LoadAndika()
    {
        using var stream = typeof(Fonts).Assembly.GetManifestResourceStream("Andika-Bold.ttf")
            ?? throw new InvalidOperationException("Andika-Bold.ttf is not embedded");
        return SKTypeface.FromStream(stream) ?? SKTypeface.Default;
    }
}
