namespace Keypuncher.Rendering;

/// <summary>
/// How much a cut-out's outline says about what it is. A silhouette names a thing by its legs, ears, horns, fins or
/// petals sticking out; a cut-out that is nearly a filled oval (a curled flamingo, a close-up of a bear's head, one
/// arm of an octopus, two mice side by side) gives a lump that could be anything, and a red lump read as a kidney.
/// Measured once when a picture is read: the share of its convex hull the thing fills.
/// </summary>
public static class Outline
{
    /// <summary>At or above this share of its hull a cut-out is a lump. Set from the 39 pictures on the family desktop
    /// in Nov 2026: the lumps (three dogs in a heap 0.79, octopus arm 0.81, rabbit 0.83, seal, mouse, jellyfish, tulip,
    /// bear's head, curled flamingo, mango up to 1.00) all fill 0.79 or more; every clear silhouette (antelopes,
    /// dinosaurs, cow, horse, elephant, zebra, starfish) fills 0.75 or less.</summary>
    public const double LumpSolidity = 0.77;

    /// <summary>A cut-out narrower than this on its short side is too coarse for its outline to carry detail.</summary>
    public const int MinSide = 160;

    /// <summary>Whether a cut-out's silhouette is worth showing: big enough, and not a lump.</summary>
    public static bool IsClear(int width, int height, double solidity) => Math.Min(width, height) >= MinSide && solidity < LumpSolidity;

    /// <summary>The share of its convex hull an RGBA picture's opaque part (alpha over half) fills, 0 to 1; 0 when
    /// nothing is opaque. Each row's leftmost and rightmost opaque pixels hold every hull corner, so only those are kept.</summary>
    public static double Solidity(ReadOnlySpan<byte> rgba, int width, int height, int rowBytes)
    {
        var corners = new List<(long X, long Y)>(height * 4);
        long area = 0;
        for (var y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * rowBytes, width * 4);
            int left = -1, right = -1;
            for (var x = 0; x < width; x++)
            {
                if (row[(x * 4) + 3] <= 127) continue;
                area++;
                if (left < 0) left = x;
                right = x;
            }
            if (left < 0) continue;
            corners.Add((left, y));
            corners.Add((left, y + 1));
            corners.Add((right + 1, y));
            corners.Add((right + 1, y + 1));
        }
        if (area == 0) return 0;
        var hull = HullArea(corners);
        return hull <= 0 ? 1 : Math.Min(1, area / hull);
    }

    /// <summary>The area of the convex hull round the points (Andrew's monotone chain).</summary>
    private static double HullArea(List<(long X, long Y)> points)
    {
        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hull = new (long X, long Y)[points.Count * 2];
        var k = 0;
        static long Cross((long X, long Y) o, (long X, long Y) a, (long X, long Y) b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
        foreach (var p in points)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], p) <= 0) k--;
            hull[k++] = p;
        }
        for (int i = points.Count - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], points[i]) <= 0) k--;
            hull[k++] = points[i];
        }
        long twice = 0;
        for (var i = 0; i < k - 1; i++) twice += (hull[i].X * hull[i + 1].Y) - (hull[i + 1].X * hull[i].Y);
        return Math.Abs(twice) / 2.0;
    }
}
