using Keypuncher.Director;
using Keypuncher.Pictures;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

public sealed class PhotoFetcherTests : IDisposable
{
    private const string Download = "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Sable_antelope_male.jpg/1280px-Sable_antelope_male.jpg";
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-fetcher-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 6, 12, 0, 0);
    private readonly PictureFolder _folder;
    private readonly FakeHandler _web = new FakeHandler().Bytes(Download, TestPhotos.Jpeg(), "image/jpeg");
    private readonly FakePhotoFinder _finder = new() { Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope male.jpg", Download), ["took it"]) };
    private readonly FakeCutOut _cutOut = new();
    private readonly List<string> _log = [];

    public PhotoFetcherTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PhotoFetcher Fetcher(ICutOut? cutOut = null) => new(_folder, _finder, cutOut ?? _cutOut, new HttpClient(_web), () => _now, _log.Add);

    [Fact]
    public async Task A_found_photo_is_downloaded_cut_out_and_put_in_play_with_its_sidecar_and_thumbnail()
    {
        var outcome = await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);

        Assert.Equal(FetchOutcome.Pictured, outcome);
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.True(File.Exists(_folder.ThumbPath("sable-antelope")));
        Assert.True(File.Exists(_folder.RawPath("sable-antelope", ".jpg")));
        Assert.Empty(Directory.GetFiles(_folder.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_folder.Root, "*.part", SearchOption.AllDirectories));
        Assert.Empty(_folder.Wanted.All());
        var sidecar = Assert.IsType<PictureSidecar>(_folder.ReadSidecar("sable-antelope"));
        Assert.Equal(("sable antelope", RealCategory.Animal, PictureSource.Photo, _now), (sidecar.Name, sidecar.Category, sidecar.Source, sidecar.MadeAt));
        Assert.Equal("File:Sable antelope male.jpg", sidecar.Photo!.FileTitle);
        Assert.Equal("Charles J. Sharp, CC BY-SA 4.0, via Wikimedia Commons", sidecar.CreditLine);
        Assert.Equal("fake, covers 40% of the photo", sidecar.CutOut);
        Assert.Equal(1, _cutOut.Calls);
        Assert.Single(_web.Requests);
        using var picture = SKBitmap.Decode(_folder.PicturePath("sable-antelope"));
        Assert.Equal((480, 360), (picture.Width, picture.Height));
        Assert.Contains(_log, l => l.Contains("picture in play"));
    }

    [Fact]
    public async Task Without_the_cut_out_model_the_photo_is_kept_and_the_thing_stays_wanted_without_a_try_counted()
    {
        _cutOut.IsAvailable = false;
        var outcome = await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);

        Assert.Equal(FetchOutcome.RawOnly, outcome);
        Assert.False(_folder.HasPicture("sable-antelope"));
        Assert.Equal(_folder.RawPath("sable-antelope", ".jpg"), _folder.FindRaw("sable-antelope"));
        var wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal((PhotoFetcher.ModelMissing, 0, _now), (wanted.Reason, wanted.Tries, wanted.LastTry));
        Assert.NotNull(_folder.ReadSidecar("sable-antelope"));
        Assert.Null(_folder.ReadSidecar("sable-antelope")!.CutOut);
    }

    [Fact]
    public async Task A_kept_photo_is_cut_when_the_model_arrives_without_going_online()
    {
        _cutOut.IsAvailable = false;
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        _cutOut.IsAvailable = true;
        _web.Requests.Clear();
        _finder.Asked.Clear();

        var outcome = await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);

        Assert.Equal(FetchOutcome.Pictured, outcome);
        Assert.Empty(_web.Requests);
        Assert.Empty(_finder.Asked);
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.Empty(_folder.Wanted.All());
    }

    [Fact]
    public async Task Recut_makes_the_cut_out_again_from_the_kept_photo()
    {
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        File.Delete(_folder.PicturePath("sable-antelope"));

        Assert.Equal(FetchOutcome.Pictured, Fetcher().Recut("sable-antelope"));
        Assert.True(_folder.HasPicture("sable-antelope"));
        Assert.Equal(2, _cutOut.Calls);
        Assert.Equal(FetchOutcome.NothingSuitable, Fetcher().Recut("nothing-here"));
    }

    [Fact]
    public async Task A_thing_with_a_picture_already_is_left_alone_and_its_stale_wanted_entry_dropped()
    {
        TestPhotos.WritePng(_folder.PicturePath("sable-antelope"));
        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);

        Assert.Equal(FetchOutcome.AlreadyHave, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Empty(_finder.Asked);
        Assert.Empty(_folder.Wanted.All());
    }

    [Fact]
    public async Task A_hidden_picture_counts_as_had()
    {
        TestPhotos.WritePng(_folder.HiddenPath("sable-antelope"));
        Assert.Equal(FetchOutcome.AlreadyHave, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Empty(_finder.Asked);
    }

    [Fact]
    public async Task Try_another_passes_the_file_turned_down_to_the_finder_and_keeps_the_old_picture_until_the_new_one_lands()
    {
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        var before = File.ReadAllBytes(_folder.PicturePath("sable-antelope"));
        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);
        _folder.Wanted.Bump("sable-antelope", "try another", _now, countTry: false, exclude: "File:Sable antelope male.jpg");
        _finder.Answer = PhotoSearch.Nothing("no photo found", []);

        Assert.Equal(FetchOutcome.NothingSuitable, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(["File:Sable antelope male.jpg"], _finder.Asked[^1].Exclude);
        Assert.Equal(before, File.ReadAllBytes(_folder.PicturePath("sable-antelope")));

        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope female.jpg", Download), []);
        Assert.Equal(FetchOutcome.Pictured, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal("File:Sable antelope female.jpg", _folder.ReadSidecar("sable-antelope")!.Photo!.FileTitle);
        Assert.Equal(["File:Sable antelope male.jpg"], _folder.ReadSidecar("sable-antelope")!.Alternatives);
        Assert.Empty(_folder.Wanted.All());
    }

    [Fact]
    public async Task Try_another_leaves_the_old_pictures_photo_and_story_alone_until_a_new_cut_out_lands()
    {
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        var picture = File.ReadAllBytes(_folder.PicturePath("sable-antelope"));
        var raw = File.ReadAllBytes(_folder.FindRaw("sable-antelope")!);
        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);
        _folder.Wanted.Bump("sable-antelope", "another was asked for", _now, countTry: false, exclude: "File:Sable antelope male.jpg");
        const string second = "https://upload.wikimedia.org/wikipedia/commons/thumb/4/4b/Sable_antelope_female.png/1280px-Sable_antelope_female.png";
        _web.Bytes(second, TestPhotos.Jpeg(300, 300), "image/png");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope female.png", second), []);
        _cutOut.FailWith = "touches three edges";

        Assert.Equal(FetchOutcome.RawOnly, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(picture, File.ReadAllBytes(_folder.PicturePath("sable-antelope")));
        Assert.Equal(_folder.RawPath("sable-antelope", ".jpg"), _folder.FindRaw("sable-antelope"));
        Assert.Equal(raw, File.ReadAllBytes(_folder.FindRaw("sable-antelope")!));
        var sidecar = _folder.ReadSidecar("sable-antelope")!;
        Assert.Equal("File:Sable antelope male.jpg", sidecar.Photo!.FileTitle);
        Assert.Equal("fake, covers 40% of the photo", sidecar.CutOut);
        Assert.Equal(["File:Sable antelope male.jpg", "File:Sable antelope female.png"], _folder.Wanted.Get("sable-antelope")!.Exclude);

        _cutOut.FailWith = null;
        Assert.Equal(FetchOutcome.Pictured, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(["File:Sable antelope male.jpg", "File:Sable antelope female.png"], _finder.Asked[^1].Exclude);
    }

    [Fact]
    public async Task Try_another_twice_keeps_every_photo_turned_down_before_out_of_the_running()
    {
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);

        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);
        _folder.Wanted.Bump("sable-antelope", "another was asked for", _now, countTry: false, exclude: "File:Sable antelope male.jpg");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope female.jpg", Download), []);
        Assert.Equal(FetchOutcome.Pictured, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(["File:Sable antelope male.jpg"], _finder.Asked[^1].Exclude);

        _folder.Wanted.Add("sable antelope", RealCategory.Animal, _now);
        _folder.Wanted.Bump("sable-antelope", "another was asked for", _now, countTry: false, exclude: "File:Sable antelope female.jpg");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope calf.jpg", Download), []);
        Assert.Equal(FetchOutcome.Pictured, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(["File:Sable antelope female.jpg", "File:Sable antelope male.jpg"], _finder.Asked[^1].Exclude.Order());
        Assert.Equal(["File:Sable antelope female.jpg", "File:Sable antelope male.jpg"], _folder.ReadSidecar("sable-antelope")!.Alternatives.Order());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void A_photo_stored_on_its_side_is_decoded_the_right_way_up(int orientation)
    {
        using var photo = PhotoFetcher.Decode(TestPhotos.JpegStoredSideways(orientation))!;

        Assert.Equal((20, 40), (photo.Width, photo.Height));
        // Turned clockwise the stored left half (red) is at the top; turned anticlockwise it is at the bottom.
        var top = photo.GetPixel(10, 5);
        var bottom = photo.GetPixel(10, 35);
        var (red, blue) = orientation == 6 ? (top, bottom) : (bottom, top);
        Assert.True(red.Red > 200 && red.Blue < 60, $"expected red, got {red}");
        Assert.True(blue.Blue > 200 && blue.Red < 60, $"expected blue, got {blue}");
    }

    [Fact]
    public void A_photo_stored_upright_is_left_as_it_is()
    {
        using var photo = PhotoFetcher.Decode(TestPhotos.Jpeg())!;
        Assert.Equal((480, 360), (photo.Width, photo.Height));
    }

    [Fact]
    public async Task Nothing_found_counts_a_try_and_offline_does_not()
    {
        _finder.Answer = PhotoSearch.Nothing("no article", ["no article"]);
        Assert.Equal(FetchOutcome.NothingSuitable, await Fetcher().FetchAsync("snorkelwump", RealCategory.Object));
        var wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal(("snorkelwump", RealCategory.Object, 1, "no article"), (wanted.Name, wanted.Category, wanted.Tries, wanted.Reason));

        _finder.Answer = PhotoSearch.NoNetwork("connection refused", []);
        Assert.Equal(FetchOutcome.Offline, await Fetcher().FetchAsync("snorkelwump", RealCategory.Object));
        wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal((1, "offline"), (wanted.Tries, wanted.Reason));
        Assert.Empty(_web.Requests);
    }

    [Fact]
    public async Task A_failed_matte_keeps_the_photo_and_turns_the_file_down()
    {
        _cutOut.FailWith = "touches three edges";
        var outcome = await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);

        Assert.Equal(FetchOutcome.RawOnly, outcome);
        Assert.False(_folder.HasPicture("sable-antelope"));
        Assert.NotNull(_folder.FindRaw("sable-antelope"));
        var wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal("cut-out failed: touches three edges", wanted.Reason);
        Assert.Equal(1, wanted.Tries);
        Assert.Equal(["File:Sable antelope male.jpg"], wanted.Exclude);
        Assert.Equal("fake failed: touches three edges", _folder.ReadSidecar("sable-antelope")!.CutOut);
    }

    [Fact]
    public async Task A_download_that_is_not_a_picture_is_turned_down()
    {
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Broken.jpg", "https://upload.wikimedia.org/broken.jpg"), []);
        _web.Bytes("broken.jpg", "not a picture"u8.ToArray(), "image/jpeg");

        Assert.Equal(FetchOutcome.NothingSuitable, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        var wanted = Assert.Single(_folder.Wanted.All());
        Assert.Equal(["File:Broken.jpg"], wanted.Exclude);
        Assert.Null(_folder.FindRaw("sable-antelope"));
        Assert.Null(_folder.ReadSidecar("sable-antelope"));
    }

    [Fact]
    public async Task A_download_that_is_not_there_is_turned_down_and_a_broken_connection_is_offline()
    {
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Gone.jpg", "https://upload.wikimedia.org/gone.jpg"), []);
        Assert.Equal(FetchOutcome.NothingSuitable, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal(["File:Gone.jpg"], _folder.Wanted.Get("sable-antelope")!.Exclude);

        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Far.jpg", "https://upload.wikimedia.org/far.jpg"), []);
        _web.Throws("far.jpg", new HttpRequestException("connection reset"));
        Assert.Equal(FetchOutcome.Offline, await Fetcher().FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal((1, "offline"), (_folder.Wanted.Get("sable-antelope")!.Tries, _folder.Wanted.Get("sable-antelope")!.Reason));
    }

    [Fact]
    public async Task A_doubtful_photo_says_so_in_its_sidecar()
    {
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Sable antelope male.jpg", Download, "a drawing or painting rather than a photo"), []);
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        Assert.StartsWith("fake, covers", _folder.ReadSidecar("sable-antelope")!.CutOut);
        _cutOut.IsAvailable = false;
        File.Delete(_folder.PicturePath("sable-antelope"));
        File.Delete(_folder.SidecarPath("sable-antelope"));
        await Fetcher().FetchAsync("sable antelope", RealCategory.Animal);
        Assert.Equal("taken with doubt: a drawing or painting rather than a photo", _folder.ReadSidecar("sable-antelope")!.CutOut);
    }

    [Fact]
    public async Task The_chain_asks_the_next_cut_out_when_the_first_fails()
    {
        var first = new FakeCutOut { FailWith = "too small" };
        var second = new FakeCutOut();
        var missing = new FakeCutOut { IsAvailable = false };
        var chain = new CutOutChain(missing, first, second);

        Assert.True(chain.IsAvailable);
        Assert.Equal(FetchOutcome.Pictured, await Fetcher(chain).FetchAsync("sable antelope", RealCategory.Animal));
        Assert.Equal((0, 1, 1), (missing.Calls, first.Calls, second.Calls));
        Assert.False(new CutOutChain(missing).IsAvailable);
        Assert.Equal("none", new CutOutChain(missing).Name);
    }

    [Theory]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData(null, ".jpg")]
    public void The_raw_photo_is_kept_under_its_own_type(string? mime, string extension) => Assert.Equal(extension, PhotoFetcher.ExtensionFor(mime));
}
