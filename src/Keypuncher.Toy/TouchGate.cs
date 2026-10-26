using Keypuncher.Core;

namespace Keypuncher.Toy;

/// <summary>
/// The grown-up exit for a screen with no keyboard. One finger in each of two opposite corners, and no other
/// finger on the screen, held for <see cref="HoldSeconds"/>, shows six shapes in a row and names four of them;
/// tapping those four in the named order within <see cref="Timeout"/> opens the parent screen.
/// <para>A palm or a child lying on the screen is many contacts, a slap is short, and a finger that slides in
/// from elsewhere never counts as being in a corner. A child who still gets there cannot read the names: a tap
/// on any other shape ends the challenge, and every challenge moves the shapes, so copying where a grown-up
/// tapped last time, or tapping along the row, never gets through. Taps keep playing for the child throughout.</para>
/// </summary>
public sealed class TouchGate(Random random)
{
    public const double HoldSeconds = 3;
    public const double Timeout = GrownUpGate.Timeout;
    /// <summary>How far from a corner, as a share of the short screen side, a finger still counts as in it.</summary>
    public const double CornerReach = 0.18;
    public const int Steps = 4;
    /// <summary>Shape radius as a share of the short side; the slots are a sixth of it apart.</summary>
    public const double ShapeRadius = 0.06;
    private const double SlotSpacing = 1.0 / 6;
    private const double HitRadius = SlotSpacing / 2;
    private const int NoCorner = -1;

    private static readonly GateShape[] AllShapes = Enum.GetValues<GateShape>();

    // Corners are numbered top-left 0, top-right 1, bottom-left 2, bottom-right 3, so opposite ones add up to 3.
    private readonly Dictionary<long, int> _fingers = [];
    private double? _holdSince;
    private bool _waitForLift;
    private double _expires;
    private double _aspect = 1;
    private GateShape[] _slots = [];
    private int[] _targets = [];
    private int[] _previousTargets = [];
    private int[] _passedTargets = [];
    private int _step;

    /// <summary>The shapes to tap, in order, or null when no challenge is showing.</summary>
    public IReadOnlyList<GateShape>? Challenge { get; private set; }

    public double SecondsLeft(double now) => Challenge is null ? 0 : Math.Max(0, _expires - now);

    /// <summary>The row of shapes to draw while the challenge shows, laid out for the screen's width over height.</summary>
    public IReadOnlyList<GateTarget> Targets
    {
        get
        {
            if (Challenge is null) return [];
            var tapped = _targets.Take(_step).ToHashSet();
            return [.. _slots.Select((shape, slot) =>
            {
                var (x, y) = SlotCentre(slot, _aspect);
                return new GateTarget(shape, x, y, ShapeRadius, tapped.Contains(slot));
            })];
        }
    }

    /// <summary>A finger landed at x, y (0..1 of the screen). Returns true when it completed the challenge.</summary>
    public bool FingerDown(long id, double x, double y, double aspect, double now)
    {
        _aspect = aspect;
        Expire(now);
        _fingers[id] = CornerOf(x, y, aspect);
        UpdateHold(now);
        if (Challenge is null) return false;

        var slot = SlotAt(x, y, aspect);
        if (slot is null) return false;                   // a tap beside the row just plays
        if (slot != _targets[_step])
        {
            Challenge = null;                             // a wrong shape ends it; the hold starts again
            return false;
        }
        if (++_step < Steps) return false;
        _passedTargets = _targets;
        Challenge = null;
        return true;
    }

    /// <summary>A finger moved; one that leaves its corner stops counting as in a corner until it lifts.</summary>
    public void FingerMove(long id, double x, double y, double aspect, double now)
    {
        if (!_fingers.TryGetValue(id, out var corner) || corner == NoCorner) return;
        if (CornerOf(x, y, aspect) != corner) _fingers[id] = NoCorner;
        UpdateHold(now);
    }

