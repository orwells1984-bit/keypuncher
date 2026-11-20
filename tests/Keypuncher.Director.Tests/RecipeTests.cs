using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Director.Tests;

public class RecipeTests
{
    private const string Sample = """
        {"kind":"Creature","name":"brown trout","body":{"shape":"Fish","lobes":3,"hue":30,"size":0.3},
         "eyes":2,"motion":{"type":"Drift","speed":0.4},
         "voice":{"wave":"Sine","attack":0.05,"decay":0.6,"scale":"pentatonic","root":"E5"}}
        """;

    private static Recipe Parse(string json) => JsonSerializer.Deserialize<Recipe>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void Out_of_range_numbers_are_clamped_not_rejected()
    {
        // What gemma3:4b actually returned on the desktop bench: Ollama ignores min/max.
        var loose = Sample.Replace("\"size\":0.3", "\"size\":8")
                          .Replace("\"speed\":0.4", "\"speed\":3")
                          .Replace("\"attack\":0.05", "\"attack\":2")
                          .Replace("\"hue\":30", "\"hue\":400");
        var recipe = RecipeValidator.Normalise(Parse(loose));
        Assert.Equal(1.0, recipe.Body.Size);
        Assert.Equal(1.0, recipe.Motion.Speed);
        Assert.Equal(0.5, recipe.Voice.Attack);
        Assert.Equal(40, recipe.Body.Hue);
    }

    [Fact]
    public void An_animal_is_always_a_creature()
    {
        var plantCat = Sample.Replace("\"Creature\"", "\"Plant\"").Replace("\"Fish\"", "\"Cat\"");
        var recipe = RecipeValidator.Normalise(Parse(plantCat));
        Assert.Equal(Shape.Cat, recipe.Body.Shape);
        Assert.Equal(RecipeKind.Creature, recipe.Kind);
    }

    [Theory]
    [InlineData("C5", true)]
    [InlineData("F#3", true)]
    [InlineData("H9", false)]
    [InlineData("C7", false)]
    public void Only_playable_notes_are_notes(string note, bool expected) =>
        Assert.Equal(expected, RecipeValidator.IsNote(note));
}
