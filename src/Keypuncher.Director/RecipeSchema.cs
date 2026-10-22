using System.Text.Json;

namespace Keypuncher.Director;

/// <summary>
/// The JSON schema handed to the model as its output format. Kept as a literal so that
/// it can be read, diffed and pasted into a prompt-testing tool without running code.
/// Must stay in step with <see cref="Keypuncher.Core.Recipe"/>.
/// </summary>
public static class RecipeSchema
{
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["kind", "name", "body", "eyes", "motion", "voice"],
          "properties": {
            "kind": { "type": "string", "enum": ["Creature", "Plant", "Effect"] },
            "name": { "type": "string", "minLength": 2, "maxLength": 24 },
            "body": {
              "type": "object",
              "additionalProperties": false,
              "required": ["shape", "lobes", "hue", "size"],
              "properties": {
                "shape": { "type": "string", "enum": ["Blob", "Petal", "Star", "Wave", "Cloud", "Stripe", "DotField"] },
                "lobes": { "type": "integer", "minimum": 1, "maximum": 12 },
                "hue": { "type": "integer", "minimum": 0, "maximum": 359 },
                "size": { "type": "number", "minimum": 0.1, "maximum": 1.0 }
              }
            },
            "eyes": { "type": "integer", "minimum": 0, "maximum": 4 },
            "motion": {
              "type": "object",
              "additionalProperties": false,
              "required": ["type", "speed"],
              "properties": {
                "type": { "type": "string", "enum": ["Still", "Hop", "Drift", "Sway", "Spin", "Burst"] },
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
}
