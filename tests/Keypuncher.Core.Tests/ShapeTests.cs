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
        // Added after the recognition review: four more pictures and one more animal, after the sea animals.
        Assert.True(Shape.Sun.IsPicture() && Shape.Ball.IsPicture() && Shape.Egg.IsPicture() && Shape.Car.IsPicture());
        Assert.True(Shape.Butterfly.IsAnimal());
    }

    [Fact]
    public void The_sun_ball_egg_and_butterfly_words_have_drawings_of_their_own()
    {
        // The sun was a star (the same five points as the star at a baby's level), the ball and egg plain ovals,
        // the butterfly four petals with a face, which read as a flower.
        Assert.Equal(Shape.Sun, BuiltInRecipes.Concepts["sun"].Body.Shape);
        Assert.Equal(Shape.Ball, BuiltInRecipes.Concepts["ball"].Body.Shape);
        Assert.Equal(Shape.Egg, BuiltInRecipes.Concepts["egg"].Body.Shape);
        Assert.Equal(Shape.Butterfly, BuiltInRecipes.Concepts["butterfly"].Body.Shape);
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
