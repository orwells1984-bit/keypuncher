namespace Keypuncher.Core;

/// <summary>
/// Hand-written recipes for Phase 1, before the director exists. They are the starting palette, the
/// stand-in pictures for the concept words, and later the fallback whenever the model has nothing
/// valid to offer.
/// </summary>
public static class BuiltInRecipes
{
    /// <summary>Things a key can make. A key keeps the same family, so the list order is part of the toy's
    /// behaviour: append, do not reorder.</summary>
    public static readonly IReadOnlyList<Recipe> Palette =
    [
        Make(RecipeKind.Creature, "puffbird", Shape.Blob, 5, 340, 0.55, 2, MotionType.Hop, 0.6, Waveform.Sine, 0.01, 0.6, "pentatonic", "C5"),
        Make(RecipeKind.Plant, "petalpop", Shape.Petal, 6, 300, 0.6, 0, MotionType.Sway, 0.4, Waveform.Triangle, 0.02, 0.9, "pentatonic", "C4"),
        Make(RecipeKind.Creature, "driftle", Shape.Cloud, 4, 200, 0.6, 2, MotionType.Drift, 0.3, Waveform.Sine, 0.08, 1.2, "pentatonic", "G4"),
        Make(RecipeKind.Plant, "twinkweed", Shape.Star, 5, 50, 0.45, 0, MotionType.Sway, 0.5, Waveform.Triangle, 0.01, 0.5, "pentatonic", "E5"),
        Make(RecipeKind.Creature, "stripfin", Shape.Stripe, 3, 180, 0.5, 1, MotionType.Drift, 0.6, Waveform.Triangle, 0.01, 0.4, "pentatonic", "A4"),
        Make(RecipeKind.Creature, "bloblet", Shape.Blob, 3, 120, 0.4, 2, MotionType.Hop, 0.9, Waveform.Square, 0.005, 0.25, "pentatonic", "C5"),
        Make(RecipeKind.Plant, "wiggrass", Shape.Wave, 4, 100, 0.5, 0, MotionType.Sway, 0.6, Waveform.Sine, 0.03, 0.7, "pentatonic", "D4"),
        Make(RecipeKind.Creature, "spinnet", Shape.Star, 6, 270, 0.4, 2, MotionType.Spin, 0.3, Waveform.Sine, 0.01, 0.8, "pentatonic", "G5"),
        Make(RecipeKind.Plant, "dotbush", Shape.DotField, 7, 20, 0.55, 0, MotionType.Sway, 0.3, Waveform.Triangle, 0.02, 0.6, "pentatonic", "A3"),
        Make(RecipeKind.Creature, "plumpsy", Shape.Blob, 8, 30, 0.7, 2, MotionType.Sway, 0.3, Waveform.Sine, 0.05, 1.0, "pentatonic", "E4"),
        Make(RecipeKind.Plant, "tuftlet", Shape.Petal, 9, 160, 0.4, 0, MotionType.Sway, 0.5, Waveform.Triangle, 0.01, 0.5, "pentatonic", "D5"),
        Make(RecipeKind.Creature, "cloudkin", Shape.Cloud, 6, 230, 0.75, 2, MotionType.Drift, 0.2, Waveform.Sine, 0.12, 1.6, "pentatonic", "C4"),
        // Real animals and dinosaurs, so that keys and taps make things a child can name.
        .. Animals.All,
    ];

