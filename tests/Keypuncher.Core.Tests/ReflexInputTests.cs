using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public class ReflexInputTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static KeyPress Down(int scan, double ms) => new(T0.AddMilliseconds(ms), scan, "a", false, true);
    private static KeyPress Up(int scan, double ms) => new(T0.AddMilliseconds(ms), scan, "a", false, false);

    [Fact]
    public void A_single_press_answers_immediately()
    {
        var g = new MashGrouper();
        var (kind, ev) = g.Feed(Down(0x1E, 0));
        Assert.Equal(PressKind.Single, kind);
        Assert.Equal(1, ev!.Intensity);
    }

    [Fact]
    public void Three_keys_within_the_window_make_one_mash()
    {
        var g = new MashGrouper();
        Assert.Equal(PressKind.Single, g.Feed(Down(0x1E, 0)).Kind);
        Assert.Equal(PressKind.Absorbed, g.Feed(Down(0x1F, 10)).Kind);
        var (kind, ev) = g.Feed(Down(0x20, 20));
        Assert.Equal(PressKind.Mash, kind);
        Assert.Equal(3, ev!.Intensity);
        Assert.Equal(PressKind.Absorbed, g.Feed(Down(0x21, 30)).Kind);
    }

    [Fact]
    public void Keys_further_apart_are_separate_presses()
    {
        var g = new MashGrouper();
        Assert.Equal(PressKind.Single, g.Feed(Down(0x1E, 0)).Kind);
        Assert.Equal(PressKind.Single, g.Feed(Down(0x1F, 100)).Kind);
    }

    [Fact]
    public void A_held_key_repeats_three_times_then_goes_quiet()
    {
        var g = new MashGrouper();
        g.Feed(Down(0x1E, 0));
        var kinds = Enumerable.Range(1, 6).Select(i => g.Feed(Down(0x1E, 500 + (i * 33))).Kind).ToList();
        Assert.Equal([PressKind.Repeat, PressKind.Repeat, PressKind.Repeat, PressKind.Ignored, PressKind.Ignored, PressKind.Ignored], kinds);

        g.Feed(Up(0x1E, 1000));
        Assert.Equal(PressKind.Single, g.Feed(Down(0x1E, 1200)).Kind);
    }

    [Fact]
    public void Keyboard_left_is_screen_left_and_top_row_is_higher()
    {
        var q = KeyboardGeometry.Position(0x10);
        var p = KeyboardGeometry.Position(0x19);
        var z = KeyboardGeometry.Position(0x2C);
        Assert.True(q.X < p.X);
        Assert.True(q.Y < z.Y);
        Assert.InRange(KeyboardGeometry.Position(0xE04B).X, 0, 1); // unknown keys still land on screen
    }

    [Fact]
    public void Spawn_limiter_allows_a_burst_then_the_steady_rate()
    {
        var limiter = new SpawnLimiter(perSecond: 8, burst: 4);
        var allowed = Enumerable.Range(0, 10).Count(_ => limiter.TryTake(T0));
        Assert.Equal(4, allowed);
        Assert.True(limiter.TryTake(T0.AddMilliseconds(130)));
    }

    [Fact]
    public void World_bakes_the_oldest_things_past_the_cap_and_drops_effects_when_done()
    {
        var w = new World();
        for (var i = 0; i < World.MaxLiveThings + 5; i++)
            w.Add(new Entity { Id = w.NextId(), Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[0], X = 0.5, Y = 0.5, Born = i });
        w.Add(new Entity { Id = w.NextId(), Role = EntityRole.Effect, Recipe = BuiltInRecipes.Burst, X = 0.5, Y = 0.5, Born = 0, Lifetime = 1 });

        w.Tick(2);

        Assert.Equal(World.MaxLiveThings, w.LiveThingCount);
        Assert.Equal(5, w.Baked.Count);
        Assert.Equal(1, w.Baked[0].Id);
        Assert.DoesNotContain(w.Live, e => e.Role == EntityRole.Effect);
        Assert.True(w.Dirty);
    }

    [Fact]
    public void Untouched_things_fade_to_the_floor_and_a_touch_wakes_them()
    {
        var w = new World();
        var e = w.Add(new Entity { Id = w.NextId(), Role = EntityRole.Thing, Recipe = BuiltInRecipes.Palette[0], X = 0.5, Y = 0.5, Born = 0 });
        for (var i = 0; i < 1200; i++) w.Tick(1);
        Assert.Equal(World.EnergyFloor, e.Energy, 3);
        w.Touch(0.51, 0.5, 0.05);
        Assert.Equal(1, e.Energy);
    }

    [Fact]
    public void Every_concept_and_palette_recipe_is_in_range()
    {
        foreach (var r in BuiltInRecipes.Palette.Concat(BuiltInRecipes.Concepts.Values))
        {
            Assert.InRange(r.Body.Size, 0.1, 1.0);
            Assert.InRange(r.Body.Hue, 0, 359);
            Assert.InRange(r.Eyes, 0, 4);
        }
    }
}
