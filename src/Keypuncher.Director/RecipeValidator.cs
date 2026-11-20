using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>
/// Keeps a recipe the model wrote playable. Ollama enforces the schema's structure and enums at
/// decode time but ignores numeric minimum/maximum, and small models routinely write "size": 8.
/// Numbers are therefore clamped into range rather than rejected; only an unplayable note is refused.
/// </summary>
public static class RecipeValidator
{
    /// <summary>Clamps every number into the schema's range; ranges mirror the schema in <see cref="RealThingSchema"/>.</summary>
    public static Recipe Normalise(Recipe parsed) => parsed with
    {
        // An animal is alive whatever the model says: a "Plant" cat would grow a stem.
        Kind = parsed.Body.Shape.IsAnimal() ? RecipeKind.Creature : parsed.Kind,
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

    /// <summary>Clamps, treating NaN as the low end so a garbage number still renders.</summary>
    private static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : Math.Clamp(v, lo, hi);

    public static bool IsNote(string s) =>
        s.Length is 2 or 3
        && s[0] is >= 'A' and <= 'G'
        && (s.Length == 2 ? s[1] is >= '3' and <= '6'
                          : s[1] == '#' && s[2] is >= '3' and <= '6');
}
