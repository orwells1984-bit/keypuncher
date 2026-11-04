using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class ShapeTests
{
    [Fact]
    public void Every_shape_is_abstract_a_picture_or_an_animal_and_only_one_of_them()
    {
        foreach (var shape in Enum.GetValues<Shape>())
            Assert.Equal(1, new[] { shape.IsAbstract(), shape.IsPicture(), shape.IsAnimal() }.Count(x => x));
        Assert.True(Shape.Blob.IsAbstract());
        Assert.True(Shape.Apple.IsPicture());
        Assert.True(Shape.Boat.IsPicture());
        Assert.True(Shape.Cat.IsAnimal());
        Assert.False(Shape.Moon.IsAnimal());
    }

    [Fact]
    public void The_apple_moon_house_and_boat_words_have_drawings_of_their_own()
    {
        Assert.Equal(Shape.Apple, BuiltInRecipes.Concepts["apple"].Body.Shape);
        Assert.Equal(Shape.Moon, BuiltInRecipes.Concepts["moon"].Body.Shape);
        Assert.Equal(Shape.House, BuiltInRecipes.Concepts["house"].Body.Shape);
        Assert.Equal(Shape.Boat, BuiltInRecipes.Concepts["boat"].Body.Shape);
        // Nothing drawn as a picture has eyes: none of them is alive.
        foreach (var recipe in BuiltInRecipes.Concepts.Values.Where(r => r.Body.Shape.IsPicture()))
            Assert.Equal(0, recipe.Eyes);
        // The invented key families keep to the abstract shapes the director restyles.
        foreach (var recipe in BuiltInRecipes.Palette.Take(BuiltInRecipes.InventedFamilies))
            Assert.True(recipe.Body.Shape.IsAbstract(), recipe.Name);
    }
}
