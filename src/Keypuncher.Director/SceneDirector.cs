using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>The kind of step a scene request asks for. The loop chooses it, because left to choose, a small model
/// only ever lets the time of day move on; the model chooses what the new weather is, or where to go.</summary>
public enum SceneChange
{
    /// <summary>The weather changes; the place and time stay.</summary>
    Weather,
    /// <summary>The time of day moves on; the weather may change too.</summary>
    Time,
    /// <summary>A new real place; the weather may change too, the time stays.</summary>
    Journey,
}

/// <summary>What the director is asked for: the next step for the scene.</summary>
/// <param name="Current">The scene now.</param>
/// <param name="RecentPlaces">Places the world has been lately, newest first, so it moves on somewhere new.</param>
/// <param name="Things">The real things the keys make now, so a new place can be one where they are really found.</param>
/// <param name="Change">What kind of step to take.</param>
/// <param name="MustGoTo">The director's last resort for a journey the model keeps missing: one of
/// <see cref="KnownPlace.All"/>, which must still pass the place check.</param>
public sealed record SceneRequest(Scene Current, IReadOnlyList<string> RecentPlaces, IReadOnlyList<string> Things,
    SceneChange Change = SceneChange.Journey, KnownPlace? MustGoTo = null);

/// <summary>A real place whose landscape is known here rather than asked of the model.</summary>
public sealed record KnownPlace(string Name, Landscape Landscape)
{
    /// <summary>Well-known real places, Icelandic and Bulgarian ones among them, offered to the model on every journey
    /// so that it does not stay where it is or make up a name ("Savannah, Botswana"), and drawn from as a last resort.
    /// The model still chooses, and its choice is still checked. Names are written without accents.</summary>
    public static readonly IReadOnlyList<KnownPlace> All =
    [
        new("Thorsmork, Iceland", Landscape.Meadow), new("Alpe di Siusi, Italy", Landscape.Meadow), new("Lauterbrunnen, Switzerland", Landscape.Meadow),
        new("Fljotshlid, Iceland", Landscape.Farm), new("Tuscany, Italy", Landscape.Farm), new("Cotswolds, England", Landscape.Farm),
        new("Hallormsstadaskogur, Iceland", Landscape.Forest), new("Black Forest, Germany", Landscape.Forest), new("Bialowieza Forest, Poland", Landscape.Forest),
        new("Amazon rainforest, Brazil", Landscape.Jungle), new("Daintree Rainforest, Australia", Landscape.Jungle), new("Borneo, Malaysia", Landscape.Jungle),
        new("Landmannalaugar, Iceland", Landscape.Mountains), new("Rila Mountains, Bulgaria", Landscape.Mountains), new("Swiss Alps, Switzerland", Landscape.Mountains),
        new("Banff, Canada", Landscape.Mountains),
        new("Reynisfjara, Iceland", Landscape.Beach), new("Raudasandur, Iceland", Landscape.Beach), new("Sunny Beach, Bulgaria", Landscape.Beach),
        new("Bondi Beach, Australia", Landscape.Beach),
        new("Lake Myvatn, Iceland", Landscape.Lake), new("Jokulsarlon, Iceland", Landscape.Lake), new("Lake Bled, Slovenia", Landscape.Lake),
        new("Seven Rila Lakes, Bulgaria", Landscape.Lake),
        new("Sahara, Morocco", Landscape.Desert), new("Namib Desert, Namibia", Landscape.Desert), new("Wadi Rum, Jordan", Landscape.Desert),
        new("Serengeti, Tanzania", Landscape.Savanna), new("Masai Mara, Kenya", Landscape.Savanna), new("Kruger National Park, South Africa", Landscape.Savanna),
        new("Vatnajokull, Iceland", Landscape.SnowyHills), new("Lapland, Finland", Landscape.SnowyHills), new("Svalbard, Norway", Landscape.SnowyHills),
        new("Eldhraun, Iceland", Landscape.LavaField), new("Thingvellir, Iceland", Landscape.LavaField), new("Reykjanes, Iceland", Landscape.LavaField),
    ];
}

/// <summary>The model's answer for one scene step.</summary>
public sealed record ScenePick
{
    [JsonPropertyName("place")] public required string Place { get; init; }
    [JsonPropertyName("landscape")] public required Landscape Landscape { get; init; }
    [JsonPropertyName("timeOfDay")] public required TimeOfDay TimeOfDay { get; init; }
    [JsonPropertyName("weather")] public required Weather Weather { get; init; }
    [JsonPropertyName("fact")] public required string Fact { get; init; }
}

/// <summary>One scene the director tried, kept for the grown-ups' page and the sandbox.</summary>
public sealed record SceneAttempt(string Tried, string? Fact, string? Refused, PlaceCheck? Check);

/// <summary>What came of one scene request: the new scene, or nothing, and every attempt on the way.</summary>
public sealed record SceneOutcome(SceneRequest Request, Scene? Scene, IReadOnlyList<SceneAttempt> Attempts, TimeSpan Took)
{
    public bool Accepted => Scene is not null;
}

