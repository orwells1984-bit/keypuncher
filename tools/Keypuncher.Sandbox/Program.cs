// Prompt playground for the director.
//
//   dotnet run --project tools/Keypuncher.Sandbox -- picks [model] [count]     ask for real things, as the toy does
//   dotnet run --project tools/Keypuncher.Sandbox -- extinct [model] [count]   only extinct animals, in creature families
//   dotnet run --project tools/Keypuncher.Sandbox -- scenes [model] [count]    walk the scene through real places, weather and time
//   dotnet run --project tools/Keypuncher.Sandbox -- places [model]            score the place checker on known places
//   dotnet run --project tools/Keypuncher.Sandbox -- facts [model]             score the place-fact check on true and false sentences
//   dotnet run --project tools/Keypuncher.Sandbox -- fits [model]              score ways of asking whether a thing is found at a place
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
var cold = new OllamaLanguageModel(host, model, temperature: 0);
var checker = new FactChecker(cold);
var placeChecker = new PlaceChecker(cold);

switch (mode)
{
    case "picks":
        await Picks(args.Length > 2 ? int.Parse(args[2]) : 12);
        break;
    case "extinct":
        await Picks(args.Length > 2 ? int.Parse(args[2]) : 12, extinctOnly: true);
        break;
    case "check":
        await Check();
        break;
    case "scenes":
        await Scenes(args.Length > 2 ? int.Parse(args[2]) : 12);
        break;
    case "places":
        await Places();
        break;
    case "facts":
        await Facts();
        break;
    case "fits":
        await Fits();
        break;
    case "recipe":
        await OldRecipe(args.Length > 2 ? args[2] : "a smiling sun");
        break;
    default:
        Console.WriteLine("modes: picks, extinct, scenes, places, facts, fits, check, recipe");
        break;
}

