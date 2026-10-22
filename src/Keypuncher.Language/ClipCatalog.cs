using Keypuncher.Core;

namespace Keypuncher.Language;

/// <summary>One line on the recording screen.</summary>
/// <param name="Prompt">What to show the person recording, large: the letter, digit or word.</param>
/// <param name="Hint">How to say it.</param>
/// <param name="Optional">Nice to have; the toy works without it.</param>
public sealed record ClipEntry(ClipId Id, string Prompt, string Hint, bool Optional = false);

/// <summary>Every clip the toy can play in a language, in recording order: letters, numbers, words.</summary>
public static class ClipCatalog
{
    public static IReadOnlyList<char> Alphabet(string languageTag) => languageTag switch
    {
        KeyMeanings.Icelandic => IcelandicAlphabet.Upper,
        KeyMeanings.Bulgarian => BulgarianPhoneticMap.Alphabet,
        _ => [.. Enumerable.Range('A', 26).Select(i => (char)i)],
    };

    public static IEnumerable<ClipEntry> For(string languageTag)
    {
        foreach (var c in Alphabet(languageTag))
            yield return LetterEntry(languageTag, c, false);
        if (languageTag == KeyMeanings.Icelandic)
            foreach (var c in KeyMeanings.IcelandicExtras)
                yield return LetterEntry(languageTag, c, true);

        for (var n = 0; n <= 9; n++)
            yield return new ClipEntry(new ClipId(languageTag, ClipKind.Number, n.ToString()),
                n.ToString(), $"Say the number: {NumberWords.For(languageTag, n)}");

        foreach (var concept in StarterConcepts.All)
            if (concept.WordIn(languageTag) is { } word)
                yield return new ClipEntry(new ClipId(languageTag, ClipKind.Word, concept.Id), word, "Say the word");
    }

    private static ClipEntry LetterEntry(string languageTag, char c, bool optional) =>
        new(new ClipId(languageTag, ClipKind.Letter, char.ToLowerInvariant(c).ToString()),
            $"{c} {char.ToLowerInvariant(c)}", $"Say the sound of {c}, not its name", optional);
}
