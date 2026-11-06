using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

/// <summary>Drives the matte post-processing with hand-made photos and mattes: no model, no files.</summary>
public sealed class CutOutPostTests
{
    private static readonly SKColor Paint = new(200, 100, 50);

    [Fact]
    public void Two_blobs_and_specks_leave_one_thing_trimmed_at_the_toys_size()
    {
        using var photo = Photo(1200, 800);
        var matte = new byte[1200 * 800];
        Fill(matte, 1200, 198, 148, 902, 652, 20);     // a faint fringe two pixels out: thrown away
        Fill(matte, 1200, 199, 149, 901, 651, 100);    // a soft edge one pixel out: kept
        Fill(matte, 1200, 200, 150, 900, 650, 255);    // the thing
        Fill(matte, 1200, 1000, 700, 1100, 780, 255);  // another blob, far away and small beside it
        foreach (var (x, y) in new[] { (10, 10), (1190, 5), (600, 790), (50, 400) }) Fill(matte, 1200, x, y, x + 2, y + 2, 255);

        using var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason);

        Assert.NotNull(picture);
        Assert.Null(reason);
        Assert.Equal(700 * 500 / (1200.0 * 800), coverage, 1e-9);
        Assert.Equal(SKColorType.Rgba8888, picture.ColorType);
        Assert.Equal(SKAlphaType.Unpremul, picture.AlphaType);
        // The kept box is 704 by 504 with its border, brought down to the toy's long side.
        Assert.Equal(ICutOut.MaxSide, picture.Width);
        Assert.InRange(picture.Height, (504 * 512 / 704) - 1, (504 * 512 / 704) + 1);
        var middle = picture.GetPixel(picture.Width / 2, picture.Height / 2);
        Assert.Equal(255, middle.Alpha);
        Assert.Equal(Paint, middle.WithAlpha(255));
        // The corner is the transparent border; scaling may blend a little of the soft edge into it.
        Assert.InRange(picture.GetPixel(0, 0).Alpha, 0, CutOutPost.MinAlpha);
    }

    [Fact]
    public void The_soft_edge_keeps_its_alpha_and_the_faint_fringe_and_specks_go()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 48, 38, 252, 162, 20);
        Fill(matte, 300, 49, 39, 251, 161, 100);
        Fill(matte, 300, 50, 40, 250, 160, 255);
        Fill(matte, 300, 10, 10, 13, 13, 255);
        Fill(matte, 300, 270, 170, 290, 190, 255);

        using var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason);

        Assert.NotNull(picture);
        Assert.Null(reason);
        Assert.Equal(0.4, coverage, 1e-9);
        // Trimmed to the thing's box plus two pixels, and no smaller than the toy's size so nothing is scaled.
        Assert.Equal(204, picture.Width);
        Assert.Equal(124, picture.Height);
        Assert.Equal(SKAlphaType.Unpremul, picture.AlphaType);
        Assert.Equal(255, picture.GetPixel(2, 2).Alpha);           // photo (50, 40): the thing
        Assert.Equal(100, picture.GetPixel(1, 2).Alpha);           // photo (49, 40): soft edge
        Assert.Equal(0, picture.GetPixel(0, 2).Alpha);             // photo (48, 40): fringe under 25
        Assert.Equal(0, picture.GetPixel(0, 0).Alpha);
        Assert.Equal(Paint, picture.GetPixel(2, 2).WithAlpha(255));
        Assert.Equal(200 * 120, Count(picture, a => a == 255));
        Assert.Equal((202 * 122) - (200 * 120), Count(picture, a => a == 100));
    }

    [Fact]
    public void A_thing_too_small_to_be_the_subject_is_turned_down()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 100, 100, 140, 130, 255);

        var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason);

        Assert.Null(picture);
        Assert.Equal("too small", reason);
        Assert.Equal(0.02, coverage, 1e-9);
    }

    [Fact]
    public void An_empty_matte_is_turned_down()
    {
        using var photo = Photo(300, 200);
        Assert.Null(CutOutPost.Finish(photo, new byte[300 * 200], out var coverage, out var reason));
        Assert.Equal("too small", reason);
        Assert.Equal(0, coverage);
    }

    [Fact]
    public void A_thing_that_fills_the_frame_is_turned_down()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 2, 2, 298, 198, 255);

        Assert.Null(CutOutPost.Finish(photo, matte, out var coverage, out var reason));
        Assert.Equal("fills the frame", reason);
        Assert.InRange(coverage, 0.96, 0.97);
    }

    [Fact]
    public void A_thing_running_off_three_edges_is_a_background_not_a_thing()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 0, 0, 300, 100, 255);

        Assert.Null(CutOutPost.Finish(photo, matte, out var coverage, out var reason));
        Assert.Equal("touches three edges", reason);
        Assert.Equal(0.5, coverage, 1e-9);
    }

    [Fact]
    public void Two_edges_are_allowed()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 0, 0, 150, 100, 255);

        using var picture = CutOutPost.Finish(photo, matte, out _, out var reason);

        Assert.NotNull(picture);
        Assert.Null(reason);
        Assert.Equal(152, picture.Width);
        Assert.Equal(102, picture.Height);
    }

    [Fact]
    public void A_sizeable_patch_almost_touching_the_thing_is_kept_a_far_one_is_not()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 50, 40, 200, 160, 255);    // the thing, 18000 px
        Fill(matte, 300, 202, 60, 250, 140, 255);   // a second horn: 21% of it, two pixels away
        Fill(matte, 300, 260, 150, 295, 195, 255);  // 9% of it, but eleven pixels away

        using var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason);

        Assert.NotNull(picture);
        Assert.Null(reason);
        Assert.Equal((18000 + (48 * 80)) / 60000.0, coverage, 1e-9);
        Assert.Equal(204, picture.Width);
        Assert.Equal(124, picture.Height);
        Assert.Equal(255, picture.GetPixel(210 - 48, 100 - 38).Alpha);  // in the second patch
        Assert.Equal(0, picture.GetPixel(200 - 48, 100 - 38).Alpha);    // the gap between them
        Assert.Equal(255, picture.GetPixel(199 - 48, 100 - 38).Alpha);
    }

    [Fact]
    public void A_small_patch_beside_the_thing_is_a_speck()
    {
        using var photo = Photo(300, 200);
        var matte = new byte[300 * 200];
        Fill(matte, 300, 50, 40, 200, 160, 255);
        Fill(matte, 300, 202, 90, 230, 110, 255);   // 3% of the thing, two pixels away

        using var picture = CutOutPost.Finish(photo, matte, out var coverage, out _);

        Assert.NotNull(picture);
        Assert.Equal(0.3, coverage, 1e-9);
        Assert.Equal(154, picture.Width);
    }

    [Fact]
    public void A_matte_of_the_wrong_size_is_refused()
    {
        using var photo = Photo(30, 20);
        Assert.Throws<ArgumentException>(() => CutOutPost.Finish(photo, new byte[10], out _, out _));
    }

    [Fact]
    public void The_chain_tries_each_model_until_one_makes_a_picture()
    {
        using var photo = Photo(30, 20);
        var missing = new FakeCutOut("missing", available: false, succeeds: true);
        var weak = new FakeCutOut("weak", available: true, succeeds: false);
        var strong = new FakeCutOut("strong", available: true, succeeds: true);
        var chain = new CutOutChain(missing, weak, strong);

        var result = chain.Cut(photo);
        using var picture = result.Picture;

        Assert.Equal("weak+strong", chain.Name);
        Assert.True(chain.IsAvailable);
        Assert.True(result.Succeeded);
        Assert.Equal("strong", result.Model);
        Assert.Equal(0, missing.Calls);
        Assert.Equal(1, weak.Calls);
        Assert.Equal(1, strong.Calls);
    }

    [Fact]
    public void The_chain_reports_the_last_failure_or_a_missing_model()
    {
        using var photo = Photo(30, 20);
        var weak = new FakeCutOut("weak", available: true, succeeds: false);
        Assert.Equal("weak", new CutOutChain(weak).Cut(photo).Model);
        Assert.False(new CutOutChain(weak).Cut(photo).Succeeded);

        var none = new CutOutChain(new FakeCutOut("missing", available: false, succeeds: true));
        Assert.False(none.IsAvailable);
        Assert.Equal("model missing", none.Cut(photo).Reason);
    }

    private static SKBitmap Photo(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(Paint);
        return bitmap;
    }

    /// <summary>Sets the matte to one value over a box, right and bottom exclusive.</summary>
    private static void Fill(byte[] matte, int stride, int left, int top, int right, int bottom, byte value)
    {
        for (var y = top; y < bottom; y++)
            matte.AsSpan((y * stride) + left, right - left).Fill(value);
    }

    private static int Count(SKBitmap bitmap, Func<byte, bool> alpha)
    {
        var n = 0;
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
                if (alpha(pixels[(y * bitmap.RowBytes) + (x * 4) + 3])) n++;
        return n;
    }

    private sealed class FakeCutOut(string name, bool available, bool succeeds) : ICutOut
    {
        public int Calls { get; private set; }
        public string Name => name;
        public bool IsAvailable => available;

        public CutOutResult Cut(SKBitmap photo)
        {
            Calls++;
            return succeeds
                ? new CutOutResult(new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul)), name, 0.5, null)
                : new CutOutResult(null, name, 0.01, "too small");
        }
    }
}
