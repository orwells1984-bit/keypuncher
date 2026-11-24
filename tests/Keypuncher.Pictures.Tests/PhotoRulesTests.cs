using Keypuncher.Core;
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
    [InlineData("File:Parasaurolophus holotype skulls.png", "holotype")]
    [InlineData("File:Points of a horse.jpg", "points")]
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

    [Fact]
    public void Remains_in_the_plural_are_not_a_life_restoration() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Parasaurolophus holotype skulls.png"), RealCategory.ExtinctAnimal)!.Value.Kind);

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

    [Theory]
    [InlineData("File:Common Seal Phoca vitulina.jpg", "harbour seal")]
    [InlineData("File:Harbor Seal, Phoca vitulina - Flickr - GregTheBusker.jpg", "harbour seal")]
    [InlineData("File:Harbor seal in Holyoke, MA. Connecticut River. 01.jpg", "harbour seal")]
    public void The_kind_of_thing_is_never_held_against_a_file_that_names_it_another_way(string title, string name)
    {
        // 2026-11-24: all three were turned down for "seal", and a museum skull went into play as the harbour seal.
        Assert.Null(PhotoRules.Vet(Facts(title), RealCategory.Animal, name));
    }

    [Fact]
    public void The_kind_of_thing_does_not_excuse_other_bad_words() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Common seal skull.jpg"), RealCategory.Animal, "harbour seal")!.Value.Kind);

    [Theory]
    [InlineData("File:Phoca vitulina 05 MWNH 1464.JPG", "MWNH 1464")]
    [InlineData("File:Puffin NHMUK 1881.5.4.jpg", "NHMUK 1881")]
    [InlineData("File:Fox USNM49440.jpg", "USNM49440")]
    public void A_museum_catalogue_number_is_a_specimen(string title, string number)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.Animal, "harbour seal");
        Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
        Assert.Contains(number, verdict.Value.Why);
    }

    [Theory]
    [InlineData("File:IMG 1234 puffin.jpg")]
    [InlineData("File:DSC 0042 Puffin.jpg")]
    [InlineData("File:Puffin by SMITH 2019.jpg")]
    public void A_cameras_number_is_not_a_museums(string title) =>
        Assert.Null(PhotoRules.Vet(Facts(title), RealCategory.Animal));

    [Theory]
    [InlineData("Category:Phoca vitulina skulls")]
    [InlineData("Category:Phoca vitulina (skeletons)")]
    [InlineData("Category:Natural history collections of Museum Wiesbaden")]
    [InlineData("Category:Mammal specimens in the Natural History Museum, London")]
    [InlineData("Category:Taxidermied Phoca vitulina")]
    public void A_living_animal_in_a_remains_or_museum_category_is_turned_down(string category) =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Phoca vitulina 05.jpg", categories: category), RealCategory.Animal, "harbour seal")!.Value.Kind);

    [Theory]
    [InlineData("Category:Albula vulpes")]
    [InlineData("Category:Albula vulpes in the Bahamas")]
    public void A_word_that_only_contains_a_remains_word_is_not_one(string category) =>
        Assert.Null(PhotoRules.Vet(Facts("File:Bonefish 1.jpg", categories: category), RealCategory.Animal, "bonefish"));

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

    [Theory]
    [InlineData("File:Flock of sheep.jpg", "sheep")]
    [InlineData("File:Anas platyrhynchos (mixed pair) (32428014687).jpg", "duck")]
    [InlineData("File:Eubalaena glacialis with calf.jpg", "whale")]
    [InlineData("File:Kangaroo and joey03.jpg", "brown kangaroo")]
    [InlineData("File:Bird Diversity 2011.png", "bird")]
    [InlineData("File:Leaf Varieties (15 sp).png", "serrated leaf")]
    [InlineData("File:Mangos - single and halved.jpg", "mango")]
    [InlineData("File:Zebras in Etosha.jpg", "zebra")]
    [InlineData("File:Coconut seller in Goa.jpg", "coconut")]
    public void A_photo_of_several_or_of_the_thing_with_people_is_turned_down(string title, string name)
    {
        var verdict = PhotoRules.Vet(Facts(title), RealCategory.Animal, name);
        Assert.Equal(Rejection.Hard, verdict!.Value.Kind);
    }

    [Theory]
    [InlineData("File:Sheep on a hill.jpg", "sheep")]
    [InlineData("File:Mango Maya.jpg", "mango")]
    [InlineData("File:Cyanea capillata no.JPG", "jellyfish")]
    [InlineData("File:Grass snake.jpg", "grass snake")]
    public void A_photo_of_one_passes_the_group_rule(string title, string name) =>
        Assert.Null(PhotoRules.Vet(Facts(title), RealCategory.Animal, name));

    [Fact]
    public void A_category_of_groups_is_turned_down() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:Sheep 12.jpg", categories: "Category:Flocks of sheep in Wales"), RealCategory.Animal, "sheep")!.Value.Kind);

    /// <summary>On 2026-11-22 a 13th-century manuscript page went into play as Steller's sea cow: art is welcome for an
    /// extinct animal, a page of an old book is not.</summary>
    [Theory]
    [InlineData("File:Der naturen bloeme - Jacob van Maerlant - KB KA 16 - 110v.jpg")]
    [InlineData("File:Aberdeen Bestiary folio 12.jpg")]
    [InlineData("File:Sea cow manuscript.jpg")]
    public void A_manuscript_page_is_turned_down_even_for_an_extinct_animal(string title) =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts(title), RealCategory.ExtinctAnimal, "steller's sea cow")!.Value.Kind);

    /// <summary>On 2026-11-23 the page that took its place was another one: fetched before "folio" counted, and passed by
    /// the checker. A library's shelf mark, an old-book category or a date before 1600 each turn a file down, whatever
    /// the thing is.</summary>
    [Theory]
    [InlineData("File:Visboeck zeekoe KB 78 E 54.jpg", null, null)]
    [InlineData("File:Bestiary whale BL Royal 12 C xix.jpg", null, null)]
    [InlineData("File:Zeekoe.jpg", "Category:Visboek by Adriaen Coenen", null)]
    [InlineData("File:Zeekoe.jpg", "Category:Medieval manuscripts of fish", null)]
    [InlineData("File:Zeekoe.jpg", null, "between 1577 and 1580")]
    [InlineData("File:Zeekoe.jpg", null, "13th century")]
    public void A_scan_of_an_old_book_is_turned_down_for_anything(string title, string? category, string? made)
    {
        var facts = new FileFacts(title, "image/jpeg", "BITMAP", 2000, 1400, category is null ? [] : [category], "Public domain", "False", null, made);
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(facts, RealCategory.ExtinctAnimal, "steller's sea cow")!.Value.Kind);
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(facts, RealCategory.Animal, "whale")!.Value.Kind);
    }

    [Theory]
    [InlineData("File:MS Estonia 1993.jpg", RealCategory.Vehicle, "ferry", "1993-06-01")]
    [InlineData("File:Allosaurus Juvenile Reconstruction.jpg", RealCategory.ExtinctAnimal, "allosaurus", "2019")]
    [InlineData("File:Dodo 1.JPG", RealCategory.ExtinctAnimal, "dodo", "circa 1626")]
    [InlineData("File:IMG 2034 480kb.jpg", RealCategory.Animal, "cat", "2011-05-03 12:22:11")]
    public void A_ship_a_restoration_and_a_dated_photo_are_not_taken_for_old_books(string title, RealCategory category, string name, string made) =>
        Assert.Null(PhotoRules.Vet(new FileFacts(title, "image/jpeg", "BITMAP", 2000, 1400, [], "CC BY-SA 4.0", "True", null, made), category, name));

    [Theory]
    [InlineData("between 1577 and 1580", 1577)]
    [InlineData("13th century", 1200)]
    [InlineData("2011-05-03 12:22:11", 2011)]
    [InlineData("Unknown date", null)]
    [InlineData(null, null)]
    public void The_year_a_picture_was_made_is_read_from_commons_wording(string? made, int? year) =>
        Assert.Equal(year, PhotoRules.YearMade(made));

    [Fact]
    public void A_kept_photo_is_judged_again_by_its_file_name_alone()
    {
        Assert.NotNull(PhotoRules.NameFault("File:Adriaen Coenen's Visboeck - KB 78 E 54 - folio 395v.jpg", RealCategory.ExtinctAnimal, "steller's sea cow"));
        Assert.Null(PhotoRules.NameFault("File:Allosaurus Juvenile Reconstruction.jpg", RealCategory.ExtinctAnimal, "allosaurus"));
        // A painting is only a doubt, and the checker has seen it; a photo from iNaturalist has no file name to judge.
        Assert.Null(PhotoRules.NameFault("File:Red fox painting.jpg", RealCategory.Animal, "red fox"));
        Assert.Null(PhotoRules.NameFault("inaturalist:116758814", RealCategory.Animal, "red fox"));
    }

    [Fact]
    public void A_manuscript_category_is_turned_down() =>
        Assert.Equal(Rejection.Hard, PhotoRules.Vet(Facts("File:KB KA 16 sea cow.jpg", categories: "Category:Der naturen bloeme"), RealCategory.ExtinctAnimal, "steller's sea cow")!.Value.Kind);

    [Theory]
    [InlineData("110v", true)]
    [InlineData("12r", true)]
    [InlineData("v", false)]
    [InlineData("4x4", false)]
    [InlineData("river", false)]
    public void A_folio_number_is_known(string word, bool folio) => Assert.Equal(folio, PhotoRules.IsFolio(word));
}
