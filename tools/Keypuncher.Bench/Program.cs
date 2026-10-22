// Phase 0 benchmark: how fast does each candidate model write a valid recipe on this PC?
//
//   dotnet run --project tools/Keypuncher.Bench -- --models gemma3:4b,qwen3:4b,llama3.1:8b --runs 5
//
// Options: --url http://localhost:11434   --ctx 4096   --out bench-results
// Models must already be pulled (`ollama pull gemma3:4b`); missing ones are skipped with a note.

using System.Diagnostics;
using System.Globalization;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;

var opts = Options.Parse(args);
var world = new WorldSummary("meadow", "dusk", "calm", 3, 0.5, ["puffbird", "dottree"]);
var glosses = new[] { "a smiling sun", "a round sleepy cat", "a small bright fish", "a plump little bird", "a soft crescent moon" };

var results = new List<ModelResult>();
foreach (var model in opts.Models)
{
    var lm = new OllamaLanguageModel(opts.Url, model, opts.Ctx);
    var local = await lm.ListLocalModelsAsync();
    if (!local.Any(n => n == model || n.StartsWith(model + ":", StringComparison.Ordinal)))
    {
        Console.WriteLine($"skip {model}: not pulled (ollama pull {model})");
        continue;
    }

    Console.WriteLine($"\n== {model} ==");
    // Warm-up loads the model into VRAM; its load time is reported separately.
    var warm = await lm.GenerateJsonAsync(DirectorPrompts.System, DirectorPrompts.NewRecipe(world, glosses[0]), RecipeSchema.Element);
    Console.WriteLine($"load {warm.Timing.Load.TotalSeconds:0.0}s");

    var runs = new List<RunResult>();
    for (var i = 0; i < opts.Runs; i++)
    {
        var sw = Stopwatch.StartNew();
        var r = await lm.GenerateJsonAsync(DirectorPrompts.System, DirectorPrompts.NewRecipe(world, glosses[i % glosses.Length]), RecipeSchema.Element);
        sw.Stop();
        var ok = RecipeValidator.TryParse(r.Json, out var recipe, out var error);
        runs.Add(new RunResult(sw.Elapsed, r.Timing.OutputTokensPerSecond, r.Timing.PromptEval, r.Timing.OutputTokens, ok));
        Console.WriteLine($"  run {i + 1}: {sw.Elapsed.TotalSeconds,5:0.0}s  {r.Timing.OutputTokensPerSecond,5:0.0} tok/s  {r.Timing.OutputTokens,4} tok  {(ok ? recipe!.Name : "REJECTED " + error)}");
    }
    results.Add(new ModelResult(model, warm.Timing.Load, runs));
}

if (results.Count == 0) { Console.WriteLine("nothing benchmarked"); return 1; }

var report = Report.Markdown(results, opts);
Console.WriteLine("\n" + report);
Directory.CreateDirectory(opts.OutDir);
var path = Path.Combine(opts.OutDir, $"{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}.md");
File.WriteAllText(path, report);
Console.WriteLine($"written to {path}");
return 0;

sealed record RunResult(TimeSpan WallClock, double TokensPerSecond, TimeSpan PromptEval, int OutputTokens, bool Valid);
sealed record ModelResult(string Model, TimeSpan Load, IReadOnlyList<RunResult> Runs);

sealed record Options(Uri Url, string[] Models, int Runs, int Ctx, string OutDir)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback)
        {
            var i = Array.IndexOf(args, "--" + name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        return new Options(
            new Uri(Get("url", "http://localhost:11434")),
            Get("models", "gemma3:4b,qwen3:4b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            int.Parse(Get("runs", "5"), CultureInfo.InvariantCulture),
            int.Parse(Get("ctx", "4096"), CultureInfo.InvariantCulture),
            Get("out", "bench-results"));
    }
}

static class Report
{
    public static string Markdown(IReadOnlyList<ModelResult> results, Options opts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Keypuncher bench, {Environment.MachineName}, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{Gpu()}  ctx={opts.Ctx}  runs={opts.Runs}");
        sb.AppendLine();
        sb.AppendLine("| Model | Load s | Median wall s | Median tok/s | Prompt eval s | Valid recipes |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var r in results)
        {
            var wall = Median(r.Runs.Select(x => x.WallClock.TotalSeconds));
            var tps = Median(r.Runs.Select(x => x.TokensPerSecond));
            var pe = Median(r.Runs.Select(x => x.PromptEval.TotalSeconds));
            var valid = r.Runs.Count(x => x.Valid);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Model} | {r.Load.TotalSeconds:0.0} | {wall:0.0} | {tps:0.0} | {pe:0.00} | {valid}/{r.Runs.Count} |");
        }
        sb.AppendLine();
        sb.AppendLine("Gate for Phase 0: a valid recipe in under 5 s median on the desktop.");
        return sb.ToString();
    }

    static double Median(IEnumerable<double> xs)
    {
        var s = xs.OrderBy(x => x).ToArray();
        return s.Length == 0 ? 0 : s.Length % 2 == 1 ? s[s.Length / 2] : (s[s.Length / 2 - 1] + s[s.Length / 2]) / 2;
    }

    static string Gpu()
    {
        try
        {
            var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=name,memory.total,driver_version --format=csv,noheader")
            { RedirectStandardOutput = true, UseShellExecute = false });
            if (p is null) return "gpu: unknown";
            var line = p.StandardOutput.ReadLine();
            p.WaitForExit(2000);
            return "gpu: " + (line ?? "unknown");
        }
        catch (Exception) { return "gpu: none reported"; }
    }
}
