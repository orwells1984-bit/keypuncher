using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>What the director is asked for: a new real thing for one key family.</summary>
/// <param name="Slot">The family in <see cref="KeyFamilies"/>.</param>
/// <param name="Kind">The family's kind, which it keeps: a plant family only ever holds plants.</param>
/// <param name="Current">The family's thing now; null while it is still an invented built-in one.</param>
/// <param name="Existing">Names of the real things already in the world, so the pick is something new.</param>
/// <param name="GrowInto">When the current thing really grows into something (a tadpole), the next pick must be that.</param>
public sealed record DirectorRequest(
    int Slot,
    RecipeKind Kind,
    string? Current,
    IReadOnlyList<string> Existing,
    string? GrowInto = null,
    string Biome = "meadow");

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

    /// <summary>Why <paramref name="name"/> cannot be a real thing's ordinary name, or null if it could be.</summary>
    public static string? NameProblem(string name)
    {
        if (name.Length is < 2 or > 24) return "name length";
        if (!name.All(c => c is (>= 'a' and <= 'z') or ' ' or '-' or '\'' or '.')) return "not a plain English name";
        if (name.Count(c => c == ' ') > 3) return "too many words";
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
        catch (JsonException e) { error = "bad JSON: " + e.Message; return false; }
        if (parsed is null) { error = "empty"; return false; }

        var subject = Tidy(parsed.Subject);
        pick = parsed with { Subject = subject };
        if (NameProblem(subject) is { } problem) { error = problem; return false; }
        if (string.IsNullOrWhiteSpace(parsed.Fact)) { error = "no fact"; return false; }

        if (request.GrowInto is { } next && subject != Tidy(next)) { error = $"should have been {next}"; return false; }
        if (request.GrowInto is null && (request.Existing.Any(e => Tidy(e) == subject) || (request.Current is { } now && Tidy(now) == subject)))
        {
            error = "already in the world";
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

        var grows = parsed.GrowsInto is { } g && Tidy(g) is var tidy && tidy != subject && NameProblem(tidy) is null ? tidy : null;
        pick = pick with { Shape = shape, GrowsInto = grows, Fact = parsed.Fact.Trim() };
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
