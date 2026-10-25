using Keypuncher.Core;

namespace Keypuncher.Director;

/// <summary>One line of the director's log: what it was asked, what came of it, or why it could not run.</summary>
public sealed record DirectorLogEntry(DateTime At, int Slot, DirectorOutcome? Outcome, string? Error);

/// <summary>
/// The director loop. Every few minutes of play (the calm level's <see cref="CalmLimits.RestyleMinutes"/>) it asks
/// the <see cref="RealThingDirector"/> for a real thing for one key family and, if one comes back, restyles that family.
/// Families still holding an invented built-in thing go first; after that, a thing that really grows into
/// something becomes it (tadpole, then frog), and otherwise the family changed longest ago is next.
/// Call <see cref="Update"/> from the UI thread each frame; the model runs on the thread pool and its answer is
/// applied on a later <see cref="Update"/>, so the reflex loop never waits for it.
/// </summary>
public sealed class DirectorLoop(RealThingDirector director, Random? random = null) : IDisposable
{
    /// <summary>The first request comes this long into play, so starting the toy is not when things change.</summary>
    public const double FirstAfterSeconds = 30;
    public const int LogLength = 12;

    /// <summary>Plain names for the built-in animals whose recipe names are shorthand.</summary>
    private static readonly Dictionary<string, string> PlainNames = new()
    {
        ["rex"] = "tyrannosaurus rex",
        ["longneck"] = "brontosaurus",
    };

    private readonly Random _random = random ?? new Random();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<DirectorLogEntry> _log = [];
    private Task<DirectorOutcome>? _running;
    private (int Slot, Recipe Was)? _asked;
    private double? _startedAt, _lastAsked;

    public string ModelName => director.ModelName;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<DirectorLogEntry> Log => _log;

    public bool Busy => _running is not null;

    /// <summary>Raised on the UI thread when a family has been restyled.</summary>
    public event Action<int, Recipe>? Restyled;

    /// <param name="world">The world whose families change.</param>
    /// <param name="restyleMinutes">Minutes of play between requests.</param>
    /// <param name="enabled">False while the director is switched off or the child is asleep: nothing new
    /// is asked, though an answer already on its way is still used.</param>
    public void Update(World world, double restyleMinutes, bool enabled)
    {
        if (_running is { IsCompleted: true } done) Finish(world, done);
        if (!enabled || _running is not null) return;

        _startedAt ??= world.Time;
        var due = _lastAsked is { } last ? last + (restyleMinutes * 60) : _startedAt.Value + FirstAfterSeconds;
        if (world.Time < due) return;

        var request = NextRequest(world.Families);
        _lastAsked = world.Time;
        if (request is null) return;
        _asked = (request.Slot, world.Families[request.Slot]);
        var token = _stop.Token;
        _running = Task.Run(() => director.ProposeAsync(request, token), token);
    }

    /// <summary>The next family to restyle and what to ask for it; null if no family may change.</summary>
    public DirectorRequest? NextRequest(KeyFamilies families)
    {
        var slots = Enumerable.Range(0, families.Count).Where(KeyFamilies.CanRestyle).ToList();
        if (slots.Count == 0) return null;

        var existing = families.All
            .Where((r, i) => !KeyFamilies.CanRestyle(i) || families.ChangedAt(i) is not null)
            .Select(r => PlainNames.GetValueOrDefault(r.Name, r.Name))
            .Distinct()
            .ToList();

        var invented = slots.Where(i => families.ChangedAt(i) is null).ToList();
        if (invented.Count > 0)
        {
            var slot = invented[_random.Next(invented.Count)];
            return new DirectorRequest(slot, families[slot].Kind, null, existing);
        }

        var growing = slots.Where(i => families[i].GrowsInto is not null).OrderBy(i => families.ChangedAt(i)).ToList();
        var pick = growing.Count > 0 ? growing[0] : slots.OrderBy(i => families.ChangedAt(i)).First();
        var current = families[pick];
        return new DirectorRequest(pick, current.Kind, current.Name, existing, current.GrowsInto);
    }

    private void Finish(World world, Task<DirectorOutcome> done)
    {
        var (slot, was) = _asked!.Value;
        _running = null;
        _asked = null;
        if (done.IsCanceled) return;
        if (done.Exception is { } e)
        {
            AddLog(new DirectorLogEntry(DateTime.Now, slot, null, e.GetBaseException().Message));
            return;
        }

        var outcome = done.Result;
        AddLog(new DirectorLogEntry(DateTime.Now, slot, outcome, null));
        // A fresh world, or another change, since the request was made: the answer is for something gone.
        if (outcome.Recipe is not { } recipe || !ReferenceEquals(world.Families[slot], was)) return;
        if (world.Families.Restyle(slot, recipe, world.Time)) Restyled?.Invoke(slot, recipe);
    }

    private void AddLog(DirectorLogEntry entry)
    {
        _log.Insert(0, entry);
        if (_log.Count > LogLength) _log.RemoveAt(_log.Count - 1);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
