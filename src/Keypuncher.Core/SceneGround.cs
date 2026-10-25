namespace Keypuncher.Core;

/// <summary>
/// The backdrop's colours for a <see cref="Scene"/> at one <see cref="ArtStyle"/>. Each of the style's four ground
/// colours keeps its brightness (its luminance) and only takes on the scene's hue: sand for a beach, gold for the
/// savanna, a peach dawn. So every scene keeps the contrast the Art Rules set for that level, and the sky and
/// hills stay as close in tone as the style's own. Snow and fog only ever lighten.
/// </summary>
public static class SceneGround
{
    /// <summary>A hue in degrees and a saturation (0..1) to give a colour, keeping its luminance; null keeps it.</summary>
    private readonly record struct Tint(double Hue, double Saturation);

    public static Ground For(ArtStyle style, Scene scene)
    {
        var g = style.Ground;
        var (top, bottom) = Sky(scene.TimeOfDay);
        var (far, near) = Hills(scene.Landscape);
        var skyTop = Apply(g.SkyTop, top);
        var skyBottom = Apply(g.SkyBottom, bottom);
        var farHill = Apply(g.FarHill, far);
        var nearHill = Apply(g.NearHill, near);

        // Grey weather takes most of the colour out of the sky, keeping its brightness.
        var grey = scene.Weather switch { Weather.Cloudy => 0.55, Weather.Rain => 0.35, Weather.Fog => 0.3, Weather.Snow => 0.45, _ => 1 };
        if (grey < 1)
        {
            skyTop = Desaturate(skyTop, grey);
            skyBottom = Desaturate(skyBottom, grey);
        }

        // Snow lies on the hills and fog hides them: both lift them towards white.
        var lift = (scene.Landscape == Landscape.SnowyHills ? 0.45 : 0) + scene.Weather switch { Weather.Snow => 0.4, Weather.Fog => 0.35, _ => 0 };
        if (lift > 0)
        {
            farHill = Lighten(farHill, Math.Min(0.7, lift));
            nearHill = Lighten(nearHill, Math.Min(0.6, lift * 0.85));
        }
        return new Ground(skyTop, skyBottom, farHill, nearHill);
    }

    private static (Tint? Top, Tint? Bottom) Sky(TimeOfDay time) => time switch
    {
        TimeOfDay.Dawn => (new Tint(265, 0.35), new Tint(22, 0.75)),
        TimeOfDay.Midday => (new Tint(204, 0.75), new Tint(200, 0.45)),
        TimeOfDay.Afternoon => (new Tint(208, 0.6), new Tint(44, 0.7)),
        TimeOfDay.Evening => (new Tint(250, 0.4), new Tint(14, 0.8)),
        _ => (null, null),
    };

    private static (Tint? Far, Tint? Near) Hills(Landscape landscape) => landscape switch
    {
        Landscape.Farm => (new Tint(92, 0.4), new Tint(70, 0.5)),
        Landscape.Forest => (new Tint(140, 0.35), new Tint(122, 0.4)),
        Landscape.Jungle => (new Tint(150, 0.5), new Tint(132, 0.55)),
        Landscape.Mountains => (new Tint(215, 0.18), new Tint(110, 0.3)),
        Landscape.Beach => (new Tint(194, 0.6), new Tint(42, 0.6)),
        Landscape.Lake => (new Tint(204, 0.5), new Tint(108, 0.35)),
        Landscape.Desert => (new Tint(34, 0.55), new Tint(40, 0.65)),
        Landscape.Savanna => (new Tint(52, 0.45), new Tint(44, 0.55)),
        Landscape.SnowyHills => (new Tint(212, 0.15), new Tint(205, 0.12)),
        Landscape.LavaField => (new Tint(100, 0.12), new Tint(86, 0.22)),
        _ => (null, null),
    };

    private static uint Apply(uint rgb, Tint? tint) => tint is { } t ? WithLuminance(t.Hue, t.Saturation, Luminance(rgb)) : rgb;

    /// <summary>The colour of hue <paramref name="hue"/> and HSL saturation <paramref name="saturation"/> whose
    /// luminance is <paramref name="target"/>, found by bisecting on HSL lightness (luminance rises with it).</summary>
    public static uint WithLuminance(double hue, double saturation, double target)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 30; i++)
        {
            var mid = (lo + hi) / 2;
            if (Luminance(FromHsl(hue, saturation, mid)) < target) lo = mid;
            else hi = mid;
        }
        return FromHsl(hue, saturation, (lo + hi) / 2);
    }

    /// <summary>Keeps <paramref name="keep"/> of a colour's saturation and all of its luminance.</summary>
    private static uint Desaturate(uint rgb, double keep)
    {
        var (h, s, _) = ToHsl(rgb);
        return WithLuminance(h, s * keep, Luminance(rgb));
    }

    /// <summary>Mixes a colour towards white by <paramref name="amount"/>.</summary>
    private static uint Lighten(uint rgb, double amount)
    {
        uint Channel(int shift) => (uint)Math.Round(((rgb >> shift) & 0xFF) + ((255 - ((rgb >> shift) & 0xFF)) * amount));
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    /// <summary>WCAG relative luminance of 0xRRGGBB.</summary>
    public static double Luminance(uint rgb)
    {
        static double Linear(uint c)
        {
            var s = c / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Linear((rgb >> 16) & 0xFF)) + (0.7152 * Linear((rgb >> 8) & 0xFF)) + (0.0722 * Linear(rgb & 0xFF));
    }

    private static uint FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var hp = (((h % 360) + 360) % 360) / 60;
        var x = c * (1 - Math.Abs((hp % 2) - 1));
        var (r, g, b) = hp switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        var m = l - (c / 2);
        uint Byte(double v) => (uint)Math.Clamp(Math.Round((v + m) * 255), 0, 255);
        return (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
    }

    private static (double H, double S, double L) ToHsl(uint rgb)
    {
        double r = ((rgb >> 16) & 0xFF) / 255.0, g = ((rgb >> 8) & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        var d = max - min;
        if (d == 0) return (0, 0, l);
        var s = d / (1 - Math.Abs((2 * l) - 1));
        var h = max == r ? ((g - b) / d) % 6 : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (((h * 60) + 360) % 360, Math.Clamp(s, 0, 1), l);
    }
}
