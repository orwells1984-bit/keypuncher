using System.Net;
using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

public sealed class PictureMakerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-maker-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 7, 1, 0, 0);
    private readonly PictureFolder _folder;
    private readonly FakeImageMaker _model = new();
    private readonly FakeCutOut _cutOut = new();
    private readonly FakeChecker _checker = new();
    private readonly List<string> _log = [];

    public PictureMakerTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private PictureMaker Maker(IVisionChecker? checker = null) => new(_folder, _model, _cutOut, checker, () => _now, _log.Add);

    [Fact]
    public async Task A_gap_is_drawn_cut_out_put_in_play_and_checked()
    {
        _folder.Wanted.Add("sailboat", RealCategory.Vehicle, _now.AddDays(-1));
        _folder.Wanted.Bump("sailboat", "no photo found", _now.AddHours(-1));

        var outcome = await Maker(_checker).MakeAsync("sailboat", RealCategory.Vehicle);

        Assert.Equal(MakeOutcome.Made, outcome);
        Assert.True(_folder.HasPicture("sailboat"));
        Assert.True(File.Exists(_folder.RawPath("sailboat", ".png")));
        Assert.Null(_folder.Wanted.Get("sailboat"));
        var sidecar = _folder.ReadSidecar("sailboat")!;
        Assert.Equal(PictureSource.Generated, sidecar.Source);
        Assert.Equal("Z-Image-Turbo", sidecar.Generated!.Model);
        Assert.Equal(2, sidecar.Generated.Seed);
        Assert.Equal(GenerationPrompt.For("sailboat", RealCategory.Vehicle), sidecar.Generated.Prompt);
        Assert.StartsWith("fake, covers", sidecar.CutOut);
        Assert.Equal("made by Z-Image-Turbo, seed 2", sidecar.CreditLine);
        Assert.True(sidecar.Check!.Passed);
        Assert.Equal("a sailboat", sidecar.Check.Saw);
        Assert.Equal([("sailboat", RealCategory.Vehicle)], _checker.Asked);
        Assert.Equal([(GenerationPrompt.For("sailboat", RealCategory.Vehicle), 2L)], _model.Asked);
    }

    [Fact]
    public async Task A_nursery_name_is_drawn_under_its_real_name_but_kept_under_its_own()
    {
        var outcome = await Maker().MakeAsync("rex", RealCategory.Animal);

        Assert.Equal(MakeOutcome.Made, outcome);
        Assert.True(_folder.HasPicture("rex"));
        Assert.Equal(GenerationPrompt.For("Tyrannosaurus", RealCategory.ExtinctAnimal), _model.Asked.Single().Prompt);
        Assert.Contains("life restoration of one whole Tyrannosaurus", _model.Asked.Single().Prompt);
    }

    [Fact]
    public async Task A_failed_cut_out_keeps_the_picture_raw_and_asks_for_another_seed_next_time()
    {
        _cutOut.FailWith = "touches three edges";
        var outcome = await Maker().MakeAsync("ball", RealCategory.Object);

        Assert.Equal(MakeOutcome.CutOutFailed, outcome);
        Assert.False(_folder.HasPicture("ball"));
        Assert.True(File.Exists(_folder.RawPath("ball", ".png")));
        var wanted = _folder.Wanted.Get("ball")!;
        Assert.Equal("cut-out failed: touches three edges", wanted.Reason);
        Assert.Equal(1, wanted.Tries);
        Assert.Equal("fake failed: touches three edges", _folder.ReadSidecar("ball")!.CutOut);

        _cutOut.FailWith = null;
        await Maker().MakeAsync("ball", RealCategory.Object);
        Assert.Equal([1L, 2L], _model.Asked.Select(a => a.Seed));
    }

    [Fact]
    public async Task The_picture_that_failed_is_kept_under_its_number_when_another_is_drawn()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        _checker.Passed = false;
        await Maker(_checker).CheckAsync("ball");

        // A try whose cut-out fails leaves the hidden picture and its story as they were.
        _cutOut.FailWith = "touches three edges";
        Assert.Equal(MakeOutcome.CutOutFailed, await Maker().MakeAsync("ball", RealCategory.Object));
        Assert.Empty(_folder.Kept("ball"));
        Assert.Equal((1, 1L), (_folder.ReadSidecar("ball")!.Id, _folder.ReadSidecar("ball")!.Generated!.Seed));
        Assert.True(_folder.IsHidden("ball"));

        _cutOut.FailWith = null;
        Assert.Equal(MakeOutcome.Made, await Maker().MakeAsync("ball", RealCategory.Object));

        var kept = Assert.Single(_folder.Kept("ball"));
        Assert.Equal((1, 1L, false), (kept.Id, kept.Generated!.Seed, kept.Check!.Passed));
        Assert.False(kept.Hidden);
        Assert.Equal(2, _folder.ReadSidecar("ball")!.Id);
        Assert.NotEqual(1L, _folder.ReadSidecar("ball")!.Generated!.Seed);
        Assert.True(_folder.HasPicture("ball"));
    }

    [Fact]
    public async Task A_model_that_fails_is_not_counted_as_a_try()
    {
        _model.FailWith = "out of memory on the card";
        var outcome = await Maker().MakeAsync("ball", RealCategory.Object);

        Assert.Equal(MakeOutcome.Failed, outcome);
        Assert.Equal(0, _folder.Wanted.Get("ball")!.Tries);
        Assert.Null(_folder.ReadSidecar("ball"));
        Assert.Contains(_log, l => l.Contains("out of memory"));
    }

    [Fact]
    public async Task Missing_models_are_said_so_and_nothing_is_written()
    {
        _model.IsAvailable = false;
        Assert.Equal(MakeOutcome.ModelMissing, await Maker().MakeAsync("ball", RealCategory.Object));
        _model.IsAvailable = true;
        _cutOut.IsAvailable = false;
        Assert.Equal(MakeOutcome.ModelMissing, await Maker().MakeAsync("ball", RealCategory.Object));
        Assert.Equal("cut-out model missing", _folder.Wanted.Get("ball")!.Reason);
        Assert.Empty(_model.Asked);
    }

    [Fact]
    public async Task A_thing_with_a_picture_already_is_left_alone()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        Assert.Equal(MakeOutcome.AlreadyHave, await Maker().MakeAsync("ball", RealCategory.Object));
        Assert.Single(_model.Asked);
    }

    [Fact]
    public async Task A_picture_that_fails_its_check_is_hidden_and_drawn_again_with_another_seed()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        _checker.Passed = false;
        _checker.Saw = "a brown egg";

        var check = await Maker(_checker).CheckAsync("ball");

        Assert.False(check!.Passed);
        Assert.False(_folder.HasPicture("ball"));
        Assert.True(_folder.IsHidden("ball"));
        var sidecar = _folder.ReadSidecar("ball")!;
        Assert.False(sidecar.Check!.Passed);
        Assert.Equal("a brown egg", sidecar.Check.Saw);
        Assert.Equal(PictureSource.Generated, sidecar.Source);
        Assert.Contains(_log, l => l.Contains("looks WRONG"));
        Assert.Equal(PictureChecks.FailedCheck, _folder.Wanted.Get("ball")!.Reason);

        _checker.Passed = true;
        Assert.Equal(MakeOutcome.Made, await Maker(_checker).MakeAsync("ball", RealCategory.Object));
        Assert.True(_folder.HasPicture("ball"));
        Assert.False(_folder.IsHidden("ball"));
        Assert.Equal([1L, 2L], _model.Asked.Select(a => a.Seed));
    }

    [Fact]
    public async Task A_picture_a_grown_up_hid_is_not_drawn_again()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        _folder.Hide("ball");
        Assert.Equal(MakeOutcome.AlreadyHave, await Maker().MakeAsync("ball", RealCategory.Object));
        Assert.Single(_model.Asked);
    }

    [Fact]
    public async Task A_checker_that_does_not_answer_leaves_the_sidecar_as_it_was()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        _checker.Answer = false;
        Assert.Null(await Maker(_checker).CheckAsync("ball"));
        Assert.Null(_folder.ReadSidecar("ball")!.Check);
        Assert.Null(await Maker(_checker).CheckAsync("no-such-thing"));
    }

    [Theory]
    [InlineData("no photo found", 1, true)]
    [InlineData("no article", 1, true)]
    [InlineData("offline", 5, false)]
    [InlineData("cut-out model missing", 5, false)]
    [InlineData("cut-out failed: too small", 2, false)]
    [InlineData("cut-out failed: too small", 3, true)]
    [InlineData(null, 0, false)]
    public void A_gap_is_a_thing_the_photo_sources_gave_up_on(string? reason, int tries, bool gap)
    {
        var wanted = new WantedEntry("ball", "ball", RealCategory.Object, _now) { Reason = reason, Tries = tries };
        Assert.Equal(gap, PictureMaker.IsGap(wanted));
        Assert.False(PictureMaker.IsGap(wanted with { Slug = "wiggrass", Name = "wiggrass", Reason = "no article" }));
    }

    [Theory]
    [InlineData("zebra", RealCategory.Animal, "a photo of one whole zebra, full body, side view, standing on plain white background, studio lighting, sharp, centered")]
    [InlineData("Tyrannosaurus", RealCategory.ExtinctAnimal, "a realistic life restoration of one whole Tyrannosaurus, full body, side view, plain white background, museum illustration style, centered")]
    [InlineData("arctic thyme", RealCategory.Plant, "a photo of one whole arctic thyme plant, entire plant visible, plain white background, centered")]
    [InlineData("sailboat", RealCategory.Vehicle, "a photo of one sailboat, whole, three-quarter view, plain white background, product photo, centered")]
    [InlineData("ball", RealCategory.Object, "a photo of one ball, whole, three-quarter view, plain white background, product photo, centered")]
    public void The_prompt_asks_for_one_whole_thing_on_white(string name, RealCategory category, string prompt) =>
        Assert.Equal(prompt, GenerationPrompt.For(name, category));

    /// <summary>Draws the sample animal on white, as the image model would.</summary>
    private sealed class FakeImageMaker : IImageMaker
    {
        public string Name => "Z-Image-Turbo";
        public bool IsAvailable { get; set; } = true;
        public string? FailWith { get; set; }
        public List<(string Prompt, long Seed)> Asked { get; } = [];

        public Task<(MadeImage? Image, string? Reason)> MakeAsync(string prompt, long seed, CancellationToken ct = default)
        {
            Asked.Add((prompt, seed));
            if (FailWith is { } why) return Task.FromResult<(MadeImage?, string?)>((null, why));
            using var sample = SamplePicture.Draw();
            using var photo = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var c = new SKCanvas(photo)) { c.Clear(SKColors.White); c.DrawBitmap(sample, 0, 0, SKSamplingOptions.Default); }
            using var data = photo.Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult<(MadeImage?, string?)>((new MadeImage(data.ToArray(), new GenerationProvenance(Name, "z_image_turbo-Q4_K.gguf", prompt, seed, 8, 1.0f)), null));
        }
    }

    private sealed class FakeChecker : IVisionChecker
    {
        public string Model => "gemma3:4b";
        public bool Passed { get; set; } = true;
        public bool Answer { get; set; } = true;
        public string Saw { get; set; } = "a sailboat";
        public List<(string Name, RealCategory Category)> Asked { get; } = [];
        public List<byte[]?> Photos { get; } = [];

        public Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, byte[]? photo = null, CancellationToken ct = default)
        {
            Asked.Add((name, category));
            Photos.Add(photo);
            return Task.FromResult(Answer ? new VisionCheck(Model, Passed, Passed ? 0.9 : 0.3, Saw, new DateTime(2026, 11, 7, 1, 5, 0)) : null);
        }
    }
}

