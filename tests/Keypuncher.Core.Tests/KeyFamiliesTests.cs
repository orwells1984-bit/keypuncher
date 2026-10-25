using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class KeyFamiliesTests
{
    [Fact]
    public void Only_invented_families_change_and_each_keeps_its_kind()
    {
        var families = new KeyFamilies();
        var daisy = BuiltInRecipes.Palette[1] with { Name = "daisy" };   // family 1 is a plant
        Assert.True(families.Restyle(1, daisy, 5));
        Assert.Same(daisy, families.For(1));
        Assert.Same(daisy, families.For((uint)(1 + families.Count)));

        Assert.False(families.Restyle(0, daisy, 5));                      // family 0 is a creature
        Assert.False(families.Restyle(BuiltInRecipes.InventedFamilies, BuiltInRecipes.Animals.Cat with { Name = "lion" }, 5));
        Assert.Same(BuiltInRecipes.Palette[0], families[0]);
        Assert.Equal([1], families.Restyled().Select(f => f.Slot));
    }

    [Fact]
    public void Loading_skips_families_that_cannot_hold_the_saved_thing()
    {
        var families = new KeyFamilies();
        families.Load([(0, BuiltInRecipes.Palette[1], 2), (30, BuiltInRecipes.Animals.Cat, 2), (3, BuiltInRecipes.Palette[3] with { Name = "fern" }, 4)]);
        Assert.Same(BuiltInRecipes.Palette[0], families[0]);
        Assert.Equal("fern", families[3].Name);
        Assert.Equal(4, families.ChangedAt(3));
    }
}
