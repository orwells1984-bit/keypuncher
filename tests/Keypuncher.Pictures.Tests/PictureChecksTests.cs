using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

/// <summary>What a vision check does to a picture: a pass leaves it be; a fail takes it out of play and asks for
/// another photo, which the next fetch brings; a grown-up can put it back.</summary>
public sealed class PictureChecksTests : IDisposable
{
    private const string Shoal = "https://upload.wikimedia.org/maldives-surgeonfish.jpg";
    private const string Trout = "https://upload.wikimedia.org/brown-trout.jpg";
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-checks-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 8, 16, 0, 0);
    private readonly PictureFolder _folder;
    private readonly FakeHandler _web = new FakeHandler().Bytes(Shoal, TestPhotos.Jpeg(), "image/jpeg").Bytes(Trout, TestPhotos.Jpeg(), "image/jpeg");
    private readonly FakePhotoFinder _finder = new() { Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Maldives Surgeonfish.jpg", Shoal), []) };
    private readonly FakeCutOut _cutOut = new();
    private readonly Checker _checker = new();
    private readonly List<string> _log = [];

    public PictureChecksTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PhotoFetcher Fetcher() => new(_folder, _finder, _cutOut, new HttpClient(_web), () => _now, _log.Add);
    private PictureChecks Checks() => new(_folder, _checker, () => _now, _log.Add);

    [Fact]
    public async Task A_picture_that_passes_stays_in_play_with_its_verdict()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        var check = await Checks().CheckAsync("fish");

        Assert.True(check!.Passed);
        Assert.True(_folder.HasPicture("fish"));
        Assert.True(_folder.ReadSidecar("fish")!.Check!.Passed);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Empty(Checks().Unchecked());
    }

    [Fact]
    public async Task A_picture_that_fails_is_taken_out_of_play_and_another_photo_is_wanted()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        _checker.Saw = "a school of about thirty blue surgeonfish [counted 30]";

        var check = await Checks().CheckAsync("fish");

        Assert.False(check!.Passed);
        Assert.False(_folder.HasPicture("fish"));
        Assert.True(_folder.IsHidden("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.True(sidecar.Hidden);
        Assert.False(sidecar.Check!.Passed);
        var wanted = _folder.Wanted.Get("fish")!;
        Assert.Equal(PictureChecks.FailedCheck, wanted.Reason);
        Assert.Equal(["File:Maldives Surgeonfish.jpg"], wanted.Exclude);
        Assert.Contains(_log, l => l.Contains("looks WRONG") && l.Contains("taken out of play"));
    }

    [Fact]
    public async Task A_picture_a_grown_up_chose_for_a_rung_stays_in_play_when_it_fails_and_keeps_its_verdict()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        var card = new ThingCard("fish", "fish");
        _folder.Cards.Save(card with { Rungs = card.Rungs.With(PictureStage.Photo, RungChoice.OfPicture(1)) }, _now);
        _checker.Passed = false;

        var check = await Checks().CheckAsync("fish");

        Assert.False(check!.Passed);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.IsHidden("fish"));
        Assert.False(_folder.ReadSidecar("fish")!.Check!.Passed);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Contains(_log, l => l.Contains("looks WRONG") && l.Contains("a grown-up chose this picture"));
    }

    [Fact]
    public async Task A_picture_a_grown_up_added_from_their_own_files_stays_in_play_when_it_fails()
    {
        using (var sample = Keypuncher.Rendering.SamplePicture.Draw())
        using (var data = sample.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            Assert.Equal(HandOutcome.Added, HandPictures.Add(_folder, _cutOut, "fish", RealCategory.Animal, data.ToArray(), ".png", _now).Outcome);
        _checker.Passed = false;

        var check = await Checks().CheckAsync("fish");

        Assert.False(check!.Passed);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.ReadSidecar("fish")!.Check!.Passed);
        Assert.Null(_folder.Wanted.Get("fish"));
    }

    [Fact]
    public async Task A_choice_of_another_picture_does_not_spare_the_one_in_play()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        var card = new ThingCard("fish", "fish");
        _folder.Cards.Save(card with { Rungs = card.Rungs.With(PictureStage.Flat, RungChoice.OfPicture(2)) }, _now);
        _checker.Passed = false;

        await Checks().CheckAsync("fish");

        Assert.True(_folder.IsHidden("fish"));
        Assert.NotNull(_folder.Wanted.Get("fish"));
    }

    [Fact]
    public async Task A_failed_picture_is_kept_under_its_number_when_another_takes_its_place()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);

        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        var kept = Assert.Single(_folder.Kept("fish"));
        Assert.Equal((1, "File:Maldives Surgeonfish.jpg", false), (kept.Id, kept.Photo!.FileTitle, kept.Check!.Passed));
        Assert.False(kept.Hidden);
        Assert.Equal(2, _folder.ReadSidecar("fish")!.Id);
    }

    [Fact]
    public async Task The_next_fetch_puts_another_photo_in_play_and_it_waits_for_its_own_check()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);

        var outcome = await Fetcher().FetchAsync("fish", RealCategory.Animal);

        Assert.Equal(FetchOutcome.Pictured, outcome);
        Assert.Contains("File:Maldives Surgeonfish.jpg", _finder.Asked[^1].Exclude);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.IsHidden("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.Equal("File:Brown trout.jpg", sidecar.Photo!.FileTitle);
        Assert.Null(sidecar.Check);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Equal("fish", Assert.Single(Checks().Unchecked()).Slug);
    }

    [Fact]
    public async Task A_grown_up_putting_a_failed_picture_back_stops_the_search_for_another()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");

        _folder.Unhide("fish");

        Assert.True(_folder.HasPicture("fish"));
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Equal(FetchOutcome.AlreadyHave, await Fetcher().FetchAsync("fish", RealCategory.Animal));
        // Its verdict stays, so the toy does not check it again after the next goodnight.
        Assert.Empty(Checks().Unchecked());
    }

    [Fact]
    public async Task A_picture_a_grown_up_hid_is_left_hidden_by_the_fetcher()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _folder.Hide("fish");

        Assert.Equal(FetchOutcome.AlreadyHave, await Fetcher().FetchAsync("fish", RealCategory.Animal));
        Assert.True(_folder.IsHidden("fish"));
        Assert.Empty(Checks().Unchecked());
    }

    [Fact]
    public async Task The_checker_is_shown_the_photo_the_cut_out_came_from()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        await Checks().CheckAsync("fish");

        var photo = Assert.Single(_checker.Photos);
        Assert.NotNull(photo);
        using var decoded = SkiaSharp.SKBitmap.Decode(photo);
        Assert.NotNull(decoded);
        Assert.True(Math.Max(decoded.Width, decoded.Height) <= PictureChecks.MaxPhotoSide);
    }

    [Fact]
    public async Task Without_its_photo_a_picture_is_checked_alone()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        File.Delete(_folder.FindRaw("fish")!);

        Assert.True((await Checks().CheckAsync("fish"))!.Passed);
        Assert.Null(Assert.Single(_checker.Photos));
    }

    /// <summary>The octopus and thylacine pictures were cut out from closer looks that kept 11% and 8% of the crop,
    /// before the cut-out turned those down; a check takes them out without asking the model.</summary>
    [Fact]
    public async Task A_picture_whose_closer_look_kept_too_little_is_taken_out_without_asking()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _folder.WriteSidecar(_folder.ReadSidecar("fish")! with { CutOut = "isnet, zoomed in, covers 11% of the photo" });

        var check = await Checks().CheckAsync("fish");

        Assert.False(check!.Passed);
        Assert.Equal(PictureChecks.CutOutRule, check.Model);
        Assert.Empty(_checker.Asked);
        Assert.True(_folder.IsHidden("fish"));
        Assert.Equal(["File:Maldives Surgeonfish.jpg"], _folder.Wanted.Get("fish")!.Exclude);
    }

    [Fact]
    public async Task A_checker_that_does_not_answer_changes_nothing()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Answer = false;

        Assert.Null(await Checks().CheckAsync("fish"));
        Assert.True(_folder.HasPicture("fish"));
        Assert.Null(_folder.ReadSidecar("fish")!.Check);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Null(await Checks().CheckAsync("no-such-thing"));
    }

    /// <summary>The check takes its time; a fetch or a grown-up's own picture can land while it looks.</summary>
    [Fact]
    public async Task A_verdict_on_a_picture_that_was_replaced_meanwhile_is_dropped()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        _checker.OnCheck = () =>
        {
            using var sample = Keypuncher.Rendering.SamplePicture.Draw();
            using var data = sample.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            HandPictures.Add(_folder, _cutOut, "fish", RealCategory.Animal, data.ToArray(), ".png", _now);
        };

        var check = await Checks().CheckAsync("fish");

        Assert.Null(check);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.IsHidden("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.Equal((2, PictureSource.Hand), (sidecar.Id, sidecar.Source));
        Assert.Null(sidecar.Check);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Contains(_log, l => l.Contains("the verdict is dropped"));
    }

    [Fact]
    public async Task The_checker_is_asked_about_the_real_name_of_a_nursery_name()
    {
        await Fetcher().FetchAsync("rex", RealCategory.Animal);
        await Checks().CheckAsync("rex");
        Assert.Equal(("Tyrannosaurus", RealCategory.ExtinctAnimal), _checker.Asked.Single());
    }

    [Fact]
    public async Task A_picture_checked_under_older_rules_is_checked_again()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        await Checks().CheckAsync("fish");
        Assert.Equal(PictureChecks.Rules, _folder.ReadSidecar("fish")!.Check!.Rules);
        Assert.Empty(Checks().Unchecked());

        // A verdict from before the rules were numbered, as on the family desktop: a school of fish passed as one.
        var sidecar = _folder.ReadSidecar("fish")!;
        _folder.WriteSidecar(sidecar with { Check = sidecar.Check! with { Rules = 0 } });

        Assert.Equal("fish", Assert.Single(Checks().Unchecked()).Slug);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        Assert.False(_folder.HasPicture("fish"));
        Assert.True(_folder.IsHidden("fish"));
    }

    [Fact]
    public async Task A_picture_a_grown_up_put_back_stays_in_play_when_checked_again()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _folder.Unhide("fish");
        var sidecar = _folder.ReadSidecar("fish")!;
        _folder.WriteSidecar(sidecar with { Check = sidecar.Check! with { Rules = 0 } });

        await Checks().CheckAsync("fish");

        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.ReadSidecar("fish")!.Check!.Passed);
    }

    [Fact]
    public async Task Pictures_never_checked_come_before_those_checked_under_older_rules()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        await Checks().CheckAsync("fish");
        var sidecar = _folder.ReadSidecar("fish")!;
        _folder.WriteSidecar(sidecar with { Check = sidecar.Check! with { Rules = 0 } });
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);
        await Fetcher().FetchAsync("trout", RealCategory.Animal);

        Assert.Equal(["trout", "fish"], Checks().Unchecked().Select(p => p.Slug));
    }

    private sealed class Checker : IVisionChecker
    {
        public string Model => "gemma3:4b";
        public bool Passed { get; set; } = true;
        public bool Answer { get; set; } = true;
        public string Saw { get; set; } = "one blue fish";
        public List<(string Name, RealCategory Category)> Asked { get; } = [];
        public List<byte[]?> Photos { get; } = [];
        /// <summary>Something that happens to the folder while the checker is looking.</summary>
        public Action? OnCheck { get; set; }

        public Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, byte[]? photo = null, CancellationToken ct = default)
        {
            Asked.Add((name, category));
            Photos.Add(photo);
            OnCheck?.Invoke();
            return Task.FromResult(Answer ? new VisionCheck(Model, Passed, Passed ? 1 : 0, Saw, new DateTime(2026, 11, 8, 16, 1, 0)) : null);
        }
    }
}
