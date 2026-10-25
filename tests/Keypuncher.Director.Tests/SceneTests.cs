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
    public void Time_only_goes_forward_and_a_rainbow_only_follows_rain_in_the_schema()
    {
        var props = SceneSchema.For(FromVik).GetProperty("properties");
        Assert.Equal(["Morning", "Midday"], props.GetProperty("timeOfDay").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("Rainbow", props.GetProperty("weather").ToString());
        var evening = SceneSchema.For(FromVik with { Current = Vik with { TimeOfDay = TimeOfDay.Evening, Weather = Weather.Rain } }).GetProperty("properties");
        Assert.Equal(["Evening", "Dawn"], evening.GetProperty("timeOfDay").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Rainbow", evening.GetProperty("weather").ToString());
    }

    [Fact]
    public void A_small_real_step_is_taken()
    {
        Assert.True(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Rain"), FromVik, out var scene, out var newPlace, out var error), error);
        Assert.False(newPlace);
        Assert.Equal(Vik with { Weather = Weather.Rain, Fact = "A real fact about the place." }, scene);

        Assert.True(Build(Step("Thingvellir, Iceland", "LavaField", "Midday", "Cloudy"), FromVik, out scene, out newPlace, out error), error);
        Assert.True(newPlace);
        Assert.Equal((Landscape.LavaField, TimeOfDay.Midday, "Thingvellir, Iceland"), (scene!.Landscape, scene.TimeOfDay, scene.Place));
        Assert.False(scene.Snows);   // until the place check says so
    }

    [Theory]
    [InlineData("Reynisfjara, Iceland", "Beach", "Dawn", "Rain", "time only goes forward")]
    [InlineData("Reynisfjara, Iceland", "Beach", "Morning", "Rainbow", "a rainbow only comes after rain")]
    [InlineData("Sahara, Morocco", "Desert", "Morning", "Snow", "it never snows in the desert")]
    [InlineData("Reynisfjara, Iceland", "Forest", "Morning", "Cloudy", "a new landscape needs a new place")]
    [InlineData("Reynisfjara, Iceland", "Beach", "Morning", "Cloudy", "nothing changed")]
    [InlineData("Bondi Beach, Australia", "Beach", "Midday", "Clear", "changed everything at once")]
    [InlineData("Lake Myvatn, Iceland", "Lake", "Morning", "Cloudy", "been there lately")]
    [InlineData("Hogwarts {castle}", "Mountains", "Morning", "Cloudy", "not a plain place name")]
    public void Steps_the_real_world_does_not_take_are_refused(string place, string landscape, string time, string weather, string why)
    {
        Assert.False(Build(Step(place, landscape, time, weather), FromVik, out _, out _, out var error));
        Assert.Equal(why, error);
    }

    [Fact]
    public void Snow_at_the_same_place_needs_the_place_to_have_snow()
    {
        var noSnow = FromVik with { Current = Vik with { Snows = false } };
        Assert.False(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Snow"), noSnow, out _, out _, out var error));
        Assert.Equal("it never snows there", error);
        Assert.True(Build(Step("Reynisfjara, Iceland", "Beach", "Morning", "Snow"), FromVik, out _, out _, out _));
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
        Assert.Equal(new Scene(Landscape.LavaField, TimeOfDay.Morning, Weather.Snow, "Thingvellir, Iceland", "A real fact about the place.", Snows: true), outcome.Scene);
        Assert.Contains("Narnia, England", author.Prompts[1]);
        Assert.Contains("puffin", author.Prompts[0]);
        Assert.Contains("Lake Myvatn, Iceland", author.Prompts[0]);

        var stay = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Midday", "Cloudy"));
        var neverAsked = new ScriptedModel("not json");
        var staying = await new SceneDirector(stay, new PlaceChecker(neverAsked)).ProposeAsync(FromVik);
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
        Assert.All(outcome.Attempts, a => Assert.Equal("checker: it does not snow there", a.Refused));
    }

    [Fact]
    public async Task The_loop_moves_the_scene_between_family_changes_and_remembers_where_it_has_been()
    {
        var world = new World();
        var author = new ScriptedModel(Step("Reynisfjara, Iceland", "Beach", "Morning", "Cloudy"), Step("Lake Myvatn, Iceland", "Lake", "Morning", "Cloudy"));
        var checker = new ScriptedModel(Place("a beach in Iceland", "real", ["Beach"], "true"), Place("a lake in Iceland", "real", ["Lake"], "true"));
        var things = new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel()));
        using var loop = new DirectorLoop(things, new Random(1), scenes: new SceneDirector(author, new PlaceChecker(checker)));
        var limits = CalmLimits.Toddler with { RestyleMinutes = 100, SceneMinutes = 2 };

        loop.Update(world, limits, enabled: true);
        // The first family request comes first; the model is unreachable, so it is logged and tried again later.
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, limits, enabled: true);
        await Settle(loop, world, limits);
        Assert.Contains("refused", loop.Log[0].Error);

        Tick(world, 89);
        loop.Update(world, limits, enabled: true);
        Assert.False(loop.Busy);
        Tick(world, 1);
        loop.Update(world, limits, enabled: true);
        Assert.True(loop.Busy);
        await Settle(loop, world, limits);
        Assert.Equal("Reynisfjara, Iceland", world.Scene.Place);
        Assert.Equal(Scene.Default, world.PreviousScene);
        Assert.Equal(world.Time, world.SceneChangedAt);
        Assert.True(world.Dirty);
        Assert.True(Assert.Single(loop.Log, e => e.Scene is not null).Scene!.Accepted);

        // Family requests now often ask for something found here.
        var places = Enumerable.Range(0, 40).Select(_ => loop.NextRequest(world.Families, world.Scene)!)
            .Where(r => r.Category is RealCategory.Animal or RealCategory.Plant).Select(r => r.Place).ToList();
        Assert.Contains(places, p => p!.Contains("Reynisfjara, Iceland"));
        Assert.Contains(places, p => !p!.Contains("Reynisfjara"));

        Tick(world, 120);
        loop.Update(world, limits, enabled: true);
        await Settle(loop, world, limits);
        Assert.Equal("Lake Myvatn, Iceland", world.Scene.Place);
        Assert.Equal(["Reynisfjara, Iceland"], loop.NextSceneRequest(world).RecentPlaces);
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

    private static string Step(string place, string landscape, string time, string weather) =>
        JsonSerializer.Serialize(new { place, landscape, timeOfDay = time, weather, fact = "A real fact about the place." });

    private static string Place(string what, string verdict, string[] landscapes, string snows = "unsure") =>
        JsonSerializer.Serialize(new { whatItIs = what, verdict, landscapes, snows });
}
