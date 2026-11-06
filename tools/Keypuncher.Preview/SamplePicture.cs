using SkiaSharp;

/// <summary>A stand-in cut-out for the picture sheet when no real pictures are at hand: a shaded, four-legged,
/// horned animal on a transparent background, with enough tones that the flat-colour stage has work to do.</summary>
internal static class SamplePicture
{
    public static SKBitmap Draw()
    {
        var bitmap = new SKBitmap(new SKImageInfo(480, 360, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bitmap);
        c.Clear(SKColors.Transparent);
        using var dark = new SKPaint { IsAntialias = true, Color = new SKColor(0x2A, 0x1C, 0x14) };
        using var horn = new SKPaint { IsAntialias = true, Color = new SKColor(0x3B, 0x30, 0x28), Style = SKPaintStyle.Stroke, StrokeWidth = 9, StrokeCap = SKStrokeCap.Round };
        using var belly = new SKPaint { IsAntialias = true, Color = new SKColor(0xEE, 0xE6, 0xDA) };
        using var coat = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 110), new SKPoint(0, 250),
                [new SKColor(0x5A, 0x3A, 0x26), new SKColor(0x8C, 0x5E, 0x3C), new SKColor(0x3E, 0x28, 0x1A)], SKShaderTileMode.Clamp),
        };

        // Legs, then the body over their tops, the neck and head, and the horns sweeping back.
        foreach (var x in new[] { 130f, 165f, 300f, 335f })
            c.DrawRoundRect(new SKRect(x, 200, x + 18, 340), 8, 8, dark);
        c.DrawOval(new SKRect(100, 120, 370, 250), coat);
        c.DrawOval(new SKRect(150, 200, 320, 245), belly);
        using (var neck = new SKPathBuilder())
        {
            neck.MoveTo(330, 170);
            neck.LineTo(390, 70);
            neck.LineTo(420, 85);
            neck.LineTo(365, 190);
            neck.Close();
            using var path = neck.Detach();
            c.DrawPath(path, coat);
        }
        c.DrawOval(new SKRect(380, 55, 450, 100), coat);
        c.DrawOval(new SKRect(425, 75, 455, 95), belly);
        using (var sweep = new SKPathBuilder())
        {
            sweep.MoveTo(395, 62);
            sweep.QuadTo(370, 10, 320, 20);
            using var path = sweep.Detach();
            c.DrawPath(path, horn);
        }
        c.DrawCircle(412, 70, 5, dark);
        // A tail.
        c.DrawLine(102, 160, 80, 230, horn);
        return bitmap;
    }
}
