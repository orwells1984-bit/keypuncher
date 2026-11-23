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

    /// <summary>On 2026-11-22 the coconut and the coconut palm both showed "Coconut 4.jpg".</summary>
    [Fact]
    public async Task A_picture_whose_photo_another_thing_shows_fails_for_the_thing_the_file_fits_less()
    {
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Coconut 4.jpg", Shoal), []);
        await Fetcher().FetchAsync("coconut", RealCategory.Plant);
        // The fetch now tells the search the photo is taken; this finder hands it over anyway, as before that rule.
        await Fetcher().FetchAsync("coconut palm", RealCategory.Plant);
        Assert.Contains("File:Coconut 4.jpg", _finder.Asked[^1].Exclude);
        Assert.Empty(_folder.ReadSidecar("coconut-palm")!.Alternatives);

        var palm = await Checks().CheckAsync("coconut-palm");
        var coconut = await Checks().CheckAsync("coconut");

        Assert.False(palm!.Passed);
        Assert.Equal(PictureChecks.SharedPhotoRule, palm.Model);
        Assert.Contains("the same photo as coconut", palm.Saw);
        Assert.True(_folder.IsHidden("coconut-palm"));
        Assert.Equal(["File:Coconut 4.jpg"], _folder.Wanted.Get("coconut-palm")!.Exclude);
        Assert.True(coconut!.Passed);
        Assert.True(_folder.HasPicture("coconut"));
    }

    [Theory]
    [InlineData("coconut", "File:Coconut 4.jpg", 1.0)]
    [InlineData("coconut palm", "File:Coconut 4.jpg", 0.5)]
    [InlineData("mango", "File:Coconut 4.jpg", 0.0)]
    public void A_file_title_fits_the_name_it_spells_out(string name, string title, double claim) =>
        Assert.Equal(claim, PictureChecks.Claim(name, title));

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

    /// <summary>On 2026-11-15 the photos fetched in place of three failed ones went straight into play: fossil skulls
    /// for a parasaurolophus. Now the replacement waits, out of play, for the checker.</summary>
    [Fact]
    public async Task The_next_fetch_brings_another_photo_which_waits_out_of_play_for_its_own_check()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);

        var outcome = await Fetcher().FetchAsync("fish", RealCategory.Animal);

        Assert.Equal(FetchOutcome.Pictured, outcome);
        Assert.Contains("File:Maldives Surgeonfish.jpg", _finder.Asked[^1].Exclude);
        Assert.False(_folder.HasPicture("fish"));
        Assert.True(_folder.IsHidden("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.Equal("File:Brown trout.jpg", sidecar.Photo!.FileTitle);
        Assert.True(sidecar.AwaitsCheck);
        Assert.Null(sidecar.Check);
        Assert.Null(_folder.Wanted.Get("fish"));
        Assert.Equal("fish", Assert.Single(Checks().Unchecked()).Slug);
        // Nothing fetches yet another while it waits.
        Assert.Equal(FetchOutcome.AlreadyHave, await Fetcher().FetchAsync("fish", RealCategory.Animal));
        Assert.Contains(_log, l => l.Contains("waits for the picture check"));
    }

    [Fact]
    public async Task A_waiting_photo_the_checker_passes_goes_into_play()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = true;

        var check = await Checks().CheckAsync("fish");

        Assert.True(check!.Passed);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.IsHidden("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.Equal((false, false, false), (sidecar.Hidden, sidecar.AwaitsCheck, sidecar.ShownByGrownUp));
        Assert.Empty(Checks().Unchecked());
        Assert.Contains(_log, l => l.Contains("it goes into play"));
    }

    [Fact]
    public async Task A_waiting_photo_the_checker_fails_never_enters_play_and_another_is_wanted()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);
        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        var check = await Checks().CheckAsync("fish");

        Assert.False(check!.Passed);
        Assert.False(_folder.HasPicture("fish"));
        Assert.True(_folder.IsHidden("fish"));
        Assert.False(_folder.ReadSidecar("fish")!.AwaitsCheck);
        Assert.Equal(["File:Brown trout.jpg"], _folder.Wanted.Get("fish")!.Exclude);
        Assert.Empty(Checks().Unchecked());
    }

    [Fact]
    public async Task A_grown_up_can_put_a_waiting_photo_in_play_without_the_checker()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);
        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        _folder.Unhide("fish");

        Assert.True(_folder.HasPicture("fish"));
        var sidecar = _folder.ReadSidecar("fish")!;
        Assert.Equal((false, true), (sidecar.AwaitsCheck, sidecar.ShownByGrownUp));
    }

    [Fact]
    public async Task Another_photo_a_grown_up_asked_for_goes_into_play_at_once()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        // Try another on the grown-ups' page: the photo that comes is theirs to judge.
        _folder.Wanted.Bump("fish", PhotoFetcher.AskedForAnother, _now, countTry: false);
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);

        await Fetcher().FetchAsync("fish", RealCategory.Animal);

        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.ReadSidecar("fish")!.AwaitsCheck);
    }

    [Fact]
    public async Task Waiting_photos_are_checked_first()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _checker.Passed = false;
        await Checks().CheckAsync("fish");
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Brown trout.jpg", Trout), []);
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        await Fetcher().FetchAsync("trout", RealCategory.Animal);

        Assert.Equal(["fish", "trout"], Checks().Unchecked().Select(p => p.Slug));
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

    /// <summary>The second manuscript page shown as Steller's sea cow was fetched before folios were refused and then
    /// passed by the vision model: the name rules of the day are run over what is already in play.</summary>
    [Fact]
    public async Task A_photo_whose_file_name_would_be_refused_today_is_swept_out_without_asking_the_checker()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Adriaen Coenen's Visboeck - KB 78 E 54 - folio 395v.jpg", Trout), []);
        await Fetcher().FetchAsync("trout", RealCategory.Animal);
        await Checks().CheckAsync("trout");   // passed by the model, as on the desktop
        Assert.True(_folder.HasPicture("trout"));
        var asked = _checker.Asked.Count;

        var taken = Checks().SweepFileNames();

        Assert.Equal(["trout"], taken);
        Assert.Equal(asked, _checker.Asked.Count);
        Assert.True(_folder.HasPicture("fish"));
        Assert.False(_folder.HasPicture("trout"));
        Assert.True(_folder.IsHidden("trout"));
        var sidecar = _folder.ReadSidecar("trout")!;
        Assert.Equal((PictureChecks.FileNameRule, false), (sidecar.Check!.Model, sidecar.Check.Passed));
        Assert.Equal(["File:Adriaen Coenen's Visboeck - KB 78 E 54 - folio 395v.jpg"], _folder.Wanted.Get("trout")!.Exclude);
        Assert.Empty(Checks().SweepFileNames());
    }

    [Fact]
    public async Task A_swept_photo_a_grown_up_put_back_stays()
    {
        _finder.Answer = PhotoSearch.Found(FakePhotoFinder.Photo("File:Kangaroo and joey03.jpg", Trout), []);
        await Fetcher().FetchAsync("kangaroo", RealCategory.Animal);
        Checks().SweepFileNames();
        Assert.True(_folder.IsHidden("kangaroo"));
        _folder.Unhide("kangaroo");

        Assert.Empty(Checks().SweepFileNames());
        Assert.True(_folder.HasPicture("kangaroo"));
    }

    [Fact]
    public async Task Only_an_extinct_animals_verdict_goes_stale_under_the_rules_that_changed_only_how_they_are_read()
    {
        await Fetcher().FetchAsync("fish", RealCategory.Animal);
        await Checks().CheckAsync("fish");
        var sidecar = _folder.ReadSidecar("fish")!;
        var old = sidecar.Check! with { Rules = PictureChecks.LivingRules };
        Assert.False(PictureChecks.Stale(old, RealCategory.Animal));
        Assert.True(PictureChecks.Stale(old, RealCategory.ExtinctAnimal));
        Assert.True(PictureChecks.Stale(old with { Rules = PictureChecks.LivingRules - 1 }, RealCategory.Animal));
        Assert.True(PictureChecks.Stale(null, RealCategory.Plant));
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
        /// <summary>Something that happens to the folder while the checker is looking.</summary>
        public Action? OnCheck { get; set; }

        public Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default)
        {
            Asked.Add((name, category));
            OnCheck?.Invoke();
            return Task.FromResult(Answer ? new VisionCheck(Model, Passed, Passed ? 1 : 0, Saw, new DateTime(2026, 11, 8, 16, 1, 0)) : null);
        }
    }
}
