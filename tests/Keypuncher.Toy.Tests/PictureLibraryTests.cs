using Keypuncher.Core;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Toy.Tests;

/// <summary>The realism ladder: a thing with a picture climbs from its stock shape to its picture as it is met,
/// one without keeps its shape, and the meetings are kept between sessions.</summary>
public sealed class PictureLibraryTests : IDisposable
{
    /// <summary>A colour no palette or landscape uses, so any of it on screen came from the picture.</summary>
    private static readonly SKColor Magenta = new(0xFF, 0x00, 0xFF);
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-picture-tests").FullName;

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
    public void Meetings_are_counted_only_for_things_with_a_picture_and_kept()
    {
        WritePicture("sable-antelope");
        using (var library = new PictureLibrary(_dir))
        {
            library.Meet("sable antelope");
            library.Meet("Sable Antelope");
            library.Meet("zebra");
            Assert.Equal(2, library.Meetings("sable antelope"));
            Assert.Equal(0, library.Meetings("zebra"));
        }
        using var again = new PictureLibrary(_dir);
        Assert.Equal(2, again.Meetings("sable antelope"));
    }

    [Fact]
    public void A_broken_file_is_ignored_and_the_thing_keeps_its_shape()
    {
        File.WriteAllText(Path.Combine(_dir, "zebra.png"), "not a picture");
        using var library = new PictureLibrary(_dir) { Forced = PictureStage.Photo };
        Assert.False(library.Has("zebra"));
        Assert.Equal(PictureStage.Shape, library.Stage(Thing("zebra")));
    }

    [Theory]
    [InlineData(CalmLevel.Baby)]
    [InlineData(CalmLevel.Toddler)]
    [InlineData(CalmLevel.BigToddler)]
    public void Only_the_photo_stage_shows_the_picture_itself(CalmLevel level)
    {
        WritePicture("sable-antelope");
        using var library = new PictureLibrary(_dir);
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var counts = new Dictionary<PictureStage, (int Magenta, int Palette)>();
        foreach (var stage in Enum.GetValues<PictureStage>())
        {
            library.Forced = stage;
            using var frame = Render(renderer, World(level, "sable antelope"));
            counts[stage] = (Count(frame, p => Near(p, Magenta)), Count(frame, p => Near(p, PaletteFill(level))));
        }

        Assert.Equal(0, counts[PictureStage.Shape].Magenta);
        Assert.Equal(0, counts[PictureStage.Silhouette].Magenta);
        Assert.True(counts[PictureStage.Silhouette].Palette > counts[PictureStage.Shape].Palette / 2, "the silhouette is filled with the thing's palette colour");
        Assert.True(counts[PictureStage.Flat].Magenta > 500, "a one-colour picture keeps its colour when flattened");
        Assert.True(counts[PictureStage.Photo].Magenta > 500, "the photo stage draws the picture");
    }

    [Fact]
    public void A_thing_without_a_picture_draws_as_before()
    {
        using var library = new PictureLibrary(_dir) { Forced = PictureStage.Photo };
        using var plain = new SceneRenderer(Fonts.LoadAndika());
        using var withLibrary = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        using var a = Render(plain, World(CalmLevel.Toddler, "zebra"));
        using var b = Render(withLibrary, World(CalmLevel.Toddler, "zebra"));
        Assert.Equal(a.Bytes, b.Bytes);
    }

    [Fact]
    public void Showing_a_new_thing_counts_as_meeting_it_but_one_already_there_does_not()
    {
        WritePicture("sable-antelope");
        using var library = new PictureLibrary(_dir);
        using var renderer = new SceneRenderer(Fonts.LoadAndika()) { Pictures = library };
        var world = new World { Style = ArtStyle.Toddler };
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.3, Y = 0.5, Born = -60 });
        using (Render(renderer, world)) { }
        Assert.Equal(0, library.Meetings("sable antelope"));

        world.Add(new Entity { Id = 2, Role = EntityRole.Thing, Recipe = Thing("sable antelope"), X = 0.7, Y = 0.5, Born = world.Time });
        using (Render(renderer, world)) { }
        using (Render(renderer, world)) { }
        Assert.Equal(1, library.Meetings("sable antelope"));
    }

    private static Recipe Thing(string name) => BuiltInRecipes.Animals.Cat with { Name = name };

    private static World World(CalmLevel level, string name)
    {
        var world = new World { Style = ArtStyle.For(level) };
        world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = Thing(name), X = 0.5, Y = 0.5, Born = -60 });
        return world;
    }

    private static SKColor PaletteFill(CalmLevel level)
    {
        var rgb = ArtStyle.For(level).Snap(Thing("x").Body.Hue).Fill;
        return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    /// <summary>A magenta blob with a transparent margin, as a cut-out would be.</summary>
    private void WritePicture(string slug)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(200, 120, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bitmap))
        {
            c.Clear(SKColors.Transparent);
            using var paint = new SKPaint { IsAntialias = true, Color = Magenta };
            c.DrawOval(new SKRect(20, 20, 180, 100), paint);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var f = File.Create(Path.Combine(_dir, slug + ".png"));
        data.SaveTo(f);
    }

    private static SKBitmap Render(SceneRenderer renderer, World world)
    {
        var bitmap = new SKBitmap(new SKImageInfo(640, 360, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, 640, 360, world);
        return bitmap;
    }

    private static int Count(SKBitmap bitmap, Func<SKColor, bool> match)
    {
        var n = 0;
        foreach (var p in bitmap.Pixels) if (match(p)) n++;
        return n;
    }

    private static bool Near(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) < 12 && Math.Abs(a.Green - b.Green) < 12 && Math.Abs(a.Blue - b.Blue) < 12;
}
