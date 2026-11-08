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
/// <param name="Month">The month now, 1 to 12, for the season: weather must be usual for the place at this time of year.</param>
/// <param name="Rare">Whether this step may also bring weather that is rare there now, which the loop allows only
/// now and then (<see cref="Climates.RareChance"/>).</param>
/// <param name="Offer">For a weather step, the weathers the model may choose between, drawn by the loop: left to
/// choose from all of them, a small model only ever swaps clear for cloudy and back.</param>
public sealed record SceneRequest(Scene Current, IReadOnlyList<string> RecentPlaces, IReadOnlyList<string> Things,
    SceneChange Change = SceneChange.Journey, KnownPlace? MustGoTo = null, int Month = 1, bool Rare = false,
    IReadOnlyList<Weather>? Offer = null)
{
    /// <summary>The weathers a weather step from <paramref name="current"/> may bring now: usual there, not the weather
    /// it has, a rainbow only after rain.</summary>
    public static IEnumerable<Weather> Changes(Scene current, int month, bool rare) =>
        System.Enum.GetValues<Weather>().Where(w => w != current.Weather && (w != Weather.Rainbow || current.Weather == Weather.Rain)
            && current.Allows(w, month, rare));

    /// <summary>Whether <paramref name="w"/> may come now in <paramref name="climate"/>.</summary>
    public bool Allows(Climate climate, bool south, Weather w) => Climates.Allows(climate, south, Month, w, Rare);
}

