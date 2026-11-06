namespace Keypuncher.Director;

/// <summary>
/// One way of asking the model, for the bench to compare. The toy runs <see cref="Shipped"/>; <see cref="Baseline"/>
/// is the prompts as they first shipped, kept as the bench's reference. Each switch changes one thing about how the
/// author or the checker is asked, so a run with it on and one with it off differ in that alone. The letters match
/// the experiment plan.
/// </summary>
/// <param name="Name">How the bench names it on the command line and in its CSV.</param>
/// <param name="SchemaInPrompt">A1: the author's user prompt ends with the JSON schema, each field carrying a
/// <c>description</c> and an example. Ollama holds the schema at decode time but does not show it to the model, so
/// without this the model never reads what "subject" means and sometimes echoes the type name "string".</param>
/// <param name="FewShot">A2: the author's system prompt ends with three complete example answers.</param>
/// <param name="GentleChecker">A4: the checker is asked to recall first and is not told to be strict; the second
/// look becomes the only look.</param>
/// <param name="InstructionsInUserPrompt">A8: the standing orders go at the top of the user prompt instead of the
/// system prompt, which some small models weigh more.</param>
public sealed record PromptVariant(
    string Name,
    bool SchemaInPrompt = false,
    bool FewShot = false,
    bool GentleChecker = false,
    bool InstructionsInUserPrompt = false)
{
    /// <summary>The prompts as they first shipped; the bench's reference.</summary>
    public static readonly PromptVariant Baseline = new("baseline");

    /// <summary>What the toy runs: A2, the author shown three example answers, and the checker as it was. On the
    /// 5 Nov 2026 bench (qwen3:4b, 3 runs of the 48 author cases) the checker agreed with 65% of A2's picks against
    /// 51% for the baseline, at a median 3.7 s a pick against 6.8 s, with more different names; A1, A4, A8 and
    /// "best" all scored 44-53%. The gentle checker (A4) refused 7 in 100 real things against 2, so the checker
    /// keeps the baseline's second look.</summary>
    public static readonly PromptVariant Shipped = new("A2", FewShot: true);

    /// <summary>Every variant the bench knows, the combined one last.</summary>
    public static readonly IReadOnlyList<PromptVariant> All =
    [
        Baseline,
        new("A1", SchemaInPrompt: true),
        Shipped,
        new("A4", GentleChecker: true),
        new("A8", InstructionsInUserPrompt: true),
        new("best", SchemaInPrompt: true, FewShot: true, GentleChecker: true),
    ];

    /// <summary>The variant called <paramref name="name"/> (case does not matter), or a combination written with
    /// plus signs: "A1+A4".</summary>
    public static PromptVariant Parse(string name)
    {
        var parts = name.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("no variant named", nameof(name));
        var found = parts.Select(p => All.FirstOrDefault(v => string.Equals(v.Name, p, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"no variant called {p}; known: {string.Join(", ", All.Select(v => v.Name))}", nameof(name))).ToList();
        if (found.Count == 1) return found[0];
        return new PromptVariant(string.Join("+", found.Select(v => v.Name)),
            found.Any(v => v.SchemaInPrompt), found.Any(v => v.FewShot), found.Any(v => v.GentleChecker), found.Any(v => v.InstructionsInUserPrompt));
    }

    /// <summary>A system prompt and a user prompt as the variant wants them: with
    /// <see cref="InstructionsInUserPrompt"/> the standing orders move to the top of the user prompt.</summary>
    public (string System, string User) Arrange(string system, string user) =>
        InstructionsInUserPrompt ? ("You answer only with JSON that matches the schema you are given.", system + "\n\n" + user) : (system, user);
}
