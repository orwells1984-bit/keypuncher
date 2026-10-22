namespace Keypuncher.Core;

/// <summary>
/// One thing the child did. Several key presses within a few tens of milliseconds are
/// grouped into one event with a higher <see cref="Intensity"/> before they reach the world.
/// </summary>
/// <param name="ScanCode">Physical key position; decides where on screen the response appears.</param>
/// <param name="Character">The character the active layout gives this key, or null for a non-character key or a touch.</param>
/// <param name="X">0..1 across the screen.</param>
/// <param name="Y">0..1 down the screen.</param>
/// <param name="Intensity">1 for a single press; higher for a mash or a palm.</param>
public sealed record InputEvent(
    DateTimeOffset Timestamp,
    int ScanCode,
    string? Character,
    double X,
    double Y,
    int Intensity);
