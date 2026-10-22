namespace Keypuncher.Language;

/// <summary>
/// One thing the child can learn a word for. The LLM draws it from <see cref="Gloss"/>;
/// the words the child sees and hears come only from <see cref="Words"/>, which a parent approves.
/// </summary>
/// <param name="Id">Stable id, e.g. "sun".</param>
/// <param name="Gloss">English description handed to the LLM, never shown to the child.</param>
/// <param name="Words">Language tag ("is", "en", "bg") to the word in that language.</param>
public sealed record Concept(string Id, string Gloss, IReadOnlyDictionary<string, string> Words)
{
    public string? WordIn(string languageTag) => Words.TryGetValue(languageTag, out var w) ? w : null;
}

/// <summary>Starter concepts. Vanya and Stephen extend and correct this list in the parent screen.</summary>
public static class StarterConcepts
{
    public static readonly IReadOnlyList<Concept> All =
    [
        new("sun",  "a smiling sun",            new Dictionary<string, string> { ["is"] = "sól",    ["en"] = "sun",  ["bg"] = "слънце" }),
        new("cat",  "a round sleepy cat",        new Dictionary<string, string> { ["is"] = "köttur", ["en"] = "cat",  ["bg"] = "котка" }),
        new("fish", "a small bright fish",       new Dictionary<string, string> { ["is"] = "fiskur", ["en"] = "fish", ["bg"] = "риба" }),
        new("bird", "a plump little bird",       new Dictionary<string, string> { ["is"] = "fugl",   ["en"] = "bird", ["bg"] = "птица" }),
        new("moon", "a soft crescent moon",      new Dictionary<string, string> { ["is"] = "tungl",  ["en"] = "moon", ["bg"] = "луна" }),
        new("flower", "a big friendly flower",   new Dictionary<string, string> { ["is"] = "blóm",   ["en"] = "flower", ["bg"] = "цвете" }),
        new("house", "a cosy little house",      new Dictionary<string, string> { ["is"] = "hús",    ["en"] = "house", ["bg"] = "къща" }),
    ];

    /// <summary>Concepts whose word in <paramref name="languageTag"/> starts with <paramref name="letter"/>.
    /// Ð has none in Icelandic, so callers fall back to <see cref="Containing"/>.</summary>
    public static IEnumerable<Concept> StartingWith(string languageTag, char letter) =>
        All.Where(c => c.WordIn(languageTag) is { } w && char.ToUpperInvariant(w[0]) == char.ToUpperInvariant(letter));

    public static IEnumerable<Concept> Containing(string languageTag, char letter) =>
        All.Where(c => c.WordIn(languageTag) is { } w && w.Contains(letter, StringComparison.InvariantCultureIgnoreCase));
}
