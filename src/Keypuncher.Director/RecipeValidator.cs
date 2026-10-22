using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>
/// Turns model output into a <see cref="Recipe"/> or a reason it was rejected.
/// Ollama enforces the schema at decode time, but the app never trusts that alone:
/// a rejected recipe means the old palette stays, which is always safe.
/// </summary>
public static class RecipeValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static bool TryParse(JsonDocument? json, out Recipe? recipe, out string? error)
    {
        recipe = null;
        error = null;
        if (json is null) { error = "not JSON"; return false; }

        Recipe? parsed;
        try { parsed = json.RootElement.Deserialize<Recipe>(Options); }
        catch (JsonException e) { error = e.Message; return false; }
        if (parsed is null) { error = "empty"; return false; }

        // Range checks mirror RecipeSchema; they catch a runtime that ignored the schema.
        if (parsed.Name.Length is < 2 or > 24) error = "name length";
        else if (parsed.Body.Lobes is < 1 or > 12) error = "lobes";
        else if (parsed.Body.Hue is < 0 or > 359) error = "hue";
        else if (parsed.Body.Size is < 0.1 or > 1.0) error = "size";
        else if (parsed.Eyes is < 0 or > 4) error = "eyes";
        else if (parsed.Motion.Speed is < 0 or > 1) error = "speed";
        else if (parsed.Voice.Attack is < 0 or > 0.5) error = "attack";
        else if (parsed.Voice.Decay is < 0.05 or > 2.0) error = "decay";
        else if (!IsNote(parsed.Voice.Root)) error = "root";

        if (error is not null) return false;
        recipe = parsed;
        return true;
    }

    private static bool IsNote(string s) =>
        s.Length is 2 or 3
        && s[0] is >= 'A' and <= 'G'
        && (s.Length == 2 ? char.IsDigit(s[1]) && s[1] is >= '3' and <= '6'
                          : s[1] == '#' && s[2] is >= '3' and <= '6');
}
