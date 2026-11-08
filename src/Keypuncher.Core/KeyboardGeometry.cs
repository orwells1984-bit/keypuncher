namespace Keypuncher.Core;

/// <summary>
/// Where a physical key sits on an ISO keyboard (the Icelandic layout is ISO), as a 0..1 position
/// on the screen. Keys on the left respond on the left, the top row near the top, so where the baby
/// hits matters. Works from Set 1 scancodes, which is what the Windows low-level hook reports (extended
/// keys with 0xE000 added), so it does not depend on the active layout.
/// <para>
/// The main block (Esc to Backspace, Tab to Enter, Shift to Shift, Ctrl to the right-hand Ctrl) fills the
/// whole width, as it is the part a baby reaches. The clusters to its right on a full-size keyboard, the
/// arrows with the Insert/Home block and the number pad, are folded into the right quarter of the screen,
/// each key at the height of its own row: they are physically right of everything, so they answer on the
/// right, but giving them their true share of the width would squeeze the main block into the left two
/// thirds and leave the right of the screen empty on a laptop with no number pad.
/// </para>
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

    // Main-block keys outside the runs: row and column.
    private static readonly Dictionary<int, (double Row, double Column)> Singles = new()
    {
        [0x01] = (0, 0.0),     // Esc
        [0x57] = (0, 12.5),    // F11
        [0x58] = (0, 13.5),    // F12
        [0x29] = (1, 0.0),     // ° left of 1
        [0x0E] = (1, 13.6),    // Backspace
        [0x0F] = (2, 0.25),    // Tab
        [0x3A] = (3, 0.4),     // Caps Lock
        [0x2B] = (3, 12.75),   // ' (ISO key by Enter)
        [0x1C] = (2.5, 13.7),  // Enter (ISO Enter is two rows tall)
        [0x2A] = (4, 0.3),     // Left Shift
        [0x56] = (4, 1.25),    // < (ISO key by Z)
        [0x36] = (4, 13.3),    // Right Shift
        [0x1D] = (5, 0.5),     // Left Ctrl
        [0xE05B] = (5, 1.6),   // Left Win (extended)
        [0x5B] = (5, 1.6),     // Left Win, in case a keyboard reports it plain
        [0x38] = (5, 2.7),     // Alt
        [0x39] = (5, 7.0),     // Space
        [0xE038] = (5, 10.6),  // AltGr
        [0xE05C] = (5, 11.6),  // Right Win
        [0xE05D] = (5, 12.6),  // Menu
        [0xE01D] = (5, 13.6),  // Right Ctrl
    };

    // The arrows, the Insert/Home block and the number pad: row and screen x, in the right quarter.
    private static readonly double[] NavX = [0.72, 0.77, 0.82];
    private static readonly double[] PadX = [0.84, 0.88, 0.92, 0.96];

    private static readonly Dictionary<int, (double Row, double X)> RightSide = new()
    {
        [0xE037] = (0, NavX[0]),   // Print Screen
        [0x46] = (0, NavX[1]),     // Scroll Lock
        [0x45] = (0, NavX[2]),     // Pause (the hook reports Pause as plain 0x45 and Num Lock as 0xE045)
        [0xE052] = (1, NavX[0]),   // Insert
        [0xE047] = (1, NavX[1]),   // Home
        [0xE049] = (1, NavX[2]),   // Page Up
        [0xE053] = (2, NavX[0]),   // Delete
        [0xE04F] = (2, NavX[1]),   // End
        [0xE051] = (2, NavX[2]),   // Page Down
        [0xE048] = (4, NavX[1]),   // Up
        [0xE04B] = (5, NavX[0]),   // Left
        [0xE050] = (5, NavX[1]),   // Down
        [0xE04D] = (5, NavX[2]),   // Right

        [0xE045] = (1, PadX[0]),   // Num Lock
        [0xE035] = (1, PadX[1]),   // pad /
        [0x37] = (1, PadX[2]),     // pad *
        [0x4A] = (1, PadX[3]),     // pad -
        [0x47] = (2, PadX[0]),     // pad 7
        [0x48] = (2, PadX[1]),     // pad 8
        [0x49] = (2, PadX[2]),     // pad 9
        [0x4E] = (2.5, PadX[3]),   // pad + (two rows tall)
        [0x4B] = (3, PadX[0]),     // pad 4
        [0x4C] = (3, PadX[1]),     // pad 5
        [0x4D] = (3, PadX[2]),     // pad 6
        [0x4F] = (4, PadX[0]),     // pad 1
        [0x50] = (4, PadX[1]),     // pad 2
        [0x51] = (4, PadX[2]),     // pad 3
        [0xE01C] = (4.5, PadX[3]), // pad Enter (two rows tall)
        [0x52] = (5, (PadX[0] + PadX[1]) / 2), // pad 0 (two columns wide)
        [0x53] = (5, PadX[2]),     // pad .
    };

    /// <summary>Screen position for a scancode. Keys not on a full-size ISO keyboard (media keys and the like)
    /// get a stable spot spread across the screen so they still respond somewhere.</summary>
    public static (double X, double Y) Position(int scanCode)
    {
        if (TryLocate(scanCode, out var row, out var x)) return (x, RowY(row));

        var h = (uint)scanCode * 2654435761u;
        return (0.08 + ((h % 1000) / 1000.0 * 0.84), 0.15 + (((h / 1000) % 1000) / 1000.0 * 0.7));
    }

    /// <summary>True for a key whose place on the keyboard is known, so <see cref="Position"/> follows it.</summary>
    public static bool IsKnown(int scanCode) => TryLocate(scanCode, out _, out _);

    /// <summary>The column on the main block's scale, 0 at the left; used to pick a pitch so that left is low and
    /// right is high.</summary>
    public static double Column(int scanCode) => (Position(scanCode).X * Columns) - 0.5;

    /// <summary>The digit row is row 1, the letters are rows 2 to 4 and the space bar row 5; null for a key whose
    /// place is not known.</summary>
    public static double? Row(int scanCode) => TryLocate(scanCode, out var row, out _) ? row : null;

    private static double RowY(double row) => 0.12 + (row * 0.15);

    private static bool TryLocate(int scanCode, out double row, out double x)
    {
        foreach (var (r, first, last, firstColumn) in Runs)
        {
            if (scanCode >= first && scanCode <= last)
            {
                row = r;
                x = (firstColumn + (scanCode - first) + 0.5) / Columns;
                return true;
            }
        }
        if (Singles.TryGetValue(scanCode, out var s))
        {
            row = s.Row;
            x = (s.Column + 0.5) / Columns;
            return true;
        }
        if (RightSide.TryGetValue(scanCode, out var r2))
        {
            (row, x) = r2;
            return true;
        }
        row = 0;
        x = 0;
        return false;
    }
}
