using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>One line of the director's log: what it was asked, what came of it, or why it could not run.
/// <paramref name="Slot"/> is -1 for a scene step, whose result is in <paramref name="Scene"/>.</summary>
public sealed record DirectorLogEntry(DateTime At, int Slot, DirectorOutcome? Outcome, string? Error, SceneOutcome? Scene = null)
{
    /// <summary>One line per attempt, for the grown-ups' page and the log file.</summary>
    public IEnumerable<string> Lines(string timeFormat = "HH:mm")
    {
        var at = At.ToString(timeFormat);
        if (Error is { } error)
        {
            yield return $"{at}  could not reach the model: {error}";
            yield break;
        }
        if (Scene is { } scene)
        {
            foreach (var a in scene.Attempts)
                yield return a.Refused is null
                    ? $"{at}  scene: {a.Tried}. {scene.Scene?.Fact ?? a.Fact}"
                    : $"{at}  scene turned down: {a.Tried} ({a.Refused}){(a.Check is { } c ? $"; checker said \"{c.WhatItIs}\"" : "")}";
            if (!scene.Accepted) yield return $"{at}  scene unchanged; trying again soon";
            yield break;
        }
        foreach (var a in Outcome!.Attempts)
            yield return a.Refused is null
                ? $"{at}  key family {Slot}: {a.Subject}. {a.Fact}"
                : $"{at}  turned down for key family {Slot}: {a.Subject} ({a.Refused}){(a.Check is { } c ? $"; checker said \"{c.WhatItIs}\"" : "")}";
        if (!Outcome.Accepted) yield return $"{at}  key family {Slot} unchanged; trying again soon";
    }
}

