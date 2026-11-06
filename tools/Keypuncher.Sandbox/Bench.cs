// The reliability bench: the same labelled requests, every model and prompt variant, one CSV row a call.
//
//   dotnet run --project tools/Keypuncher.Sandbox -- bench --models qwen3:4b,gemma3:4b --variants baseline,A1,A4
//       --sets author,checker,habitat,scene --runs 3 --temperature 0.8 --think --out bench-results --until 04:15 --judge qwen3:4b
//
// --judge names one model that judges every model's author picks, so that "checker agrees" compares authors and not
// each model's own checking; without it each model checks its own picks, as it would in the toy. The judge runs
// after each author pass, so the two models swap once a pass rather than twice a call.
//
// It refuses to start while another job's model is loaded (the nightly tournament), and --until HH:mm stops it
// cleanly, summary written, before the tournament's hour: a run that starts at noon has until about 04:30.
//
// The sets live in tools/Keypuncher.Sandbox/bench/*.json and are copied next to the executable. The author set is
// scored on the author's first answer alone (no retries, no checker), so what it measures is how often the model
// gives a usable pick when asked once, then the checker says whether the pick really is the kind asked for (the
// schema forces the category word, so "gazelle" as a vehicle passes the rules); the checker and habitat sets are scored against hand-written labels; the
// scene set runs the scene director whole, place check included.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;

namespace Keypuncher.Sandbox;

