using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Language;
using Keypuncher.Storage;

namespace Keypuncher.Toy.Tests;

public sealed class TouchGateTests
{
    private const double Wide = 16.0 / 9;
    private const double Tall = 9.0 / 16;
    private long _nextId = 1;
    private double _now;

    [Theory]
    [InlineData(Wide)]
    [InlineData(Tall)]
    public void Two_opposite_corners_held_for_three_seconds_show_the_shapes(double aspect)
    {
        var gate = new TouchGate(new Random(1));
        var a = Down(gate, 0.02, 0.03, aspect);
        var b = Down(gate, 0.98, 0.96, aspect);
        Run(gate, 2.9);
        Assert.Null(gate.Challenge);
        Run(gate, 0.2);
        Assert.Equal(TouchGate.Steps, gate.Challenge!.Count);
        Assert.Equal(6, gate.Targets.Count);
        Assert.Equal(gate.Challenge.Count, gate.Challenge.Distinct().Count());
        gate.FingerUp(a, _now);
        gate.FingerUp(b, _now);
    }

    [Fact]
    public void The_other_diagonal_works_too_but_two_corners_on_one_side_do_not()
    {
        var gate = new TouchGate(new Random(1));
        var a = Down(gate, 0.97, 0.02);
        var b = Down(gate, 0.03, 0.98);
        Run(gate, 3.1);
        Assert.NotNull(gate.Challenge);
        Lift(gate, a, b);
        gate.Cancel();

        foreach (var (x1, y1, x2, y2) in new[] { (0.02, 0.02, 0.02, 0.98), (0.02, 0.02, 0.98, 0.02), (0.98, 0.98, 0.02, 0.98) })
        {
            var c = Down(gate, x1, y1);
            var d = Down(gate, x2, y2);
            Run(gate, 10);
            Assert.Null(gate.Challenge);
            Lift(gate, c, d);
        }
    }

    [Fact]
    public void A_palm_in_a_corner_does_not_open_it()
    {
        var gate = new TouchGate(new Random(1));
        // A resting hand: heel and fingers, five contacts in and around one corner, and one finger in the other.
        long[] palm = [Down(gate, 0.02, 0.03), Down(gate, 0.06, 0.05), Down(gate, 0.1, 0.02), Down(gate, 0.04, 0.12), Down(gate, 0.12, 0.1)];
        var far = Down(gate, 0.98, 0.97);
        Run(gate, 30);
        Assert.Null(gate.Challenge);
        Assert.Equal((0, 0), gate.HoldCue(_now));
        Lift(gate, [.. palm, far]);
    }

    [Fact]
    public void A_child_lying_across_the_screen_does_not_open_it()
    {
        var gate = new TouchGate(new Random(1));
        // Hands in both corners, but an arm, a cheek and a tummy on the glass in between.
        long[] body = [Down(gate, 0.03, 0.03), Down(gate, 0.97, 0.97), Down(gate, 0.3, 0.3), Down(gate, 0.5, 0.55), Down(gate, 0.7, 0.6)];
        Run(gate, 30);
        Assert.Null(gate.Challenge);

        // Rolling off leaves only the two hands, but the one now in a corner slid there, so it does not count.
        Move(gate, body[0], 0.3, 0.3);
        Move(gate, body[0], 0.03, 0.03);
        Lift(gate, body[2], body[3], body[4]);
        Run(gate, 30);
        Assert.Null(gate.Challenge);
    }

    [Fact]
    public void Two_handed_slapping_never_adds_up_to_a_hold()
    {
        var gate = new TouchGate(new Random(1));
        var random = new Random(5);
        for (var i = 0; i < 400; i++)
        {
            // Mostly anywhere, often right in the corners, held up to a second and a half.
            var (x1, y1) = i % 3 == 0 ? (0.02, 0.02) : (random.NextDouble(), random.NextDouble());
            var (x2, y2) = i % 3 == 0 ? (0.98, 0.98) : (random.NextDouble(), random.NextDouble());
            var a = Down(gate, x1, y1);
            Run(gate, random.NextDouble() * 0.05);
            var b = Down(gate, x2, y2);
            Run(gate, random.NextDouble() * 1.5);
            Lift(gate, a, b);
            Run(gate, random.NextDouble() * 0.4);
        }
        Assert.Null(gate.Challenge);
    }

    [Fact]
    public void Leaving_a_corner_or_adding_a_finger_starts_the_hold_again()
    {
        var gate = new TouchGate(new Random(1));
        var a = Down(gate, 0.02, 0.02);
        var b = Down(gate, 0.98, 0.98);
        Run(gate, 2);
        var c = Down(gate, 0.5, 0.5);
        Assert.Equal((0, 0), gate.HoldCue(_now));
        gate.FingerUp(c, _now);
        Run(gate, 2.5);
        Assert.Null(gate.Challenge);       // 2 s + 2.5 s, but the third finger reset the count
        Run(gate, 0.6);
        Assert.NotNull(gate.Challenge);
        Lift(gate, a, b);
    }

