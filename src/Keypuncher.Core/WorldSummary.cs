namespace Keypuncher.Core;

/// <summary>
/// The compact view of the world that the director loop hands to the LLM.
/// Kept small on purpose: the prompt should stay well under 1k tokens.
/// </summary>
public sealed record WorldSummary(
    string Biome,
    string TimeOfDay,
    string Mood,
    int LiveEntities,
    double RecentInputEnergy,
    IReadOnlyList<string> ExistingRecipeNames);