/// <summary>A real place whose landscape and climate are known here rather than asked of the model.</summary>
/// <param name="Fact">One true sentence about the place, written by hand, shown to grown-ups instead of the model's.</param>
/// <param name="South">South of the equator, where the seasons are the other way round.</param>
public sealed record KnownPlace(string Name, Landscape Landscape, string Fact, Climate Climate, bool South = false)
{
    /// <summary>Well-known real places, Icelandic and Bulgarian ones among them, offered to the model on every journey
    /// so that it does not stay where it is or make up a name ("Savannah, Botswana"), and drawn from as a last resort.
    /// The model still chooses, and its choice is still checked. Names are written without accents.</summary>
    public static readonly IReadOnlyList<KnownPlace> All =
    [
        new("Thorsmork, Iceland", Landscape.Meadow, "Þórsmörk is a green valley between three glaciers in the south of Iceland.", Climate.Subpolar),
        new("Alpe di Siusi, Italy", Landscape.Meadow, "Alpe di Siusi, in the Dolomites, is the largest high mountain meadow in Europe.", Climate.Mountain),
        new("Lauterbrunnen, Switzerland", Landscape.Meadow, "Lauterbrunnen is a valley in the Swiss Alps with over seventy waterfalls.", Climate.Mountain),
        new("Fljotshlid, Iceland", Landscape.Farm, "Fljótshlíð is a farming district in south Iceland that looks across to the Eyjafjallajökull glacier.", Climate.Subpolar),
        new("Tuscany, Italy", Landscape.Farm, "Tuscany's farms grow grapes, olives and sunflowers on rolling hills.", Climate.Mediterranean),
        new("Cotswolds, England", Landscape.Farm, "Sheep farming made the Cotswolds rich, and its villages are built of golden limestone.", Climate.Temperate),
        new("Hallormsstadaskogur, Iceland", Landscape.Forest, "Hallormsstaðaskógur in east Iceland is the country's largest forest, of birch and planted trees.", Climate.Subpolar),
        new("Black Forest, Germany", Landscape.Forest, "The Black Forest in south-west Germany is named for its dark fir and spruce trees.", Climate.Continental),
        new("Bialowieza Forest, Poland", Landscape.Forest, "Białowieża Forest is one of the last old forests of Europe's lowlands, and wild bison live there.", Climate.Continental),
        new("Amazon rainforest, Brazil", Landscape.Jungle, "The Amazon is the largest rainforest on Earth.", Climate.Rainforest, South: true),
        new("Daintree Rainforest, Australia", Landscape.Jungle, "The Daintree in Queensland is one of the oldest rainforests in the world.", Climate.Rainforest, South: true),
        new("Borneo, Malaysia", Landscape.Jungle, "Borneo's rainforests are one of the few places where wild orangutans live.", Climate.Rainforest),
        new("Landmannalaugar, Iceland", Landscape.Mountains, "Landmannalaugar has colourful rhyolite mountains and hot springs to bathe in.", Climate.Subarctic),
        new("Rila Mountains, Bulgaria", Landscape.Mountains, "Musala, in the Rila Mountains, is the highest peak in Bulgaria and the Balkans.", Climate.Mountain),
        new("Swiss Alps, Switzerland", Landscape.Mountains, "The Matterhorn, in the Swiss Alps, is one of the best-known mountains in the world.", Climate.Mountain),
        new("Banff, Canada", Landscape.Mountains, "Banff, in the Rocky Mountains, is Canada's oldest national park.", Climate.Mountain),
        new("Reynisfjara, Iceland", Landscape.Beach, "Reynisfjara is a black sand beach with cliffs of basalt columns.", Climate.Subpolar),
        new("Raudasandur, Iceland", Landscape.Beach, "Rauðasandur, in the Westfjords, is a long beach of red and golden sand.", Climate.Subpolar),
        new("Sunny Beach, Bulgaria", Landscape.Beach, "Sunny Beach is a long sandy beach on Bulgaria's Black Sea coast.", Climate.Temperate),
        new("Bondi Beach, Australia", Landscape.Beach, "Bondi Beach in Sydney is one of Australia's best-known beaches.", Climate.Subtropical, South: true),
        new("Lake Myvatn, Iceland", Landscape.Lake, "Lake Mývatn in north Iceland is full of nesting ducks in summer.", Climate.Subarctic),
        new("Jokulsarlon, Iceland", Landscape.Lake, "Jökulsárlón is a glacier lagoon where icebergs float out to the sea.", Climate.Subpolar),
        new("Lake Bled, Slovenia", Landscape.Lake, "Lake Bled has a small island with a church on it.", Climate.Continental),
        new("Seven Rila Lakes, Bulgaria", Landscape.Lake, "The Seven Rila Lakes lie one above another, high in the Rila Mountains.", Climate.Mountain),
        new("Sahara, Morocco", Landscape.Desert, "The Sahara is the largest hot desert in the world.", Climate.HotDesert),
        new("Namib Desert, Namibia", Landscape.Desert, "The Namib is thought to be the oldest desert in the world, and has tall red dunes.", Climate.FoggyDesert, South: true),
        new("Wadi Rum, Jordan", Landscape.Desert, "Wadi Rum is a desert valley of sand and red sandstone mountains.", Climate.HotDesert),
        new("Serengeti, Tanzania", Landscape.Savanna, "Every year more than a million wildebeest walk across the Serengeti.", Climate.Savanna, South: true),
        new("Masai Mara, Kenya", Landscape.Savanna, "The Masai Mara in Kenya is famous for its lions, cheetahs and wildebeest.", Climate.Savanna, South: true),
        new("Kruger National Park, South Africa", Landscape.Savanna, "Kruger National Park is one of the largest wildlife reserves in Africa.", Climate.Savanna, South: true),
        new("Vatnajokull, Iceland", Landscape.SnowyHills, "Vatnajökull is the largest glacier in Iceland.", Climate.Polar),
        new("Lapland, Finland", Landscape.SnowyHills, "Finnish Lapland is snowy for much of the year, and reindeer live there.", Climate.Subarctic),
        new("Svalbard, Norway", Landscape.SnowyHills, "Svalbard, far north of mainland Norway, has more polar bears than people.", Climate.Polar),
        new("Eldhraun, Iceland", Landscape.LavaField, "Eldhraun is a huge lava field from the Laki eruption of 1783, now under thick moss.", Climate.Subpolar),
        new("Thingvellir, Iceland", Landscape.LavaField, "At Þingvellir you can see the gap between the North American and Eurasian plates.", Climate.Subpolar),
        new("Reykjanes, Iceland", Landscape.LavaField, "Reykjanes is a peninsula of lava fields and volcanoes in south-west Iceland.", Climate.Subpolar),
    ];

