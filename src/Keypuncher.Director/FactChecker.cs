using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
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
    /// offered as an extinct animal. A vehicle and an everyday object count as each other: the bench found the
    /// checker calls a boat an Object and a sledge a Vehicle as often as not, and either is drawn the same way.</summary>
    public bool Passes(RealCategory claimed) => Verdict == "real" && Enum.TryParse<RealCategory>(Kind, out var kind) && SameKind(kind, claimed);

    public static bool SameKind(RealCategory a, RealCategory b) => a == b || (IsThing(a) && IsThing(b));

    private static bool IsThing(RealCategory c) => c is RealCategory.Vehicle or RealCategory.Object;

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
/// <param name="variant">How the checker is asked; the toy uses <see cref="PromptVariant.Baseline"/>, the bench tries others.</param>
public sealed class FactChecker(ILanguageModel model, PromptVariant? variant = null)
{
    private readonly PromptVariant _variant = variant ?? PromptVariant.Baseline;

    /// <summary>Whether a name in <see cref="RealThingRules.WellKnownKind"/> (a jeep, a woolly rhinoceros) passes without
    /// a call. The toy trusts the list; the bench switches it off to measure the model alone. The small model's one
    /// steady blind spot is ordinary vehicles and Ice Age animals, which it calls "unsure".</summary>
    public bool TrustWellKnown { get; init; } = true;

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
    private const string Elements = "\"Sea\", \"FreshWater\", \"Land\", \"Air\"";

