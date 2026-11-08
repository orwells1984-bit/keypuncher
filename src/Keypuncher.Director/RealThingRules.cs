using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>What the director is asked for: a new real thing for one key family.</summary>
/// <param name="Slot">The family in <see cref="KeyFamilies"/>.</param>
/// <param name="Kind">The family's kind, which it keeps: a plant family only ever holds plants.</param>
/// <param name="Current">The family's thing now; null while it is still an invented built-in one.</param>
/// <param name="Existing">Names of the real things already in the world, so the pick is something new.</param>
/// <param name="Category">The one kind of real thing asked for; the schema allows only this category, so a plant
/// family always gets a plant and a creature family is varied between animals, extinct animals, vehicles and
/// objects by the loop rather than by the model's habits.</param>
/// <param name="Place">Where such a thing is found, as the prompt puts it ("that really lives at Lake Bled,
/// Slovenia"): where the scene is, or drawn at random when there is no scene. Naming a place also keeps a small
/// model from offering the same three animals every time.</param>
/// <param name="GrowInto">When the current thing really grows into something (a tadpole), the next pick must be that.</param>
/// <param name="MustBe">The director's last resort for a category the model keeps missing: the pick must be this
/// well-known real thing, which is still checked like any other.</param>
/// <param name="Here">Where the scene is, when it is a named place ("at Lake Bled, Slovenia"); a living animal or plant must be
/// confirmed as really found there by <see cref="FactChecker.FitsAsync"/>.</param>
/// <param name="Landscape">What the scene looks like, when it is known: a lake takes no sea creatures, a desert no
/// fish (<see cref="Habitat"/>).</param>
public sealed record DirectorRequest(
    int Slot,
    RecipeKind Kind,
    string? Current,
    IReadOnlyList<string> Existing,
    string? GrowInto = null,
    RealCategory Category = RealCategory.Animal,
    string? Place = null,
    string? MustBe = null,
    string? Here = null,
    Landscape? Landscape = null);

