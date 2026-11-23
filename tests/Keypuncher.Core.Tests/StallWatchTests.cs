using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class StallWatchTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static (StallWatch Watch, List<string> Lines) New(string what = "the model is reading")
    {
        var lines = new List<string>();
        var watch = new StallWatch(() => what, lines.Add);
        watch.Start(S(0));
        return (watch, lines);
    }

    [Fact]
    public void Frames_that_come_on_time_are_not_a_pause()
    {
        var (watch, lines) = New();
        using var _ = watch;
        for (var t = 0.0; t < 3; t += 0.016)
        {
            Assert.Null(watch.Beat(S(t)));
            Assert.False(watch.Look(S(t + 0.01)));
        }
        Assert.Empty(lines);
        Assert.Equal(0, watch.Pauses);
    }

    [Fact]
    public void A_pause_is_written_down_while_it_lasts_and_again_when_it_ends()
    {
        var (watch, lines) = New();
        using var _ = watch;
        watch.Start(S(9.99));
        watch.Beat(S(10));
        watch.Phase = "saving the world";
        Assert.False(watch.Look(S(10.5)));
        Assert.True(watch.Look(S(11.2)));
        // Said once, then again only every five seconds.
        Assert.False(watch.Look(S(12)));
        Assert.True(watch.Look(S(16.3)));
        watch.Phase = "between frames";
        Assert.Equal(S(21.5), watch.Beat(S(31.5)));

        Assert.Equal(3, lines.Count);
        Assert.Contains("nothing drawn for 1.2 s", lines[0]);
        Assert.Contains("saving the world", lines[0]);
        Assert.Contains("the model is reading", lines[0]);
        Assert.Contains("nothing drawn for 6.3 s", lines[1]);
        Assert.Contains("paused for 21.5 s", lines[2]);
        // The end of the pause names what the thread was stuck in, not what it has moved on to.
        Assert.Contains("saving the world", lines[2]);
        Assert.Equal(1, watch.Pauses);
        Assert.Null(watch.Beat(S(31.52)));
    }

    [Fact]
    public void A_stopped_watch_owes_no_frames()
    {
        var (watch, lines) = New();
        watch.Beat(S(0.5));
        watch.Stop();
        Assert.False(watch.Look(S(60)));
        Assert.Null(watch.Beat(S(61)));
        Assert.Empty(lines);
    }

    [Fact]
    public void What_was_going_on_failing_to_read_does_not_lose_the_pause()
    {
        var lines = new List<string>();
        using var watch = new StallWatch(() => throw new InvalidOperationException("no card"), lines.Add);
        watch.Start(S(0.5));
        watch.Beat(S(1));
        Assert.True(watch.Look(S(3)));
        Assert.Contains("no card", Assert.Single(lines));
    }
}
