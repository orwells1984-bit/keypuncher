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

    public static IEnumerable<ClipEntry> For(string languageTag) => For(languageTag, null);

    /// <summary>As <see cref="For(string)"/>, with the words the grown-ups wrote for things in this language
    /// (thing's slug to word): a starter thing is listed with the grown-ups' word for it in place of the toy's,
    /// and every other thing with a word follows the starter words, by word. A thing's clip is named by its slug,
    /// which for a starter thing is its concept id, so recordings made before keep their place.</summary>
    public static IEnumerable<ClipEntry> For(string languageTag, IReadOnlyDictionary<string, string>? thingWords)
    {
        foreach (var c in Alphabet(languageTag))
            yield return LetterEntry(languageTag, c, false);
        if (languageTag == KeyMeanings.Icelandic)
            foreach (var c in KeyMeanings.IcelandicExtras)
                yield return LetterEntry(languageTag, c, true);

        for (var n = 0; n <= 9; n++)
            yield return new ClipEntry(new ClipId(languageTag, ClipKind.Number, n.ToString()),
                n.ToString(), $"Say the number: {NumberWords.For(languageTag, n)}");

        var starters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in StarterConcepts.All)
        {
            starters.Add(concept.Id);
            var byHand = thingWords is not null && thingWords.TryGetValue(concept.Id, out var w) && !string.IsNullOrWhiteSpace(w) ? w : null;
            if ((byHand ?? concept.WordIn(languageTag)) is { } word)
                yield return new ClipEntry(new ClipId(languageTag, ClipKind.Word, concept.Id), word, "Say the word");
        }
        if (thingWords is null) yield break;
        foreach (var (slug, word) in thingWords.Where(t => !starters.Contains(t.Key) && !string.IsNullOrWhiteSpace(t.Value)).OrderBy(t => t.Value, StringComparer.CurrentCultureIgnoreCase))
            yield return new ClipEntry(ThingClip(languageTag, slug), word, "Say the word");
    }

    /// <summary>The clip of the word for a thing, by the thing's slug.</summary>
    public static ClipId ThingClip(string languageTag, string slug) => new(languageTag, ClipKind.Word, slug);

    private static ClipEntry LetterEntry(string languageTag, char c, bool optional) =>
        new(new ClipId(languageTag, ClipKind.Letter, char.ToLowerInvariant(c).ToString()),
            $"{c} {char.ToLowerInvariant(c)}", $"Say the sound of {c}, not its name", optional);
}
