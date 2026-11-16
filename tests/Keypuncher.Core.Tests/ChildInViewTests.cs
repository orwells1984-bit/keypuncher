using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public sealed class ChildInViewTests
{
    private static readonly ViewBox Face = new(0.6, 0.25, 0.15, 0.2);
    private const double Moving = 0.2, Still = 0.0;

    /// <summary>Looks every half second from <paramref name="from"/> up to (not including) <paramref name="to"/>,
    /// returning whether the child was in view at each.</summary>
    private static List<bool> Looks(ChildInView view, double from, double to, ViewBox? face, double? moved)
    {
        var seen = new List<bool>();
        for (var t = from; t < to - 1e-9; t += 0.5)
        {
            view.Look(t, face, moved);
            seen.Add(view.InView);
        }
        return seen;
    }

    [Fact]
    public void A_child_looking_down_at_the_keys_and_moving_stays_in_view()
    {
        var view = new ChildInView(0);
        view.Look(0.5, Face, null);
        // A minute of pressing keys with the face turned down: no face, but movement where the child was.
        Assert.All(Looks(view, 1, 60, null, Moving), Assert.True);
    }

    [Fact]
    public void The_child_is_out_of_view_after_quiet_seconds_with_no_face_and_no_movement()
    {
        var view = new ChildInView(0);
        view.Look(0.5, Face, null);
        var seen = Looks(view, 1, 20, null, Still);
        var firstOut = seen.IndexOf(false) * 0.5 + 1;
        Assert.Equal(0.5 + ChildInView.QuietFor, firstOut);
        Assert.All(seen.SkipWhile(s => s).ToList(), Assert.False);
    }

    [Fact]
    public void A_short_still_spell_does_not_show_the_warning()
    {
        var view = new ChildInView(0);
        view.Look(0.5, Face, null);
        Looks(view, 1, 10, null, Moving);
        // Sitting still watching the screen, face down, for a little under the quiet time.
        Assert.All(Looks(view, 10, 10 + ChildInView.QuietFor - 1, null, Still), Assert.True);
        Assert.All(Looks(view, 10 + ChildInView.QuietFor - 1, 40, null, Moving), Assert.True);
    }

    [Fact]
    public void Movement_alone_keeps_the_child_in_view_only_so_long_after_the_last_face()
    {
        var view = new ChildInView(0);
        view.Look(0, Face, null);
        var seen = Looks(view, 0.5, ChildInView.MovementCarries + 5, null, Moving);
        Assert.True(seen[^1] == false);
        Assert.Equal(ChildInView.MovementCarries, seen.IndexOf(false) * 0.5 + 0.5);
        // Movement long after the last face does not bring the child back; a face does.
        Assert.All(Looks(view, ChildInView.MovementCarries + 5, ChildInView.MovementCarries + 10, null, Moving), Assert.False);
        Assert.True(view.Look(ChildInView.MovementCarries + 10, Face, null));
        Assert.True(view.InView);
    }

    [Fact]
    public void Once_out_one_stray_change_does_not_bring_the_child_back_but_steady_movement_does()
    {
        var view = new ChildInView(0);
        view.Look(0, Face, null);
        Looks(view, 0.5, 15, null, Still);
        Assert.False(view.InView);
        view.Look(15, null, Moving);
        Assert.False(view.InView);
        view.Look(15.5, null, Still);
        view.Look(16, null, Moving);
        Assert.False(view.InView);
        Assert.True(view.Look(16.5, null, Moving));
        Assert.True(view.InView);
    }

    [Fact]
    public void One_miss_or_one_stray_face_does_not_toggle_it()
    {
        var view = new ChildInView(0);
        view.Look(0.5, Face, null);
        Assert.False(view.Look(1, null, Still));
        Assert.True(view.InView);

        Looks(view, 1.5, 15, null, Still);
        Assert.False(view.InView);
        // A face brings the child back at once, and one miss after it does not take the child away again.
        Assert.True(view.Look(15, Face, null));
        Assert.False(view.Look(15.5, null, Still));
        Assert.True(view.InView);
    }

    [Fact]
    public void The_child_gets_the_first_seconds_and_movement_anywhere_counts_before_a_face_is_seen()
    {
        var view = new ChildInView(0);
        Assert.Equal(ViewBox.Whole, view.Area);
        Assert.All(Looks(view, 0, 30, null, Moving), Assert.True);

        var empty = new ChildInView(0);
        var seen = Looks(empty, 0, 15, null, Still);
        Assert.Equal(ChildInView.QuietFor, seen.IndexOf(false) * 0.5);
    }

    [Fact]
    public void The_area_follows_the_face_and_reaches_down_to_where_the_hands_and_head_go()
    {
        var view = new ChildInView(0);
        view.Look(1, Face, null);
        var area = view.Area;
        Assert.Equal(Face.X - Face.W, area.X, 6);
        Assert.Equal(Face.X + 2 * Face.W, area.X + area.W, 6);
        Assert.Equal(Face.Y - Face.H / 2, area.Y, 6);
        Assert.Equal(Face.Y + 3.5 * Face.H, area.Y + area.H, 6);
        view.Look(1.5, null, Moving);
        Assert.Equal(area, view.Area);
        Assert.Null(view.LastFace);
        // A face at the edge keeps the area inside the picture.
        var corner = new ViewBox(0.02, 0.05, 0.1, 0.2).AroundFace();
        Assert.Equal(0, corner.X);
        Assert.Equal(0, corner.Y);
        var edge = new ViewBox(0.9, 0.8, 0.1, 0.2).AroundFace();
        Assert.Equal(1, edge.X + edge.W, 6);
        Assert.Equal(1, edge.Y + edge.H, 6);
    }

    [Fact]
    public void Motion_grid_counts_changed_cells_inside_the_area_only_and_ignores_noise()
    {
        const int w = 640, h = 480;
        var before = new byte[w * h];
        Array.Fill(before, (byte)100);
        var noisy = (byte[])before.Clone();
        var random = new Random(1);
        for (var i = 0; i < noisy.Length; i++) noisy[i] = (byte)(100 + random.Next(-12, 13));
        var moved = (byte[])before.Clone();
        // Something bright moves into the top-left quarter.
        for (var y = 0; y < h / 2; y++)
            for (var x = 0; x < w / 2; x++)
                moved[y * w + x] = 200;

        var a = MotionGrid.Cells(before, w, h);
        Assert.Equal(MotionGrid.Columns * MotionGrid.Rows, a.Length);
        Assert.Equal(0, MotionGrid.ChangedShare(a, MotionGrid.Cells(noisy, w, h), ViewBox.Whole));
        var b = MotionGrid.Cells(moved, w, h);
        Assert.Equal(0.25, MotionGrid.ChangedShare(a, b, ViewBox.Whole), 3);
        Assert.Equal(1, MotionGrid.ChangedShare(a, b, new ViewBox(0, 0, 0.5, 0.5)), 3);
        Assert.Equal(0, MotionGrid.ChangedShare(a, b, new ViewBox(0.5, 0.5, 0.5, 0.5)), 3);
    }
}
