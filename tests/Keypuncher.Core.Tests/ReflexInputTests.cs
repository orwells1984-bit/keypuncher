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
    public void World_retires_the_oldest_things_past_the_cap_with_a_fade_and_drops_effects_when_done()
    {
        var w = new World();
        var cap = w.Limits.LiveThings;
        for (var i = 0; i < cap + 3; i++) AddThing(w, born: i);
        w.Add(new Entity { Id = w.NextId(), Role = EntityRole.Effect, Recipe = BuiltInRecipes.Burst, X = 0.5, Y = 0.5, Born = 0, Lifetime = 1 });
        w.Tick(1.5);

        // Still on screen while they fade, but no longer counted.
        Assert.Equal(cap, w.LiveThingCount);
        Assert.Equal([1L, 2, 3], w.Live.Where(e => e.IsRetiring).Select(e => e.Id));
        Assert.Empty(w.Baked);
        Assert.DoesNotContain(w.Live, e => e.Role == EntityRole.Effect);

        w.Tick(World.RetireSeconds);
        Assert.Equal([1L, 2, 3], w.Baked.Select(e => e.Id));
        Assert.DoesNotContain(w.Live, e => e.IsRetiring);
        Assert.Contains(w.Live, e => e.Id == cap + 3);   // the newest is never the one pushed out
        Assert.True(w.Dirty);
    }

    [Fact]
    public void Untouched_things_settle_to_still_and_a_touch_wakes_them()
    {
        var w = new World { Limits = CalmLimits.Toddler };
        var e = AddThing(w, born: 0);
        w.Tick(CalmLimits.Toddler.SettleSeconds / 2);
        Assert.InRange(e.Energy, 0.45, 0.55);
        Assert.True(e.Awake);
        w.Tick(CalmLimits.Toddler.SettleSeconds / 2 + 0.01);
        Assert.Equal(0, e.Energy);
        Assert.False(e.Awake);

        w.Touch(0.51, 0.5, 0.05);
        Assert.Equal(1, e.Energy);
        Assert.True(e.Awake);
    }

    [Fact]
    public void Quiet_settles_things_faster()
    {
        var w = new World { Quiet = true };
        var e = AddThing(w, born: 0);
        w.Tick((w.Limits.SettleSeconds / 3) + 0.01);
        Assert.Equal(0, e.Energy);
    }

    [Fact]
    public void Only_the_most_recently_woken_few_move()
    {
        var w = new World { Limits = CalmLimits.Toddler };
        var things = Enumerable.Range(0, 5).Select(i => AddThing(w, born: i * 0.1)).ToList();
        w.Tick(0.6);
        Assert.Equal([things[3], things[4]], things.Where(e => e.Awake));

        w.Wake(things[0]);
        Assert.Equal([things[0], things[4]], things.Where(e => e.Awake));
    }

    [Fact]
    public void The_goodnight_puts_every_thing_to_sleep()
    {
        var w = new World { Limits = CalmLimits.BigToddler };
        for (var i = 0; i < 4; i++) AddThing(w, born: 0);
        w.Tick(0.1);
        Assert.Equal(4, w.Live.Count(e => e.Awake));
        w.Night = 0.5;
        w.Tick(0.1);
        Assert.Equal(2, w.Live.Count(e => e.Awake));
        w.Night = 1;
        w.Tick(0.1);
        Assert.DoesNotContain(w.Live, e => e.Awake);
    }

    [Fact]
    public void Newest_prefers_the_same_recipe()
    {
        var w = new World();
        var a = AddThing(w, born: 0, BuiltInRecipes.Palette[0]);
        AddThing(w, born: 1, BuiltInRecipes.Palette[1]);
        Assert.Same(a, w.Newest(BuiltInRecipes.Palette[0]));
        Assert.Equal(2, w.Newest(BuiltInRecipes.Palette[5])!.Id);
        Assert.Null(new World().Newest(BuiltInRecipes.Palette[0]));
    }

    [Fact]
    public void Calm_levels_get_busier_with_age()
    {
        CalmLimits[] levels = [CalmLimits.For(CalmLevel.Baby), CalmLimits.For(CalmLevel.Toddler), CalmLimits.For(CalmLevel.BigToddler)];
        foreach (var (calmer, busier) in levels.Zip(levels.Skip(1)))
        {
            Assert.True(calmer.LiveThings < busier.LiveThings);
            Assert.True(calmer.Moving < busier.Moving);
            Assert.True(calmer.SettleSeconds < busier.SettleSeconds);
            Assert.True(calmer.NewThingsPerSecond < busier.NewThingsPerSecond);
            Assert.True(calmer.HistoryOpacity < busier.HistoryOpacity);
            Assert.True(calmer.SessionMinutes < busier.SessionMinutes);
        }
        Assert.All(levels, l => Assert.InRange(l.MaxMotionHz, 0.1, 1));
        Assert.All(levels, l => Assert.InRange(l.CountUpTo, 1, 9));
    }

    [Fact]
    public void Effects_avoid_saturated_red()
    {
        // Bursts flash in and out; red flashes are the riskiest kind (WCAG 2.3.1).
        foreach (var r in new[] { BuiltInRecipes.Burst, BuiltInRecipes.Sparkle })
            Assert.InRange(r.Body.Hue, 20, 340);
    }

    private static Entity AddThing(World w, double born, Recipe? recipe = null) =>
        w.Add(new Entity { Id = w.NextId(), Role = EntityRole.Thing, Recipe = recipe ?? BuiltInRecipes.Palette[0], X = 0.5, Y = 0.5, Born = born });

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
