using Keypuncher.Core;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

/// <summary>The realism ladder: a thing with a picture climbs from its stock shape to its picture as it is met
/// over separate plays, one without keeps its shape, the level caps the rung, pictures are read and drawn off the
/// drawing thread and seen when they land, and a picture is drawn afresh only a few times in a long play.</summary>
public sealed class PictureLibraryTests : IDisposable
{
    /// <summary>A colour no palette or landscape uses, so any of it on screen came from the picture.</summary>
    private static readonly SKColor Magenta = new(0xFF, 0x00, 0xFF);
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-picture-tests").FullName;
    private DateTime _now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("sable antelope", "sable-antelope")]
    [InlineData("  Sable  Antelope! ", "sable-antelope")]
    [InlineData("Þingvallableikja", "þingvallableikja")]
    [InlineData("T. rex", "t-rex")]
    public void Names_map_to_one_file_each(string name, string slug) => Assert.Equal(slug, PictureLibrary.Slug(name));

    [Fact]
    public void Each_stage_lasts_a_few_meetings_and_the_picture_is_the_top()
    {
        Assert.Equal(PictureStage.Shape, PictureLibrary.StageFor(0));
        Assert.Equal(PictureStage.Shape, PictureLibrary.StageFor(PictureLibrary.MeetingsPerStage - 1));
        Assert.Equal(PictureStage.Silhouette, PictureLibrary.StageFor(PictureLibrary.MeetingsPerStage));
        Assert.Equal(PictureStage.Flat, PictureLibrary.StageFor(2 * PictureLibrary.MeetingsPerStage));
        Assert.Equal(PictureStage.Photo, PictureLibrary.StageFor(3 * PictureLibrary.MeetingsPerStage));
        Assert.Equal(PictureStage.Photo, PictureLibrary.StageFor(1000));
    }

    [Fact]
    public void Meetings_within_the_gap_count_once_and_are_kept_between_sessions()
    {
        using (var library = Library())
        {
            Assert.True(library.Meet("sable antelope"));
            Assert.False(library.Meet("Sable Antelope"));
            _now += PictureLibrary.MeetingGap / 2;
            Assert.False(library.Meet("sable antelope"));
            _now += PictureLibrary.MeetingGap;
            Assert.True(library.Meet("sable antelope"));
            Assert.True(library.Meet("zebra"));
            Assert.Equal(2, library.Meetings("sable antelope"));
            Assert.Equal(1, library.Meetings("zebra"));
            Assert.True(library.Dirty);
            library.Save();
            Assert.False(library.Dirty);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }
        using var again = Library();
        Assert.Equal(2, again.Meetings("sable antelope"));
        _now += TimeSpan.FromMinutes(1);
        Assert.False(again.Meet("zebra"), "the gap holds across a restart");
    }

    [Fact]
    public void The_first_versions_bare_counts_are_read_and_old_names_without_a_picture_are_forgotten()
    {
        WritePicture("sable-antelope");
        var stale = (_now - PictureLibrary.Forget - TimeSpan.FromDays(1)).ToString("o");
        File.WriteAllText(Path.Combine(_dir, "meetings.json"),
            "{\"cat\": 4, \"sable-antelope\": {\"n\": 2, \"last\": \"" + stale + "\"}, \"dodo\": {\"n\": 7, \"last\": \"" + stale + "\"}}");
        using var library = Library();
        Assert.Equal(4, library.Meetings("cat"));
        Assert.Equal(2, library.Meetings("sable antelope"));
        Assert.Equal(0, library.Meetings("dodo"));
    }

    [Fact]
    public void A_broken_file_is_ignored_and_the_thing_keeps_its_shape()
    {
        File.WriteAllText(Path.Combine(_dir, "zebra.png"), "not a picture");
        using var library = Library();
        library.Forced = PictureStage.Photo;
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("zebra"), ArtStyle.Toddler));
        library.WaitIdle(TimeSpan.FromSeconds(10));
        Assert.False(library.Has("zebra"));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("zebra"), ArtStyle.Toddler));
    }

    [Fact]
    public void A_picture_written_during_play_is_seen_once_it_is_renamed_into_place()
    {
        using var library = Library();
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        library.Forced = PictureStage.Photo;
        var world = World(CalmLevel.BigToddler, "sable antelope");
        using (var before = Render(renderer, world)) Assert.Equal(0, Count(before, p => Near(p, Magenta)));

        // Written under a temporary name first: not a picture yet.
        WritePicture("sable-antelope", ".png.tmp");
        _now += TimeSpan.FromSeconds(10);
        using (var still = Render(renderer, world)) Assert.Equal(0, Count(still, p => Near(p, Magenta)));
        Assert.False(library.IsLoading("sable antelope"));

        File.Move(Path.Combine(_dir, "sable-antelope.png.tmp"), Path.Combine(_dir, "sable-antelope.png"));
        _now += TimeSpan.FromSeconds(10);
        using (Render(renderer, world)) { }
        library.Has("sable antelope");
        library.WaitIdle(TimeSpan.FromSeconds(10));
        Assert.True(library.Has("sable antelope"), "the picture is read once it is in place");
        // The thing already on screen keeps the rung it was born at; the next one born gets the picture.
        using (var same = Render(renderer, world)) Assert.Equal(0, Count(same, p => Near(p, Magenta)));
        world.Add(new Entity { Id = 2, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.8, Y = 0.5, Born = world.Time });
        world.Tick(1);
        using var after = Settled(renderer, library, world);
        Assert.True(Count(after, p => Near(p, Magenta)) > 500, "the next one born is drawn from the picture");
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void A_name_that_does_not_climb_is_its_picture_from_the_first_meeting_at_every_level(CalmLevel level)
    {
        WritePicture("sable-antelope");
        WritePicture("zebra");
        using var library = Library();
        library.Climbs = name => name != "sable antelope";
        var style = ArtStyle.For(level);
        library.Stage(Thing("sable antelope"), style);
        library.Stage(Thing("zebra"), style);
        library.WaitIdle(TimeSpan.FromSeconds(10));
        library.Pump();
        Assert.Equal(PictureStage.Photo, library.Stage(Thing("sable antelope"), style));
        Assert.Equal(PictureStage.Photo, library.Cap("sable antelope", style));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("zebra"), style));
        Assert.Equal(style.MaxPictureStage, library.Cap("zebra", style));
    }

    [Fact]
    public void A_director_pick_on_screen_turns_into_its_photo_when_the_photo_lands_even_in_Baby()
    {
        using var library = Library();
        library.Climbs = BuiltInRecipes.IsBuiltIn;
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var world = World(CalmLevel.Baby, "sable antelope");
        using (var before = Render(renderer, world)) Assert.Equal(0, Count(before, p => Near(p, Magenta)));

        WritePicture("sable-antelope");
        _now += TimeSpan.FromSeconds(10);
        using var after = Settled(renderer, library, world);
        Assert.True(Count(after, p => Near(p, Magenta)) > 500, "the thing already on screen is now its photo");
    }

    [Fact]
    public void The_toys_own_things_are_built_in_and_the_directors_picks_are_not()
    {
        Assert.True(BuiltInRecipes.IsBuiltIn("cat"));
        Assert.True(BuiltInRecipes.IsBuiltIn("Longneck"));
        Assert.True(BuiltInRecipes.IsBuiltIn("puffbird"));
        Assert.True(BuiltInRecipes.IsBuiltIn("house"));
        Assert.True(BuiltInRecipes.IsBuiltIn("ball"));
        Assert.False(BuiltInRecipes.IsBuiltIn("sable antelope"));
        Assert.False(BuiltInRecipes.IsBuiltIn("wild onion"));
        Assert.False(BuiltInRecipes.IsBuiltIn("acacia tree"));
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void Only_the_photo_stage_shows_the_picture_itself_and_the_level_caps_it(CalmLevel level)
    {
        WritePicture("sable-antelope");
        using var library = Library();
        var style = ArtStyle.For(level);
        var counts = new Dictionary<PictureStage, (int Magenta, int Palette)>();
        foreach (var stage in Enum.GetValues<PictureStage>())
        {
            library.Forced = stage;
            using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
            using var frame = Settled(renderer, library, World(level, "sable antelope"));
            counts[stage] = (Count(frame, p => Near(p, Magenta)), Count(frame, p => Near(p, PaletteFill(level))));
        }

        Assert.Equal(0, counts[PictureStage.Shape].Magenta);
        Assert.Equal(0, counts[PictureStage.Silhouette].Magenta);
        Assert.True(counts[PictureStage.Silhouette].Palette > counts[PictureStage.Shape].Palette / 2, "the silhouette is filled with the thing's palette colour");
        if (style.PalettePictures)
            Assert.Equal(0, counts[PictureStage.Flat].Magenta);
        else
            Assert.True(counts[PictureStage.Flat].Magenta > 500, "a one-colour picture keeps its colour when flattened");
        if (style.MaxPictureStage == PictureStage.Photo)
            Assert.True(counts[PictureStage.Photo].Magenta > 500, "the photo stage draws the picture");
        else
            Assert.Equal(0, counts[PictureStage.Photo].Magenta);
    }

    [Fact]
    public void Flat_colours_at_the_youngest_levels_come_from_the_palette()
    {
        using (var bitmap = SamplePicture.Draw())
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var f = File.Create(Path.Combine(_dir, "sample.png"))) data.SaveTo(f);
        using var library = Library();
        library.Forced = PictureStage.Flat;
        var style = ArtStyle.Baby;
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        using var frame = Settled(renderer, library, World(CalmLevel.Baby, "sample"));
        var allowed = style.Colors.Select(c => Rgb(c.Fill)).Append(Rgb(style.Ink)).Append(SKColors.White).ToList();
        // Everything that is not sky or hills is in the palette, ink or white; edges blend, so a few pixels are not.
        using var empty = Render(renderer, new World { Style = style });
        var pixels = frame.Pixels;
        var backdrop = empty.Pixels;
        int body = 0, off = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == backdrop[i]) continue;
            body++;
            if (!allowed.Any(a => Near(pixels[i], a, 32))) off++;
        }
        Assert.True(body > 2000, $"the thing is drawn ({body} px)");
        // Edges blend neighbours (ink into yellow, ink into sky), so a share of the pixels is between colours.
        Assert.True(off < body / 5, $"{off} of {body} px are off the palette");
    }

    [Fact]
    public void A_thing_without_a_picture_draws_as_before()
    {
        using var library = Library();
        library.Forced = PictureStage.Photo;
        using var plain = new SceneRenderer(Fonts.LoadAndika());
        using var withLibrary = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        using var a = Render(plain, World(CalmLevel.Toddler, "zebra"));
        using var b = Render(withLibrary, World(CalmLevel.Toddler, "zebra"));
        Assert.Equal(a.Bytes, b.Bytes);
    }

    [Fact]
    public void A_thing_born_in_play_is_a_meeting_but_one_restored_or_seen_before_is_not()
    {
        using var library = Library();
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var world = new World { Style = ArtStyle.Toddler };
        world.Tick(500);
        // Restored from the last save a second after it was born: there already.
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.3, Y = 0.5, Born = world.Time - 1 });
        using (Render(renderer, world)) { }
        Assert.Equal(0, library.Meetings("sable antelope"));

        world.Tick(1);
        world.Add(new Entity { Id = 2, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.7, Y = 0.5, Born = world.Time });
        world.Tick(1);
        using (Render(renderer, world)) { }
        using (Render(renderer, world)) { }
        Assert.Equal(1, library.Meetings("sable antelope"));
    }

    [Fact]
    public void A_picture_whose_silhouette_is_a_lump_keeps_its_shape_at_the_silhouette_rung()
    {
        WritePicture("sable-antelope");
        WritePicture("octopus", lump: true);
        using var library = Library();
        library.Forced = PictureStage.Silhouette;
        // Asking starts the reads on the pool.
        _ = library.Has("sable antelope") | library.Has("octopus");
        library.WaitIdle(TimeSpan.FromSeconds(5));
        Assert.Equal(PictureStage.Silhouette, library.Stage(Thing("sable antelope"), ArtStyle.BigToddler));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("octopus"), ArtStyle.BigToddler));
        // The rungs either side are untouched: a lump's flat colours and photo still show.
        library.Forced = PictureStage.Flat;
        Assert.Equal(PictureStage.Flat, library.Stage(Thing("octopus"), ArtStyle.BigToddler));
    }

    [Fact]
    public void A_things_rung_is_settled_when_it_is_first_seen_and_a_promotion_shows_on_the_next_one_born()
    {
        WritePicture("sable-antelope");
        using var library = Library();
        for (var i = 0; i < PictureLibrary.MeetingsPerStage - 1; i++)
        {
            library.Meet("sable antelope");
            _now += PictureLibrary.MeetingGap;
        }
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var world = new World { Style = ArtStyle.BigToddler };
        world.Tick(10);
        using (Render(renderer, world)) { }
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.25, Y = 0.5, Born = world.Time });
        world.Tick(1);
        using (Settled(renderer, library, world)) { }
        // Its third meeting: it was born as its shape and stays so, though the count now says silhouette.
        Assert.Equal(PictureLibrary.MeetingsPerStage, library.Meetings("sable antelope"));
        Assert.Equal(PictureStage.Silhouette, library.Stage(Thing("sable antelope"), ArtStyle.BigToddler));
        using var one = Render(renderer, world);
        // A thing's colour fades a little as it settles, so matches are loose.
        var left = Count(one, p => Near(p, PaletteFill(CalmLevel.BigToddler), 48), new SKRectI(0, 0, 320, 360));
        Assert.True(left > 200, $"the first is drawn as its shape ({left} px)");
        Assert.Equal(0, Count(one, p => Near(p, Magenta, 48), new SKRectI(0, 0, 320, 360)));

        _now += PictureLibrary.MeetingGap;
        world.Tick(1);
        world.Add(new Entity { Id = 2, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.75, Y = 0.5, Born = world.Time });
        world.Tick(1);
        using var two = Settled(renderer, library, world);
        var rightPalette = Count(two, p => Near(p, PaletteFill(CalmLevel.BigToddler), 48), new SKRectI(320, 0, 640, 360));
        Assert.True(rightPalette > 300, $"the new one is a silhouette in the palette colour ({rightPalette} px)");
        Assert.Equal(0, Count(two, p => Near(p, Magenta, 48), new SKRectI(320, 0, 640, 360)));
        // The old one is unchanged: still drawn as the stock shape.
        Assert.InRange(Count(two, p => Near(p, PaletteFill(CalmLevel.BigToddler), 48), new SKRectI(0, 0, 320, 360)), left * 0.7, left * 1.3);
    }

    [Fact]
    public void A_pictured_thing_is_drawn_afresh_only_a_few_times_as_it_pops_in_grows_and_joins_the_history()
    {
        WritePicture("sable-antelope");
        using var library = Library();
        library.Forced = PictureStage.Photo;
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var world = new World { Style = ArtStyle.Toddler, Limits = CalmLimits.Toddler };
        var recipe = Thing("sable antelope");
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Thing, Recipe = recipe, X = 0.3, Y = 0.5, Born = world.Time, Scale = 1.2 });
        world.Add(new Entity { Id = world.NextId(), Role = EntityRole.Thing, Recipe = recipe, X = 0.7, Y = 0.5, Born = world.Time, Scale = 0.7 });
        // Ten minutes of play at 20 frames a second: the pop-in, the slow growth, two sizes awake at once.
        for (var frame = 0; frame < 600 * 20; frame++)
        {
            world.Tick(1 / 20.0);
            using (Render(renderer, world)) { }
            if (frame % 200 == 0) library.WaitIdle(TimeSpan.FromSeconds(10));
        }
        library.WaitIdle(TimeSpan.FromSeconds(10));
        using var frameAfter = Render(renderer, world);
        // Things settled long ago are drawn a little faded, so magenta-ish rather than magenta.
        Assert.True(Count(frameAfter, p => p.Red > 150 && p.Blue > 150 && p.Green < 110) > 500, "both are drawn from the picture");
        Assert.InRange(library.CompositeDraws, 1, 6);
    }

    [Fact]
    public void A_picture_replaced_while_its_drawing_is_on_its_way_is_dropped_and_the_new_one_drawn()
    {
        WritePicture("sable-antelope");
        using var library = Library();
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        var recipe = Thing("sable antelope");
        bool Paint() => library.Paint(canvas, recipe, PictureStage.Photo, 24, ArtStyle.Toddler, 64);
        // "Try another" lands during play: a drawing of the old picture is queued on the pool, the picture is let go
        // before the drawing starts, and the drawing is then thrown away rather than made from freed pixels or
        // kept by a picture nothing shows.
        for (var i = 0; i < 25; i++)
        {
            Paint();
            library.WaitIdle(TimeSpan.FromSeconds(10));
            Assert.False(Paint(), "the drawing is queued, not done");
            library.Invalidate("sable antelope");
            library.WaitIdle(TimeSpan.FromSeconds(10));
            Assert.False(library.Busy);
        }
        Paint();
        library.WaitIdle(TimeSpan.FromSeconds(10));
        Paint();
        library.WaitIdle(TimeSpan.FromSeconds(10));
        Assert.True(Paint(), "the picture read after the last replacement is drawn");
        Assert.True(Count(bitmap, p => Near(p, Magenta, 48)) > 50);
    }

    [Fact]
    public void A_grown_up_can_make_a_pick_climb_the_ladder_and_one_of_the_toys_own_things_real_at_once()
    {
        WritePicture("sable-antelope");
        WritePicture("zebra");
        using var library = Library();
        library.Climbs = name => name == "zebra";
        library.Choices = slug => slug switch
        {
            "sable-antelope" => new ThingChoices(ClimbChoice.Ladder, new RungChoices()),
            "zebra" => new ThingChoices(ClimbChoice.Real, new RungChoices()),
            _ => null,
        };
        var style = ArtStyle.For(CalmLevel.Baby);
        library.Stage(Thing("sable antelope"), style);
        library.Stage(Thing("zebra"), style);
        library.WaitIdle(TimeSpan.FromSeconds(10));

        Assert.True(library.Climbing("sable antelope"));
        Assert.False(library.Climbing("zebra"));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("sable antelope"), style));
        Assert.Equal(style.MaxPictureStage, library.Cap("sable antelope", style));
        Assert.Equal(PictureStage.Photo, library.Stage(Thing("zebra"), style));
    }

    [Fact]
    public void A_skipped_rung_is_passed_over_and_the_calm_levels_cap_still_holds()
    {
        WritePicture("sable-antelope");
        using var library = Library();
        library.Choices = _ => new ThingChoices(ClimbChoice.Auto, new RungChoices().With(PictureStage.Silhouette, RungChoice.Skip));
        var big = ArtStyle.For(CalmLevel.BigToddler);
        library.Stage(Thing("sable antelope"), big);
        library.WaitIdle(TimeSpan.FromSeconds(10));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("sable antelope"), big));

        for (var i = 0; i < PictureLibrary.MeetingsPerStage; i++)
        {
            Assert.True(library.Meet("sable antelope"));
            _now += PictureLibrary.MeetingGap + TimeSpan.FromMinutes(1);
        }

        Assert.Equal(PictureStage.Flat, library.Stage(Thing("sable antelope"), big));

        // Baby stops at flat colours: with those skipped too, a thing that has climbed all the way is its silhouette
        // there, and never its photo.
        library.Choices = _ => new ThingChoices(ClimbChoice.Auto, new RungChoices().With(PictureStage.Flat, RungChoice.Skip));
        library.Forced = PictureStage.Photo;
        var baby = ArtStyle.For(CalmLevel.Baby);
        Assert.Equal(PictureStage.Flat, baby.MaxPictureStage);
        Assert.Equal(PictureStage.Silhouette, library.Stage(Thing("sable antelope"), baby));
        Assert.Equal(PictureStage.Silhouette, library.Settled("sable antelope", PictureStage.Photo, baby));
    }

    [Fact]
    public void A_rung_set_to_the_drawn_shape_keeps_the_shape_whatever_pictures_there_are()
    {
        WritePicture("sable-antelope");
        using var library = Library();
        library.Choices = _ => new ThingChoices(ClimbChoice.Auto, new RungChoices().With(PictureStage.Flat, RungChoice.Shape));
        var style = ArtStyle.For(CalmLevel.BigToddler);
        library.Forced = PictureStage.Flat;
        library.Stage(Thing("sable antelope"), style);
        library.WaitIdle(TimeSpan.FromSeconds(10));

        Assert.Equal(PictureStage.Shape, library.Stage(Thing("sable antelope"), style));
        library.Forced = PictureStage.Photo;
        Assert.Equal(PictureStage.Photo, library.Stage(Thing("sable antelope"), style));
    }

    [Fact]
    public void A_rung_given_another_picture_is_drawn_from_that_one_and_from_the_one_in_play_when_it_is_gone()
    {
        var cyan = new SKColor(0x00, 0xFF, 0xFF);
        WritePicture("sable-antelope");
        WritePicture("2", colour: cyan, into: Path.Combine(_dir, "more", "sable-antelope"));
        using var library = Library();
        library.Forced = PictureStage.Photo;
        library.Choices = _ => new ThingChoices(ClimbChoice.Auto, new RungChoices().With(PictureStage.Photo, RungChoice.OfPicture(2)));
        var world = World(CalmLevel.BigToddler, "sable antelope");
        using (var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library })
        using (var chosen = Settled(renderer, library, world))
        {
            Assert.True(Count(chosen, p => Near(p, cyan)) > 500, "the photo rung is drawn from picture 2");
            Assert.Equal(0, Count(chosen, p => Near(p, Magenta)));
        }

        // A choice of a picture that is not there costs the choice, not the thing's picture.
        library.Choices = _ => new ThingChoices(ClimbChoice.Auto, new RungChoices().With(PictureStage.Photo, RungChoice.OfPicture(9)));
        library.Invalidate("sable antelope");
        using var fallbackRenderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        using var fallback = Settled(fallbackRenderer, library, world);
        Assert.True(Count(fallback, p => Near(p, Magenta)) > 500, "the picture in play stands in");
        Assert.Equal(0, Count(fallback, p => Near(p, cyan)));
    }

    [Fact]
    public void One_rung_can_be_drawn_by_itself_as_play_draws_it()
    {
        WritePicture("sable-antelope");
        WritePicture("lump", lump: true);
        var style = ArtStyle.For(CalmLevel.Toddler);
        var file = Path.Combine(_dir, "sable-antelope.png");

        var photo = PictureLibrary.RenderRung(file, Thing("sable antelope"), PictureStage.Photo, style, 128);
        var shape = PictureLibrary.RenderRung(file, Thing("sable antelope"), PictureStage.Shape, style, 128);
        var silhouette = PictureLibrary.RenderRung(file, Thing("sable antelope"), PictureStage.Silhouette, style, 128);
        var lump = PictureLibrary.RenderRung(Path.Combine(_dir, "lump.png"), Thing("lump"), PictureStage.Silhouette, style, 128);
        var missing = PictureLibrary.RenderRung(Path.Combine(_dir, "no-such.png"), Thing("zebra"), PictureStage.Photo, style, 128);
        using var photoPixels = SKBitmap.FromImage(photo.Image);
        using var shapePixels = SKBitmap.FromImage(shape.Image);
        using var silhouettePixels = SKBitmap.FromImage(silhouette.Image);

        Assert.Equal((128, 128), (photo.Image.Width, photo.Image.Height));
        Assert.Equal(PictureStage.Photo, photo.Drawn);
        Assert.True(Count(photoPixels, p => Near(p, Magenta)) > 300, "the photo rung shows the picture");
        Assert.Equal(PictureStage.Shape, shape.Drawn);
        Assert.Equal(0, Count(shapePixels, p => Near(p, Magenta)));
        Assert.True(Count(shapePixels, p => p.Alpha > 200) > 300, "the shape rung shows the drawn shape");
        Assert.Equal(PictureStage.Silhouette, silhouette.Drawn);
        Assert.Equal(0, Count(silhouettePixels, p => Near(p, Magenta)));
        Assert.True(Count(silhouettePixels, p => p.Alpha > 200 && Near(p, PaletteFill(CalmLevel.Toddler), 48)) > 200, "the silhouette is filled with the thing's colour");
        Assert.Equal(PictureStage.Shape, lump.Drawn);
        Assert.Equal(PictureStage.Shape, missing.Drawn);
        foreach (var render in new[] { photo, shape, silhouette, lump, missing }) render.Image.Dispose();
    }

    private PictureLibrary Library() => new(_dir, () => _now);

    private static Recipe Thing(string name) => BuiltInRecipes.Animals.Cat with { Name = name };

    private static World World(CalmLevel level, string name)
    {
        var world = new World { Style = ArtStyle.For(level) };
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = Thing(name), X = 0.5, Y = 0.5, Born = -60 });
        return world;
    }

    private static SKColor Rgb(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static SKColor PaletteFill(CalmLevel level) => Rgb(ArtStyle.For(level).Snap(Thing("x").Body.Hue).Fill);

    /// <summary>A magenta blob with a transparent margin, as a cut-out would be.</summary>
    /// <summary>Writes a magenta cut-out: by default an animal standing side-on (body, four legs, a neck and head), whose
    /// silhouette is clear; a lump is a plain oval, which the silhouette rung skips.</summary>
    private void WritePicture(string slug, string extension = ".png", bool lump = false, SKColor? colour = null, string? into = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(400, 240, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true, Color = colour ?? Magenta };
            if (lump) c.DrawOval(new SKRect(40, 40, 360, 200), paint);
            else
            {
                c.DrawOval(new SKRect(60, 70, 300, 160), paint);
                foreach (var x in new[] { 80f, 120f, 230f, 270f }) c.DrawRect(new SKRect(x, 130, x + 22, 230), paint);
                c.DrawRect(new SKRect(270, 30, 300, 110), paint);
                c.DrawOval(new SKRect(270, 10, 360, 50), paint);
            }
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(into ?? _dir);
        using var f = File.Create(Path.Combine(into ?? _dir, slug + extension));
        data.SaveTo(f);
    }

    private static SKBitmap Render(SceneRenderer renderer, World world)
    {
        var bitmap = new SKBitmap(new SKImageInfo(640, 360, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, 640, 360, world);
        return bitmap;
    }

    /// <summary>A frame drawn once the pictures it needs are read and drawn on the pool.</summary>
    private static SKBitmap Settled(SceneRenderer renderer, PictureLibrary library, World world)
    {
        for (var i = 0; i < 3; i++)
        {
            using (Render(renderer, world)) { }
            library.WaitIdle(TimeSpan.FromSeconds(10));
        }
        return Render(renderer, world);
    }

    private static int Count(SKBitmap bitmap, Func<SKColor, bool> match, SKRectI? within = null)
    {
        var area = within ?? new SKRectI(0, 0, bitmap.Width, bitmap.Height);
        var n = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
                if (match(bitmap.GetPixel(x, y))) n++;
        return n;
    }

    private static bool Near(SKColor a, SKColor b, int tolerance = 12) =>
        Math.Abs(a.Red - b.Red) < tolerance && Math.Abs(a.Green - b.Green) < tolerance && Math.Abs(a.Blue - b.Blue) < tolerance;
}
