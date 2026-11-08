using Keypuncher.Core;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Real animals and dinosaurs, drawn the way a picture book for babies draws them: a few big rounded
/// parts, each filled and outlined, and the one or two features that name the animal (a cat's pointed
/// ears, a cow's patches, an elephant's trunk) kept even at the plainest level. Nothing has teeth,
/// claws or a sharp point. Every animal faces right and fits the same box as the other shapes, about
/// one radius each way, with its feet near the bottom (or, for a swimmer, round the middle).
/// </summary>
/// <remarks>
/// Animals whose faces children know from the front (cat, dog, cow, sheep, rabbit, bear, mouse) turn
/// their heads to the viewer and get two eyes. The rest are only recognisable in profile, so they keep
/// one eye even for a baby: two eyes on a side-on head stop it reading as that animal at all.
/// </remarks>
internal static partial class RecipePainter
{
    /// <summary>Natural white for wool, a cow's coat and a muzzle; a touch warm so it never matches an eye's pure white.</summary>
    private const uint PaleFill = 0xFAF6EE;

    private static void Animal(SKCanvas c, Shape shape, float r, int lobes, int hue, double t, Brush b)
    {
        using var k = new Kit(c, r, hue, t, b);
        switch (shape)
        {
            case Shape.Cat: Cat(k, lobes); break;
            case Shape.Dog: Dog(k); break;
            case Shape.Fish: Fish(k, lobes); break;
            case Shape.Bird: Bird(k); break;
            case Shape.Cow: Cow(k, lobes); break;
            case Shape.Sheep: Sheep(k); break;
            case Shape.Horse: Horse(k); break;
            case Shape.Duck: Duck(k); break;
            case Shape.Elephant: Elephant(k); break;
            case Shape.Rabbit: Rabbit(k); break;
            case Shape.Bear: Bear(k); break;
            case Shape.Mouse: Mouse(k); break;
            case Shape.Rex: Rex(k); break;
            case Shape.LongNeck: LongNeck(k); break;
            case Shape.Triceratops: Triceratops(k); break;
            case Shape.Stegosaurus: Stegosaurus(k, lobes); break;
            case Shape.Whale: Whale(k); break;
            case Shape.Dolphin: Dolphin(k); break;
            case Shape.Shark: Shark(k); break;
            case Shape.Octopus: Octopus(k); break;
            case Shape.Crab: Crab(k); break;
            case Shape.Turtle: Turtle(k); break;
            case Shape.Seal: Seal(k); break;
            case Shape.Seahorse: Seahorse(k); break;
            case Shape.Starfish: Starfish(k); break;
            case Shape.Jellyfish: Jellyfish(k, lobes); break;
            case Shape.Butterfly: Butterfly(k); break;
        }
    }

    /// <summary>Seen from above: two big upper wings and two smaller lower ones either side of a dark body, with a
    /// round head, two antennae and a face. Petals round a middle read as a flower, so the wings never meet.</summary>
    private static void Butterfly(Kit k)
    {
        // The wings open and close a little, on the thing's own clock.
        var flap = 1 - Math.Abs(k.Wag(0.12f));
        foreach (var side in new[] { -1f, 1f })
        {
            k.Oval(k.Body, side * 0.4f * flap, 0.38f, 0.3f * flap, 0.26f, side * 30);
            k.Oval(k.Body, side * 0.48f * flap, -0.12f, 0.46f * flap, 0.36f, side * -25);
            if (k.Fine) k.Circle(k.Second, side * 0.55f * flap, -0.15f, 0.13f * flap);
        }
        k.Ribbon(k.Dark, 0.12f, (0, -0.2f), (0, 0.62f));
        var (hx, hy, h) = k.FrontHead(0, -0.46f, 0.22f);
        k.Stroke((hx - (0.3f * h), hy - (0.8f * h)), (hx - (0.75f * h), hy - (2.2f * h)), (hx - (1.4f * h), hy - (2.4f * h)));
        k.Stroke((hx + (0.3f * h), hy - (0.8f * h)), (hx + (0.75f * h), hy - (2.2f * h)), (hx + (1.4f * h), hy - (2.4f * h)));
        k.Circle(k.Body, hx, hy, h);
        k.FrontEyes(hx, hy, h);
    }

    private static void Cat(Kit k, int lobes)
    {
        var wag = k.Wag(0.08f);
        k.Ribbon(k.Body, 0.13f, (-0.55f, 0.1f), (-0.98f, -0.05f + wag), (-0.85f, -0.55f + wag));
        k.Legs(k.Body, 0.15f, 0.86f, 0.17f, -0.5f, -0.3f, 0.18f, 0.36f);
        using var body = k.OvalPath(-0.05f, 0.18f, 0.62f, 0.34f);
        k.Fill(k.Body, body);
        // Tabby stripes from a toddler up, in the outline's colour so they never add a hue.
        k.Stripes(body, Math.Min(Math.Max(lobes, 2), k.Style.MaxStripes), -0.45f, 0.3f);
        k.Line(body);

        var (hx, hy, h) = k.FrontHead(0.5f, -0.25f, 0.44f);
        foreach (var side in new[] { -1f, 1f })
        {
            k.Rounded(k.Body, 0.08f, (hx + (side * 0.92f * h), hy - (0.15f * h)), (hx + (side * 0.78f * h), hy - (1.3f * h)), (hx + (side * 0.12f * h), hy - (0.82f * h)));
            if (k.Fine)
                k.Rounded(k.Pink, 0.05f, (hx + (side * 0.72f * h), hy - (0.45f * h)), (hx + (side * 0.7f * h), hy - (1.0f * h)), (hx + (side * 0.35f * h), hy - (0.75f * h)));
        }
        k.Circle(k.Body, hx, hy, h);
        k.FrontEyes(hx, hy - (0.08f * h), h);
        k.Rounded(k.Pink, 0.04f, (hx - (0.16f * h), hy + (0.3f * h)), (hx + (0.16f * h), hy + (0.3f * h)), (hx, hy + (0.46f * h)));
    }

