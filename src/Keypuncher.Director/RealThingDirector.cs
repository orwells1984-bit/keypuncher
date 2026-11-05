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
/// 3. <see cref="FactChecker"/>, a separate cold call, must say "real" and name the same kind of thing, and a
///    living animal or plant must be confirmed as found where the scene is.
/// A refused pick is named in the next try's prompt. A request for an extinct animal also offers a few well-known
/// ones on every try and, if all <see cref="MaxAttempts"/> miss, has the model draw one of them, still checked.
/// Any other request that misses leaves the family as it is, which is always safe.
/// </summary>
/// <param name="random">Picks the well-known extinct animals to offer.</param>
/// <param name="variant">How the author is asked; the toy uses <see cref="PromptVariant.Baseline"/>, the bench tries others.</param>
public sealed class RealThingDirector(ILanguageModel author, FactChecker checker, Random? random = null, PromptVariant? variant = null)
{
    private readonly Random _random = random ?? new Random();
    private readonly PromptVariant _variant = variant ?? PromptVariant.Baseline;

    public const int MaxAttempts = 3;

    public string ModelName => author.ModelName;

    public async Task<DirectorOutcome> ProposeAsync(DirectorRequest request, CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        var attempts = new List<DirectorAttempt>();
        var turnedDown = new List<string>();
        for (var i = 0; i < MaxAttempts; i++)
        {
            var suggestions = Suggestions(request, attempts);
            var result = await AskAsync(request, turnedDown, suggestions, cancellationToken);
            if (!RealThingRules.TryBuild(result.Json, request, out var pick, out var recipe, out var error))
            {
                var name = pick?.Subject ?? "?";
                attempts.Add(new DirectorAttempt(name, pick?.Fact, error, null));
                if (pick is not null) turnedDown.Add($"{name} ({error})");
                continue;
            }

            // The same name again gets the same answer, so it is not checked again. In a test run the model offered
            // "wadi rum gazelle" three times running.
            if (attempts.Any(a => a.Refused is not null && string.Equals(a.Subject, pick!.Subject, StringComparison.OrdinalIgnoreCase)))
            {
                attempts.Add(new DirectorAttempt(pick!.Subject, pick.Fact, "turned down already", null));
                continue;
            }

            var check = await checker.CheckAsync(pick!.Subject, pick.GrowsInto, pick.Category, cancellationToken);
            if (!check.Passes(pick.Category))
            {
                var why = check.Verdict == "real" ? $"checker: {check.Kind}, not {pick.Category}" : $"checker: {check.Verdict}";
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, why, check));
                turnedDown.Add($"{pick.Subject} ({check.Verdict switch
                {
                    "fictional" => "not real",
                    "real" when check.Kind == nameof(RealCategory.Animal) && pick.Category == RealCategory.ExtinctAnimal => "still alive today, so not extinct",
                    "real" => $"not {RealThingPick.Describe(pick.Category)}",
                    _ => "not sure it is real",
                }})");
                continue;
            }

            // Real, but it must also belong where the scene is.
            if (request.Here is { } here && request.GrowInto is null && pick.Category is RealCategory.Animal or RealCategory.Plant
                && !await checker.FitsAsync(pick.Subject, pick.Category, here, cancellationToken))
            {
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, $"checker: not found {here}", check));
                turnedDown.Add($"{pick.Subject} (not found {here})");
                continue;
            }

            // The thing is real; a life stage the checker does not confirm is dropped rather than the thing.
            if (recipe!.GrowsInto is not null && !check.GrowsIntoConfirmed) recipe = recipe with { GrowsInto = null };
            attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, null, check));
            return new DirectorOutcome(request, recipe, attempts, DateTime.UtcNow - started);
        }

        // An extinct animal must not come back empty: draw a well-known one not yet in the world, checked as usual.
        if (Suggestions(request, attempts) is [var fallback, ..])
        {
            var forced = request with { MustBe = fallback };
            var result = await AskAsync(forced, [], null, cancellationToken);
            if (RealThingRules.TryBuild(result.Json, forced, out var pick, out var recipe, out var error))
            {
                var check = await checker.CheckAsync(pick!.Subject, pick.GrowsInto, pick.Category, cancellationToken);
                if (check.Passes(pick.Category))
                {
                    if (recipe!.GrowsInto is not null && !check.GrowsIntoConfirmed) recipe = recipe with { GrowsInto = null };
                    attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, null, check));
                    return new DirectorOutcome(request, recipe, attempts, DateTime.UtcNow - started);
                }
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, $"checker: {check.Verdict}, {check.Kind}", check));
            }
            else attempts.Add(new DirectorAttempt(pick?.Subject ?? fallback, pick?.Fact, error, null));
        }
        return new DirectorOutcome(request, null, attempts, DateTime.UtcNow - started);
    }

    /// <summary>One try at the author: the prompts as the variant arranges them.</summary>
    private Task<GenerationResult> AskAsync(DirectorRequest request, IReadOnlyList<string> turnedDown, IReadOnlyList<string>? suggestions, CancellationToken cancellationToken)
    {
        var (system, user, schema) = DirectorPrompts.Author(request, turnedDown, suggestions, _variant);
        return author.GenerateJsonAsync(system, user, schema, cancellationToken);
    }

    /// <summary>For an extinct animal, a few well-known ones not yet in the world or already tried; the small model
    /// otherwise tends to offer a living animal or a dinosaur that is already there.</summary>
    private List<string>? Suggestions(DirectorRequest request, List<DirectorAttempt> attempts)
    {
        if (request.Category != RealCategory.ExtinctAnimal || request.GrowInto is not null || request.MustBe is not null) return null;
        var free = RealThingPick.WellKnownExtinct
            .Where(n => !request.Existing.Any(e => RealThingRules.SameThing(e, n)) && !attempts.Any(a => RealThingRules.SameThing(a.Subject, n)))
            .ToList();
        return [.. free.OrderBy(_ => _random.Next()).Take(5)];
    }
}
