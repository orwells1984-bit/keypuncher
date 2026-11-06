using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>Where the background fetching has got to, for the grown-ups' page.</summary>
/// <param name="Queued">Things waiting their turn.</param>
/// <param name="Fetching">The thing being fetched now, or null.</param>
/// <param name="Tried">Fetches finished since the toy started.</param>
/// <param name="Pictured">Of those, how many put a picture in play.</param>
/// <param name="Last">What the last finished fetch came to, as a line.</param>
public sealed record PictureJobsStatus(int Queued, string? Fetching, int Tried, int Pictured, string? Last);

/// <summary>
/// The toy's one background worker for pictures: real things are queued as the director picks them, and the
/// worker fetches each in turn on a low-priority thread, so play never waits on the network. Each thing is tried
/// once per run of the toy unless asked again; the wanted list remembers across runs. When a picture lands,
/// <see cref="Landed"/> is raised on the worker thread with the thing's name, and the caller marshals to the UI
/// thread before telling the renderer. Fetching can be switched off by the grown-ups: then things are still noted
/// as wanted, and fetched when it is on again.
/// </summary>
public sealed class PictureJobs : IDisposable
{
    private readonly PictureFolder _folder;
    private readonly IPictureFetcher _fetcher;
    private readonly Func<bool> _enabled;
    private readonly Action<string>? _note;
    private readonly Func<DateTime> _clock;
    private readonly Lock _lock = new();
    private readonly Queue<(string Name, RealCategory Category)> _queue = new();
    private readonly HashSet<string> _tried = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _worker;
    private string? _fetching;
    private int _finished, _pictured;
    private string? _last;

    /// <param name="enabled">Read before each fetch: the grown-ups' switch; always on when null.</param>
    /// <param name="note">Where lines about each fetch go, for the director log.</param>
    public PictureJobs(PictureFolder folder, IPictureFetcher fetcher, Func<bool>? enabled = null, Action<string>? note = null, Func<DateTime>? clock = null)
    {
        _folder = folder;
        _fetcher = fetcher;
        _enabled = enabled ?? (() => true);
        _note = note;
        _clock = clock ?? (() => DateTime.Now);
        _worker = new Thread(Run) { IsBackground = true, Name = "pictures", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>Raised on the worker thread with the name of a thing whose picture has just landed.</summary>
    public event Action<string>? Landed;

    public PictureJobsStatus Status
    {
        get { lock (_lock) return new PictureJobsStatus(_queue.Count, _fetching, _finished, _pictured, _last); }
    }

    /// <summary>Whether a fetch is running or waiting.</summary>
    public bool Busy
    {
        get { lock (_lock) return _fetching is not null || _queue.Count > 0; }
    }

    /// <summary>Asks for a picture of a thing. Nothing happens when it has one already and nobody asked for another,
    /// or when it was tried this run (unless <paramref name="again"/>). Returns whether it was queued.</summary>
    public bool Enqueue(string name, RealCategory category, bool categoryGuessed = false, bool again = false)
    {
        var slug = Slugs.For(name);
        try
        {
            if (_folder.Wanted.Get(slug) is null)
            {
                if (_folder.HasPicture(slug) || _folder.IsHidden(slug)) return false;
                _folder.Wanted.Add(name, category, _clock(), categoryGuessed);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The wanted list could not be written (the folder read-only, the file held): noted, never fatal to play.
            _note?.Invoke($"could not note {name} as wanted: {e.Message}");
            return false;
        }
        if (!_enabled()) return false;
        lock (_lock)
        {
            if (again) _tried.Remove(slug);
            if (_tried.Contains(slug) || _queue.Any(q => Slugs.For(q.Name) == slug) || _fetching == name) return false;
            _queue.Enqueue((name, category));
        }
        _signal.Release();
        return true;
    }

    /// <summary>Queues everything on the wanted list that has not been tried this run; returns how many.</summary>
    public int RetryWanted(bool again = false) =>
        _folder.Wanted.All().Count(w => Enqueue(w.Name, w.Category, w.CategoryGuessed, again));

    /// <summary>Waits until the queue is empty and nothing is being fetched, for tools and tests; the toy never waits.</summary>
    public bool WaitIdle(TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (Busy)
        {
            if (DateTime.UtcNow > until) return false;
            Thread.Sleep(10);
        }
        return true;
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { _signal.Wait(_stop.Token); }
            catch (OperationCanceledException) { return; }
            (string Name, RealCategory Category) job;
            lock (_lock)
            {
                if (!_queue.TryDequeue(out job)) continue;
                _fetching = job.Name;
            }
            var outcome = "skipped: fetching is switched off";
            try
            {
                if (_enabled())
                {
                    lock (_lock) _tried.Add(Slugs.For(job.Name));
                    var result = _fetcher.FetchAsync(job.Name, job.Category, _stop.Token).GetAwaiter().GetResult();
                    outcome = result.ToString();
                    if (result == FetchOutcome.Pictured)
                    {
                        lock (_lock) _pictured++;
                        Landed?.Invoke(job.Name);
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                outcome = $"failed: {e.Message}";
                _note?.Invoke($"picture of {job.Name} failed: {e}");
            }
            finally
            {
                lock (_lock)
                {
                    _fetching = null;
                    _finished++;
                    _last = $"{job.Name}: {outcome}";
                }
            }
            _note?.Invoke($"picture of {job.Name}: {outcome}");
        }
    }

    /// <summary>Stops the worker; a fetch under way is cancelled and the queue dropped. The wanted list keeps the rest.
    /// Waits two seconds at most, and returns whether the worker stopped in that time: when it did not, a fetch that
    /// would not be cancelled is still running on it, and what it uses must be left to it.</summary>
    public bool Dispose()
    {
        _stop.Cancel();
        _signal.Release();
        var stopped = Thread.CurrentThread == _worker || _worker.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
        _signal.Dispose();
        return stopped;
    }

    void IDisposable.Dispose() => Dispose();
}
