using Keypuncher.Director;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

public sealed class PhotoRulesTests
{
    private static FileFacts Facts(string title, int width = 2000, int height = 1400, string mime = "image/jpeg", string mediaType = "BITMAP",
        string licence = "CC BY-SA 4.0", string copyrighted = "True", string? url = null, params string[] categories) =>
        new(title, mime, mediaType, width, height, categories, licence, copyrighted, url);

    [Fact]
    public void A_plain_photo_of_the_thing_passes() =>
        Assert.Null(PhotoRules.Vet(Facts("File:Sable antelope male.jpg", categories: "Category:Hippotragus niger"), RealCategory.Animal));

    [Theory]
    [InlineData("File:Sable antelope range.svg", "image/svg+xml", "DRAWING")]
    [InlineData("File:Sable antelope.gif", "image/gif", "BITMAP")]
    [InlineData("File:Sable antelope.tif", "image/tiff", "BITMAP")]
    public void Only_jpeg_png_and_webp_are_photos(string title, string mime, string mediaType)
    {
        var verdict = PhotoRules.Vet(Facts(title, mime: mime, mediaType: mediaType), RealCategory.Animal);
        Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
    }

    [Theory]
    [InlineData(500, 400)]
    [InlineData(599, 300)]
    public void Small_pictures_are_turned_down(int width, int height) =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Puffin.jpg", width, height), RealCategory.Animal)!.Value.Kind);

    [Theory]
    [InlineData(2000, 900, false)]
    [InlineData(2000, 1000, true)]
    [InlineData(2000, 1001, true)]
    [InlineData(600, 1300, false)]
    [InlineData(3000, 1200, false)]
    public void Only_sensible_shapes_pass(int width, int height, bool passes) =>
        Assert.Equal(passes, PhotoRules.Vet(Facts("File:Puffin.jpg", width, height), RealCategory.Animal) is null);

    [Theory]
    [InlineData("File:Puffin range map.png", "range")]
    [InlineData("File:Puffin skull.jpg", "skull")]
    [InlineData("File:Puffin egg.jpg", "egg")]
    [InlineData("File:Tractor cockpit.jpg", "cockpit")]
    [InlineData("File:Puffin_distribution.jpg", "distribution")]
    public void File_names_that_mean_something_else_are_turned_down(string title, string word)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.Animal);
        Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
        Assert.Contains(word, verdict.Value.Why);
    }

    [Theory]
    [InlineData("Category:Taxidermied puffins")]
    [InlineData("Category:Skeletons of birds")]
    [InlineData("Category:Maps of Iceland")]
    [InlineData("Category:Scale models of tractors")]
    [InlineData("Category:Botanical illustrations of oaks")]
    public void Categories_that_mean_something_else_are_turned_down(string category)
    {
        var verdict = PhotoRules.Vet(Facts("File:Puffin 1.jpg", categories: category), RealCategory.Animal);
        Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
        Assert.Contains("category", verdict.Value.Why);
    }

    [Theory]
    [InlineData("File:Puffin painting by Audubon.jpg")]
    [InlineData("File:Oak tree artwork.jpg")]
    public void A_drawing_of_a_living_thing_is_a_soft_no(string title) =>
        Assert.Equal(Rejection.Soft, PhotoRules.Vet(Facts(title), RealCategory.Animal)!.Value.Kind);

    [Theory]
    [InlineData("File:Harbor seal on rocks.jpg", "seal")]
    [InlineData("File:Hornbill horn close.jpg", "hornbill horn")]
    [InlineData("File:Seal skull.jpg", null)]
    public void The_things_own_name_is_never_held_against_a_file(string title, string? name)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.Animal, name);
        if (name is null) Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
        else Assert.Null(verdict);
    }

    [Fact]
    public void The_things_own_name_does_not_excuse_other_bad_words() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Seal skull.jpg"), RealCategory.Animal, "seal")!.Value.Kind);

    [Theory]
    [InlineData("File:Graptemys map.png", "map turtle", "map")]
    [InlineData("File:Northern map turtle map.png", "map turtle", "map")]
    [InlineData("File:Northern map turtle.jpg", "map turtle", null)]
    [InlineData("File:Range Rover range.png", "Range Rover", "range")]
    [InlineData("File:Fire engine engine.jpg", "fire engine", "engine")]
    [InlineData("File:Fire engine in Leeds.jpg", "fire engine", null)]
    public void The_things_own_name_is_excused_only_where_it_is_spelt_out_whole(string title, string name, string? bad)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.Vehicle, name);
        if (bad is null) Assert.Null(verdict);
        else Assert.Equal((Rejection.Hard, $"file name says {bad}"), verdict!.Value);
    }

    [Fact]
    public void A_category_of_paintings_is_a_soft_no() =>
        Assert.Equal(Rejection.Soft, PhotoRules.Vet(Facts("File:Hippotragus niger 1.jpg", categories: "Category:Paintings of antelopes"), RealCategory.Animal)!.Value.Kind);

    [Fact]
    public void A_local_wikipedia_upload_is_turned_down() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Puffin.jpg", url: "https://upload.wikimedia.org/wikipedia/en/3/3a/Puffin.jpg"), RealCategory.Animal)!.Value.Kind);

    [Fact]
    public void A_copyrighted_file_without_a_licence_is_turned_down() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Puffin.jpg", licence: "", copyrighted: "True"), RealCategory.Animal)!.Value.Kind);

    [Fact]
    public void A_public_domain_file_needs_no_licence_tag() =>
        Assert.Null(PhotoRules.Vet(Facts("File:Puffin.jpg", licence: "", copyrighted: "False"), RealCategory.Animal));

    [Theory]
    [InlineData("File:Woolly mammoth skeleton.jpg", true)]
    [InlineData("File:Mammuthus primigenius mummy.jpg", true)]
    [InlineData("File:Woolly mammoth restoration.jpg", false)]
    [InlineData("File:Woolly mammoth illustration.jpg", false)]
    [InlineData("File:Woolly mammoth range map.png", true)]
    public void For_an_extinct_animal_remains_are_turned_down_and_restorations_welcome(string title, bool turnedDown)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.ExtinctAnimal);
        Assert.Equal(turnedDown, verdict is not null);
        if (verdict is { } v) Assert.Equal(Rejection.Hard, v.Kind);
    }

    [Fact]
    public void An_extinct_animal_in_a_skeleton_category_is_turned_down() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Mammut 1.jpg", categories: "Category:Mammuthus primigenius skeletons"), RealCategory.ExtinctAnimal)!.Value.Kind);

    [Theory]
    [InlineData("File:Woolly mammoth restoration.jpg", true)]
    [InlineData("File:Mammoth life reconstruction.png", true)]
    [InlineData("File:Mammoth paleoart.jpg", true)]
    [InlineData("File:Mammoth skeleton.jpg", false)]
    public void A_restoration_is_known_by_its_name(string title, bool restoration) =>
        Assert.Equal(restoration, PhotoRules.IsRestoration(Facts(title)));

    [Fact]
    public void A_restoration_is_known_by_its_category() =>
        Assert.True(PhotoRules.IsRestoration(Facts("File:Mammut 2.jpg", categories: "Category:Mammuthus primigenius life restorations")));

    [Fact]
    public void Words_split_a_file_name_on_what_commons_uses() =>
        Assert.Equal(["sable", "antelope", "hippotragus", "niger", "male", "jpg"], PhotoRules.Words("File:Sable_antelope-(Hippotragus niger), male.jpg"));
}
