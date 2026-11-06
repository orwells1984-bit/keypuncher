using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Director.Tests;

/// <summary>The changes the Nov 2026 bench recommended: the examples in the baseline, the place kept in a name but not
/// in the repeat check, vehicles and objects as one kind for the checker, well-known names unasked, and the element
/// of a thing against the landscape in code.</summary>
public class RecommendationTests
{
    private static readonly DirectorRequest AtBled = new(0, RecipeKind.Creature, null, ["ferry", "brown trout"],
        Place: "that really lives at Lake Bled, Slovenia", Here: "at Lake Bled, Slovenia", Landscape: Landscape.Lake);

    private static string Pick(string subject, string category, string shape) =>
        $$"""
        {"subject":"{{subject}}","category":"{{category}}","fact":"Found in and around Lake Bled.",
         "shape":"{{shape}}","lobes":3,"hue":20,"size":0.5,"motion":{"type":"Drift","speed":0.4},
         "voice":{"wave":"Sine","attack":0.01,"decay":0.4,"scale":"pentatonic","root":"E5"},"growsInto":null}
        """;

    private static string Check(string what, string verdict, string kind) => JsonSerializer.Serialize(new { whatItIs = what, verdict, kind, growsInto = "none" });

    private static JsonElement Fit(string[] elements, string continent = "Europe", string climate = "Temperate") => JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        placeContinent = continent, placeClimate = climate, thingContinents = new[] { continent, "Asia" }, thingClimates = new[] { climate }, thingElements = elements,
    })).RootElement;

    private static bool Build(string json, DirectorRequest request, out string? error) =>
        RealThingRules.TryBuild(JsonDocument.Parse(json), request, out _, out _, out error);

    [Theory]
    [InlineData("slovenian ferry", "ferry")]
    [InlineData("lake bled ferry", "ferry")]
    [InlineData("lake bled", "")]
    [InlineData("icelandic horse", "icelandic horse")]
    [InlineData("brown trout", "brown trout")]
    public void The_place_and_its_people_come_off_a_name_for_the_repeat_check(string name, string key) =>
        Assert.Equal(key, RealThingRules.PlaceKey(name, AtBled.Place, AtBled.Here));

    [Theory]
    [InlineData("at Wadi Rum, Jordan", "jordanian")]
    [InlineData("at Reynisfjara, Iceland", "icelandic")]
    [InlineData("at Kruger National Park, South Africa", "african")]
    [InlineData("in Switzerland", "swiss")]
    [InlineData("in Italy", "italian")]
    [InlineData("in England", "english")]
    public void A_people_is_made_from_its_place(string place, string people) => Assert.Contains(people, RealThingRules.PlaceWords(place));

    [Fact]
    public void A_pick_keeps_a_place_in_its_name_unless_it_is_a_repeat_or_the_place_itself()
    {
        // Real kinds carry places in their names; the child never sees the name, so it is kept whole.
        Assert.True(Build(Pick("slovenian carp", "Animal", "Fish"), AtBled, out var error), error);
        Assert.True(Build(Pick("iceland poppy", "Plant", "Petal"), AtBled with { Kind = RecipeKind.Plant, Category = RealCategory.Plant }, out error), error);
        // The ferry is already there, under whatever name.
        Assert.False(Build(Pick("lake bled ferry", "Vehicle", "Stripe"), AtBled with { Category = RealCategory.Vehicle }, out error));
        Assert.Equal("already in the world", error);
        Assert.False(Build(Pick("trout", "Animal", "Fish"), AtBled with { Existing = ["slovenian trout"] }, out error));
        Assert.Equal("already in the world", error);
        // The place is not a thing in it.
        Assert.False(Build(Pick("lake bled", "Animal", "Fish"), AtBled, out error));
        Assert.Equal("the place, not a thing in it", error);
    }

    [Fact]
    public void A_vehicle_and_an_object_pass_for_each_other_but_not_for_an_animal()
    {
        var boat = new FactCheck("a small boat", "real", "Object", "none");
        Assert.True(boat.Passes(RealCategory.Vehicle));
        Assert.True(boat.Passes(RealCategory.Object));
        Assert.False(boat.Passes(RealCategory.Animal));
        Assert.False(new FactCheck("an antelope", "real", "Animal", "none").Passes(RealCategory.Vehicle));
        Assert.False(new FactCheck("unknown", "unsure", "None", "none").Passes(RealCategory.Object));
    }

    [Fact]
    public async Task A_well_known_vehicle_or_extinct_animal_passes_the_checker_unasked_in_the_kind_claimed()
    {
        Assert.Equal(RealCategory.Vehicle, RealThingRules.WellKnownKind("Jeep"));
        Assert.Equal(RealCategory.Vehicle, RealThingRules.WellKnownKind("pickup trucks"));
        Assert.Equal(RealCategory.ExtinctAnimal, RealThingRules.WellKnownKind("woolly rhinoceros"));
        Assert.Null(RealThingRules.WellKnownKind("jeepaloo"));
        Assert.Null(RealThingRules.WellKnownKind("gazelle"));

        var unsure = Check("unknown", "unsure", "None");
        var model = new RealThingTests.ScriptedModel(unsure);
        var checker = new FactChecker(model);
        Assert.True((await checker.CheckAsync("jeep", null, RealCategory.Vehicle)).Passes(RealCategory.Vehicle));
        Assert.True((await checker.CheckAsync("jeep", null, RealCategory.Object)).Passes(RealCategory.Object));
        Assert.True((await checker.CheckAsync("woolly rhinoceros", null, RealCategory.ExtinctAnimal)).Passes(RealCategory.ExtinctAnimal));
        Assert.Empty(model.Prompts);
        // The name alone does not pass for another kind: a "jeep" offered as an animal is still asked about.
        Assert.False((await checker.CheckAsync("jeep", null, RealCategory.Animal)).Passes(RealCategory.Animal));
        Assert.Single(model.Prompts);
        // A growth claim on a well-known thing is not confirmed.
        Assert.False((await checker.CheckAsync("dodo", "phoenix", RealCategory.ExtinctAnimal)).GrowsIntoConfirmed);

        // The bench measures the model alone.
        var measured = new RealThingTests.ScriptedModel(unsure);
        Assert.False((await new FactChecker(measured) { TrustWellKnown = false }.CheckAsync("jeep", null, RealCategory.Vehicle)).Passes(RealCategory.Vehicle));
        Assert.Single(measured.Prompts);
    }

    [Fact]
    public void A_things_element_comes_from_its_name_then_its_shape_then_the_models_answer()
    {
        Assert.Equal(Element.Sea, Habitat.Of("shark", Shape.Shark, Fit(["FreshWater"])));           // the name is known
        Assert.Equal(Element.Sea, Habitat.Of("great white", Shape.Shark, Fit(["FreshWater"])));     // the shape is a sea shape
        Assert.Equal(Element.FreshWater, Habitat.Of("pike", null, null));
        Assert.Equal(Element.FreshWater, Habitat.Of("northern pike", null, null));                   // "pike" is the last word
        Assert.Equal(Element.FreshWater, Habitat.Of("grayling", Shape.Fish, Fit(["FreshWater"])));   // a Fish, narrowed by the answer
        Assert.Equal(Element.Land | Element.Air, Habitat.Of("hoopoe", Shape.Bird, Fit(["Land", "Air"])));
        Assert.Equal(Element.All, Habitat.Of("hoopoe", null, null));                                  // nothing known: anywhere
        Assert.Equal(Element.Sea | Element.FreshWater, Habitat.Of("mudfish", Shape.Fish, Fit(["Land"])));   // shape and answer disagree: the shape
    }

    [Fact]
    public void Sea_creatures_are_not_found_at_a_lake_nor_fish_in_a_desert_nor_trout_on_a_beach()
    {
        var sea = Fit(["Sea"]);
        Assert.False(FactChecker.Fits(sea, Landscape.Lake, "shark", Shape.Shark));
        Assert.False(FactChecker.Fits(sea, Landscape.Lake, "jellyfish"));
        Assert.True(FactChecker.Fits(sea, Landscape.Beach, "jellyfish"));
        Assert.False(FactChecker.Fits(sea, Landscape.Savanna, "whale", Shape.Whale));
        Assert.True(FactChecker.Fits(Fit(["FreshWater"]), Landscape.Lake, "brown trout", Shape.Fish));
        Assert.False(FactChecker.Fits(Fit(["FreshWater"], "Asia", "Dry"), Landscape.Desert, "brown trout", Shape.Fish));
        Assert.False(FactChecker.Fits(Fit(["FreshWater"]), Landscape.Beach, "pike", Shape.Fish));
        Assert.True(FactChecker.Fits(Fit(["Sea", "Land"]), Landscape.Beach, "harbour seal", Shape.Seal));
        Assert.True(FactChecker.Fits(Fit(["Land"]), Landscape.Lake, "roe deer", Shape.Sheep));
        Assert.True(FactChecker.Fits(Fit(["Land"]), null, "roe deer", Shape.Sheep));                      // no landscape known: continent and climate only
        var elsewhere = JsonDocument.Parse(JsonSerializer.Serialize(new { placeContinent = "Europe", placeClimate = "Temperate", thingContinents = new[] { "Africa" }, thingClimates = new[] { "Dry" }, thingElements = new[] { "Land" } })).RootElement;
        Assert.False(FactChecker.Fits(elsewhere, Landscape.Lake, "gazelle", Shape.Sheep));                 // right element, wrong continent
        Assert.True(FactChecker.Fits(Fit(["Land"]), Landscape.Lake, "kangaroo", Shape.Rabbit));             // an element answer cannot catch a kangaroo in Europe: continents must
    }

    [Fact]
    public async Task The_director_turns_a_shark_away_from_a_lake_and_keeps_the_trout()
    {
        var author = new RealThingTests.ScriptedModel(Pick("tiger shark", "Animal", "Shark"), Pick("grayling", "Animal", "Fish"));
        var fits = JsonSerializer.Serialize(new { placeContinent = "Europe", placeClimate = "Temperate", thingContinents = new[] { "Europe" }, thingClimates = new[] { "Temperate" }, thingElements = new[] { "Sea" } });
        var fresh = JsonSerializer.Serialize(new { placeContinent = "Europe", placeClimate = "Temperate", thingContinents = new[] { "Europe" }, thingClimates = new[] { "Temperate" }, thingElements = new[] { "FreshWater" } });
        var checker = new RealThingTests.ScriptedModel(Check("a shark", "real", "Animal"), fits, Check("a river fish", "real", "Animal"), fresh);
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(AtBled);

        Assert.Equal("grayling", outcome.Recipe!.Name);
        Assert.Equal("checker: not found at Lake Bled, Slovenia", outcome.Attempts[0].Refused);
        Assert.Contains("thingElements", checker.Prompts[1]);
        Assert.Contains("tiger shark (not found at Lake Bled, Slovenia)", author.Prompts[1]);
    }

    [Fact]
    public void The_toys_requests_carry_the_scenes_landscape()
    {
        var scene = new Scene(Landscape.Lake, Place: "Lake Bled, Slovenia");
        Assert.Equal("that really lives at Lake Bled, Slovenia", DirectorLoop.HerePlace(scene, RealCategory.Animal));
        Assert.Equal(Element.NotSea, Habitat.Allowed(Landscape.Lake));
        Assert.Equal(Element.NotFreshWater, Habitat.Allowed(Landscape.Beach));
        Assert.Equal(Element.Dry, Habitat.Allowed(Landscape.Forest));
        Assert.Equal(Element.All, Habitat.Allowed(null));
    }
}
