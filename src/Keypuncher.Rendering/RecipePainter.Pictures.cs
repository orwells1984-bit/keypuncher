using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Pictures of particular things that are not animals: the apple, the moon, a house and a boat. Until these
/// existed the starter words borrowed an abstract shape (the apple was a red blob on a stem, the moon a
/// yellow cloud), which read as a lollipop and a bone. Drawn like the animals: a few big rounded parts,
/// filled and outlined, with the one or two features that name the thing kept at every level. Each fits
/// the same box as the other shapes, about one radius each way, resting on its bottom edge.
/// </summary>
internal static partial class RecipePainter
{
    private static void Picture(SKCanvas c, Shape shape, float r, int hue, double t, Brush b)
    {
        using var k = new Kit(c, r, hue, t, b);
        switch (shape)
        {
            case Shape.Apple: Apple(k); break;
            case Shape.Moon: Moon(k); break;
            case Shape.House: House(k); break;
            case Shape.Boat: Boat(k); break;
            case Shape.Sun: Sun(k); break;
            case Shape.Ball: Ball(k); break;
            case Shape.Egg: Egg(k); break;
            case Shape.Car: Car(k); break;
        }
    }

    /// <summary>A round body with a dip at the top, a short dark stalk out of the dip, a leaf, and a shine.</summary>
    private static void Apple(Kit k)
    {
        using var builder = new SKPathBuilder();
        var r = k.R;
        builder.MoveTo(0, 0.95f * r);
        builder.CubicTo(-0.75f * r, 0.95f * r, -1.0f * r, 0.35f * r, -0.95f * r, -0.2f * r);
        builder.CubicTo(-0.9f * r, -0.75f * r, -0.4f * r, -0.95f * r, 0, -0.55f * r);
        builder.CubicTo(0.4f * r, -0.95f * r, 0.9f * r, -0.75f * r, 0.95f * r, -0.2f * r);
        builder.CubicTo(1.0f * r, 0.35f * r, 0.75f * r, 0.95f * r, 0, 0.95f * r);
        builder.Close();
        using var body = builder.Detach();

        // The stalk and leaf go on first, so the body's outline sits over their roots.
        k.Ribbon(k.Dark, 0.1f, (0, -0.5f), (0.04f, -0.78f), (0.1f, -0.98f));
        k.Oval(k.Leaf, 0.36f, -0.72f, 0.3f, 0.13f, -30);
        k.Fill(k.Body, body);
        k.Line(body);
        // A shine at the top left, with no outline, so it reads as a polished apple and not an eye.
        k.Dot(k.Pale, -0.42f, -0.3f, 0.11f);
    }

    /// <summary>A crescent: a disc with a smaller one taken out of its upper right, opening to the right.</summary>
    private static void Moon(Kit k)
    {
        using var disc = new SKPathBuilder();
        using var bite = new SKPathBuilder();
        disc.AddCircle(0, 0, 0.95f * k.R, SKPathDirection.Clockwise);
        bite.AddCircle(0.5f * k.R, -0.2f * k.R, 0.78f * k.R, SKPathDirection.Clockwise);
        using var whole = disc.Detach();
        using var cut = bite.Detach();
        using var crescent = whole.Op(cut, SKPathOp.Difference) ?? whole;
        k.Fill(k.Body, crescent);
        k.Line(crescent);
    }

    /// <summary>A box with a pointed roof in a second colour, a door, and for older eyes a window and chimney.</summary>
    private static void House(Kit k)
    {
        if (k.Fine) k.Rounded(k.Dark, 0.04f, (0.45f, -0.95f), (0.7f, -0.95f), (0.7f, -0.45f), (0.45f, -0.6f));
        k.Rounded(k.Body, 0.08f, (-0.75f, -0.15f), (0.75f, -0.15f), (0.75f, 0.95f), (-0.75f, 0.95f));
        k.Rounded(k.Second, 0.1f, (-1.0f, -0.1f), (0, -0.98f), (1.0f, -0.1f));
        k.Rounded(k.Dark, 0.1f, (-0.2f, 0.95f), (-0.2f, 0.4f), (0.2f, 0.4f), (0.2f, 0.95f));
        if (k.Fine) k.Rounded(k.Pale, 0.04f, (0.3f, 0.1f), (0.6f, 0.1f), (0.6f, 0.4f), (0.3f, 0.4f));
    }

    /// <summary>A hull that narrows towards the bottom, a mast and a pale triangular sail.</summary>
    private static void Boat(Kit k)
    {
        k.Ribbon(k.Dark, 0.08f, (0, 0.35f), (0, -0.98f));
        k.Rounded(k.Pale, 0.08f, (0.07f, -0.92f), (0.07f, 0.25f), (0.8f, 0.25f));
        k.Rounded(k.Body, 0.12f, (-1.0f, 0.35f), (1.0f, 0.35f), (0.7f, 0.9f), (-0.7f, 0.9f));
    }

