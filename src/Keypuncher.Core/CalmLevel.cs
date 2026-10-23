using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>How busy the screen may get and how boldly it is drawn, by the child's age. See the Keypuncher
/// Calm Rules and Art Rules pages, and <see cref="CalmLimits"/> and <see cref="ArtStyle"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalmLevel>))]
public enum CalmLevel
{
    /// <summary>About 6 to 12 months.</summary>
    Baby,
    /// <summary>About 12 to 18 months; the default.</summary>
    Toddler,
    /// <summary>18 months and up.</summary>
    BigToddler,
}

/// <summary>
/// The limits one <see cref="CalmLevel"/> sets. Babies track one to four things at a time and learn from
/// seeing their own press cause a response, so only a few things stay awake and the rest go still.
/// </summary>
/// <param name="LiveThings">Things on screen before the oldest retires into the background.</param>
/// <param name="Moving">How many of the most recently woken things move at once.</param>
/// <param name="SettleSeconds">An untouched thing goes from fully awake to still in this time.</param>
/// <param name="NewThingsPerSecond">Steady rate of new things; presses beyond it wake an existing one.</param>
/// <param name="MaxMotionHz">Fastest idle motion, in cycles a second.</param>
/// <param name="HistoryOpacity">Opacity of the retired things painted behind everything.</param>
/// <param name="HistoryShown">How many of the most recently retired things are painted.</param>
/// <param name="CountUpTo">A digit up to this makes and counts that many things; above it, one thing.</param>
/// <param name="SessionMinutes">Default play time before the goodnight.</param>
public sealed record CalmLimits(
    int LiveThings,
    int Moving,
    double SettleSeconds,
    double NewThingsPerSecond,
    double MaxMotionHz,
    double HistoryOpacity,
    int HistoryShown,
    int CountUpTo,
    int SessionMinutes)
{
    public static readonly CalmLimits Baby = new(4, 1, 8, 0.7, 0.5, 0.12, 40, 3, 5);
    public static readonly CalmLimits Toddler = new(7, 2, 15, 1, 0.75, 0.20, 80, 5, 10);
    public static readonly CalmLimits BigToddler = new(12, 4, 30, 2, 1, 0.30, 150, 9, 15);

    public static CalmLimits For(CalmLevel level) => level switch
    {
        CalmLevel.Baby => Baby,
        CalmLevel.BigToddler => BigToddler,
        _ => Toddler,
    };
}
