namespace Keypuncher.Core;

public enum EntityRole
{
    /// <summary>A creature or plant that stays in the landscape and is saved.</summary>
    Thing,
    /// <summary>A short-lived burst or sparkle.</summary>
    Effect,
    /// <summary>A big letter or digit that rises and fades.</summary>
    Letter,
    /// <summary>A word label under a concept's picture.</summary>
    Word,
}

/// <summary>One placed thing in the world. Positions are 0..1 of the screen; times are world seconds.</summary>
public sealed class Entity
{
    public required long Id { get; init; }
    public required EntityRole Role { get; init; }
    /// <summary>What to draw and how it sounds; null for <see cref="EntityRole.Letter"/> and <see cref="EntityRole.Word"/>.</summary>
    public Recipe? Recipe { get; init; }
    /// <summary>The glyph or word shown, for letters and words.</summary>
    public string? Text { get; init; }
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Born { get; init; }
    /// <summary>Seconds until it is removed; infinity for things.</summary>
    public double Lifetime { get; init; } = double.PositiveInfinity;
    /// <summary>Extra scale on top of the recipe's size, e.g. larger for a word's picture.</summary>
    public double Scale { get; init; } = 1;
    /// <summary>0..1 offset so that identical recipes do not move in lockstep.</summary>
    public double Phase { get; init; }
    /// <summary>1 when new or touched, decaying towards a floor; drawn as liveliness and opacity.</summary>
    public double Energy { get; set; } = 1;

    public double Age(double worldTime) => worldTime - Born;
    public bool IsPersistent => Role == EntityRole.Thing;
}
