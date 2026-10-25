using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>Builds the prompts for the director loop. Everything child-facing is constrained elsewhere;
/// the model only ever chooses shapes, colours, motion and sound. <see cref="System"/> and
/// <see cref="NewRecipe"/> are the Phase 0 prompts the bench measured; the toy uses <see cref="RealSystem"/>
/// and <see cref="RealThing"/>, which keep every pick to real things.</summary>
public static class DirectorPrompts
{
    public const string System = """
        You design gentle, friendly creatures, plants and effects for a toy used by a baby.
        Everything you make is soft, round, bright and kind. Nothing is scary, sharp or loud.
        You answer only with JSON that matches the schema you are given. Never add text around it.
        Numbers are fractions unless stated: size, speed and attack are between 0 and 1,
        decay is between 0.05 and 2 seconds, hue is 0 to 359 degrees, lobes 1 to 12, eyes 0 to 4.
        Hues snap to a small palette of bright colours, so pick the nearest natural one.
        For a real animal or dinosaur use its own shape (Cat, Dog, Fish, Bird, Cow, Sheep, Horse, Duck,
        Elephant, Rabbit, Bear, Mouse, Rex, LongNeck, Triceratops, Stegosaurus, Whale, Dolphin, Shark,
        Octopus, Crab, Turtle, Seal, Seahorse, Starfish, Jellyfish); it is always a Creature.
        """;

    public static string NewRecipe(WorldSummary world, string conceptGloss) =>
        $"""
        The world is a {world.Biome} at {world.TimeOfDay}; the mood is {world.Mood}.
        There are {world.LiveEntities} things alive. Recent play energy is {world.RecentInputEnergy:0.0} on a 0 to 1 scale.
        Things that already exist: {(world.ExistingRecipeNames.Count == 0 ? "none yet" : string.Join(", ", world.ExistingRecipeNames))}.

        Invent one new thing that a child would recognise as "{conceptGloss}", drawn with the shape vocabulary.
        Give it a short, pronounceable made-up name that is not already used, and a voice that fits its size.
        """;

    /// <summary>The director's standing orders: only real things, named plainly, drawn with the nearest shape.</summary>
    public const string RealSystem = """
        You choose real things for a picture toy used by a baby. Everything you choose must be real: a kind of
        animal that lives today; an animal that really lived long ago and is known from fossils or history
        (a dinosaur, a woolly mammoth, a dodo); a real plant, flower, tree, fruit or vegetable; a real vehicle;
        or a real everyday object.
        Never choose anything from myths, fairy tales, stories, films, cartoons, games or toys. Never a character,
        a brand, a mix of two animals, or a made-up name. Dragons, unicorns, fairies, mermaids and monsters are
        not real. If you are not sure something is real, choose something more common that you are sure of.
        subject is the thing's ordinary English name in small letters, one to three words, like "oak tree" or
        "fire engine". fact is one short true sentence about where it lives or grows, or when it lived.

        Draw it with the nearest shape:
        - Animals with a shape of their own: Cat, Dog, Fish, Bird, Cow, Sheep, Horse, Duck, Elephant, Rabbit, Bear,
          Mouse, Whale, Dolphin, Shark, Octopus, Crab, Turtle, Seal, Seahorse, Starfish, Jellyfish. A similar
          animal uses the nearest one: lion or tiger Cat, wolf or fox Dog, goat Sheep, zebra or donkey Horse,
          hen, owl or penguin Bird, goose or swan Duck, hippo Elephant, squirrel or hamster Mouse.
        - Dinosaurs: Rex for meat-eaters on two legs, LongNeck for long-necked plant-eaters, Triceratops for
          horned ones, Stegosaurus for ones with plates. A woolly mammoth is an Elephant.
        - Small animals with no shape of their own: Blob for round ones (ladybird, frog, hedgehog, snail),
          Petal for butterflies and moths, Stripe for caterpillars and worms.
        - Plants: Petal for flowers, Cloud for trees and bushes, Wave for grass, reeds and seaweed, DotField for
          berries and peas, Blob for fruit and vegetables, Star for pine trees and cactus.
        - Vehicles and objects: Stripe for boats, trains, buses and cars, Blob for balls and balloons, Cloud for
          a hot-air balloon, Star for a kite.
        Use its real colour: hue is 0 to 359 degrees and snaps to a few bright colours, so pick the nearest.
        size, speed and attack are between 0 and 1, decay between 0.05 and 2 seconds, lobes 1 to 12.
        Big things move slowly and sound low; small things are quick and sound high. Nothing sharp, scary or loud.
        Answer only with JSON that matches the schema you are given. Never add text around it.
        """;

    /// <summary>The ask for one key family. <paramref name="turnedDown"/> lists earlier picks for this request and
    /// why they were refused, so the model does not offer them again.</summary>
    public static string RealThing(DirectorRequest request, IReadOnlyList<string> turnedDown, IReadOnlyList<string>? suggestions = null)
    {
        var what = RealThingPick.Describe(request.Category) + (request.Place is { } place ? $". It should be something {place}" : "");
        var ask = request.GrowInto is { } next
            ? $"The {request.Current} has grown up. Draw the real {next} it becomes: subject must be \"{next}\"."
            : request.MustBe is { } must
            ? $"Draw the real {must}, {RealThingPick.Describe(request.Category)}: subject must be \"{must}\"."
            : $"Choose {what}. Name one particular kind, not a group word like dinosaur, bird or flower."
              + (request.Category == RealCategory.ExtinctAnimal
                  ? " Dinosaurs, Ice Age mammals, sea reptiles, flying reptiles and birds that died out all count. An animal that still lives anywhere today does not."
                  : "");
        return $"""
            {ask}
            Not any of these, which are already in the world: {(request.Existing.Count == 0 ? "none yet" : string.Join(", ", request.Existing))}.
            growsInto: if it really grows or turns into something else in nature (tadpole to frog, caterpillar to
            butterfly, acorn to oak tree, egg to chick), name that; otherwise null.
            {(turnedDown.Count == 0 ? "" : "Already turned down, do not choose again: " + string.Join("; ", turnedDown) + ".")}
            {(suggestions is { Count: > 0 } ? "If you are not sure of one, choose one of these: " + string.Join(", ", suggestions) + "." : "")}
            """;
    }
}