/// <summary>The JSON schema for <see cref="ScenePick"/>, narrowed per request to the kind of step asked for, so that
/// time only goes forward, a weather step really changes the weather, and a rainbow only follows rain. Ollama holds
/// enums at decode time.</summary>
public static class SceneSchema
{
    public static JsonElement For(SceneRequest request)
    {
        var now = request.Current;
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("place", "landscape", "timeOfDay", "weather", "fact"),
            ["properties"] = new JsonObject
            {
                ["place"] = new JsonObject { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 48 },
                ["landscape"] = Enum(request.MustGoTo is { } must ? [must.Landscape.ToString()]
                    : request.Change == SceneChange.Journey ? Enum<Landscape>() : [now.Landscape.ToString()]),
                ["timeOfDay"] = Enum([(request.Change == SceneChange.Time ? Scene.Next(now.TimeOfDay) : now.TimeOfDay).ToString()]),
                ["weather"] = Enum(Enum<Weather>()
                    .Where(w => w != nameof(Weather.Rainbow) || now.Weather == Weather.Rain)
                    .Where(w => request.Change != SceneChange.Weather || w != now.Weather.ToString())),
                ["fact"] = new JsonObject { ["type"] = "string", ["minLength"] = 8, ["maxLength"] = 120 },
            },
        };
        return JsonDocument.Parse(schema.ToJsonString()).RootElement.Clone();
    }

    private static IEnumerable<string> Enum<T>() where T : struct, Enum => System.Enum.GetNames<T>();

    private static JsonObject Enum(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]) };
}

