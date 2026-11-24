namespace Keypuncher.Core;

/// <summary>A box in a camera picture, in shares of its width and height (0 to 1).</summary>
public readonly record struct ViewBox(double X, double Y, double W, double H)
{
    public static readonly ViewBox Whole = new(0, 0, 1, 1);

    /// <summary>Where the child's body is likely to be around this face: its width again to each side, half
    /// its height above it and two and a half below, for a child leaning over the keyboard shows the camera the top
    /// of its head and its arms, not its face.</summary>
    public ViewBox AroundFace()
    {
        var x0 = Math.Max(0, X - W);
        var x1 = Math.Min(1, X + 2 * W);
        var y0 = Math.Max(0, Y - H / 2);
        var y1 = Math.Min(1, Y + 3.5 * H);
        return new ViewBox(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>
/// Decides from what the camera reports, a couple of times a second, whether the child is in view. Windows' face
/// detector only finds a face turned to the camera, and a child pressing keys mostly looks down at them, so a face
/// alone is not enough: once a face has been seen, movement where the child was keeps the child in view. The child
/// is out of view only after <see cref="QuietFor"/> seconds with neither a face nor movement there, or after
/// <see cref="MovementCarries"/> seconds of movement without a face. Once out, a face brings the child back, or
/// <see cref="MovingLooksToReturn"/> looks in a row with movement where the child was (while still within
/// <see cref="MovementCarries"/> of the last face); a warning, once shown, therefore lasts at least a look or two, and
/// in view lasts at least <see cref="QuietFor"/>, so the warning cannot flicker. Pure: fed with times and observations.
/// </summary>
public sealed class ChildInView
{
    /// <summary>Seconds with neither a face nor movement where the child was before the child counts as gone.</summary>
    public const double QuietFor = 10;
    /// <summary>Movement alone keeps the child in view for at most this long after the last face.</summary>
    public const double MovementCarries = 90;
    /// <summary>The share of the child's area that must have changed since the last look to count as movement.</summary>
    public const double MovingShare = 0.04;
    /// <summary>Looks in a row with movement that bring a child counted out of view back, while within
    /// <see cref="MovementCarries"/> of the last face; one stray change is not enough.</summary>
    public const int MovingLooksToReturn = 2;

    private double _lastFace, _lastSign;
    private int _movingLooks;

    /// <param name="start">When looking starts; the child gets <see cref="QuietFor"/> seconds from then to be seen,
    /// and until a face is seen, movement anywhere in the picture counts.</param>
    public ChildInView(double start) => _lastFace = _lastSign = start;

    /// <summary>Whether the child is in view after the last look.</summary>
    public bool InView { get; private set; } = true;

    /// <summary>Where movement counts as the child's: around the last face seen, or the whole picture before one.</summary>
    public ViewBox Area { get; private set; } = ViewBox.Whole;

    /// <summary>The face box seen at the last look, if any.</summary>
    public ViewBox? LastFace { get; private set; }

    /// <summary>Takes one look's findings.</summary>
    /// <param name="now">Seconds, on the same clock as the start.</param>
    /// <param name="face">The largest face found, or null.</param>
    /// <param name="moved">The share of <see cref="Area"/> (as it was before this look) that changed since the last
    /// look, or null when it could not be measured.</param>
    /// <returns>Whether <see cref="InView"/> changed.</returns>
    public bool Look(double now, ViewBox? face, double? moved)
    {
        var was = InView;
        LastFace = face;
        if (face is { } f)
        {
            _lastFace = _lastSign = now;
            _movingLooks = 0;
            Area = f.AroundFace();
            InView = true;
            return InView != was;
        }
        var moving = moved >= MovingShare && now - _lastFace < MovementCarries;
        _movingLooks = moving ? _movingLooks + 1 : 0;
        if (InView)
        {
            if (moving) _lastSign = now;
            if (now - _lastSign >= QuietFor || now - _lastFace >= MovementCarries) InView = false;
        }
        else if (_movingLooks >= MovingLooksToReturn)
        {
            _lastSign = now;
            InView = true;
        }
        return InView != was;
    }
}

/// <summary>A coarse grid of a grey picture's brightness, for telling whether part of the picture moved between two
/// looks without being fooled by the camera's pixel noise.</summary>
public static class MotionGrid
{
    public const int Columns = 40, Rows = 30;
    /// <summary>A cell's mean brightness must change by more than this (of 255) to count as changed.</summary>
    public const int Threshold = 8;

    /// <summary>A picture whose mean brightness (of 255) is below this is too dark to find a child in: the camera's
    /// picture was all but black in the 06:05 and 06:34 plays of late November, before the lights were on, and the
    /// toy said it could not see the child when it could not see anything.</summary>
    public const int DarkMean = 30;

    /// <summary>Whether a grid from <see cref="Cells"/> is too dark to find a child in.</summary>
    public static bool TooDark(byte[] cells) => cells.Length > 0 && cells.Average(c => (double)c) < DarkMean;

    /// <summary>Mean brightness of each of <see cref="Columns"/>×<see cref="Rows"/> cells, row by row.</summary>
    public static byte[] Cells(ReadOnlySpan<byte> gray, int width, int height, int stride = 0)
    {
        if (stride == 0) stride = width;
        var sums = new int[Columns * Rows];
        var counts = new int[Columns * Rows];
        for (var y = 0; y < height; y++)
        {
            var row = gray.Slice(y * stride, width);
            var cy = y * Rows / height * Columns;
            for (var x = 0; x < width; x++)
            {
                var c = cy + x * Columns / width;
                sums[c] += row[x];
                counts[c]++;
            }
        }
        var cells = new byte[sums.Length];
        for (var i = 0; i < cells.Length; i++) cells[i] = (byte)(counts[i] == 0 ? 0 : sums[i] / counts[i]);
        return cells;
    }

    /// <summary>The share of the cells inside <paramref name="area"/> whose brightness changed by more than
    /// <see cref="Threshold"/> between two grids from <see cref="Cells"/>.</summary>
    public static double ChangedShare(byte[] before, byte[] after, ViewBox area)
    {
        var x0 = Math.Clamp((int)Math.Floor(area.X * Columns), 0, Columns - 1);
        var x1 = Math.Clamp((int)Math.Ceiling((area.X + area.W) * Columns), x0 + 1, Columns);
        var y0 = Math.Clamp((int)Math.Floor(area.Y * Rows), 0, Rows - 1);
        var y1 = Math.Clamp((int)Math.Ceiling((area.Y + area.H) * Rows), y0 + 1, Rows);
        int changed = 0, all = 0;
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
            {
                all++;
                if (Math.Abs(before[y * Columns + x] - after[y * Columns + x]) > Threshold) changed++;
            }
        return all == 0 ? 0 : changed / (double)all;
    }
}