    private static void Dog(Kit k)
    {
        var wag = k.Wag(0.12f);
        k.Ribbon(k.Body, 0.12f, (-0.58f, 0.05f), (-0.78f, -0.15f), (-0.85f + wag, -0.45f));
        k.Legs(k.Body, 0.15f, 0.86f, 0.19f, -0.48f, -0.28f, 0.16f, 0.36f);
        using var body = k.OvalPath(-0.05f, 0.18f, 0.62f, 0.34f);
        k.Fill(k.Body, body);
        k.Line(body);

        var (hx, hy, h) = k.FrontHead(0.5f, -0.22f, 0.46f);
        k.Circle(k.Body, hx, hy, h);
        // Floppy ears in the darker shade, hanging by the cheeks.
        k.Oval(k.Dark, hx - (1.02f * h), hy + (0.25f * h), 0.24f * h, 0.58f * h, 12);
        k.Oval(k.Dark, hx + (1.02f * h), hy + (0.25f * h), 0.24f * h, 0.58f * h, -12);
        k.Oval(k.Pale, hx, hy + (0.42f * h), 0.5f * h, 0.36f * h);
        k.FrontEyes(hx, hy - (0.15f * h), h);
        k.Oval(k.Ink, hx, hy + (0.3f * h), 0.17f * h, 0.12f * h);
    }

    private static void Fish(Kit k, int lobes)
    {
        k.C.Save();
        k.C.Translate(-0.5f * k.R, 0);
        k.C.RotateDegrees(k.Wag(10));
        k.Rounded(k.Body, 0.1f, (0.05f, 0), (-0.48f, -0.44f), (-0.48f, 0.44f));
        k.C.Restore();
        k.Rounded(k.Body, 0.08f, (-0.22f, -0.3f), (0.02f, -0.58f), (0.22f, -0.32f));
        k.Rounded(k.Body, 0.06f, (-0.12f, 0.3f), (0.0f, 0.5f), (0.14f, 0.3f));
        using var body = k.OvalPath(0.05f, 0, 0.62f, 0.4f);
        k.Fill(k.Body, body);
        k.Stripes(body, Math.Min(Math.Max(lobes, 2), k.Style.MaxStripes), -0.3f, 0.3f);
        k.Line(body);
        k.SideEye(0.36f, -0.08f, 0.2f);
    }

    private static void Bird(Kit k)
    {
        k.Ribbon(k.Accent, 0.05f, (-0.08f, 0.45f), (-0.1f, 0.82f), (0.02f, 0.84f));
        k.Ribbon(k.Accent, 0.05f, (0.12f, 0.45f), (0.12f, 0.82f), (0.24f, 0.84f));
        k.Rounded(k.Body, 0.08f, (-0.35f, 0.15f), (-0.95f, -0.2f), (-0.85f, 0.25f));
        k.Circle(k.Body, -0.02f, 0.18f, 0.46f);
        k.Rounded(k.Accent, 0.04f, (0.55f, -0.38f), (0.85f, -0.27f), (0.55f, -0.17f));
        var h = k.SideHead(0.3f);
        k.Circle(k.Body, 0.32f, -0.28f, h);
        k.Oval(k.Body, -0.12f, 0.2f, 0.32f, 0.2f, -18);
        k.SideEye(0.38f, -0.32f, h);
    }

    private static void Cow(Kit k, int lobes)
    {
        var wag = k.Wag(0.06f);
        k.Ribbon(k.Pale, 0.06f, (-0.68f, 0.0f), (-0.82f + wag, 0.25f), (-0.8f + wag, 0.5f));
        k.Circle(k.Dark, -0.8f + wag, 0.53f, 0.08f);
        k.Legs(k.Pale, 0.15f, 0.86f, 0.18f, -0.52f, -0.32f, 0.16f, 0.36f);
        if (k.Fine) k.Hooves(0.86f, 0.18f, -0.52f, -0.32f, 0.16f, 0.36f);
        using var body = k.OvalPath(-0.05f, 0.14f, 0.68f, 0.36f);
        k.Fill(k.Pale, body);
        // Black-and-white patches, the cow's name tag: a big one even for a baby, more when older.
        k.Inside(body, () =>
        {
            var spots = new (float X, float Y, float Rx, float Ry)[] { (-0.3f, 0.0f, 0.26f, 0.2f), (0.2f, 0.25f, 0.2f, 0.16f), (-0.55f, 0.35f, 0.16f, 0.14f), (0.38f, -0.12f, 0.12f, 0.1f) };
            foreach (var s in spots.Take(Math.Clamp(lobes, 2, Math.Max(2, k.Style.MaxDots / 2 + 1))))
                k.C.DrawOval(k.R * s.X, k.R * s.Y, k.R * s.Rx, k.R * s.Ry, k.Dark.FillPaint);
        });
        k.Line(body);

        var (hx, hy, h) = k.FrontHead(0.55f, -0.22f, 0.42f);
        k.Rounded(k.Accent, 0.06f, (hx - (0.62f * h), hy - (0.7f * h)), (hx - (0.75f * h), hy - (1.15f * h)), (hx - (0.35f * h), hy - (0.85f * h)));
        k.Rounded(k.Accent, 0.06f, (hx + (0.62f * h), hy - (0.7f * h)), (hx + (0.75f * h), hy - (1.15f * h)), (hx + (0.35f * h), hy - (0.85f * h)));
        k.Oval(k.Pale, hx - (0.95f * h), hy - (0.35f * h), 0.36f * h, 0.17f * h, 20);
        k.Oval(k.Pale, hx + (0.95f * h), hy - (0.35f * h), 0.36f * h, 0.17f * h, -20);
        k.Oval(k.Pale, hx, hy, 0.78f * h, 0.95f * h);
        k.FrontEyes(hx, hy - (0.25f * h), h);
        k.Oval(k.Pink, hx, hy + (0.6f * h), 0.62f * h, 0.38f * h);
        if (k.Fine)
        {
            k.Dot(k.Ink, hx - (0.25f * h), hy + (0.6f * h), 0.08f * h);
            k.Dot(k.Ink, hx + (0.25f * h), hy + (0.6f * h), 0.08f * h);
        }
    }

