using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>Where a picture came from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PictureSource>))]
public enum PictureSource
{
    /// <summary>A photograph fetched from Wikimedia Commons.</summary>
    Photo,
    /// <summary>Made by the local image model.</summary>
    Generated,
    /// <summary>Put in the folder by hand.</summary>
    Hand,
}

/// <summary>What is known about one picture, kept beside it as <c>&lt;slug&gt;.json</c>: the real thing it shows, where it
/// came from and under what licence, whether a grown-up hid it, and what the check made of it.</summary>
public sealed record PictureSidecar(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] RealCategory Category,
    [property: JsonPropertyName("source")] PictureSource Source,
    [property: JsonPropertyName("madeAt")] DateTime MadeAt)
{
    /// <summary>The picture's number among the thing's pictures, which it keeps when another takes its place and it
    /// moves under <c>more/</c>; a grown-up's choice of a picture for a rung names it by this. The first is 1.</summary>
    [JsonPropertyName("id")] public int Id { get; init; } = 1;
    [JsonPropertyName("hidden")] public bool Hidden { get; init; }
    [JsonPropertyName("photo")] public PhotoProvenance? Photo { get; init; }
    [JsonPropertyName("generated")] public GenerationProvenance? Generated { get; init; }
    [JsonPropertyName("check")] public VisionCheck? Check { get; init; }
    /// <summary>Files under <c>raw/</c> that were turned down or kept as runners-up, for "try another".</summary>
    [JsonPropertyName("alternatives")] public IReadOnlyList<string> Alternatives { get; init; } = [];
    /// <summary>How the cut-out was made: the model and whether the matte passed its gate.</summary>
    [JsonPropertyName("cutOut")] public string? CutOut { get; init; }

    /// <summary>One line of credit for the grown-ups' page: who, which licence, where.</summary>
    public string CreditLine => Source switch
    {
        PictureSource.Photo when Photo is { } p => string.Join(", ", new[] { p.Artist, p.LicenseShortName, "via " + p.Site }.Where(s => !string.IsNullOrWhiteSpace(s))),
        PictureSource.Generated when Generated is { } g => $"made by {g.Model}, seed {g.Seed}",
        _ => "put here by hand",
    };
}

/// <summary>The Wikimedia Commons file a photo came from and the terms it carries.</summary>
public sealed record PhotoProvenance(
    [property: JsonPropertyName("pageTitle")] string PageTitle,
    [property: JsonPropertyName("pageUrl")] string PageUrl,
    [property: JsonPropertyName("fileTitle")] string FileTitle,
    [property: JsonPropertyName("fileUrl")] string FileUrl,
    [property: JsonPropertyName("descriptionUrl")] string DescriptionUrl,
    [property: JsonPropertyName("artist")] string? Artist,
    [property: JsonPropertyName("credit")] string? Credit,
    [property: JsonPropertyName("licenseShortName")] string LicenseShortName,
    [property: JsonPropertyName("licenseUrl")] string? LicenseUrl,
    [property: JsonPropertyName("attribution")] string? Attribution,
    [property: JsonPropertyName("sha1")] string? Sha1,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("mime")] string Mime,
    [property: JsonPropertyName("how")] string How)
{
    /// <summary>Where the photo was found, for the credit line: Wikimedia Commons unless the file page says otherwise.</summary>
    [JsonIgnore]
    public string Site => DescriptionUrl.Contains("inaturalist.org", StringComparison.OrdinalIgnoreCase) ? "iNaturalist" : "Wikimedia Commons";
}

/// <summary>How the local image model made a picture, so it can be made again or varied.</summary>
public sealed record GenerationProvenance(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("weights")] string Weights,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("seed")] long Seed,
    [property: JsonPropertyName("steps")] int Steps,
    [property: JsonPropertyName("cfg")] float Cfg);

/// <summary>What a vision model said when shown the picture and asked whether it is the thing.</summary>
public sealed record VisionCheck(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("saw")] string Saw,
    [property: JsonPropertyName("at")] DateTime At);

/// <summary>A real thing the toy would like a picture of, and why it has none yet.</summary>
public sealed record WantedEntry(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")] RealCategory Category,
    [property: JsonPropertyName("since")] DateTime Since)
{
    [JsonPropertyName("tries")] public int Tries { get; init; }
    [JsonPropertyName("lastTry")] public DateTime? LastTry { get; init; }
    /// <summary>"no photo found", "offline", "cut-out model missing", "cut-out failed", "failed check".</summary>
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    /// <summary>Commons file titles already turned down for this thing, so another is tried next time.</summary>
    [JsonPropertyName("exclude")] public IReadOnlyList<string> Exclude { get; init; } = [];
    /// <summary>Whether the category was guessed from the recipe rather than known from the director.</summary>
    [JsonPropertyName("categoryGuessed")] public bool CategoryGuessed { get; init; }
}
