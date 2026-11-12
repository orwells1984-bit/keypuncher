using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using static Keypuncher.Director.Tests.RealThingTests;

namespace Keypuncher.Director.Tests;

/// <summary>The fixes for what the runs of 8 to 11 Nov 2026 showed the director doing wrong: things filed under the
/// kind the author was made to answer in, the same refused or present names offered again and again, growing back
/// into what a family just was, misspelt names and places offered as things.</summary>
public class DirectorPicksTests
{
    private static readonly DirectorRequest Objects = new(0, RecipeKind.Creature, null, ["cat", "dog"], Category: RealCategory.Object,
        Place: "that a child might really have at Bondi Beach, Australia");
    private static readonly DirectorRequest Plants = new(1, RecipeKind.Plant, null, ["coconut", "lilly pilly"], Category: RealCategory.Plant,
        Place: "that really grows at Bondi Beach, Australia");

    private static string Pick(string subject, string category, string shape, string? growsInto = null, params string[] ideas) =>
        JsonSerializer.Serialize(new
        {
            ideas = ideas.Length == 0 ? null : ideas,
            subject, category, fact = "Found along the coast.", shape, lobes = 3, hue = 30, size = 0.4,
            motion = new { type = "Drift", speed = 0.3 }, voice = new { wave = "Sine", attack = 0.05, decay = 0.6, scale = "pentatonic", root = "E5" },
            growsInto,
        });

    private static string Check(string what, string verdict, string kind, string? spelling = null) =>
        JsonSerializer.Serialize(new { whatItIs = what, spelling, verdict, kind, growsInto = "none" });

