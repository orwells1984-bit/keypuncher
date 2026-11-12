using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures.Tests;

public sealed class ToyNamesTests
{
    [Theory]
    [InlineData("wiggrass", true)]
    [InlineData("driftle", true)]
    [InlineData("puffbird", true)]
    [InlineData("cat", false)]
    [InlineData("sable antelope", false)]
    public void An_invented_thing_is_known_by_name(string name, bool invented) => Assert.Equal(invented, ToyNames.IsInvented(name));

    [Theory]
    [InlineData("rex", RealCategory.Animal, "Tyrannosaurus", RealCategory.ExtinctAnimal)]
    [InlineData("longneck", RealCategory.Animal, "Brachiosaurus", RealCategory.ExtinctAnimal)]
    [InlineData("stegosaurus", RealCategory.Animal, "Stegosaurus", RealCategory.ExtinctAnimal)]
    [InlineData("cat", RealCategory.Animal, "cat", RealCategory.Animal)]
    [InlineData("sable antelope", RealCategory.Animal, "sable antelope", RealCategory.Animal)]
    public void A_nursery_name_is_looked_up_under_its_real_name(string name, RealCategory category, string real, RealCategory realCategory) =>
        Assert.Equal((real, realCategory), ToyNames.LookUp(name, category));

    [Fact]
    public async Task The_finder_asks_for_the_real_name_and_nothing_for_an_invented_thing()
    {
        var inner = new RecordingFinder();
        var finder = new ToyNameFinder(inner);

        var rex = await finder.FindAsync("rex", RealCategory.Animal, []);
        var grass = await finder.FindAsync("wiggrass", RealCategory.Plant, []);

        Assert.Equal([("Tyrannosaurus", RealCategory.ExtinctAnimal)], inner.Asked);
        Assert.Equal("no photo found", rex.Reason);
        Assert.Null(grass.Photo);
        Assert.False(grass.Offline);
        Assert.Equal("an invented thing", grass.Reason);
    }

    private sealed class RecordingFinder : IPhotoFinder
    {
        public List<(string, RealCategory)> Asked { get; } = [];
        public Task<PhotoSearch> FindAsync(string name, RealCategory category, IReadOnlyCollection<string> exclude, CancellationToken ct = default)
        {
            Asked.Add((name, category));
            return Task.FromResult(PhotoSearch.Nothing("no photo found", []));
        }
    }
}
