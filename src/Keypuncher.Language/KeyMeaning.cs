using System.Globalization;
using System.Text;
using Keypuncher.Core;

namespace Keypuncher.Language;

/// <summary>What a key means in one language: the glyph to show, and the clip to play if there is one.</summary>
/// <param name="Display">The glyph, in the case the keycap gives; the caller applies the parent's case choice.</param>
/// <param name="Clip">The letter sound or number to say, or null when this language says nothing for the key.</param>
/// <param name="FallbackText">What a synthetic voice may say if the clip is not recorded. Null for letters:
/// voices say letter names, and the toy says letter sounds.</param>
/// <param name="Letter">The letter in this language's alphabet, for finding a word; null for digits.</param>
/// <param name="Number">The digit's value; null for letters.</param>
public sealed record KeyMeaning(
    string Language,
    string Display,
    ClipId? Clip,
    string? FallbackText,
    char? Letter,
    int? Number);

public static class KeyMeanings
{
    public const string Icelandic = "is";
    public const string English = "en";
    public const string Bulgarian = "bg";
    public static readonly IReadOnlyList<string> Languages = [Icelandic, English, Bulgarian];

    /// <summary>Latin letters on the keyboard that are not in the Icelandic alphabet. They are shown and,
    /// if Vanya records them, said in Icelandic too.</summary>
    public static readonly IReadOnlyList<char> IcelandicExtras = ['C', 'Q', 'W', 'Z'];

    /// <summary>Returns null for characters that are neither a letter nor a digit (punctuation);
    /// those keys still make a shape and a note.</summary>
    public static KeyMeaning? For(string languageTag, string? character)
    {
        if (character is not { Length: 1 }) return null;
        var c = character[0];

        if (c is >= '0' and <= '9')
        {
            var n = c - '0';
            return new KeyMeaning(languageTag, character, new ClipId(languageTag, ClipKind.Number, character),
                NumberWords.For(languageTag, n), null, n);
        }
        if (!char.IsLetter(c)) return null;

        var upper = char.ToUpperInvariant(c);
        switch (languageTag)
        {
            case Icelandic when IcelandicAlphabet.Contains(upper) || IcelandicExtras.Contains(upper):
                return Letter(languageTag, upper);
            case English when upper is >= 'A' and <= 'Z':
                return Letter(languageTag, upper);
            case Bulgarian when (BulgarianPhoneticMap.Map(upper) ?? BulgarianPhoneticMap.Map(StripAccent(upper))) is { } cyr:
                return Letter(languageTag, cyr);
            default:
                // A key this language has no letter for (Ð in English, W in Bulgarian): show it, say nothing.
                return new KeyMeaning(languageTag, upper.ToString(), null, null, null, null);
        }
    }

    public static string ApplyCase(string display, bool upperCase) =>
        upperCase ? display.ToUpper(CultureInfo.InvariantCulture) : display.ToLower(CultureInfo.InvariantCulture);

    private static KeyMeaning Letter(string languageTag, char upper) =>
        new(languageTag, upper.ToString(),
            new ClipId(languageTag, ClipKind.Letter, char.ToLowerInvariant(upper).ToString()), null, upper, null);

    private static char StripAccent(char c)
    {
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        return d.Length > 0 ? d[0] : c;
    }
}
