using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>What a grown-up chose to have drawn on one rung of a thing's realism ladder.</summary>
public enum RungKind
{
    /// <summary>The toy decides, as it always has: the rung is made from the picture in play.</summary>
    Auto,
    /// <summary>The toy's own drawn shape, whatever pictures there are.</summary>
    Shape,
    /// <summary>The ladder jumps over this rung.</summary>
    Skip,
    /// <summary>The rung is made from one of the thing's kept pictures, named by its number.</summary>
    Picture,
}

/// <summary>One rung's choice. Written in a thing's card as a word: "auto", "shape", "skip" or "picture:3".</summary>
/// <param name="Picture">The picture's number when <see cref="Kind"/> is <see cref="RungKind.Picture"/>, otherwise 0.</param>
[JsonConverter(typeof(RungChoiceConverter))]
public readonly record struct RungChoice(RungKind Kind, int Picture = 0)
{
    private const string PicturePrefix = "picture:";

    public static RungChoice Auto => default;
    public static RungChoice Shape => new(RungKind.Shape);
    public static RungChoice Skip => new(RungKind.Skip);
    public static RungChoice OfPicture(int id) => new(RungKind.Picture, id);

    /// <summary>Whether a grown-up set this rung, rather than leaving it to the toy.</summary>
    public bool ByHand => Kind != RungKind.Auto;

    /// <summary>Reads a choice as it is written in a card or typed at the console; false when it is none of them.</summary>
    public static bool TryParse(string? text, out RungChoice choice)
    {
        choice = Auto;
        switch (text?.Trim().ToLowerInvariant())
        {
            case "auto": return true;
            case "shape": choice = Shape; return true;
            case "skip": choice = Skip; return true;
            case { } t when t.StartsWith(PicturePrefix, StringComparison.Ordinal)
                && int.TryParse(t.AsSpan(PicturePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0:
                choice = OfPicture(id);
                return true;
            default: return false;
        }
    }

    public override string ToString() => Kind switch
    {
        RungKind.Shape => "shape",
        RungKind.Skip => "skip",
        RungKind.Picture => PicturePrefix + Picture.ToString(CultureInfo.InvariantCulture),
        _ => "auto",
    };
}

/// <summary>Writes a <see cref="RungChoice"/> as its word.</summary>
public sealed class RungChoiceConverter : JsonConverter<RungChoice>
{
    /// <summary>A word this version does not know (a card written by a later one, or by hand) reads as "auto":
    /// the rung goes back to the toy rather than the whole card being thrown away.</summary>
    public override RungChoice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) { reader.Skip(); return RungChoice.Auto; }
        return RungChoice.TryParse(reader.GetString(), out var choice) ? choice : RungChoice.Auto;
    }

    public override void Write(Utf8JsonWriter writer, RungChoice value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>A thing's choices for the four rungs. The photo rung is the picture itself, so it can only be left to
/// the toy or given a picture: there is no drawn shape of a photo, and nothing above it to skip to. The shape rung
/// is the toy's own drawing, so it takes no picture: it can only be left as it is or skipped.</summary>
public sealed record RungChoices
{
    [JsonPropertyName("shape")] public RungChoice Shape { get; init; }
    [JsonPropertyName("silhouette")] public RungChoice Silhouette { get; init; }
    [JsonPropertyName("flat")] public RungChoice Flat { get; init; }
    [JsonPropertyName("photo")] public RungChoice Photo { get; init; }

    /// <summary>Whether a grown-up set any rung.</summary>
    [JsonIgnore] public bool AnyByHand => Shape.ByHand || Silhouette.ByHand || Flat.ByHand || Photo.ByHand;

    public RungChoice For(PictureStage stage) => stage switch
    {
        PictureStage.Shape => Shape,
        PictureStage.Silhouette => Silhouette,
        PictureStage.Flat => Flat,
        _ => Photo,
    };

    /// <summary>Whether a choice may be made for a rung: anything for the silhouette and the flat colours, no
    /// picture for the shape, and only "auto" or a picture for the photo.</summary>
    public static bool Allows(PictureStage stage, RungChoice choice) => stage switch
    {
        PictureStage.Shape => choice.Kind != RungKind.Picture,
        PictureStage.Photo => choice.Kind is RungKind.Auto or RungKind.Picture,
        _ => true,
    };

    /// <summary>The rung drawn in place of one the ladder has reached, with skipped rungs passed over: the next
    /// rung up that is not skipped, as far as <paramref name="cap"/> allows; failing that the nearest below; and the
    /// shape when everything is skipped, since something must be drawn.</summary>
    public PictureStage Landing(PictureStage reached, PictureStage cap)
    {
        if (reached > cap) reached = cap;
        for (var s = reached; s <= cap; s++)
            if (For(s).Kind != RungKind.Skip) return s;
        for (var s = reached - 1; s >= PictureStage.Shape; s--)
            if (For(s).Kind != RungKind.Skip) return s;
        return PictureStage.Shape;
    }

    /// <summary>These choices with one rung changed.</summary>
    /// <exception cref="ArgumentException">The rung does not take that choice; see <see cref="Allows"/>.</exception>
    public RungChoices With(PictureStage stage, RungChoice choice)
    {
        if (!Allows(stage, choice)) throw new ArgumentException($"the {stage} rung cannot be '{choice}'", nameof(choice));
        return stage switch
        {
            PictureStage.Shape => this with { Shape = choice },
            PictureStage.Silhouette => this with { Silhouette = choice },
            PictureStage.Flat => this with { Flat = choice },
            _ => this with { Photo = choice },
        };
    }

    /// <summary>Whether any rung is made from this picture.</summary>
    public bool Uses(int picture) =>
        new[] { Shape, Silhouette, Flat, Photo }.Any(c => c.Kind == RungKind.Picture && c.Picture == picture);
}

/// <summary>What the renderer needs of a thing's card: whether it climbs, and what is drawn on each rung.</summary>
public sealed record ThingChoices(ClimbChoice Climb, RungChoices Rungs);

/// <summary>Whether a thing climbs the realism ladder over many plays or is shown as real as the calm level allows
/// at once.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClimbChoice>))]
public enum ClimbChoice
{
    /// <summary>The toy decides: its own things climb, the director's picks are real at once.</summary>
    Auto,
    Ladder,
    Real,
}