    private static void Sheep(Kit k)
    {
        k.Legs(k.Dark, 0.2f, 0.86f, 0.12f, -0.45f, -0.25f, 0.15f, 0.33f);
        // Wool: a ring of round bumps round a middle, merged into one fluffy outline.
        using (var builder = new SKPathBuilder())
        {
            var bumps = 6 + (k.Style.MaxLobes / 2);
            for (var i = 0; i < bumps; i++)
            {
                var a = i * Math.PI * 2 / bumps;
                builder.AddCircle(k.R * (float)(-0.08 + (Math.Cos(a) * 0.5)), k.R * (float)(0.1 + (Math.Sin(a) * 0.26)), k.R * 0.22f, SKPathDirection.Clockwise);
            }
            builder.AddOval(SKRect.Create(k.R * -0.62f, k.R * -0.2f, k.R * 1.08f, k.R * 0.6f), SKPathDirection.Clockwise);
            using var wool = builder.Detach();
            using var merged = wool.Simplify() ?? wool;
            k.Fill(k.Pale, merged);
            k.Line(merged);
        }

        var (hx, hy, h) = k.FrontHead(0.58f, -0.12f, 0.38f);
        k.Oval(k.Dark, hx - (0.9f * h), hy - (0.25f * h), 0.38f * h, 0.16f * h, 15);
        k.Oval(k.Dark, hx + (0.9f * h), hy - (0.25f * h), 0.38f * h, 0.16f * h, -15);
        k.Oval(k.Dark, hx, hy, 0.68f * h, 0.9f * h);
        if (k.Fine)
            foreach (var dx in new[] { -0.35f, 0f, 0.35f })
                k.Circle(k.Pale, hx + (dx * h), hy - (0.85f * h), 0.24f * h);
        k.FrontEyes(hx, hy - (0.1f * h), h);
    }

    private static void Horse(Kit k)
    {
        var wag = k.Wag(0.06f);
        k.Ribbon(k.Dark, 0.14f, (-0.58f, -0.08f), (-0.8f + wag, 0.15f), (-0.78f + wag, 0.5f));
        k.Legs(k.Body, 0.0f, 0.88f, 0.15f, -0.45f, -0.28f, 0.2f, 0.36f);
        if (k.Fine) k.Hooves(0.88f, 0.15f, -0.45f, -0.28f, 0.2f, 0.36f);
        k.Oval(k.Body, -0.05f, 0.02f, 0.6f, 0.3f);
        k.Rounded(k.Body, 0.12f, (0.2f, 0.05f), (0.4f, -0.62f), (0.66f, -0.58f), (0.58f, 0.08f));
        k.Ribbon(k.Body, 0.3f, (0.58f, -0.66f), (0.92f, -0.38f));
        k.Rounded(k.Body, 0.05f, (0.52f, -0.78f), (0.56f, -1.02f), (0.66f, -0.8f));
        k.Ribbon(k.Dark, 0.11f, (0.5f, -0.78f), (0.36f, -0.45f), (0.24f, -0.08f));
        if (k.Fine) k.Dot(k.Ink, 0.93f, -0.36f, 0.035f);
        k.SideEye(0.66f, -0.6f, 0.17f);
    }

    private static void Duck(Kit k)
    {
        k.Oval(k.Accent, -0.08f, 0.8f, 0.17f, 0.07f);
        k.Oval(k.Accent, 0.18f, 0.82f, 0.17f, 0.07f);
        k.Rounded(k.Body, 0.1f, (-0.4f, 0.1f), (-0.9f, -0.12f), (-0.55f, 0.45f));
        k.Oval(k.Body, -0.05f, 0.35f, 0.62f, 0.38f);
        k.Oval(k.Accent, 0.68f, -0.18f, 0.22f, 0.1f, 8);
        var h = k.SideHead(0.32f);
        k.Circle(k.Body, 0.35f, -0.28f, h);
        k.Oval(k.Body, -0.12f, 0.33f, 0.34f, 0.2f, -12);
        k.SideEye(0.42f, -0.34f, h);
    }

    private static void Elephant(Kit k)
    {
        k.Ribbon(k.Dark, 0.05f, (-0.68f, 0.0f), (-0.82f + k.Wag(0.05f), 0.4f));
        k.Legs(k.Body, 0.1f, 0.88f, 0.25f, -0.48f, -0.22f, 0.15f, 0.38f);
        k.Oval(k.Body, -0.08f, 0.08f, 0.66f, 0.42f);
        // The trunk hangs and curls up at the tip, drawn under the head so the head covers its root.
        k.Ribbon(k.Body, 0.17f, (0.72f, -0.08f), (0.98f, 0.55f), (1.05f, 0.35f));
        if (k.Fine) k.Ribbon(k.Pale, 0.06f, (0.7f, 0.12f), (0.82f, 0.22f));
        var h = k.SideHead(0.36f);
        k.Circle(k.Body, 0.58f, -0.18f, h);
        k.Oval(k.Body, 0.32f, -0.12f, 0.27f, 0.36f);
        if (k.Fine) k.Oval(k.Pink, 0.32f, -0.12f, 0.15f, 0.24f);
        k.SideEye(0.72f, -0.28f, 0.22f);
    }

    private static void Rabbit(Kit k)
    {
        k.Oval(k.Body, -0.28f, 0.8f, 0.26f, 0.12f);
        k.Oval(k.Body, 0.28f, 0.8f, 0.26f, 0.12f);
        k.Oval(k.Body, 0, 0.38f, 0.48f, 0.46f);
        if (k.Fine) k.Oval(k.Pale, 0, 0.45f, 0.27f, 0.3f);
        k.Oval(k.Body, -0.17f, 0.68f, 0.12f, 0.09f);
        k.Oval(k.Body, 0.17f, 0.68f, 0.12f, 0.09f);
        var (hx, hy, h) = k.FrontHead(0, -0.22f, 0.4f);
        foreach (var side in new[] { -1f, 1f })
        {
            var ex = hx + (side * 0.45f * h);
            var ey = hy - (1.25f * h);
            k.Oval(k.Body, ex, ey, 0.3f * h, 0.85f * h, side * 8);
            if (k.Fine) k.Oval(k.Pink, ex, ey + (0.08f * h), 0.15f * h, 0.6f * h, side * 8);
        }
        k.Circle(k.Body, hx, hy, h);
        k.FrontEyes(hx, hy - (0.08f * h), h);
        k.Oval(k.Pink, hx, hy + (0.35f * h), 0.14f * h, 0.1f * h);
    }

