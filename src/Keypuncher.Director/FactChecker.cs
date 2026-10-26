using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>The checker's answer about one pick.</summary>
/// <param name="WhatItIs">The checker's own few words on what the thing is; asked first, so it has to recall the
/// thing before it judges it.</param>
/// <param name="Verdict">"real", "fictional" or "unsure".</param>
/// <param name="Kind">What kind of thing the checker says it is, as a <see cref="RealCategory"/> name or "None";
/// the checker is not told what the pick claims, so this is its own call, compared with the claim afterwards.</param>
/// <param name="GrowsInto">"true", "false" or "none" for the pick's growsInto claim.</param>
public sealed record FactCheck(
    [property: JsonPropertyName("whatItIs")] string WhatItIs,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("growsInto")] string GrowsInto)
{
    /// <summary>Only a sure "real" of the claimed kind passes; "unsure" counts as no, and so does a puffin
    /// offered as an extinct animal.</summary>
    public bool Passes(RealCategory claimed) => Verdict == "real" && Kind == claimed.ToString();

    public bool GrowsIntoConfirmed => GrowsInto == "true";

    public static FactCheck Failed(string why) => new(why, "unsure", "None", "none");
}

/// <summary>
/// The second opinion on whether a pick is real. A separate, cold (temperature 0) call that sees only the name
/// and the growsInto claim, not the claimed category, the drawing or the author's fact, and is told that
/// "unsure" is the safe answer. It names the kind of thing itself, which is then compared with the claim. It is the same small model as the author, so its mistakes are not independent; it catches
/// made-up and blended names well, and a plausible-sounding invention less well. The parent page lists every
/// pick with its fact so a grown-up can spot what gets through.
/// </summary>
public sealed class FactChecker(ILanguageModel model)
{
    public const string System = """
        You check facts for a children's picture encyclopedia. You are strict: anything you do not know well
        is "unsure". Answer only with JSON that matches the schema.
        """;

    public const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["whatItIs", "verdict", "kind", "growsInto"],
          "properties": {
            "whatItIs": { "type": "string", "maxLength": 80 },
            "verdict": { "type": "string", "enum": ["real", "fictional", "unsure"] },
            "kind": { "type": "string", "enum": ["Animal", "ExtinctAnimal", "Plant", "Vehicle", "Object", "None"] },
            "growsInto": { "type": "string", "enum": ["true", "false", "none"] }
          }
        }
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse(SchemaJson).RootElement.Clone();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Prompt(string subject, string? growsInto) =>
        $"""
        Name: "{subject}"
        Said to grow or turn into: {(growsInto is null ? "nothing" : $"\"{growsInto}\"")}

        whatItIs: say in a few plain words what this is, or "unknown".
        verdict: "real" only if it is a real kind of animal, plant, vehicle or everyday object that exists today,
        or an animal known from fossils or history. "fictional" if it comes from myths, fairy tales, stories,
        films, cartoons, games or toys, if it is a character or a brand, or if the name is made up or blends two
        things. "unsure" if you do not know it well.
        kind: "Animal" for a kind of animal that is still alive somewhere today; "ExtinctAnimal" only for one
        that has died out and lives nowhere today (dinosaurs, the woolly mammoth, the dodo); "Plant" for a plant,
        flower, tree, fruit or vegetable; "Vehicle"; "Object" for an everyday object; "None" if it is not real.
        growsInto: "true" if a {subject} really grows or turns into {(growsInto is null ? "that" : $"a {growsInto}")} in nature,
        "false" if not, "none" if nothing was said.
        """;

    private const string Continents = "\"Africa\", \"Asia\", \"Europe\", \"NorthAmerica\", \"SouthAmerica\", \"Oceania\", \"Antarctica\"";
    private const string Climates = "\"Tropical\", \"Dry\", \"Temperate\", \"Cold\", \"Polar\"";

    public const string FitSchemaJson = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["placeContinent", "placeClimate", "thingContinents", "thingClimates"],
          "properties": {
            "placeContinent": { "type": "string", "enum": [{{Continents}}] },
            "placeClimate": { "type": "string", "enum": [{{Climates}}] },
            "thingContinents": { "type": "array", "minItems": 1, "maxItems": 7, "items": { "type": "string", "enum": [{{Continents}}] } },
            "thingClimates": { "type": "array", "minItems": 1, "maxItems": 5, "items": { "type": "string", "enum": [{{Climates}}] } }
          }
        }
        """;

    private static readonly JsonElement FitSchema = JsonDocument.Parse(FitSchemaJson).RootElement.Clone();

    /// <summary>Asks for facts the small model knows well (which continent and climate the place has, and where
    /// and in what climates the thing is found) rather than for a verdict, which it would not give: asked "does a
    /// linden tree grow at Lake Bled?" it was unsure of nearly every real case, and refusing only on "no" let a
    /// gazelle through at a Bulgarian beach.</summary>
    public static string FitPrompt(string subject, RealCategory category, string here)
    {
        var (lives, kept) = category == RealCategory.Plant ? ("grows", "grown in gardens and fields") : ("lives", "kept by people");
        return $"""
            Name: "{subject}"
            Place: "{here}"

            placeContinent: the continent the place is on.
            placeClimate: the place's climate: Tropical (hot and wet), Dry (desert), Temperate, Cold (long snowy
            winters, mountains, subarctic) or Polar.
            thingContinents: every continent where a {subject} {lives} today, in the wild or {kept}.
            thingClimates: every climate a {subject} {lives} in.
            """;
    }

    /// <summary>Whether a living animal or plant could be found <paramref name="here"/> ("at Lake Bled, Slovenia"):
    /// the place's continent and climate must both be among the thing's. No answer is a no.</summary>
    public async Task<bool> FitsAsync(string subject, RealCategory category, string here, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, FitPrompt(subject, category, here), FitSchema, cancellationToken);
        return Fits(result.Json?.RootElement);
    }

    /// <summary>Whether a found-there answer puts the place's continent and climate among the thing's.</summary>
    public static bool Fits(JsonElement? answer)
    {
        if (answer is not { ValueKind: JsonValueKind.Object } a) return false;
        static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool Among(JsonElement e, string name, string? value) => value is not null && e.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Array && v.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == value);
        return Among(a, "thingContinents", Text(a, "placeContinent")) && Among(a, "thingClimates", Text(a, "placeClimate"));
    }

    public async Task<FactCheck> CheckAsync(string subject, string? growsInto, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, Prompt(subject, growsInto), Schema, cancellationToken);
        if (result.Json is null) return FactCheck.Failed("checker gave no JSON");
        try
        {
            return result.Json.RootElement.Deserialize<FactCheck>(Options) ?? FactCheck.Failed("checker gave nothing");
        }
        catch (JsonException)
        {
            return FactCheck.Failed("checker gave bad JSON");
        }
    }
}