public static class Bench
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public sealed record AuthorCase(string Id, RecipeKind Kind, RealCategory Category, IReadOnlyList<string> Existing,
        string? Place = null, string? Here = null, string? Current = null, string? GrowInto = null, string? MustBe = null)
    {
        public DirectorRequest Request => new(0, Kind, Current, Existing, GrowInto, Category, Place, MustBe, Here);
    }

    public sealed record CheckerCase(string Name, RealCategory Claimed, bool Real, string? Why = null);
    public sealed record HabitatCase(string Name, RealCategory Category, string Here, bool Found);
    public sealed record SceneCase(string Id, string Place, SceneChange Change, int Month, TimeOfDay TimeOfDay, Weather Weather,
        IReadOnlyList<string> Recent, IReadOnlyList<string> Things);

    /// <summary>One call's row. <see cref="Agrees"/> is null where there is no label (the author set), else whether
    /// the model agreed with the hand-written label.</summary>
    public sealed record Row(string Model, string Variant, string Set, string RequestId, int Run, int Try, string Answer, string Outcome,
        bool ValidJson, bool Passed, bool? Agrees, double TotalMs, double LoadMs, int PromptTokens, int OutputTokens, string Raw);

    public sealed record Options(Uri Host, IReadOnlyList<string> Models, IReadOnlyList<PromptVariant> Variants, IReadOnlyList<string> Sets,
        int Runs, float Temperature, bool Think, string OutDir, string SetsDir, DateTime? Until, bool Force, IReadOnlyList<string> OtherJobs, string? Judge)
    {
        /// <summary>The clock time to stop by, "HH:mm": today if still ahead, else tomorrow.</summary>
        public static DateTime? ParseUntil(string? text, DateTime now)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var t = TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
            var at = now.Date + t.ToTimeSpan();
            return at > now ? at : at.AddDays(1);
        }

        public static Options Parse(string[] args, Uri host)
        {
            string Get(string name, string fallback)
            {
                var i = Array.IndexOf(args, "--" + name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            var sets = Path.Combine(AppContext.BaseDirectory, "bench");
            if (!Directory.Exists(sets)) sets = Path.Combine("tools", "Keypuncher.Sandbox", "bench");
            return new Options(
                host,
                Get("models", "qwen3:4b").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                [.. Get("variants", "baseline").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(PromptVariant.Parse)],
                Get("sets", "author,checker,habitat,scene").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                int.Parse(Get("runs", "1"), CultureInfo.InvariantCulture),
                float.Parse(Get("temperature", "0.8"), CultureInfo.InvariantCulture),
                args.Contains("--think"),
                Get("out", "bench-results"),
                Get("sets-dir", sets),
                ParseUntil(Get("until", ""), DateTime.Now),
                args.Contains("--force"),
                Get("other-jobs", "laguna,qwen3-coder").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Get("judge", "") is { Length: > 0 } judge ? judge : null);
        }

        /// <summary>The variant's name in the CSV, with the model settings that are not the toy's own.</summary>
        public string Label(PromptVariant v) => v.Name + (Temperature == 0.8f ? "" : $"+t{Temperature.ToString("0.0", CultureInfo.InvariantCulture)}") + (Think ? "+think" : "");
    }

    public static async Task<int> RunAsync(string[] args, Uri host)
    {
        var opts = Options.Parse(args, host);
        var authorSet = Load<AuthorCase>(opts, "author.json");
        var checkerSet = Load<CheckerCase>(opts, "checker.json");
        var habitatSet = Load<HabitatCase>(opts, "habitat.json");
        var sceneSet = Load<SceneCase>(opts, "scenes.json");
        Console.WriteLine($"bench: {opts.Models.Count} model(s), {opts.Variants.Count} variant(s), sets {string.Join(",", opts.Sets)}, {opts.Runs} run(s); "
            + $"{authorSet.Count} author, {checkerSet.Count} checker, {habitatSet.Count} habitat, {sceneSet.Count} scene cases"
            + (opts.Until is { } until ? $"; stops by {until:yyyy-MM-dd HH:mm}" : ""));

        // Another job's model in memory means its requests are queued or running; with one model loaded at a time the
        // bench and the job would swap and both crawl, as the toy did under the tournament on 3 Nov. Only the other
        // jobs' models count (--other-jobs, name prefixes): the bench's own last model stays loaded for 30 minutes.
        var busy = (await new OllamaLanguageModel(host, opts.Models[0]).LoadedModelsAsync())
            .Where(n => opts.OtherJobs.Any(o => n.StartsWith(o, StringComparison.OrdinalIgnoreCase))).ToList();
        if (busy.Count > 0 && !opts.Force)
        {
            Console.WriteLine($"not starting: Ollama has {string.Join(", ", busy)} loaded for another job (ollama ps). Wait, or pass --force.");
            return 2;
        }

        Directory.CreateDirectory(opts.OutDir);
        var stamp = $"{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmm}";
        var csvPath = Path.Combine(opts.OutDir, stamp + "-bench.csv");
        await using var csv = new StreamWriter(csvPath, append: false, Encoding.UTF8);
        await csv.WriteLineAsync("model,variant,set,request_id,run,try,answer,outcome,valid_json,passed,agrees,total_ms,load_ms,prompt_tokens,output_tokens,raw_json");
        var rows = new List<Row>();
        var loads = new List<(string Model, double ColdLoadS, long Size, long SizeVram)>();
        var stopped = false;
        var judge = opts.Judge is { } j ? new FactChecker(new OllamaLanguageModel(opts.Host, j, temperature: 0, think: opts.Think)) : null;

        foreach (var model in opts.Models)
        {
            var probe = new OllamaLanguageModel(opts.Host, model);
            var local = await probe.ListLocalModelsAsync();
            if (!local.Any(n => n == model || n == model + ":latest"))
            {
                Console.WriteLine($"skip {model}: not pulled (ollama pull {model})");
                continue;
            }

            Console.WriteLine($"\n== {model} ==");
            await probe.UnloadAsync();
            var cold = await probe.WarmUpAsync();
            var size = await probe.LoadedSizeAsync();
            Console.WriteLine($"cold load {cold.TotalSeconds:0.0} s; {Gb(size?.Size)} loaded, {Gb(size?.SizeVram)} of it on the GPU"
                + (size is { } s && s.SizeVram < s.Size ? "  <-- does not fit, partly on the CPU" : ""));

            foreach (var variant in opts.Variants)
            {
                var label = opts.Label(variant);
                var author = new OllamaLanguageModel(opts.Host, model, temperature: opts.Temperature, think: opts.Think);
                var coldModel = new OllamaLanguageModel(opts.Host, model, temperature: 0, think: opts.Think);
                var checker = new FactChecker(coldModel, variant);
                var placeChecker = new PlaceChecker(coldModel);
                Console.WriteLine($"-- {label} --");

                for (var run = 1; run <= opts.Runs && !stopped; run++)
                {
                    if (opts.Sets.Contains("author"))
                    {
                        var pending = new List<(Row Row, RealThingPick Pick)>();
                        foreach (var c in authorSet)
                        {
                            if (OutOfTime()) break;
                            var (row, pick) = await AuthorAsync(author, judge is null ? checker : null, variant, c, model, label, run);
                            if (pick is null) Keep(row); else pending.Add((row, pick));
                        }
                        foreach (var (row, pick) in pending) Keep(await JudgeAsync(judge!, row, pick));
                    }
                    // The checker runs cold (temperature 0), so its answers do not change between runs: once is enough.
                    if (opts.Sets.Contains("checker") && run == 1)
                        foreach (var c in checkerSet) { if (OutOfTime()) break; Keep(await CheckerAsync(checker, c, model, label, run)); }
                    if (opts.Sets.Contains("habitat") && run == 1)
                        foreach (var c in habitatSet) { if (OutOfTime()) break; Keep(await HabitatAsync(coldModel, c, model, label, run)); }
                    if (opts.Sets.Contains("scene"))
                        foreach (var c in sceneSet) { if (OutOfTime()) break; foreach (var row in await SceneAsync(author, placeChecker, c, model, label, run)) Keep(row); }
                }
                if (stopped) break;
            }

            // Measured again after real calls, which load the model with the toy's context size; the figure after the
            // warm-up alone has differed from it.
            var steady = await probe.LoadedSizeAsync() ?? size;
            loads.Add((model, cold.TotalSeconds, steady?.Size ?? 0, steady?.SizeVram ?? 0));
            if (stopped) break;
        }

        var summary = Summary(rows, loads, opts, stopped);
        var mdPath = Path.Combine(opts.OutDir, stamp + "-bench.md");
        await File.WriteAllTextAsync(mdPath, summary);
        Console.WriteLine("\n" + summary);
        Console.WriteLine($"rows: {csvPath}\nsummary: {mdPath}");
        if (stopped) Console.WriteLine($"stopped at {DateTime.Now:HH:mm} as asked (--until); the rows so far are in the CSV.");
        return rows.Count == 0 ? 1 : 0;

        bool OutOfTime()
        {
            if (opts.Until is { } until && DateTime.Now >= until) stopped = true;
            return stopped;
        }

        void Keep(Row row)
        {
            rows.Add(row);
            csv.WriteLine(Csv(row));
            csv.Flush();
            Console.WriteLine($"  {row.Set} {row.RequestId} run {row.Run}: {row.Answer} -> {row.Outcome}{(row.Agrees is { } a ? (a ? " (right)" : " (WRONG)") : "")} {row.TotalMs / 1000:0.0}s");
        }
    }

    private static List<T> Load<T>(Options opts, string file)
    {
        var path = Path.Combine(opts.SetsDir, file);
        if (!File.Exists(path)) throw new FileNotFoundException($"bench set missing: {path}");
        return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? [];
    }

    /// <summary>Words a pick's name must not carry: the author folds the place into the name ("icelandic potato",
    /// "wadi rum gazelle") when asked for something that lives there, and the checker then knows no such kind.</summary>
    private static readonly string[] PlaceWords =
    [
        "icelandic", "iceland", "slovenian", "slovenia", "bulgarian", "bulgaria", "jordanian", "jordan", "african", "africa",
        "australian", "english", "lake", "bled", "wadi", "rum", "kruger", "hallormsstadaskogur", "reynisfjara", "savanna", "desert",
    ];

    /// <summary>One author call. Passed means the rules let the pick through, as the toy would send it to the checker;
    /// a name that carries the place fails too, since the checker always turns those away. Agrees is then the
    /// checker's word on whether it is real and the kind asked for: the schema holds the category to the one asked,
    /// so a "gazelle" offered as a vehicle passes the rules and only the checker catches it. The timing is the
    /// author's call alone. With no <paramref name="checker"/> the pick comes back for a judge to look at later.
    /// An extinct-animal request carries the well-known names the toy offers on every try.</summary>
    private static async Task<(Row Row, RealThingPick? Pending)> AuthorAsync(OllamaLanguageModel author, FactChecker? checker, PromptVariant variant, AuthorCase c, string model, string label, int run)
    {
        var request = c.Request;
        var suggestions = request.Category == RealCategory.ExtinctAnimal && request.GrowInto is null && request.MustBe is null
            ? RealThingPick.WellKnownExtinct.Where(n => !request.Existing.Any(e => RealThingRules.SameThing(e, n))).OrderBy(_ => new Random(c.Id.GetHashCode() + run).Next()).Take(5).ToList()
            : null;
        var (system, user, schema) = DirectorPrompts.Author(request, [], suggestions, variant);
        var clock = Stopwatch.StartNew();
        var result = await author.GenerateJsonAsync(system, user, schema);
        clock.Stop();
        var ok = RealThingRules.TryBuild(result.Json, request, out var pick, out _, out var error);
        var subject = pick?.Subject ?? "?";
        var outcome = ok ? "ok" : error ?? "?";
        if (ok && subject.Split(' ', '-').Any(w => PlaceWords.Contains(w))) { ok = false; outcome = "place in the name"; }
        var row = new Row(model, label, "author", c.Id, run, 1, subject, outcome, result.Json is not null, ok, null,
            Ms(result, clock), result.Timing.Load.TotalMilliseconds, result.Timing.PromptTokens, result.Timing.OutputTokens, result.RawText);
        if (!ok) return (row, null);
        if (checker is null) return (row, pick);
        return (await JudgeAsync(checker, row, pick!), null);
    }

    /// <summary>The checker's word on a pick that passed the rules: Agrees, and why not in Outcome.</summary>
    private static async Task<Row> JudgeAsync(FactChecker checker, Row row, RealThingPick pick)
    {
        var check = await checker.CheckAsync(pick.Subject, pick.GrowsInto, pick.Category);
        var agrees = check.Passes(pick.Category);
        return row with
        {
            Agrees = agrees,
            Outcome = agrees ? row.Outcome : check.Verdict == "real" ? $"checker: {check.Kind}, not {pick.Category}" : $"checker: {check.Verdict}",
        };
    }

    /// <summary>The checker on one name, against its label. The model is called through the checker, so the row's
    /// timing is the checker's whole cost (two calls where the baseline takes a second look).</summary>
    private static async Task<Row> CheckerAsync(FactChecker checker, CheckerCase c, string model, string label, int run)
    {
        var clock = Stopwatch.StartNew();
        var check = await checker.CheckAsync(c.Name, null, c.Claimed);
        clock.Stop();
        var passed = check.Passes(c.Claimed);
        var outcome = $"{check.Verdict}, {check.Kind}: \"{check.WhatItIs}\"";
        return new Row(model, label, "checker", c.Name + "/" + c.Claimed, run, 1, c.Name, outcome, check.WhatItIs != "checker gave no JSON" && check.WhatItIs != "checker gave bad JSON",
            passed, passed == c.Real, clock.Elapsed.TotalMilliseconds, 0, 0, 0, JsonSerializer.Serialize(check));
    }

    private static async Task<Row> HabitatAsync(OllamaLanguageModel cold, HabitatCase c, string model, string label, int run)
    {
        var clock = Stopwatch.StartNew();
        var result = await cold.GenerateJsonAsync(FactChecker.System, FactChecker.FitPrompt(c.Name, c.Category, c.Here), JsonDocument.Parse(FactChecker.FitSchemaJson).RootElement.Clone());
        clock.Stop();
        var fits = FactChecker.Fits(result.Json?.RootElement);
        return new Row(model, label, "habitat", c.Name + " " + c.Here, run, 1, c.Name, fits ? "found there" : "not found there", result.Json is not null,
            fits, fits == c.Found, Ms(result, clock), result.Timing.Load.TotalMilliseconds, result.Timing.PromptTokens, result.Timing.OutputTokens, result.RawText);
    }

    /// <summary>The scene director whole, one row an attempt: accepted or why not. The place must be one of
    /// <see cref="KnownPlace.All"/>, so its climate and hemisphere are known as they would be in play.</summary>
    private static async Task<IReadOnlyList<Row>> SceneAsync(OllamaLanguageModel author, PlaceChecker placeChecker, SceneCase c, string model, string label, int run)
    {
        var known = KnownPlace.All.FirstOrDefault(p => p.Name == c.Place) ?? throw new InvalidOperationException($"scene case {c.Id}: {c.Place} is not a known place");
        var scene = new Scene(known.Landscape, c.TimeOfDay, c.Weather, known.Name, known.Fact, known.Climate, known.South);
        List<Weather>? offer = c.Change == SceneChange.Weather ? [.. SceneRequest.Changes(scene, c.Month, false).Take(DirectorLoop.WeathersOffered)] : null;
        var request = new SceneRequest(scene, c.Recent, c.Things, c.Change, Month: c.Month, Offer: offer);
        var director = new SceneDirector(author, placeChecker);
        var outcome = await director.ProposeAsync(request);
        var rows = new List<Row>();
        var i = 0;
        foreach (var a in outcome.Attempts)
        {
            i++;
            var used = a.Refused is null;
            rows.Add(new Row(model, label, "scene", c.Id, run, i, a.Tried, a.Refused ?? "used", a.Refused is not "not JSON", used, used ? true : i == outcome.Attempts.Count ? false : (bool?)null,
                outcome.Took.TotalMilliseconds / outcome.Attempts.Count, 0, 0, 0, a.Check is { } k ? JsonSerializer.Serialize(k, Json) : ""));
        }
        if (rows.Count == 0)
            rows.Add(new Row(model, label, "scene", c.Id, run, 1, "?", "no attempt", false, false, false, outcome.Took.TotalMilliseconds, 0, 0, 0, ""));
        return rows;
    }

    private static double Ms(Keypuncher.Inference.GenerationResult result, Stopwatch clock) =>
        result.Timing.Total > TimeSpan.Zero ? result.Timing.Total.TotalMilliseconds : clock.Elapsed.TotalMilliseconds;

    private static string Gb(long? bytes) => bytes is { } b and > 0 ? $"{b / 1_073_741_824.0:0.0} GB" : "?";

    private static string Csv(Row r)
    {
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        var inv = CultureInfo.InvariantCulture;
        return string.Join(",", Q(r.Model), Q(r.Variant), r.Set, Q(r.RequestId), r.Run.ToString(inv), r.Try.ToString(inv), Q(r.Answer), Q(r.Outcome),
            r.ValidJson ? "1" : "0", r.Passed ? "1" : "0", r.Agrees is { } a ? (a ? "1" : "0") : "",
            r.TotalMs.ToString("0", inv), r.LoadMs.ToString("0", inv), r.PromptTokens.ToString(inv), r.OutputTokens.ToString(inv), Q(r.Raw));
    }

    /// <summary>One table a set, a row a model and variant.</summary>
    public static string Summary(IReadOnlyList<Row> rows, IReadOnlyList<(string Model, double ColdLoadS, long Size, long SizeVram)> loads, Options opts, bool stopped = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Director bench, {Environment.MachineName}, {DateTime.Now:yyyy-MM-dd HH:mm}{(stopped ? " (STOPPED EARLY)" : "")}");
        sb.AppendLine();
        if (stopped)
        {
            // The cases run in a fixed order (the real names first in the checker set), so a cut-off run looks better than it is.
            sb.AppendLine($"**Stopped at {DateTime.Now:HH:mm} by --until before every case ran. The figures below are for part of each set, in set order, and do not compare with a full run.**");
            sb.AppendLine();
        }
        sb.AppendLine($"Models {string.Join(", ", opts.Models)}; variants {string.Join(", ", opts.Variants.Select(opts.Label))}; {opts.Runs} run(s) a case "
            + "(the checker and habitat sets once: at temperature 0 the answers do not change)."
            + (opts.Judge is { } judge ? $" Author picks judged by {judge}." : " Each model judges its own author picks."));
        sb.AppendLine();
        if (loads.Count > 0)
        {
            sb.AppendLine("| model | cold load s | loaded | on GPU | fits 6 GB? |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var l in loads)
                sb.AppendLine($"| {l.Model} | {l.ColdLoadS:0.0} | {Gb(l.Size)} | {Gb(l.SizeVram)} | {(l.Size > 0 && l.SizeVram >= l.Size ? "yes" : l.Size > 0 ? "NO, partly on the CPU" : "?")} |");
            sb.AppendLine();
        }
        foreach (var set in rows.Select(r => r.Set).Distinct())
        {
            sb.AppendLine($"## {set}");
            sb.AppendLine();
            var labelled = set != "author" && rows.Any(r => r.Set == set && r.Agrees is not null);
            sb.AppendLine(set == "author"
                ? "| model | variant | calls | valid JSON | passed rules | checker agrees | distinct names | median s | p90 s |"
                : set == "scene"
                ? "| model | variant | requests | accepted | tries a request | median s a try |"
                : "| model | variant | calls | valid JSON | said yes | right | real kept | not-real let through | median s | p90 s |");
            sb.AppendLine(set == "author" ? "| --- | --- | --- | --- | --- | --- | --- | --- | --- |" : set == "scene" ? "| --- | --- | --- | --- | --- | --- |" : "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var g in rows.Where(r => r.Set == set).GroupBy(r => (r.Model, r.Variant)))
            {
                var list = g.ToList();
                var ms = list.Select(r => r.TotalMs).OrderBy(x => x).ToList();
                string Pct(Func<Row, bool> f, IReadOnlyList<Row>? of = null) => (of ?? list).Count == 0 ? "-" : $"{100.0 * (of ?? list).Count(f) / (of ?? list).Count:0}%";
                if (set == "author")
                {
                    var distinct = list.Where(r => r.Passed).GroupBy(r => r.RequestId).Select(x => x.Select(r => r.Answer).Distinct().Count()).DefaultIfEmpty(0).Average();
                    sb.AppendLine($"| {g.Key.Model} | {g.Key.Variant} | {list.Count} | {Pct(r => r.ValidJson)} | {Pct(r => r.Passed)} | {Pct(r => r.Agrees == true)} | {distinct:0.0} of {opts.Runs} | {Median(ms) / 1000:0.0} | {P90(ms) / 1000:0.0} |");
                }
                else if (set == "scene")
                {
                    var requests = list.GroupBy(r => (r.RequestId, r.Run)).ToList();
                    sb.AppendLine($"| {g.Key.Model} | {g.Key.Variant} | {requests.Count} | {100.0 * requests.Count(q => q.Any(r => r.Passed)) / Math.Max(1, requests.Count):0}% | {(double)list.Count / Math.Max(1, requests.Count):0.0} | {Median(ms) / 1000:0.0} |");
                }
                else
                {
                    // "real kept": labelled-true cases the model passed; "let through": labelled-false cases it passed.
                    var shouldPass = list.Where(r => (r.Agrees == true && r.Passed) || (r.Agrees == false && !r.Passed)).ToList();
                    var shouldFail = list.Where(r => (r.Agrees == true && !r.Passed) || (r.Agrees == false && r.Passed)).ToList();
                    sb.AppendLine($"| {g.Key.Model} | {g.Key.Variant} | {list.Count} | {Pct(r => r.ValidJson)} | {Pct(r => r.Passed)} | {Pct(r => r.Agrees == true)} | {Pct(r => r.Passed, shouldPass)} | {Pct(r => r.Passed, shouldFail)} | {Median(ms) / 1000:0.0} | {P90(ms) / 1000:0.0} |");
                }
            }
            sb.AppendLine();
            if (labelled)
            {
                var wrong = rows.Where(r => r.Set == set && r.Agrees == false).GroupBy(r => r.RequestId).OrderByDescending(x => x.Count()).Take(25).ToList();
                if (wrong.Count > 0)
                {
                    sb.AppendLine($"Most often wrong: {string.Join("; ", wrong.Select(w => $"{w.Key} ({w.Count()}x: {w.First().Outcome.Replace("|", "/")})"))}.");
                    sb.AppendLine();
                }
            }
            if (set == "author")
            {
                var failures = rows.Where(r => r.Set == set && r.Agrees != true).GroupBy(r => r.Outcome).OrderByDescending(x => x.Count()).ToList();
                if (failures.Count > 0) sb.AppendLine($"Why picks failed: {string.Join("; ", failures.Select(f => $"{f.Key} {f.Count()}x"))}.");
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    private static double P90(IReadOnlyList<double> sorted) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.9))];
}
