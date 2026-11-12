// Pictures of real things, outside play: what the toy wants, fetching it, the cut-out models, and a dry run that
// says how much of a world the photo sources cover.
//
//   dotnet run --project tools/Keypuncher.Pictures -- wanted                    the things with no picture yet, and why
//   dotnet run --project tools/Keypuncher.Pictures -- fetch [--until HH:mm] [--limit N] [--again]
//                                                                               fetch a photo for every wanted thing (no GPU, any time)
//   dotnet run --project tools/Keypuncher.Pictures -- dryrun [--into DIR] [--out report.md] [--sets DIR] [--names file [--all]] [--no-download] [--until HH:mm]
//                                                                               every real thing in world.db and the bench sets through the finder
//                                                                               (and the cut-out), into a folder of its own, with a Markdown report;
//                                                                               with --names, only that file's names unless --all is given too
//   dotnet run --project tools/Keypuncher.Pictures -- model                     download the two cut-out models (about 400 MB) and check them
//   dotnet run --project tools/Keypuncher.Pictures -- recut [slug]              cut out again from raw/ (a new model, or new rules)
//   dotnet run --project tools/Keypuncher.Pictures -- hide <slug> | show <slug>  take a picture out of play, or put it back
//   dotnet run --project tools/Keypuncher.Pictures -- things [slug]             each thing's pictures (the one in play and the kept ones, by number)
//                                                                               and what the grown-ups chose for it
//   dotnet run --project tools/Keypuncher.Pictures -- choose <slug> <shape|silhouette|flat|photo> <auto|shape|skip|picture:N>
//                                                                               what is drawn on one rung of a thing's ladder
//   dotnet run --project tools/Keypuncher.Pictures -- choose <slug> climb <auto|ladder|real>
//   dotnet run --project tools/Keypuncher.Pictures -- choose <slug> category <auto|Animal|ExtinctAnimal|Plant|Vehicle|Object>
//   dotnet run --project tools/Keypuncher.Pictures -- generate [--until HH:mm] [--limit N] [--seed N] [--again]
//                                                                               draw a picture with the image model for every thing the
//                                                                               photo sources gave up on, cut it out, and have gemma3 check it
//   dotnet run --project tools/Keypuncher.Pictures -- check [slug] [--again]    show pictures to gemma3; one it fails is taken out of play and
//                                                                               another photo is wanted (put it back with 'show <slug>')
//   dotnet run --project tools/Keypuncher.Pictures -- nightly [--until HH:mm]   fetch, then generate, then check what is unchecked, then fetch
//                                                                               replacements for what the check turned down
//
// Every verb takes --data DIR for the toy's data folder (default KEYPUNCHER_DATA or %LOCALAPPDATA%\Keypuncher).
// generate, check and nightly take the graphics card: they need stable-diffusion.cpp and the Z-Image-Turbo weights
// under --sd DIR (default KEYPUNCHER_SD or <data>\sd; sd-cli.exe in it or sd\, the weights in it or models\), refuse
// to start while the toy runs, and unload whatever Ollama holds first. The other verbs need neither Ollama nor the card.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
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
var finder = new ToyNameFinder(new WikimediaPhotoFinder(http));

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
    case "things":
        return Things(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null);
    case "choose":
        return Choose(args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToArray());
    case "generate":
        return await GenerateAsync();
    case "check":
        return await CheckAsync(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null);
    case "nightly":
        return await NightlyAsync();
    case "probe":
        return await ProbeAsync(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "probe.jsonl");
    default:
        Console.WriteLine("verbs: wanted, fetch, dryrun, model, recut, hide, show, things, choose, generate, check, nightly, probe");
        return 1;
}

