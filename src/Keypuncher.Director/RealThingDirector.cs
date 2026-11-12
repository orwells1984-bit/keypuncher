using Keypuncher.Core;
using Keypuncher.Inference;

namespace Keypuncher.Director;

/// <summary>One pick the director tried, kept for the grown-ups' page and the sandbox.</summary>
/// <param name="Subject">The name the model gave, tidied.</param>
/// <param name="Fact">The model's fact about it, if it got that far.</param>
/// <param name="Refused">Why it was turned down, or null if it was used.</param>
/// <param name="Check">The fact checker's answer, if it got that far.</param>
public sealed record DirectorAttempt(string Subject, string? Fact, string? Refused, FactCheck? Check)
{
    /// <summary>A refusal that still holds after this request (not real, a place, not found here), for the loop to
    /// remember through the play; null when the refusal was about this request only, or the pick was used.</summary>
    public TurnedDown? Lasting { get; init; }
    /// <summary>What the director changed about a pick it used, for the log: its spelling, or the kind it is filed as.</summary>
    public string? Note { get; init; }
}

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
/// 3. <see cref="FactChecker"/>, a separate cold call, must say "real" and name the same kind of thing (a vehicle
///    and an object counting as each other, and a few well-known names passing unasked), and a living animal or
///    plant must be confirmed as found where the scene is: continent and climate by the model, and the element by
///    code (<see cref="Habitat"/>), so no shark at a lake.
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
        var turnedDown = new List<string>(EarlierTurnedDown(request));
        var ideas = new List<string>();
        string? idea = null;
        for (var i = 0; i < MaxAttempts; i++)
        {
            // After a miss, the model's own next idea that is still free is asked for by name: asked again openly, a
            // small model mostly gives the same answer.
            var asking = idea is null ? request : request with { MustBe = idea };
            var suggestions = idea is null ? Suggestions(request, attempts) : null;
            var result = await AskAsync(asking, turnedDown, suggestions, cancellationToken);
            var built = RealThingRules.TryBuild(result.Json, asking, out var pick, out var recipe, out var error);
            if (pick?.Ideas is { } offered) ideas.AddRange(offered);
            if (!built)
            {
                var name = pick?.Subject ?? "?";
                attempts.Add(new DirectorAttempt(name, pick?.Fact, error, null)
                {
                    Lasting = error is "the place, not a thing in it" or "from a story, not real" && pick is not null
                        ? new TurnedDown(name, error == "from a story, not real" ? "not real" : "a place, not a thing in it", request.Category) : null,
                });
                if (pick is not null) turnedDown.Add($"{name} ({error})");
                idea = NextIdea(request, ideas, attempts);
                continue;
            }

            // The same name again gets the same answer, so it is not checked again. In a test run the model offered
            // "wadi rum gazelle" three times running; in another, "sand dollar" in request after request.
            if (attempts.Any(a => a.Refused is not null && RealThingRules.SameThing(a.Subject, pick!.Subject)))
            {
                attempts.Add(new DirectorAttempt(pick!.Subject, pick.Fact, "turned down already", null));
                idea = NextIdea(request, ideas, attempts);
                continue;
            }
            if (request.TurnedDownEarlier(pick!.Subject) is { } earlier)
            {
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, $"turned down earlier: {earlier.Why}", null));
                idea = NextIdea(request, ideas, attempts);
                continue;
            }

            var check = await checker.CheckAsync(pick.Subject, pick.GrowsInto, pick.Category, cancellationToken);
            var notes = new List<string>();

            // A near miss in the spelling is put right, so the picture search finds it ("lilly pillay" is a lilly pilly).
            if (FactChecker.Respelt(pick.Subject, check.Spelling) is { } respelt)
            {
                if (!RealThingRules.TryBuild(pick with { Subject = respelt }, asking, out var fixedPick, out var fixedRecipe, out var fixedError))
                {
                    // Kept under the name the author gave, so offering it again is "turned down already", unasked.
                    attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, fixedError, check) { Note = $"spelt {respelt}" });
                    turnedDown.Add($"{respelt} ({fixedError})");
                    idea = NextIdea(request, ideas, attempts);
                    continue;
                }
                notes.Add($"spelt {respelt}, not {pick.Subject}");
                (pick, recipe) = (fixedPick!, fixedRecipe!);
            }

            var filedAs = pick.Category;
            if (!check.Passes(pick.Category))
            {
                // Real, but another kind of thing than the author was asked for. The author can only answer in the
                // category asked for, so a sand dollar asked for as an object came back as one, and the checker rightly
                // said animal, twice a request for a whole evening. When the family can hold the checker's kind, the
                // pick is used and filed as that.
                if (Refile(check, request) is { } kind
                    && RealThingRules.TryBuild(pick with { Category = kind }, asking, out var refiledPick, out var refiledRecipe, out _, refiled: true))
                {
                    notes.Add($"filed as {kind}: the checker said \"{check.WhatItIs}\"");
                    (pick, recipe, filedAs) = (refiledPick!, refiledRecipe!, kind);
                }
                else
                {
                    var why = check.Verdict == "real" ? $"checker: {check.Kind}, not {pick.Category}" : $"checker: {check.Verdict}";
                    var told = check.Verdict switch
                    {
                        "fictional" => "not real",
                        "real" when check.Kind == nameof(RealCategory.Animal) && pick.Category == RealCategory.ExtinctAnimal => "still alive today, so not extinct",
                        "real" => $"not {RealThingPick.Describe(pick.Category)}",
                        _ => "not sure it is real",
                    };
                    attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, why, check)
                    {
                        Lasting = check.Verdict is "fictional" or "unsure" ? new TurnedDown(pick.Subject, told, pick.Category) : null,
                    });
                    turnedDown.Add($"{pick.Subject} ({told})");
                    idea = NextIdea(request, ideas, attempts);
                    continue;
                }
            }
            // A vehicle and an object pass for each other; what the checker called it is what it is filed as.
            else if (check.Verdict == "real" && Enum.TryParse<RealCategory>(check.Kind, out var said) && said != filedAs)
                filedAs = said;

            // Real, but it must also belong where the scene is.
            if (request.Here is { } here && request.GrowInto is null && filedAs is RealCategory.Animal or RealCategory.Plant
                && !await checker.FitsAsync(pick.Subject, filedAs, here, request.Landscape, pick.Shape, cancellationToken))
            {
                attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, $"checker: not found {here}", check)
                {
                    Lasting = new TurnedDown(pick.Subject, $"not found {here}", pick.Category, request.Here),
                });
                turnedDown.Add($"{pick.Subject} (not found {here})");
                idea = NextIdea(request, ideas, attempts);
                continue;
            }

            // The thing is real; a life stage the checker does not confirm is dropped rather than the thing.
            if (recipe!.GrowsInto is not null && !check.GrowsIntoConfirmed) recipe = recipe with { GrowsInto = null };
            recipe = recipe with { Category = filedAs };
            attempts.Add(new DirectorAttempt(pick.Subject, pick.Fact, null, check) { Note = notes.Count == 0 ? null : string.Join("; ", notes) });
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

    /// <summary>The kind the checker named, when it is real, differs from the claim, and the family can hold it; null
    /// otherwise. Not for a request for an extinct animal (a living one is what the request is there to avoid), a
    /// growth or a forced last resort, or a plant family, which takes plants only.</summary>
    public static RealCategory? Refile(FactCheck check, DirectorRequest request) =>
        request.GrowInto is null && request.Category != RealCategory.ExtinctAnimal && request.Kind == RecipeKind.Creature
        && check.Verdict == "real" && Enum.TryParse<RealCategory>(check.Kind, out var kind) && Enum.IsDefined(kind)
        && RealThingPick.KindOf(kind) == request.Kind && !FactCheck.SameKind(kind, request.Category)
            ? kind : null;

    /// <summary>The first of the model's ideas this request has not tried and that is not already in the world or turned
    /// down, or null to ask openly again. None while growing, which has its answer already.</summary>
    public static string? NextIdea(DirectorRequest request, IEnumerable<string> ideas, IReadOnlyList<DirectorAttempt> attempts)
    {
        if (request.GrowInto is not null) return null;
        foreach (var raw in ideas)
        {
            var name = RealThingRules.Tidy(raw);
            if (RealThingRules.NameProblem(name) is not null || attempts.Any(a => RealThingRules.SameThing(a.Subject, name))) continue;
            var key = RealThingRules.PlaceKey(name, request.Place, request.Here);
            if (key.Length == 0 || request.TurnedDownEarlier(name) is not null) continue;
            bool Same(string other) => RealThingRules.SameThing(other, name) || RealThingRules.SameThing(RealThingRules.PlaceKey(other, request.Place, request.Here), key);
            if (request.Existing.Any(Same) || (request.Current is { } now && Same(now))) continue;
            return name;
        }
        return null;
    }

    /// <summary>Earlier refusals in this play of things of the kind asked for, as the prompt names them.</summary>
    private static IEnumerable<string> EarlierTurnedDown(DirectorRequest request) =>
        request.GrowInto is not null || request.TurnedDownBefore is null ? []
        : request.TurnedDownBefore.Where(t => t.Category == request.Category && (t.Here is null || t.Here == request.Here))
            .Take(MaxEarlierNamed).Select(t => $"{t.Name} ({t.Why})");

    /// <summary>How many earlier refusals a prompt names at most; the rest are still refused, unasked.</summary>
    public const int MaxEarlierNamed = 8;

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