    private static void Bear(Kit k)
    {
        k.Legs(k.Body, 0.15f, 0.86f, 0.24f, -0.48f, -0.24f, 0.14f, 0.38f);
        k.Oval(k.Body, -0.05f, 0.15f, 0.64f, 0.4f);
        var (hx, hy, h) = k.FrontHead(0.5f, -0.24f, 0.46f);
        foreach (var side in new[] { -1f, 1f })
        {
            k.Circle(k.Body, hx + (side * 0.72f * h), hy - (0.72f * h), 0.3f * h);
            if (k.Fine) k.Circle(k.Pale, hx + (side * 0.72f * h), hy - (0.72f * h), 0.15f * h);
        }
        k.Circle(k.Body, hx, hy, h);
        k.Oval(k.Pale, hx, hy + (0.4f * h), 0.44f * h, 0.33f * h);
        k.FrontEyes(hx, hy - (0.15f * h), h);
        k.Oval(k.Ink, hx, hy + (0.3f * h), 0.17f * h, 0.12f * h);
    }

    private static void Mouse(Kit k)
    {
        k.Ribbon(k.Pink, 0.05f, (-0.55f, 0.55f), (-0.95f, 0.75f + k.Wag(0.06f)), (-0.98f, 0.3f));
        k.Oval(k.Body, -0.25f, 0.8f, 0.14f, 0.07f);
        k.Oval(k.Body, 0.2f, 0.8f, 0.14f, 0.07f);
        k.Oval(k.Body, -0.1f, 0.48f, 0.56f, 0.32f);
        var (hx, hy, h) = k.FrontHead(0.42f, 0.02f, 0.4f);
        foreach (var side in new[] { -1f, 1f })
        {
            k.Circle(k.Body, hx + (side * 0.82f * h), hy - (0.72f * h), 0.52f * h);
            k.Circle(k.Pink, hx + (side * 0.82f * h), hy - (0.72f * h), 0.3f * h);
        }
        k.Circle(k.Body, hx, hy, h);
        k.FrontEyes(hx, hy - (0.1f * h), h);
        k.Circle(k.Pink, hx, hy + (0.38f * h), 0.12f * h);
    }

    private static void Rex(Kit k)
    {
        k.Rounded(k.Body, 0.12f, (-0.3f, -0.05f), (-1.05f, 0.62f), (-0.18f, 0.4f));
        k.Legs(k.Body, 0.35f, 0.82f, 0.16f, -0.3f);
        k.Oval(k.Body, -0.25f, 0.86f, 0.18f, 0.07f);
        k.Oval(k.Body, -0.05f, 0.1f, 0.38f, 0.48f, 28);
        k.Oval(k.Body, -0.05f, 0.4f, 0.24f, 0.3f);
        k.Legs(k.Body, 0.45f, 0.82f, 0.17f, 0.0f);
        k.Oval(k.Body, 0.06f, 0.86f, 0.19f, 0.07f);
        k.Ribbon(k.Body, 0.08f, (0.28f, -0.02f), (0.42f, 0.1f), (0.5f, 0.03f));
        k.Rounded(k.Body, 0.16f, (0.12f, -0.55f), (0.32f, -0.95f), (0.95f, -0.86f), (0.95f, -0.5f), (0.3f, -0.3f));
        // A closed, gentle smile, never a row of teeth.
        if (k.Fine)
        {
            k.Stroke((0.52f, -0.6f), (0.72f, -0.54f), (0.9f, -0.6f));
            k.Dot(k.Ink, 0.86f, -0.8f, 0.03f);
        }
        k.SideEye(0.48f, -0.75f, 0.17f);
    }

    private static void LongNeck(Kit k)
    {
        k.Rounded(k.Body, 0.1f, (-0.45f, 0.0f), (-1.05f, 0.42f), (-0.35f, 0.32f));
        k.Legs(k.Body, 0.12f, 0.88f, 0.2f, -0.42f, -0.22f, 0.1f, 0.3f);
        k.Ribbon(k.Body, 0.2f, (0.3f, 0.0f), (0.58f, -0.4f), (0.6f, -0.82f));
        k.Oval(k.Body, -0.08f, 0.12f, 0.55f, 0.32f);
        var h = k.SideHead(0.15f);
        k.Oval(k.Body, 0.72f, -0.88f, h * 1.5f, h);
        k.SideEye(0.74f, -0.92f, h);
    }

    private static void Triceratops(Kit k)
    {
        k.Rounded(k.Body, 0.1f, (-0.5f, 0.1f), (-1.05f, 0.42f), (-0.45f, 0.42f));
        k.Legs(k.Body, 0.25f, 0.86f, 0.2f, -0.45f, -0.25f, 0.12f, 0.3f);
        k.Oval(k.Body, -0.12f, 0.25f, 0.56f, 0.34f);
        // The frill is the triceratops' name tag; in a second colour so it reads as its own part.
        k.Circle(k.Second, 0.42f, -0.18f, 0.4f);
        if (k.Fine)
            for (var i = 0; i < 5; i++)
            {
                var a = (Math.PI * 0.7) + (i * Math.PI * 0.4);
                k.Circle(k.Pale, 0.42f + (float)(Math.Cos(a) * 0.3), -0.18f + (float)(Math.Sin(a) * 0.3), 0.05f);
            }
        k.Rounded(k.Accent, 0.05f, (0.88f, -0.02f), (1.06f, 0.1f), (0.88f, 0.2f));
        k.Oval(k.Body, 0.65f, 0.04f, 0.3f, 0.22f);
        k.Rounded(k.Pale, 0.05f, (0.47f, -0.1f), (0.64f, -0.17f), (0.8f, -0.5f));
        k.Rounded(k.Pale, 0.05f, (0.6f, -0.12f), (0.77f, -0.13f), (0.94f, -0.45f));
        k.Rounded(k.Pale, 0.03f, (0.84f, -0.08f), (0.95f, -0.24f), (0.94f, -0.04f));
        k.SideEye(0.6f, -0.02f, 0.17f);
    }

