using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Storage;

namespace Keypuncher.PicturesTool;

/// <summary>
/// Runs every real thing the toy has met (from world.db) and every name in the director's bench sets through the
/// photo finder, and the cut-out unless asked not to, into a folder of its own, then writes a report: how many got
/// a picture, how many a photo that would not cut out, how many nothing, and the gaps by name. That turns "photos
/// cover most things" into a number before any image model is tried.
/// </summary>
public static class DryRun
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    /// <summary>One thing to try, and where its name came from.</summary>
    public sealed record Item(string Name, RealCategory Category, string Source);

    private sealed record CheckerCase(string Name, RealCategory Claimed, bool Real);
    private sealed record HabitatCase(string Name, RealCategory Category);
    private sealed record AuthorCase(RealCategory Category, string? Current, string? GrowInto);

    public static async Task<int> RunAsync(Options options, IPhotoFinder finder, ICutOut cutOut, ModelStore models, TextWriter log)
    {
        var into = options.Into ?? Path.Combine(options.Data, "pictures-dryrun");
        var folder = new PictureFolder(into);
        var items = Gather(options, log);
        if (items.Count == 0) { log.WriteLine("nothing to try: no world.db, no bench sets, no --names file"); return 1; }
        log.WriteLine($"{items.Count} things to try, into {into}"
            + (options.NoDownload ? " (finder only, nothing downloaded)" : $"; cut-out models: {(models.AllPresent ? "both" : models.AnyPresent ? "one" : "none, so photos are kept but not cut out")}")
            + (options.Until is { } u ? $"; stops by {u:HH:mm}" : ""));

        using var http = PictureHttp.NewClient();
        var fetcher = new PhotoFetcher(folder, finder, cutOut, http);
        var rows = new List<(Item Item, string Outcome, string Detail)>();
        var started = DateTime.Now;
        foreach (var item in items)
        {
            if (options.Until is { } until && DateTime.Now >= until) { log.WriteLine("stopping: the time is up"); break; }
            log.Write($"{item.Name} ({item.Category}): ");
            string outcome, detail;
            var slug = Slugs.For(item.Name);
            try
            {
                if (options.NoDownload)
                {
                    var search = await finder.FindAsync(item.Name, item.Category, []);
                    if (search.Offline) { log.WriteLine("offline; stopping"); rows.Add((item, "offline", search.Reason ?? "")); break; }
                    outcome = search.Photo is not null ? "photo found" : "nothing";
                    detail = search.Photo is { } p ? $"{p.Provenance.FileTitle}, {p.Provenance.How}" + (p.Doubt is { } d ? $" (doubt: {d})" : "") : search.Reason ?? "";
                }
                else
                {
                    var result = await fetcher.FetchAsync(item.Name, item.Category);
                    if (result == FetchOutcome.Offline) { log.WriteLine("offline; stopping"); rows.Add((item, "offline", "")); break; }
                    outcome = result switch
                    {
                        FetchOutcome.Pictured or FetchOutcome.AlreadyHave => "pictured",
                        FetchOutcome.RawOnly => "photo, not cut out",
                        _ => "nothing",
                    };
                    var sidecar = folder.ReadSidecar(slug);
                    detail = result switch
                    {
                        FetchOutcome.Pictured or FetchOutcome.AlreadyHave => $"{sidecar?.Photo?.FileTitle}; {sidecar?.CreditLine}; cut by {sidecar?.CutOut}",
                        _ => folder.Wanted.Get(slug)?.Reason ?? "",
                    };
                }
            }
            catch (Exception e)
            {
                outcome = "failed";
                detail = e.GetBaseException().Message;
            }
            rows.Add((item, outcome, detail));
            log.WriteLine($"{outcome}; {detail}");
            await Task.Delay(1000);
        }

        var report = Report(rows, items.Count, started, options.NoDownload);
        var outPath = options.Out ?? Path.Combine(into, "dryrun.md");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        await File.WriteAllTextAsync(outPath, report);
        log.WriteLine();
        log.WriteLine(report.Split('\n').TakeWhile(l => !l.StartsWith("| ")).Aggregate(new StringBuilder(), (sb, l) => sb.AppendLine(l)).ToString());
        log.WriteLine($"Report: {outPath}");
        return 0;
    }

    /// <summary>Every name worth a picture, once each: the world's real things first, then the bench sets, then a
    /// names file (one "name, category" a line).</summary>
    public static List<Item> Gather(Options options, TextWriter log)
    {
        var items = new List<Item>();
        var seen = new HashSet<string>();
        void Add(string? name, RealCategory category, string source)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(Slugs.For(name))) return;
            items.Add(new Item(name.Trim(), category, source));
        }

        var db = Path.Combine(options.Data, "world.db");
        if (File.Exists(db))
        {
            // A copy, so the toy's own file is never opened for writing by anyone but the toy.
            var copy = Path.Combine(Path.GetTempPath(), $"keypuncher-dryrun-{Environment.ProcessId}.db");
            File.Copy(db, copy, overwrite: true);
            try
            {
                var world = new World();
                using (var store = new WorldStore(copy)) store.Load(world);
                foreach (var (_, recipe, _) in world.Families.Restyled())
                {
                    Add(recipe.Name, CategoryOf(recipe), "world, key family");
                    Add(recipe.GrowsInto, RealCategory.Animal, "world, grows into");
                }
                foreach (var e in world.Live.Concat(world.Baked))
                    if (e.Recipe is { } recipe) Add(recipe.Name, CategoryOf(recipe), "world, thing on screen");
                log.WriteLine($"world.db: {items.Count} names");
            }
            finally { TryDelete(copy); TryDelete(copy + "-wal"); TryDelete(copy + "-shm"); }
        }
        else log.WriteLine($"no world.db in {options.Data}");

        var sets = options.Sets ?? FindBenchSets();
        if (sets is not null && Directory.Exists(sets))
        {
            var before = items.Count;
            foreach (var c in Load<CheckerCase>(Path.Combine(sets, "checker.json")).Where(c => c.Real)) Add(c.Name, c.Claimed, "bench, checker set");
            foreach (var h in Load<HabitatCase>(Path.Combine(sets, "habitat.json"))) Add(h.Name, h.Category, "bench, habitat set");
            foreach (var a in Load<AuthorCase>(Path.Combine(sets, "author.json")))
            {
                Add(a.Current, a.Category, "bench, author set");
                Add(a.GrowInto, a.Category, "bench, author set");
            }
            log.WriteLine($"bench sets in {sets}: {items.Count - before} more names");
        }
        else log.WriteLine("no bench sets found (pass --sets DIR)");

        if (options.Names is { } file && File.Exists(file))
        {
            foreach (var line in File.ReadLines(file))
            {
                var parts = line.Split(',', 2, StringSplitOptions.TrimEntries);
                if (parts.Length == 0 || parts[0].Length == 0 || parts[0].StartsWith('#')) continue;
                Add(parts[0], parts.Length > 1 && Enum.TryParse<RealCategory>(parts[1], true, out var c) ? c : RealCategory.Animal, "names file");
            }
        }
        return items;
    }

    private static RealCategory CategoryOf(Recipe recipe) => recipe.Kind switch
    {
        RecipeKind.Creature => RealCategory.Animal,
        RecipeKind.Plant => RealCategory.Plant,
        _ => RealCategory.Object,
    };

    private static string Report(List<(Item Item, string Outcome, string Detail)> rows, int total, DateTime started, bool finderOnly)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Photo dry run, {started:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        var counts = rows.GroupBy(r => r.Outcome).OrderByDescending(g => g.Count()).Select(g => $"{g.Count()} {g.Key}");
        sb.AppendLine($"{rows.Count} of {total} things tried{(finderOnly ? " (finder only)" : "")}: {string.Join(", ", counts)}.");
        var gaps = rows.Where(r => r.Outcome is "nothing" or "photo, not cut out" or "failed").Select(r => r.Item.Name).ToList();
        sb.AppendLine(gaps.Count == 0 ? "No gaps." : $"Gaps ({gaps.Count}): {string.Join(", ", gaps)}.");
        sb.AppendLine();
        sb.AppendLine("| Thing | Category | From | Outcome | Detail |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var (item, outcome, detail) in rows)
            sb.AppendLine($"| {item.Name} | {item.Category} | {item.Source} | {outcome} | {detail.Replace("|", "/").Replace("\n", " ")} |");
        return sb.ToString();
    }

    /// <summary>tools/Keypuncher.Sandbox/bench, looked for upward from the executable and from the current folder.</summary>
    private static string? FindBenchSets()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "Keypuncher.Sandbox", "bench");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static List<T> Load<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Json) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