    public const string FitSchemaJson = $$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["placeContinent", "placeClimate", "thingContinents", "thingClimates", "thingElements"],
          "properties": {
            "placeContinent": { "type": "string", "enum": [{{Continents}}] },
            "placeClimate": { "type": "string", "enum": [{{Climates}}] },
            "thingContinents": { "type": "array", "minItems": 1, "maxItems": 7, "items": { "type": "string", "enum": [{{Continents}}] } },
            "thingClimates": { "type": "array", "minItems": 1, "maxItems": 5, "items": { "type": "string", "enum": [{{Climates}}] } },
            "thingElements": { "type": "array", "minItems": 1, "maxItems": 4, "items": { "type": "string", "enum": [{{Elements}}] } }
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
            thingElements: where a {subject} {lives}: in the Sea, in FreshWater (rivers, lakes, ponds), on Land, or in the Air; every one that is true.
            """;
    }

    /// <summary>Whether a living animal or plant could be found <paramref name="here"/> ("at Lake Bled, Slovenia"):
    /// the place's continent and climate must both be among the thing's, and where the scene's
    /// <paramref name="landscape"/> is known, the thing's element must fit it (<see cref="Habitat"/>): no shark at a
    /// lake. <paramref name="shape"/> is how the pick is drawn, which settles the element for the sea shapes. No answer is a no.</summary>
    public async Task<bool> FitsAsync(string subject, RealCategory category, string here, Landscape? landscape = null, Shape? shape = null, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, FitPrompt(subject, category, here), FitSchema, cancellationToken);
        return Fits(result.Json?.RootElement, landscape, subject, shape);
    }

    /// <summary>Whether a found-there answer fits: continent and climate as <see cref="Fits(JsonElement?)"/>, and the
    /// thing's element in the landscape.</summary>
    public static bool Fits(JsonElement? answer, Landscape? landscape, string subject, Shape? shape = null) =>
        Fits(answer) && Habitat.Fits(Habitat.Of(subject, shape, answer), landscape);

    /// <summary>Whether a found-there answer puts the place's continent and climate among the thing's.</summary>
    public static bool Fits(JsonElement? answer)
    {
        if (answer is not { ValueKind: JsonValueKind.Object } a) return false;
        static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool Among(JsonElement e, string name, string? value) => value is not null && e.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Array && v.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == value);
        return Among(a, "thingContinents", Text(a, "placeContinent")) && Among(a, "thingClimates", Text(a, "placeClimate"));
    }

    /// <summary>The gentler prompt for a second look at an animal or plant the strict first pass was unsure of.</summary>
    public const string SecondLookSystem = """
        You know the world's animals and plants well, common and obscure. Answer only with JSON that matches
        the schema.
        """;

    public static string SecondLookPrompt(string subject, RealCategory claimed) =>
        $"""
        Name: "{subject}"
        Is this a real kind of {(claimed == RealCategory.Plant ? "plant, flower, tree, fruit or vegetable" : "animal")}, living somewhere in the world today or known from fossils?

        whatItIs: say in a few plain words what it is, or "unknown".
        verdict: "real" if you recognise the name as a real kind of {(claimed == RealCategory.Plant ? "plant" : "animal")}, even a little-known one;
        "fictional" if it comes from myths, stories, films, games or toys, is a character or a brand, or is a
        made-up or blended name; "unsure" only if you have never heard of it.
        kind: "Animal" for a kind of animal alive somewhere today; "ExtinctAnimal" for one that has died out;
        "Plant" for a plant, flower, tree, fruit or vegetable; "Vehicle"; "Object"; "None" if it is not real.
        growsInto: "none".
        """;

    /// <summary>A4: the gentle question asked first and only, for any kind of thing, with the growsInto claim kept.</summary>
    public static string GentlePrompt(string subject, string? growsInto) =>
        $"""
        Name: "{subject}"
        Said to grow or turn into: {(growsInto is null ? "nothing" : $"\"{growsInto}\"")}

        whatItIs: say in a few plain words what this is, or "unknown".
        verdict: "real" if you recognise the name as a real kind of animal, plant, vehicle or everyday object, even a
        little-known one, or an animal known from fossils or history; "fictional" if it comes from myths, fairy
        tales, stories, films, cartoons, games or toys, is a character or a brand, or is a made-up or blended name;
        "unsure" only if you have never heard of it.
        kind: "Animal" for a kind of animal that is still alive somewhere today; "ExtinctAnimal" only for one that
        has died out and lives nowhere today (dinosaurs, the woolly mammoth, the dodo); "Plant" for a plant, flower,
        tree, fruit or vegetable; "Vehicle"; "Object" for an everyday object; "None" if it is not real.
        growsInto: "true" if a {subject} really grows or turns into {(growsInto is null ? "that" : $"a {growsInto}")} in nature,
        "false" if not, "none" if nothing was said.
        """;

    /// <summary>The system and user prompt for the first look at <paramref name="subject"/>, as the variant arranges them.</summary>
    public (string System, string User) FirstLook(string subject, string? growsInto) =>
        _variant.GentleChecker ? _variant.Arrange(SecondLookSystem, GentlePrompt(subject, growsInto)) : _variant.Arrange(System, Prompt(subject, growsInto));

    /// <param name="claimed">What the pick says the thing is. The checker is still not told; it only decides whether
    /// an "unsure" gets a second, gentler look, which an animal or a plant does: the strict first pass turned
    /// away a salamander and a mudfish as "unknown", and a made-up animal name rarely survives the name rules
    /// and the first pass both. With <see cref="PromptVariant.GentleChecker"/> the first look is already the gentle one, so there is no second.</param>
    public async Task<FactCheck> CheckAsync(string subject, string? growsInto, RealCategory? claimed = null, CancellationToken cancellationToken = default)
    {
        // A well-known name of the kind claimed needs no call; a growth claim on it is not confirmed, so it is dropped.
        if (TrustWellKnown && claimed is { } c && RealThingRules.WellKnownKind(subject) is { } known && FactCheck.SameKind(known, c))
            return new FactCheck("well known", "real", known.ToString(), growsInto is null ? "none" : "false");
        var (system, user) = FirstLook(subject, growsInto);
        var first = await AskAsync(system, user, cancellationToken);
        if (_variant.GentleChecker) return first;
        if (first.Verdict != "unsure" || claimed is not (RealCategory.Animal or RealCategory.Plant)) return first;
        var (system2, user2) = _variant.Arrange(SecondLookSystem, SecondLookPrompt(subject, claimed.Value));
        var second = await AskAsync(system2, user2, cancellationToken);
        // The second look may only turn "unsure" into a verdict; the growsInto claim stays with the first pass.
        return second.Verdict == "unsure" ? first : second with { GrowsInto = first.GrowsInto };
    }

    private async Task<FactCheck> AskAsync(string system, string prompt, CancellationToken cancellationToken)
    {
        var result = await model.GenerateJsonAsync(system, prompt, Schema, cancellationToken);
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
