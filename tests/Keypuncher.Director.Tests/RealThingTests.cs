using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference;

namespace Keypuncher.Director.Tests;

public class RealThingTests
{
    private static readonly DirectorRequest CreatureFamily = new(0, RecipeKind.Creature, null, ["cat", "dog"]);
    private static readonly DirectorRequest PlantFamily = new(1, RecipeKind.Plant, null, [], Category: RealCategory.Plant);
    private static readonly DirectorRequest Vehicles = CreatureFamily with { Category = RealCategory.Vehicle };
    private static readonly DirectorRequest Extinct = CreatureFamily with { Category = RealCategory.ExtinctAnimal };

    [Fact]
    public void Schema_asks_what_the_thing_is_before_how_it_looks()
    {
        var order = RealThingSchema.Element.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(["subject", "category", "fact"], order.Take(3));
        Assert.DoesNotContain("Burst", RealThingSchema.Element.GetProperty("properties").GetProperty("motion").ToString());
    }

    [Fact]
    public void A_real_animal_becomes_a_creature_with_its_name_and_no_child_facing_text()
    {
        Assert.True(Build(Pick("Puffin", "Animal", "Bird"), CreatureFamily, out var pick, out var recipe, out var error), error);
        Assert.Equal("puffin", recipe!.Name);
        Assert.Equal(RecipeKind.Creature, recipe.Kind);
        Assert.Equal(Shape.Bird, recipe.Body.Shape);
        Assert.Equal("Lives on sea cliffs in Iceland.", pick!.Fact);
    }

