using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>The checker's answer about one place.</summary>
/// <param name="WhatItIs">The checker's own few words on where the place is, asked first so it recalls it.</param>
/// <param name="Verdict">"real", "fictional" or "unsure".</param>
/// <param name="Landscapes">The kinds of country found there, in the checker's own words from the list.</param>
/// <param name="Snows">"true", "false" or "unsure": whether it ever snows there.</param>
public sealed record PlaceCheck(
    [property: JsonPropertyName("whatItIs")] string WhatItIs,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("landscapes")] IReadOnlyList<Landscape> Landscapes,
    [property: JsonPropertyName("snows")] string Snows)
{
    /// <summary>Only a sure yes allows snow.</summary>
    public bool SnowsThere => Snows == "true";

    public static PlaceCheck Failed(string why) => new(why, "unsure", [], "unsure");
}

/// <summary>
/// The second opinion on a new place: a cold (temperature 0) call that sees only the place's name, not the
/// landscape, weather or fact the author gave, and names the landscapes found there itself. Like
/// <see cref="FactChecker"/> it is the same small model, so it catches made-up and story places better than a
/// real-sounding invention; every place is listed with its fact on the grown-ups' page.
/// </summary>
public sealed class PlaceChecker(ILanguageModel model)
{
    public const string System = """
        You check facts for a children's atlas. You are strict: any place you do not know well is "unsure".
        Answer only with JSON that matches the schema.
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["whatItIs", "verdict", "landscapes", "snows"],
          "properties": {
            "whatItIs": { "type": "string", "maxLength": 80 },
            "verdict": { "type": "string", "enum": ["real", "fictional", "unsure"] },
            "landscapes": { "type": "array", "maxItems": 4,
              "items": { "type": "string", "enum": [{{string.Join(", ", Enum.GetNames<Landscape>().Select(n => $"\"{n}\""))}}] } },
            "snows": { "type": "string", "enum": ["true", "false", "unsure"] }
          }
        }
        """).RootElement.Clone();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Prompt(string place) =>
        $"""
        Place: "{place}"

        whatItIs: say in a few plain words where on Earth this is, or "unknown".
        verdict: "real" only if it is a real place on Earth that you know. "fictional" if it comes from myths,
        stories, films, cartoons or games, or if the name is made up. "unsure" if you do not know it well.
        landscapes: the kinds of country really found at this place, one to four of: Meadow, Farm, Forest, Jungle,
        Mountains, Beach, Lake, Desert, Savanna, SnowyHills (hills under snow for much of the year), LavaField
        (old lava under moss). Empty if it is not real.
        snows: "true" if snow really falls there in some years, "false" if it never does, "unsure" if you do not know.
        """;

    public async Task<PlaceCheck> CheckAsync(string place, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, Prompt(place), Schema, cancellationToken);
        if (result.Json is null) return PlaceCheck.Failed("checker gave no JSON");
        try
        {
            return result.Json.RootElement.Deserialize<PlaceCheck>(Options) ?? PlaceCheck.Failed("checker gave nothing");
        }
        catch (JsonException)
        {
            return PlaceCheck.Failed("checker gave bad JSON");
        }
    }
}
