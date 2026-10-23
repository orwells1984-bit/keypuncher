using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>
/// Turns model output into a <see cref="Recipe"/> or a reason it was rejected.
/// Ollama enforces the schema's structure and enums at decode time but ignores numeric
/// minimum/maximum, and small models routinely write "size": 8. Numbers are therefore clamped
/// into range rather than rejected; only a structurally wrong document or an unplayable note is
/// refused, in which case the old palette stays, which is always safe.
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

        if (parsed.Name.Length < 2) { error = "name length"; return false; }
        if (!IsNote(parsed.Voice.Root)) { error = "root"; return false; }

        // Ranges mirror RecipeSchema.
        recipe = parsed with
        {
            Name = parsed.Name.Length > 24 ? parsed.Name[..24] : parsed.Name,
            Body = parsed.Body with
            {
                Lobes = Math.Clamp(parsed.Body.Lobes, 1, 12),
                Hue = ((parsed.Body.Hue % 360) + 360) % 360,
                Size = Clamp(parsed.Body.Size, 0.1, 1.0),
            },
            Eyes = Math.Clamp(parsed.Eyes, 0, 4),
            Motion = parsed.Motion with { Speed = Clamp(parsed.Motion.Speed, 0.0, 1.0) },
            Voice = parsed.Voice with
            {
                Attack = Clamp(parsed.Voice.Attack, 0.0, 0.5),
                Decay = Clamp(parsed.Voice.Decay, 0.05, 2.0),
            },
        };
        return true;
    }

    /// <summary>Clamps, treating NaN as the low end so a garbage number still renders.</summary>
    private static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : Math.Clamp(v, lo, hi);

    private static bool IsNote(string s) =>
        s.Length is 2 or 3
        && s[0] is >= 'A' and <= 'G'
        && (s.Length == 2 ? s[1] is >= '3' and <= '6'
                          : s[1] == '#' && s[2] is >= '3' and <= '6');
}
