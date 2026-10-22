namespace Keypuncher.Core;

/// <summary>
/// Where a physical key sits on an ISO keyboard (the Icelandic layout is ISO), as a 0..1 position
/// on the screen. Keys on the left respond on the left, the top row near the top, so where the baby
/// hits matters. Works from Set 1 scancodes, which is what the Windows low-level hook reports, so it
/// does not depend on the active layout.
/// </summary>
public static class KeyboardGeometry
{
    private const double Columns = 14.5;

    // Row number, first scancode in the row, column of that key; keys in a row are consecutive.
    private static readonly (int Row, int FirstScan, int LastScan, double FirstColumn)[] Runs =
    [
        (0, 0x3B, 0x44, 2.0),   // F1..F10
        (1, 0x02, 0x0D, 1.0),   // 1 .. the key left of backspace
        (2, 0x10, 0x1B, 1.5),   // Q .. Ð
        (3, 0x1E, 0x28, 1.75),  // A .. Æ
        (4, 0x2C, 0x35, 2.25),  // Z .. -
    ];

    private static readonly Dictionary<int, (int Row, double Column)> Singles = new()
    {
        [0x01] = (0, 0.0),   // Esc
        [0x57] = (0, 12.5),  // F11
        [0x58] = (0, 13.5),  // F12
        [0x29] = (1, 0.0),   // ° left of 1
        [0x0E] = (1, 13.6),  // Backspace
        [0x0F] = (2, 0.25),  // Tab
        [0x3A] = (3, 0.4),   // Caps Lock
        [0x2B] = (3, 12.75), // ' (ISO key by Enter)
        [0x1C] = (3, 13.7),  // Enter
        [0x2A] = (4, 0.3),   // Left Shift
        [0x56] = (4, 1.25),  // < (ISO key by Z)
        [0x36] = (4, 13.3),  // Right Shift
        [0x1D] = (5, 0.5),   // Ctrl
        [0x5B] = (5, 1.6),   // Win
        [0x38] = (5, 2.7),   // Alt
        [0x39] = (5, 7.0),   // Space
    };

    /// <summary>Screen position for a scancode. Unknown keys (numpad, arrows, media) get a stable spot
    /// spread across the screen so they still respond somewhere.</summary>
    public static (double X, double Y) Position(int scanCode)
    {
        if (TryLocate(scanCode, out var row, out var column))
            return ((column + 0.5) / Columns, RowY(row));

        var h = (uint)scanCode * 2654435761u;
        return (0.08 + ((h % 1000) / 1000.0 * 0.84), 0.15 + (((h / 1000) % 1000) / 1000.0 * 0.7));
    }

    /// <summary>The column, 0 at the left; used to pick a pitch so that left is low and right is high.</summary>
    public static double Column(int scanCode) =>
        TryLocate(scanCode, out _, out var column) ? column : Position(scanCode).X * Columns;

    /// <summary>The digit row is row 1 and the letters are rows 2 to 4.</summary>
    public static int? Row(int scanCode) => TryLocate(scanCode, out var row, out _) ? row : null;

    private static double RowY(int row) => 0.12 + (row * 0.15);

    private static bool TryLocate(int scanCode, out int row, out double column)
    {
        foreach (var (r, first, last, firstColumn) in Runs)
        {
            if (scanCode >= first && scanCode <= last)
            {
                row = r;
                column = firstColumn + (scanCode - first);
                return true;
            }
        }
        if (Singles.TryGetValue(scanCode, out var s))
        {
            (row, column) = s;
            return true;
        }
        row = 0;
        column = 0;
        return false;
    }
}
