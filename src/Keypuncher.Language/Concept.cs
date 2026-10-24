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
        // Added in Phase 1 so that more letters have a word; Vanya and Stephen to check before recording.
        new("tree", "a tall leafy tree",         new Dictionary<string, string> { ["is"] = "tré",    ["en"] = "tree", ["bg"] = "дърво" }),
        new("ball", "a bouncy round ball",       new Dictionary<string, string> { ["is"] = "bolti",  ["en"] = "ball", ["bg"] = "топка" }),
        new("dog", "a happy fluffy dog",         new Dictionary<string, string> { ["is"] = "hundur", ["en"] = "dog",  ["bg"] = "куче" }),
        new("egg", "a smooth speckled egg",      new Dictionary<string, string> { ["is"] = "egg",    ["en"] = "egg",  ["bg"] = "яйце" }),
        new("star", "a twinkling star",          new Dictionary<string, string> { ["is"] = "stjarna", ["en"] = "star", ["bg"] = "звезда" }),
        new("apple", "a shiny red apple",        new Dictionary<string, string> { ["is"] = "epli",   ["en"] = "apple", ["bg"] = "ябълка" }),
        new("bear", "a big gentle bear",         new Dictionary<string, string> { ["is"] = "björn",  ["en"] = "bear", ["bg"] = "мечка" }),
        new("duck", "a little yellow duck",      new Dictionary<string, string> { ["is"] = "önd",    ["en"] = "duck", ["bg"] = "патица" }),
        new("mouse", "a tiny quick mouse",       new Dictionary<string, string> { ["is"] = "mús",    ["en"] = "mouse", ["bg"] = "мишка" }),
        new("cloud", "a soft white cloud",       new Dictionary<string, string> { ["is"] = "ský",    ["en"] = "cloud", ["bg"] = "облак" }),
        new("boat", "a little bobbing boat",     new Dictionary<string, string> { ["is"] = "bátur",  ["en"] = "boat", ["bg"] = "лодка" }),
        new("butterfly", "a bright fluttering butterfly", new Dictionary<string, string> { ["is"] = "fiðrildi", ["en"] = "butterfly", ["bg"] = "пеперуда" }),
        // Added with the animal and dinosaur pictures; Vanya to check every word here before recording.
        new("cow", "a black and white cow",      new Dictionary<string, string> { ["is"] = "kýr",    ["en"] = "cow",  ["bg"] = "крава" }),
        new("sheep", "a woolly white sheep",     new Dictionary<string, string> { ["is"] = "kind",   ["en"] = "sheep", ["bg"] = "овца" }),
        new("horse", "a tall gentle horse",      new Dictionary<string, string> { ["is"] = "hestur", ["en"] = "horse", ["bg"] = "кон" }),
        new("elephant", "a big kind elephant",   new Dictionary<string, string> { ["is"] = "fíll",   ["en"] = "elephant", ["bg"] = "слон" }),
        new("rabbit", "a soft long-eared rabbit", new Dictionary<string, string> { ["is"] = "kanína", ["en"] = "rabbit", ["bg"] = "заек" }),
        new("rex", "a friendly Tyrannosaurus rex", new Dictionary<string, string> { ["is"] = "grameðla", ["en"] = "T. rex", ["bg"] = "тиранозавър" }),
        new("longneck", "a gentle long-necked dinosaur", new Dictionary<string, string> { ["is"] = "þórseðla", ["en"] = "brontosaurus", ["bg"] = "бронтозавър" }),
        new("triceratops", "a three-horned dinosaur", new Dictionary<string, string> { ["is"] = "þríhyrna", ["en"] = "triceratops", ["bg"] = "трицератопс" }),
        new("stegosaurus", "a dinosaur with plates on its back", new Dictionary<string, string> { ["is"] = "kambeðla", ["en"] = "stegosaurus", ["bg"] = "стегозавър" }),
        // Added with the sea creature pictures; Vanya to check these too before recording.
        new("whale", "a big blue whale",        new Dictionary<string, string> { ["is"] = "hvalur",  ["en"] = "whale", ["bg"] = "кит" }),
        new("dolphin", "a playful dolphin",     new Dictionary<string, string> { ["is"] = "höfrungur", ["en"] = "dolphin", ["bg"] = "делфин" }),
        new("shark", "a friendly shark",        new Dictionary<string, string> { ["is"] = "hákarl",  ["en"] = "shark", ["bg"] = "акула" }),
        new("octopus", "an octopus with eight arms", new Dictionary<string, string> { ["is"] = "kolkrabbi", ["en"] = "octopus", ["bg"] = "октопод" }),
        new("crab", "a little red crab",        new Dictionary<string, string> { ["is"] = "krabbi",  ["en"] = "crab", ["bg"] = "рак" }),
        new("turtle", "a sea turtle",           new Dictionary<string, string> { ["is"] = "skjaldbaka", ["en"] = "turtle", ["bg"] = "костенурка" }),
        new("seal", "a round sleek seal",       new Dictionary<string, string> { ["is"] = "selur",   ["en"] = "seal", ["bg"] = "тюлен" }),
        new("seahorse", "a curly-tailed seahorse", new Dictionary<string, string> { ["is"] = "sæhestur", ["en"] = "seahorse", ["bg"] = "морско конче" }),
        new("starfish", "a five-armed starfish", new Dictionary<string, string> { ["is"] = "krossfiskur", ["en"] = "starfish", ["bg"] = "морска звезда" }),
        new("jellyfish", "a floating jellyfish", new Dictionary<string, string> { ["is"] = "marglytta", ["en"] = "jellyfish", ["bg"] = "медуза" }),
    ];

    /// <summary>Concepts whose word in <paramref name="languageTag"/> starts with <paramref name="letter"/>.
    /// Ð has none in Icelandic, so callers fall back to <see cref="Containing"/>.</summary>
    public static IEnumerable<Concept> StartingWith(string languageTag, char letter) =>
        All.Where(c => c.WordIn(languageTag) is { } w && char.ToUpperInvariant(w[0]) == char.ToUpperInvariant(letter));

    public static IEnumerable<Concept> Containing(string languageTag, char letter) =>
        All.Where(c => c.WordIn(languageTag) is { } w && w.Contains(letter, StringComparison.InvariantCultureIgnoreCase));
}
