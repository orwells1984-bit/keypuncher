namespace Keypuncher.Core;

/// <summary>
/// The landscape: live entities plus the "baked" ones that were pushed out by the cap and are now
/// part of the painted background. Single-threaded; the app ticks it from the UI thread.
/// </summary>
public sealed class World
{
    public const int MaxLiveThings = 300;
    public const int MaxBaked = 5000;
    /// <summary>Untouched things lose energy over about ten minutes, down to <see cref="EnergyFloor"/>.</summary>
    private const double EnergyDecayPerSecond = 1.0 / 600;
    public const double EnergyFloor = 0.35;

    private readonly List<Entity> _live = [];
    private readonly List<Entity> _baked = [];
    private long _nextId = 1;

    public double Time { get; private set; }
    public IReadOnlyList<Entity> Live => _live;
    /// <summary>Append-only between <see cref="Load"/> and <see cref="Clear"/> calls, so a renderer can
    /// paint only the new tail; trimming from the front bumps <see cref="BakedGeneration"/>.</summary>
    public IReadOnlyList<Entity> Baked => _baked;
    public int BakedGeneration { get; private set; }
    /// <summary>True when something that is saved has changed since <see cref="MarkSaved"/>.</summary>
    public bool Dirty { get; private set; }

    public int LiveThingCount => _live.Count(e => e.IsPersistent);

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

        foreach (var e in _live)
            if (e.IsPersistent && e.Energy > EnergyFloor)
                e.Energy = Math.Max(EnergyFloor, e.Energy - (dt * EnergyDecayPerSecond));

        var things = LiveThingCount;
        if (things <= MaxLiveThings) return;

        // Oldest first; _live is in insertion order, which is birth order.
        for (var i = 0; i < _live.Count && things > MaxLiveThings;)
        {
            if (!_live[i].IsPersistent) { i++; continue; }
            _baked.Add(_live[i]);
            _live.RemoveAt(i);
            things--;
        }
        if (_baked.Count > MaxBaked)
        {
            _baked.RemoveRange(0, _baked.Count - MaxBaked);
            BakedGeneration++;
        }
        Dirty = true;
    }

    /// <summary>Wakes things near a point, e.g. under a tap.</summary>
    public void Touch(double x, double y, double radius)
    {
        foreach (var e in _live)
        {
            if (!e.IsPersistent) continue;
            var dx = e.X - x;
            var dy = e.Y - y;
            if ((dx * dx) + (dy * dy) <= radius * radius) e.Energy = 1;
        }
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
    }

    public void Clear()
    {
        _live.Clear();
        _baked.Clear();
        BakedGeneration++;
        Dirty = true;
    }
}
