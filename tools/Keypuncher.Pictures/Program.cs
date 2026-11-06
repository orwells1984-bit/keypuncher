// Pictures of real things, outside play: what the toy wants, fetching it, the cut-out models, and a dry run that
// says how much of a world the photo sources cover.
//
//   dotnet run --project tools/Keypuncher.Pictures -- wanted                    the things with no picture yet, and why
//   dotnet run --project tools/Keypuncher.Pictures -- fetch [--until HH:mm] [--limit N] [--again]
//                                                                               fetch a photo for every wanted thing (no GPU, any time)
//   dotnet run --project tools/Keypuncher.Pictures -- dryrun [--into DIR] [--out report.md] [--sets DIR] [--names file] [--no-download] [--until HH:mm]
//                                                                               every real thing in world.db and the bench sets through the finder
//                                                                               (and the cut-out), into a folder of its own, with a Markdown report
//   dotnet run --project tools/Keypuncher.Pictures -- model                     download the two cut-out models (about 400 MB) and check them
//   dotnet run --project tools/Keypuncher.Pictures -- recut [slug]              cut out again from raw/ (a new model, or new rules)
//   dotnet run --project tools/Keypuncher.Pictures -- hide <slug> | show <slug>  take a picture out of play, or put it back
//
// Every verb takes --data DIR for the toy's data folder (default KEYPUNCHER_DATA or %LOCALAPPDATA%\Keypuncher).
// Nothing here needs Ollama or the graphics card; the batch that makes pictures with an image model comes later.

using System.Globalization;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.PicturesTool;

var verb = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "wanted";
var options = Options.Parse(args);
var folder = new PictureFolder(Path.Combine(options.Data, "pictures"));
var models = new ModelStore(options.Data);
using var isnet = new OnnxCutOut(OnnxCutOut.ISNet, models.Folder);
using var birefnet = new OnnxCutOut(OnnxCutOut.BiRefNetLite, models.Folder);
var cutOut = new CutOutChain(isnet, birefnet);
using var http = PictureHttp.NewClient();
var finder = new WikimediaPhotoFinder(http);

switch (verb)
{
    case "wanted":
        return Wanted();
    case "fetch":
        return await FetchAsync();
    case "dryrun":
        return await DryRun.RunAsync(options, finder, cutOut, models, Console.Out);
    case "model":
        return await ModelAsync();
    case "recut":
        return Recut(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null);
    case "hide":
    case "show":
        return HideOrShow(verb == "hide", args.Length > 1 ? args[1] : null);
    default:
        Console.WriteLine("verbs: wanted, fetch, dryrun, model, recut, hide, show");
        return 1;
}

int Wanted()
{
    Console.WriteLine($"Pictures in {folder.Root}: {folder.All().Count} ({folder.All().Count(p => p.Hidden)} hidden). Cut-out models: {ModelsLine()}.");
    var wanted = folder.Wanted.All();
    if (wanted.Count == 0) { Console.WriteLine("Nothing wanted."); return 0; }
    Console.WriteLine($"{wanted.Count} wanted:");
    foreach (var w in wanted)
        Console.WriteLine($"  {w.Name} ({w.Category}{(w.CategoryGuessed ? ", guessed" : "")}), since {w.Since:yyyy-MM-dd}"
            + (w.Reason is { } r ? $", {r}" : "") + (w.Tries > 0 ? $", tried {w.Tries}" : "") + (w.Exclude.Count > 0 ? $", not {string.Join("; ", w.Exclude)}" : ""));
    return 0;
}

async Task<int> FetchAsync()
{
    var fetcher = new PhotoFetcher(folder, finder, cutOut, http, note: line => Console.WriteLine("  " + line));
    var wanted = folder.Wanted.All().Where(w => options.Again || w.Reason != "no photo found").ToList();
    if (options.Limit is { } limit) wanted = wanted.Take(limit).ToList();
    Console.WriteLine($"{wanted.Count} to fetch; cut-out models: {ModelsLine()}" + (options.Until is { } u ? $"; stops by {u:HH:mm}" : ""));
    var counts = new Dictionary<FetchOutcome, int>();
    foreach (var w in wanted)
    {
        if (options.Until is { } until && DateTime.Now >= until) { Console.WriteLine("stopping: the time is up"); break; }
        Console.Write($"{w.Name} ({w.Category}): ");
        var outcome = await fetcher.FetchAsync(w.Name, w.Category);
        counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
        Console.WriteLine(Describe(outcome, w.Name));
        if (outcome == FetchOutcome.Offline) { Console.WriteLine("stopping: no network"); break; }
        await Task.Delay(1000);
    }
    Console.WriteLine(string.Join(", ", counts.Select(c => $"{c.Value} {c.Key}")));
    return 0;
}

