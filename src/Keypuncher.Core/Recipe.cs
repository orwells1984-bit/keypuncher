using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>
/// A Tier A "sprite recipe": a small, parameterised description of a thing in the world
/// that the text LLM writes and the renderer and synth turn into pixels and sound.
/// The vocabulary is deliberately narrow so that a 4B model can fill it in reliably.
/// </summary>
public sealed record Recipe
{
    [JsonPropertyName("kind")] public required RecipeKind Kind { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("body")] public required Body Body { get; init; }
    [JsonPropertyName("eyes")] public int Eyes { get; init; }
    [JsonPropertyName("motion")] public required Motion Motion { get; init; }
    [JsonPropertyName("voice")] public required Voice Voice { get; init; }
    [JsonPropertyName("growsInto")] public string? GrowsInto { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<RecipeKind>))]
public enum RecipeKind { Creature, Plant, Effect }

[JsonConverter(typeof(JsonStringEnumConverter<Shape>))]
public enum Shape { Blob, Petal, Star, Wave, Cloud, Stripe, DotField }

[JsonConverter(typeof(JsonStringEnumConverter<MotionType>))]
public enum MotionType { Still, Hop, Drift, Sway, Spin, Burst }

[JsonConverter(typeof(JsonStringEnumConverter<Waveform>))]
public enum Waveform { Sine, Triangle, Square, Saw, Noise }

public sealed record Body(
    [property: JsonPropertyName("shape")] Shape Shape,
    [property: JsonPropertyName("lobes")] int Lobes,
    [property: JsonPropertyName("hue")] int Hue,
    [property: JsonPropertyName("size")] double Size);

public sealed record Motion(
    [property: JsonPropertyName("type")] MotionType Type,
    [property: JsonPropertyName("speed")] double Speed);

public sealed record Voice(
    [property: JsonPropertyName("wave")] Waveform Wave,
    [property: JsonPropertyName("attack")] double Attack,
    [property: JsonPropertyName("decay")] double Decay,
    [property: JsonPropertyName("scale")] string Scale,
    [property: JsonPropertyName("root")] string Root);