async Task<int> GenerateAsync()
{
    if (await CardBusyAsync() is { } busy) { Console.WriteLine(busy); return 2; }
    var setup = SdCliSetup.Find(options.Sd);
    if (!setup.AllPresent) { Console.WriteLine($"Image model not all there under {options.Sd}: missing {string.Join(", ", setup.Missing())}."); return 1; }
    if (!cutOut.IsAvailable) { Console.WriteLine($"No cut-out model in {models.Folder}: run 'model' first."); return 1; }
    var maker = new PictureMaker(folder, new SdCli(setup, note: line => Console.WriteLine("  " + line)), cutOut, Checker(), note: line => Console.WriteLine("  " + line));
    var gaps = folder.Wanted.All().Where(w => options.Again ? !ToyNames.IsInvented(w.Name) : PictureMaker.IsGap(w)).ToList();
    if (options.Limit is { } limit) gaps = gaps.Take(limit).ToList();
    Console.WriteLine($"{gaps.Count} to draw with {SdCliSetup.ModelName}" + (options.Until is { } u ? $"; stops by {u:HH:mm}" : ""));
    var counts = new Dictionary<MakeOutcome, int>();
    var failures = 0;
    foreach (var w in gaps)
    {
        if (options.Until is { } until && DateTime.Now >= until) { Console.WriteLine("stopping: the time is up"); break; }
        Console.WriteLine($"{w.Name} ({w.Category}):");
        var outcome = await maker.MakeAsync(w.Name, w.Category, options.Seed);
        counts[outcome] = counts.GetValueOrDefault(outcome) + 1;
        if (outcome == MakeOutcome.ModelMissing) break;
        if (outcome == MakeOutcome.Failed && ++failures >= 3) { Console.WriteLine("stopping: three failures in a row"); break; }
        if (outcome != MakeOutcome.Failed) failures = 0;
    }
    Console.WriteLine(string.Join(", ", counts.Select(c => $"{c.Value} {c.Key}")));
    return 0;
}

async Task<int> CheckAsync(string? slug)
{
    if (await CardBusyAsync() is { } busy) { Console.WriteLine(busy); return 2; }
    var checker = Checker();
    var checks = new PictureChecks(folder, checker, note: line => Console.WriteLine("  " + line));
    var slugs = slug is { } one ? [one] : folder.All().Where(p => options.Again || PictureChecks.Stale(p.Check)).Select(p => p.Slug).ToList();
    Console.WriteLine($"{slugs.Count} to check with {checker.Model}" + (options.Until is { } u ? $"; stops by {u:HH:mm}" : ""));
    int passed = 0, flagged = 0, silent = 0;
    foreach (var s in slugs)
    {
        if (options.Until is { } until && DateTime.Now >= until) { Console.WriteLine("stopping: the time is up"); break; }
        Console.WriteLine($"{s}:");
        var check = await checks.CheckAsync(s);
        if (check is null) { if (++silent >= 3) { Console.WriteLine("stopping: the checker is not answering"); break; } }
        else if (check.Passed) passed++;
        else flagged++;
    }
    Console.WriteLine($"{passed} look right, {flagged} taken out of play (another photo is wanted for each), {silent} unanswered");
    return 0;
}

// Tries the checker's questions on every picture in play and writes the raw answers, one JSON line a picture, to the
// file given; acts on nothing. For tuning the questions against pictures whose right answer is known.
async Task<int> ProbeAsync(string outPath)
{
    if (await CardBusyAsync() is { } busy) { Console.WriteLine(busy); return 2; }
    var vision = new OllamaVision(new HttpClient { BaseAddress = OllamaHost(), Timeout = TimeSpan.FromMinutes(3) }, options.CheckModel);
    var pictures = folder.All().Where(p => folder.HasPicture(p.Slug)).ToList();
    Console.WriteLine($"{pictures.Count} pictures to probe with {vision.Model} into {outPath}");
    using var output = new StreamWriter(outPath, append: false);
    foreach (var p in pictures)
    {
        var (real, category) = ToyNames.LookUp(p.Name, folder.Cards.Get(p.Slug)?.Category ?? p.Category);
        var png = File.ReadAllBytes(folder.PicturePath(p.Slug));
        var photo = folder.FindRaw(p.Slug) is { } raw ? Shrunk(File.ReadAllBytes(raw)) : null;
        var watch = Stopwatch.StartNew();
        var (sawA, verdictA) = await vision.AskDescribedAsync(OllamaVision.Question(real, category), png, real);
        var (sawB, verdictB) = await vision.AskDescribedAsync(OllamaVision.PlainQuestion(real, category), png, real);
        string? count = null, compare = null;
        if (photo is not null)
        {
            count = await vision.AskRawAsync(OllamaVision.CountQuestion(real), [photo], OllamaVision.CountSchema());
            compare = await vision.AskRawAsync(OllamaVision.Compare(real), [photo, png], OllamaVision.CompareSchema());
        }
        var line = JsonSerializer.Serialize(new { slug = p.Slug, name = real, category = category.ToString(), sawA, verdictA, sawB, verdictB, count, compare, seconds = watch.Elapsed.TotalSeconds });
        await output.WriteLineAsync(line);
        await output.FlushAsync();
        Console.WriteLine($"{p.Slug}: {watch.Elapsed.TotalSeconds:0} s; count {count}");
    }
    return 0;

    static byte[]? Shrunk(byte[] bytes)
    {
        using var photo = PhotoFetcher.Decode(bytes);
        if (photo is null) return null;
        var scale = Math.Min(1.0, PictureChecks.MaxPhotoSide / (double)Math.Max(photo.Width, photo.Height));
        var info = new SkiaSharp.SKImageInfo(Math.Max(1, (int)Math.Round(photo.Width * scale)), Math.Max(1, (int)Math.Round(photo.Height * scale)), SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Unpremul);
        using var small = scale < 1 ? photo.Resize(info, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell)) : photo.Copy();
        using var data = small?.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90);
        return data?.ToArray();
    }
}