    /// <summary>One recipe per animal shape, in its natural colour as near as the palette allows. Front-facing
    /// animals have two eyes, side-on ones one; the painter gives every animal a face whatever this says.</summary>
    public static class Animals
    {
        public static readonly Recipe Cat = Make(RecipeKind.Creature, "cat", Shape.Cat, 3, 30, 0.6, 2, MotionType.Sway, 0.2, Waveform.Triangle, 0.05, 0.8, "pentatonic", "G4");
        public static readonly Recipe Dog = Make(RecipeKind.Creature, "dog", Shape.Dog, 1, 22, 0.65, 2, MotionType.Hop, 0.5, Waveform.Triangle, 0.01, 0.4, "pentatonic", "D4");
        public static readonly Recipe Fish = Make(RecipeKind.Creature, "fish", Shape.Fish, 3, 28, 0.5, 1, MotionType.Drift, 0.5, Waveform.Sine, 0.01, 0.4, "pentatonic", "A4");
        public static readonly Recipe Bird = Make(RecipeKind.Creature, "bird", Shape.Bird, 1, 215, 0.45, 1, MotionType.Hop, 0.7, Waveform.Sine, 0.005, 0.3, "pentatonic", "E5");
        public static readonly Recipe Cow = Make(RecipeKind.Creature, "cow", Shape.Cow, 4, 25, 0.8, 2, MotionType.Sway, 0.15, Waveform.Triangle, 0.08, 1.4, "pentatonic", "C3");
        public static readonly Recipe Sheep = Make(RecipeKind.Creature, "sheep", Shape.Sheep, 1, 25, 0.65, 2, MotionType.Sway, 0.2, Waveform.Triangle, 0.05, 0.9, "pentatonic", "E4");
        public static readonly Recipe Horse = Make(RecipeKind.Creature, "horse", Shape.Horse, 1, 22, 0.8, 1, MotionType.Hop, 0.3, Waveform.Triangle, 0.02, 0.6, "pentatonic", "G3");
        public static readonly Recipe Duck = Make(RecipeKind.Creature, "duck", Shape.Duck, 1, 50, 0.5, 1, MotionType.Drift, 0.4, Waveform.Square, 0.01, 0.3, "pentatonic", "F4");
        public static readonly Recipe Elephant = Make(RecipeKind.Creature, "elephant", Shape.Elephant, 1, 215, 0.95, 1, MotionType.Sway, 0.1, Waveform.Triangle, 0.1, 1.6, "pentatonic", "C3");
        public static readonly Recipe Rabbit = Make(RecipeKind.Creature, "rabbit", Shape.Rabbit, 1, 25, 0.55, 2, MotionType.Hop, 0.8, Waveform.Sine, 0.005, 0.25, "pentatonic", "A5");
        public static readonly Recipe Bear = Make(RecipeKind.Creature, "bear", Shape.Bear, 1, 22, 0.85, 2, MotionType.Sway, 0.2, Waveform.Triangle, 0.05, 1.0, "pentatonic", "C3");
        public static readonly Recipe Mouse = Make(RecipeKind.Creature, "mouse", Shape.Mouse, 1, 260, 0.45, 2, MotionType.Hop, 1.0, Waveform.Sine, 0.005, 0.2, "pentatonic", "C6");
        public static readonly Recipe Rex = Make(RecipeKind.Creature, "rex", Shape.Rex, 1, 125, 0.9, 1, MotionType.Sway, 0.2, Waveform.Saw, 0.05, 1.0, "pentatonic", "C3");
        public static readonly Recipe LongNeck = Make(RecipeKind.Creature, "longneck", Shape.LongNeck, 1, 180, 0.95, 1, MotionType.Sway, 0.1, Waveform.Sine, 0.15, 1.8, "lydian", "D3");
        public static readonly Recipe Triceratops = Make(RecipeKind.Creature, "triceratops", Shape.Triceratops, 1, 280, 0.8, 1, MotionType.Sway, 0.15, Waveform.Triangle, 0.05, 1.2, "pentatonic", "E3");
        public static readonly Recipe Stegosaurus = Make(RecipeKind.Creature, "stegosaurus", Shape.Stegosaurus, 5, 100, 0.8, 1, MotionType.Sway, 0.15, Waveform.Triangle, 0.05, 1.2, "pentatonic", "A3");