    [Fact]
    public async Task A_thing_the_checker_calls_another_kind_the_family_can_hold_is_used_and_filed_as_that()
    {
        // Run of 8 Nov: "sand dollar" asked for as an object, the checker rightly said a sea urchin, twice a request.
        var author = new ScriptedModel(Pick("sand dollar", "Object", "Blob"));
        var checker = new ScriptedModel(Check("a type of sea urchin", "real", "Animal"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(Objects);

        Assert.Equal("sand dollar", outcome.Recipe!.Name);
        Assert.Equal(RealCategory.Animal, outcome.Recipe.Category);
        Assert.Equal(2, outcome.Recipe.Eyes);   // a living thing drawn as a blob gets a face
        Assert.Contains("filed as Animal", outcome.Attempts.Single().Note);
    }

    [Fact]
    public async Task A_vehicle_the_checker_calls_an_object_is_filed_as_an_object()
    {
        // A beach ball can only be offered as a vehicle when a vehicle is asked for; it passes, and is filed as what it is.
        var author = new ScriptedModel(Pick("beach ball", "Vehicle", "Ball"));
        var checker = new ScriptedModel(Check("an inflatable ball", "real", "Object"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(Objects with { Category = RealCategory.Vehicle });
        Assert.Equal(RealCategory.Object, outcome.Recipe!.Category);

        // A well-known vehicle keeps its kind without a call.
        var jeep = await new RealThingDirector(new ScriptedModel(Pick("jeep", "Vehicle", "Car")), new FactChecker(new ScriptedModel()))
            .ProposeAsync(Objects with { Category = RealCategory.Vehicle });
        Assert.Equal(RealCategory.Vehicle, jeep.Recipe!.Category);
    }

    [Fact]
    public void Nothing_is_refiled_for_an_extinct_request_a_plant_family_or_an_unsure_checker()
    {
        var animal = new FactCheck("a seabird", "real", "Animal", "none");
        Assert.Null(RealThingDirector.Refile(animal, Objects with { Category = RealCategory.ExtinctAnimal }));
        Assert.Null(RealThingDirector.Refile(new FactCheck("unknown", "unsure", "Animal", "none"), Objects));
        Assert.Null(RealThingDirector.Refile(animal, Plants));
        Assert.Null(RealThingDirector.Refile(new FactCheck("a flower", "real", "Plant", "none"), Objects));
        Assert.Equal(RealCategory.Animal, RealThingDirector.Refile(animal, Objects));
    }

    [Fact]
    public async Task A_name_turned_down_earlier_in_the_play_is_named_and_not_checked_again()
    {
        var request = Objects with { TurnedDownBefore = [new TurnedDown("fluffaroo", "not real", RealCategory.Object)] };
        var author = new ScriptedModel(Pick("fluffaroo", "Object", "Blob"), Pick("bucket", "Object", "Blob"));
        var checker = new ScriptedModel(Check("a pail", "real", "Object"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(request);

        Assert.Equal("bucket", outcome.Recipe!.Name);
        Assert.Equal("turned down earlier: not real", outcome.Attempts[0].Refused);
        Assert.Single(checker.Prompts);
        Assert.Contains("fluffaroo (not real)", author.Prompts[0]);

        // One about a place holds only while the scene is there, and one about another kind is not named.
        var elsewhere = request with { TurnedDownBefore = [new TurnedDown("mango", "not found at Lake Bled, Slovenia", RealCategory.Object, "at Lake Bled, Slovenia")] };
        Assert.Null(elsewhere.TurnedDownEarlier("mango"));
        Assert.NotNull((elsewhere with { Here = "at Lake Bled, Slovenia" }).TurnedDownEarlier("mango"));
        var plants = new ScriptedModel(Pick("banksia", "Plant", "Cloud"));
        await new RealThingDirector(plants, new FactChecker(new ScriptedModel(Check("a shrub", "real", "Plant")))).ProposeAsync(Plants with { TurnedDownBefore = request.TurnedDownBefore });
        Assert.DoesNotContain("fluffaroo", plants.Prompts[0]);
    }

    [Fact]
    public async Task After_a_repeat_the_next_try_asks_for_the_models_own_next_idea_by_name()
    {
        // Run of 9 Nov: "coconut" six times at Bondi Beach, though one was in the world.
        var author = new ScriptedModel(
            Pick("coconut", "Plant", "Blob", null, "coconut", "lilly pilly", "banksia", "pigface"),
            Pick("banksia", "Plant", "Cloud", null, "banksia", "coconut", "sea grass"));
        var checker = new ScriptedModel(Check("an australian shrub", "real", "Plant"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(Plants);

        Assert.Equal("banksia", outcome.Recipe!.Name);
        Assert.Equal("already in the world", outcome.Attempts[0].Refused);
        Assert.Contains("subject must be \"banksia\"", author.Prompts[1]);   // lilly pilly is in the world too
        Assert.Equal("pigface", RealThingDirector.NextIdea(Plants, ["coconut", "lilly pilly", "bondi beach", "banksia", "pigface"],
            [new DirectorAttempt("banksia", null, "checker: unsure", null)]));
        Assert.Null(RealThingDirector.NextIdea(Plants with { GrowInto = "coconut palm" }, ["banksia"], []));
    }

    [Fact]
    public async Task A_near_miss_in_spelling_is_put_right_and_a_different_name_is_not_swapped_in()
    {
        // Run of 8 Nov: "lilly pillay", whose picture was never found.
        var author = new ScriptedModel(Pick("lilly pillay", "Plant", "DotField"));
        var checker = new ScriptedModel(Check("an australian tree with berries", "real", "Plant", spelling: "lilly pilly"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(Plants with { Existing = [] });
        Assert.Equal("lilly pilly", outcome.Recipe!.Name);
        Assert.Contains("spelt lilly pilly", outcome.Attempts.Single().Note);
        Assert.Contains("spelling:", checker.Prompts[0]);

        Assert.Null(FactChecker.Respelt("sable", "sable antelope"));
        Assert.Null(FactChecker.Respelt("puffin", "puffins"));
        Assert.Null(FactChecker.Respelt("puffin", ""));
        Assert.Null(FactChecker.Respelt("cat", "bat"));
        Assert.Equal("eucalyptus", FactChecker.Respelt("eucalyptis", "Eucalyptus"));

        // Put right, it may turn out to be in the world already.
        var again = await new RealThingDirector(new ScriptedModel(Pick("lilly pillay", "Plant", "DotField")),
            new FactChecker(new ScriptedModel(Check("a tree", "real", "Plant", spelling: "lilly pilly")))).ProposeAsync(Plants);
        Assert.False(again.Accepted);
        Assert.Equal("already in the world", again.Attempts[0].Refused);
        Assert.Equal("turned down already", again.Attempts[1].Refused);
    }

    [Theory]
    [InlineData("bondi beach")]
    [InlineData("sandy beach")]
    [InlineData("rock pool lagoon")]
    [InlineData("pine forest")]
    public void A_place_offered_as_a_thing_is_refused(string name) =>
        Assert.Equal("the place, not a thing in it", RealThingRules.NameProblem(name));

    [Theory]
    [InlineData("beach ball")]
    [InlineData("sea lion")]
    [InlineData("forest elephant")]
    public void A_thing_named_after_a_place_passes(string name) => Assert.Null(RealThingRules.NameProblem(name));

    [Fact]
    public void A_life_stage_that_goes_back_or_copies_something_in_the_world_is_dropped()
    {
        // Run of 9 Nov: coconut grew into coconut palm and back into coconut, which was on another key already.
        var growing = new DirectorRequest(6, RecipeKind.Plant, "coconut", ["coconut", "coconut palm"], GrowInto: "coconut palm", Category: RealCategory.Plant);
        Assert.True(RealThingRules.TryBuild(JsonDocument.Parse(Pick("coconut palm", "Plant", "Cloud", "coconut")), growing, out _, out var palm, out var error), error);
        Assert.Null(palm!.GrowsInto);

        var acorn = new DirectorRequest(6, RecipeKind.Plant, null, [], Category: RealCategory.Plant, Been: ["oak tree"]);
        Assert.True(RealThingRules.TryBuild(JsonDocument.Parse(Pick("acorn", "Plant", "Egg", "oak tree")), acorn, out _, out var seed, out _));
        Assert.Null(seed!.GrowsInto);
        Assert.True(RealThingRules.TryBuild(JsonDocument.Parse(Pick("acorn", "Plant", "Egg", "oak tree")), acorn with { Been = [] }, out _, out seed, out _));
        Assert.Equal("oak tree", seed!.GrowsInto);
    }

    [Fact]
    public void The_loop_does_not_grow_a_thing_into_one_already_in_the_world()
    {
        var world = new World();
        using var loop = new DirectorLoop(new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel())), new Random(2), [RealCategory.Plant]);
        var slots = Enumerable.Range(0, BuiltInRecipes.InventedFamilies).ToList();
        foreach (var i in slots) world.Families.Restyle(i, world.Families[i] with { Name = $"thing{i}" }, 0);
        var (a, b) = (slots[0], slots[1]);
        world.Families.Restyle(a, world.Families[a] with { Name = "coconut palm", GrowsInto = "coconut" }, 1);
        Assert.Equal("coconut", loop.NextRequest(world.Families)!.GrowInto);

        world.Families.Restyle(b, world.Families[b] with { Name = "coconut" }, 2);
        var next = loop.NextRequest(world.Families)!;
        Assert.Null(next.GrowInto);
        Assert.NotEqual(a, next.Slot);   // asked like any other, oldest first
    }

    [Fact]
    public async Task The_loop_remembers_lasting_refusals_and_announces_a_pick_by_the_kind_it_is_filed_as()
    {
        var world = new World();
        var author = new ScriptedModel(
            Pick("fluffaroo", "Object", "Blob"), Pick("fluffaroo", "Object", "Blob"), Pick("fluffaroo", "Object", "Blob"),
            Pick("sand dollar", "Object", "Blob"));
        var checker = new ScriptedModel(Check("unknown", "fictional", "None"), Check("a sea urchin", "real", "Animal"));
        using var loop = new DirectorLoop(new RealThingDirector(author, new FactChecker(checker)), creatureCategories: [RealCategory.Object]);
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            if (world.Families[i].Kind == RecipeKind.Plant) world.Families.Restyle(i, world.Families[i] with { Name = $"plant{i}" }, 0);
        var picked = new List<RealCategory>();
        loop.Picked += (_, category) => picked.Add(category);

        loop.Update(world, Every(10), enabled: true);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, Every(10), enabled: true);
        await Settle(loop, world);
        var remembered = Assert.Single(loop.Refused);
        Assert.Equal(("fluffaroo", "not real"), (remembered.Name, remembered.Why));
        Assert.Single(checker.Prompts);   // the second and third fluffaroo were not checked again
        Assert.Same(loop.Refused[0], loop.NextRequest(world.Families)!.TurnedDownBefore!.Single());

        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, Every(10), enabled: true);
        await Settle(loop, world);
        Assert.Equal([RealCategory.Animal], picked);
        Assert.Contains("fluffaroo (not real)", author.Prompts[3]);
    }

    private static async Task Settle(DirectorLoop loop, World world)
    {
        for (var i = 0; i < 200 && loop.Busy; i++)
        {
            await Task.Delay(10);
            loop.Update(world, Every(1), enabled: false);
        }
        Assert.False(loop.Busy);
    }
}