async Task<int> NightlyAsync()
{
    // Photos first, over the network only; then the card for the gaps; then the checker over everything new.
    var fetched = await FetchAsync();
    if (options.Until is { } until && DateTime.Now >= until) return fetched;
    var generated = await GenerateAsync();
    if (generated == 2) return 2;
    var checkedOk = await CheckAsync(null);
    if (checkedOk != 0 || options.Until is { } stop && DateTime.Now >= stop) return checkedOk;
    // A picture the checker turned down is wanted again with its photo excluded: fetch its replacement now.
    return await FetchAsync();
}

/// <summary>Why the card cannot be taken now: the toy is running, or Ollama holds a model we could not unload.</summary>
async Task<string?> CardBusyAsync()
{
    if (Process.GetProcessesByName("Keypuncher").Length > 0) return "The toy is running; the card is its. Try again when it is closed.";
    try
    {
        using var ollama = new HttpClient { BaseAddress = OllamaHost(), Timeout = TimeSpan.FromSeconds(30) };
        using var ps = JsonDocument.Parse(await ollama.GetStringAsync("api/ps"));
        if (!ps.RootElement.TryGetProperty("models", out var loaded)) return null;
        foreach (var m in loaded.EnumerateArray())
        {
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null) continue;
            Console.WriteLine($"Ollama holds {name}; asking it to let go.");
            using var _ = await ollama.PostAsJsonAsync("api/generate", new { model = name, keep_alive = 0 });
        }
        return null;
    }
    catch (HttpRequestException) { return null; } // No Ollama: the card is free.
    catch (TaskCanceledException) { return "Ollama is not answering; is it busy with the tournament?"; }
}

IVisionChecker Checker() => new OllamaVision(new HttpClient { BaseAddress = OllamaHost(), Timeout = TimeSpan.FromMinutes(3) }, options.CheckModel);

static Uri OllamaHost() => new(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } h ? (h.Contains("://") ? h : "http://" + h) : "http://localhost:11434");

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

int Things(string? only)
{
    var pictures = folder.All().ToDictionary(p => p.Slug);
    var cards = folder.Cards.All().ToDictionary(c => c.Slug);
    var slugs = only is { } one ? [Slugs.For(one)] : pictures.Keys.Concat(cards.Keys).Distinct().Order(StringComparer.Ordinal).ToList();
    if (slugs.Count == 0) { Console.WriteLine("No pictures and no cards yet."); return 0; }
    foreach (var slug in slugs)
    {
        var picture = pictures.GetValueOrDefault(slug);
        var card = cards.GetValueOrDefault(slug);
        var kept = folder.Kept(slug);
        if (picture is null && card is null && kept.Count == 0) { Console.WriteLine($"{slug}: no such thing"); continue; }
        Console.WriteLine($"{picture?.Name ?? card?.Name ?? slug} [{slug}] ({card?.Category?.ToString() ?? picture?.Category.ToString() ?? "kind unknown"}{(card?.Category is not null ? ", set by a grown-up" : "")})");
        if (picture is not null)
            Console.WriteLine($"  picture {picture.Id}: {(folder.HasPicture(slug) ? "in play" : folder.IsHidden(slug) ? "hidden" : "no cut-out")}, {PictureLine(picture)}");
        foreach (var k in kept) Console.WriteLine($"  picture {k.Id}: kept, {PictureLine(k)}");
        if (card is null) { Console.WriteLine("  no choices: drawn as the toy decides"); continue; }
        Console.WriteLine($"  rungs: shape {card.Rungs.Shape}, silhouette {card.Rungs.Silhouette}, flat {card.Rungs.Flat}, photo {card.Rungs.Photo}; climb {card.Climb.ToString().ToLowerInvariant()}");
        if (card.Words.Count > 0) Console.WriteLine($"  words: {string.Join(", ", card.Words.Select(w => $"{w.Key} {w.Value}"))}");
    }
    return 0;
}

