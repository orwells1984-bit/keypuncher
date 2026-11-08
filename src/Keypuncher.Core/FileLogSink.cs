using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Keypuncher.Core;

/// <summary>
/// Writes the log to a text file, one entry a line (an exception follows on indented lines), from a background
/// thread so that the UI thread and the keyboard hook never wait on the disk. When the file passes
/// <see cref="MaxBytes"/> it is moved aside to <c>name.1.log</c>, the older ones shift up, and the oldest past
/// <see cref="Keep"/> is deleted, so the log never takes more than about MaxBytes × (Keep + 1). The file is opened
/// shared, so it can be read (or tailed) while the toy runs.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    /// <summary>Entries waiting beyond this are dropped and counted, rather than let memory grow.</summary>
    private const int MaxQueued = 20_000;

    private readonly BlockingCollection<LogRecord> _queue = new(MaxQueued);
    private readonly Thread _writer;
    private readonly object _flushLock = new();
    private long _queued, _written;
    private int _dropped;
    private StreamWriter? _file;

    public FileLogSink(string path, long maxBytes = 5_000_000, int keep = 4)
    {
        Path = System.IO.Path.GetFullPath(path);
        MaxBytes = maxBytes;
        Keep = keep;
        _writer = new Thread(Run) { IsBackground = true, Name = "log writer", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public string Path { get; }
    public long MaxBytes { get; }
    public int Keep { get; }

    public void Write(in LogRecord record)
    {
        if (_queue.IsAddingCompleted) return;
        Interlocked.Increment(ref _queued);
        try
        {
            if (!_queue.TryAdd(record))
            {
                Interlocked.Increment(ref _dropped);
                Interlocked.Increment(ref _written);
            }
        }
        catch (InvalidOperationException)
        {
            // Disposed between the check and the add.
            Interlocked.Increment(ref _written);
        }
    }

    public void Flush(TimeSpan timeout)
    {
        var target = Interlocked.Read(ref _queued);
        var until = DateTime.UtcNow + timeout;
        lock (_flushLock)
        {
            while (Interlocked.Read(ref _written) < target && _writer.IsAlive)
            {
                var left = until - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return;
                Monitor.Wait(_flushLock, left);
            }
        }
    }

    /// <summary>One entry as it appears in the file.</summary>
    public static string Format(in LogRecord r)
    {
        var line = new StringBuilder(r.Message.Length + 64);
        line.Append(r.At.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ');
        line.Append(r.Level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warn => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "FATAL",
        });
        line.Append(' ').Append(r.Area.PadRight(9)).Append(' ');
        line.Append('[').Append(r.ThreadName is { Length: > 0 } name ? $"{r.ThreadId} {name}" : r.ThreadId.ToString(CultureInfo.InvariantCulture)).Append("] ");
        line.Append(r.Message);
        if (r.Suppressed > 0) line.Append($"  (and {r.Suppressed} more the same in the {Log.RepeatWindow.TotalSeconds:0} s before this)");
        if (r.Exception is { } e)
        {
            line.Append(Environment.NewLine);
            foreach (var l in e.ToString().ReplaceLineEndings("\n").Split('\n'))
                line.Append("    ").Append(l).Append(Environment.NewLine);
            line.Length -= Environment.NewLine.Length;
        }
        return line.ToString();
    }

    private void Run()
    {
        foreach (var record in _queue.GetConsumingEnumerable())
        {
            try
            {
                var lost = Interlocked.Exchange(ref _dropped, 0);
                if (lost > 0)
                    WriteLine($"{record.At:yyyy-MM-dd HH:mm:ss.fff} WARN  log       {lost} entries were dropped: the log could not keep up");
                WriteLine(Format(record));
                // Flushed when the queue is empty, so a burst is written in one go and a crash loses nothing waiting.
                if (_queue.Count == 0) _file?.Flush();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Keypuncher log could not write to {Path}: {e}");
                CloseFile();
            }
            finally
            {
                Interlocked.Increment(ref _written);
                lock (_flushLock) Monitor.PulseAll(_flushLock);
            }
        }
        CloseFile();
    }

    private void WriteLine(string text)
    {
        var file = Open();
        file.WriteLine(text);
        if (file.BaseStream.Length > MaxBytes)
        {
            CloseFile();
            Rotate();
        }
    }

    private StreamWriter Open()
    {
        if (_file is not null) return _file;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        return _file = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
    }

    /// <summary>name.log → name.1.log → name.2.log …, dropping the oldest.</summary>
    private void Rotate()
    {
        string Numbered(int n) => System.IO.Path.ChangeExtension(Path, $".{n}{System.IO.Path.GetExtension(Path)}");
        if (File.Exists(Numbered(Keep))) File.Delete(Numbered(Keep));
        for (var n = Keep - 1; n >= 1; n--)
            if (File.Exists(Numbered(n))) File.Move(Numbered(n), Numbered(n + 1));
        if (Keep >= 1) File.Move(Path, Numbered(1));
        else File.Delete(Path);
    }

    private void CloseFile()
    {
        try { _file?.Dispose(); }
        catch (Exception e) { Trace.WriteLine($"Keypuncher log could not close {Path}: {e}"); }
        _file = null;
    }

    /// <summary>Writes what is waiting, then closes the file.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _writer.Join(TimeSpan.FromSeconds(3));
    }
}