    private static void Stegosaurus(Kit k, int lobes)
    {
        // Plates along the back, tallest over the hips, in a second colour.
        var plates = Math.Clamp(lobes, 4, Math.Min(k.Style.MaxLobes, 7));
        for (var i = 0; i < plates; i++)
        {
            var u = (i + 0.5f) / plates;
            var x = -0.55f + (u * 0.95f);
            var height = 0.2f + (0.16f * (float)Math.Sin(u * Math.PI));
            var baseY = 0.2f - (0.36f * (float)Math.Sin((u * 0.9 + 0.05) * Math.PI));
            k.Rounded(k.Second, 0.06f, (x - 0.09f, baseY + 0.05f), (x - 0.06f, baseY - height + 0.06f), (x, baseY - height), (x + 0.06f, baseY - height + 0.06f), (x + 0.09f, baseY + 0.05f));
        }
        k.Rounded(k.Body, 0.1f, (-0.4f, 0.12f), (-1.05f, 0.05f), (-0.4f, 0.42f));
        if (k.Fine)
        {
            k.Rounded(k.Pale, 0.03f, (-0.88f, 0.04f), (-0.92f, -0.12f), (-0.8f, 0.03f));
            k.Rounded(k.Pale, 0.03f, (-0.74f, 0.04f), (-0.76f, -0.12f), (-0.66f, 0.06f));
        }
        k.Legs(k.Body, 0.2f, 0.88f, 0.18f, -0.38f, -0.2f, 0.18f, 0.32f);
        k.Ribbon(k.Body, 0.2f, (0.38f, 0.25f), (0.66f, 0.38f));
        k.Oval(k.Body, -0.08f, 0.22f, 0.55f, 0.34f);
        var h = k.SideHead(0.13f);
        k.Oval(k.Body, 0.7f, 0.38f, h * 1.4f, h);
        k.SideEye(0.72f, 0.34f, h);
    }

    private static void Whale(Kit k)
    {
        // The spout first, so the head covers its root.
        k.Ribbon(k.Pale, 0.06f, (0.32f, -0.2f), (0.3f, -0.62f));
        k.Ribbon(k.Pale, 0.05f, (0.3f, -0.6f), (0.18f, -0.78f), (0.08f, -0.7f));
        k.Ribbon(k.Pale, 0.05f, (0.3f, -0.6f), (0.42f, -0.78f), (0.52f, -0.7f));
        var wag = k.Wag(0.06f);
        k.Ribbon(k.Body, 0.16f, (-0.62f, 0.1f), (-0.86f, -0.16f + wag));
        k.Oval(k.Body, -0.98f, -0.28f + wag, 0.17f, 0.075f, -50);
        k.Oval(k.Body, -0.76f, -0.32f + wag, 0.17f, 0.075f, 50);
        using var body = k.OvalPath(0.0f, 0.12f, 0.8f, 0.42f);
        k.Fill(k.Body, body);
        // A pale belly, the way a child draws a whale.
        k.Inside(body, () => k.C.DrawOval(k.R * 0.1f, k.R * 0.5f, k.R * 0.75f, k.R * 0.2f, k.Pale.FillPaint));
        k.Line(body);
        k.Oval(k.Body, 0.15f, 0.36f, 0.2f, 0.08f, 30);
        if (k.Fine) k.Stroke((0.55f, 0.22f), (0.68f, 0.28f), (0.78f, 0.2f));
        k.SideEye(0.5f, 0.02f, 0.22f);
    }

    private static void Dolphin(Kit k)
    {
        var wag = k.Wag(0.06f);
        k.Rounded(k.Body, 0.08f, (-0.25f, -0.18f), (-0.18f, -0.52f), (0.1f, -0.2f));
        k.Ribbon(k.Body, 0.12f, (-0.5f, 0.04f), (-0.82f, 0.0f + wag));
        k.Oval(k.Body, -0.92f, -0.1f + wag, 0.15f, 0.065f, -35);
        k.Oval(k.Body, -0.92f, 0.1f + wag, 0.15f, 0.065f, 35);
        k.Ribbon(k.Body, 0.13f, (0.52f, 0.0f), (0.78f, 0.06f));
        using var body = k.OvalPath(0.0f, 0.0f, 0.62f, 0.27f);
        k.Fill(k.Body, body);
        k.Inside(body, () => k.C.DrawOval(k.R * 0.1f, k.R * 0.26f, k.R * 0.55f, k.R * 0.13f, k.Pale.FillPaint));
        k.Line(body);
        k.Oval(k.Body, 0.12f, 0.24f, 0.16f, 0.07f, 35);
        k.SideEye(0.4f, -0.07f, 0.18f);
    }

    private static void Shark(Kit k)
    {
        var wag = k.Wag(0.06f);
        // A tall back fin and a crescent tail are what say shark; no teeth, a closed smile.
        k.Rounded(k.Body, 0.08f, (-0.2f, -0.18f), (-0.05f, -0.68f), (0.18f, -0.18f));
        k.Rounded(k.Body, 0.08f, (-0.5f, 0.05f), (-1.0f, -0.48f + wag), (-0.78f, 0.05f), (-0.95f, 0.38f + wag));
        k.Rounded(k.Body, 0.08f, (0.05f, 0.22f), (-0.08f, 0.55f), (0.28f, 0.26f));
        using (var builder = new SKPathBuilder())
        {
            builder.MoveTo(-0.65f * k.R, 0.05f * k.R);
            builder.CubicTo(-0.4f * k.R, -0.3f * k.R, 0.6f * k.R, -0.32f * k.R, 0.98f * k.R, 0.04f * k.R);
            builder.CubicTo(0.6f * k.R, 0.36f * k.R, -0.4f * k.R, 0.34f * k.R, -0.65f * k.R, 0.05f * k.R);
            builder.Close();
            using var body = builder.Detach();
            k.Fill(k.Body, body);
            k.Inside(body, () => k.C.DrawOval(k.R * 0.15f, k.R * 0.3f, k.R * 0.75f, k.R * 0.14f, k.Pale.FillPaint));
            k.Line(body);
        }
        if (k.Fine)
        {
            foreach (var x in new[] { 0.32f, 0.39f, 0.46f }) k.Stroke((x, -0.02f), (x - 0.02f, 0.12f));
            k.Stroke((0.66f, 0.12f), (0.78f, 0.16f), (0.88f, 0.1f));
        }
        k.SideEye(0.62f, -0.04f, 0.16f);
    }

    private static void Octopus(Kit k)
    {
        // Eight arms from two, six for a baby so they stay thick and apart.
        var arms = k.Fine ? 8 : 6;
        for (var i = 0; i < arms; i++)
        {
            var u = (i + 0.5f) / arms;
            var x0 = -0.42f + (0.84f * u);
            var x1 = -0.95f + (1.9f * u);
            var curl = (u < 0.5f ? -1 : 1) * 0.16f;
            var sway = k.Wag(0.06f) * (i % 2 == 0 ? 1 : -1);
            k.Ribbon(k.Body, 0.13f, (x0, 0.1f), (x1 * 0.8f + sway, 0.45f), (x1 + sway, 0.62f), (x1 + curl + sway, 0.78f));
        }
        k.Oval(k.Body, 0, -0.28f, 0.56f, 0.56f);
        if (k.Fine)
        {
            k.Circle(k.Second, -0.3f, -0.55f, 0.07f);
            k.Circle(k.Second, 0.3f, -0.5f, 0.06f);
            k.Stroke((-0.14f, 0.04f), (0.0f, 0.1f), (0.14f, 0.04f));
        }
        k.FrontEyes(0, -0.18f, 0.42f);
    }

