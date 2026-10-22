using Keypuncher.Core;

namespace Keypuncher.Toy;

/// <summary>
/// The grown-up exit: Ctrl+Shift+Q shows a short word, and typing that word within
/// <see cref="Timeout"/> opens the parent screen. A baby might hit the chord with a palm, but will not
/// then type the word. Keys keep working for the child while the challenge is showing.
/// </summary>
public sealed class GrownUpGate(Random random)
{
    public const double Timeout = 20;
    private const int QScanCode = 0x10;
    private static readonly string[] Words = ["lamp", "fork", "milk", "sock", "book", "frog", "salt", "kite"];

    private string _typed = "";
    private double _expires;

    /// <summary>The word to type, or null when no challenge is showing.</summary>
    public string? Challenge { get; private set; }

    public double SecondsLeft(double now) => Challenge is null ? 0 : Math.Max(0, _expires - now);

    /// <summary>Returns true when this press completed the challenge.</summary>
    public bool Feed(KeyPress press, double now)
    {
        if (!press.IsDown) return false;
        if (Challenge is not null && now > _expires) Challenge = null;

        if (press.Ctrl && press.Shift && press.ScanCode == QScanCode)
        {
            Challenge = Words[random.Next(Words.Length)];
            _typed = "";
            _expires = now + Timeout;
            return false;
        }
        if (Challenge is null || press.Character is not { Length: 1 } c || !char.IsLetter(c[0])) return false;

        _typed = (_typed + char.ToLowerInvariant(c[0]))[^Math.Min(_typed.Length + 1, Challenge.Length)..];
        if (_typed != Challenge) return false;

        Challenge = null;
        return true;
    }

    public void Cancel() => Challenge = null;
}
