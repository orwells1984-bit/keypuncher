using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

/// <summary>A picture a grown-up adds from their own files: taken as it is when it is a cut-out already, cut out
/// when it has a background, and always keeping the picture it takes the place of.</summary>
public sealed class HandPicturesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-hand-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 10, 18, 0, 0);
    private readonly PictureFolder _folder;
    private readonly FakeCutOut _cutOut = new();

    public HandPicturesTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] CutOutPng()
    {
        using var sample = SamplePicture.Draw();
        using var data = sample.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void A_picture_with_a_clear_background_goes_in_as_it_is()
    {
        var result = HandPictures.Add(_folder, _cutOut, "Sable antelope", RealCategory.Animal, CutOutPng(), ".png", _now);

        Assert.Equal((HandOutcome.Added, 1), (result.Outcome, result.Id));
        Assert.Equal(0, _cutOut.Calls);
        Assert.True(_folder.HasPicture("sable-antelope"));
        var sidecar = _folder.ReadSidecar("sable-antelope")!;
        Assert.Equal((PictureSource.Hand, 1, "Sable antelope", RealCategory.Animal, _now), (sidecar.Source, sidecar.Id, sidecar.Name, sidecar.Category, sidecar.MadeAt));
        Assert.Equal("put here by hand", sidecar.CreditLine);
        Assert.Equal(_folder.RawPath("sable-antelope", ".png"), _folder.FindRaw("sable-antelope"));
        using var picture = SKBitmap.Decode(_folder.PicturePath("sable-antelope"));
        Assert.Equal((480, 360), (picture.Width, picture.Height));
    }

    [Fact]
    public void A_big_cut_out_is_made_the_size_the_toy_draws()
    {
        using var big = new SKBitmap(new SKImageInfo(2048, 1024, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(big))
        {
            c.Clear(SKColors.Transparent);
            c.DrawCircle(1024, 512, 400, new SKPaint { Color = SKColors.SaddleBrown });
        }
        using var data = big.Encode(SKEncodedImageFormat.Png, 100);

        var result = HandPictures.Add(_folder, _cutOut, "ball", RealCategory.Object, data.ToArray(), ".png", _now);

        Assert.Equal(HandOutcome.Added, result.Outcome);
        using var picture = SKBitmap.Decode(_folder.PicturePath("ball"));
        Assert.Equal((ICutOut.MaxSide, ICutOut.MaxSide / 2), (picture.Width, picture.Height));
    }

    [Fact]
    public void A_photo_with_a_background_is_cut_out()
    {
        var result = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, TestPhotos.Jpeg(), ".JPG", _now);

        Assert.Equal(HandOutcome.Added, result.Outcome);
        Assert.Equal(1, _cutOut.Calls);
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.Equal(_folder.RawPath("sable-antelope", ".jpg"), _folder.FindRaw("sable-antelope"));
        Assert.StartsWith("fake", _folder.ReadSidecar("sable-antelope")!.CutOut);
    }

    [Fact]
    public void A_photo_waits_for_the_cut_out_models_and_changes_nothing()
    {
        _cutOut.IsAvailable = false;

        var result = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, TestPhotos.Jpeg(), ".jpg", _now);

        Assert.Equal(HandOutcome.NeedsCutOutModel, result.Outcome);
        Assert.False(_folder.HasPicture("sable-antelope"));
        Assert.Null(_folder.ReadSidecar("sable-antelope"));
        Assert.Null(_folder.FindRaw("sable-antelope"));
    }

    [Fact]
    public void A_photo_that_cannot_be_cut_out_says_why_and_changes_nothing()
    {
        _cutOut.FailWith = "fills the frame";

        var result = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, TestPhotos.Jpeg(), ".jpg", _now);

        Assert.Equal((HandOutcome.CutOutFailed, "fills the frame"), (result.Outcome, result.Reason));
        Assert.False(_folder.HasPicture("sable-antelope"));
    }

    [Fact]
    public void A_file_that_is_no_picture_is_turned_away()
    {
        var result = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, "not a picture"u8.ToArray(), ".png", _now);

        Assert.Equal(HandOutcome.NotAPicture, result.Outcome);
        Assert.False(_folder.HasPicture("sable-antelope"));
    }

    [Fact]
    public void The_picture_it_takes_the_place_of_is_kept_and_the_thing_is_no_longer_wanted()
    {
        HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, CutOutPng(), ".png", _now);
        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);

        var second = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, TestPhotos.Jpeg(), ".jpg", _now.AddMinutes(5));

        Assert.Equal((HandOutcome.Added, 2), (second.Outcome, second.Id));
        Assert.Equal(2, _folder.ReadSidecar("sable-antelope")!.Id);
        Assert.Equal(1, Assert.Single(_folder.Kept("sable-antelope")).Id);
        Assert.Equal(_folder.KeptPicturePath("sable-antelope", 1), _folder.PathOf("sable-antelope", 1));
        Assert.Equal(_folder.PicturePath("sable-antelope"), _folder.PathOf("sable-antelope", 2));
        Assert.Null(_folder.Wanted.Get("sable-antelope"));
        Assert.Equal(["sable-antelope"], _folder.KeptSlugs());
    }

    [Fact]
    public void A_hidden_picture_is_kept_too_and_the_new_one_is_in_play()
    {
        HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, CutOutPng(), ".png", _now);
        _folder.Hide("sable-antelope");

        var second = HandPictures.Add(_folder, _cutOut, "sable antelope", RealCategory.Animal, CutOutPng(), ".png", _now.AddMinutes(5));

        Assert.Equal(2, second.Id);
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.False(_folder.IsHidden("sable-antelope"));
        Assert.False(_folder.ReadSidecar("sable-antelope")!.Hidden);
        Assert.Single(_folder.Kept("sable-antelope"));
    }

    [Fact]
    public void Only_a_picture_with_some_clear_background_counts_as_cut_out()
    {
        using var clear = PhotoFetcher.Decode(CutOutPng())!;
        using var solid = PhotoFetcher.Decode(TestPhotos.Jpeg())!;
        Assert.True(HandPictures.IsCutOut(clear));
        Assert.False(HandPictures.IsCutOut(solid));
    }

    /// <summary>A photo saved with rounded corners, or a picture with a see-through hole in it, has some clear
    /// pixels but still has its background: it is cut out like any photo.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_photo_with_only_clear_corners_or_a_clear_hole_is_still_cut_out(bool corners)
    {
        using var photo = new SKBitmap(new SKImageInfo(800, 600, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(photo))
        using (var green = new SKPaint { Color = SKColors.ForestGreen })
        using (var hole = new SKPaint { BlendMode = SKBlendMode.Clear })
        {
            c.Clear(SKColors.Transparent);
            if (corners) c.DrawRoundRect(new SKRect(0, 0, 800, 600), 160, 160, green);
            else
            {
                c.DrawRect(new SKRect(0, 0, 800, 600), green);
                c.DrawCircle(400, 300, 200, hole);
            }
        }
        Assert.False(HandPictures.IsCutOut(photo));
        using var data = photo.Encode(SKEncodedImageFormat.Png, 100);

        var result = HandPictures.Add(_folder, _cutOut, "meadow", RealCategory.Plant, data.ToArray(), ".png", _now);

        Assert.Equal(HandOutcome.Added, result.Outcome);
        Assert.Equal(1, _cutOut.Calls);
    }
}
