namespace Keypuncher.Core;

/// <summary>
/// The landscape: live entities plus the "baked" ones that were pushed out by the cap and are now
/// part of the painted background. How many things stay live, and how many of them move, comes from
/// <see cref="Limits"/>. Single-threaded; the app ticks it from the UI thread.
/// </summary>
public sealed class World
{
    public const int MaxBaked = 5000;
    /// <summary>A thing pushed out by the cap fades for this long before it joins the background.</summary>
    public const double RetireSeconds = 2;
    /// <summary>While <see cref="Quiet"/>, things settle this many times faster.</summary>
    private const double QuietSettleFactor = 3;

    private readonly List<Entity> _live = [];
    private readonly List<Entity> _baked = [];
    private readonly List<Entity> _awake = [];
    private long _nextId = 1;

    public double Time { get; private set; }
    public IReadOnlyList<Entity> Live => _live;
    /// <summary>Append-only between <see cref="Load"/> and <see cref="Clear"/> calls, so a renderer can
    /// paint only the new tail; trimming from the front bumps <see cref="BakedGeneration"/>.</summary>
    public IReadOnlyList<Entity> Baked => _baked;
    public int BakedGeneration { get; private set; }
    /// <summary>True when something that is saved has changed since <see cref="MarkSaved"/>.</summary>
    public bool Dirty { get; private set; }

    public CalmLimits Limits { get; set; } = CalmLimits.Toddler;
    /// <summary>Set after a while with no input: things settle faster.</summary>
    public bool Quiet { get; set; }
    /// <summary>0 while playing, rising to 1 as the goodnight dims the scene and puts things to sleep.</summary>
    public double Night { get; set; }

    /// <summary>Things that are full members of the scene (not counting ones fading out).</summary>
    public int LiveThingCount => _live.Count(e => e.IsPersistent && !e.IsRetiring);

    public long NextId() => _nextId++;

    public Entity Add(Entity entity)
    {
        _live.Add(entity);
        if (entity.IsPersistent) Dirty = true;
        return entity;
    }

    public void Tick(double dt)
    {
        Time += dt;
        _live.RemoveAll(e => e.Age(Time) > e.Lifetime);

        var settle = dt / Math.Max(0.1, Limits.SettleSeconds) * (Quiet ? QuietSettleFactor : 1);
        foreach (var e in _live)
            if (e.IsPersistent && e.Energy > 0)
                e.Energy = Math.Max(0, e.Energy - settle);

        // Oldest first; _live is in insertion order, which is birth order, so the newest is never pushed out.
        var things = LiveThingCount;
        for (var i = 0; i < _live.Count && things > Math.Max(1, Limits.LiveThings); i++)
        {
            if (!_live[i].IsPersistent || _live[i].IsRetiring) continue;
            _live[i].RetiredAt = Time;
            things--;
            Dirty = true;
        }

        var baked = false;
        for (var i = 0; i < _live.Count;)
        {
            if (_live[i].RetiredAt is not { } at || Time - at < RetireSeconds) { i++; continue; }
            _live[i].Awake = false;
            _baked.Add(_live[i]);
            _live.RemoveAt(i);
            baked = true;
        }
        if (_baked.Count > MaxBaked)
        {
            _baked.RemoveRange(0, _baked.Count - MaxBaked);
            BakedGeneration++;
        }
        if (baked) Dirty = true;

        UpdateAwake();
    }

    /// <summary>Wakes things near a point, e.g. under a tap.</summary>
    public void Touch(double x, double y, double radius)
    {
        foreach (var e in _live)
        {
            if (!e.IsPersistent || e.IsRetiring) continue;
            var dx = e.X - x;
            var dy = e.Y - y;
            if ((dx * dx) + (dy * dy) <= radius * radius) Wake(e);
        }
    }

    /// <summary>Wakes one thing, so it moves again and counts as the most recent.</summary>
    public void Wake(Entity e)
    {
        e.Energy = 1;
        e.Woken = Time;
        UpdateAwake();
    }

    /// <summary>The most recently born thing made from <paramref name="recipe"/>, or failing that the most
    /// recent thing of any kind; null in an empty world.</summary>
    public Entity? Newest(Recipe? recipe)
    {
        Entity? any = null;
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var e = _live[i];
            if (!e.IsPersistent || e.IsRetiring) continue;
            if (e.Recipe == recipe) return e;
            any ??= e;
        }
        return any;
    }

    public void MarkSaved() => Dirty = false;

    public void Load(double time, IEnumerable<Entity> live, IEnumerable<Entity> baked)
    {
        _live.Clear();
        _baked.Clear();
        Time = time;
        _live.AddRange(live);
        _baked.AddRange(baked);
        _nextId = _live.Concat(_baked).Select(e => e.Id).DefaultIfEmpty(0).Max() + 1;
        BakedGeneration++;
        Dirty = false;
        UpdateAwake();
    }

    public void Clear()
    {
        _live.Clear();
        _baked.Clear();
        BakedGeneration++;
        Dirty = true;
    }

    /// <summary>Only the few most recently woken things move; the goodnight lets them fall asleep one by one.</summary>
    private void UpdateAwake()
    {
        var allowed = Night >= 1 ? 0 : (int)Math.Ceiling(Limits.Moving * (1 - Night));
        _awake.Clear();
        foreach (var e in _live)
        {
            if (!e.IsPersistent) continue;
            e.Awake = false;
            if (!e.IsRetiring && e.Energy > 0) _awake.Add(e);
        }
        _awake.Sort((a, b) => b.Woken.CompareTo(a.Woken) is var c && c != 0 ? c : b.Id.CompareTo(a.Id));
        for (var i = 0; i < Math.Min(allowed, _awake.Count); i++) _awake[i].Awake = true;
    }
}