    [Theory]
    [InlineData("unicorn")]
    [InlineData("baby dragon")]
    [InlineData("rainbow fairy")]
    [InlineData("sea-monster")]
    [InlineData("glitter123")]
    [InlineData("Ñandú")]
    public void Story_creatures_and_odd_names_are_refused(string name)
    {
        Assert.False(Build(Pick(name, "Animal", "Blob"), CreatureFamily, out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("komodo dragon")]
    [InlineData("dragonfly")]
    [InlineData("gila monster")]
    [InlineData("ghost crab")]
    public void Real_things_with_a_story_word_in_their_name_pass(string name) =>
        Assert.True(Build(Pick(name, "Animal", "Blob"), CreatureFamily, out _, out _, out var error), error);

    [Fact]
    public void A_pick_must_fit_its_family_and_its_shape()
    {
        Assert.False(Build(Pick("oak tree", "Plant", "Cloud"), CreatureFamily, out _, out _, out _));
        Assert.False(Build(Pick("hedgehog", "Animal", "Blob"), PlantFamily, out _, out _, out _));
        Assert.False(Build(Pick("bus", "Vehicle", "Stripe"), CreatureFamily, out _, out _, out var e0));
        Assert.Equal("not a kind of animal alive today", e0);
        Assert.False(Build(Pick("bus", "Vehicle", "Elephant"), Vehicles, out _, out _, out var e1));
        Assert.Contains("animal shape", e1);
        Assert.False(Build(Pick("crocodile", "Animal", "Rex"), CreatureFamily, out _, out _, out var e2));
        Assert.Contains("dinosaur", e2);
        Assert.True(Build(Pick("allosaurus", "ExtinctAnimal", "Rex"), Extinct, out _, out _, out var e3), e3);
    }

    [Fact]
    public void A_name_with_its_own_shape_is_drawn_with_it()
    {
        Assert.True(Build(Pick("cow", "Animal", "Horse"), CreatureFamily with { Existing = [] }, out _, out var cow, out _));
        Assert.Equal(Shape.Cow, cow!.Body.Shape);
        Assert.True(Build(Pick("woolly mammoth", "ExtinctAnimal", "Blob"), Extinct, out _, out var mammoth, out _));
        Assert.Equal(Shape.Elephant, mammoth!.Body.Shape);
    }

    [Fact]
    public void Faces_only_on_animals_and_plants_stay_rooted()
    {
        Assert.True(Build(Pick("ladybird", "Animal", "DotField"), CreatureFamily, out _, out var bug, out _));
        Assert.Equal(2, bug!.Eyes);
        Assert.True(Build(Pick("fire engine", "Vehicle", "Stripe"), Vehicles, out _, out var engine, out _));
        Assert.Equal(0, engine!.Eyes);
        Assert.True(Build(Pick("tulip", "Plant", "Petal", motion: "Hop"), PlantFamily, out _, out var tulip, out _));
        Assert.Equal(RecipeKind.Plant, tulip!.Kind);
        Assert.Equal(MotionType.Sway, tulip.Motion.Type);
        Assert.Equal(0, tulip.Eyes);
    }

    [Fact]
    public void Repeats_are_refused_unless_the_family_is_growing_into_it()
    {
        Assert.False(Build(Pick("Cat", "Animal", "Cat"), CreatureFamily, out _, out _, out var error));
        Assert.Equal("already in the world", error);

        var growing = new DirectorRequest(0, RecipeKind.Creature, "tadpole", ["frog"], GrowInto: "frog");
        Assert.True(Build(Pick("frog", "Animal", "Blob"), growing, out _, out _, out _));
        Assert.False(Build(Pick("newt", "Animal", "Blob"), growing, out _, out _, out var wrong));
        Assert.Contains("frog", wrong);
    }

    [Fact]
    public void A_plural_of_something_already_there_and_the_place_itself_are_refused()
    {
        Assert.False(Build(Pick("apples", "Plant", "Blob"), PlantFamily with { Existing = ["apple"] }, out _, out _, out var plural));
        Assert.Equal("already in the world", plural);
        var kitchen = CreatureFamily with { Category = RealCategory.Object, Place = "in a kitchen" };
        Assert.False(Build(Pick("kitchen", "Object", "Blob"), kitchen, out _, out _, out var place));
        Assert.Equal("the place, not a thing in it", place);
        Assert.True(Build(Pick("teapot", "Object", "Blob"), kitchen, out _, out _, out _));
    }

    [Fact]
    public void A_story_life_stage_is_dropped_but_the_thing_kept()
    {
        Assert.True(Build(Pick("caterpillar", "Animal", "Stripe", growsInto: "Butterfly"), CreatureFamily, out _, out var real, out _));
        Assert.Equal("butterfly", real!.GrowsInto);
        Assert.True(Build(Pick("caterpillar", "Animal", "Stripe", growsInto: "fairy"), CreatureFamily, out _, out var story, out _));
        Assert.Null(story!.GrowsInto);
        Assert.True(Build(Pick("strawberry", "Plant", "DotField", growsInto: "null"), PlantFamily, out _, out var none, out _));
        Assert.Null(none!.GrowsInto);
    }

    [Theory]
    [InlineData("dinosaur")]
    [InlineData("flower")]
    [InlineData("bird")]
    public void Group_words_are_refused(string name) =>
        Assert.Equal("too general, not one kind", Build(Pick(name, "Animal", "Blob"), CreatureFamily, out _, out _, out var e) ? null : e);

    [Fact]
    public void Each_request_gets_a_schema_that_allows_only_its_category_and_shapes()
    {
        static List<string> Enum(JsonElement schema, string prop) =>
            schema.GetProperty("properties").GetProperty(prop).GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).ToList();

        var plant = RealThingSchema.For(PlantFamily);
        Assert.Equal(["Plant"], Enum(plant, "category"));
        Assert.DoesNotContain("Cat", Enum(plant, "shape"));
        Assert.Contains("Petal", Enum(plant, "shape"));

        var alive = RealThingSchema.For(CreatureFamily);
        Assert.Contains("Cat", Enum(alive, "shape"));
        Assert.DoesNotContain("Rex", Enum(alive, "shape"));
        Assert.Contains("Rex", Enum(RealThingSchema.For(Extinct), "shape"));
        Assert.DoesNotContain("Whale", Enum(RealThingSchema.For(Vehicles), "shape"));
    }

    [Fact]
    public void Numbers_are_clamped_as_for_any_recipe() =>
        Assert.True(Build(Pick("hedgehog", "Animal", "Blob").Replace("\"size\":0.5", "\"size\":7"), CreatureFamily, out _, out var r, out _)
                    && r!.Body.Size == 1.0);

    [Fact]
    public void Only_a_sure_real_in_the_right_category_passes_the_check()
    {
        Assert.True(new FactCheck("a seabird", "real", "Animal", "none").Passes(RealCategory.Animal));
        Assert.False(new FactCheck("unknown", "unsure", "Animal", "none").Passes(RealCategory.Animal));
        Assert.False(new FactCheck("a myth", "fictional", "Animal", "none").Passes(RealCategory.Animal));
        Assert.False(new FactCheck("a seabird", "real", "Animal", "none").Passes(RealCategory.ExtinctAnimal));
        Assert.False(new FactCheck("an extinct bird", "real", "ExtinctAnimal", "none").Passes(RealCategory.Animal));
    }

    [Fact]
    public async Task The_director_names_refused_picks_in_its_next_try_and_uses_the_first_real_one()
    {
        var author = new ScriptedModel(Pick("unicorn", "Animal", "Horse"), Pick("fluffaroo", "Animal", "Blob"), Pick("puffin", "Animal", "Bird"));
        var checker = new ScriptedModel(Check("unknown", "fictional"), Check("a seabird", "real"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(CreatureFamily);

        Assert.True(outcome.Accepted);
        Assert.Equal("puffin", outcome.Recipe!.Name);
        Assert.Equal(["unicorn", "fluffaroo", "puffin"], outcome.Attempts.Select(a => a.Subject));
        Assert.Equal([false, false, true], outcome.Attempts.Select(a => a.Refused is null));
        Assert.Contains("unicorn", author.Prompts[1]);
        Assert.Contains("fluffaroo (not real)", author.Prompts[2]);
        Assert.Contains("\"fluffaroo\"", checker.Prompts[0]);
        Assert.DoesNotContain("Lives on sea cliffs", checker.Prompts[0]);   // the checker never sees the author's fact
    }

    [Fact]
    public async Task A_living_animal_offered_as_extinct_is_refused_by_the_checkers_own_call()
    {
        var author = new ScriptedModel(Pick("puffin", "ExtinctAnimal", "Bird"), Pick("dodo", "ExtinctAnimal", "Bird"));
        var checker = new ScriptedModel(Check("a seabird", "real", kind: "Animal"), Check("an extinct bird", "real", kind: "ExtinctAnimal"));
        var outcome = await new RealThingDirector(author, new FactChecker(checker)).ProposeAsync(Extinct);

        Assert.Equal("dodo", outcome.Recipe!.Name);
        Assert.Equal("checker: Animal, not ExtinctAnimal", outcome.Attempts[0].Refused);
        Assert.DoesNotContain("ExtinctAnimal", checker.Prompts[0].Split("kind:")[0]);   // the claim is not shown to the checker
        Assert.Contains("puffin (still alive today, so not extinct)", author.Prompts[1]);
    }

    [Fact]
    public async Task A_last_try_at_an_extinct_animal_gets_well_known_ones_not_yet_in_the_world()
    {
        var author = new ScriptedModel(Pick("puffin", "ExtinctAnimal", "Bird"), Pick("puffin", "ExtinctAnimal", "Bird"), Pick("moa", "ExtinctAnimal", "Bird"));
        var checker = new ScriptedModel(Check("a seabird", "real"), Check("a seabird", "real"), Check("an extinct bird", "real", kind: "ExtinctAnimal"));
        var request = Extinct with { Existing = [.. RealThingPick.WellKnownExtinct.Where(n => n != "moa" && n != "dodo")] };
        var outcome = await new RealThingDirector(author, new FactChecker(checker), new Random(1)).ProposeAsync(request);

        Assert.Equal("moa", outcome.Recipe!.Name);
        Assert.DoesNotContain("choose one of these", author.Prompts[1]);
        var last = author.Prompts[2];
        Assert.Contains("choose one of these: ", last);
        var offered = last[(last.IndexOf("choose one of these: ") + 21)..].TrimEnd('.', '\n', ' ').Split(", ");
        Assert.Equal(["dodo", "moa"], offered.Order());
    }

    [Fact]
    public void No_example_in_the_standing_orders_can_pass_for_an_extinct_animal() =>
        Assert.DoesNotContain("puffin", DirectorPrompts.RealSystem);

    [Fact]
    public async Task The_director_gives_up_after_three_tries_and_drops_an_unconfirmed_life_stage()
    {
        var none = await new RealThingDirector(
            new ScriptedModel(Pick("dragon", "Animal", "Blob"), Pick("dragon", "Animal", "Blob"), Pick("dragon", "Animal", "Blob")),
            new FactChecker(new ScriptedModel())).ProposeAsync(CreatureFamily);
        Assert.False(none.Accepted);
        Assert.Equal(RealThingDirector.MaxAttempts, none.Attempts.Count);

        var unconfirmed = await new RealThingDirector(
            new ScriptedModel(Pick("caterpillar", "Animal", "Stripe", growsInto: "moth")),
            new FactChecker(new ScriptedModel(Check("a larva", "real", growsInto: "false")))).ProposeAsync(CreatureFamily);
        Assert.Null(unconfirmed.Recipe!.GrowsInto);
    }

    [Fact]
    public async Task The_loop_waits_restyles_invented_families_first_then_grows_things_into_what_they_become()
    {
        var world = new World();
        var author = new ScriptedModel(Pick("tadpole", "Animal", "Blob", growsInto: "frog"), Pick("frog", "Animal", "Blob"));
        var checker = new ScriptedModel(Check("a young frog", "real", growsInto: "true"), Check("an amphibian", "real"));
        using var loop = new DirectorLoop(new RealThingDirector(author, new FactChecker(checker)), new Random(3), [RealCategory.Animal]);
        // Plant families are real already, so the one invented family left to ask about is a creature one.
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            if (world.Families[i].Kind == RecipeKind.Plant) world.Families.Restyle(i, world.Families[i] with { Name = $"plant{i}" }, 0);
        var plants = world.Families.Restyled().Count();
        loop.Update(world, restyleMinutes: 1, enabled: true);
        Assert.False(loop.Busy);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, 1, enabled: true);
        Assert.True(loop.Busy);
        await Settle(loop, world);

        Assert.Equal(plants + 1, world.Families.Restyled().Count());
        var slot = Enumerable.Range(0, BuiltInRecipes.InventedFamilies).Single(i => world.Families[i].Name == "tadpole");
        Assert.Equal("tadpole", world.Families[slot].Name);
        Assert.Equal(RecipeKind.Creature, world.Families[slot].Kind);
        Assert.True(world.Dirty);

        // Every invented family is real now; the tadpole is the next to change, and becomes a frog.
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            if (i != slot && world.Families.ChangedAt(i) is null) world.Families.Restyle(i, world.Families[i] with { Name = $"thing{i}" }, world.Time);
        var next = loop.NextRequest(world.Families)!;
        Assert.Equal((slot, "tadpole", "frog"), (next.Slot, next.Current, next.GrowInto));
        Assert.Contains("tadpole", next.Existing);

        Tick(world, 59);
        loop.Update(world, 1, enabled: true);
        Assert.False(loop.Busy);
        Tick(world, 1);
        loop.Update(world, 1, enabled: true);
        await Settle(loop, world);
        Assert.Equal("frog", world.Families[slot].Name);
    }

    [Fact]
    public async Task The_loop_asks_nothing_while_off_and_logs_an_unreachable_model()
    {
        var world = new World();
        using var loop = new DirectorLoop(new RealThingDirector(new BrokenModel(), new FactChecker(new BrokenModel())));
        loop.Update(world, 1, enabled: false);
        Tick(world, 600);
        loop.Update(world, 1, enabled: false);
        Assert.False(loop.Busy);

        loop.Update(world, 1, enabled: true);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, 1, enabled: true);
        await Settle(loop, world);
        Assert.Contains("refused", Assert.Single(loop.Log).Error);
        Assert.Empty(world.Families.Restyled());
    }

    [Fact]
    public async Task An_empty_answer_is_tried_again_soon_not_a_whole_interval_later()
    {
        var world = new World();
        var author = new ScriptedModel(Pick("dragon", "Animal", "Blob"), Pick("dragon", "Animal", "Blob"), Pick("dragon", "Animal", "Blob"), Pick("hedgehog", "Animal", "Blob"));
        using var loop = new DirectorLoop(new RealThingDirector(author, new FactChecker(new ScriptedModel(Check("a small mammal", "real")))), creatureCategories: [RealCategory.Animal]);
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            if (world.Families[i].Kind == RecipeKind.Plant) world.Families.Restyle(i, world.Families[i] with { Name = $"plant{i}" }, 0);
        var before = world.Families.Restyled().Count();

        loop.Update(world, restyleMinutes: 10, enabled: true);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, 10, enabled: true);
        await Settle(loop, world);
        Assert.False(Assert.Single(loop.Log).Outcome!.Accepted);

        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, 10, enabled: true);
        Assert.True(loop.Busy);
        await Settle(loop, world);
        Assert.Equal(before + 1, world.Families.Restyled().Count());
    }

    [Fact]
    public async Task An_answer_for_a_world_that_was_cleared_meanwhile_is_not_used()
    {
        var world = new World();
        var gate = new TaskCompletionSource();
        var author = new ScriptedModel(Pick("puffin", "Animal", "Bird")) { Gate = gate.Task };
        using var loop = new DirectorLoop(new RealThingDirector(author, new FactChecker(new ScriptedModel(Check("a seabird", "real")))), creatureCategories: [RealCategory.Animal]);
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            if (world.Families[i].Kind == RecipeKind.Plant) world.Families.Restyle(i, world.Families[i] with { Name = $"plant{i}" }, 0);
        loop.Update(world, 1, true);
        Tick(world, DirectorLoop.FirstAfterSeconds);
        loop.Update(world, 1, true);
        for (var i = 0; i < BuiltInRecipes.InventedFamilies; i++)
            world.Families.Restyle(i, world.Families[i] with { Name = "changed" }, world.Time);
        gate.SetResult();
        await Settle(loop, world);
        Assert.All(world.Families.All.Take(BuiltInRecipes.InventedFamilies), f => Assert.Equal("changed", f.Name));
        Assert.True(Assert.Single(loop.Log).Outcome!.Accepted);
    }

    private static void Tick(World world, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += 0.5) world.Tick(Math.Min(0.5, seconds - t));
    }

    private static async Task Settle(DirectorLoop loop, World world)
    {
        for (var i = 0; i < 200 && loop.Busy; i++)
        {
            await Task.Delay(10);
            loop.Update(world, 1, enabled: false);
        }
        Assert.False(loop.Busy);
    }

    private static bool Build(string json, DirectorRequest request, out RealThingPick? pick, out Recipe? recipe, out string? error) =>
        RealThingRules.TryBuild(JsonDocument.Parse(json), request, out pick, out recipe, out error);

    private static string Pick(string subject, string category, string shape, string motion = "Drift", string? growsInto = null) =>
        $$"""
        {"subject":"{{subject}}","category":"{{category}}","fact":"Lives on sea cliffs in Iceland.",
         "shape":"{{shape}}","lobes":3,"hue":20,"size":0.5,"motion":{"type":"{{motion}}","speed":0.4},
         "voice":{"wave":"Sine","attack":0.01,"decay":0.4,"scale":"pentatonic","root":"E5"},
         "growsInto":{{(growsInto is null ? "null" : $"\"{growsInto}\"")}}}
        """;

    private static string Check(string what, string verdict, string kind = "Animal", string growsInto = "none") =>
        JsonSerializer.Serialize(new { whatItIs = what, verdict, kind, growsInto });

    /// <summary>Answers each prompt with the next scripted JSON and remembers the prompts.</summary>
    private sealed class ScriptedModel(params string[] answers) : ILanguageModel
    {
        private int _next;
        public List<string> Prompts { get; } = [];
        public Task? Gate { get; init; }
        public string ModelName => "scripted";

        public async Task<GenerationResult> GenerateJsonAsync(string systemPrompt, string userPrompt, JsonElement jsonSchema, CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate;
            Prompts.Add(userPrompt);
            var raw = answers[Math.Min(_next++, answers.Length - 1)];
            return new GenerationResult(raw, JsonDocument.Parse(raw), new GenerationTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0));
        }
    }

    private sealed class BrokenModel : ILanguageModel
    {
        public string ModelName => "broken";
        public Task<GenerationResult> GenerateJsonAsync(string systemPrompt, string userPrompt, JsonElement jsonSchema, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection refused");
    }
}
