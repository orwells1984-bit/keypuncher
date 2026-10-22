using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Director.Tests;

public class RecipeTests
{
    private const string Sample = """
        {"kind":"Creature","name":"puffbird","body":{"shape":"Blob","lobes":5,"hue":32,"size":0.6},
         "eyes":2,"motion":{"type":"Hop","speed":0.4},
         "voice":{"wave":"Triangle","attack":0.01,"decay":0.3,"scale":"pentatonic","root":"C5"},
         "growsInto":"puffbird-adult"}
        """;

    [Fact]
    public void Schema_is_valid_json_with_required_keys()
    {
        var required = RecipeSchema.Element.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("voice", required);
        Assert.Contains("body", required);
    }

    [Fact]
    public void Sample_recipe_parses()
    {
        Assert.True(RecipeValidator.TryParse(JsonDocument.Parse(Sample), out var recipe, out var error), error);
        Assert.Equal(RecipeKind.Creature, recipe!.Kind);
        Assert.Equal(Shape.Blob, recipe.Body.Shape);
        Assert.Equal(Waveform.Triangle, recipe.Voice.Wave);
        Assert.Equal("puffbird-adult", recipe.GrowsInto);
    }

    [Theory]
    [InlineData("\"hue\":32", "\"hue\":400", "hue")]
    [InlineData("\"root\":\"C5\"", "\"root\":\"H9\"", "root")]
    [InlineData("\"size\":0.6", "\"size\":3", "size")]
    public void Out_of_range_values_are_rejected(string from, string to, string expectedError)
    {
        var bad = Sample.Replace(from, to);
        Assert.False(RecipeValidator.TryParse(JsonDocument.Parse(bad), out _, out var error));
        Assert.Equal(expectedError, error);
    }

    [Fact]
    public void Null_document_is_rejected()
    {
        Assert.False(RecipeValidator.TryParse(null, out _, out var error));
        Assert.Equal("not JSON", error);
    }

    [Fact]
    public void Prompt_mentions_the_concept_and_existing_names()
    {
        var world = new WorldSummary("meadow", "dusk", "calm", 3, 0.5, ["puffbird"]);
        var prompt = DirectorPrompts.NewRecipe(world, "a smiling sun");
        Assert.Contains("a smiling sun", prompt);
        Assert.Contains("puffbird", prompt);
    }
}
