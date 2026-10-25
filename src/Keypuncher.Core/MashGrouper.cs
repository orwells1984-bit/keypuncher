namespace Keypuncher.Core;

public enum PressKind
{
    /// <summary>Nothing to do: a key-up, or an auto-repeat past the first few.</summary>
    Ignored,
    /// <summary>A deliberate-looking press: respond fully, including speech.</summary>
    Single,
    /// <summary>A held key auto-repeating: respond gently, no speech.</summary>
    Repeat,
    /// <summary>The press that turned a group into a mash (a palm or a fist): one big response.</summary>
    Mash,
    /// <summary>Part of a group that has already been answered.</summary>
    Absorbed,
}

/// <summary>
/// Turns raw key presses into what the child did. The first key of a group answers at once, so a
/// single press costs no latency; keys landing within <see cref="Window"/> of the previous one join its
/// group, and the third key makes the group a <see cref="PressKind.Mash"/>. A held key repeats a few
/// times and then goes quiet.
/// </summary>
public sealed class MashGrouper
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan MaxGroup = TimeSpan.FromMilliseconds(250);
    private const int MashThreshold = 3;
    private const int MaxRepeats = 3;
    /// <summary>Windows starts auto-repeat at most a second after the press and repeats far faster, so a
    /// "held" key silent for longer than this was released where the toy could not see it.</summary>
    private static readonly TimeSpan LostRelease = TimeSpan.FromSeconds(1.5);

    private readonly Dictionary<int, (int Repeats, DateTimeOffset Last)> _held = [];
    private DateTimeOffset _groupStart = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPress = DateTimeOffset.MinValue;
    private int _groupCount;
    private double _sumX, _sumY;

    public (PressKind Kind, InputEvent? Event) Feed(KeyPress press)
    {
        if (!press.IsDown)
        {
            _held.Remove(press.ScanCode);
            return (PressKind.Ignored, null);
        }

        var (x, y) = KeyboardGeometry.Position(press.ScanCode);

        if (_held.TryGetValue(press.ScanCode, out var held) && press.Timestamp - held.Last <= LostRelease)
        {
            var repeats = held.Repeats;
            _held[press.ScanCode] = (repeats + 1, press.Timestamp);
            return repeats < MaxRepeats
                ? (PressKind.Repeat, new InputEvent(press.Timestamp, press.ScanCode, press.Character, x, y, 1))
                : (PressKind.Ignored, null);
        }
        _held[press.ScanCode] = (0, press.Timestamp);

        var joins = press.Timestamp - _lastPress <= Window && press.Timestamp - _groupStart <= MaxGroup;
        _lastPress = press.Timestamp;

        if (!joins)
        {
            _groupStart = press.Timestamp;
            _groupCount = 1;
            (_sumX, _sumY) = (x, y);
            return (PressKind.Single, new InputEvent(press.Timestamp, press.ScanCode, press.Character, x, y, 1));
        }

        _groupCount++;
        _sumX += x;
        _sumY += y;
        if (_groupCount != MashThreshold) return (PressKind.Absorbed, null);

        return (PressKind.Mash, new InputEvent(
            press.Timestamp, press.ScanCode, null, _sumX / _groupCount, _sumY / _groupCount, _groupCount));
    }

    /// <summary>Forgets held keys, e.g. after the window lost focus and key-ups went elsewhere.</summary>
    public void Reset() => _held.Clear();
}
