using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

public sealed class PictureFolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-folder-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 6, 12, 0, 0);
    private readonly PictureFolder _folder;

    public PictureFolderTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static PictureSidecar Sidecar(string name, DateTime at) => new(Keypuncher.Core.Slugs.For(name), name, RealCategory.Animal, PictureSource.Hand, at);

    [Fact]
    public void The_paths_hang_off_the_root()
    {
        var root = Path.Combine(_dir, "pictures");
        Assert.Equal(Path.Combine(root, "sable-antelope.png"), _folder.PicturePath("sable-antelope"));
        Assert.Equal(Path.Combine(root, "sable-antelope.json"), _folder.SidecarPath("sable-antelope"));
        Assert.Equal(Path.Combine(root, "thumbs", "sable-antelope.png"), _folder.ThumbPath("sable-antelope"));
        Assert.Equal(Path.Combine(root, "hidden", "sable-antelope.png"), _folder.HiddenPath("sable-antelope"));
        Assert.Equal(Path.Combine(root, "raw", "sable-antelope.jpg"), _folder.RawPath("sable-antelope", ".jpg"));
    }

    [Fact]
    public void A_picture_is_written_whole_with_a_thumbnail_and_no_temporary_file_left()
    {
        using var cutOut = SamplePicture.Draw();
        _folder.WriteSidecar(Sidecar("sable antelope", _now));
        _folder.WritePicture("sable-antelope", cutOut);

        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.Empty(Directory.GetFiles(_folder.Root, "*.tmp", SearchOption.AllDirectories));
        using var thumb = SKBitmap.Decode(_folder.ThumbPath("sable-antelope"));
        Assert.Equal(PictureFolder.ThumbSide, Math.Max(thumb.Width, thumb.Height));
        using var picture = SKBitmap.Decode(_folder.PicturePath("sable-antelope"));
        Assert.Equal((480, 360), (picture.Width, picture.Height));
    }

    [Fact]
    public void A_sidecar_round_trips()
    {
        var sidecar = Sidecar("sable antelope", _now) with
        {
            Photo = new PhotoProvenance("Sable antelope", "https://en.wikipedia.org/wiki/Sable_antelope", "File:X.jpg", "https://u/x.jpg", "https://c/File:X.jpg",
                "Someone", null, "CC BY 4.0", null, null, "sha", 2000, 1300, "image/jpeg", "the lead"),
            Alternatives = ["File:Y.jpg"],
            CutOut = "isnet, covers 30% of the photo",
        };
        _folder.WriteSidecar(sidecar);
        var read = Assert.IsType<PictureSidecar>(_folder.ReadSidecar("sable-antelope"));
        Assert.Equal(sidecar, read with { Alternatives = sidecar.Alternatives });
        Assert.Equal(["File:Y.jpg"], read.Alternatives);
        Assert.Null(_folder.ReadSidecar("nothing"));
    }

    [Fact]
    public void Hiding_moves_the_picture_out_of_play_and_marks_the_sidecar_and_unhiding_puts_it_back()
    {
        using var cutOut = SamplePicture.Draw();
        _folder.WriteSidecar(Sidecar("sable antelope", _now));
        _folder.WritePicture("sable-antelope", cutOut);

        _folder.Hide("sable-antelope");
        Assert.False(_folder.HasPicture("sable-antelope"));
        Assert.True(_folder.IsHidden("sable-antelope"));
        Assert.True(_folder.ReadSidecar("sable-antelope")!.Hidden);

        _folder.Unhide("sable-antelope");
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.False(_folder.IsHidden("sable-antelope"));
        Assert.False(_folder.ReadSidecar("sable-antelope")!.Hidden);

        _folder.Hide("sable-antelope");
        _folder.WritePicture("sable-antelope", cutOut);
        Assert.False(_folder.IsHidden("sable-antelope"));
    }

    [Fact]
    public void All_lists_sidecars_newest_first_and_skips_the_other_json_files()
    {
        _folder.WriteSidecar(Sidecar("puffin", _now));
        _folder.WriteSidecar(Sidecar("oak tree", _now.AddHours(1)));
        _folder.Wanted.Add("tractor", RealCategory.Vehicle, _now);
        File.WriteAllText(Path.Combine(_folder.Root, "meetings.json"), "{}");

        Assert.Equal(["oak tree", "puffin"], _folder.All().Select(s => s.Name));
    }

    [Fact]
    public void Raw_photos_are_found_whatever_their_type()
    {
        Assert.Null(_folder.FindRaw("puffin"));
        _folder.WriteRaw("puffin", ".webp", [1, 2, 3]);
        Assert.Equal(_folder.RawPath("puffin", ".webp"), _folder.FindRaw("puffin"));
        Assert.Empty(Directory.GetFiles(_folder.RawFolder, "*.part"));
    }

    [Fact]
    public void The_wanted_list_is_kept_in_wanted_json_and_read_back()
    {
        var added = _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now, categoryGuessed: true);
        Assert.Equal(("sable-antelope", "sable antelope", RealCategory.Animal, _now, true), (added.Slug, added.Name, added.Category, added.Since, added.CategoryGuessed));
        Assert.Same(added, _folder.Wanted.Add("sable antelope", RealCategory.Plant, _now.AddDays(1)));

        _folder.Wanted.Bump("sable-antelope", "no photo found", _now.AddMinutes(5), exclude: "File:A.jpg");
        _folder.Wanted.Bump("sable-antelope", "offline", _now.AddMinutes(6), countTry: false, exclude: "File:A.jpg");
        _folder.Wanted.Bump("nothing", "ignored", _now);

        var again = new PictureFolder(_folder.Root).Wanted.Get("sable-antelope")!;
        Assert.Equal((1, "offline", _now.AddMinutes(6)), (again.Tries, again.Reason, again.LastTry));
        Assert.Equal(["File:A.jpg"], again.Exclude);
        Assert.True(again.CategoryGuessed);

        _folder.Wanted.Remove("sable-antelope");
        Assert.Empty(_folder.Wanted.All());
        Assert.Empty(new PictureFolder(_folder.Root).Wanted.All());
    }

    [Fact]
    public void The_wanted_list_comes_oldest_first()
    {
        _folder.Wanted.Add("b", RealCategory.Object, _now.AddHours(1));
        _folder.Wanted.Add("a", RealCategory.Object, _now);
        Assert.Equal(["a", "b"], _folder.Wanted.All().Select(w => w.Name));
    }

    [Fact]
    public void A_broken_wanted_file_is_treated_as_empty()
    {
        Directory.CreateDirectory(_folder.Root);
        File.WriteAllText(Path.Combine(_folder.Root, "wanted.json"), "{ not json");
        Assert.Empty(_folder.Wanted.All());
    }

    [Fact]
    public void The_credit_line_names_where_the_photo_came_from()
    {
        var photo = new PhotoProvenance("Sable antelope", "", "File:X.jpg", "", "https://commons.wikimedia.org/wiki/File:X.jpg", "Someone", null, "CC BY 4.0", null, null, null, 1, 1, "image/jpeg", "");
        Assert.Equal("Someone, CC BY 4.0, via Wikimedia Commons", (Sidecar("x", _now) with { Source = PictureSource.Photo, Photo = photo }).CreditLine);
        var inat = photo with { DescriptionUrl = "https://www.inaturalist.org/photos/1", Artist = null, LicenseShortName = "CC BY NC" };
        Assert.Equal("CC BY NC, via iNaturalist", (Sidecar("x", _now) with { Source = PictureSource.Photo, Photo = inat }).CreditLine);
        Assert.Equal("put here by hand", Sidecar("x", _now).CreditLine);
    }
}