    /// <summary>A disc ringed by short round-ended rays, standing apart from it: a star's points join its body, a
    /// sun's rays do not, which is what tells the two apart at a glance.</summary>
    private static void Sun(Kit k)
    {
        var rays = k.Fine ? 10 : 8;
        for (var i = 0; i < rays; i++)
        {
            var a = (i * MathF.PI * 2 / rays);
            var (cos, sin) = (MathF.Cos(a), MathF.Sin(a));
            k.Ribbon(k.Body, 0.1f, (0.7f * cos, 0.7f * sin), (0.9f * cos, 0.9f * sin));
        }
        k.Circle(k.Body, 0, 0, 0.52f);
    }

    /// <summary>A round ball with a pale band round its middle and a second colour beside it, like a beach ball.</summary>
    private static void Ball(Kit k)
    {
        using var ball = k.OvalPath(0, 0.05f, 0.85f, 0.85f);
        k.Fill(k.Body, ball);
        k.Inside(ball, () =>
        {
            using var band = new SKPathBuilder();
            var r = k.R;
            band.MoveTo(-0.3f * r, -0.9f * r);
            band.CubicTo(0.05f * r, -0.4f * r, 0.05f * r, 0.5f * r, -0.3f * r, 1.0f * r);
            band.LineTo(0.3f * r, 1.0f * r);
            band.CubicTo(0.65f * r, 0.5f * r, 0.65f * r, -0.4f * r, 0.3f * r, -0.9f * r);
            band.Close();
            using var path = band.Detach();
            k.Fill(k.Pale, path);
            k.Line(path);
            if (k.Fine)
            {
                using var side = new SKPathBuilder();
                side.MoveTo(0.3f * r, -0.9f * r);
                side.CubicTo(0.65f * r, -0.4f * r, 0.65f * r, 0.5f * r, 0.3f * r, 1.0f * r);
                side.LineTo(1.0f * r, 1.0f * r);
                side.LineTo(1.0f * r, -0.9f * r);
                side.Close();
                using var rest = side.Detach();
                k.Fill(k.Second, rest);
                k.Line(rest);
            }
        });
        k.Line(ball);
        k.Dot(k.Pale, -0.45f, -0.35f, 0.1f);
    }

    /// <summary>An egg: white whatever its hue, as eggs are, rounder at the bottom than the top.</summary>
    private static void Egg(Kit k)
    {
        using var builder = new SKPathBuilder();
        var r = k.R;
        builder.MoveTo(0, -0.95f * r);
        builder.CubicTo(0.45f * r, -0.95f * r, 0.72f * r, 0.0f * r, 0.72f * r, 0.35f * r);
        builder.CubicTo(0.72f * r, 0.75f * r, 0.4f * r, 0.95f * r, 0, 0.95f * r);
        builder.CubicTo(-0.4f * r, 0.95f * r, -0.72f * r, 0.75f * r, -0.72f * r, 0.35f * r);
        builder.CubicTo(-0.72f * r, 0.0f * r, -0.45f * r, -0.95f * r, 0, -0.95f * r);
        builder.Close();
        using var egg = builder.Detach();
        k.Fill(k.Pale, egg);
        k.Line(egg);
    }

    /// <summary>A car from the side: a long low body, a cabin with pale windows, and two dark wheels with pale hubs.</summary>
    private static void Car(Kit k)
    {
        k.Rounded(k.Body, 0.12f, (-0.55f, -0.1f), (-0.35f, -0.6f), (0.35f, -0.6f), (0.6f, -0.1f));
        k.Rounded(k.Pale, 0.05f, (-0.38f, -0.15f), (-0.25f, -0.47f), (-0.04f, -0.47f), (-0.04f, -0.15f));
        k.Rounded(k.Pale, 0.05f, (0.06f, -0.15f), (0.06f, -0.47f), (0.27f, -0.47f), (0.44f, -0.15f));
        k.Rounded(k.Body, 0.16f, (-1.0f, -0.12f), (1.0f, -0.12f), (1.0f, 0.5f), (-1.0f, 0.5f));
        if (k.Fine) k.Dot(k.Accent, 0.9f, 0.05f, 0.07f);
        foreach (var x in new[] { -0.55f, 0.55f })
        {
            k.Circle(k.Dark, x, 0.58f, 0.3f);
            k.Circle(k.Pale, x, 0.58f, 0.12f);
        }
    }
}