// Restyles a fresh set of families the way the toy would, back to back, and prints every attempt.
async Task Picks(int count, bool extinctOnly = false)
{
    var world = new World();
    var random = new Random(Environment.TickCount);
    var loop = new DirectorLoop(new RealThingDirector(author, checker), random);
    var director = new RealThingDirector(author, checker, random);
    var creatureSlots = Enumerable.Range(0, BuiltInRecipes.InventedFamilies).Where(i => world.Families[i].Kind == RecipeKind.Creature).ToList();
    Console.WriteLine($"# Director {(extinctOnly ? "extinct-animal " : "")}picks, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| # | family | asked | tried | result | fact / checker | s |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- |");
    int used = 0, tries = 0;
    for (var i = 0; i < count; i++)
    {
        // At a different real place each time, as the toy would be after its journeys.
        var known = KnownPlace.All[random.Next(KnownPlace.All.Count)];
        var request = loop.NextRequest(world.Families, new Scene(known.Landscape, TimeOfDay.Afternoon, Weather.Clear, known.Name))!;
        if (extinctOnly)
        {
            var slot = creatureSlots[i % creatureSlots.Count];
            var places = DirectorLoop.PlacesFor(RealCategory.ExtinctAnimal);
            request = request with
            {
                Slot = slot, Kind = RecipeKind.Creature, GrowInto = null, Category = RealCategory.ExtinctAnimal,
                Current = world.Families.ChangedAt(slot) is null ? null : world.Families[slot].Name,
                Place = places[random.Next(places.Count)],
            };
        }
        var outcome = await director.ProposeAsync(request);
        var asked = request.GrowInto is { } g ? $"grow {request.Current} into {g}" : $"{request.Category} {request.Place}" + (request.Current is { } c ? $", replacing {c}" : "");
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

// Steps the scene the way the toy would, back to back, and prints every attempt.
async Task Scenes(int count)
{
    var world = new World();
    var random = new Random(Environment.TickCount);
    var scenes = new SceneDirector(author, placeChecker);
    var stepsHere = 0;
    Console.WriteLine($"# Director scene steps, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| # | from | tried | result | fact / checker | s |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
    int used = 0, tries = 0;
    var places = new List<string>();
    for (var i = 0; i < count; i++)
    {
        var change = DirectorLoop.ChooseChange(world.Scene, stepsHere, random.NextDouble());
        var things = world.Families.All.Skip(BuiltInRecipes.InventedFamilies).Select(r => r.Name).OrderBy(_ => random.Next()).Take(12).ToList();
        var request = new SceneRequest(world.Scene, [.. places], things, change);
        var outcome = await scenes.ProposeAsync(request);
        foreach (var a in outcome.Attempts)
        {
            tries++;
            var detail = a.Check is { } k ? $"{a.Fact} / checker: {k.Verdict}, {string.Join(" ", k.Landscapes)}, snows {k.Snows}, \"{k.WhatItIs}\"" : a.Fact;
            Console.WriteLine($"| {i + 1} | {request.Change}: {world.Scene} | {a.Tried} | {a.Refused ?? "used"} | {detail?.Replace("|", "/")} | {outcome.Took.TotalSeconds:0.0} |");
        }
        if (outcome.Scene is { } s)
        {
            used++;
            stepsHere = SceneRules.SamePlace(world.Scene.Place, s.Place) ? stepsHere + 1 : 0;
            if (world.Scene.Place is { } left && !SceneRules.SamePlace(left, s.Place)) places.Insert(0, left);
            world.SetScene(s);
        }
    }
    Console.WriteLine();
    Console.WriteLine($"{used} of {count} steps changed the scene, in {tries} tries. Places visited: {string.Join("; ", places.Append(world.Scene.Place ?? "meadow"))}.");
}

// Real places, including Icelandic ones, and made-up or story places.
async Task Places()
{
    (string Name, bool Real)[] cases =
    [
        ("Reynisfjara, Iceland", true), ("Thingvellir, Iceland", true), ("Lake Myvatn, Iceland", true), ("Vatnajokull, Iceland", true),
        ("Serengeti, Tanzania", true), ("Sahara, Morocco", true), ("Amazon rainforest, Brazil", true), ("Black Forest, Germany", true),
        ("Lake Bled, Slovenia", true), ("Rila Mountains, Bulgaria", true), ("Bondi Beach, Australia", true), ("Yosemite, USA", true),
        ("Narnia", false), ("Hogwarts, Scotland", false), ("Neverland", false), ("Mordor, New Zealand", false),
        ("Atlantis", false), ("Whispering Pines Valley, Iceland", false), ("Glimmerlake, Norway", false), ("Candy Mountain, Canada", false),
    ];
    Console.WriteLine($"# Place checker, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| place | checker | landscapes | snows | what it is | right? |");
    Console.WriteLine("| --- | --- | --- | --- | --- | --- |");
    var right = 0;
    var clock = Stopwatch.StartNew();
    foreach (var (name, real) in cases)
    {
        var check = await placeChecker.CheckAsync(name);
        var ok = (check.Verdict == "real") == real;
        if (ok) right++;
        Console.WriteLine($"| {name} | {check.Verdict} | {string.Join(" ", check.Landscapes)} | {check.Snows} | {check.WhatItIs.Replace("|", "/")} | {(ok ? "yes" : "NO")} |");
    }
    Console.WriteLine();
    Console.WriteLine($"{right} of {cases.Length} right; {clock.Elapsed.TotalSeconds / cases.Length:0.0} s a check.");
}

// True and false sentences about real places, the false ones including those the model wrote in a test run.
async Task Facts()
{
    (string Place, string Fact, bool True)[] cases =
    [
        ("Hallormsstadaskogur, Iceland", "Hallormsstadaskogur in east Iceland is the country's largest forest.", true),
        ("Bondi Beach, Australia", "Bondi Beach is a famous beach in Sydney.", true),
        ("Wadi Rum, Jordan", "Wadi Rum is a desert valley in southern Jordan.", true),
        ("Lake Bled, Slovenia", "Lake Bled has a small island with a church on it.", true),
        ("Serengeti, Tanzania", "Wildebeest migrate across the Serengeti every year.", true),
        ("Yosemite, USA", "Yosemite is a national park in California with tall granite cliffs.", true),
        ("Lake Myvatn, Iceland", "Lake Myvatn is a lake in north Iceland.", true),
        ("Banff, Canada", "Banff is a national park in the Rocky Mountains.", true),
        ("Hallormsstadaskogur, Iceland", "Hallormsstadaskogur is a small village in the western part of Iceland.", false),
        ("Hallormsstadaskogur, Iceland", "Hallormsstadaskogur is in the north of Iceland, with a dense forest of ancient oak trees.", false),
        ("Bondi Beach, Australia", "Bondi Beach is a beach in Melbourne.", false),
        ("Wadi Rum, Jordan", "Wadi Rum is a rainforest in Jordan.", false),
        ("Lake Bled, Slovenia", "Lake Bled is a lake in Austria.", false),
        ("Lake Myvatn, Iceland", "Lake Myvatn is a lake in the south of Iceland.", false),
        ("Sahara, Morocco", "The Sahara is a desert in South America.", false),
        ("Banff, Canada", "Banff is a beach town on the coast of Canada.", false),
    ];
    Console.WriteLine($"# Place-fact check, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine("| place | sentence | true? | confirmed | right? |");
    Console.WriteLine("| --- | --- | --- | --- | --- |");
    int right = 0, keptTrue = 0, keptFalse = 0;
    var clock = Stopwatch.StartNew();
    foreach (var (place, fact, isTrue) in cases)
    {
        var confirmed = await placeChecker.ConfirmFactAsync(place, fact);
        if (confirmed == isTrue) right++;
        if (confirmed && isTrue) keptTrue++;
        if (confirmed && !isTrue) keptFalse++;
        Console.WriteLine($"| {place} | {fact} | {(isTrue ? "yes" : "no")} | {(confirmed ? "yes" : "no")} | {(confirmed == isTrue ? "yes" : "NO")} |");
    }
    Console.WriteLine();
    Console.WriteLine($"{right} of {cases.Length} right: {keptTrue} of {cases.Count(c => c.True)} true sentences kept, {keptFalse} of {cases.Count(c => !c.True)} false ones let through; {clock.Elapsed.TotalSeconds / cases.Length:0.0} s a check.");
}

// Living things that are and are not found at real places, the "not found" ones including picks from a test run,
// asked as FactChecker.FitsAsync asks and, to compare, without the reasoning first. FitsAsync lets all but "no" through.
async Task Fits()
{
    (string Name, RealCategory Category, string Here, bool Found)[] cases =
    [
        ("linden tree", RealCategory.Plant, "at Lake Bled, Slovenia", true), ("strawberry", RealCategory.Plant, "at Lake Bled, Slovenia", true),
        ("arctic thyme", RealCategory.Plant, "at Reynisfjara, Iceland", true), ("nootka lupine", RealCategory.Plant, "at Reynisfjara, Iceland", true),
        ("alpine rose", RealCategory.Plant, "at Lauterbrunnen, Switzerland", true), ("edelweiss", RealCategory.Plant, "at Swiss Alps, Switzerland", true),
        ("birch tree", RealCategory.Plant, "at Hallormsstadaskogur, Iceland", true), ("acacia tree", RealCategory.Plant, "at Masai Mara, Kenya", true),
        ("baobab tree", RealCategory.Plant, "at Serengeti, Tanzania", true), ("puffin", RealCategory.Animal, "at Reynisfjara, Iceland", true),
        ("chamois", RealCategory.Animal, "at Swiss Alps, Switzerland", true), ("brown trout", RealCategory.Animal, "at Lake Bled, Slovenia", true),
        ("mute swan", RealCategory.Animal, "at Lake Bled, Slovenia", true), ("giraffe", RealCategory.Animal, "at Masai Mara, Kenya", true),
        ("arctic fox", RealCategory.Animal, "at Hallormsstadaskogur, Iceland", true), ("cow", RealCategory.Animal, "at Lauterbrunnen, Switzerland", true),
        ("mango", RealCategory.Plant, "at Lake Bled, Slovenia", false), ("banana", RealCategory.Plant, "at Reynisfjara, Iceland", false),
        ("coconut palm", RealCategory.Plant, "at Hallormsstadaskogur, Iceland", false), ("cactus", RealCategory.Plant, "at Reynisfjara, Iceland", false),
        ("oak tree", RealCategory.Plant, "at Wadi Rum, Jordan", false), ("sunflower", RealCategory.Plant, "at Eldhraun, Iceland", false),
        ("polar bear", RealCategory.Animal, "at Masai Mara, Kenya", false), ("penguin", RealCategory.Animal, "at Swiss Alps, Switzerland", false),
        ("kangaroo", RealCategory.Animal, "at Lake Bled, Slovenia", false), ("giraffe", RealCategory.Animal, "at Reynisfjara, Iceland", false),
        ("camel", RealCategory.Animal, "at Hallormsstadaskogur, Iceland", false), ("gazelle", RealCategory.Animal, "at Bondi Beach, Australia", false),
    ];
    string Lives(RealCategory c) => c == RealCategory.Plant ? "grows" : "lives";
    string Kept(RealCategory c) => c == RealCategory.Plant ? "grown in gardens and fields" : "kept by people";
    var schema = JsonDocument.Parse(FactChecker.FitSchemaJson).RootElement.Clone();
    var plain = JsonDocument.Parse("""
        {"type":"object","additionalProperties":false,"required":["verdict"],
         "properties":{"verdict":{"type":"string","enum":["yes","no","unsure"]}}}
        """).RootElement.Clone();
    (string Name, JsonElement Schema, Func<string, RealCategory, string, string> Prompt)[] ways =
    [
        ("now", schema, FactChecker.FitPrompt),
        ("plain", plain, (n, c, h) => $"""
            Name: "{n}"
            Place: "{h}"

            verdict: "yes" if a {n} {Lives(c)} there or in the countryside around it today, in the wild or {Kept(c)}.
            "no" if it could not {Lives(c).TrimEnd('s')} there: the climate is far too hot or too cold for it, or it lives only
            on another continent. "unsure" if you do not know.
            """),
    ];
    Console.WriteLine($"# Found-there check, {model}, {DateTime.Now:yyyy-MM-dd HH:mm}");
    Console.WriteLine();
    Console.WriteLine($"| name | place | found? | {string.Join(" | ", ways.Select(w => w.Name))} |");
    Console.WriteLine($"| --- | --- | --- |{string.Concat(ways.Select(_ => " --- |"))}");
    var verdicts = new string[ways.Length, cases.Length];
    var seconds = new double[ways.Length];
    for (var i = 0; i < cases.Length; i++)
    {
        var (name, category, here, found) = cases[i];
        for (var w = 0; w < ways.Length; w++)
        {
            var clock = Stopwatch.StartNew();
            var result = await cold.GenerateJsonAsync(FactChecker.System, ways[w].Prompt(name, category, here), ways[w].Schema);
            seconds[w] += clock.Elapsed.TotalSeconds;
            verdicts[w, i] = result.Json?.RootElement.TryGetProperty("verdict", out var v) == true ? v.GetString() ?? "?" : "?";
        }
        Console.WriteLine($"| {name} | {here} | {(found ? "yes" : "no")} | {string.Join(" | ", Enumerable.Range(0, ways.Length).Select(w => verdicts[w, i]))} |");
    }
    Console.WriteLine();
    for (var w = 0; w < ways.Length; w++)
    {
        int Count(Func<string, bool> passes, bool found) => Enumerable.Range(0, cases.Length).Count(i => cases[i].Found == found && passes(verdicts[w, i]));
        Console.WriteLine($"{ways[w].Name}: only yes passes: {Count(v => v == "yes", true)} of {cases.Count(c => c.Found)} found kept, {Count(v => v == "yes", false)} of {cases.Count(c => !c.Found)} not found let through; "
            + $"all but no passes: {Count(v => v != "no", true)} kept, {Count(v => v != "no", false)} let through; {seconds[w] / cases.Length:0.0} s a check.");
    }
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
        ("strawberry", RealCategory.Plant, true), ("pumpkin", RealCategory.Plant, true),
        // Real, but not the kind claimed: these must be refused too.
        ("dodo", RealCategory.Animal, false), ("puffin", RealCategory.ExtinctAnimal, false),
        ("triceratops", RealCategory.Animal, false), ("fennec fox", RealCategory.Plant, false),
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
        var check = await checker.CheckAsync(name, null);
        var passed = rule is null && check.Passes(category);
        var ok = passed == real;
        if (ok) right++;
        else if (passed) inventedThrough++;
        else realRefused++;
        Console.WriteLine($"| {name} | {category} | {rule ?? "ok"} | {check.Verdict}, {check.Kind} | {check.WhatItIs.Replace("|", "/")} | {(ok ? "yes" : "NO")} |");
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
