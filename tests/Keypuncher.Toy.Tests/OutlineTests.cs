using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

public class OutlineTests
{
    private static double SolidityOf(Action<SKCanvas, SKPaint> draw, int w = 300, int h = 300)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Black };
            draw(c, paint);
        }
        return Outline.Solidity(bitmap.GetPixelSpan(), bitmap.Width, bitmap.Height, bitmap.RowBytes);
    }

    [Fact]
    public void A_filled_box_or_disc_fills_its_hull()
    {
        Assert.InRange(SolidityOf((c, p) => c.DrawRect(new SKRect(50, 50, 250, 200), p)), 0.99, 1.0);
        Assert.InRange(SolidityOf((c, p) => c.DrawCircle(150, 150, 120, p)), 0.97, 1.0);
    }

    [Fact]
    public void Limbs_sticking_out_leave_most_of_the_hull_empty()
    {
        // A plus sign of thin bars: about a third of its octagonal hull.
        var cross = SolidityOf((c, p) =>
        {
            c.DrawRect(new SKRect(135, 20, 165, 280), p);
            c.DrawRect(new SKRect(20, 135, 280, 165), p);
        });
        Assert.InRange(cross, 0.25, 0.45);
        Assert.True(Outline.IsClear(300, 300, cross));
    }

    [Fact]
    public void Lumps_and_tiny_pictures_are_not_clear()
    {
        Assert.False(Outline.IsClear(300, 300, 0.9));
        Assert.False(Outline.IsClear(165, 117, 0.5));
        Assert.True(Outline.IsClear(512, 346, 0.72));
        Assert.Equal(0, SolidityOf((_, _) => { }));
    }
}
