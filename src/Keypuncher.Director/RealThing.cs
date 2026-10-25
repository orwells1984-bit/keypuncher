using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>What kind of real thing a pick is. The kind of recipe follows from it, not from the model.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RealCategory>))]
public enum RealCategory
{
    /// <summary>A kind of animal alive today.</summary>
    Animal,
    /// <summary>An animal known from fossils or history: dinosaurs, mammoths, the dodo.</summary>
    ExtinctAnimal,
    /// <summary>A plant, flower, tree, fruit or vegetable.</summary>
    Plant,
    Vehicle,
    /// <summary>A real everyday object: a ball, a kite, a teapot.</summary>
    Object,
}

/// <summary>
/// The model's answer for one key family: a real thing, named, placed in a category, with one fact about it,
/// then drawn in the recipe vocabulary. The name and fact are never shown to or said to the child; the words
/// the child sees and hears still come only from the concept list.
/// </summary>
public sealed record RealThingPick
{
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    [JsonPropertyName("category")] public required RealCategory Category { get; init; }
    /// <summary>Where it lives or grows, or when it lived. Asked for so the model grounds the pick in something
    /// it knows; also shown to grown-ups beside each pick.</summary>
    [JsonPropertyName("fact")] public required string Fact { get; init; }
    [JsonPropertyName("shape")] public required Shape Shape { get; init; }
    [JsonPropertyName("lobes")] public int Lobes { get; init; }
    [JsonPropertyName("hue")] public int Hue { get; init; }
    [JsonPropertyName("size")] public double Size { get; init; }
    [JsonPropertyName("motion")] public required Motion Motion { get; init; }
    [JsonPropertyName("voice")] public required Voice Voice { get; init; }
    /// <summary>What it really grows or turns into, if anything: tadpole to frog, acorn to oak tree.</summary>
    [JsonPropertyName("growsInto")] public string? GrowsInto { get; init; }

    public static RecipeKind KindOf(RealCategory category) => category == RealCategory.Plant ? RecipeKind.Plant : RecipeKind.Creature;

    public static string Describe(RealCategory category) => category switch
    {
        RealCategory.Animal => "a kind of animal alive today",
        RealCategory.ExtinctAnimal => "an animal that lived long ago, known from fossils or history",
        RealCategory.Plant => "a plant, flower, tree, fruit or vegetable",
        RealCategory.Vehicle => "a vehicle",
        _ => "an everyday object",
    };
}

/// <summary>The JSON schema for <see cref="RealThingPick"/>. The name, category and fact come first, so the model
/// commits to what the thing is before it draws it.</summary>
public static class RealThingSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["subject", "category", "fact", "shape", "lobes", "hue", "size", "motion", "voice", "growsInto"],
          "properties": {
            "subject": { "type": "string", "minLength": 2, "maxLength": 24 },
            "category": { "type": "string", "enum": ["Animal", "ExtinctAnimal", "Plant", "Vehicle", "Object"] },
            "fact": { "type": "string", "minLength": 8, "maxLength": 100 },
            "shape": { "type": "string", "enum": ["Blob", "Petal", "Star", "Wave", "Cloud", "Stripe", "DotField",
              "Cat", "Dog", "Fish", "Bird", "Cow", "Sheep", "Horse", "Duck", "Elephant", "Rabbit", "Bear", "Mouse",
              "Rex", "LongNeck", "Triceratops", "Stegosaurus",
              "Whale", "Dolphin", "Shark", "Octopus", "Crab", "Turtle", "Seal", "Seahorse", "Starfish", "Jellyfish"] },
            "lobes": { "type": "integer", "minimum": 1, "maximum": 12 },
            "hue": { "type": "integer", "minimum": 0, "maximum": 359 },
            "size": { "type": "number", "minimum": 0.1, "maximum": 1.0 },
            "motion": {
              "type": "object",
              "additionalProperties": false,
              "required": ["type", "speed"],
              "properties": {
                "type": { "type": "string", "enum": ["Still", "Hop", "Drift", "Sway", "Spin"] },
                "speed": { "type": "number", "minimum": 0.0, "maximum": 1.0 }
              }
            },
            "voice": {
              "type": "object",
              "additionalProperties": false,
              "required": ["wave", "attack", "decay", "scale", "root"],
              "properties": {
                "wave": { "type": "string", "enum": ["Sine", "Triangle", "Square", "Saw", "Noise"] },
                "attack": { "type": "number", "minimum": 0.0, "maximum": 0.5 },
                "decay": { "type": "number", "minimum": 0.05, "maximum": 2.0 },
                "scale": { "type": "string", "enum": ["pentatonic", "major", "lydian", "whole-tone"] },
                "root": { "type": "string", "pattern": "^[A-G]#?[3-6]$" }
              }
            },
            "growsInto": { "type": ["string", "null"], "maxLength": 24 }
          }
        }
        """;

    public static JsonElement Element { get; } = JsonDocument.Parse(Json).RootElement.Clone();

    /// <summary>The schema for one request: the category enum holds only the category asked for, and the shape
    /// enum only shapes that thing could be, so the model cannot answer a plant family with an animal or draw a
    /// bus as an elephant. Ollama enforces enums at decode time, so these are held, not just asked for.</summary>
    public static JsonElement For(DirectorRequest request)
    {
        var root = JsonNode.Parse(Json)!.AsObject();
        var props = root["properties"]!.AsObject();
        var category = request.GrowInto is null ? request.Category : request.Kind == RecipeKind.Plant ? RealCategory.Plant : RealCategory.Animal;
        props["category"]!["enum"] = request.GrowInto is null
            ? new JsonArray(category.ToString())
            : new JsonArray([.. Enum.GetValues<RealCategory>().Where(c => RealThingPick.KindOf(c) == request.Kind).Select(c => (JsonNode)c.ToString())]);
        props["shape"]!["enum"] = new JsonArray([.. ShapesFor(category, request.GrowInto is not null).Select(s => (JsonNode)s.ToString())]);
        return JsonDocument.Parse(root.ToJsonString()).RootElement.Clone();
    }

    /// <summary>Shapes a thing of <paramref name="category"/> may be drawn with.</summary>
    public static IEnumerable<Shape> ShapesFor(RealCategory category, bool anyAnimal = false) =>
        Enum.GetValues<Shape>().Where(s => category switch
        {
            RealCategory.ExtinctAnimal => true,
            RealCategory.Animal => anyAnimal || s is not (Shape.Rex or Shape.LongNeck or Shape.Triceratops or Shape.Stegosaurus),
            _ => !s.IsAnimal(),
        });
}
