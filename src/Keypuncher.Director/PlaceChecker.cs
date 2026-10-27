using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>The checker's answer about one place.</summary>
/// <param name="WhatItIs">The checker's own few words on where the place is, asked first so it recalls it.</param>
/// <param name="Verdict">"real", "fictional" or "unsure".</param>
/// <param name="Landscapes">The kinds of country found there, in the checker's own words from the list.</param>
/// <param name="Climate">The weather the place has through the year.</param>
/// <param name="Hemisphere">"north" or "south" of the equator, for which way round the seasons are.</param>
public sealed record PlaceCheck(
    [property: JsonPropertyName("whatItIs")] string WhatItIs,
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("landscapes")] IReadOnlyList<Landscape> Landscapes,
    [property: JsonPropertyName("climate")] Climate Climate = Climate.Temperate,
    [property: JsonPropertyName("hemisphere")] string Hemisphere = "north")
{
    public bool South => Hemisphere == "south";

    public static PlaceCheck Failed(string why) => new(why, "unsure", []);
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
          "required": ["whatItIs", "verdict", "landscapes", "climate", "hemisphere"],
          "properties": {
            "whatItIs": { "type": "string", "maxLength": 80 },
            "verdict": { "type": "string", "enum": ["real", "fictional", "unsure"] },
            "landscapes": { "type": "array", "maxItems": 4,
              "items": { "type": "string", "enum": [{{string.Join(", ", Enum.GetNames<Landscape>().Select(n => $"\"{n}\""))}}] } },
            "climate": { "type": "string", "enum": [{{string.Join(", ", Enum.GetNames<Climate>().Select(n => $"\"{n}\""))}}] },
            "hemisphere": { "type": "string", "enum": ["north", "south"] }
          }
        }
        """).RootElement.Clone();

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string Prompt(string place) =>
        $"""
        Place: "{place}"

        whatItIs: say in a few plain words where on Earth this is, or "unknown".
        verdict: "real" only if it is a real, named place on Earth that you know: a town, park, island, lake,
        mountain, beach, forest or region. "fictional" if it comes from myths, stories, films, cartoons or games,
        or if the name is made up. "unsure" if you do not know it well, or if it is only a kind of country with a
        country's name after it ("Desert, Egypt", "Savannah, Botswana"), or if the place is not in that country.
        landscapes: the kinds of country really found at this place, one to four of: Meadow, Farm, Forest, Jungle,
        Mountains, Beach, Lake, Desert, Savanna, SnowyHills (hills under snow for much of the year), LavaField
        (old lava under moss). Empty if it is not real.
        climate: the weather there through the year, one of: Polar (ice and snow most of the year), Subarctic (long
        snowy winters, short summers), Mountain (high mountains, snowy winters), Continental (cold snowy winters, warm
        summers), Subpolar (cool, windy and wet all year, snowy winters, like coastal Iceland), Temperate (mild and
        rainy all year, little snow), Mediterranean (hot dry summers, mild rainy winters), Subtropical (warm all
        year, never snowy), HotDesert (hot and dry), FoggyDesert (a dry desert by a cold sea, often foggy),
        Savanna (hot, with a rainy season and a dry season), Rainforest (hot and wet all year).
        hemisphere: "north" or "south" of the equator.
        """;

    private static readonly JsonElement FactSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["verdict"],
          "properties": { "verdict": { "type": "string", "enum": ["true", "false", "unsure"] } }
        }
        """).RootElement.Clone();

    public static string FactPrompt(string place, string fact) =>
        $"""
        Place: "{place}"
        Sentence: "{fact}"

        verdict: "true" only if everything in the sentence is true of this place, including where it is.
        "false" if any part is wrong. "unsure" if you do not know.
        """;

    /// <summary>Whether a cold call agrees that <paramref name="fact"/> is true of <paramref name="place"/>; "unsure" is no.</summary>
    public async Task<bool> ConfirmFactAsync(string place, string fact, CancellationToken cancellationToken = default)
    {
        var result = await model.GenerateJsonAsync(System, FactPrompt(place, fact), FactSchema, cancellationToken);
        return result.Json?.RootElement.TryGetProperty("verdict", out var v) == true && v.GetString() == "true";
    }

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
