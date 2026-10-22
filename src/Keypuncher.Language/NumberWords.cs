namespace Keypuncher.Language;

/// <summary>Counting words 0 to 9, used when a number has no recording yet and a voice can say it.</summary>
public static class NumberWords
{
    private static readonly Dictionary<string, string[]> Words = new()
    {
        ["is"] = ["núll", "einn", "tveir", "þrír", "fjórir", "fimm", "sex", "sjö", "átta", "níu"],
        ["en"] = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"],
        ["bg"] = ["нула", "едно", "две", "три", "четири", "пет", "шест", "седем", "осем", "девет"],
    };

    public static string? For(string languageTag, int n) =>
        n is >= 0 and <= 9 && Words.TryGetValue(languageTag, out var w) ? w[n] : null;
}
