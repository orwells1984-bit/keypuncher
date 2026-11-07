using System.Net;
using System.Text.Json;
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
    public async Task A_picture_in_play_can_be_checked_later_and_a_flag_does_not_hide_it()
    {
        await Maker().MakeAsync("ball", RealCategory.Object);
        _checker.Passed = false;
        _checker.Saw = "a brown egg";

        var check = await Maker(_checker).CheckAsync("ball");

        Assert.False(check!.Passed);
        Assert.True(_folder.HasPicture("ball"));
        var sidecar = _folder.ReadSidecar("ball")!;
        Assert.False(sidecar.Check!.Passed);
        Assert.Equal("a brown egg", sidecar.Check.Saw);
        Assert.Equal(PictureSource.Generated, sidecar.Source);
        Assert.Contains(_log, l => l.Contains("looks WRONG"));
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

        public Task<VisionCheck?> CheckAsync(byte[] png, string name, RealCategory category, CancellationToken ct = default)
        {
            Asked.Add((name, category));
            return Task.FromResult(Answer ? new VisionCheck(Model, Passed, Passed ? 0.9 : 0.3, Saw, new DateTime(2026, 11, 7, 1, 5, 0)) : null);
        }
    }
}

public sealed class OllamaVisionTests
{
    [Fact]
    public async Task The_check_sends_the_picture_and_the_question_and_reads_the_verdict()
    {
        string? sent = null;
        var handler = new RecordingHandler(request => { sent = request; return """{"model":"gemma3:4b","message":{"role":"assistant","content":"{\"shows\":\"a striped zebra standing\",\"is_the_thing\":true,\"one_whole_thing\":true,\"confidence\":0.92}"},"done":true}"""; });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var png = TestPhotos.Jpeg(64, 48);

        var check = await new OllamaVision(http).CheckAsync(png, "zebra", RealCategory.Animal);

        Assert.True(check!.Passed);
        Assert.Equal(0.92, check.Confidence, 1e-9);
        Assert.Equal("a striped zebra standing", check.Saw);
        Assert.Equal("gemma3:4b", check.Model);
        using var body = JsonDocument.Parse(sent!);
        Assert.Equal("gemma3:4b", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        var message = body.RootElement.GetProperty("messages")[0];
        Assert.Equal(OllamaVision.Question("zebra", RealCategory.Animal), message.GetProperty("content").GetString());
        Assert.Equal(Convert.ToBase64String(png), message.GetProperty("images")[0].GetString());
        Assert.Equal("object", body.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("http://localhost:11434/api/chat", handler.Url);
    }

    [Theory]
    [InlineData("""{"shows":"two zebras","is_the_thing":true,"one_whole_thing":false,"confidence":0.9}""", false, "two zebras")]
    [InlineData("""{"shows":"a horse","is_the_thing":false,"one_whole_thing":true,"confidence":0.9}""", false, "a horse")]
    [InlineData("""{"shows":"a zebra","is_the_thing":true,"one_whole_thing":true,"confidence":0.5}""", false, "a zebra")]
    [InlineData("""{"shows":"a zebra","is_the_thing":true,"one_whole_thing":true,"confidence":0.6}""", true, "a zebra")]
    [InlineData("I think it is a zebra", false, "I think it is a zebra")]
    public void The_verdict_passes_only_when_the_thing_is_there_whole_alone_and_the_model_is_sure(string content, bool passed, string saw)
    {
        var check = OllamaVision.Read(content, "gemma3:4b", DateTime.UnixEpoch);
        Assert.Equal(passed, check.Passed);
        Assert.Equal(saw, check.Saw);
    }

    [Fact]
    public async Task A_server_error_is_no_verdict()
    {
        var handler = new RecordingHandler(_ => null);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        Assert.Null(await new OllamaVision(http).CheckAsync(TestPhotos.Jpeg(32, 32), "zebra", RealCategory.Animal));
    }

    [Fact]
    public void The_question_names_the_thing_and_its_kind()
    {
        Assert.Equal("This is a cut-out picture for a toddler's toy. Does it show exactly one whole arctic thyme (a plant) and nothing else? Say what you see.",
            OllamaVision.Question("arctic thyme", RealCategory.Plant));
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
