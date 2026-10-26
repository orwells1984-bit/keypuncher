using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using static Keypuncher.Director.Tests.RealThingTests;

namespace Keypuncher.Director.Tests;

public class SceneTests
{
    private static readonly Scene Vik = new(Landscape.Beach, TimeOfDay.Morning, Weather.Cloudy, "Reynisfjara, Iceland", "A black sand beach.", Snows: true);
    private static readonly SceneRequest FromVik = new(Vik, ["Lake Myvatn, Iceland"], ["puffin", "seal"]);

    [Fact]
    public void The_schema_holds_each_kind_of_step_time_only_goes_forward_and_a_rainbow_only_follows_rain()
    {
        static string[] Values(JsonElement props, string name) => [.. props.GetProperty(name).GetProperty("enum").EnumerateArray().Select(e => e.GetString()!)];

        var journey = SceneSchema.For(FromVik).GetProperty("properties");
        Assert.Equal(["Morning"], Values(journey, "timeOfDay"));
        Assert.Equal(Enum.GetNames<Landscape>(), Values(journey, "landscape"));
        Assert.DoesNotContain("Rainbow", Values(journey, "weather"));

        var weather = SceneSchema.For(FromVik with { Change = SceneChange.Weather }).GetProperty("properties");
        Assert.Equal(["Beach"], Values(weather, "landscape"));
        Assert.Equal(["Morning"], Values(weather, "timeOfDay"));
        Assert.DoesNotContain("Cloudy", Values(weather, "weather"));
        // Staying, the place is held to its name and no new fact is asked for; a journey asks for one.
        Assert.Equal(["Reynisfjara, Iceland"], Values(weather, "place"));
        Assert.False(weather.TryGetProperty("fact", out _));
        Assert.True(journey.TryGetProperty("fact", out _));

        // Snow is not offered where it cannot fall: the model chose it three times running for Wadi Rum.
        var wadiRum = new Scene(Landscape.Desert, TimeOfDay.Midday, Weather.Clear, "Wadi Rum, Jordan", "A desert valley.", Snows: false);
        Assert.DoesNotContain("Snow", Values(SceneSchema.For(FromVik with { Current = wadiRum, Change = SceneChange.Weather }).GetProperty("properties"), "weather"));
        Assert.DoesNotContain("Snow", Values(SceneSchema.For(FromVik with { Current = Vik with { Snows = false }, Change = SceneChange.Weather }).GetProperty("properties"), "weather"));
        Assert.Contains("Snow", Values(weather, "weather"));

        var evening = SceneSchema.For(FromVik with { Change = SceneChange.Time, Current = Vik with { TimeOfDay = TimeOfDay.Evening, Weather = Weather.Rain } }).GetProperty("properties");
        Assert.Equal(["Dawn"], Values(evening, "timeOfDay"));
        Assert.Contains("Rainbow", Values(evening, "weather"));
    }

    [Fact]
    public void The_loop_chooses_the_kind_of_step_so_the_scene_really_travels()
    {
        Assert.Equal(SceneChange.Journey, DirectorLoop.ChooseChange(Scene.Default, 0, 0.9));
        Assert.Equal(SceneChange.Journey, DirectorLoop.ChooseChange(Vik, DirectorLoop.StepsBeforeJourney, 0.9));
        Assert.Equal(SceneChange.Journey, DirectorLoop.ChooseChange(Vik, 0, 0.1));
        Assert.Equal(SceneChange.Weather, DirectorLoop.ChooseChange(Vik, 1, 0.5));
        Assert.Equal(SceneChange.Time, DirectorLoop.ChooseChange(Vik, 1, 0.9));
    }