/// <summary>
/// The checks a pick must pass before anything is asked about whether it is real. They cannot prove a thing
/// is real (that is <see cref="FactChecker"/>'s job), but they turn away the usual failures of a small model
/// cheaply and for certain: the stock fantasy creatures, names that are not names, a shape that cannot be the
/// thing (a dinosaur-shaped bus), a thing that does not fit its key family, and repeats.
/// </summary>
public static class RealThingRules
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Words that, on their own, mean a pick is from a story. Checked as whole words, so "dragonfly"
    /// and "snapdragon" pass; real names that contain one are in <see cref="RealDespiteTheWord"/>.</summary>
    private static readonly HashSet<string> FantasyWords =
    [
        "dragon", "unicorn", "fairy", "faerie", "mermaid", "merman", "monster", "ghost", "troll", "elf", "goblin",
        "gnome", "phoenix", "griffin", "gryphon", "pegasus", "yeti", "bigfoot", "sasquatch", "alien", "zombie",
        "vampire", "werewolf", "wizard", "witch", "kraken", "hydra", "centaur", "minotaur", "cyclops", "pixie",
        "sprite", "imp", "demon", "magic", "magical", "enchanted", "pokemon", "nessie", "kelpie", "selkie",
        "hobbit", "ogre", "leprechaun", "genie", "jackalope",
    ];

    /// <summary>Real things whose names hold a word from <see cref="FantasyWords"/>.</summary>
    private static readonly HashSet<string> RealDespiteTheWord =
    [
        "komodo dragon", "bearded dragon", "sea dragon", "leafy sea dragon", "weedy sea dragon", "water dragon",
        "dragon fruit", "gila monster", "ghost crab", "ghost shark", "ghost orchid", "ghost pepper",
        "fairy tern", "fairy shrimp", "fairy penguin", "fairy wren", "elf owl", "witch hazel", "unicorn fish",
        "monster truck",
    ];

    /// <summary>Group words: real, but not one kind of thing a child could picture.</summary>
    private static readonly HashSet<string> GroupWords =
    [
        "animal", "animals", "creature", "dinosaur", "dinosaurs", "dino", "dinos", "bird", "mammal", "reptile", "insect", "bug",
        "plant", "flower", "tree", "vegetable", "fruit", "vehicle", "object", "thing", "toy", "baby", "fossil",
    ];

    /// <summary>Words that describe rather than name: "extinct marine reptile" is not one kind of thing.</summary>
    private static readonly HashSet<string> DescribingWords = ["extinct", "prehistoric", "ancient", "fossil", "unknown", "some", "any", "kind", "type"];

    /// <summary>Group words that end a description rather than a name: "sea animal", "flying reptile".</summary>
    private static readonly HashSet<string> GroupEndings = ["animal", "animals", "creature", "creatures", "reptile", "reptiles", "mammal", "mammals", "beast", "species", "dinosaur", "dinosaurs", "thing", "vehicle", "vehicles", "object", "objects"];
    /// <summary>What the model hands back when it echoes the schema instead of answering: in a run it offered
    /// "string" three times for a vehicle.</summary>
    private static readonly HashSet<string> Placeholders = ["string", "name", "subject", "text", "example", "placeholder", "item", "value", "unknown", "tbd"];

    /// <summary>Ordinary things the checker is known to call "unsure": in the bench (Nov 2026) it turned away a jeep, a
    /// pickup truck, a hydrofoil, an SUV, a woolly rhinoceros five times and a sabre-toothed cat, all real, while it
    /// let fewer fakes through than that. A pick by one of these names, in the kind given, passes without the call.
    /// Vehicles and the well-known extinct animals only: animals and plants alive today are too many to list and are
    /// where a made-up name would hide.</summary>
    private static readonly Dictionary<string, RealCategory> WellKnown = BuildWellKnown();

    private static Dictionary<string, RealCategory> BuildWellKnown()
    {
        var known = new Dictionary<string, RealCategory>(StringComparer.Ordinal);
        foreach (var v in new[]
        {
            "car", "bus", "van", "truck", "lorry", "pickup truck", "pick-up truck", "jeep", "suv", "taxi", "tractor", "digger", "excavator",
            "bulldozer", "dump truck", "fire engine", "fire truck", "ambulance", "police car", "bicycle", "bike", "tricycle", "scooter",
            "motorbike", "motorcycle", "skateboard", "train", "steam train", "tram", "cable car", "ski lift", "aeroplane", "airplane",
            "helicopter", "hot-air balloon", "glider", "boat", "rowing boat", "rowboat", "row boat", "sailing boat", "sailboat", "fishing boat",
            "motorboat", "speedboat", "canoe", "kayak", "ferry", "hydrofoil", "ship", "tugboat", "submarine", "snowmobile", "sled", "sledge",
            "dune buggy", "quad bike", "camper van", "wheelbarrow", "pram", "pushchair",
        }) known[v] = RealCategory.Vehicle;
        foreach (var e in RealThingPick.WellKnownExtinct) known[e] = RealCategory.ExtinctAnimal;
        foreach (var e in new[]
        {
            "tyrannosaurus", "tyrannosaurus rex", "t. rex", "t-rex", "triceratops", "stegosaurus", "brontosaurus", "apatosaurus",
            "saber-toothed cat", "sabre-toothed tiger", "saber-toothed tiger", "woolly rhino", "mammoth", "quagga", "aurochs",
            "elephant bird", "giant moa", "cave lion", "dire wolf", "pterodactyl", "mosasaurus",
        }) known[e] = RealCategory.ExtinctAnimal;
        return known;
    }

    /// <summary>The kind a well-known name is, or null if the name is not on the list.</summary>
    public static RealCategory? WellKnownKind(string name)
    {
        var tidy = Tidy(name);
        if (WellKnown.TryGetValue(tidy, out var kind)) return kind;
        // "a jeep" and "jeeps" are a jeep.
        if (tidy.StartsWith("a ") || tidy.StartsWith("an ")) return WellKnownKind(tidy[(tidy.IndexOf(' ') + 1)..]);
        return WellKnown.FirstOrDefault(k => SameThing(k.Key, tidy)).Key is not null ? WellKnown.First(k => SameThing(k.Key, tidy)).Value : null;
    }

    /// <summary>Peoples and languages whose name is not made from the place's by a plain ending.</summary>
    private static readonly Dictionary<string, string[]> IrregularDemonyms = new()
    {
        ["england"] = ["english"], ["britain"] = ["british"], ["wales"] = ["welsh"], ["scotland"] = ["scottish", "scots"],
        ["ireland"] = ["irish"], ["france"] = ["french"], ["spain"] = ["spanish"], ["portugal"] = ["portuguese"],
        ["netherlands"] = ["dutch"], ["holland"] = ["dutch"], ["switzerland"] = ["swiss"], ["denmark"] = ["danish"],
        ["sweden"] = ["swedish"], ["finland"] = ["finnish"], ["norway"] = ["norwegian"], ["greece"] = ["greek"],
        ["turkey"] = ["turkish"], ["poland"] = ["polish"], ["germany"] = ["german"], ["china"] = ["chinese"],
        ["japan"] = ["japanese"], ["vietnam"] = ["vietnamese"], ["nepal"] = ["nepali", "nepalese"], ["thailand"] = ["thai"],
        ["philippines"] = ["filipino", "philippine"], ["cyprus"] = ["cypriot"], ["malta"] = ["maltese"], ["peru"] = ["peruvian"],
        ["argentina"] = ["argentine", "argentinian"], ["chile"] = ["chilean"], ["morocco"] = ["moroccan"], ["mexico"] = ["mexican"],
        ["canada"] = ["canadian"], ["madagascar"] = ["malagasy"], ["new zealand"] = ["kiwi"], ["iceland"] = ["icelandic"],
    };

    /// <summary>The words a place lends to a name: the place's own words ("lake", "bled", "slovenia") and the ways
    /// English makes a people from them ("slovenian"). The place is written as the prompt puts it ("that really lives at
    /// Lake Bled, Slovenia"), so its proper nouns are the capitalised words.</summary>
    public static HashSet<string> PlaceWords(params string?[] places)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var place in places)
        {
            if (place is null) continue;
            foreach (var raw in place.Split(' ', ',', ';', '(', ')', '"'))
            {
                if (raw.Length < 3 || !char.IsUpper(raw[0])) continue;
                var w = raw.ToLowerInvariant().Trim('.', '\'');
                words.Add(w);
                foreach (var ending in new[] { "n", "an", "ian", "ic", "ish", "ese", "i", "s" }) words.Add(w + ending);
                var stem = w.TrimEnd('a', 'e', 'o', 'y');
                if (stem.Length >= 3) foreach (var ending in new[] { "an", "ian", "ean", "n", "ic" }) words.Add(stem + ending);
                if (IrregularDemonyms.TryGetValue(w, out var irregular)) words.UnionWith(irregular);
            }
            var lower = place.ToLowerInvariant();
            foreach (var (name, people) in IrregularDemonyms) if (lower.Contains(name)) words.UnionWith(people);
        }
        return words;
    }

    /// <summary>The name without the place the request named, for telling repeats apart: "slovenian ferry", "lake bled
    /// ferry" and "ferry" are one thing. The pick keeps its full name; a place in a name is harmless in itself (the child
    /// never sees it) and often part of a real kind's name (iceland poppy, icelandic horse). Empty when the name was
    /// nothing but the place.</summary>
    public static string PlaceKey(string name, params string?[] places)
    {
        var words = PlaceWords(places);
        var kept = Tidy(name).Split(' ').Where(w => !words.Contains(w.Trim('-')) && !w.Split('-').All(words.Contains));
        return string.Join(' ', kept);
    }

    /// <summary>What a model writes when it means no life stage.</summary>
    private static readonly HashSet<string> Nothing = ["null", "none", "nothing", "n/a", "na", "no", "-"];

    /// <summary>Names that have a shape of their own; a pick by that name is drawn with it whatever the model chose.</summary>
    private static readonly Dictionary<string, Shape> OwnShapes = new(StringComparer.Ordinal)
    {
        ["tyrannosaurus"] = Shape.Rex, ["tyrannosaurus rex"] = Shape.Rex, ["t. rex"] = Shape.Rex, ["t-rex"] = Shape.Rex,
        ["brontosaurus"] = Shape.LongNeck, ["apatosaurus"] = Shape.LongNeck, ["brachiosaurus"] = Shape.LongNeck,
        ["diplodocus"] = Shape.LongNeck,
        ["woolly mammoth"] = Shape.Elephant, ["mammoth"] = Shape.Elephant,
        ["sea turtle"] = Shape.Turtle, ["tortoise"] = Shape.Turtle, ["blue whale"] = Shape.Whale,
        ["orca"] = Shape.Whale, ["killer whale"] = Shape.Whale, ["humpback whale"] = Shape.Whale,
        ["puppy"] = Shape.Dog, ["kitten"] = Shape.Cat, ["lamb"] = Shape.Sheep, ["foal"] = Shape.Horse,
        ["pony"] = Shape.Horse, ["duckling"] = Shape.Duck, ["bunny"] = Shape.Rabbit, ["calf"] = Shape.Cow,
    };

    static RealThingRules()
    {
        foreach (var animal in BuiltInRecipes.Animals.All.Where(a => a.Name is not ("rex" or "longneck")))
            OwnShapes[animal.Name] = animal.Body.Shape;
    }

    private static bool IsDinosaurShape(Shape s) => s is Shape.Rex or Shape.LongNeck or Shape.Triceratops or Shape.Stegosaurus;

    /// <summary>Lower case, single spaces, no quotes around it.</summary>
    public static string Tidy(string name) =>
        string.Join(' ', name.Trim().Trim('"', '\'', '.', '!').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The same name, or one is the other with a plural ending: apple and apples, peach and peaches.</summary>
    public static bool SameThing(string a, string b)
    {
        a = Tidy(a);
        b = Tidy(b);
        return a == b || a + "s" == b || b + "s" == a || a + "es" == b || b + "es" == a;
    }

    /// <summary>Why <paramref name="name"/> cannot be a real thing's ordinary name, or null if it could be.</summary>
    public static string? NameProblem(string name)
    {
        if (name.Length is < 2 or > 24) return "name length";
        if (!name.All(c => c is (>= 'a' and <= 'z') or ' ' or '-' or '\'' or '.')) return "not a plain English name";
        if (name.Count(c => c == ' ') > 3) return "too many words";
        if (Placeholders.Contains(name)) return "a placeholder, not a name";
        var parts = name.Split(' ');
        if (GroupWords.Contains(name) || parts.Any(DescribingWords.Contains) || GroupEndings.Contains(parts[^1]))
            return "too general, not one kind";
        if (RealDespiteTheWord.Contains(name)) return null;
        var words = name.Split(' ', '-');
        if (FantasyWords.Contains(name) || words.Any(FantasyWords.Contains)) return "from a story, not real";
        return null;
    }

    /// <summary>Turns the model's JSON into a pick and a recipe for <paramref name="request"/>'s family, or says why not.</summary>
    public static bool TryBuild(JsonDocument? json, DirectorRequest request, out RealThingPick? pick, out Recipe? recipe, out string? error)
    {
        pick = null;
        recipe = null;
        error = null;
        if (json is null) { error = "not JSON"; return false; }

        RealThingPick? parsed;
        try { parsed = json.RootElement.Deserialize<RealThingPick>(Options); }
        catch (JsonException e)
        {
            Log.Warn("director", $"the pick did not fit the schema: {Log.Short(json.RootElement.GetRawText())}", e);
            error = "bad JSON: " + e.Message;
            return false;
        }
        if (parsed is null) { error = "empty"; return false; }

        var subject = Tidy(parsed.Subject);
        pick = parsed with { Subject = subject };
        if (NameProblem(subject) is { } problem) { error = problem; return false; }
        if (string.IsNullOrWhiteSpace(parsed.Fact)) { error = "no fact"; return false; }

        if ((request.GrowInto ?? request.MustBe) is { } next && subject != Tidy(next)) { error = $"should have been {next}"; return false; }
        // Repeats are told apart without the place in the name: "lake bled ferry" is the ferry already there.
        var key = PlaceKey(subject, request.Place, request.Here);
        if (key.Length == 0 || (request.Place is { } where && where.Split(' ').Any(w => SameThing(w, subject)))) { error = "the place, not a thing in it"; return false; }
        bool Same(string other) => SameThing(other, subject) || SameThing(PlaceKey(other, request.Place, request.Here), key);
        if (request.GrowInto is null && (request.Existing.Any(Same) || (request.Current is { } now && Same(now))))
        {
            error = "already in the world";
            return false;
        }

        if (parsed.Category != request.Category && request.GrowInto is null)
        {
            error = $"not {RealThingPick.Describe(request.Category)}";
            return false;
        }
        if (RealThingPick.KindOf(parsed.Category) != request.Kind)
        {
            error = request.Kind == RecipeKind.Plant ? "not a plant" : "a plant, but this family is not";
            return false;
        }

        var shape = OwnShapes.TryGetValue(subject, out var own) ? own : parsed.Shape;
        var living = parsed.Category is RealCategory.Animal or RealCategory.ExtinctAnimal;
        if (shape.IsAnimal() && !living) { error = "an animal shape for a thing that is not an animal"; return false; }
        if (IsDinosaurShape(shape) && parsed.Category != RealCategory.ExtinctAnimal) { error = "a dinosaur shape for an animal alive today"; return false; }

        // Plants stay rooted; nothing in a family bursts, which is for effects.
        var motion = parsed.Motion.Type switch
        {
            _ when request.Kind == RecipeKind.Plant && parsed.Motion.Type is not (MotionType.Still or MotionType.Sway) => parsed.Motion with { Type = MotionType.Sway },
            MotionType.Burst => parsed.Motion with { Type = MotionType.Drift },
            _ => parsed.Motion,
        };

        if (!RecipeValidator.IsNote(parsed.Voice.Root)) { error = "root"; return false; }

        var grows = parsed.GrowsInto is { } g && Tidy(g) is var tidy && !Nothing.Contains(tidy) && tidy != subject && NameProblem(tidy) is null ? tidy : null;
        // Small models sometimes slip a stray character from another script into the fact; it is shown to grown-ups.
        var fact = new string(parsed.Fact.Trim().Where(c => c < 0x2E80).ToArray());
        pick = pick with { Shape = shape, GrowsInto = grows, Fact = fact };
        recipe = RecipeValidator.Normalise(new Recipe
        {
            Kind = request.Kind,
            Name = subject,
            Body = new Body(shape, parsed.Lobes, parsed.Hue, parsed.Size),
            // Faces only on living things; an animal shape draws its own face.
            Eyes = living && !shape.IsAnimal() ? 2 : 0,
            Motion = motion,
            Voice = parsed.Voice,
            GrowsInto = grows,
        });
        return true;
    }
}
