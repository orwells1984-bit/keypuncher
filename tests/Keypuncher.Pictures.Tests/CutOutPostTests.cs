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
    public void Shrinking_does_not_tint_the_edge_with_the_photos_background()
    {
        // A red thing on a green field: the background sits under every transparent pixel of the composed picture,
        // and scaling the channels on their own would blend it into the thing's soft edge as a green halo.
        using var photo = new SKBitmap(new SKImageInfo(1400, 1000, SKColorType.Rgba8888, SKAlphaType.Premul));
        photo.Erase(SKColors.Lime);
        var matte = new byte[1400 * 1000];
        using (var c = new SKCanvas(photo))
        using (var red = new SKPaint { Color = SKColors.Red })
            c.DrawRect(new SKRect(300, 200, 1100, 800), red);
        Fill(matte, 1400, 300, 200, 1100, 800, 255);

        using var picture = CutOutPost.Finish(photo, matte, out _, out var reason);

        Assert.NotNull(picture);
        Assert.Null(reason);
        Assert.Equal(ICutOut.MaxSide, picture.Width);
        var edgePixels = 0;
        for (var y = 0; y < picture.Height; y++)
            for (var x = 0; x < picture.Width; x++)
            {
                var p = picture.GetPixel(x, y);
                if (p.Alpha == 0) continue;
                if (p.Alpha < 255) edgePixels++;
                Assert.True(p.Red >= 250 && p.Green <= 5, $"({x}, {y}) is {p}, not the thing's red");
            }
        Assert.True(edgePixels > 0, "the scaled edge has part-transparent pixels");
    }

    [Fact]
    public void Scale_keeps_the_colour_of_a_part_transparent_pixel()
    {
        // Two green columns at alpha 0, then red at full alpha, halved: the boundary pixel is half-transparent red.
        using var unpremul = new SKBitmap(new SKImageInfo(8, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 8; x++)
                unpremul.SetPixel(x, y, x < 3 ? new SKColor(0, 255, 0, 0) : new SKColor(255, 0, 0, 255));

        using var scaled = CutOutPost.Scale(unpremul, 4, 1);

        Assert.Equal(SKAlphaType.Unpremul, scaled.AlphaType);
        Assert.Equal((4, 1), (scaled.Width, scaled.Height));
        Assert.Equal(0, scaled.GetPixel(0, 0).Alpha);
        Assert.Equal(255, scaled.GetPixel(3, 0).Alpha);
        var edge = scaled.GetPixel(1, 0);
        Assert.InRange(edge.Alpha, 1, 254);
        Assert.Equal((255, 0, 0), (edge.Red, edge.Green, edge.Blue));
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

        var picture = CutOutPost.Finish(photo, matte, out var coverage, out var reason, out var box);

        Assert.Null(picture);
        Assert.Equal("too small", reason);
        Assert.Equal(0.02, coverage, 1e-9);
        Assert.Equal(new SKRectI(100, 100, 140, 130), box);
    }

    [Fact]
    public void The_zoom_crop_is_the_box_with_room_round_it_kept_inside_the_photo()
    {
        using var photo = Photo(1200, 800);
        using var crop = CutOutChain.Zoom(photo, new SKRectI(400, 300, 600, 400));
        Assert.Equal((400, 256), (crop!.Width, crop.Height));

        using var corner = CutOutChain.Zoom(photo, new SKRectI(0, 0, 100, 100));
        Assert.Equal((256, 256), (corner!.Width, corner.Height));

        Assert.Null(CutOutChain.Zoom(photo, new SKRectI(100, 100, 1100, 700)));
    }

    [Fact]
    public void The_zoom_crop_holds_the_photos_own_pixels()
    {
        using var photo = Photo(1000, 800);
        photo.SetPixel(500, 400, SKColors.Blue);
        using var crop = CutOutChain.Zoom(photo, new SKRectI(480, 380, 520, 420));
        Assert.Equal(SKColors.Blue, crop!.GetPixel(crop.Width / 2, crop.Height / 2));
        Assert.Equal(Paint, crop.GetPixel(0, 0));
    }

    [Fact]
    public void The_chain_looks_closer_at_a_thing_too_small_to_pass()
    {
        using var photo = Photo(1600, 1200);
        var model = new FakeCutOut("isnet", available: true, succeeds: true, smallAbove: 800);
        var result = new CutOutChain(model).Cut(photo);
        using var picture = result.Picture;

        Assert.True(result.Succeeded);
        Assert.Equal("isnet, zoomed in", result.Model);
        Assert.Equal(2, model.Calls);
        Assert.Equal((400, 300), model.LastSize);
    }

    /// <summary>The octopus photo: the first look found only the white shell in its arms, and the closer look round the
    /// shell kept 11% of the crop. A whole thing fills about a quarter of a crop twice its box each way.</summary>
    [Theory]
    [InlineData(0.11, false)]
    [InlineData(0.149, false)]
    [InlineData(0.15, true)]
    [InlineData(0.18, true)]
    public void A_closer_look_must_keep_a_fair_share_of_its_crop(double coverage, bool kept)
    {
        using var photo = Photo(1600, 1200);
        var model = new FakeCutOut("isnet", available: true, succeeds: true, smallAbove: 800, coverage: coverage);
        var result = new CutOutChain(model).Cut(photo);
        using var picture = result.Picture;

        Assert.Equal(kept, result.Succeeded);
        Assert.Equal(kept ? null : "too small", result.Reason);
        Assert.Equal(2, model.Calls);
    }

    [Theory]
    [InlineData("isnet, zoomed in, covers 11% of the photo", true)]
    [InlineData("birefnet-lite, zoomed in, covers 14% of the photo", true)]
    [InlineData("isnet, zoomed in, covers 15% of the photo", false)]
    [InlineData("isnet, zoomed in, covers 26% of the picture", false)]
    [InlineData("isnet, covers 9% of the photo", false)]
    [InlineData("isnet failed: too small", false)]
    [InlineData(null, false)]
    public void A_sidecar_tells_whether_its_closer_look_kept_too_little(string? note, bool tooLittle) =>
        Assert.Equal(tooLittle, CutOutChain.ZoomedTooLittle(note));

    [Fact]
    public void The_chain_gives_up_when_the_closer_look_is_too_small_as_well()
    {
        using var photo = Photo(1600, 1200);
        var model = new FakeCutOut("isnet", available: true, succeeds: false, smallAbove: 800);
        var result = new CutOutChain(model).Cut(photo);

        Assert.False(result.Succeeded);
        Assert.Equal("too small", result.Reason);
        Assert.Equal(2, model.Calls);
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
    public void The_chain_passes_over_a_cut_out_closed_under_it()
    {
        using var photo = Photo(30, 20);
        var closed = new FakeCutOut("closed", available: true, succeeds: true, closed: true);
        var strong = new FakeCutOut("strong", available: true, succeeds: true);

        var result = new CutOutChain(closed, strong).Cut(photo);
        using var picture = result.Picture;

        Assert.True(result.Succeeded);
        Assert.Equal("strong", result.Model);
        Assert.Equal("model missing", new CutOutChain(closed).Cut(photo).Reason);
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

    /// <summary>A cut-out that is <paramref name="closed"/> still looks available (its file is on disk) but throws, as
    /// one disposed between the chain's check and its cut would.</summary>
    private sealed class FakeCutOut(string name, bool available, bool succeeds, bool closed = false, int smallAbove = int.MaxValue, double coverage = 0.5) : ICutOut
    {
        public int Calls { get; private set; }
        public (int Width, int Height) LastSize { get; private set; }
        public string Name => name;
        public bool IsAvailable => available;

        /// <summary>A photo wider than <paramref name="smallAbove"/> has its thing in a 200 by 150 box at the centre, too small to pass.</summary>
        public CutOutResult Cut(SKBitmap photo)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            Calls++;
            LastSize = (photo.Width, photo.Height);
            if (photo.Width > smallAbove)
            {
                var box = new SKRectI((photo.Width / 2) - 100, (photo.Height / 2) - 75, (photo.Width / 2) + 100, (photo.Height / 2) + 75);
                return new CutOutResult(null, name, 0.015, "too small", box);
            }
            return succeeds
                ? new CutOutResult(new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul)), name, coverage, null)
                : new CutOutResult(null, name, 0.01, "too small");
        }
    }
}
