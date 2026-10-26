using Keypuncher.Core;

namespace Keypuncher.Toy;

/// <summary>
/// The grown-up exit: Ctrl+Shift+Q, or holding Esc for <see cref="EscHoldSeconds"/>, shows a short
/// word, and typing that word within <see cref="Timeout"/> opens the parent screen. A baby might hit
/// the chord with a palm or lean on Esc, but will not then type the word. Keys keep working for the
/// child while the challenge is showing.
/// </summary>
public sealed class GrownUpGate(Random random)
{
    public const double Timeout = 20;
    public const double EscHoldSeconds = 3;
    /// <summary>The hold cue stays hidden this long, so a child tapping Esc never makes it blink.</summary>
    public const double CueAfterSeconds = 0.5;
    /// <summary>Once shown, the cue fades in over this long.</summary>
    public const double CueFadeSeconds = 0.3;
    /// <summary>Held Esc repeats every few tens of milliseconds; silence longer than this means its release was lost.</summary>
    private const double EscLostAfterSeconds = 0.5;
    private const int QScanCode = 0x10;
    private const int EscScanCode = 0x01;
    private static readonly string[] Words = ["lamp", "fork", "milk", "sock", "book", "frog", "salt", "kite"];

    private string _typed = "";
    private double _expires;
    private DateTimeOffset? _escDownAt;
    private DateTimeOffset _escLastAt;

    /// <summary>The word to type, or null when no challenge is showing.</summary>
    public string? Challenge { get; private set; }

    public double SecondsLeft(double now) => Challenge is null ? 0 : Math.Max(0, _expires - now);

    /// <summary>Returns true when this press completed the challenge.</summary>
    public bool Feed(KeyPress press, double now)
    {
        if (press.ScanCode == EscScanCode)
        {
            // Held Esc auto-repeats; the first key-down starts the clock and a key-up stops it.
            _escLastAt = press.Timestamp;
            if (!press.IsDown) _escDownAt = null;
            else if (_escDownAt is null) _escDownAt = press.Timestamp;
            else if ((press.Timestamp - _escDownAt.Value).TotalSeconds >= EscHoldSeconds && Challenge is null)
                Show(now);
            return false;
        }
        if (!press.IsDown) return false;
        if (Challenge is not null && now > _expires) Challenge = null;

        if (press.Ctrl && press.Shift && press.ScanCode == QScanCode)
        {
            Show(now);
            return false;
        }
        if (Challenge is null || press.Character is not { Length: 1 } c || !char.IsLetter(c[0])) return false;

        _typed = (_typed + char.ToLowerInvariant(c[0]))[^Math.Min(_typed.Length + 1, Challenge.Length)..];
        if (_typed != Challenge) return false;

        Challenge = null;
        return true;
    }

    public void Cancel() => Challenge = null;

    /// <summary>How far through the Esc hold the grown-up is (0 to 1) and how strongly to draw that (0 to 1), so the
    /// play page can show a small filling ring. Nothing shows for the first <see cref="CueAfterSeconds"/>, once
    /// the word is up, or when Esc has gone quiet without a key-up. The ring gives nothing away: the word still
    /// has to be typed.</summary>
    public (double Progress, double Opacity) EscCue(DateTimeOffset now)
    {
        if (Challenge is not null || _escDownAt is not { } down || (now - _escLastAt).TotalSeconds > EscLostAfterSeconds)
            return (0, 0);
        var held = (now - down).TotalSeconds;
        if (held < CueAfterSeconds) return (0, 0);
        return (Math.Min(1, held / EscHoldSeconds), Math.Min(1, (held - CueAfterSeconds) / CueFadeSeconds));
    }


    private void Show(double now)
    {
        Challenge = Words[random.Next(Words.Length)];
        _typed = "";
        _expires = now + Timeout;
    }
}
