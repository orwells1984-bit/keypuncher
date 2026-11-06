namespace Keypuncher.Core;

/// <summary>
/// How things are drawn at one <see cref="CalmLevel"/>: how big, how thick the outline, which colours.
/// Infant acuity and contrast sensitivity are a fraction of an adult's, and colour only registers when
/// it is saturated, so the younger the child the bigger, bolder and plainer the shapes. See the
/// Keypuncher Art Rules page. Lengths are fractions of the shorter screen side ("unit"); from a lap in
/// front of a 27-inch screen one degree of view is about 0.03 unit.
/// </summary>
/// <param name="SizeScale">Multiplies every recipe's size.</param>
/// <param name="MinDiameter">A thing's body is never smaller than this across.</param>
/// <param name="MaxDiameter">A thing's body is never bigger than this across, so a word's big picture still fits the screen.</param>
/// <param name="MinStroke">Outlines, stems and letter edges are at least this wide, and nothing finer is drawn at all.</param>
/// <param name="StrokeOfRadius">Outline width as a share of the body radius, when that is more than <see cref="MinStroke"/>.</param>
/// <param name="Ink">The eyes: each pupil, the ring round each white and the blink line, the darkest thing drawn.</param>
/// <param name="Stem">Plant stems.</param>
/// <param name="Colors">Every recipe hue snaps to the nearest of these, so the model's choice still varies things but can't camouflage one against the sky or hills.</param>
/// <param name="Highlight">How far towards white the top-left highlight on a blob goes (0 for flat).</param>
/// <param name="EyeScale">Multiplies eye size and spacing, so a baby's creature has big, face-like eyes.</param>
/// <param name="MinPupil">A pupil is at least this across.</param>
/// <param name="MinEyes">A creature with any eyes gets at least this many, because babies attend to faces; one without stays faceless.</param>
/// <param name="MaxEyes">Cap on eyes per creature.</param>
/// <param name="MaxLobes">Cap on petals, star points, cloud bumps and blob wobbles.</param>
/// <param name="MaxStripes">Stripes on a striped body; 0 leaves it plain.</param>
/// <param name="MaxDots">Dots in a dot field, fewer and bigger for the youngest.</param>
/// <param name="MultiHueDots">Whether a dot field's dots may each take a different palette colour.</param>
/// <param name="LetterHeight">A letter's height as a share of the screen height.</param>
/// <param name="Ground">Sky top, sky bottom, far hill, near hill: a quiet backdrop close in tone to itself, so the things are what stands out.</param>
/// <param name="MaxPictureStage">How real a thing with a picture may get at this level: the youngest stop at flat colours, which keep the outline and the palette, rather than the photo.</param>
/// <param name="PalettePictures">Whether a picture's flat colours snap to this palette (plus ink and white), so the level's few colours hold for pictures too.</param>
public sealed record ArtStyle(
    double SizeScale,
    double MinDiameter,
    double MaxDiameter,
    double MinStroke,
    double StrokeOfRadius,
    uint Ink,
    uint Stem,
    IReadOnlyList<ArtColor> Colors,
    double Highlight,
    double EyeScale,
    double MinPupil,
    int MinEyes,
    int MaxEyes,
    int MaxLobes,
    int MaxStripes,
    int MaxDots,
    bool MultiHueDots,
    double LetterHeight,
    Ground Ground,
    PictureStage MaxPictureStage = PictureStage.Photo,
    bool PalettePictures = false)
{
    /// <summary>The five colour categories 4-month-olds already sort by, flat and fully saturated,
    /// every one drawn in the same near-black ink: 9:1 or better against the ground.</summary>
    private static readonly ArtColor[] BabyColors =
    [
        new(0xE53935, 0x1E2240), // red
        new(0xFFC400, 0x1E2240), // yellow
        new(0x1FA34A, 0x1E2240), // green
        new(0x1E6FD9, 0x1E2240), // blue
        new(0x8E3FD0, 0x1E2240), // purple
    ];

    /// <summary>The five plus orange, pink, teal and brown, so there is more to tell apart.</summary>
    private static readonly ArtColor[] ToddlerColors =
    [
        new(0xE53935, 0x262A4A), // red
        new(0xFF7A00, 0x262A4A), // orange
        new(0xFFC400, 0x262A4A), // yellow
        new(0x1FA34A, 0x262A4A), // green
        new(0x00A3A3, 0x262A4A), // teal
        new(0x1E6FD9, 0x262A4A), // blue
        new(0x8E3FD0, 0x262A4A), // purple
        new(0xF0508C, 0x262A4A), // pink
        new(0x8D5A3B, 0x262A4A), // brown
    ];

    /// <summary>The same nine, each outlined in a dark shade of itself that still reads 4.5:1 or better
    /// against the ground.</summary>
    private static readonly ArtColor[] BigToddlerColors =
    [
        new(0xE53935, 0x661A17), // red, its outline kept short of a saturated red
        new(0xFF7A00, 0x733700), // orange
        new(0xFFC400, 0x5C4700), // yellow
        new(0x1FA34A, 0x03541E), // green
        new(0x00A3A3, 0x005252), // teal
        new(0x1E6FD9, 0x003273), // blue
        new(0x8E3FD0, 0x471273), // purple
        new(0xF0508C, 0x731538), // pink
        new(0x8D5A3B, 0x6E3C1D), // brown
    ];

    /// <summary>About 6 to 12 months: sees roughly 20/120 to 20/60, so everything is big (6 degrees of view at the
    /// least), thick-edged (0.6 degrees) and plain, with pupils a degree across.</summary>
    public static readonly ArtStyle Baby = new(
        SizeScale: 2.2, MinDiameter: 0.18, MaxDiameter: 0.40, MinStroke: 0.018, StrokeOfRadius: 0.12,
        Ink: 0x1E2240, Stem: 0x1E2240, Colors: BabyColors, Highlight: 0.15,
        EyeScale: 2.0, MinPupil: 0.03, MinEyes: 2, MaxEyes: 2,
        MaxLobes: 5, MaxStripes: 0, MaxDots: 4, MultiHueDots: false, LetterHeight: 0.30,
        Ground: new(0xDCE9F2, 0xF5EFE3, 0xC9DCC4, 0xB7D1B0),
        MaxPictureStage: PictureStage.Flat, PalettePictures: true);

    /// <summary>About 12 to 18 months: medium detail works, still with a dark outline on everything.</summary>
    public static readonly ArtStyle Toddler = new(
        SizeScale: 1.5, MinDiameter: 0.12, MaxDiameter: 0.36, MinStroke: 0.0105, StrokeOfRadius: 0.09,
        Ink: 0x262A4A, Stem: 0x2E6B34, Colors: ToddlerColors, Highlight: 0.30,
        EyeScale: 1.4, MinPupil: 0.018, MinEyes: 0, MaxEyes: 2,
        MaxLobes: 6, MaxStripes: 3, MaxDots: 6, MultiHueDots: false, LetterHeight: 0.26,
        Ground: new(0xC9E6FA, 0xFBF1DD, 0xB5DBAC, 0x9DCF95),
        MaxPictureStage: PictureStage.Photo, PalettePictures: true);

    /// <summary>18 months and up: near-adult sight for coarse shapes; the Phase 1 sizes and sky, bolder colours.</summary>
    public static readonly ArtStyle BigToddler = new(
        SizeScale: 1.0, MinDiameter: 0.075, MaxDiameter: 0.36, MinStroke: 0.006, StrokeOfRadius: 0.07,
        Ink: 0x262A4A, Stem: 0x2E6B34, Colors: BigToddlerColors, Highlight: 0.45,
        EyeScale: 1.0, MinPupil: 0.012, MinEyes: 0, MaxEyes: 4,
        MaxLobes: 8, MaxStripes: 6, MaxDots: 12, MultiHueDots: true, LetterHeight: 0.26,
        Ground: new(0xB8E2FF, 0xFFF2D8, 0xA8DC9F, 0x86CB80));

    public static ArtStyle For(CalmLevel level) => level switch
    {
        CalmLevel.Baby => Baby,
        CalmLevel.BigToddler => BigToddler,
        _ => Toddler,
    };

    /// <summary>The palette colour nearest to a recipe's hue.</summary>
    public ArtColor Snap(int hue) => Colors[SnapIndex(hue)];

    /// <summary>A second palette colour for a thing's smaller parts (a flower's centre), never the body's own.</summary>
    public ArtColor Companion(int hue)
    {
        var body = SnapIndex(hue);
        var next = SnapIndex(hue + 60);
        return Colors[next == body ? (body + 1) % Colors.Count : next];
    }

    private int SnapIndex(int hue)
    {
        var h = ((hue % 360) + 360) % 360;
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < Colors.Count; i++)
        {
            var d = Math.Abs(Colors[i].Hue - h);
            d = Math.Min(d, 360 - d);
            if (d < bestDistance) (best, bestDistance) = (i, d);
        }
        return best;
    }
}

/// <summary>A fill and the outline drawn around it, as 0xRRGGBB.</summary>
public sealed record ArtColor(uint Fill, uint Outline)
{
    /// <summary>The fill's hue in degrees, for snapping a recipe's hue to the palette.</summary>
    public double Hue => HueOf(Fill);

    private static double HueOf(uint rgb)
    {
        double r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var chroma = max - min;
        if (chroma == 0) return 0;
        var h = max == r ? ((g - b) / chroma) % 6 : max == g ? ((b - r) / chroma) + 2 : ((r - g) / chroma) + 4;
        return ((h * 60) + 360) % 360;
    }
}

/// <summary>The backdrop's four colours, as 0xRRGGBB.</summary>
public sealed record Ground(uint SkyTop, uint SkyBottom, uint FarHill, uint NearHill);