    [Fact]
    public void A_small_real_step_is_taken()
    {
        Assert.True(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Rain"), FromVik with { Change = SceneChange.Weather }, out var scene, out var newPlace, out var error), error);
        Assert.False(newPlace);
        // The model's new fact is ignored: a place keeps the fact it was given on the journey there.
        Assert.Equal(Vik with { Weather = Weather.Rain }, scene);

        Assert.True(Build(Step("Reynisfjara, Iceland", "Beach", "Midday", "Clear"), FromVik with { Change = SceneChange.Time }, out scene, out newPlace, out error), error);
        Assert.Equal((TimeOfDay.Midday, Weather.Clear), (scene!.TimeOfDay, scene.Weather));

        Assert.True(Build(Step("Thingvellir, Iceland", "LavaField", "Morning", "Rain"), FromVik, out scene, out newPlace, out error), error);
        Assert.True(newPlace);
        Assert.Equal((Landscape.LavaField, Weather.Rain, "Thingvellir, Iceland"), (scene!.Landscape, scene.Weather, scene.Place));
        Assert.False(scene.Snows);   // until the place check says so
    }

    [Theory]
    [InlineData(SceneChange.Time, "Reynisfjara, Iceland", "Beach", "Dawn", "Rain", "time only goes forward")]
    [InlineData(SceneChange.Weather, "Reynisfjara, Iceland", "Beach", "Morning", "Rainbow", "a rainbow only comes after rain")]
    [InlineData(SceneChange.Journey, "Sahara, Morocco", "Desert", "Morning", "Snow", "it never snows in the desert")]
    [InlineData(SceneChange.Weather, "Reynisfjara, Iceland", "Forest", "Morning", "Rain", "a new landscape needs a new place")]
    [InlineData(SceneChange.Weather, "Reynisfjara, Iceland", "Beach", "Morning", "Cloudy", "should have changed only the weather")]
    [InlineData(SceneChange.Weather, "Bondi Beach, Australia", "Beach", "Morning", "Clear", "should have changed only the weather")]
    [InlineData(SceneChange.Time, "Reynisfjara, Iceland", "Beach", "Morning", "Rain", "should have moved the time on")]
    [InlineData(SceneChange.Journey, "Reynisfjara, Iceland", "Beach", "Morning", "Rain", "should have gone somewhere new")]
    [InlineData(SceneChange.Journey, "Bondi Beach, Australia", "Beach", "Midday", "Clear", "changed everything at once")]
    [InlineData(SceneChange.Journey, "Lake Myvatn, Iceland", "Lake", "Morning", "Cloudy", "been there lately")]
    [InlineData(SceneChange.Journey, "Seren: Tanzania", "Savanna", "Morning", "Cloudy", "not a plain place name")]
    public void Steps_the_real_world_does_not_take_are_refused(SceneChange change, string place, string landscape, string time, string weather, string why)
    {
        Assert.False(Build(Step(place, landscape, time, weather), FromVik with { Change = change }, out _, out _, out var error));
        Assert.Equal(why, error);
    }

    [Fact]
    public void Snow_at_the_same_place_needs_the_place_to_have_snow()
    {
        var noSnow = FromVik with { Current = Vik with { Snows = false }, Change = SceneChange.Weather };
        Assert.False(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Snow"), noSnow, out _, out _, out var error));
        Assert.Equal("it never snows there", error);
        Assert.True(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Snow"), noSnow with { Current = Vik }, out _, out _, out _));
    }

    [Fact]
    public async Task A_new_place_must_be_real_and_look_like_its_landscape_and_staying_needs_no_check()
    {
        var author = new ScriptedModel(
            Step("Narnia, England", "Forest", "Morning", "Cloudy"),
            Step("Thingvellir, Iceland", "Desert", "Morning", "Cloudy"),
            Step("Thingvellir, Iceland", "LavaField", "Morning", "Snow"));
        var checker = new ScriptedModel(
            Place("a land in a story", "fictional", []),
            Place("a national park in Iceland", "real", ["LavaField", "Lake"], "true"),
            Place("a national park in Iceland", "real", ["LavaField", "Lake"], "true"));
        var outcome = await new SceneDirector(author, new PlaceChecker(checker)).ProposeAsync(FromVik);

        Assert.Equal(["checker: not a real place", "checker: not Desert country", null], outcome.Attempts.Select(a => a.Refused));
        // A known place's own fact replaces the model's.
        var thingvellir = KnownPlace.All.Single(p => p.Name == "Thingvellir, Iceland");
        Assert.Equal(new Scene(Landscape.LavaField, TimeOfDay.Morning, Weather.Snow, "Thingvellir, Iceland", thingvellir.Fact, Snows: true), outcome.Scene);
        Assert.Contains("Narnia, England", author.Prompts[1]);
        Assert.Contains("puffin", author.Prompts[0]);
        Assert.Contains("Lake Myvatn, Iceland", author.Prompts[0]);

        var stay = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Midday", "Cloudy"));
        var neverAsked = new ScriptedModel("not json");
        var staying = await new SceneDirector(stay, new PlaceChecker(neverAsked)).ProposeAsync(FromVik with { Change = SceneChange.Time });
        Assert.Equal(TimeOfDay.Midday, staying.Scene!.TimeOfDay);
        Assert.Empty(neverAsked.Prompts);
    }

    [Fact]
    public async Task Snow_at_a_new_place_needs_the_checker_to_say_it_snows_there()
    {
        var author = new ScriptedModel(Step("Bondi Beach, Australia", "Beach", "Morning", "Snow"));
        var checker = new ScriptedModel(Place("a beach in Sydney", "real", ["Beach"], "false"));
        var outcome = await new SceneDirector(author, new PlaceChecker(checker)).ProposeAsync(FromVik);
        Assert.False(outcome.Accepted);
        Assert.All(outcome.Attempts.Take(SceneDirector.MaxAttempts), a => Assert.Equal("checker: it does not snow there", a.Refused));
        Assert.StartsWith("should have been ", outcome.Attempts[^1].Refused);
    }

    [Fact]
    public async Task A_journey_offers_known_places_and_if_every_try_misses_goes_to_one()
    {
        var stuck = Step("Reynisfjara, Iceland", "Beach", "Morning", "Rain");
        var author = new GoesWhereTold(stuck);
        var checker = new ScriptedModel(Place("a valley in Iceland", "real", ["Forest"], "true"));
        var outcome = await new SceneDirector(author, new PlaceChecker(checker), new Random(2)).ProposeAsync(FromVik);

        Assert.Equal(SceneDirector.MaxAttempts + 1, outcome.Attempts.Count);
        Assert.All(outcome.Attempts.Take(SceneDirector.MaxAttempts), a => Assert.Equal("should have gone somewhere new", a.Refused));
        Assert.Contains("If you are not sure of a place, choose one of these:", author.Prompts[0]);
        Assert.DoesNotContain("Reynisfjara, Iceland (Beach)", author.Prompts[0]);
        Assert.DoesNotContain("Lake Myvatn, Iceland (Lake)", author.Prompts[0]);
        // The known place's own landscape is used, even though the checker named another.
        var known = KnownPlace.All.Single(p => p.Name == outcome.Scene!.Place);
        Assert.Equal(known.Landscape, outcome.Scene!.Landscape);
        Assert.Contains($"Travel to {known.Name}", author.Prompts[^1]);
    }

    [Fact]
    public async Task A_known_place_the_checker_is_unsure_of_is_still_used_but_an_unknown_one_is_not()
    {
        var known = new ScriptedModel(Step("Eldhraun, Iceland", "LavaField", "Morning", "Cloudy"));
        var outcome = await new SceneDirector(known, new PlaceChecker(new ScriptedModel(Place("unknown", "unsure", []))), new Random(1)).ProposeAsync(FromVik);
        Assert.Equal("Eldhraun, Iceland", outcome.Scene!.Place);

        var unknown = new ScriptedModel(Step("Koh Rong San, Cambodia", "Beach", "Morning", "Cloudy"));
        var refused = await new SceneDirector(unknown, new PlaceChecker(new ScriptedModel(Place("unknown", "unsure", []))), new Random(1)).ProposeAsync(FromVik);
        Assert.Equal("checker: not sure it is a real place", refused.Attempts[0].Refused);
    }

    [Theory]
    [InlineData("Savannah, Botswana")]
    [InlineData("Desert, Egypt")]
    [InlineData("Meadow, not yet a named place")]
    [InlineData("The Lake, Iceland")]
    public void A_kind_of_country_is_not_a_named_place(string place)
    {
        Assert.False(Build(Step(place, "Savanna", "Morning", "Clear"), FromVik, out _, out _, out var error));
        Assert.Equal("a kind of country, not a named place", error);
    }

    /// <summary>Answers with a fixed step until told where it must go, then goes there.</summary>
    private sealed class GoesWhereTold(string answer) : Keypuncher.Inference.ILanguageModel
    {
        public List<string> Prompts { get; } = [];
        public string ModelName => "scripted";

        public Task<Keypuncher.Inference.GenerationResult> GenerateJsonAsync(string systemPrompt, string userPrompt, JsonElement jsonSchema, CancellationToken cancellationToken = default)
        {
            Prompts.Add(userPrompt);
            var must = System.Text.RegularExpressions.Regex.Match(userPrompt, "place must be exactly \"([^\"]+)\", landscape (\\w+)");
            var raw = must.Success ? Step(must.Groups[1].Value, must.Groups[2].Value, "Morning", "Clear") : answer;
            return Task.FromResult(new Keypuncher.Inference.GenerationResult(raw, JsonDocument.Parse(raw),
                new Keypuncher.Inference.GenerationTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0)));
        }
    }

    [Fact]
    public async Task The_loop_moves_the_scene_between_family_changes_and_remembers_where_it_has_been()
    {
        var world = new World();
        // The second step may be any kind; one of its three tries fits each.
        var author = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Morning", "Cloudy"),
            Step("Lake Myvatn, Iceland", "Lake", "Morning", "Cloudy"), Step("Reynisfjara, Iceland", "Beach", "Morning", "Rain"), Step("Reynisfjara, Iceland", "Beach", "Midday", "Cloudy"));
        var checker = new ScriptedModel(Place("a beach in Iceland", "real", ["Beach"], "true"), Place("a lake in Iceland", "real", ["Lake"], "true"));
        var things = new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel()));
        using var loop = new DirectorLoop(things, new Random(1), scenes: new SceneDirector(author, new PlaceChecker(checker)));
        var limits = CalmLimits.Toddler with { RestyleMinutes = 100, SceneMinutes = 2 };
        var noted = new List<string>();
        loop.Noted += noted.Add;

