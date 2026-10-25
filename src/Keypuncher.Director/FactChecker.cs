using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>The checker's answer about one pick.</summary>
/// <param name="WhatItIs">The checker's own few words on what the thing is; asked first, so it has to recall the
/// thing before it judges it.</param>
/// <param name="Verdict">"real", "fictional" or "unsure".</param>
/// <param name="RightCategory">Whether it really is the kind of thing the pick says.</param>
/// <param name="GrowsInto">"true", "false" or "none" for the pick's growsInto claim.</param>
public sealed record FactCheck(
    [property: JsonPropertyName("whatItIs")] string WhatItIs,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("rightCategory")] bool RightCategory,
    [property: JsonPropertyName("growsInto")] string GrowsInto)
{
    /// <summary>Only a sure "real" in the right category passes; "unsure" counts as no.</summary>
    public bool Passed => Verdict == "real" && RightCategory;

    public bool GrowsIntoConfirmed => GrowsInto == "true";

    public static FactCheck Failed(string why) => new(why, "unsure", false, "none");
}

/// <summary>
/// The second opinion on whether a pick is real. A separate, cold (temperature 0) call that sees only the name,
/// the category and the growsInto claim, not the drawing or the author's fact, and is told that "unsure" is
/// the safe answer. It is the same small model as the author, so its mistakes are not independent; it catches
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
          "required": ["whatItIs", "verdict", "rightCategory", "growsInto"],
          "properties": {
            "whatItIs": { "type": "string", "maxLength": 80 },
            "verdict": { "type": "string", "enum": ["real", "fictional", "unsure"] },
            "rightCategory": { "type": "boolean" },
            "growsInto": { "type": "string", "enum": ["true", "false", "none"] }
          }
        }
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse(SchemaJson).RootElement.Clone();
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Prompt(string subject, RealCategory category, string? growsInto) =>
        $"""
        Name: "{subject}"
        Said to be: {RealThingPick.Describe(category)}
        Said to grow or turn into: {(growsInto is null ? "nothing" : $"\"{growsInto}\"")}

        whatItIs: say in a few plain words what this is, or "unknown".
        verdict: "real" only if it is a real kind of animal, plant, vehicle or everyday object that exists today,
        or an animal known from fossils or history. "fictional" if it comes from myths, fairy tales, stories,
        films, cartoons, games or toys, if it is a character or a brand, or if the name is made up or blends two
        things. "unsure" if you do not know it well.
        rightCategory: true only if it really is {RealThingPick.Describe(category)}.
        growsInto: "true" if a {subject} really grows or turns into {(growsInto is null ? "that" : $"a {growsInto}")} in nature,
        "false" if not, "none" if nothing was said.
        """;

    public async Task<FactCheck> CheckAsync(string subject, RealCategory category, string? growsInto, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, Prompt(subject, category, growsInto), Schema, cancellationToken);
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
