namespace Keypuncher.Core;

/// <summary>A raw key transition as the platform reports it, before any grouping.</summary>
/// <param name="ScanCode">Set 1 scancode: the physical key.</param>
/// <param name="Character">What the Icelandic layout gives the key unshifted, or null for a key with no character.
/// For a dead key this is the accent itself (´, ¨, `, ^, ~ or °).</param>
/// <param name="IsDeadKey">True for an accent key that combines with the next letter.</param>
public readonly record struct KeyPress(
    DateTimeOffset Timestamp,
    int ScanCode,
    string? Character,
    bool IsDeadKey,
    bool IsDown,
    bool Ctrl = false,
    bool Shift = false,
    bool Alt = false);
