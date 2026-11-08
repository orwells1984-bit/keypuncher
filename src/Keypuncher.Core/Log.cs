using System.Diagnostics;

namespace Keypuncher.Core;

public enum LogLevel { Debug, Info, Warn, Error, Fatal }

/// <summary>One entry in the app's log. <paramref name="Suppressed"/> is how many identical entries were held back
/// since the last one of these was written (see <see cref="Log.RepeatWindow"/>).</summary>
public readonly record struct LogRecord(DateTime At, LogLevel Level, string Area, string Message, Exception? Exception,
    int ThreadId, string? ThreadName, int Suppressed = 0);

/// <summary>Where log entries go: the app adds a <see cref="FileLogSink"/>; tests add their own.</summary>
public interface ILogSink
{
    /// <summary>Called on whichever thread logged; must not block for long and must not throw.</summary>
    void Write(in LogRecord record);

    /// <summary>Waits, at most <paramref name="timeout"/>, until everything written so far is on disk.</summary>
    void Flush(TimeSpan timeout);
}

/// <summary>
/// The app-wide log. Any part of the toy can write to it from any thread; with no sink added (the tests, the
/// command-line tools) it costs a level check and nothing else. Logging never throws: a sink that fails is reported
/// to <see cref="Trace"/> and the caller carries on.
/// <para>
/// The same entry (level, area, message and exception type) written again within <see cref="RepeatWindow"/> is held
/// back and counted, so an error in something that runs every frame writes a line every half minute, not sixty a
/// second; the next one written says how many were held back.
/// </para>
/// </summary>
public static class Log
{
    /// <summary>Identical entries within this time are counted rather than written.</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(30);
    private const int RepeatMemory = 512;

    private static readonly object Lock = new();
    private static readonly Dictionary<(LogLevel, string, string, Type?), (DateTime Written, int Held)> Recent = [];
    private static ILogSink[] _sinks = [];

    /// <summary>Entries below this level are dropped. Debug by default: the file is size-capped.</summary>
    public static LogLevel MinLevel { get; set; } = LogLevel.Debug;

    /// <summary>The clock entries are stamped with; local time, so the log reads like the director's log.</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    public static bool Enabled(LogLevel level) => level >= MinLevel && _sinks.Length > 0;

    public static void AddSink(ILogSink sink)
    {
        lock (Lock) _sinks = [.. _sinks, sink];
    }

    public static void RemoveSink(ILogSink sink)
    {
        lock (Lock) _sinks = [.. _sinks.Where(s => s != sink)];
    }

    public static void Debug(string area, string message, Exception? e = null) => Write(LogLevel.Debug, area, message, e);
    public static void Info(string area, string message, Exception? e = null) => Write(LogLevel.Info, area, message, e);
    public static void Warn(string area, string message, Exception? e = null) => Write(LogLevel.Warn, area, message, e);
    public static void Error(string area, string message, Exception? e = null) => Write(LogLevel.Error, area, message, e);
    public static void Fatal(string area, string message, Exception? e = null) => Write(LogLevel.Fatal, area, message, e);

    public static void Write(LogLevel level, string area, string message, Exception? e = null)
    {
        if (!Enabled(level)) return;
        try
        {
            var now = Clock();
            int held;
            lock (Lock)
            {
                var key = (level, area, message, e?.GetType());
                if (Recent.TryGetValue(key, out var seen) && now - seen.Written < RepeatWindow)
                {
                    Recent[key] = seen with { Held = seen.Held + 1 };
                    return;
                }
                held = seen.Held;
                if (Recent.Count >= RepeatMemory)
                    foreach (var old in Recent.Where(kv => now - kv.Value.Written >= RepeatWindow).Select(kv => kv.Key).ToList())
                        Recent.Remove(old);
                Recent[key] = (now, 0);
            }
            var thread = Thread.CurrentThread;
            var record = new LogRecord(now, level, area, message, e, thread.ManagedThreadId, thread.Name, held);
            foreach (var sink in _sinks)
            {
                try { sink.Write(record); }
                catch (Exception failed) { Trace.WriteLine($"Keypuncher log sink {sink.GetType().Name} failed: {failed}"); }
            }
        }
        catch (Exception failed)
        {
            Trace.WriteLine($"Keypuncher log failed: {failed}");
        }
    }

    /// <summary>Waits until every sink has written what it has, at most <paramref name="timeout"/> each.</summary>
    public static void Flush(TimeSpan? timeout = null)
    {
        foreach (var sink in _sinks)
        {
            try { sink.Flush(timeout ?? TimeSpan.FromSeconds(2)); }
            catch (Exception failed) { Trace.WriteLine($"Keypuncher log flush failed: {failed}"); }
        }
    }

    /// <summary>Runs <paramref name="action"/>, logging (and swallowing) anything it throws: for steps such as the
    /// parts of shutting down, where one failing must not stop the rest.</summary>
    public static void Safely(string area, string what, Action action)
    {
        try { action(); }
        catch (Exception e) { Error(area, $"{what} failed", e); }
    }

    /// <summary>Logs the task's exception, if it ends with one, instead of leaving it unobserved: for fire-and-forget
    /// work. A cancelled task is noted at Debug.</summary>
    public static void LogFailures(this Task task, string area, string what)
    {
        task.ContinueWith(t =>
        {
            if (t.IsCanceled) Debug(area, $"{what} was cancelled");
            else if (t.Exception is { } e) Error(area, $"{what} failed", e.InnerExceptions.Count == 1 ? e.InnerException : e);
        }, CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The first <paramref name="max"/> characters of a text, on one line, for a log entry.</summary>
    public static string Short(string? text, int max = 300)
    {
        if (text is null) return "(nothing)";
        var one = text.ReplaceLineEndings(" ⏎ ").Trim();
        return one.Length <= max ? one : one[..max] + $"… ({text.Length} chars)";
    }
}
