using System.Net;
using System.Text;
using Keypuncher.Inference.Ollama;

namespace Keypuncher.Director.Tests;

public class OllamaModelsTests
{
    /// <summary>Answers each request by the first route whose key its path ends with; remembers what was asked.</summary>
    private sealed class FakeOllama : HttpMessageHandler
    {
        private readonly List<(string Path, Func<string, HttpResponseMessage> Answer)> _routes = [];
        public List<(string Path, string Body)> Asked { get; } = [];

        public FakeOllama On(string path, string body, HttpStatusCode code = HttpStatusCode.OK) =>
            On(path, _ => new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public FakeOllama On(string path, Func<string, HttpResponseMessage> answer)
        {
            _routes.Add((path, answer));
            return this;
        }

        public OllamaModels Models() => new(new HttpClient(this) { BaseAddress = new Uri("http://localhost:11434/") });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Asked) Asked.Add((path, body));
            foreach (var (key, answer) in _routes)
                if (path.EndsWith(key, StringComparison.Ordinal)) return answer(body);
            throw new HttpRequestException($"nothing listens at {path}");
        }
    }

    private const string TwoOthers = """
        {"models":[
          {"name":"laguna-xs-2.1:latest","size":20000000000},
          {"name":"qwen3-coder:30b","size":18000000000}]}
        """;

    private const string AllFour = """
        {"models":[
          {"name":"gemma3:4b","size":3338801804},
          {"name":"qwen3:4b","size":2497293931},
          {"name":"laguna-xs-2.1:latest","size":20000000000},
          {"name":"qwen3-coder:30b","size":18000000000}]}
        """;

    [Theory]
    [InlineData("qwen3:4b", "qwen3:4b", true)]
    [InlineData("QWEN3:4B", "qwen3:4b", true)]
    [InlineData("laguna-xs-2.1", "laguna-xs-2.1:latest", true)]
    [InlineData("qwen3:4b", "qwen3:8b", false)]
    [InlineData("qwen3", "qwen3:4b", false)]
    [InlineData("localhost:5000/toy", "localhost:5000/toy:latest", true)]
    public void A_name_without_a_tag_means_latest_and_case_does_not_matter(string a, string b, bool same) =>
        Assert.Equal(same, OllamaModels.Same(a, b));

    [Fact]
    public async Task The_run_of_11_November_is_caught_both_models_are_missing()
    {
        var check = await new FakeOllama().On("api/tags", TwoOthers).Models().CheckAsync(["qwen3:4b", "gemma3:4b"]);

        Assert.True(check.Reachable);
        Assert.Equal(["qwen3:4b", "gemma3:4b"], check.Missing);
        Assert.Equal(["laguna-xs-2.1:latest", "qwen3-coder:30b"], check.Installed.Select(m => m.Name));
    }

    [Fact]
    public async Task Nothing_is_missing_when_both_are_there_and_a_model_wanted_twice_is_named_once()
    {
        var ollama = new FakeOllama().On("api/tags", AllFour);
        Assert.Empty((await ollama.Models().CheckAsync(["qwen3:4b", "gemma3:4b"])).Missing);
        Assert.Equal(["phi4-mini"], (await ollama.Models().CheckAsync(["phi4-mini", "PHI4-MINI", "qwen3:4b"])).Missing);
        Assert.Equal(2497293931, (await ollama.Models().ListAsync()).Single(m => m.Name == "qwen3:4b").Size);
    }

    [Fact]
    public async Task An_Ollama_that_does_not_answer_is_unreachable_and_nothing_is_called_missing()
    {
        var check = await new FakeOllama().Models().CheckAsync(["qwen3:4b"]);

        Assert.False(check.Reachable);
        Assert.Empty(check.Missing);
        Assert.Empty(check.Installed);
    }

    [Fact]
    public async Task Capabilities_say_which_model_can_see_and_an_unknown_one_may_do_anything()
    {
        var ollama = new FakeOllama().On("api/tags", AllFour).On("api/show", body =>
            body.Contains("gemma3") ? Json("""{"capabilities":["completion","vision"]}""")
            : body.Contains("laguna") ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Json("""{"capabilities":["completion","tools"]}"""));

        var models = await ollama.Models().ListAsync(withCapabilities: true);

        Assert.Equal(["gemma3:4b", "laguna-xs-2.1:latest"], models.Where(m => m.Sees).Select(m => m.Name));
        Assert.All(models, m => Assert.True(m.Writes));
        Assert.False(new InstalledModel("embedder", 1, ["embedding"]).Writes);
    }

    [Fact]
    public async Task A_download_sums_its_files_and_ends_on_success()
    {
        var lines = string.Join("\n",
            """{"status":"pulling manifest"}""",
            """{"status":"pulling aaa","digest":"sha256:aaa","total":2000,"completed":500}""",
            """{"status":"pulling aaa","digest":"sha256:aaa","total":2000,"completed":2000}""",
            """{"status":"pulling bbb","digest":"sha256:bbb","total":100,"completed":40}""",
            """{"status":"verifying sha256 digest"}""",
            """{"status":"success"}""") + "\n";
        var ollama = new FakeOllama().On("api/pull", lines);
        var seen = new List<PullProgress>();

        await ollama.Models().PullAsync("qwen3:4b", new Immediate(seen.Add));

        Assert.Contains("\"model\":\"qwen3:4b\"", ollama.Asked.Single().Body);
        Assert.Equal((500L, 2000L), (seen[1].Completed, seen[1].Total));
        Assert.Equal((2040L, 2100L), (seen[3].Completed, seen[3].Total));
        Assert.Equal("success", seen[^1].Status);
        Assert.All(seen, p => Assert.Equal("qwen3:4b", p.Model));
    }

    [Fact]
    public async Task A_download_Ollama_refuses_fails_with_its_own_words()
    {
        var inStream = new FakeOllama().On("api/pull", "{\"status\":\"pulling manifest\"}\n{\"error\":\"pull model manifest: file does not exist\"}\n");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => inStream.Models().PullAsync("no-such-model"));
        Assert.Contains("file does not exist", refused.Message);

        var atOnce = new FakeOllama().On("api/pull", """{"error":"invalid model name"}""", HttpStatusCode.BadRequest);
        Assert.Contains("invalid model name", (await Assert.ThrowsAsync<InvalidOperationException>(() => atOnce.Models().PullAsync("??"))).Message);

        var cutShort = new FakeOllama().On("api/pull", "{\"status\":\"pulling manifest\"}\n");
        await Assert.ThrowsAsync<InvalidOperationException>(() => cutShort.Models().PullAsync("qwen3:4b"));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Reports on the calling thread, where <see cref="Progress{T}"/> would post to the pool and arrive late.</summary>
    private const string LagunaLoaded = """
        {"models":[{"name":"laguna-xs-2.1:latest","size":4100000000,"size_vram":3000000000}]}
        """;

    [Fact]
    public async Task What_ollama_has_loaded_is_read_with_its_share_of_the_card()
    {
        var loaded = await new FakeOllama().On("api/ps", LagunaLoaded).Models().LoadedAsync();
        Assert.Equal([new LoadedModel("laguna-xs-2.1:latest", 3_000_000_000)], loaded);
        Assert.Empty(await new FakeOllama().On("api/ps", """{"models":[]}""").Models().LoadedAsync());
    }

    [Fact]
    public async Task The_look_at_the_card_never_throws()
    {
        var state = await new FakeOllama().Models().CardAsync(() => 4900);
        Assert.False(state.OllamaReachable);
        Assert.Equal(4900, state.FreeMiB);
        Assert.Null(state.Trouble("qwen3:4b", ["qwen3:4b", "gemma3:4b"]));

        var broken = await new FakeOllama().On("api/ps", LagunaLoaded).Models().CardAsync(() => throw new InvalidOperationException("no nvidia-smi"));
        Assert.True(broken.OllamaReachable);
        Assert.Null(broken.FreeMiB);
    }

    private static readonly string[] Ours = ["qwen3:4b", "gemma3:4b"];

    [Fact]
    public void The_run_of_21_November_is_caught_another_jobs_model_holds_ollama()
    {
        var state = new CardState(true, [new LoadedModel("laguna-xs-2.1:latest", 3_000_000_000)], 1847);
        Assert.Equal(["laguna-xs-2.1:latest"], state.Others(Ours));
        Assert.Contains("Ollama is running laguna-xs-2.1:latest for something else", state.Trouble("qwen3:4b", Ours));
    }

    [Fact]
    public void A_card_too_full_for_the_model_is_caught_even_when_ollama_shows_nothing_loaded()
    {
        // At 07:54 that morning the other model was still loading, so Ollama listed nothing; the card said it all.
        var trouble = new CardState(true, [], 1847).Trouble("qwen3:4b", Ours, 2_497_293_931);
        Assert.Contains("Only 1,847 MiB", trouble);
        Assert.Contains("3,097 MiB", trouble);
        Assert.Contains("Only 1,847 MiB", new CardState(false, [], 1847).Trouble("qwen3:4b", Ours));
    }

    [Fact]
    public void A_free_card_or_the_toys_own_model_loaded_is_no_trouble()
    {
        Assert.Null(new CardState(true, [], 4900).Trouble("qwen3:4b", Ours, 2_497_293_931));
        // The director's model is still loaded from a play a few minutes ago: the card looks full and is not.
        Assert.Null(new CardState(true, [new LoadedModel("qwen3:4b", 3_200_000_000)], 1600).Trouble("qwen3:4b", Ours));
        // The picture checker is loaded: Ollama swaps it out, so its memory counts as free.
        Assert.Null(new CardState(true, [new LoadedModel("gemma3:4b", 3_900_000_000)], 900).Trouble("qwen3:4b", Ours));
        // Nothing known, nothing said.
        Assert.Null(CardState.Unknown.Trouble("qwen3:4b", Ours));
        Assert.Null(new CardState(true, [], null).Trouble("qwen3:4b", Ours));
    }

    [Fact]
    public void What_a_model_needs_follows_its_size_on_disk()
    {
        Assert.Equal(CardState.DefaultNeededMiB, CardState.NeededMiB(null));
        Assert.Equal(CardState.DefaultNeededMiB, CardState.NeededMiB(0));
        Assert.Equal(3097, CardState.NeededMiB(2_497_293_931));
    }

    private sealed class Immediate(Action<PullProgress> report) : IProgress<PullProgress>
    {
        public void Report(PullProgress value) => report(value);
    }
}
