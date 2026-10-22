namespace Keypuncher.Language;

/// <summary>The 32 letters of the Icelandic alphabet, in dictionary order.</summary>
public static class IcelandicAlphabet
{
    public static readonly IReadOnlyList<char> Upper =
        ['A', 'Á', 'B', 'D', 'Ð', 'E', 'É', 'F', 'G', 'H', 'I', 'Í', 'J', 'K', 'L', 'M',
         'N', 'O', 'Ó', 'P', 'R', 'S', 'T', 'U', 'Ú', 'V', 'X', 'Y', 'Ý', 'Þ', 'Æ', 'Ö'];

    /// <summary>Letters on the Icelandic keyboard that no other target language uses as a plain letter.</summary>
    public static readonly IReadOnlyList<char> KeyboardOnly = ['Ð', 'Þ', 'Æ', 'Ö'];

    public static bool Contains(char c) => Upper.Contains(char.ToUpperInvariant(c));
}