async Task<int> ModelAsync()
{
    Console.WriteLine($"Models folder: {models.Folder}; {ModelsLine()}.");
    if (models.AllPresent) return 0;
    var progress = new Progress<ModelProgress>(p => Console.Write($"\r  {p.Model.FileName}: {p.Done / 1_000_000} of {p.Total / 1_000_000} MB   "));
    try
    {
        await models.DownloadMissingAsync(progress);
        Console.WriteLine();
        Console.WriteLine($"Done; {ModelsLine()}.");
        return 0;
    }
    catch (Exception e)
    {
        Console.WriteLine();
        Console.WriteLine($"The download did not finish: {e.GetBaseException().Message}");
        return 1;
    }
}

int Recut(string? slug)
{
    if (!cutOut.IsAvailable) { Console.WriteLine($"No cut-out model in {models.Folder}: run 'model' first."); return 1; }
    var fetcher = new PhotoFetcher(folder, finder, cutOut, http, note: line => Console.WriteLine("  " + line));
    var slugs = slug is { } one ? [one] : folder.All().Select(p => p.Slug).Concat(folder.Wanted.All().Select(w => w.Slug)).Distinct().ToList();
    var done = 0;
    foreach (var s in slugs)
    {
        if (folder.FindRaw(s) is null) continue;
        Console.Write($"{s}: ");
        var outcome = fetcher.Recut(s);
        Console.WriteLine(Describe(outcome, s));
        if (outcome == FetchOutcome.Pictured) done++;
    }
    Console.WriteLine($"{done} cut out again.");
    return 0;
}

int HideOrShow(bool hide, string? slug)
{
    if (slug is null) { Console.WriteLine("which picture? give its slug, e.g. sable-antelope"); return 1; }
    if (hide) folder.Hide(slug); else folder.Unhide(slug);
    Console.WriteLine($"{slug}: {(folder.HasPicture(slug) ? "in play" : folder.IsHidden(slug) ? "hidden" : "no such picture")}");
    return 0;
}

string ModelsLine() => models.AllPresent ? "both here" : models.AnyPresent ? $"{string.Join(", ", ModelStore.All.Where(models.IsPresent).Select(m => m.Name))} here, {string.Join(", ", models.Missing.Select(m => m.Name))} missing" : "none downloaded";

string Describe(FetchOutcome outcome, string name)
{
    var slug = Slugs.For(name);
    return outcome switch
    {
        FetchOutcome.Pictured => $"pictured ({folder.ReadSidecar(slug)?.CreditLine})",
        FetchOutcome.AlreadyHave => "already has a picture",
        FetchOutcome.RawOnly => $"photo kept, not cut out ({folder.Wanted.Get(slug)?.Reason})",
        FetchOutcome.NothingSuitable => $"nothing suitable ({folder.Wanted.Get(slug)?.Reason})",
        FetchOutcome.Offline => "offline",
        _ => outcome.ToString(),
    };
}

namespace Keypuncher.PicturesTool
{
    /// <summary>The command line, shared by the verbs.</summary>
    public sealed record Options(string Data, string? Into, string? Out, string? Sets, string? Names, bool NoDownload, DateTime? Until, int? Limit, bool Again)
    {
        public static Options Parse(string[] args)
        {
            string? Get(string name)
            {
                var i = Array.IndexOf(args, "--" + name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            var data = Get("data") ?? Environment.GetEnvironmentVariable("KEYPUNCHER_DATA");
            if (string.IsNullOrEmpty(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keypuncher");
            return new Options(data, Get("into"), Get("out"), Get("sets"), Get("names"), args.Contains("--no-download"),
                ParseUntil(Get("until"), DateTime.Now), Get("limit") is { } l ? int.Parse(l, CultureInfo.InvariantCulture) : null, args.Contains("--again"));
        }

        /// <summary>The clock time to stop by, "HH:mm": today if still ahead, else tomorrow.</summary>
        public static DateTime? ParseUntil(string? text, DateTime now)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var at = now.Date + TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture).ToTimeSpan();
            return at > now ? at : at.AddDays(1);
        }
    }
}