        loop.Update(world, limits, enabled: true);
        // The first family request comes first; the model is unreachable, so it is logged and tried again later.
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, limits, enabled: true);
        await Settle(loop, world, limits);
        Assert.Contains("refused", loop.Log[0].Error);
        Assert.Contains(noted, l => l.Contains("asking key family"));
        Assert.Contains(noted, l => l.Contains("could not reach the model: connection refused"));

        // The first scene step comes a minute into play, sooner than the calm level's interval.
        Tick(world, DirectorLoop.FirstSceneAfterSeconds - DirectorLoop.FirstAfterSeconds - 1);
        loop.Update(world, limits, enabled: true);
        Assert.False(loop.Busy);
        Tick(world, 1);
        loop.Update(world, limits, enabled: true);
        Assert.True(loop.Busy);
        await Settle(loop, world, limits);
        Assert.Equal("Reynisfjara, Iceland", world.Scene.Place);
        Assert.Contains(noted, l => l.Contains("asking for a scene step (Journey)"));
        Assert.Contains(noted, l => l.Contains("scene: Reynisfjara, Iceland, Beach, Morning, Cloudy."));
        Assert.Equal(Scene.Default, world.PreviousScene);
        Assert.Equal(world.Time, world.SceneChangedAt);
        Assert.True(world.Dirty);
        Assert.True(Assert.Single(loop.Log, e => e.Scene is not null).Scene!.Accepted);

        // Family requests now often ask for something found here.
        var places = Enumerable.Range(0, 40).Select(_ => loop.NextRequest(world.Families, world.Scene)!)
            .Where(r => r.Category is RealCategory.Animal or RealCategory.Plant).Select(r => r.Place).ToList();
        Assert.Contains(places, p => p!.Contains("Reynisfjara, Iceland"));
        Assert.Contains(places, p => !p!.Contains("Reynisfjara"));

        var first = world.Scene;
        Tick(world, 120);
        loop.Update(world, limits, enabled: true);
        await Settle(loop, world, limits);
        Assert.NotEqual(first, world.Scene);
        if (world.Scene.Place == "Lake Myvatn, Iceland") Assert.Equal(["Reynisfjara, Iceland"], loop.NextSceneRequest(world).RecentPlaces);
        else Assert.Equal("Reynisfjara, Iceland", world.Scene.Place);
    }

    [Fact]
    public async Task With_no_wait_set_the_loop_alternates_keys_and_scenes_back_to_back()
    {
        var world = new World();
        var author = new ScriptedModel(
            JsonSerializer.Serialize(new { subject = "hedgehog", category = "Animal", fact = "Lives in gardens.", shape = "Blob", lobes = 3, hue = 30, size = 0.4,
                motion = new { type = "Hop", speed = 0.3 }, voice = new { wave = "Sine", attack = 0.01, decay = 0.4, scale = "pentatonic", root = "C4" }, growsInto = (string?)null }));
        var things = new RealThingDirector(author, new FactChecker(new ScriptedModel(JsonSerializer.Serialize(new { whatItIs = "a small mammal", verdict = "real", kind = "Animal", growsInto = "none" }))));
        var sceneAuthor = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Morning", "Cloudy"));
        var scenes = new SceneDirector(sceneAuthor, new PlaceChecker(new ScriptedModel(Place("a beach in Iceland", "real", ["Beach"], "true"))));
        using var loop = new DirectorLoop(things, new Random(1), [RealCategory.Animal], scenes);
        var fast = CalmLimits.Baby with { RestyleMinutes = 0, SceneMinutes = 0 };

        var kinds = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            loop.Update(world, fast, enabled: true);
            Assert.True(loop.Busy);
            await Settle(loop, world, fast);
            kinds.Add(loop.Log[0].Scene is not null);
        }
        Assert.Equal([true, false, true, false], kinds);
        Assert.Equal("Reynisfjara, Iceland", world.Scene.Place);
    }

    [Fact]
    public async Task An_unreachable_model_is_not_asked_again_at_once_even_with_no_wait_set()
    {
        var world = new World();
        using var loop = new DirectorLoop(new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel())));
        var fast = CalmLimits.Baby with { RestyleMinutes = 0, SceneMinutes = 0 };
        loop.Update(world, fast, enabled: true);
        await Settle(loop, world, fast);
        loop.Update(world, fast, enabled: true);
        Assert.False(loop.Busy);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, fast, enabled: true);
        Assert.True(loop.Busy);
        await Settle(loop, world, fast);
    }

    [Fact]
    public async Task A_scene_for_a_world_that_was_cleared_meanwhile_is_not_used()
    {
        var world = new World();
        var gate = new TaskCompletionSource();
        var author = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Morning", "Cloudy")) { Gate = gate.Task };
        var checker = new ScriptedModel(Place("a beach in Iceland", "real", ["Beach"], "true"));
        using var loop = new DirectorLoop(new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel())),
            scenes: new SceneDirector(author, new PlaceChecker(checker)));
        var limits = CalmLimits.Toddler with { RestyleMinutes = 100, SceneMinutes = 0.1 };
        loop.Update(world, limits, true);
        Tick(world, 6);
        loop.Update(world, limits, true);
        Assert.True(loop.Busy);
        world.SetScene(new Scene(Landscape.Farm));
        gate.SetResult();
        await Settle(loop, world, limits);
        Assert.Equal(Landscape.Farm, world.Scene.Landscape);
    }

    [Fact]
    public void A_scene_reads_plainly_for_grown_ups()
    {
        Assert.Equal("Reynisfjara, Iceland: on a beach, morning, cloudy", Vik.ToString());
        Assert.Equal("a meadow: in a meadow, morning, clear sky", Scene.Default.ToString());
    }

    private static async Task Settle(DirectorLoop loop, World world, CalmLimits limits)
    {
        for (var i = 0; i < 200 && loop.Busy; i++)
        {
            await Task.Delay(10);
            loop.Update(world, limits, enabled: false);
        }
        Assert.False(loop.Busy);
    }

    private static bool Build(string json, SceneRequest request, out Scene? scene, out bool newPlace, out string? error) =>
        SceneRules.TryBuild(JsonDocument.Parse(json), request, out _, out scene, out newPlace, out error);

    [Theory]
    [InlineData("true", "A real fact about the place.")]
    [InlineData("false", null)]
    [InlineData("unsure", null)]
    [InlineData("cut off", null)]
    public async Task Another_places_fact_is_kept_only_if_a_second_look_agrees(string verdict, string? fact)
    {
        var step = Step("Ring of Kerry, Ireland", "Farm", "Morning", "Cloudy");
        if (verdict == "cut off") step = step.Replace("about the place.", "about the");
        var author = new ScriptedModel(step);
        var checker = new ScriptedModel(Place("a road around a peninsula in Ireland", "real", ["Farm", "Mountains"]), JsonSerializer.Serialize(new { verdict = "true" == verdict ? "true" : "false" }));
        var outcome = await new SceneDirector(author, new PlaceChecker(checker)).ProposeAsync(FromVik);
        Assert.Equal(fact, outcome.Scene!.Fact);
        if (verdict == "cut off") Assert.Single(checker.Prompts);
        else Assert.Contains("A real fact about the place.", checker.Prompts[1]);
    }

    [Fact]
    public async Task Snowy_hills_fit_a_mountain_place_where_it_snows()
    {
        // The model chose snowy hills for the Rila Mountains, a known mountain place, and was turned down.
        var author = new ScriptedModel(Step("Rila Mountains, Bulgaria", "SnowyHills", "Morning", "Cloudy"));
        var checker = new ScriptedModel(Place("mountains in Bulgaria", "real", ["Mountains"], "true"));
        var outcome = await new SceneDirector(author, new PlaceChecker(checker)).ProposeAsync(FromVik);
        Assert.Equal(Landscape.SnowyHills, outcome.Scene!.Landscape);

        Assert.False(KnownPlace.Fits(Landscape.SnowyHills, [Landscape.Mountains], snows: false));
        Assert.False(KnownPlace.Fits(Landscape.Mountains, [Landscape.SnowyHills], snows: true));
    }

    [Fact]
    public void Every_known_place_has_one_short_fact_of_its_own()
    {
        Assert.All(KnownPlace.All, p => Assert.InRange(p.Fact.Length, 30, 120));
        Assert.Equal(KnownPlace.All.Count, KnownPlace.All.Select(p => p.Fact).Distinct().Count());
    }

    private static string Step(string place, string landscape, string time, string weather) =>
        JsonSerializer.Serialize(new { place, landscape, timeOfDay = time, weather, fact = "A real fact about the place." });

    private static string Place(string what, string verdict, string[] landscapes, string snows = "unsure") =>
        JsonSerializer.Serialize(new { whatItIs = what, verdict, landscapes, snows });
}