    /// <summary>Whether <paramref name="claimed"/> fits a place where <paramref name="found"/> are found: snowy hills
    /// also fit mountains where it snows (the Rila Mountains).</summary>
    public static bool Fits(Landscape claimed, IEnumerable<Landscape> found, bool snows) =>
        found.Contains(claimed) || (claimed == Landscape.SnowyHills && snows && found.Contains(Landscape.Mountains));
}

/// <summary>The model's answer for one scene step.</summary>
public sealed record ScenePick
{
    [JsonPropertyName("place")] public required string Place { get; init; }
    [JsonPropertyName("landscape")] public required Landscape Landscape { get; init; }
    [JsonPropertyName("timeOfDay")] public required TimeOfDay TimeOfDay { get; init; }
    [JsonPropertyName("weather")] public required Weather Weather { get; init; }
    /// <summary>Only asked for on a journey: a place keeps the fact it was given when the world went there.</summary>
    [JsonPropertyName("fact")] public string? Fact { get; init; }
}

/// <summary>One scene the director tried, kept for the grown-ups' page and the sandbox.</summary>
public sealed record SceneAttempt(string Tried, string? Fact, string? Refused, PlaceCheck? Check);

/// <summary>What came of one scene request: the new scene, or nothing, and every attempt on the way.</summary>
public sealed record SceneOutcome(SceneRequest Request, Scene? Scene, IReadOnlyList<SceneAttempt> Attempts, TimeSpan Took)
{
    public bool Accepted => Scene is not null;
}

/// <summary>The JSON schema for <see cref="ScenePick"/>, narrowed per request to the kind of step asked for, so that
/// time only goes forward, a weather step really changes the weather, a rainbow only follows rain, and where the
/// climate is known, only weather usual there in this season is offered. Ollama holds enums at decode time.</summary>
public static class SceneSchema
{
    public static JsonElement For(SceneRequest request)
    {
        var now = request.Current;
        var journey = request.Change == SceneChange.Journey || now.Place is null;
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = journey ? new JsonArray("place", "landscape", "timeOfDay", "weather", "fact") : new JsonArray("place", "landscape", "timeOfDay", "weather"),
            ["properties"] = new JsonObject
            {
                // Staying put, or going where told, the place is held to its name: the model would otherwise shorten
                // it ("Jok: Jokulsarlon") and waste a try.
                ["place"] = request.MustGoTo is { } to ? Enum([to.Name])
                    : !journey && now.Place is { } here ? Enum([here])
                    : new JsonObject { ["type"] = "string", ["minLength"] = 3, ["maxLength"] = 48 },
                // A journey goes somewhere that looks different, so the child sees the world move: the backdrop is
                // all a journey changes, and savanna to savanna looked like nothing at all.
                ["landscape"] = Enum(request.MustGoTo is { } must ? [must.Landscape.ToString()]
                    : request.Change == SceneChange.Journey ? Enum<Landscape>().Where(l => l != now.Landscape.ToString()) : [now.Landscape.ToString()]),
                ["timeOfDay"] = Enum([(request.Change == SceneChange.Time ? Scene.Next(now.TimeOfDay) : now.TimeOfDay).ToString()]),
                ["weather"] = Enum(Enum<Weather>()
                    .Where(w => w != nameof(Weather.Rainbow) || now.Weather == Weather.Rain)
                    .Where(w => request.Change != SceneChange.Weather || w != now.Weather.ToString())
                    .Where(w => Fits(request, System.Enum.Parse<Weather>(w)))
                    .Where(w => request.Change != SceneChange.Weather || request.Offer is not { Count: > 0 } offer || offer.Contains(System.Enum.Parse<Weather>(w)))),
            },
        };
        if (journey) schema["properties"]!["fact"] = new JsonObject { ["type"] = "string", ["minLength"] = 8, ["maxLength"] = 120 };
        return JsonDocument.Parse(schema.ToJsonString()).RootElement.Clone();
    }

    /// <summary>Whether <paramref name="w"/> is usual now where the step goes, if that place's climate is known:
    /// staying put, or going to a known place. Left free, the model chose snow three times running for Wadi Rum. A new
    /// place the model chooses is not known until it is checked; the director then fits its weather.</summary>
    private static bool Fits(SceneRequest request, Weather w) =>
        request.MustGoTo is { } to ? request.Allows(to.Climate, to.South, w)
        : request.Change == SceneChange.Journey || request.Current.Place is null || w == request.Current.Weather
        || request.Allows(request.Current.Climate, request.Current.South, w);

    private static IEnumerable<string> Enum<T>() where T : struct, Enum => System.Enum.GetNames<T>();

    private static JsonObject Enum(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]) };
}