    public void FingerUp(long id, double now)
    {
        _fingers.Remove(id);
        if (_fingers.Count == 0) _waitForLift = false;
        UpdateHold(now);
    }

    /// <summary>Call every frame: a hold completes, or a challenge runs out, with no touch to say so.</summary>
    public void Tick(double now)
    {
        Expire(now);
        if (_holdSince is { } since && now - since >= HoldSeconds) Show(now);
    }

    public void Cancel() => Challenge = null;

    /// <summary>How far through the corner hold the grown-up is (0 to 1) and how strongly to draw it, for the same ring
    /// the Esc hold fills: nothing for the first <see cref="GrownUpGate.CueAfterSeconds"/>, then a short fade in.</summary>
    public (double Progress, double Opacity) HoldCue(double now)
    {
        if (Challenge is not null || _holdSince is not { } since) return (0, 0);
        var held = now - since;
        if (held < GrownUpGate.CueAfterSeconds) return (0, 0);
        return (Math.Min(1, held / HoldSeconds), Math.Min(1, (held - GrownUpGate.CueAfterSeconds) / GrownUpGate.CueFadeSeconds));
    }

    /// <summary>The centre of slot 0..5 (0..1 of the screen): a row across the middle, a sixth of the short side apart.</summary>
    public static (double X, double Y) SlotCentre(int slot, double aspect)
    {
        var (shortX, _) = ShortSide(aspect);
        return (0.5 + ((slot - 2.5) * SlotSpacing * shortX), 0.5);
    }

    private void UpdateHold(double now)
    {
        var corners = _fingers.Values.ToList();
        var holding = !_waitForLift && Challenge is null && corners.Count == 2
            && corners[0] != NoCorner && corners[1] != NoCorner && corners[0] + corners[1] == 3;
        if (!holding) _holdSince = null;
        else _holdSince ??= now;
    }

    private void Show(double now)
    {
        _holdSince = null;
        _waitForLift = true;
        // Shuffle until the taps differ from the last challenge's and from the last ones that passed (a child may have
        // watched those), and are not a sweep along the row.
        do
        {
            _slots = [.. AllShapes.OrderBy(_ => random.Next())];
            _targets = [.. Enumerable.Range(0, _slots.Length).OrderBy(_ => random.Next()).Take(Steps)];
        }
        while (_targets.SequenceEqual(_previousTargets) || _targets.SequenceEqual(_passedTargets) || Sorted(_targets) || Sorted(Enumerable.Reverse(_targets)));
        _previousTargets = _targets;
        Challenge = [.. _targets.Select(slot => _slots[slot])];
        _step = 0;
        _expires = now + Timeout;
    }

    private void Expire(double now)
    {
        if (Challenge is not null && now > _expires) Challenge = null;
    }

    private static bool Sorted(IEnumerable<int> slots) => slots.Zip(slots.Skip(1)).All(p => p.First < p.Second);

    /// <summary>What one unit of the short side is in 0..1 screen units across and down.</summary>
    private static (double X, double Y) ShortSide(double aspect) => aspect >= 1 ? (1 / aspect, 1) : (1, aspect);

    private static int CornerOf(double x, double y, double aspect)
    {
        var (shortX, shortY) = ShortSide(aspect);
        var right = x > 0.5;
        var bottom = y > 0.5;
        var dx = (right ? 1 - x : x) / shortX;
        var dy = (bottom ? 1 - y : y) / shortY;
        if (Math.Sqrt((dx * dx) + (dy * dy)) > CornerReach) return NoCorner;
        return (bottom ? 2 : 0) + (right ? 1 : 0);
    }

    private int? SlotAt(double x, double y, double aspect)
    {
        var (shortX, shortY) = ShortSide(aspect);
        for (var slot = 0; slot < _slots.Length; slot++)
        {
            var (cx, cy) = SlotCentre(slot, aspect);
            var dx = (x - cx) / shortX;
            var dy = (y - cy) / shortY;
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= HitRadius) return slot;
        }
        return null;
    }
}
