// Prompt playground for the director.
//
//   dotnet run --project tools/Keypuncher.Sandbox -- picks [model] [count]     ask for real things, as the toy does
//   dotnet run --project tools/Keypuncher.Sandbox -- check [model]             score the fact checker on known names
//   dotnet run --project tools/Keypuncher.Sandbox -- recipe [model] "a round sleepy cat"   the Phase 0 prompt
//
// Results are printed as Markdown, so they can go straight into bench-results/.

using System.Diagnostics;
using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;

var mode = args.Length > 0 ? args[0] : "picks";
var model = args.Length > 1 ? args[1] : "qwen3:4b";
var host = new Uri(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } h ? (h.Contains("://") ? h : "http://" + h) : "http://localhost:11434");
var author = new OllamaLanguageModel(host, model);
var checker = new FactChecker(new OllamaLanguageModel(host, model, temperature: 0));

switch (mode)
{
    case "picks":
        await Picks(args.Length > 2 ? int.Parse(args[2]) : 12);
        break;
    case "check":
        await Check();
        break;
    case "recipe":
        await OldRecipe(args.Length > 2 ? args[2] : "a smiling sun");
        break;
    default:
        Console.WriteLine("modes: picks, check, recipe");
        break;
}

// Restyles a fresh set of families the way the toy would, back to back, and prints every attempt.
async Task Picks(int count)
{
    var world = new World();
    var loop = new DirectorLoop(new RealThingDirector(author, checker), new Random(1));
    var director = new RealThingDirector(author, checker);
    Console.WriteLine($"# Director picks, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| # | family | asked | tried | result | fact / checker | s |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
    int used = 0, tries = 0;
    for (var i = 0; i < count; i++)
    {
        var request = loop.NextRequest(world.Families)!;
        var outcome = await director.ProposeAsync(request);
        var asked = request.GrowInto is { } g ? $"grow {request.Current} into {g}" : request.Current is { } c ? $"replace {c}" : $"new {request.Kind}";
        foreach (var a in outcome.Attempts)
        {
            tries++;
            var detail = a.Refused is null ? a.Fact : a.Check is { } k ? $"checker: {k.Verdict}, \"{k.WhatItIs}\"" : a.Fact;
            Console.WriteLine($"| {i + 1} | {request.Slot} | {asked} | {a.Subject} | {a.Refused ?? "used"} | {detail?.Replace("|", "/")} | {outcome.Took.TotalSeconds:0.0} |");
        }
        if (outcome.Recipe is { } r)
        {
            used++;
            world.Families.Restyle(request.Slot, r, i + 1);
        }
    }
    Console.WriteLine();
    Console.WriteLine($"{used} of {count} requests gave a real thing, in {tries} tries.");
    Console.WriteLine("Families now: " + string.Join(", ", world.Families.All.Take(BuiltInRecipes.InventedFamilies).Select(f => f.GrowsInto is { } n ? $"{f.Name} (grows into {n})" : f.Name)));
}

// Real things, including ones a small model might not know, and inventions, including plausible-sounding ones.
async Task Check()
{
    (string Name, RealCategory Category, bool Real)[] cases =
    [
        ("puffin", RealCategory.Animal, true), ("arctic fox", RealCategory.Animal, true), ("axolotl", RealCategory.Animal, true),
        ("hedgehog", RealCategory.Animal, true), ("ladybird", RealCategory.Animal, true), ("komodo dragon", RealCategory.Animal, true),
        ("woolly mammoth", RealCategory.ExtinctAnimal, true), ("dodo", RealCategory.ExtinctAnimal, true),
        ("ankylosaurus", RealCategory.ExtinctAnimal, true), ("great auk", RealCategory.ExtinctAnimal, true),
        ("dandelion", RealCategory.Plant, true), ("birch tree", RealCategory.Plant, true), ("blueberry", RealCategory.Plant, true),
        ("fire engine", RealCategory.Vehicle, true), ("tractor", RealCategory.Vehicle, true), ("teapot", RealCategory.Object, true),
        ("unicorn", RealCategory.Animal, false), ("gruffalo", RealCategory.Animal, false), ("pikachu", RealCategory.Animal, false),
        ("snorlax", RealCategory.Animal, false), ("fluffaroo", RealCategory.Animal, false), ("crocoduck", RealCategory.Animal, false),
        ("glimmerwing moth", RealCategory.Animal, false), ("sparkleraptor", RealCategory.ExtinctAnimal, false),
        ("moonpetal flower", RealCategory.Plant, false), ("bumblebeet", RealCategory.Plant, false),
        ("jabberwock", RealCategory.Animal, false), ("hippogriff", RealCategory.Animal, false),
        ("dodo", RealCategory.Animal, false), // real, but not alive today: the category must be refused
    ];
    Console.WriteLine($"# Fact checker, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| name | said to be | rules | checker | what it is | right? |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
    int right = 0, inventedThrough = 0, realRefused = 0;
    var clock = Stopwatch.StartNew();
    foreach (var (name, category, real) in cases)
    {
        var rule = RealThingRules.NameProblem(name);
        var check = await checker.CheckAsync(name, category, null);
        var passed = rule is null && check.Passed;
        var ok = passed == real;
        if (ok) right++;
        else if (passed) inventedThrough++;
        else realRefused++;
        Console.WriteLine($"| {name} | {category} | {rule ?? "ok"} | {check.Verdict}{(check.RightCategory ? "" : ", wrong category")} | {check.WhatItIs.Replace("|", "/")} | {(ok ? "yes" : "NO")} |");
    }
    Console.WriteLine();
    Console.WriteLine($"{right} of {cases.Length} right; {inventedThrough} inventions got through, {realRefused} real things refused; {clock.Elapsed.TotalSeconds / cases.Length:0.0} s a check.");
}

async Task OldRecipe(string gloss)
{
    var world = new WorldSummary("meadow", "morning", "playful", 0, 0.2, []);
    var result = await author.GenerateJsonAsync(DirectorPrompts.System, DirectorPrompts.NewRecipe(world, gloss), RecipeSchema.Element);
    Console.WriteLine(result.RawText);
    Console.WriteLine();
    if (RecipeValidator.TryParse(result.Json, out var recipe, out var error))
        Console.WriteLine("valid: " + JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = true }));
    else
        Console.WriteLine("rejected: " + error);
    Console.WriteLine($"{result.Timing.OutputTokens} tokens at {result.Timing.OutputTokensPerSecond:0.0} tok/s, total {result.Timing.Total.TotalSeconds:0.0}s");
}