/// <summary>
/// The checks a scene must pass before its place is asked about. Like <see cref="RealThingRules"/>, they cannot
/// prove the place is real, but they hold the scene to how the real world works: time goes forward, a rainbow comes
/// after rain, it does not snow in the desert, a new landscape is a new place, and only one or two things change.
/// </summary>
public static class SceneRules
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Words for a kind of country rather than the name of a place: "Savannah, Botswana" names nowhere.</summary>
    private static readonly HashSet<string> KindWords =
    [
        "meadow", "meadows", "farm", "farmland", "countryside", "forest", "woods", "jungle", "rainforest", "mountain",
        "mountains", "hills", "beach", "coast", "seaside", "sea", "ocean", "lake", "desert", "savanna", "savannah",
        "grassland", "plains", "lava field", "lava", "snow", "glacier", "island", "valley", "river", "park", "national park",
    ];

    /// <summary>Single spaces, no quotes round it.</summary>
    public static string Tidy(string place) =>
        string.Join(' ', place.Trim().Trim('"', '\'', '.', '!').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    public static bool SamePlace(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Tidy(a), Tidy(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns the model's JSON into the next scene, or says why not. <paramref name="newPlace"/> is true when
    /// the place changed, so the place must be checked before the scene is used.</summary>
    public static bool TryBuild(JsonDocument? json, SceneRequest request, out ScenePick? pick, out Scene? scene, out bool newPlace, out string? error)
    {
        pick = null;
        scene = null;
        newPlace = false;
        error = null;
        if (json is null) { error = "not JSON"; return false; }
        try { pick = json.RootElement.Deserialize<ScenePick>(Options); }
        catch (JsonException e) { error = "bad JSON: " + e.Message; return false; }
        if (pick is null) { error = "empty"; return false; }

        var now = request.Current;
        var place = Tidy(pick.Place);
        pick = pick with { Place = place };
        if (place.Length is < 3 or > 48) { error = "place name length"; return false; }
        if (!place.All(c => char.IsLetter(c) || c is ' ' or ',' or '-' or '\'' or '.')) { error = "not a plain place name"; return false; }
        var named = place.Split(',')[0].Trim().ToLowerInvariant();
        if (KindWords.Contains(named) || KindWords.Contains(named.Replace("the ", "")) || named.Contains("not yet")) { error = "a kind of country, not a named place"; return false; }
        if (request.MustGoTo is { } must && !SamePlace(place, must.Name)) { error = $"should have been {must.Name}"; return false; }
        if (string.IsNullOrWhiteSpace(pick.Fact)) { error = "no fact"; return false; }

        newPlace = !SamePlace(place, now.Place);
        var asked = request.Change switch
        {
            SceneChange.Journey when !newPlace => "should have gone somewhere new",
            SceneChange.Weather when newPlace || pick.Weather == now.Weather => "should have changed only the weather",
            SceneChange.Time when newPlace || pick.TimeOfDay == now.TimeOfDay => "should have moved the time on",
            _ => null,
        };
        if (asked is not null) { error = asked; return false; }
        if (pick.TimeOfDay != now.TimeOfDay && pick.TimeOfDay != Scene.Next(now.TimeOfDay)) { error = "time only goes forward"; return false; }
        if (pick.Weather == Weather.Rainbow && now.Weather != Weather.Rain) { error = "a rainbow only comes after rain"; return false; }
        if (pick.Weather == Weather.Snow && Scene.Hot(pick.Landscape)) { error = $"it never snows {Scene.Describe(pick.Landscape)}"; return false; }
        if (pick.Weather == Weather.Snow && !newPlace && !now.Snows) { error = "it never snows there"; return false; }
        if (pick.Landscape != now.Landscape && !newPlace) { error = "a new landscape needs a new place"; return false; }
        if (newPlace && request.RecentPlaces.Any(p => SamePlace(p, place))) { error = "been there lately"; return false; }

        var changed = (newPlace ? 1 : 0) + (pick.TimeOfDay != now.TimeOfDay ? 1 : 0) + (pick.Weather != now.Weather ? 1 : 0);
        if (changed == 0) { error = "nothing changed"; return false; }
        if (changed == 3) { error = "changed everything at once"; return false; }

        // Small models sometimes slip a stray character from another script into the fact; it is shown to grown-ups.
        var fact = new string(pick.Fact.Trim().Where(c => c < 0x2E80).ToArray());
        scene = new Scene(pick.Landscape, pick.TimeOfDay, pick.Weather, place, fact, newPlace ? false : now.Snows);
        return true;
    }
}

/// <summary>
/// Asks the model for the scene's next step: often a change of weather or the time of day moving on, sometimes a
/// journey to a new real place. Three layers, as for things: the prompt and schema, <see cref="SceneRules"/>, and
/// for a new place <see cref="PlaceChecker"/>, a cold second call that must say the place is real, that it looks
/// like the landscape claimed, and, for snow, that it snows there. A step that misses leaves the scene as it is.
/// </summary>
/// <param name="random">Picks the known places to offer on a journey.</param>
public sealed class SceneDirector(ILanguageModel author, PlaceChecker checker, Random? random = null)
{
    private readonly Random _random = random ?? new Random();

    public const int MaxAttempts = 3;

    public async Task<SceneOutcome> ProposeAsync(SceneRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var attempts = new List<SceneAttempt>();
        var suggestions = request.Change == SceneChange.Journey && request.MustGoTo is null ? Suggestions(request) : [];
        var scene = await TryAsync(request, suggestions, MaxAttempts, attempts, cancellationToken);
        // A journey must not come back empty: go to a known place, checked as usual.
        if (scene is null && suggestions is [var fallback, ..])
            scene = await TryAsync(request with { MustGoTo = fallback }, [], 1, attempts, cancellationToken);
        return new SceneOutcome(request, scene, attempts, DateTime.UtcNow - started);
    }

    /// <summary>A few known places that are not here or somewhere recent.</summary>
    private List<KnownPlace> Suggestions(SceneRequest request) =>
        [.. KnownPlace.All
            .Where(p => !SceneRules.SamePlace(p.Name, request.Current.Place) && !request.RecentPlaces.Any(r => SceneRules.SamePlace(r, p.Name)))
            .OrderBy(_ => _random.Next())
            .Take(5)];

    private async Task<Scene?> TryAsync(SceneRequest request, IReadOnlyList<KnownPlace> suggestions, int tries, List<SceneAttempt> attempts, CancellationToken cancellationToken)
    {
        var turnedDown = new List<string>();
        for (var i = 0; i < tries; i++)
        {
            var result = await author.GenerateJsonAsync(DirectorPrompts.SceneSystem, DirectorPrompts.SceneStep(request, turnedDown, suggestions), SceneSchema.For(request), cancellationToken);
            if (!SceneRules.TryBuild(result.Json, request, out var pick, out var scene, out var newPlace, out var error))
            {
                var tried = pick is null ? "?" : Describe(pick);
                attempts.Add(new SceneAttempt(tried, pick?.Fact, error, null));
                if (pick is not null) turnedDown.Add($"{tried} ({error})");
                continue;
            }

            if (newPlace)
            {
                var check = await checker.CheckAsync(pick!.Place, cancellationToken);
                // A known place's landscape is known here; any other must be one the checker finds there itself.
                var known = KnownPlace.All.FirstOrDefault(p => SceneRules.SamePlace(p.Name, pick!.Place));
                // The small checker does not know every known place (Eldhraun, Hallormsstadaskogur), so for those only
                // "fictional" is a no.
                var real = check.Verdict == "real" || (known is not null && check.Verdict == "unsure");
                var why = !real ? (check.Verdict == "fictional" ? "not a real place" : "not sure it is a real place")
                    : known is not null ? (known.Landscape == pick.Landscape ? null : $"not {pick.Landscape} country")
                    : !check.Landscapes.Contains(pick.Landscape) ? $"not {pick.Landscape} country"
                    : null;
                if (why is null && pick.Weather == Weather.Snow && !check.SnowsThere) why = "it does not snow there";
                if (why is not null)
                {
                    attempts.Add(new SceneAttempt(Describe(pick), pick.Fact, $"checker: {why}", check));
                    turnedDown.Add($"{Describe(pick)} ({why})");
                    continue;
                }
                scene = scene! with { Snows = check.SnowsThere };
                attempts.Add(new SceneAttempt(Describe(pick), pick.Fact, null, check));
            }
            else attempts.Add(new SceneAttempt(Describe(pick!), pick!.Fact, null, null));
            return scene;
        }
        return null;
    }

    private static string Describe(ScenePick p) =>
        $"{p.Place}, {p.Landscape}, {p.TimeOfDay}, {p.Weather}";
}
