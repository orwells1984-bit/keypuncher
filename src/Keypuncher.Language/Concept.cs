namespace Keypuncher.Language;

/// <summary>
/// One thing the child can learn a word for. The words the child sees and hears come only from
/// <see cref="Words"/>, which a parent approves.
/// </summary>
/// <param name="Id">Stable id, e.g. "sun".</param>
/// <param name="Words">Language tag ("is", "en", "bg") to the word in that language.</param>
public sealed record Concept(string Id, IReadOnlyDictionary<string, string> Words)
{
    public string? WordIn(string languageTag) => Words.TryGetValue(languageTag, out var w) ? w : null;
}

/// <summary>Starter concepts. Vanya and Stephen extend and correct this list in the parent screen.</summary>
public static class StarterConcepts
{
    public static readonly IReadOnlyList<Concept> All =
    [
        new("sun", new Dictionary<string, string> { ["is"] = "sól",    ["en"] = "sun",  ["bg"] = "слънце" }),
        new("cat", new Dictionary<string, string> { ["is"] = "köttur", ["en"] = "cat",  ["bg"] = "котка" }),
        new("fish", new Dictionary<string, string> { ["is"] = "fiskur", ["en"] = "fish", ["bg"] = "риба" }),
        new("bird", new Dictionary<string, string> { ["is"] = "fugl",   ["en"] = "bird", ["bg"] = "птица" }),
        new("moon", new Dictionary<string, string> { ["is"] = "tungl",  ["en"] = "moon", ["bg"] = "луна" }),
        new("flower", new Dictionary<string, string> { ["is"] = "blóm",   ["en"] = "flower", ["bg"] = "цвете" }),
        new("house", new Dictionary<string, string> { ["is"] = "hús",    ["en"] = "house", ["bg"] = "къща" }),
        // Added in Phase 1 so that more letters have a word; Vanya and Stephen to check before recording.
        new("tree", new Dictionary<string, string> { ["is"] = "tré",    ["en"] = "tree", ["bg"] = "дърво" }),
        new("ball", new Dictionary<string, string> { ["is"] = "bolti",  ["en"] = "ball", ["bg"] = "топка" }),
        new("dog", new Dictionary<string, string> { ["is"] = "hundur", ["en"] = "dog",  ["bg"] = "куче" }),
        new("egg", new Dictionary<string, string> { ["is"] = "egg",    ["en"] = "egg",  ["bg"] = "яйце" }),
        new("star", new Dictionary<string, string> { ["is"] = "stjarna", ["en"] = "star", ["bg"] = "звезда" }),
        new("apple", new Dictionary<string, string> { ["is"] = "epli",   ["en"] = "apple", ["bg"] = "ябълка" }),
        new("bear", new Dictionary<string, string> { ["is"] = "björn",  ["en"] = "bear", ["bg"] = "мечка" }),
        new("duck", new Dictionary<string, string> { ["is"] = "önd",    ["en"] = "duck", ["bg"] = "патица" }),
        new("mouse", new Dictionary<string, string> { ["is"] = "mús",    ["en"] = "mouse", ["bg"] = "мишка" }),
        new("cloud", new Dictionary<string, string> { ["is"] = "ský",    ["en"] = "cloud", ["bg"] = "облак" }),
        new("boat", new Dictionary<string, string> { ["is"] = "bátur",  ["en"] = "boat", ["bg"] = "лодка" }),
        new("butterfly", new Dictionary<string, string> { ["is"] = "fiðrildi", ["en"] = "butterfly", ["bg"] = "пеперуда" }),
        // Added with the animal and dinosaur pictures; Vanya to check every word here before recording.
        new("cow", new Dictionary<string, string> { ["is"] = "kýr",    ["en"] = "cow",  ["bg"] = "крава" }),
        new("sheep", new Dictionary<string, string> { ["is"] = "kind",   ["en"] = "sheep", ["bg"] = "овца" }),
        new("horse", new Dictionary<string, string> { ["is"] = "hestur", ["en"] = "horse", ["bg"] = "кон" }),
        new("elephant", new Dictionary<string, string> { ["is"] = "fíll",   ["en"] = "elephant", ["bg"] = "слон" }),
        new("rabbit", new Dictionary<string, string> { ["is"] = "kanína", ["en"] = "rabbit", ["bg"] = "заек" }),
        new("rex", new Dictionary<string, string> { ["is"] = "grameðla", ["en"] = "T. rex", ["bg"] = "тиранозавър" }),
        new("longneck", new Dictionary<string, string> { ["is"] = "þórseðla", ["en"] = "brontosaurus", ["bg"] = "бронтозавър" }),
        new("triceratops", new Dictionary<string, string> { ["is"] = "þríhyrna", ["en"] = "triceratops", ["bg"] = "трицератопс" }),
        new("stegosaurus", new Dictionary<string, string> { ["is"] = "kambeðla", ["en"] = "stegosaurus", ["bg"] = "стегозавър" }),
        // Added with the sea creature pictures; Vanya to check these too before recording.
        new("whale", new Dictionary<string, string> { ["is"] = "hvalur",  ["en"] = "whale", ["bg"] = "кит" }),
        new("dolphin", new Dictionary<string, string> { ["is"] = "höfrungur", ["en"] = "dolphin", ["bg"] = "делфин" }),
        new("shark", new Dictionary<string, string> { ["is"] = "hákarl",  ["en"] = "shark", ["bg"] = "акула" }),
        new("octopus", new Dictionary<string, string> { ["is"] = "kolkrabbi", ["en"] = "octopus", ["bg"] = "октопод" }),
        new("crab", new Dictionary<string, string> { ["is"] = "krabbi",  ["en"] = "crab", ["bg"] = "рак" }),
        new("turtle", new Dictionary<string, string> { ["is"] = "skjaldbaka", ["en"] = "turtle", ["bg"] = "костенурка" }),
        new("seal", new Dictionary<string, string> { ["is"] = "selur",   ["en"] = "seal", ["bg"] = "тюлен" }),
        new("seahorse", new Dictionary<string, string> { ["is"] = "sæhestur", ["en"] = "seahorse", ["bg"] = "морско конче" }),
        new("starfish", new Dictionary<string, string> { ["is"] = "krossfiskur", ["en"] = "starfish", ["bg"] = "морска звезда" }),
        new("jellyfish", new Dictionary<string, string> { ["is"] = "marglytta", ["en"] = "jellyfish", ["bg"] = "медуза" }),
    ];

}
