using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

/// <summary>What a grown-up may choose for a rung of a thing's ladder, and how it is written down.</summary>
public class RungChoiceTests
{
    [Theory]
    [InlineData("auto", RungKind.Auto, 0)]
    [InlineData("shape", RungKind.Shape, 0)]
    [InlineData("skip", RungKind.Skip, 0)]
    [InlineData("picture:3", RungKind.Picture, 3)]
    [InlineData(" Picture:12 ", RungKind.Picture, 12)]
    public void A_choice_is_read_from_its_word_and_written_back_as_it(string text, RungKind kind, int picture)
    {
        Assert.True(RungChoice.TryParse(text, out var choice));
        Assert.Equal(new RungChoice(kind, picture), choice);
        Assert.Equal(text.Trim().ToLowerInvariant(), choice.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("photo")]
    [InlineData("picture:")]
    [InlineData("picture:0")]
    [InlineData("picture:-2")]
    [InlineData("picture:two")]
    public void Anything_else_is_not_a_choice(string? text) => Assert.False(RungChoice.TryParse(text, out _));

    [Fact]
    public void Choices_are_kept_as_words_in_json_and_come_back_the_same()
    {
        var choices = new RungChoices().With(PictureStage.Silhouette, RungChoice.Skip).With(PictureStage.Flat, RungChoice.OfPicture(2)).With(PictureStage.Photo, RungChoice.OfPicture(3));

        var json = JsonSerializer.Serialize(choices);

        Assert.Equal("""{"shape":"auto","silhouette":"skip","flat":"picture:2","photo":"picture:3"}""", json);
        Assert.Equal(choices, JsonSerializer.Deserialize<RungChoices>(json));
    }

    [Fact]
    public void A_word_from_a_later_version_reads_as_auto_and_the_other_rungs_are_kept()
    {
        var choices = JsonSerializer.Deserialize<RungChoices>("""{"shape":"drawing:7","silhouette":5,"flat":"skip"}""")!;

        Assert.Equal(RungChoice.Auto, choices.Shape);
        Assert.Equal(RungChoice.Auto, choices.Silhouette);
        Assert.Equal(RungChoice.Skip, choices.Flat);
        Assert.Equal(RungChoice.Auto, choices.Photo);
    }

    [Fact]
    public void The_photo_rung_takes_only_auto_or_a_picture()
    {
        var choices = new RungChoices();

        Assert.Throws<ArgumentException>(() => choices.With(PictureStage.Photo, RungChoice.Skip));
        Assert.Throws<ArgumentException>(() => choices.With(PictureStage.Photo, RungChoice.Shape));
        Assert.Equal(RungChoice.OfPicture(4), choices.With(PictureStage.Photo, RungChoice.OfPicture(4)).Photo);
        foreach (var stage in new[] { PictureStage.Silhouette, PictureStage.Flat })
            foreach (var choice in new[] { RungChoice.Auto, RungChoice.Shape, RungChoice.Skip, RungChoice.OfPicture(1) })
                Assert.Equal(choice, choices.With(stage, choice).For(stage));
    }

    [Fact]
    public void The_shape_rung_takes_no_picture()
    {
        var choices = new RungChoices();

        Assert.Throws<ArgumentException>(() => choices.With(PictureStage.Shape, RungChoice.OfPicture(1)));
        foreach (var choice in new[] { RungChoice.Auto, RungChoice.Shape, RungChoice.Skip })
            Assert.Equal(choice, choices.With(PictureStage.Shape, choice).Shape);
    }

    [Theory]
    // reached, cap, skipped rungs, landing
    [InlineData(PictureStage.Silhouette, PictureStage.Photo, "", PictureStage.Silhouette)]
    [InlineData(PictureStage.Silhouette, PictureStage.Photo, "Silhouette", PictureStage.Flat)]
    [InlineData(PictureStage.Shape, PictureStage.Photo, "Shape,Silhouette", PictureStage.Flat)]
    [InlineData(PictureStage.Silhouette, PictureStage.Photo, "Silhouette,Flat", PictureStage.Photo)]
    [InlineData(PictureStage.Photo, PictureStage.Flat, "", PictureStage.Flat)]
    [InlineData(PictureStage.Photo, PictureStage.Flat, "Flat", PictureStage.Silhouette)]
    [InlineData(PictureStage.Flat, PictureStage.Flat, "Silhouette,Flat", PictureStage.Shape)]
    [InlineData(PictureStage.Flat, PictureStage.Flat, "Shape,Silhouette,Flat", PictureStage.Shape)]
    public void A_skipped_rung_is_passed_over_upwards_and_the_cap_is_never_passed(PictureStage reached, PictureStage cap, string skipped, PictureStage landing)
    {
        var choices = new RungChoices();
        foreach (var name in skipped.Split(',', StringSplitOptions.RemoveEmptyEntries))
            choices = choices.With(Enum.Parse<PictureStage>(name), RungChoice.Skip);

        Assert.Equal(landing, choices.Landing(reached, cap));
    }

    [Fact]
    public void Choices_know_which_pictures_they_use_and_whether_a_grown_up_set_any()
    {
        var none = new RungChoices();
        var some = none.With(PictureStage.Flat, RungChoice.OfPicture(2));

        Assert.False(none.AnyByHand);
        Assert.True(some.AnyByHand);
        Assert.True(some.Uses(2));
        Assert.False(some.Uses(1));
        Assert.False(none.With(PictureStage.Shape, RungChoice.Skip).Uses(0));
    }
}