    [Fact]
    public void The_hold_cue_fills_like_the_esc_ring()
    {
        var gate = new TouchGate(new Random(1));
        Down(gate, 0.02, 0.02);
        Down(gate, 0.98, 0.98);
        Assert.Equal((0, 0), gate.HoldCue(_now + 0.3));
        var (progress, opacity) = gate.HoldCue(_now + 1.5);
        Assert.Equal(0.5, progress, 3);
        Assert.Equal(1, opacity, 3);
        Run(gate, 3.1);
        Assert.Equal((0, 0), gate.HoldCue(_now));
    }

    [Fact]
    public void Tapping_the_named_shapes_in_order_passes()
    {
        var gate = new TouchGate(new Random(1));
        Open(gate);
        var named = gate.Challenge!;
        for (var i = 0; i < named.Count; i++)
        {
            var passed = Tap(gate, Spot(gate, named[i]));
            Assert.Equal(i == named.Count - 1, passed);
            if (!passed) Assert.Equal(i + 1, gate.Targets.Count(t => t.Tapped));
        }
        Assert.Null(gate.Challenge);
    }

    [Fact]
    public void A_tap_beside_the_row_is_ignored_but_a_wrong_shape_ends_the_challenge()
    {
        var gate = new TouchGate(new Random(1));
        Open(gate);
        var named = gate.Challenge!;
        var first = Spot(gate, named[0]);
        Assert.False(Tap(gate, (0.5, 0.1)));
        Assert.False(Tap(gate, (0.9, 0.9)));
        Assert.NotNull(gate.Challenge);

        var wrong = gate.Targets.First(t => t.Shape != named[0]);
        Assert.False(Tap(gate, (wrong.X, wrong.Y)));
        Assert.Null(gate.Challenge);
        Assert.False(Tap(gate, first));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Copying_where_the_grown_up_tapped_last_time_does_not_pass(int seed)
    {
        var gate = new TouchGate(new Random(seed));
        Open(gate);
        var watched = gate.Challenge!.Select(s => Spot(gate, s)).ToList();
        Assert.True(watched.Select(spot => Tap(gate, spot)).ToList()[^1]);

        for (var round = 0; round < 20; round++)
        {
            Open(gate);
            Assert.DoesNotContain(true, watched.Select(spot => Tap(gate, spot)).ToList());
            Assert.Null(gate.Challenge);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Tapping_along_the_row_either_way_does_not_pass(int seed)
    {
        var gate = new TouchGate(new Random(seed));
        for (var round = 0; round < 20; round++)
        {
            foreach (var order in new[] { Enumerable.Range(0, 6), Enumerable.Range(0, 6).Reverse() })
            {
                Open(gate);
                var row = gate.Targets;
                Assert.DoesNotContain(true, order.Select(slot => Tap(gate, (row[slot].X, row[slot].Y))).ToList());
            }
        }
    }

    [Fact]
    public void The_challenge_times_out()
    {
        var gate = new TouchGate(new Random(1));
        Open(gate);
        var spots = gate.Challenge!.Select(s => Spot(gate, s)).ToList();
        Run(gate, TouchGate.Timeout - 1);
        Assert.InRange(gate.SecondsLeft(_now), 0.5, 1.5);
        Run(gate, 1.1);
        Assert.Null(gate.Challenge);
        Assert.Empty(gate.Targets);
        Assert.DoesNotContain(true, spots.Select(spot => Tap(gate, spot)).ToList());
    }

    [Fact]
    public void Fingers_still_in_the_corners_after_the_shapes_show_do_not_count_again()
    {
        var gate = new TouchGate(new Random(1));
        Down(gate, 0.02, 0.02);
        Down(gate, 0.98, 0.98);
        Run(gate, 3.1);
        var first = gate.Challenge;
        gate.Cancel();
        Run(gate, 10);
        Assert.Null(gate.Challenge);
        Assert.NotNull(first);
    }

    [Fact]
    public void In_the_toy_taps_keep_playing_and_the_keyboard_exit_is_unchanged()
    {
        var world = new World();
        var mixer = new Mixer();
        var engine = new ToyEngine(world, mixer, new SpeechLibrary(new ClipStore(Directory.CreateTempSubdirectory("keypuncher-tests").FullName), null),
            ToySettings.Default with { LanguageWeights = new() { ["is"] = 1 } }, new Random(7));
        var opened = 0;
        engine.GrownUpRequested += () => opened++;
        var clock = DateTimeOffset.UnixEpoch;

        engine.OnFingerDown(1, 0.02, 0.02, Wide, 1);
        engine.OnFingerDown(2, 0.98, 0.98, Wide, 2);
        for (var t = 0.0; t < 3.2; t += 0.05) engine.Update(0.05);
        engine.OnFingerUp(1);
        engine.OnFingerUp(2);
        var named = engine.TouchGate.Challenge!;
        Assert.NotNull(named);
        Assert.Null(engine.Gate.Challenge);         // the corners never show the typed word

        // A child's tap beside the row, or on a shape, still plays its note.
        Sounds(mixer, seconds: 10);                 // let the corner taps' notes die away
        engine.OnFingerDown(3, 0.5, 0.15, Wide, 1);
        engine.OnFingerUp(3);
        Assert.True(Sounds(mixer, seconds: 0.1) > 0.01);
        Sounds(mixer, seconds: 10);
        var wrong = engine.TouchGate.Targets.First(t => t.Shape != named[0]);
        engine.OnFingerDown(4, wrong.X, wrong.Y, Wide, 1);
        engine.OnFingerUp(4);
        Assert.True(Sounds(mixer, seconds: 0.1) > 0.01);
        Assert.Null(engine.TouchGate.Challenge);    // and that wrong shape ended the challenge
        engine.OnFingerDown(1, 0.02, 0.02, Wide, 1);
        engine.OnFingerDown(2, 0.98, 0.98, Wide, 2);
        for (var t = 0.0; t < 3.2; t += 0.05) engine.Update(0.05);
        engine.OnFingerUp(1);
        engine.OnFingerUp(2);
        named = engine.TouchGate.Challenge!;

        // The keyboard exit works as before while the shapes show, and keys do not touch the shapes.
        engine.OnKey(new KeyPress(clock, 0x10, "q", false, true, Ctrl: true, Shift: true));
        var word = engine.Gate.Challenge!;
        foreach (var c in word)
        {
            engine.OnKey(new KeyPress(clock, 0x1E + (c % 9), c.ToString(), false, true));
            clock = clock.AddMilliseconds(80);
            engine.OnKey(new KeyPress(clock, 0x1E + (c % 9), c.ToString(), false, false));
            clock = clock.AddMilliseconds(80);
        }
        Assert.Equal(1, opened);
        Assert.Same(named, engine.TouchGate.Challenge);

        // And the shapes open it too.
        var id = 10L;
        foreach (var shape in named)
        {
            var target = engine.TouchGate.Targets.Single(t => t.Shape == shape);
            engine.OnFingerDown(id, target.X, target.Y, Wide, 1);
            engine.OnFingerUp(id++);
        }
        Assert.Equal(2, opened);
    }

    /// <summary>Plays this much of the mixer's output and returns its loudest sample.</summary>
    private static float Sounds(Mixer mixer, double seconds)
    {
        var buffer = new float[(int)(Mixer.SampleRate * seconds) * Mixer.Channels];
        mixer.Read(buffer);
        return buffer.Max(Math.Abs);
    }

    private void Open(TouchGate gate)
    {
        var a = Down(gate, 0.02, 0.02);
        var b = Down(gate, 0.98, 0.98);
        Run(gate, TouchGate.HoldSeconds + 0.1);
        Lift(gate, a, b);
        Assert.NotNull(gate.Challenge);
    }

    private static (double X, double Y) Spot(TouchGate gate, GateShape shape)
    {
        var target = gate.Targets.Single(t => t.Shape == shape);
        return (target.X, target.Y);
    }

    private bool Tap(TouchGate gate, (double X, double Y) spot)
    {
        var id = Down(gate, spot.X, spot.Y, out var passed);
        Run(gate, 0.1);
        gate.FingerUp(id, _now);
        Run(gate, 0.2);
        return passed;
    }

    private long Down(TouchGate gate, double x, double y, double aspect = Wide) => Down(gate, x, y, out _, aspect);

    private long Down(TouchGate gate, double x, double y, out bool passed, double aspect = Wide)
    {
        var id = _nextId++;
        passed = gate.FingerDown(id, x, y, aspect, _now);
        return id;
    }

    private void Move(TouchGate gate, long id, double x, double y) => gate.FingerMove(id, x, y, Wide, _now);

    private void Lift(TouchGate gate, params long[] ids)
    {
        foreach (var id in ids) gate.FingerUp(id, _now);
    }

    private void Run(TouchGate gate, double seconds)
    {
        var end = _now + seconds;
        while (_now < end)
        {
            _now = Math.Min(end, _now + (1.0 / 60));
            gate.Tick(_now);
        }
    }
}