public sealed class OllamaVisionTests
{
    private static string Reply(string content) =>
        JsonSerializer.Serialize(new { model = "gemma3:4b", message = new { role = "assistant", content }, done = true });

    [Fact]
    public async Task The_check_asks_for_a_description_then_a_verdict_and_reads_both()
    {
        var sent = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            sent.Add(request);
            return sent.Count == 1 ? Reply("I see one striped zebra standing on white, whole and alive.") : Reply("""{"count":1,"is_the_thing":true,"whole":true}""");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var png = TestPhotos.Jpeg(64, 48);

        var check = await new OllamaVision(http).CheckAsync(png, "zebra", RealCategory.Animal);

        Assert.True(check!.Passed);
        Assert.Equal("I see one striped zebra standing on white, whole and alive.", check.Saw);
        Assert.Equal("gemma3:4b", check.Model);
        Assert.Equal(2, sent.Count);
        using var first = JsonDocument.Parse(sent[0]);
        Assert.Equal("gemma3:4b", first.RootElement.GetProperty("model").GetString());
        Assert.False(first.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(first.RootElement.TryGetProperty("format", out _));
        var message = first.RootElement.GetProperty("messages")[0];
        Assert.Equal(OllamaVision.Question("zebra", RealCategory.Animal), message.GetProperty("content").GetString());
        Assert.Equal(Convert.ToBase64String(png), message.GetProperty("images")[0].GetString());
        using var second = JsonDocument.Parse(sent[1]);
        var messages = second.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        Assert.Equal("I see one striped zebra standing on white, whole and alive.", messages[1].GetProperty("content").GetString());
        Assert.Equal(OllamaVision.Verdict("zebra"), messages[2].GetProperty("content").GetString());
        Assert.Equal("object", second.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("http://localhost:11434/api/chat", handler.Url);
    }

    [Theory]
    [InlineData("two zebras", """{"count":2,"is_the_thing":true,"whole":true}""", RealCategory.Animal, false, "two zebras [counted 2]")]
    [InlineData("a horse", """{"count":1,"is_the_thing":false,"whole":true}""", RealCategory.Animal, false, "a horse [not the thing]")]
    [InlineData("the front half of a zebra", """{"count":1,"is_the_thing":true,"whole":false}""", RealCategory.Animal, false, "the front half of a zebra [not whole]")]
    [InlineData("a zebra", null, RealCategory.Animal, false, "a zebra [no verdict]")]
    [InlineData("a zebra", "I think it is a zebra", RealCategory.Animal, false, "a zebra [no verdict]")]
    [InlineData("a zebra", """{"count":1,"is_the_thing":true,"whole":true}""", RealCategory.Animal, true, "a zebra")]
    [InlineData("a zebra", """{"is_the_thing":true,"whole":true}""", RealCategory.Animal, true, "a zebra")]
    [InlineData("a whole fire engine", """{"count":13,"is_the_thing":true,"whole":true}""", RealCategory.Vehicle, true, "a whole fire engine")]
    [InlineData("an oak tree in leaf", """{"count":7,"is_the_thing":true,"whole":true}""", RealCategory.Plant, true, "an oak tree in leaf")]
    public void The_verdict_passes_only_when_the_thing_is_there_whole_and_alone(string saw, string? verdict, RealCategory category, bool passed, string kept)
    {
        var check = OllamaVision.Read(saw, verdict, "gemma3:4b", DateTime.UnixEpoch, category == RealCategory.Vehicle ? "fire engine" : category == RealCategory.Plant ? "oak tree" : "zebra", category);
        Assert.Equal(passed, check.Passed);
        Assert.Equal(kept, check.Saw);
    }

    /// <summary>The desktop's first run: gemma3 said yes to pictures its own description gave away.</summary>
    [Theory]
    [InlineData("two white mice", "mouse", RealCategory.Animal, "two")]
    [InlineData("A family of lions", "lion", RealCategory.Animal, "family")]
    [InlineData("four Icelandic horses standing", "icelandic horse", RealCategory.Animal, "four")]
    [InlineData("a skeleton of a parasaurolophus", "parasaurolophus", RealCategory.ExtinctAnimal, "skeleton")]
    [InlineData("one complete ichthyosaur fossil with ammonites", "ichthyosaur", RealCategory.ExtinctAnimal, "fossil")]
    [InlineData("one branch of an oak tree with leaves", "oak tree", RealCategory.Plant, "branch")]
    [InlineData("a cut-out picture of a halved almond", "seed", RealCategory.Plant, "halved")]
    [InlineData("a pumpkin pie next to a can of Libby's", "pumpkin", RealCategory.Plant, "pie")]
    [InlineData("A stack of coconuts in a crate", "coconut palm", RealCategory.Plant, "stack")]
    [InlineData("a collection of baby carrots in a radial pattern", "carrot", RealCategory.Plant, "collection")]
    [InlineData("a very faded, partial drawing of a ticket", "paddle boat", RealCategory.Vehicle, "partial")]
    [InlineData("five cut-out pieces of thylacines", "thylacine", RealCategory.ExtinctAnimal, "five")]
    [InlineData("several other sheep partially visible behind it", "sheep", RealCategory.Animal, "several")]
    [InlineData("a complete, three-dimensional skull of a harbour seal", "harbour seal", RealCategory.Animal, "skull")]
    [InlineData("a megalodon shark's mouth, a complete representation of its jaws", "megalodon", RealCategory.ExtinctAnimal, "jaws")]
    [InlineData("a lot of small fish swimming together", "fish", RealCategory.Animal, "a lot")]
    [InlineData("a single oak branch, representing the whole branch of an oak tree", "oak tree", RealCategory.Plant, "branch")]
    [InlineData("a single acacia tree branch with yellow flowers", "acacia tree", RealCategory.Plant, "branch")]
    [InlineData("depicted in a medieval illuminated manuscript", "steller's sea cow", RealCategory.ExtinctAnimal, "illuminated")]
    [InlineData("one whole paddle boat, a brown animal with a white belly", "paddle boat", RealCategory.Vehicle, "animal")]
    [InlineData("the skull of a cave bear", "cave bear", RealCategory.ExtinctAnimal, "skull")]
    [InlineData("a single, complete, and alive harbor seal skull", "harbour seal", RealCategory.Animal, "skull")]
    [InlineData("a detailed drawing of a paddle boat from an older document", "paddle boat", RealCategory.Vehicle, "drawing")]
    [InlineData("a close-up shot of the plant itself", "crowberry", RealCategory.Plant, "close up")]
    [InlineData("a peeled banana cut in half", "banana", RealCategory.Plant, "peeled")]
    public void A_description_that_gives_the_picture_away_fails_it(string saw, string name, RealCategory category, string word)
    {
        Assert.Equal(word, OllamaVision.GiveAway(saw, name, category));
        Assert.Equal($"says \"{word}\"", OllamaVision.Fault(saw, 1, true, true, name, category));
    }

    /// <summary>Words a good picture is described with must not flag it: the thing's own name, "cut-out", "I can see",
    /// "both ears", a tulip's stem, a cluster of flowers, a branch when the thing is not a plant.</summary>
    [Theory]
    [InlineData("a cut-out of one striped zebra, I can see both ears", "zebra", RealCategory.Animal)]
    [InlineData("a red tulip with a green stem", "tulip", RealCategory.Plant)]
    [InlineData("a box turtle on the ground", "box turtle", RealCategory.Animal)]
    [InlineData("a fire salamander, black with yellow spots", "salamander", RealCategory.Animal)]
    [InlineData("a cluster of purple hydrangea flowers", "hydrangea", RealCategory.Plant)]
    [InlineData("a squirrel sitting on a branch", "squirrel", RealCategory.Animal)]
    [InlineData("a pair of scissors", "pair of scissors", RealCategory.Object)]
    [InlineData("a single whole strawberry, not a slice, drawing or diagram", "strawberry", RealCategory.Plant)]
    [InlineData("a realistic, three-dimensional hot-air balloon", "hot-air balloon", RealCategory.Object)]
    [InlineData("an elephant standing on four legs", "elephant", RealCategory.Animal)]
    [InlineData("an octopus with eight arms spread out", "octopus", RealCategory.Animal)]
    [InlineData("a birch tree with many branches and leaves", "birch tree", RealCategory.Plant)]
    [InlineData("a cactus covered in many spines", "cactus", RealCategory.Plant)]
    [InlineData("a bicycle with two wheels", "bicycle", RealCategory.Vehicle)]
    [InlineData("a realistic cut-out illustration of a polar bear for a children's book", "polar bear", RealCategory.Animal)]
    [InlineData("a ferry with the words BASTO VIII on its side", "ferry", RealCategory.Vehicle)]
    [InlineData("a red cabbage, whole, rather than a drawing", "red cabbage", RealCategory.Plant)]
    [InlineData("one whole, living harbour seal, with its head and skull clearly visible", "harbour seal", RealCategory.Animal)]
    [InlineData("one whole wooden spoon with a shallow bowl and a curved handle", "wooden spoon", RealCategory.Object)]
    [InlineData("a squirrel with a bushy tail sitting on a branch", "squirrel", RealCategory.Animal)]
    [InlineData("a wooden rowing boat with two oars", "rowing boat", RealCategory.Vehicle)]
    [InlineData("a black and white drawing of a single, complete great auk", "great auk", RealCategory.ExtinctAnimal)]
    [InlineData("a watercolor painting of one brachiosaurus dinosaur", "brachiosaurus", RealCategory.ExtinctAnimal)]
    public void A_clean_description_passes(string saw, string name, RealCategory category)
    {
        Assert.Null(OllamaVision.GiveAway(saw, name, category));
        Assert.Null(OllamaVision.Fault(saw, 1, true, true, name, category));
    }

    [Theory]
    [InlineData("a single, pristine white peace lily plant with a yellow flower spike", "house", false)]
    [InlineData("a single, live chipmunk on a rock", "squirrel", false)]
    [InlineData("four whole, living Icelandic horses", "icelandic horse", true)]
    [InlineData("a wels catfish resting on gravel", "wels catfish", true)]
    [InlineData("two white mice", "mouse", true)]
    [InlineData("one whole fox", "fox", true)]
    [InlineData("a bright red balloon", "ball", false)]
    public void The_description_must_name_the_thing(string saw, string name, bool named)
    {
        Assert.Equal(named, OllamaVision.Named(saw, name));
        if (!named) Assert.Equal("does not name it", OllamaVision.Fault(saw, 1, true, true, name, RealCategory.Object));
    }

    [Theory]
    [InlineData("Here's a description of the image:\n\nOne whole zebra.", "One whole zebra.")]
    [InlineData("Here's a description for a young child's picture book:\n\n**A single mouse.**", "A single mouse.")]
    [InlineData("  One whole zebra standing.  ", "One whole zebra standing.")]
    [InlineData("A zebra: black and white.", "A zebra: black and white.")]
    public void A_preface_line_is_dropped_from_the_description(string answer, string sentence) =>
        Assert.Equal(sentence, OllamaVision.Sentence(answer));

    [Theory]
    [InlineData("mice", "mouse")]
    [InlineData("horses", "horse")]
    [InlineData("puppies", "puppy")]
    [InlineData("foxes", "fox")]
    [InlineData("branches", "branch")]
    [InlineData("fish", "fish")]
    [InlineData("grass", "grass")]
    public void A_rough_singular_is_enough_to_match_the_name(string plural, string singular) =>
        Assert.Equal(singular, OllamaVision.Singular(plural));

    /// <summary>The octopus: shown the white shell from its arms alone, gemma3 saw "a single, complete, live octopus".
    /// With the photo, a third call shows both and a clear "not all of it" takes the pass away.</summary>
    [Fact]
    public async Task With_the_photo_a_passed_cut_out_is_compared_with_it()
    {
        var sent = new List<string>();
        var handler = new RecordingHandler(request =>
        {
            sent.Add(request);
            return sent.Count switch
            {
                1 => Reply("A single, complete, live octopus with its tentacles extended."),
                2 => Reply("""{"count":1,"is_the_thing":true,"whole":true}"""),
                _ => Reply("""{"kept":"a white shell the octopus is holding.","all_of_it":false}"""),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var png = TestPhotos.Jpeg(32, 32);
        var photo = TestPhotos.Jpeg(64, 48);

        var check = await new OllamaVision(http).CheckAsync(png, "octopus", RealCategory.Animal, photo);

        Assert.False(check!.Passed);
        Assert.Equal(0, check.Confidence);
        Assert.Equal("A single, complete, live octopus with its tentacles extended. [kept a white shell the octopus is holding, not all of the octopus in the photo]", check.Saw);
        Assert.Equal(3, sent.Count);
        using var third = JsonDocument.Parse(sent[2]);
        var messages = third.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal(OllamaVision.Compare("octopus"), messages[0].GetProperty("content").GetString());
        var images = messages[0].GetProperty("images");
        Assert.Equal(Convert.ToBase64String(photo), images[0].GetString());
        Assert.Equal(Convert.ToBase64String(png), images[1].GetString());
        Assert.Equal("object", third.RootElement.GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_cut_out_that_already_failed_is_not_compared()
    {
        var sent = 0;
        var handler = new RecordingHandler(_ => ++sent == 1 ? Reply("two zebras") : Reply("""{"count":2,"is_the_thing":true,"whole":true}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };

        var check = await new OllamaVision(http).CheckAsync(TestPhotos.Jpeg(32, 32), "zebra", RealCategory.Animal, TestPhotos.Jpeg(64, 48));

        Assert.False(check!.Passed);
        Assert.Equal(2, sent);
    }

    [Theory]
    [InlineData("""{"kept":"the whole zebra","all_of_it":true}""", true, "one zebra")]
    [InlineData("""{"kept":"one hoof","all_of_it":false}""", false, "one zebra [kept one hoof, not all of the zebra in the photo]")]
    [InlineData("""{"all_of_it":false}""", false, "one zebra [not all of the zebra in the photo]")]
    [InlineData("""{"kept":"the zebra"}""", true, "one zebra")]
    [InlineData("I think it kept the zebra", true, "one zebra")]
    [InlineData(null, true, "one zebra")]
    public void Only_a_clear_no_from_the_comparison_takes_a_pass_away(string? answer, bool passed, string saw)
    {
        var check = OllamaVision.Compared(new VisionCheck("gemma3:4b", true, 1, "one zebra", DateTime.UnixEpoch), answer, "zebra");
        Assert.Equal(passed, check.Passed);
        Assert.Equal(saw, check.Saw);
    }

    [Fact]
    public void The_comparison_says_which_image_is_which()
    {
        var compare = OllamaVision.Compare("octopus");
        Assert.Contains("first image is a photo", compare);
        Assert.Contains("second image is a cut-out", compare);
        Assert.Contains("whole octopus", compare);
    }

    [Fact]
    public async Task A_server_error_is_no_verdict()
    {
        var handler = new RecordingHandler(_ => null);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        Assert.Null(await new OllamaVision(http).CheckAsync(TestPhotos.Jpeg(32, 32), "zebra", RealCategory.Animal));
    }

    [Fact]
    public async Task A_verdict_that_fails_to_come_keeps_the_description_and_flags()
    {
        var calls = 0;
        var handler = new RecordingHandler(_ => ++calls == 1 ? Reply("one striped zebra") : null);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var check = await new OllamaVision(http).CheckAsync(TestPhotos.Jpeg(32, 32), "zebra", RealCategory.Animal);
        Assert.False(check!.Passed);
        Assert.Equal("one striped zebra [no verdict]", check.Saw);
    }

    [Fact]
    public void The_question_names_the_thing_and_its_kind()
    {
        var question = OllamaVision.Question("arctic thyme", RealCategory.Plant);
        Assert.Contains("exactly one whole arctic thyme (a plant)", question);
        Assert.Contains("how many", question);
        Assert.DoesNotContain("toy", question);
        Assert.DoesNotContain("picture book", question);
        Assert.Contains("a fossil or a drawing", question);
    }

    /// <summary>Answers every POST with the given body (null for a 500) and keeps what was sent.</summary>
    private sealed class RecordingHandler(Func<string, string?> answer) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            var sent = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = answer(sent);
            return body is null
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}

public sealed class SdCliSetupTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-sd-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void The_pieces_are_found_under_sd_and_models_or_named_as_missing()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sd"));
        Directory.CreateDirectory(Path.Combine(_dir, "models"));
        File.WriteAllText(Path.Combine(_dir, "sd", "sd-cli.exe"), "");
        File.WriteAllText(Path.Combine(_dir, "models", "z_image_turbo-Q4_K.gguf"), "");
        File.WriteAllText(Path.Combine(_dir, "models", "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"), "");

        var setup = SdCliSetup.Find(_dir);

        Assert.False(setup.AllPresent);
        Assert.Equal(["ae.safetensors"], setup.Missing().ToList());
        Assert.Equal(Path.Combine(_dir, "sd", "sd-cli.exe"), setup.Exe);
        Assert.EndsWith("z_image_turbo-Q4_K.gguf", setup.DiffusionModel);

        File.WriteAllText(Path.Combine(_dir, "models", "ae.safetensors"), "");
        Assert.True(SdCliSetup.Find(_dir).AllPresent);
    }

    [Fact]
    public void The_command_line_is_the_one_the_bench_proved_with_a_dotted_budget_whatever_the_locale()
    {
        var setup = new SdCliSetup("sd-cli.exe", "z.gguf", "q.gguf", "ae.safetensors");
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("is-IS");
            var args = setup.FittedTo(4592).Arguments("a photo of one ball", 3, @"C:\out\ball.png");
            Assert.Equal(["--diffusion-model", "z.gguf", "--llm", "q.gguf", "--vae", "ae.safetensors", "-p", "a photo of one ball", "--cfg-scale", "1.0", "--steps", "8",
                "-W", "512", "-H", "512", "--offload-to-cpu", "--diffusion-fa", "--seed", "3", "-o", @"C:\out\ball.png", "-v", "--max-vram", "2.75"], args);
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(4592, 2.75, false)]
    [InlineData(4700, 3.0, false)]
    [InlineData(3900, 2.0, true)]
    [InlineData(1000, 1.0, true)]
    [InlineData(9000, 3.75, false)]
    public void The_budget_leaves_room_for_the_models_working_memory(int freeMiB, double budget, bool lean)
    {
        var fitted = new SdCliSetup("a", "b", "c", "d").FittedTo(freeMiB);
        Assert.Equal(budget, fitted.MaxVramGiB);
        Assert.Equal(lean, fitted.VaeTiling);
        Assert.Equal(lean, fitted.VaeOnCpu);
    }

    [Theory]
    [InlineData("4592\n", 4592)]
    [InlineData("4592 MiB", 4592)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("NVIDIA-SMI has failed", null)]
    public void Free_memory_is_read_from_what_nvidia_smi_prints(string? output, int? free) => Assert.Equal(free, Vram.Parse(output));

    [Theory]
    [InlineData("[W] model manager cannot make enough memory available on CUDA0", true)]
    [InlineData("[E] z_image segment 1/1 (graph) failed during workspace capacity check", true)]
    [InlineData("ggml_cuda_init: found 1 CUDA devices", false)]
    public void Out_of_memory_is_known_by_its_wording(string log, bool oom) => Assert.Equal(oom, SdCli.OutOfMemory(log));
}