/// <summary>
/// The director loop. It keeps the local model busy with two jobs, one request at a time: every
/// <see cref="CalmLimits.RestyleMinutes"/> it asks the <see cref="RealThingDirector"/> for a real thing for one key
/// family, and every <see cref="CalmLimits.SceneMinutes"/> it asks the <see cref="SceneDirector"/> for the scene's
/// next step (a real place, its weather, the time of day moving on). When both are due, the one waiting longer goes.
/// Families still holding an invented built-in thing go first; after that, a thing that really grows into
/// something becomes it (tadpole, then frog), and otherwise the family changed longest ago is next.
/// Call <see cref="Update"/> from the UI thread each frame; the model runs on the thread pool and its answer is
/// applied on a later <see cref="Update"/>, so the reflex loop never waits for it.
/// </summary>
/// <param name="creatureCategories">What a creature family may be asked for, drawn evenly; defaults to
/// <see cref="CreatureMix"/>.</param>
/// <param name="scenes">Changes the scene; without one the scene stays.</param>
public sealed class DirectorLoop(RealThingDirector director, Random? random = null, IReadOnlyList<RealCategory>? creatureCategories = null,
    SceneDirector? scenes = null) : IDisposable
{
    /// <summary>The first family request comes this long into play, so starting the toy is not when things change;
    /// a request that came back empty is tried again this long after.</summary>
    public const double FirstAfterSeconds = 30;
    /// <summary>The first scene step comes this long into play (or sooner if the calm level's interval is shorter),
    /// so even a short play sees the world move.</summary>
    public const double FirstSceneAfterSeconds = 60;
    public const int LogLength = 16;
    /// <summary>Places the scene has been, remembered so it moves on somewhere new.</summary>
    public const int RecentPlaces = 8;
    /// <summary>After this many steps at one place the next step is always a journey.</summary>
    public const int StepsBeforeJourney = 3;

    /// <summary>Plain names for the built-in animals whose recipe names are shorthand.</summary>
    private static readonly Dictionary<string, string> PlainNames = new()
    {
        ["rex"] = "tyrannosaurus rex",
        ["longneck"] = "brontosaurus",
    };

    private readonly Random _random = random ?? new Random();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<DirectorLogEntry> _log = [];
    private readonly List<string> _places = [];
    private Task<DirectorOutcome>? _family;
    private Task<SceneOutcome>? _scene;
    private (int Slot, Recipe Was)? _asked;
    private Scene? _sceneWas;
    private double? _familyDue, _sceneDue;
    private int _stepsHere;
    private bool _lastWasScene;

    public string ModelName => director.ModelName;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DirectorLogEntry> Log => _log;

    public bool Busy => _family is not null || _scene is not null;

    /// <summary>Raised on the UI thread when a family has been restyled.</summary>
    public event Action<int, Recipe>? Restyled;

    /// <summary>Raised on the UI thread with a line for the log file: each request as it goes out, and each line of
    /// what came of it.</summary>
    public event Action<string>? Noted;

    /// <param name="world">The world whose families and scene change.</param>
    /// <param name="limits">How often each job comes round.</param>
    /// <param name="enabled">False while the director is switched off or the child is asleep: nothing new
    /// is asked, though an answer already on its way is still used.</param>
    public void Update(World world, CalmLimits limits, bool enabled)
    {
        if (_family is { IsCompleted: true } family) Finish(world, family, limits);
        if (_scene is { IsCompleted: true } scene) Finish(world, scene, limits);
        if (!enabled || Busy) return;

        _familyDue ??= world.Time + Math.Min(FirstAfterSeconds, limits.RestyleMinutes * 60);
        _sceneDue ??= world.Time + Math.Min(FirstSceneAfterSeconds, limits.SceneMinutes * 60);
        // The job waiting longer goes; when both are due together (as in test mode), they take turns.
        var sceneFirst = scenes is not null && world.Time >= _sceneDue && (_sceneDue < _familyDue || (_sceneDue == _familyDue && !_lastWasScene));
        if (sceneFirst)
        {
            _sceneDue = world.Time + (limits.SceneMinutes * 60);
            var sceneRequest = NextSceneRequest(world);
            Note($"asking for a scene step ({sceneRequest.Change}) from {world.Scene}");
            _lastWasScene = true;
            _sceneWas = world.Scene;
            var sceneToken = _stop.Token;
            _scene = Task.Run(() => scenes!.ProposeAsync(sceneRequest, sceneToken), sceneToken);
            return;
        }
        if (world.Time < _familyDue) return;

        _familyDue = world.Time + (limits.RestyleMinutes * 60);
        var request = NextRequest(world.Families, world.Scene);
        if (request is null) return;
        _asked = (request.Slot, world.Families[request.Slot]);
        _lastWasScene = false;
        Note(request.GrowInto is { } next
            ? $"asking key family {request.Slot} to grow from {request.Current} into {next}"
            : $"asking key family {request.Slot} ({request.Current ?? world.Families[request.Slot].Name}) for {RealThingPick.Describe(request.Category).Split(':')[0]} {request.Place}");
        var token = _stop.Token;
        _family = Task.Run(() => director.ProposeAsync(request, token), token);
    }

    /// <summary>The scene step to ask for: what kind of step, the scene now, where it has been, and the real things in
    /// the world. A small model left to choose only ever moves the clock on, so the loop chooses the kind: a journey
    /// from the starting meadow and after <see cref="StepsBeforeJourney"/> steps at one place, otherwise mostly the
    /// weather or the time of day, now and then a journey early.</summary>
    public SceneRequest NextSceneRequest(World world)
    {
        var things = world.Families.All
            .Where((r, i) => !KeyFamilies.CanRestyle(i) || world.Families.ChangedAt(i) is not null)
            .Select(r => PlainNames.GetValueOrDefault(r.Name, r.Name))
            .Distinct()
            .OrderBy(_ => _random.Next())
            .Take(12)
            .ToList();
        return new SceneRequest(world.Scene, [.. _places.Where(p => !SceneRules.SamePlace(p, world.Scene.Place))], things,
            ChooseChange(world.Scene, _stepsHere, _random.NextDouble()));
    }

    /// <summary>The kind of step to take from <paramref name="current"/> after <paramref name="stepsHere"/> steps at
    /// its place, for a roll between 0 and 1.</summary>
    public static SceneChange ChooseChange(Scene current, int stepsHere, double roll) =>
        current.Place is null || stepsHere >= StepsBeforeJourney || roll < 0.2 ? SceneChange.Journey
        : roll < 0.6 ? SceneChange.Weather
        : SceneChange.Time;

    /// <summary>The next family to restyle and what to ask for it; null if no family may change.</summary>
    /// <param name="scene">The scene now: half the requests for a living animal or a plant ask for one found there.</param>
    public DirectorRequest? NextRequest(KeyFamilies families, Scene? scene = null)
    {
        var slots = Enumerable.Range(0, families.Count).Where(KeyFamilies.CanRestyle).ToList();
        if (slots.Count == 0) return null;

        var existing = families.All
            .Where((r, i) => !KeyFamilies.CanRestyle(i) || families.ChangedAt(i) is not null)
            .Select(r => PlainNames.GetValueOrDefault(r.Name, r.Name))
            .Distinct()
            .ToList();

        var invented = slots.Where(i => families.ChangedAt(i) is null).ToList();
        if (invented.Count > 0)
        {
            var slot = invented[_random.Next(invented.Count)];
            return Ask(slot, families[slot].Kind, null, existing, scene);
        }

        var growing = slots.Where(i => families[i].GrowsInto is not null).OrderBy(i => families.ChangedAt(i)).ToList();
        var pick = growing.Count > 0 ? growing[0] : slots.OrderBy(i => families.ChangedAt(i)).First();
        var current = families[pick];
        return current.GrowsInto is { } next
            ? new DirectorRequest(pick, current.Kind, current.Name, existing, next, current.Kind == RecipeKind.Plant ? RealCategory.Plant : RealCategory.Animal)
            : Ask(pick, current.Kind, current.Name, existing, scene);
    }

    /// <summary>A request with a category drawn at random, so the picks range widely, for something that belongs
    /// where the scene is; with no scene, a place drawn at random too.</summary>
    private DirectorRequest Ask(int slot, RecipeKind kind, string? current, IReadOnlyList<string> existing, Scene? scene)
    {
        var category = kind == RecipeKind.Plant ? RealCategory.Plant : Pick(creatureCategories ?? CreatureMix);
        var place = scene is not null ? HerePlace(scene, category) : Pick(Places[category]);
        // Only a real, named place is checked: the starting meadow is nowhere in particular.
        return new DirectorRequest(slot, kind, current, existing, Category: category, Place: place,
            Here: scene?.Place is { } named ? $"at {named}" : null);
    }

    /// <summary>Where the scene is, as a request's place: "that really lives at Reynisfjara, Iceland". An extinct
    /// animal can't be there now, so it is one that lived somewhere that looks like it.</summary>
    public static string HerePlace(Scene scene, RealCategory category)
    {
        var at = scene.Place is { } named ? $"at {named}" : Scene.Describe(scene.Landscape);
        return category switch
        {
            RealCategory.Animal => $"that really lives {at}",
            RealCategory.Plant => $"that really grows {at}",
            RealCategory.ExtinctAnimal => $"that once lived {Scene.Describe(scene.Landscape)}",
            RealCategory.Vehicle => $"that people really use {at}",
            _ => $"that a child might really have {at}",
        };
    }

    /// <summary>The places a request for <paramref name="category"/> may name.</summary>
    public static IReadOnlyList<string> PlacesFor(RealCategory category) => Places[category];

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    /// <summary>A creature family is mostly animals: half alive today, a fifth extinct, the rest vehicles and objects.</summary>
    public static readonly IReadOnlyList<RealCategory> CreatureMix =
    [
        RealCategory.Animal, RealCategory.Animal, RealCategory.Animal, RealCategory.Animal, RealCategory.Animal,
        RealCategory.ExtinctAnimal, RealCategory.ExtinctAnimal, RealCategory.Vehicle, RealCategory.Vehicle, RealCategory.Object,
    ];

    /// <summary>Where each kind of thing may be found; one is put in each request.</summary>
    private static readonly Dictionary<RealCategory, string[]> Places = new()
    {
        [RealCategory.Animal] =
        [
            "on a farm", "in a forest", "in a pond", "in the sea", "in a jungle", "in the desert", "in the Arctic",
            "on the African savanna", "in a garden", "in a river", "in the mountains", "in Iceland", "in Australia",
            "in South America", "on a beach", "in the Antarctic", "in a meadow",
        ],
        [RealCategory.ExtinctAnimal] =
        [
            "from the time of the dinosaurs", "from the Ice Age", "that swam in the sea in the time of the dinosaurs",
            "that flew in the time of the dinosaurs", "that died out in the last few hundred years",
            "from millions of years ago, before the dinosaurs",
        ],
        [RealCategory.Plant] =
        [
            "in a garden", "in a forest", "in a vegetable patch", "in a meadow", "in an orchard", "by a pond",
            "in the desert", "in Iceland", "in a jungle", "on a farm", "in the mountains", "by the sea",
        ],
        [RealCategory.Vehicle] = ["on the road", "on rails", "on the water", "in the sky", "on a farm", "on a building site", "in a town"],
        [RealCategory.Object] = ["in a kitchen", "at the beach", "in a playground", "in a garden", "at bedtime", "at a birthday party"],
    };

    private void Finish(World world, Task<DirectorOutcome> done, CalmLimits limits)
    {
        var (slot, was) = _asked!.Value;
        _family = null;
        _asked = null;
        if (done.IsCanceled) return;
        if (done.Exception is { } e)
        {
            AddLog(new DirectorLogEntry(DateTime.Now, slot, null, e.GetBaseException().Message));
            // An unreachable model is not asked again at once, however fast the director is set to go.
            _familyDue = world.Time + Math.Max(FirstAfterSeconds, limits.RestyleMinutes * 60);
            return;
        }

        var outcome = done.Result;
        AddLog(new DirectorLogEntry(DateTime.Now, slot, outcome, null));
        // Nothing usable came back: try again (another category and place, maybe another family) shortly,
        // rather than leaving the family as it is for a whole interval.
        _familyDue = world.Time + Math.Min(outcome.Accepted ? double.MaxValue : FirstAfterSeconds, limits.RestyleMinutes * 60);
        // A fresh world, or another change, since the request was made: the answer is for something gone.
        if (outcome.Recipe is not { } recipe || !ReferenceEquals(world.Families[slot], was)) return;
        if (world.Families.Restyle(slot, recipe, world.Time)) Restyled?.Invoke(slot, recipe);
    }

    private void Finish(World world, Task<SceneOutcome> done, CalmLimits limits)
    {
        var was = _sceneWas;
        _scene = null;
        _sceneWas = null;
        if (done.IsCanceled) return;
        if (done.Exception is { } e)
        {
            AddLog(new DirectorLogEntry(DateTime.Now, -1, null, e.GetBaseException().Message));
            _sceneDue = world.Time + Math.Max(FirstAfterSeconds, limits.SceneMinutes * 60);
            return;
        }

        var outcome = done.Result;
        AddLog(new DirectorLogEntry(DateTime.Now, -1, null, null, outcome));
        _sceneDue = world.Time + Math.Min(outcome.Accepted ? double.MaxValue : FirstAfterSeconds, limits.SceneMinutes * 60);
        if (outcome.Scene is not { } scene || world.Scene != was) return;
        _stepsHere = SceneRules.SamePlace(world.Scene.Place, scene.Place) ? _stepsHere + 1 : 0;
        if (world.Scene.Place is { } left && !SceneRules.SamePlace(left, scene.Place))
        {
            _places.Insert(0, left);
            if (_places.Count > RecentPlaces) _places.RemoveAt(_places.Count - 1);
        }
        world.SetScene(scene);
    }

    private void Note(string line) => Noted?.Invoke($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}");

    private void AddLog(DirectorLogEntry entry)
    {
        if (Noted is { } noted)
            foreach (var line in entry.Lines("yyyy-MM-dd HH:mm:ss")) noted(line);
        _log.Insert(0, entry);
        if (_log.Count > LogLength) _log.RemoveAt(_log.Count - 1);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