string PictureLine(PictureSidecar p) =>
    p.CreditLine + (p.Photo?.FileTitle is { } title ? $", {title}" : "") + (p.Check is { } check ? $"; {check.Model}: {(check.Passed ? "looks right" : "looks WRONG")}" : "; not checked");

int Choose(string[] words)
{
    const string usage = "choose <slug> <shape|silhouette|flat|photo> <auto|shape|skip|picture:N>, or choose <slug> climb <auto|ladder|real>, or choose <slug> category <auto|kind>";
    if (words.Length != 3) { Console.WriteLine(usage); return 1; }
    // The thing may be given as its name ("Cat", "sable antelope" in quotes) or its slug; cards are kept by slug.
    var (slug, what, value) = (Slugs.For(words[0]), words[1].ToLowerInvariant(), words[2]);
    var name = folder.ReadSidecar(slug)?.Name ?? folder.Kept(slug).FirstOrDefault()?.Name ?? folder.Wanted.Get(slug)?.Name;
    var card = folder.Cards.Get(slug) ?? (name is not null ? new ThingCard(slug, name) : null);
    if (card is null) { Console.WriteLine($"{slug}: no such thing (no picture, kept picture or wanted entry under that slug)"); return 1; }
    switch (what)
    {
        case "climb":
            if (!Enum.TryParse<ClimbChoice>(value, ignoreCase: true, out var climb) || !Enum.IsDefined(climb)) { Console.WriteLine(usage); return 1; }
            card = card with { Climb = climb };
            break;
        case "category":
            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase)) { card = card with { Category = null }; break; }
            if (!Enum.TryParse<RealCategory>(value, ignoreCase: true, out var category) || !Enum.IsDefined(category))
            {
                Console.WriteLine($"kinds: auto, {string.Join(", ", Enum.GetNames<RealCategory>())}");
                return 1;
            }
            card = card with { Category = category };
            break;
        default:
            if (!Enum.TryParse<PictureStage>(what, ignoreCase: true, out var stage) || !Enum.IsDefined(stage) || !RungChoice.TryParse(value, out var choice)) { Console.WriteLine(usage); return 1; }
            if (!RungChoices.Allows(stage, choice)) { Console.WriteLine(stage == PictureStage.Shape ? "the shape rung takes only auto or skip" : "the photo rung takes only auto or picture:N"); return 1; }
            if (choice.Kind == RungKind.Picture && folder.PathOf(slug, choice.Picture) is null)
            {
                Console.WriteLine($"{slug} has no picture {choice.Picture} in play or kept; see 'things {slug}'");
                return 1;
            }
            card = card with { Rungs = card.Rungs.With(stage, choice) };
            break;
    }
    folder.Cards.Save(card, DateTime.Now);
    return Things(slug);
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
    public sealed record Options(string Data, string? Into, string? Out, string? Sets, string? Names, bool All, bool NoDownload, DateTime? Until, int? Limit, bool Again,
        string Sd, long? Seed, string CheckModel)
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
            var sd = Get("sd") ?? Environment.GetEnvironmentVariable("KEYPUNCHER_SD");
            if (string.IsNullOrEmpty(sd)) sd = Path.Combine(data, "sd");
            return new Options(data, Get("into"), Get("out"), Get("sets"), Get("names"), args.Contains("--all"), args.Contains("--no-download"),
                ParseUntil(Get("until"), DateTime.Now), Get("limit") is { } l ? int.Parse(l, CultureInfo.InvariantCulture) : null, args.Contains("--again"),
                sd, Get("seed") is { } seed ? long.Parse(seed, CultureInfo.InvariantCulture) : null, Get("check-model") ?? OllamaVision.DefaultModel);
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
