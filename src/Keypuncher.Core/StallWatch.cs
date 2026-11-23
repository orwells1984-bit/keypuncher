using System.Diagnostics;
using System.Globalization;

namespace Keypuncher.Core;

/// <summary>
/// Notices when the screen stops being drawn, and says what was going on. The page that draws calls
/// <see cref="Beat()"/> at the start of every frame and names what it is doing in <see cref="Phase"/>; a timer on
/// another thread looks at how long ago the last beat was. A pause is written down twice: while it lasts (after
/// <see cref="Threshold"/>, then every <see cref="Again"/>), with what the drawing thread was in the middle of and
/// what the card and the model were doing, and once more when drawing comes back, with how long it was.
/// </summary>
/// <remarks>On 2026-11-23 play stood still for 21.5 s while the model read its first prompt: no frame was drawn and
/// 14 key presses arrived in one burst after it. The log of that play had nothing between the question and the
/// answer, so the cause could only be guessed at.</remarks>
public sealed class StallWatch(Func<string> what, Action<string> report) : IDisposable
{
    /// <summary>The shortest gap between two frames that counts as a pause.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromSeconds(1);
    /// <summary>How often a pause still going on is written down again.</summary>
    public static readonly TimeSpan Again = TimeSpan.FromSeconds(5);
    /// <summary>How many times one pause is written down while it lasts; a screen locked for the night is one pause.</summary>
    public const int MostReports = 12;
    private static readonly TimeSpan LookEvery = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch _clock = new();
    private readonly Lock _lock = new();
    private Timer? _timer;
    private TimeSpan? _lastBeat, _reported;
    private string _stuckIn = "";
    private int _looking, _reports;

    /// <summary>What the drawing thread is doing now ("drawing the scene", "saving the world", "between frames").</summary>
    public string Phase
    {
        get => _phase;
        set => _phase = value;
    }

    private volatile string _phase = "between frames";

    /// <summary>How many pauses have ended since the watch started.</summary>
    public int Pauses { get; private set; }

    /// <summary>Starts watching: the first frame is owed from now. With a time given (tests), the watch runs on the
    /// caller's times alone and no timer is started.</summary>
    public void Start(TimeSpan? at = null)
    {
        lock (_lock)
        {
            _clock.Restart();
            (_lastBeat, _reported, _reports) = (at ?? TimeSpan.Zero, null, 0);
            if (at is null) _timer ??= new Timer(_ => LookSafely(), null, LookEvery, LookEvery);
        }
    }

    /// <summary>Stops watching, for a page that is going away: no frames are owed any more.</summary>
    public void Stop()
    {
        Timer? timer;
        lock (_lock)
        {
            (timer, _timer) = (_timer, null);
            _lastBeat = null;
            _clock.Stop();
        }
        timer?.Dispose();
    }

    /// <summary>A frame is starting. Returns how long drawing was paused when this beat ends a pause, else null.</summary>
    public TimeSpan? Beat() => Beat(_clock.Elapsed);

    /// <summary>As <see cref="Beat()"/>, at a given time, for tests.</summary>
    public TimeSpan? Beat(TimeSpan now)
    {
        TimeSpan gap;
        string stuckIn;
        lock (_lock)
        {
            if (_lastBeat is not { } last) return null;
            gap = now - last;
            // What the thread was in when the watch last looked; on a pause too short to have been looked at, what it
            // is leaving now.
            stuckIn = _reported is null ? Phase : _stuckIn;
            (_lastBeat, _reported, _reports) = (now, null, 0);
            if (gap < Threshold) return null;
            Pauses++;
        }
        Say($"drawing was paused for {Seconds(gap)} s; the drawing thread was {stuckIn}; now: {What()}");
        return gap;
    }

    /// <summary>The timer's look, at a given time, for tests: reports a pause that has begun or is still going on.
    /// Returns whether it reported.</summary>
    public bool Look(TimeSpan now)
    {
        TimeSpan gap;
        string phase;
        lock (_lock)
        {
            if (_lastBeat is not { } last) return false;
            gap = now - last;
            if (gap < Threshold || _reports >= MostReports || (_reported is { } before && now - before < Again)) return false;
            (_reported, _reports) = (now, _reports + 1);
            phase = _stuckIn = Phase;
        }
        Say($"nothing drawn for {Seconds(gap)} s; the drawing thread is {phase}; {What()}");
        return true;
    }

    private void LookSafely()
    {
        // The look may take a while (it asks the card); the timer's next ticks pass meanwhile.
        if (Interlocked.Exchange(ref _looking, 1) == 1) return;
        try { Look(_clock.Elapsed); }
        catch (Exception e) { Log.Warn("drawing", "looking for a pause in the drawing failed", e); }
        finally { Volatile.Write(ref _looking, 0); }
    }

    private string What()
    {
        try { return what(); }
        catch (Exception e) { return $"(what was going on could not be read: {e.Message})"; }
    }

    private void Say(string line)
    {
        try { report(line); }
        catch (Exception e) { Log.Warn("drawing", "a pause in the drawing could not be written down", e); }
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

    public void Dispose() => Stop();
}
