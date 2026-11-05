namespace Keypuncher.Director;

/// <summary>
/// One way of asking the model, for the bench to compare. The toy runs <see cref="Baseline"/>, which is exactly
/// the prompts that shipped; each switch changes one thing about how the author or the checker is asked, so a
/// run with it on and one with it off differ in that alone. The letters match the experiment plan.
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
    /// <summary>What the toy runs: the prompts as shipped.</summary>
    public static readonly PromptVariant Baseline = new("baseline");

    /// <summary>Every variant the bench knows, the combined one last.</summary>
    public static readonly IReadOnlyList<PromptVariant> All =
    [
        Baseline,
        new("A1", SchemaInPrompt: true),
        new("A2", FewShot: true),
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
