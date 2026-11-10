using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Rendering;

namespace Keypuncher.Pictures.Tests;

/// <summary>The list of things on the grown-ups' page, put together from everything the pictures folder holds.</summary>
public sealed class ThingIndexTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-index-tests").FullName;
    private readonly DateTime _now = new(2026, 11, 10, 18, 0, 0);
    private readonly PictureFolder _folder;

    public ThingIndexTests() => _folder = new PictureFolder(Path.Combine(_dir, "pictures"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Picture(string name, DateTime at, RealCategory category = RealCategory.Animal, VisionCheck? check = null, int id = 1)
    {
        using var cutOut = SamplePicture.Draw();
        _folder.WriteSidecar(new PictureSidecar(Slugs.For(name), name, category, PictureSource.Photo, at) { Id = id, Check = check });
        _folder.WritePicture(Slugs.For(name), cutOut);
    }

    private static VisionCheck Failed => new("gemma3:4b", false, 0, "a plaid cloth", new DateTime(2026, 11, 10, 17, 0, 0));

    [Fact]
    public void An_empty_folder_lists_only_the_starter_things()
    {
        var things = ThingIndex.Build(_folder, ["sun", "cat"]);

        Assert.Equal(["cat", "sun"], things.Select(t => t.Name));
        Assert.All(things, t => Assert.True(t.Starter && t.Picture is null && !t.InPlay && !t.NeedsALook && t.Category is null && t.Touched is null));
    }

    [Fact]
    public void Every_thing_the_folder_knows_is_listed_once_newest_first()
    {
        Picture("Sable antelope", _now.AddHours(-2));
        Picture("cat", _now.AddHours(-1));
        _folder.Wanted.Add("plastic straw", RealCategory.Object, _now);
        var card = _folder.Cards.GetOrNew("teapot");
        _folder.Cards.Save(card with { Climb = ClimbChoice.Real }, _now.AddHours(-3));

        var things = ThingIndex.Build(_folder, ["sun", "cat"]);

        Assert.Equal(["plastic straw", "cat", "Sable antelope", "teapot", "sun"], things.Select(t => t.Name));
        var cat = things.Single(t => t.Slug == "cat");
        Assert.True(cat.Starter && cat.InPlay && cat.Picture is not null);
        Assert.Equal(RealCategory.Object, things[0].Category);
        Assert.Equal(ClimbChoice.Real, things.Single(t => t.Slug == "teapot").Card!.Climb);
    }

    [Fact]
    public void The_pictures_to_choose_from_are_the_one_in_play_and_the_kept_ones_by_number()
    {
        Picture("fish", _now);
        _folder.Keep("fish");
        Picture("fish", _now.AddMinutes(5), id: 2);

        var fish = Assert.Single(ThingIndex.Build(_folder, []));

        Assert.Equal([1, 2], fish.Choosable.Select(p => p.Id));
        Assert.Equal(1, Assert.Single(fish.Kept).Id);
        Assert.Equal(2, fish.Picture!.Id);
    }

    [Fact]
    public void A_hidden_picture_is_not_one_to_choose_and_needs_a_look()
    {
        Picture("fish", _now);
        _folder.Hide("fish");

        var fish = Assert.Single(ThingIndex.Build(_folder, []));

        Assert.True(fish.Hidden && fish.NeedsALook && !fish.InPlay);
        Assert.Empty(fish.Choosable);
    }

    [Fact]
    public void A_thing_with_only_kept_pictures_is_still_listed()
    {
        Picture("fish", _now);
        _folder.Keep("fish");
        File.Delete(_folder.PicturePath("fish"));
        File.Delete(_folder.SidecarPath("fish"));

        var fish = Assert.Single(ThingIndex.Build(_folder, []));

        Assert.Equal(("fish", "fish", false), (fish.Slug, fish.Name, fish.InPlay));
        Assert.Null(fish.Picture);
        Assert.Single(fish.Choosable);
        Assert.Equal(RealCategory.Animal, fish.Category);
    }

    [Fact]
    public void The_kind_a_grown_up_set_wins_over_the_pictures()
    {
        Picture("sailboat", _now);
        _folder.Cards.Save(_folder.Cards.GetOrNew("sailboat") with { Category = RealCategory.Vehicle }, _now);

        Assert.Equal(RealCategory.Vehicle, Assert.Single(ThingIndex.Build(_folder, [])).Category);
    }

    [Fact]
    public void Needs_a_look_finds_failed_hidden_and_wanted_things()
    {
        Picture("cat", _now);
        Picture("coconut", _now, RealCategory.Plant, Failed);
        Picture("fish", _now);
        _folder.Hide("fish");
        _folder.Wanted.Add("plastic straw", RealCategory.Object, _now);
        // Wanted because another was asked for, with a good picture still in play: nothing to look at yet.
        Picture("dog", _now);
        _folder.Wanted.Add("dog", RealCategory.Animal, _now);

        var things = ThingIndex.Build(_folder, ["sun"]);

        Assert.Equal(["coconut", "fish", "plastic straw"], ThingIndex.Select(things, ThingFilter.NeedsALook, null).Select(t => t.Name).Order());
        Assert.Equal(["sun"], ThingIndex.Select(things, ThingFilter.Starter, "").Select(t => t.Name));
        Assert.Equal(things.Count, ThingIndex.Select(things, ThingFilter.All, "  ").Count);
    }

    [Fact]
    public void The_search_looks_in_names_and_in_the_grown_ups_words_whatever_the_case()
    {
        Picture("Sable antelope", _now);
        Picture("cat", _now);
        _folder.Cards.Save(_folder.Cards.GetOrNew("cat") with { Words = new Dictionary<string, string> { ["is"] = "köttur" } }, _now);
        var things = ThingIndex.Build(_folder, []);

        Assert.Equal(["Sable antelope"], ThingIndex.Select(things, ThingFilter.All, "ANTE").Select(t => t.Name));
        Assert.Equal(["cat"], ThingIndex.Select(things, ThingFilter.All, "Kött").Select(t => t.Name));
        Assert.Empty(ThingIndex.Select(things, ThingFilter.All, "zebra"));
    }
}