/// <summary>
/// The checks a scene must pass before its place is asked about. Like <see cref="RealThingRules"/>, they cannot
/// prove the place is real, but they hold the scene to how the real world works: time goes forward, a rainbow comes
/// after rain, the weather is usual for the place and season, a new landscape is a new place, a journey is to a new
/// landscape, and only one or two things change.
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
        catch (JsonException e)
        {
            Log.Warn("director", $"the scene answer did not fit the schema: {Log.Short(json.RootElement.GetRawText())}", e);
            error = "bad JSON: " + e.Message;
            return false;
        }
        if (pick is null) { error = "empty"; return false; }

        var now = request.Current;
        var place = Tidy(pick.Place);
        pick = pick with { Place = place };
        if (place.Length is < 3 or > 48) { error = "place name length"; return false; }
        if (!place.All(c => char.IsLetter(c) || c is ' ' or ',' or '-' or '\'' or '.')) { error = "not a plain place name"; return false; }
        var named = place.Split(',')[0].Trim().ToLowerInvariant();
        if (KindWords.Contains(named) || KindWords.Contains(named.Replace("the ", "")) || named.Contains("not yet")) { error = "a kind of country, not a named place"; return false; }
        if (request.MustGoTo is { } must && !SamePlace(place, must.Name)) { error = $"should have been {must.Name}"; return false; }
        newPlace = !SamePlace(place, now.Place);
        if (newPlace && string.IsNullOrWhiteSpace(pick.Fact)) { error = "no fact"; return false; }
        var asked = request.Change switch
        {
            SceneChange.Journey when !newPlace => "should have gone somewhere new",
            SceneChange.Journey when pick.Landscape == now.Landscape => "should have gone somewhere that looks different",
            SceneChange.Weather when newPlace || pick.Weather == now.Weather => "should have changed only the weather",
            SceneChange.Time when newPlace || pick.TimeOfDay == now.TimeOfDay => "should have moved the time on",
            _ => null,
        };
        if (asked is not null) { error = asked; return false; }
        if (pick.TimeOfDay != now.TimeOfDay && pick.TimeOfDay != Scene.Next(now.TimeOfDay)) { error = "time only goes forward"; return false; }
        if (pick.Weather == Weather.Rainbow && now.Weather != Weather.Rain) { error = "a rainbow only comes after rain"; return false; }
        if (!newPlace && pick.Weather != now.Weather && !request.Allows(now.Climate, now.South, pick.Weather))
        {
            var odds = Climates.Odds(now.Climate, now.South, request.Month, pick.Weather);
            error = $"{pick.Weather.ToString().ToLowerInvariant()} is {(odds == Likelihood.Rare ? "rare" : "unheard of")} there in {Climates.Season(request.Month, now.South)}";
            return false;
        }
        if (pick.Landscape != now.Landscape && !newPlace) { error = "a new landscape needs a new place"; return false; }
        if (newPlace && request.RecentPlaces.Any(p => SamePlace(p, place))) { error = "been there lately"; return false; }

        var changed = (newPlace ? 1 : 0) + (pick.TimeOfDay != now.TimeOfDay ? 1 : 0) + (pick.Weather != now.Weather ? 1 : 0);
        if (changed == 0) { error = "nothing changed"; return false; }
        if (changed == 3) { error = "changed everything at once"; return false; }

        // Small models sometimes slip a stray character from another script into the fact; it is shown to grown-ups.
        var fact = new string((pick.Fact ?? "").Trim().Where(c => c < 0x2E80).ToArray());
        // A place's fact is settled on the journey there; a change of weather or time keeps it.
        scene = newPlace
            ? new Scene(pick.Landscape, pick.TimeOfDay, pick.Weather, place, fact)
            : now with { TimeOfDay = pick.TimeOfDay, Weather = pick.Weather, Place = now.Place ?? place };
        return true;
    }
}

