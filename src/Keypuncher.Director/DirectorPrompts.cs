using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>Builds the prompts for the director loop. Everything child-facing is constrained elsewhere;
/// the model only ever chooses shapes, colours, motion and sound.</summary>
public static class DirectorPrompts
{
    public const string System = """
        You design gentle, friendly creatures, plants and effects for a toy used by a baby.
        Everything you make is soft, round, bright and kind. Nothing is scary, sharp or loud.
        You answer only with JSON that matches the schema you are given. Never add text around it.
        """;

    public static string NewRecipe(WorldSummary world, string conceptGloss) =>
        $"""
        The world is a {world.Biome} at {world.TimeOfDay}; the mood is {world.Mood}.
        There are {world.LiveEntities} things alive. Recent play energy is {world.RecentInputEnergy:0.0} on a 0 to 1 scale.
        Things that already exist: {(world.ExistingRecipeNames.Count == 0 ? "none yet" : string.Join(", ", world.ExistingRecipeNames))}.

        Invent one new thing that a child would recognise as "{conceptGloss}", drawn with the shape vocabulary.
        Give it a short, pronounceable made-up name that is not already used, and a voice that fits its size.
        """;
}