    private static void Crab(Kit k)
    {
        foreach (var side in new[] { -1f, 1f })
        {
            // Three legs a side from a toddler up; two for a baby, so they don't merge into one dark mass.
            for (var i = 0; i < (k.Fine ? 3 : 2); i++)
                k.Ribbon(k.Body, 0.08f, (side * 0.45f, 0.3f + (i * 0.12f)), (side * 0.85f, 0.25f + (i * 0.16f)), (side * 0.95f, 0.55f + (i * 0.14f)));
            k.Ribbon(k.Body, 0.1f, (side * 0.38f, 0.05f), (side * 0.62f, -0.25f), (side * 0.72f, -0.4f));
            // A rounded mitten of a claw: a big lower pincer with a smaller one above it.
            k.Oval(k.Body, side * 0.76f, -0.5f, 0.2f, 0.15f, side * -20);
            k.Oval(k.Body, side * 0.66f, -0.7f, 0.14f, 0.09f, side * 35);
        }
        k.Oval(k.Body, 0, 0.22f, 0.6f, 0.38f);
        if (k.Fine) k.Stroke((-0.14f, 0.38f), (0.0f, 0.46f), (0.14f, 0.38f));
        k.FrontEyes(0, 0.1f, 0.4f);
    }

    private static void Turtle(Kit k)
    {
        var paddle = k.Wag(8);
        k.Oval(k.Second, -0.58f, 0.28f, 0.17f, 0.08f, -20);
        k.Oval(k.Second, 0.68f, -0.02f, 0.2f, 0.15f);
        using (var builder = new SKPathBuilder())
        {
            builder.MoveTo(-0.7f * k.R, 0.22f * k.R);
            builder.CubicTo(-0.62f * k.R, -0.55f * k.R, 0.55f * k.R, -0.55f * k.R, 0.6f * k.R, 0.22f * k.R);
            builder.Close();
            using var shell = builder.Detach();
            k.Fill(k.Body, shell);
            // The shell's plates, as lines only, from a toddler up.
            if (k.Fine)
                k.Inside(shell, () =>
                {
                    k.C.DrawOval(new SKRect(-0.22f * k.R, -0.28f * k.R, 0.14f * k.R, 0.06f * k.R), k.Body.LinePaint);
                    k.C.DrawOval(new SKRect(-0.62f * k.R, -0.1f * k.R, -0.26f * k.R, 0.26f * k.R), k.Body.LinePaint);
                    k.C.DrawOval(new SKRect(0.18f * k.R, -0.1f * k.R, 0.52f * k.R, 0.26f * k.R), k.Body.LinePaint);
                });
            k.Line(shell);
        }
        k.C.Save();
        k.C.Translate(0.25f * k.R, 0.24f * k.R);
        k.C.RotateDegrees(paddle);
        k.Oval(k.Second, 0.12f, 0.1f, 0.3f, 0.1f, 30);
        k.C.Restore();
        k.SideEye(0.74f, -0.05f, 0.13f);
    }

    private static void Seal(Kit k)
    {
        k.Rounded(k.Body, 0.08f, (-0.65f, 0.35f), (-1.02f, 0.12f), (-0.98f, 0.55f));
        k.Oval(k.Body, -0.1f, 0.32f, 0.65f, 0.3f, -6);
        var h = k.SideHead(0.32f);
        k.Circle(k.Body, 0.45f, -0.08f, h);
        k.Oval(k.Body, 0.15f, 0.55f, 0.2f, 0.08f, 40);
        k.Circle(k.Pale, 0.66f, 0.04f, 0.1f);
        k.Circle(k.Pale, 0.74f, 0.06f, 0.09f);
        k.Oval(k.Ink, 0.74f, -0.04f, 0.07f, 0.05f);
        if (k.Fine)
            foreach (var dy in new[] { -0.03f, 0.05f })
                k.Stroke((0.8f, 0.04f + dy), (0.98f, 0.0f + (dy * 2)));
        k.SideEye(0.5f, -0.16f, h);
    }

    private static void Seahorse(Kit k)
    {
        var wag = k.Wag(0.05f);
        k.Ribbon(k.Body, 0.14f, (0.0f, 0.3f), (-0.12f, 0.75f), (0.18f + wag, 0.86f), (0.16f + wag, 0.62f));
        k.Oval(k.Second, -0.27f, -0.02f, 0.1f, 0.17f, 10);
        k.Ribbon(k.Body, 0.34f, (0.0f, -0.35f), (0.12f, 0.0f), (0.02f, 0.3f));
        k.Oval(k.Body, 0.1f, 0.02f, 0.24f, 0.3f);
        if (k.Fine)
            foreach (var y in new[] { -0.12f, 0.02f, 0.16f })
                k.Stroke((0.0f, y), (0.22f, y + 0.02f));
        k.Rounded(k.Body, 0.04f, (-0.08f, -0.7f), (-0.02f, -0.92f), (0.08f, -0.7f));
        k.Ribbon(k.Body, 0.1f, (0.12f, -0.55f), (0.46f, -0.45f));
        var h = k.SideHead(0.22f);
        k.Circle(k.Body, 0.0f, -0.55f, h);
        k.SideEye(0.04f, -0.58f, h);
    }

    private static void Starfish(Kit k)
    {
        var points = new (float X, float Y)[10];
        for (var i = 0; i < 10; i++)
        {
            var a = (i * Math.PI / 5) - (Math.PI / 2);
            var rr = i % 2 == 0 ? 0.95f : 0.45f;
            points[i] = ((float)Math.Cos(a) * rr, (float)(Math.Sin(a) * rr) + 0.05f);
        }
        k.Rounded(k.Body, 0.16f, points);
        if (k.Fine)
            for (var i = 0; i < 5; i++)
            {
                var a = (i * Math.PI * 2 / 5) - (Math.PI / 2);
                k.Dot(k.Pale, (float)Math.Cos(a) * 0.62f, (float)(Math.Sin(a) * 0.62) + 0.05f, 0.05f);
            }
        k.FrontEyes(0, 0.0f, 0.36f);
        if (k.Fine) k.Stroke((-0.1f, 0.2f), (0.0f, 0.26f), (0.1f, 0.2f));
    }

