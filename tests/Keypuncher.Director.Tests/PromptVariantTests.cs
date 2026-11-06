using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Director.Tests;

public class PromptVariantTests
{
    private static readonly DirectorRequest Animals = new(0, RecipeKind.Creature, null, ["cat", "dog"], Place: "that really lives at Lake Bled, Slovenia");

    [Fact]
    public void Baseline_is_exactly_what_the_toy_sent_before()
    {
        var (system, user, schema) = DirectorPrompts.Author(Animals, []);
        Assert.Equal(DirectorPrompts.RealSystem, system);
        Assert.Equal(DirectorPrompts.RealThing(Animals, []), user);
        Assert.Equal(RealThingSchema.For(Animals).GetRawText(), schema.GetRawText());
        Assert.DoesNotContain("description", schema.GetRawText());
    }

    [Fact]
    public void A1_puts_the_schema_with_descriptions_in_the_user_prompt()
    {
        var (system, user, schema) = DirectorPrompts.Author(Animals, [], variant: PromptVariant.Parse("A1"));
        Assert.Equal(DirectorPrompts.RealSystem, system);
        var subject = schema.GetProperty("properties").GetProperty("subject");
        Assert.Contains("brown trout", subject.GetProperty("description").GetString());
        Assert.EndsWith(schema.GetRawText(), user);
        Assert.StartsWith(DirectorPrompts.RealThing(Animals, []), user);
        // The narrowing per request still applies.
        Assert.Equal("Animal", schema.GetProperty("properties").GetProperty("category").GetProperty("enum")[0].GetString());
    }

    [Fact]
    public void A2_appends_three_examples_in_the_schema_shape()
    {
        var (system, user, schema) = DirectorPrompts.Author(Animals, [], variant: PromptVariant.Parse("A2"));
        Assert.StartsWith(DirectorPrompts.RealSystem, system);
        Assert.EndsWith(DirectorPrompts.FewShotExamples, system);
        Assert.Equal(DirectorPrompts.RealThing(Animals, []), user);
        var examples = DirectorPrompts.FewShotExamples.Split('\n').Where(l => l.TrimStart().StartsWith('{')).ToList();
        Assert.Equal(3, examples.Count);
        foreach (var line in examples)
        {
            var json = JsonDocument.Parse(line);
            var category = Enum.Parse<RealCategory>(json.RootElement.GetProperty("category").GetString()!);
            var request = new DirectorRequest(0, RealThingPick.KindOf(category), null, [], Category: category);
            Assert.True(RealThingRules.TryBuild(json, request, out _, out _, out var error), error);
        }
    }

    [Fact]
    public void The_toy_runs_the_examples_and_the_baseline_checker()
    {
        Assert.Same(PromptVariant.Shipped, PromptVariant.Parse("A2"));
        Assert.Equal(PromptVariant.Shipped, new PromptVariant("A2", FewShot: true));
    }

    [Fact]
    public async Task The_director_sends_the_examples_unless_told_otherwise()
    {
        var model = new RealThingTests.ScriptedModel(JsonSerializer.Serialize(new { subject = "x" }));
        await new RealThingDirector(model, new FactChecker(model)).ProposeAsync(Animals);
        Assert.EndsWith(DirectorPrompts.FewShotExamples, model.Systems[0]);
    }

    [Fact]
    public void A8_moves_the_standing_orders_into_the_user_prompt()
    {
        var (system, user, _) = DirectorPrompts.Author(Animals, [], variant: PromptVariant.Parse("A8"));
        Assert.DoesNotContain("real things", system);
        Assert.StartsWith(DirectorPrompts.RealSystem, user);
        Assert.EndsWith(DirectorPrompts.RealThing(Animals, []), user);
    }

    [Fact]
    public void Variants_combine_with_plus_and_unknown_names_are_refused()
    {
        var both = PromptVariant.Parse("a1+A4");
        Assert.Equal("A1+A4", both.Name);
        Assert.True(both.SchemaInPrompt);
        Assert.True(both.GentleChecker);
        Assert.False(both.FewShot);
        Assert.Throws<ArgumentException>(() => PromptVariant.Parse("A99"));
        Assert.Same(PromptVariant.Baseline, PromptVariant.Parse("baseline"));
        Assert.True(PromptVariant.Parse("best").FewShot);
    }

    [Fact]
    public async Task Gentle_checker_asks_once_where_the_baseline_takes_a_second_look()
    {
        var unsureThenReal = new[]
        {
            JsonSerializer.Serialize(new { whatItIs = "unknown", verdict = "unsure", kind = "None", growsInto = "none" }),
            JsonSerializer.Serialize(new { whatItIs = "a fish", verdict = "real", kind = "Animal", growsInto = "none" }),
        };
        var baseline = new RealThingTests.ScriptedModel(unsureThenReal);
        var result = await new FactChecker(baseline).CheckAsync("mudfish", null, RealCategory.Animal);
        Assert.Equal(2, baseline.Prompts.Count);
        Assert.True(result.Passes(RealCategory.Animal));

        var gentle = new RealThingTests.ScriptedModel(unsureThenReal);
        result = await new FactChecker(gentle, PromptVariant.Parse("A4")).CheckAsync("mudfish", null, RealCategory.Animal);
        Assert.Single(gentle.Prompts);
        Assert.Contains("even a", gentle.Prompts[0]);
        Assert.Contains("grow or turn into", gentle.Prompts[0]);
        Assert.False(result.Passes(RealCategory.Animal));
    }

    [Fact]
    public async Task The_director_sends_the_variant_it_was_given()
    {
        var model = new RealThingTests.ScriptedModel(JsonSerializer.Serialize(new
        {
            subject = "puffin", category = "Animal", fact = "Lives on sea cliffs.", shape = "Bird", lobes = 3, hue = 30, size = 0.3,
            motion = new { type = "Hop", speed = 0.4 }, voice = new { wave = "Sine", attack = 0.05, decay = 0.6, scale = "pentatonic", root = "E5" }, growsInto = (string?)null,
        }));
        var checker = new FactChecker(new RealThingTests.ScriptedModel(JsonSerializer.Serialize(new { whatItIs = "a seabird", verdict = "real", kind = "Animal", growsInto = "none" })));
        var director = new RealThingDirector(model, checker, variant: PromptVariant.Parse("A1"));
        var outcome = await director.ProposeAsync(new DirectorRequest(0, RecipeKind.Creature, null, []));
        Assert.True(outcome.Accepted);
        Assert.Contains("\"description\"", model.Prompts[0]);
    }
}
