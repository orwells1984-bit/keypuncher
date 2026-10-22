namespace Keypuncher.Language;

/// <summary>
/// Maps the character printed on an Icelandic-layout keycap to a Bulgarian letter.
/// The keycaps are not printed in Cyrillic, so the mapping is phonetic where one exists and
/// uses the Icelandic-only keys (Ð Þ Æ Ö and two accented vowels) for letters with no Latin twin.
/// Covers all 30 letters of the Bulgarian alphabet exactly once.
/// </summary>
public static class BulgarianPhoneticMap
{
    public static readonly IReadOnlyDictionary<char, char> FromKeycap = new Dictionary<char, char>
    {
        ['A'] = 'А', ['B'] = 'Б', ['V'] = 'В', ['G'] = 'Г', ['D'] = 'Д', ['E'] = 'Е',
        ['Ö'] = 'Ж', ['Z'] = 'З', ['I'] = 'И', ['J'] = 'Й', ['K'] = 'К', ['L'] = 'Л',
        ['M'] = 'М', ['N'] = 'Н', ['O'] = 'О', ['P'] = 'П', ['R'] = 'Р', ['S'] = 'С',
        ['T'] = 'Т', ['U'] = 'У', ['F'] = 'Ф', ['H'] = 'Х', ['C'] = 'Ц', ['Æ'] = 'Ч',
        ['Þ'] = 'Ш', ['Ð'] = 'Щ', ['Y'] = 'Ъ', ['X'] = 'Ь', ['Ú'] = 'Ю', ['Q'] = 'Я',
    };

    public static readonly IReadOnlyList<char> Alphabet =
        ['А', 'Б', 'В', 'Г', 'Д', 'Е', 'Ж', 'З', 'И', 'Й', 'К', 'Л', 'М', 'Н', 'О',
         'П', 'Р', 'С', 'Т', 'У', 'Ф', 'Х', 'Ц', 'Ч', 'Ш', 'Щ', 'Ъ', 'Ь', 'Ю', 'Я'];

    public static char? Map(char keycap) =>
        FromKeycap.TryGetValue(char.ToUpperInvariant(keycap), out var c) ? c : null;
}