    private static void Jellyfish(Kit k, int lobes)
    {
        var strands = Math.Clamp(lobes, 3, k.Fine ? k.Style.MaxLobes : 3);
        for (var i = 0; i < strands; i++)
        {
            var x = -0.4f + (0.8f * (i + 0.5f) / strands);
            var sway = k.Wag(0.08f) * (i % 2 == 0 ? 1 : -1);
            k.Ribbon(k.Body, 0.07f, (x, 0.0f), (x - 0.12f + sway, 0.3f), (x + 0.12f - sway, 0.6f), (x + sway, 0.88f));
        }
        using var builder = new SKPathBuilder();
        builder.MoveTo(-0.62f * k.R, 0.05f * k.R);
        builder.CubicTo(-0.62f * k.R, -0.9f * k.R, 0.62f * k.R, -0.9f * k.R, 0.62f * k.R, 0.05f * k.R);
        // A scalloped rim: four soft bumps back along the bottom.
        for (var i = 0; i < 4; i++)
        {
            var x0 = 0.62f - (i * 0.31f);
            builder.QuadTo((x0 - 0.155f) * k.R, 0.22f * k.R, (x0 - 0.31f) * k.R, 0.05f * k.R);
        }
        builder.Close();
        using var bell = builder.Detach();
        k.Fill(k.Body, bell);
        k.Line(bell);
        k.FrontEyes(0, -0.28f, 0.4f);
        if (k.Fine) k.Stroke((-0.12f, -0.08f), (0.0f, -0.02f), (0.12f, -0.08f));
    }

    /// <summary>A fill and an outline for one kind of part.</summary>
    private sealed class Pen(SKColor fill, SKColor outline, float stroke) : IDisposable
    {
        public readonly SKPaint FillPaint = new() { IsAntialias = true, Color = fill };
        public readonly SKPaint LinePaint = new()
        {
            IsAntialias = true, Color = outline, Style = SKPaintStyle.Stroke, StrokeWidth = stroke,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
        };

        public void Dispose()
        {
            FillPaint.Dispose();
            LinePaint.Dispose();
        }
    }

    /// <summary>
    /// What an animal is drawn with. Positions and sizes are in body radii, x to the right and y down,
    /// so the drawings above read as proportions; the kit scales them and keeps every part at least as
    /// thick as the style's finest line.
    /// </summary>
    private sealed class Kit : IDisposable
    {
        public readonly SKCanvas C;
        public readonly float R;
        private readonly double _t;
        private readonly Brush _b;

        /// <summary>The recipe's palette colour.</summary>
        public readonly Pen Body;
        /// <summary>The body's outline colour as a fill: a sheep's face, a horse's mane, a cow's patches.</summary>
        public readonly Pen Dark;
        /// <summary>Natural white: wool, a cow, a muzzle, horns.</summary>
        public readonly Pen Pale;
        /// <summary>Orange, which every level's palette has: beaks, bills, feet.</summary>
        public readonly Pen Accent;
        public readonly Pen Pink;
        /// <summary>Another palette colour for a dinosaur's frill or plates.</summary>
        public readonly Pen Second;
        /// <summary>The eyes' ink, for noses.</summary>
        public readonly Pen Ink;
        /// <summary>Leaf green, whatever the body's colour.</summary>
        public readonly Pen Leaf;

        public Kit(SKCanvas c, float r, int hue, double t, Brush b)
        {
            C = c;
            R = r;
            _t = t;
            _b = b;
            Body = new Pen(b.FillColor, b.Outline, b.Stroke);
            Dark = new Pen(b.Outline, b.Outline, b.Stroke);
            Pale = new Pen(Rgb(PaleFill), b.Outline, b.Stroke);
            Accent = PenFor(b.Style.Snap(32), b.Stroke);
            Pink = PenFor(b.Style.Snap(335), b.Stroke);
            Second = PenFor(b.Style.Companion(hue), b.Stroke);
            Ink = new Pen(Rgb(b.Style.Ink), Rgb(b.Style.Ink), b.Stroke);
            Leaf = PenFor(b.Style.Snap(130), b.Stroke);
        }

        public ArtStyle Style => _b.Style;

        /// <summary>Small parts a baby can't make out yet (inner ears, hooves, nostrils) are left off at the plainest level.</summary>
        public bool Fine => _b.Style.MaxStripes > 0;

        /// <summary>A slow sway for a tail or fin, on the thing's own clock so it stops when the thing rests.</summary>
        public float Wag(float amount) => amount * (float)Math.Sin(_t * 2.2);

        /// <summary>Where a front-facing head goes and how big it is: bigger for younger eyes, as in a baby's
        /// picture book, and never too small to hold a pair of eyes at the style's smallest pupil.</summary>
        public (float X, float Y, float H) FrontHead(float x, float y, float h)
        {
            var scaled = h * R * (1 + (0.15f * ((float)_b.Style.EyeScale - 1)));
            var fitsEyes = 2.6f * (float)(_b.Style.MinPupil * _b.Unit);
            return (x, y, Math.Min(Math.Max(scaled, fitsEyes), 0.8f * R) / R);
        }

        /// <summary>A side-on head's radius, grown if need be to hold one eye at the smallest pupil.</summary>
        public float SideHead(float h)
        {
            var fitsEye = 1.3f * (float)(_b.Style.MinPupil * _b.Unit);
            return Math.Min(Math.Max(h * R, fitsEye), 0.5f * R) / R;
        }

        public void FrontEyes(float x, float y, float h)
        {
            var size = h * R * 1.4f / MathF.Sqrt((float)_b.Style.EyeScale);
            EyesAt(C, x * R, y * R, size, 2, _t, _b);
        }

        public void SideEye(float x, float y, float h)
        {
            var size = h * R * 1.6f / MathF.Sqrt((float)_b.Style.EyeScale);
            EyesAt(C, x * R, y * R, size, 1, _t, _b, whiteOfPupil: 1.7f);
        }

