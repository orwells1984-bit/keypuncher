using Keypuncher.Core;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>One pick the director tried, kept for the grown-ups' page and the sandbox.</summary>
/// <param name="Subject">The name the model gave, tidied.</param>
/// <param name="Fact">The model's fact about it, if it got that far.</param>
/// <param name="Refused">Why it was turned down, or null if it was used.</param>
/// <param name="Check">The fact checker's answer, if it got that far.</param>
public sealed record DirectorAttempt(string Subject, string? Fact, string? Refused, FactCheck? Check);

/// <summary>What came of one request: a recipe for the family, or nothing, and every attempt on the way.</summary>
public sealed record DirectorOutcome(DirectorRequest Request, Recipe? Recipe, IReadOnlyList<DirectorAttempt> Attempts, TimeSpan Took)
{
    public bool Accepted => Recipe is not null;
}

/// <summary>
/// Asks the model for a real thing for one key family, in three layers, each catching what the one before
/// lets through:
/// 1. The prompt (<see cref="DirectorPrompts.RealSystem"/>) asks only for real things by their ordinary names,
///    with a fact, and the schema's enums keep the drawing to shapes the toy knows.
/// 2. <see cref="RealThingRules"/> turns away story creatures, odd names, impossible shapes and repeats.
/// 3. <see cref="FactChecker"/>, a separate cold call, must say "real" and the right category.
/// A refused pick is named in the next try's prompt; after <see cref="MaxAttempts"/> the family stays as it is,
/// which is always safe.
/// </summary>
public sealed class RealThingDirector(ILanguageModel author, FactChecker checker)
{
    public const int MaxAttempts = 3;

    public string ModelName => author.ModelName;

    public async Task<DirectorOutcome> ProposeAsync(DirectorRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var attempts = new List<DirectorAttempt>();
        var turnedDown = new List<string>();
        for (var i = 0; i < MaxAttempts; i++)
        {
            var result = await author.GenerateJsonAsync(
                DirectorPrompts.RealSystem, DirectorPrompts.RealThing(request, turnedDown), RealThingSchema.Element, cancellationToken);
            if (!RealThingRules.TryBuild(result.Json, request, out var pick, out var recipe, out var error))
            {
                var name = pick?.Subject ?? "?";
                attempts.Add(new DirectorAttempt(name, pick?.Fact, error, null));
                if (pick is not null) turnedDown.Add($"{name} ({error})");
                continue;
            }

            var check = await checker.CheckAsync(pick!.Subject, pick.Category, pick.GrowsInto, cancellationToken);
            if (!check.Passed)
            {
                var why = check.Verdict == "real" ? "wrong category" : $"checker: {check.Verdict}";
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, why, check));
                turnedDown.Add($"{pick.Subject} ({(check.Verdict == "fictional" ? "not real" : why)})");
                continue;
            }

            // The thing is real; a life stage the checker does not confirm is dropped rather than the thing.
            if (recipe!.GrowsInto is not null && !check.GrowsIntoConfirmed) recipe = recipe with { GrowsInto = null };
            attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, null, check));
            return new DirectorOutcome(request, recipe, attempts, DateTime.UtcNow - started);
        }
        return new DirectorOutcome(request, null, attempts, DateTime.UtcNow - started);
    }
}