/// <summary>
/// Asks the model for the scene's next step: often a change of weather or the time of day moving on, sometimes a
/// journey to a new real place. Three layers, as for things: the prompt and schema, <see cref="SceneRules"/>, and
/// for a new place <see cref="PlaceChecker"/>, a cold second call that must say the place is real and that it looks
/// like the landscape claimed, and names its climate, so that the weather can be fitted to the place and the
/// season. A step that misses leaves the scene as it is.
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

    /// <summary>A few known places that are not here or somewhere recent, and do not look like here.</summary>
    private List<KnownPlace> Suggestions(SceneRequest request) =>
        [.. KnownPlace.All
            .Where(p => p.Landscape != request.Current.Landscape && !SceneRules.SamePlace(p.Name, request.Current.Place)
                && !request.RecentPlaces.Any(r => SceneRules.SamePlace(r, p.Name)))
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
                var (climate, south) = known is not null ? (known.Climate, known.South) : (Climates.Fit(check.Climate, pick.Landscape), check.South);
                var why = !real ? (check.Verdict == "fictional" ? "not a real place" : "not sure it is a real place")
                    : !KnownPlace.Fits(pick.Landscape, known is not null ? [known.Landscape] : check.Landscapes, Climates.CanSnow(climate)) ? $"not {pick.Landscape} country"
                    : null;
                if (why is not null)
                {
                    attempts.Add(new SceneAttempt(Describe(pick), pick.Fact, $"checker: {why}", check));
                    turnedDown.Add($"{Describe(pick)} ({why})");
                    continue;
                }
                // The fact is for grown-ups, and a small model's facts about places are often wrong: a known place
                // has its own, and any other keeps the model's only if a cold second look agrees with it.
                // A sentence cut off by the schema's length limit is not checked, only dropped.
                var whole = scene!.Fact is { } f && f.TrimEnd().EndsWith('.');
                var fact = known?.Fact ?? (whole && await checker.ConfirmFactAsync(pick.Place, scene.Fact!, cancellationToken) ? scene.Fact : null);
                // The weather was chosen before the place's climate was known; weather not usual there now is put
                // right here rather than sent back, which would waste a try on a place that is fine.
                var weather = FitWeather(request, pick.Weather, climate, south);
                scene = scene! with { Climate = climate, South = south, Weather = weather, Fact = fact };
                var tried = Describe(pick) + (weather == pick.Weather ? ""
                    : $" ({pick.Weather.ToString().ToLowerInvariant()} is not usual there in {Climates.Season(request.Month, south)}, so {weather.ToString().ToLowerInvariant()})");
                attempts.Add(new SceneAttempt(tried, fact ?? $"(fact not confirmed: {pick.Fact})", null, check));
            }
            else attempts.Add(new SceneAttempt(Describe(pick!), pick!.Fact, null, null));
            return scene;
        }
        return null;
    }

    /// <summary>The weather a journey arrives in: the model's choice if it is usual there now, otherwise the weather
    /// it left if that is, otherwise clear, or cloudy after rain, snow or fog.</summary>
    public static Weather FitWeather(SceneRequest request, Weather chosen, Climate climate, bool south)
    {
        if (request.Allows(climate, south, chosen)) return chosen;
        var was = request.Current.Weather;
        if (was != Weather.Rainbow && request.Allows(climate, south, was)) return was;
        return was is Weather.Rain or Weather.Snow or Weather.Fog ? Weather.Cloudy : Weather.Clear;
    }

    private static string Describe(ScenePick p) =>
        $"{p.Place}, {p.Landscape}, {p.TimeOfDay}, {p.Weather}";
}