        public SKPath OvalPath(float x, float y, float rx, float ry)
        {
            using var builder = new SKPathBuilder();
            builder.AddOval(new SKRect((x - rx) * R, (y - ry) * R, (x + rx) * R, (y + ry) * R), SKPathDirection.Clockwise);
            return builder.Detach();
        }

        public void Fill(Pen p, SKPath path) => C.DrawPath(path, p.FillPaint);

        public void Line(SKPath path) => C.DrawPath(path, Body.LinePaint);

        public void Oval(Pen p, float x, float y, float rx, float ry, float degrees = 0)
        {
            C.Save();
            C.Translate(x * R, y * R);
            C.RotateDegrees(degrees);
            var rect = new SKRect(-rx * R, -ry * R, rx * R, ry * R);
            C.DrawOval(rect, p.FillPaint);
            C.DrawOval(rect, p.LinePaint);
            C.Restore();
        }

        public void Circle(Pen p, float x, float y, float radius) => Oval(p, x, y, radius, radius);

        /// <summary>A small filled dot with no outline, at least the finest visible line across.</summary>
        public void Dot(Pen p, float x, float y, float radius) =>
            C.DrawCircle(x * R, y * R, Math.Max(radius * R, _b.MinDetail / 2), p.FillPaint);

        /// <summary>A polygon with every corner rounded off by <paramref name="corner"/> radii.</summary>
        public void Rounded(Pen p, float corner, params (float X, float Y)[] points)
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(points[0].X * R, points[0].Y * R);
            foreach (var (x, y) in points.Skip(1)) builder.LineTo(x * R, y * R);
            builder.Close();
            using var path = builder.Detach();
            using var effect = SKPathEffect.CreateCorner(corner * R);
            using var fill = new SKPaint { IsAntialias = true, Color = p.FillPaint.Color, PathEffect = effect };
            using var line = new SKPaint
            {
                IsAntialias = true, Color = p.LinePaint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = p.LinePaint.StrokeWidth,
                StrokeJoin = SKStrokeJoin.Round, PathEffect = effect,
            };
            C.DrawPath(path, fill);
            C.DrawPath(path, line);
        }

        /// <summary>A rounded band through two to four points, outlined either side: tails, necks, trunks, legs of birds.</summary>
        public void Ribbon(Pen p, float width, params (float X, float Y)[] points)
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(points[0].X * R, points[0].Y * R);
            switch (points.Length)
            {
                case 2: builder.LineTo(points[1].X * R, points[1].Y * R); break;
                case 3: builder.QuadTo(points[1].X * R, points[1].Y * R, points[2].X * R, points[2].Y * R); break;
                default: builder.CubicTo(points[1].X * R, points[1].Y * R, points[2].X * R, points[2].Y * R, points[3].X * R, points[3].Y * R); break;
            }
            using var path = builder.Detach();
            var inner = Math.Max(width * R, 1.4f * _b.MinDetail);
            using var outer = new SKPaint
            {
                IsAntialias = true, Color = p.LinePaint.Color, Style = SKPaintStyle.Stroke,
                StrokeWidth = inner + (2 * _b.Stroke), StrokeCap = SKStrokeCap.Round,
            };
            using var fill = new SKPaint
            {
                IsAntialias = true, Color = p.FillPaint.Color, Style = SKPaintStyle.Stroke, StrokeWidth = inner, StrokeCap = SKStrokeCap.Round,
            };
            C.DrawPath(path, outer);
            C.DrawPath(path, fill);
        }

        /// <summary>A plain line in the outline colour, through two or three points: a mouth, gills, whiskers.
        /// Half the outline's width, but never finer than the style's finest line.</summary>
        public void Stroke(params (float X, float Y)[] points)
        {
            using var thin = new SKPaint
            {
                IsAntialias = true, Color = Body.LinePaint.Color, Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(_b.Stroke / 2, _b.MinDetail), StrokeCap = SKStrokeCap.Round,
            };
            using var builder = new SKPathBuilder();
            builder.MoveTo(points[0].X * R, points[0].Y * R);
            if (points.Length == 2) builder.LineTo(points[1].X * R, points[1].Y * R);
            else builder.QuadTo(points[1].X * R, points[1].Y * R, points[2].X * R, points[2].Y * R);
            using var path = builder.Detach();
            C.DrawPath(path, thin);
        }

        /// <summary>Upright pill-shaped legs from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
        public void Legs(Pen p, float top, float bottom, float width, params float[] xs)
        {
            foreach (var x in xs)
            {
                var w = Math.Max(width * R, 2 * _b.MinDetail);
                var rect = new SKRect((x * R) - (w / 2), top * R, (x * R) + (w / 2), bottom * R);
                C.DrawRoundRect(rect, w / 2, w / 2, p.FillPaint);
                C.DrawRoundRect(rect, w / 2, w / 2, p.LinePaint);
            }
        }

        public void Hooves(float bottom, float width, params float[] xs)
        {
            foreach (var x in xs)
            {
                var w = Math.Max(width * R, 2 * _b.MinDetail);
                var rect = new SKRect((x * R) - (w / 2), (bottom * R) - (w * 0.6f), (x * R) + (w / 2), bottom * R);
                C.DrawRoundRect(rect, w * 0.3f, w * 0.3f, Dark.FillPaint);
            }
        }

        public void Inside(SKPath clip, Action draw)
        {
            C.Save();
            C.ClipPath(clip, antialias: true);
            draw();
            C.Restore();
        }

        /// <summary>Soft vertical bands across a body, as on a tabby cat or a striped fish.</summary>
        public void Stripes(SKPath body, int count, float from, float to)
        {
            if (count <= 0) return;
            using var stripe = new SKPaint { IsAntialias = true, Color = _b.Outline.WithAlpha(140) };
            var width = Math.Max(R * 0.11f, 1.4f * _b.MinDetail);
            Inside(body, () =>
            {
                for (var i = 0; i < count; i++)
                {
                    var x = (from + ((to - from) * (count == 1 ? 0.5f : i / (float)(count - 1)))) * R;
                    C.DrawRoundRect(new SKRect(x - (width / 2), -R, x + (width / 2), R), width / 2, width / 2, stripe);
                }
            });
        }

        public void Dispose()
        {
            foreach (var p in new[] { Body, Dark, Pale, Accent, Pink, Second, Ink }) p.Dispose();
        }

        private static Pen PenFor(ArtColor colour, float stroke) => new(Rgb(colour.Fill), Rgb(colour.Outline), stroke);
    }
}
