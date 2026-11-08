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

/// <summary>The abstract forms first, then real animals and dinosaurs drawn so a child knows them from a
/// picture book. New shapes go at the end: saved worlds store the name, but keep the order readable.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Shape>))]
public enum Shape
{
    Blob, Petal, Star, Wave, Cloud, Stripe, DotField,
    // Pictures of things, drawn like the animals below rather than built from the abstract shapes above.
    Apple, Moon, House, Boat,
    Cat, Dog, Fish, Bird, Cow, Sheep, Horse, Duck, Elephant, Rabbit, Bear, Mouse,
    Rex, LongNeck, Triceratops, Stegosaurus,
    Whale, Dolphin, Shark, Octopus, Crab, Turtle, Seal, Seahorse, Starfish, Jellyfish,
    // Added after the review of how easily each drawing is recognised (Nov 2026): the sun, a ball, an egg and a car
    // are pictures; the butterfly is an animal.
    Sun, Ball, Egg, Car, Butterfly,
    // A star as a child sees one at night: a small glowing point that twinkles, not the five-pointed symbol.
    Starlight,
}

public static class Shapes
{
    /// <summary>A real animal or dinosaur: always alive, so always a creature with a face.</summary>
    public static bool IsAnimal(this Shape shape) => shape is (>= Shape.Cat and <= Shape.Jellyfish) or Shape.Butterfly;

    /// <summary>A drawn picture of one particular thing (an apple, the moon, a house, a boat, the sun, a ball, an egg,
    /// a car): never an animal,
    /// and not a shape the director may use for any thing it likes.</summary>
    public static bool IsPicture(this Shape shape) => shape is (>= Shape.Apple and < Shape.Cat) or Shape.Sun or Shape.Ball or Shape.Egg or Shape.Car or Shape.Starlight;

    /// <summary>One of the abstract shapes the invented things and the director's things are built from.</summary>
    public static bool IsAbstract(this Shape shape) => shape < Shape.Apple;
}

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