        public static readonly Recipe Whale = Make(RecipeKind.Creature, "whale", Shape.Whale, 1, 215, 0.95, 1, MotionType.Drift, 0.1, Waveform.Sine, 0.2, 2.0, "lydian", "C3");
        public static readonly Recipe Dolphin = Make(RecipeKind.Creature, "dolphin", Shape.Dolphin, 1, 195, 0.75, 1, MotionType.Drift, 0.6, Waveform.Sine, 0.01, 0.4, "pentatonic", "E5");
        public static readonly Recipe Shark = Make(RecipeKind.Creature, "shark", Shape.Shark, 1, 225, 0.8, 1, MotionType.Drift, 0.3, Waveform.Triangle, 0.05, 1.0, "pentatonic", "D3");
        public static readonly Recipe Octopus = Make(RecipeKind.Creature, "octopus", Shape.Octopus, 8, 330, 0.7, 2, MotionType.Sway, 0.3, Waveform.Sine, 0.05, 0.9, "whole-tone", "G4");
        public static readonly Recipe Crab = Make(RecipeKind.Creature, "crab", Shape.Crab, 1, 2, 0.6, 2, MotionType.Sway, 0.5, Waveform.Square, 0.005, 0.2, "pentatonic", "A4");
        public static readonly Recipe Turtle = Make(RecipeKind.Creature, "turtle", Shape.Turtle, 3, 125, 0.75, 1, MotionType.Drift, 0.15, Waveform.Triangle, 0.1, 1.4, "pentatonic", "F3");
        public static readonly Recipe Seal = Make(RecipeKind.Creature, "seal", Shape.Seal, 1, 22, 0.7, 1, MotionType.Sway, 0.3, Waveform.Triangle, 0.02, 0.5, "pentatonic", "G4");
        public static readonly Recipe Seahorse = Make(RecipeKind.Creature, "seahorse", Shape.Seahorse, 3, 45, 0.6, 1, MotionType.Drift, 0.3, Waveform.Sine, 0.02, 0.6, "lydian", "B4");
        public static readonly Recipe Starfish = Make(RecipeKind.Creature, "starfish", Shape.Starfish, 5, 28, 0.55, 2, MotionType.Sway, 0.15, Waveform.Triangle, 0.02, 0.7, "pentatonic", "C5");
        public static readonly Recipe Jellyfish = Make(RecipeKind.Creature, "jellyfish", Shape.Jellyfish, 5, 290, 0.6, 2, MotionType.Drift, 0.2, Waveform.Sine, 0.15, 1.6, "whole-tone", "E5");

        public static readonly IReadOnlyList<Recipe> All =
        [
            Cat, Dog, Fish, Bird, Cow, Sheep, Horse, Duck, Elephant, Rabbit, Bear, Mouse, Rex, LongNeck, Triceratops, Stegosaurus,
            Whale, Dolphin, Shark, Octopus, Crab, Turtle, Seal, Seahorse, Starfish, Jellyfish,
        ];
    }

    /// <summary>For a mash: a burst that fades by itself.</summary>
    public static readonly Recipe Burst =
        Make(RecipeKind.Effect, "burst", Shape.DotField, 10, 50, 0.9, 0, MotionType.Burst, 0.8, Waveform.Sine, 0.01, 1.2, "pentatonic", "C4");

    /// <summary>For a dead key (the accent key) pressed on its own.</summary>
    public static readonly Recipe Sparkle =
        Make(RecipeKind.Effect, "sparkle", Shape.Star, 4, 55, 0.35, 0, MotionType.Burst, 0.6, Waveform.Triangle, 0.005, 0.4, "pentatonic", "E6");

