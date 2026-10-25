using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>The kind of country behind everything; it sets the hills' shape and colour.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Landscape>))]
public enum Landscape
{
    Meadow,
    Farm,
    Forest,
    Jungle,
    Mountains,
    Beach,
    Lake,
    Desert,
    Savanna,
    SnowyHills,
    /// <summary>Old lava under moss, as in much of Iceland.</summary>
    LavaField,
}

/// <summary>The light in the sky. There is no night: the goodnight is what darkens the scene.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeOfDay>))]
public enum TimeOfDay
{
    Dawn,
    Morning,
    Midday,
    Afternoon,
    Evening,
}

[JsonConverter(typeof(JsonStringEnumConverter<Weather>))]
public enum Weather
{
    Clear,
    Cloudy,
    Rain,
    Snow,
    Fog,
    /// <summary>Only ever straight after rain.</summary>
    Rainbow,
}

/// <summary>
/// Where the toy's world is, and in what light and weather: the backdrop behind every thing. The director moves
/// it a step at a time to real places on Earth, with real weather and time going forward, and the renderer fades
/// from one to the next slowly so that a change is never a jump. The place's name and fact are for grown-ups only.
/// </summary>
/// <param name="Place">A real place, such as "Reynisfjara, Iceland"; null for the starting meadow.</param>
/// <param name="Fact">One true sentence about the place, for grown-ups.</param>
/// <param name="Snows">Whether it ever snows at <see cref="Place"/>, as the place check said.</param>
public sealed record Scene(
    Landscape Landscape = Landscape.Meadow,
    TimeOfDay TimeOfDay = TimeOfDay.Morning,
    Weather Weather = Weather.Clear,
    string? Place = null,
    string? Fact = null,
    bool Snows = true)
{
    public static Scene Default { get; } = new();

    /// <summary>Landscapes too hot for snow anywhere.</summary>
    public static bool Hot(Landscape l) => l is Landscape.Desert or Landscape.Jungle or Landscape.Savanna;

    /// <summary>The time of day that follows <paramref name="t"/>; evening goes on to the next day's dawn.</summary>
    public static TimeOfDay Next(TimeOfDay t) => t == TimeOfDay.Evening ? TimeOfDay.Dawn : t + 1;

    /// <summary>Where such a place is, in a few words, for prompts: "on a beach".</summary>
    public static string Describe(Landscape l) => l switch
    {
        Landscape.Meadow => "in a meadow",
        Landscape.Farm => "on a farm",
        Landscape.Forest => "in a forest",
        Landscape.Jungle => "in a jungle",
        Landscape.Mountains => "in the mountains",
        Landscape.Beach => "on a beach",
        Landscape.Lake => "by a lake",
        Landscape.Desert => "in the desert",
        Landscape.Savanna => "on the savanna",
        Landscape.SnowyHills => "in snowy hills",
        _ => "on a mossy lava field",
    };

    /// <summary>For grown-ups: "Reynisfjara, Iceland: a beach, in the afternoon, cloudy".</summary>
    public override string ToString() =>
        $"{Place ?? "a meadow"}: {Describe(Landscape)}, {TimeOfDay.ToString().ToLowerInvariant()}, {Weather switch
        {
            Weather.Clear => "clear sky",
            Weather.Rainbow => "a rainbow",
            _ => Weather.ToString().ToLowerInvariant(),
        }}";
}
