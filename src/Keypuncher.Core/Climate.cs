using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>The kind of weather a place has through the year. With the month and the hemisphere it says which weather
/// is usual there now, so the director does not bring snow to a desert, or to Iceland in July.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Climate>))]
public enum Climate
{
    /// <summary>Ice and snow most of the year: Svalbard, the top of a glacier.</summary>
    Polar,
    /// <summary>Long snowy winters and short summers: Lapland, the Icelandic highlands.</summary>
    Subarctic,
    /// <summary>High mountains: snowy winters, and snow now and then even in summer.</summary>
    Mountain,
    /// <summary>Cold snowy winters and warm summers: inland Europe.</summary>
    Continental,
    /// <summary>Cool, windy and wet all year, snowy in winter: the coasts and lowlands of Iceland.</summary>
    Subpolar,
    /// <summary>Mild and rainy all year, a little snow in midwinter: England.</summary>
    Temperate,
    /// <summary>Hot dry summers and mild rainy winters: Tuscany.</summary>
    Mediterranean,
    /// <summary>Warm all year and never snowy: Sydney.</summary>
    Subtropical,
    /// <summary>Hot and dry, rain only now and then, snow almost never: the Sahara.</summary>
    HotDesert,
    /// <summary>A desert by a cold sea, where fog rolls in but rain hardly ever falls: the Namib.</summary>
    FoggyDesert,
    /// <summary>Hot all year, with a rainy season and a dry season.</summary>
    Savanna,
    /// <summary>Hot and wet all year: the Amazon.</summary>
    Rainforest,
}

/// <summary>How often a kind of weather comes at a place in a given month.</summary>
public enum Likelihood
{
    Never,
    Rare,
    Common,
}

/// <summary>Which weather is usual where and when, written down here rather than asked of a small model, which
/// chose snow three times running for Wadi Rum.</summary>
public static class Climates
{
    /// <summary>The chance that a step may also choose weather that is rare there and then. Snow out of season, or rain
    /// in the desert, is not impossible, but it should be very rare.</summary>
    public const double RareChance = 1.0 / 20;

    /// <summary>How many months either side of midwinter snow is usual; up to two months further on it is rare, and
    /// beyond that it never comes. Below zero, snow is never usual: -1 is rare only in the three winter months, -2 rare
    /// only in midwinter itself, and <see cref="int.MinValue"/> never.</summary>
    private static int SnowReach(Climate c) => c switch
    {
        Climate.Polar or Climate.Mountain => 4,
        Climate.Subarctic => 3,
        Climate.Continental or Climate.Subpolar => 2,
        Climate.Temperate => 1,
        Climate.Mediterranean => -1,
        Climate.HotDesert => -2,
        _ => int.MinValue,
    };

    /// <summary>Whether snow ever falls in such a climate.</summary>
    public static bool CanSnow(Climate c) => SnowReach(c) != int.MinValue;

    /// <summary>Months from local midwinter, 0 to 6: January in the north and July in the south are 0.</summary>
    public static int FromMidwinter(int month, bool south)
    {
        var d = Math.Abs(month - (south ? 7 : 1));
        return Math.Min(d, 12 - d);
    }

    /// <summary>The season there: "winter", "spring", "summer" or "autumn".</summary>
    public static string Season(int month, bool south)
    {
        var local = south ? (month + 5) % 12 + 1 : month;
        return local switch
        {
            12 or 1 or 2 => "winter",
            >= 3 and <= 5 => "spring",
            >= 6 and <= 8 => "summer",
            _ => "autumn",
        };
    }

    /// <summary>How often <paramref name="w"/> comes in such a climate in <paramref name="month"/> (1 to 12). A rainbow
    /// is as common as rain, and still only ever follows it.</summary>
    public static Likelihood Odds(Climate c, bool south, int month, Weather w)
    {
        var d = FromMidwinter(month, south);
        return w switch
        {
            Weather.Snow => SnowReach(c) switch
            {
                int.MinValue => Likelihood.Never,
                var reach when d <= reach => Likelihood.Common,
                var reach when reach >= 0 && d <= reach + 2 => Likelihood.Rare,
                -1 when d <= 1 => Likelihood.Rare,
                -2 when d == 0 => Likelihood.Rare,
                _ => Likelihood.Never,
            },
            Weather.Rain or Weather.Rainbow => c switch
            {
                Climate.HotDesert or Climate.FoggyDesert => Likelihood.Rare,
                // Winter brings snow instead.
                Climate.Polar or Climate.Subarctic when d <= 2 => Likelihood.Rare,
                // The dry summer, and the savanna's dry season in its winter.
                Climate.Mediterranean when d >= 5 => Likelihood.Rare,
                Climate.Savanna when d <= 2 => Likelihood.Rare,
                _ => Likelihood.Common,
            },
            Weather.Fog => c is Climate.HotDesert or Climate.Savanna ? Likelihood.Rare : Likelihood.Common,
            _ => Likelihood.Common,
        };
    }

    /// <summary>Whether <paramref name="w"/> may come there now: always if it is common, only when
    /// <paramref name="rare"/> allows it if it is rare.</summary>
    public static bool Allows(Climate c, bool south, int month, Weather w, bool rare) =>
        Odds(c, south, month, w) switch
        {
            Likelihood.Common => true,
            Likelihood.Rare => rare,
            _ => false,
        };
}