    /// <summary>Pictures for the starter concept ids until the director draws them. Only living things get
    /// eyes: a face on the sun or a house teaches that things are alive.</summary>
    public static readonly IReadOnlyDictionary<string, Recipe> Concepts = new Dictionary<string, Recipe>
    {
        ["sun"] = Make(RecipeKind.Effect, "sun", Shape.Star, 10, 45, 0.9, 0, MotionType.Spin, 0.1, Waveform.Sine, 0.05, 1.5, "major", "C4"),
        ["cat"] = Animals.Cat,
        ["fish"] = Animals.Fish,
        ["bird"] = Animals.Bird,
        ["moon"] = Make(RecipeKind.Effect, "moon", Shape.Cloud, 2, 55, 0.7, 0, MotionType.Drift, 0.1, Waveform.Sine, 0.2, 2.0, "lydian", "D4"),
        ["flower"] = Make(RecipeKind.Plant, "flower", Shape.Petal, 6, 310, 0.75, 0, MotionType.Sway, 0.3, Waveform.Triangle, 0.02, 0.9, "major", "C5"),
        ["house"] = Make(RecipeKind.Plant, "house", Shape.Stripe, 4, 15, 0.8, 0, MotionType.Still, 0, Waveform.Triangle, 0.02, 0.8, "major", "C4"),
        ["tree"] = Make(RecipeKind.Plant, "tree", Shape.Cloud, 5, 120, 0.9, 0, MotionType.Sway, 0.15, Waveform.Sine, 0.05, 1.2, "pentatonic", "G3"),
        ["ball"] = Make(RecipeKind.Creature, "ball", Shape.Blob, 1, 0, 0.45, 0, MotionType.Hop, 0.9, Waveform.Square, 0.005, 0.2, "major", "C5"),
        ["dog"] = Animals.Dog,
        ["egg"] = Make(RecipeKind.Creature, "egg", Shape.Blob, 1, 50, 0.4, 0, MotionType.Still, 0, Waveform.Sine, 0.02, 0.5, "pentatonic", "A4"),
        ["star"] = Make(RecipeKind.Effect, "star", Shape.Star, 5, 50, 0.6, 2, MotionType.Spin, 0.2, Waveform.Sine, 0.01, 1.0, "lydian", "E5"),
        ["apple"] = Make(RecipeKind.Plant, "apple", Shape.Blob, 2, 0, 0.5, 0, MotionType.Still, 0, Waveform.Triangle, 0.01, 0.5, "major", "G4"),
        ["bear"] = Animals.Bear,
        ["duck"] = Animals.Duck,
        ["mouse"] = Animals.Mouse,
        ["cloud"] = Make(RecipeKind.Effect, "cloud", Shape.Cloud, 5, 210, 0.9, 0, MotionType.Drift, 0.15, Waveform.Noise, 0.3, 1.5, "pentatonic", "C4"),
        ["boat"] = Make(RecipeKind.Creature, "boat", Shape.Stripe, 2, 190, 0.7, 0, MotionType.Sway, 0.3, Waveform.Triangle, 0.05, 0.8, "major", "G3"),
        ["butterfly"] = Make(RecipeKind.Creature, "butterfly", Shape.Petal, 4, 280, 0.5, 2, MotionType.Drift, 0.7, Waveform.Sine, 0.01, 0.5, "lydian", "A5"),
        ["cow"] = Animals.Cow,
        ["sheep"] = Animals.Sheep,
        ["horse"] = Animals.Horse,
        ["elephant"] = Animals.Elephant,
        ["rabbit"] = Animals.Rabbit,
        ["rex"] = Animals.Rex,
        ["longneck"] = Animals.LongNeck,
        ["triceratops"] = Animals.Triceratops,
        ["stegosaurus"] = Animals.Stegosaurus,
        ["whale"] = Animals.Whale,
        ["dolphin"] = Animals.Dolphin,
        ["shark"] = Animals.Shark,
        ["octopus"] = Animals.Octopus,
        ["crab"] = Animals.Crab,
        ["turtle"] = Animals.Turtle,
        ["seal"] = Animals.Seal,
        ["seahorse"] = Animals.Seahorse,
        ["starfish"] = Animals.Starfish,
        ["jellyfish"] = Animals.Jellyfish,
    };

    public static Recipe ForConcept(string conceptId) =>
        Concepts.TryGetValue(conceptId, out var r) ? r : Palette[(int)((uint)conceptId.GetHashCode(StringComparison.Ordinal) % Palette.Count)];

    private static Recipe Make(
        RecipeKind kind, string name, Shape shape, int lobes, int hue, double size, int eyes,
        MotionType motion, double speed, Waveform wave, double attack, double decay, string scale, string root) =>
        new()
        {
            Kind = kind,
            Name = name,
            Body = new Body(shape, lobes, hue, size),
            Eyes = eyes,
            Motion = new Motion(motion, speed),
            Voice = new Voice(wave, attack, decay, scale, root),
        };
}
