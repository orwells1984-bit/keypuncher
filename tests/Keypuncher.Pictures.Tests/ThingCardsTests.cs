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
    public void A_word_is_set_trimmed_and_a_blank_one_takes_it_away()
    {
        var card = new ThingCard("octopus", "octopus").WithWord("is", "  kolkrabbi ").WithWord("bg", "октопод");
        Assert.Equal(("kolkrabbi", "октопод"), (card.WordIn("is"), card.WordIn("bg")));

        var without = card.WithWord("is", " ").WithWord("bg", null);
        Assert.Empty(without.Words);
        Assert.True(without.IsEmpty);
        // The card it was made from is left as it was.
        Assert.Equal(2, card.Words.Count);
    }

    [Fact]
    public void The_words_in_a_language_are_listed_by_slug()
    {
        _folder.Cards.Save(new ThingCard("octopus", "octopus").WithWord("is", "kolkrabbi"), _now);
        _folder.Cards.Save(new ThingCard("plastic-straw", "plastic straw").WithWord("is", "rör").WithWord("en", "straw"), _now);
        _folder.Cards.Save(new ThingCard("teapot", "teapot") { Climb = ClimbChoice.Real }, _now);

        var icelandic = _folder.Cards.WordsIn("is");
        Assert.Equal(["octopus", "plastic-straw"], icelandic.Keys.Order());
        Assert.Equal("rör", icelandic["plastic-straw"]);
        Assert.Equal(["plastic-straw"], _folder.Cards.WordsIn("en").Keys);
        Assert.Empty(_folder.Cards.WordsIn("bg"));
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

    [Fact]
    public void The_book_holds_the_drawing_choices_in_memory_and_follows_every_save()
    {
        var skip = new RungChoices().With(PictureStage.Silhouette, RungChoice.Skip);
        _folder.Cards.Save(new ThingCard("octopus", "octopus") { Rungs = skip }, _now);
        _folder.Cards.Save(new ThingCard("sailboat", "sailboat") { Category = RealCategory.Vehicle }, _now);
        using var book = new ChoiceBook(_folder.Cards);
        var changed = new List<string>();
        book.Changed += changed.Add;

        Assert.Equal(new ThingChoices(ClimbChoice.Auto, skip), book.Get("octopus"));
        Assert.Null(book.Get("sailboat"));
        Assert.Null(book.Get("zebra"));

        _folder.Cards.Save(new ThingCard("zebra", "zebra") { Climb = ClimbChoice.Ladder }, _now);
        _folder.Cards.Remove("octopus");

        Assert.Equal(ClimbChoice.Ladder, book.Get("zebra")!.Climb);
        Assert.Null(book.Get("octopus"));
        Assert.Equal(["zebra", "octopus"], changed);
    }

    [Fact]
    public void Reloading_the_book_picks_up_what_another_program_changed_and_says_which_things()
    {
        _folder.Cards.Save(new ThingCard("octopus", "octopus") { Climb = ClimbChoice.Ladder }, _now);
        _folder.Cards.Save(new ThingCard("zebra", "zebra") { Climb = ClimbChoice.Real }, _now);
        using var book = new ChoiceBook(_folder.Cards);
        var elsewhere = new PictureFolder(_folder.Root).Cards;
        elsewhere.Save(new ThingCard("octopus", "octopus") { Climb = ClimbChoice.Real }, _now);
        elsewhere.Save(new ThingCard("crab", "crab") { Climb = ClimbChoice.Ladder }, _now);
        Assert.Equal(ClimbChoice.Ladder, book.Get("octopus")!.Climb);

        var changed = book.Reload();

        Assert.Equal(["crab", "octopus"], changed.Order());
        Assert.Equal(ClimbChoice.Real, book.Get("octopus")!.Climb);
        Assert.Equal(ClimbChoice.Ladder, book.Get("crab")!.Climb);
        Assert.Equal(ClimbChoice.Real, book.Get("zebra")!.Climb);
        Assert.Empty(book.Reload());
    }
}
