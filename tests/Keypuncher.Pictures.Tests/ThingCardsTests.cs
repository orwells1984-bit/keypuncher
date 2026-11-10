using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

/// <summary>The grown-ups' choices for a thing: kept one file per thing beside the pictures, and absent until a
/// grown-up chooses something.</summary>
public sealed class ThingCardsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-cards-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 10, 14, 0, 0);
    private readonly PictureFolder _folder;

    public ThingCardsTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_thing_has_no_card_until_one_is_saved()
    {
        Assert.Null(_folder.Cards.Get("octopus"));
        Assert.Empty(_folder.Cards.All());
        Assert.False(_folder.Cards.ChoseByHand("octopus", 1));
        var fresh = _folder.Cards.GetOrNew("North Pacific giant octopus");
        Assert.Equal(("north-pacific-giant-octopus", "North Pacific giant octopus", true), (fresh.Slug, fresh.Name, fresh.IsEmpty));
    }

    [Fact]
    public void A_saved_card_comes_back_whole_from_another_look_at_the_folder()
    {
        var card = new ThingCard("octopus", "octopus")
        {
            Category = RealCategory.Animal,
            Climb = ClimbChoice.Ladder,
            Rungs = new RungChoices().With(PictureStage.Silhouette, RungChoice.Skip).With(PictureStage.Photo, RungChoice.OfPicture(3)),
            Words = new Dictionary<string, string> { ["is"] = "kolkrabbi", ["bg"] = "октопод" },
        };
        var changed = new List<string>();
        _folder.Cards.Changed += changed.Add;

        _folder.Cards.Save(card, _now);

        var read = new PictureFolder(_folder.Root).Cards.Get("octopus")!;
        Assert.Equal(Path.Combine(_folder.Root, "things", "octopus.json"), _folder.Cards.PathFor("octopus"));
        Assert.Equal((RealCategory.Animal, ClimbChoice.Ladder, _now), (read.Category, read.Climb, read.UpdatedAt));
        Assert.Equal(card.Rungs, read.Rungs);
        Assert.Equal("kolkrabbi", read.WordIn("is"));
        Assert.Equal("октопод", read.WordIn("bg"));
        Assert.Null(read.WordIn("en"));
        Assert.True(_folder.Cards.ChoseByHand("octopus", 3));
        Assert.False(_folder.Cards.ChoseByHand("octopus", 1));
        Assert.Equal(["octopus"], changed);
        Assert.Equal("octopus", Assert.Single(_folder.Cards.All()).Slug);
        Assert.Empty(Directory.GetFiles(_folder.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_card_set_back_to_what_the_toy_would_do_is_removed()
    {
        var card = new ThingCard("octopus", "octopus") { Rungs = new RungChoices().With(PictureStage.Flat, RungChoice.Shape) };
        _folder.Cards.Save(card, _now);

        _folder.Cards.Save(card with { Rungs = card.Rungs.With(PictureStage.Flat, RungChoice.Auto) }, _now);

        Assert.Null(_folder.Cards.Get("octopus"));
        Assert.False(File.Exists(_folder.Cards.PathFor("octopus")));
    }

    [Fact]
    public void A_damaged_card_reads_as_none()
    {
        Directory.CreateDirectory(_folder.Cards.Folder);
        File.WriteAllText(_folder.Cards.PathFor("octopus"), "{ not json");

        Assert.Null(_folder.Cards.Get("octopus"));
        Assert.Empty(_folder.Cards.All());
    }

    [Fact]
    public void Cards_are_not_mistaken_for_pictures()
    {
        _folder.Cards.Save(new ThingCard("octopus", "octopus") { Climb = ClimbChoice.Real }, _now);

        Assert.Empty(_folder.All());
    }
}
