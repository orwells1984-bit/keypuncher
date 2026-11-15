using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Keypuncher.Core;

/// <summary>
/// What the toy did during one play, one JSON object a line, so a moment in the screen video, the camera video and
/// the camera's sound can be found by its clock time and lined up with what the child saw and heard. Each line has
/// <c>t</c> (local clock time to the millisecond, as the videos are stamped and the log is written), <c>s</c>
/// (seconds since play started, on a steady clock) and <c>ev</c> (what happened), then fields of its own. Lines are
/// written as they happen, so a toy ended from Task Manager keeps everything up to then. Safe to call from any thread.
/// </summary>
public sealed class PlayTimeline : IDisposable
{
    private readonly StreamWriter _out;
    private readonly Func<DateTime> _clock;
    private readonly System.Diagnostics.Stopwatch _steady = System.Diagnostics.Stopwatch.StartNew();
    private readonly Lock _lock = new();
    private bool _closed;

    public PlayTimeline(string path, Func<DateTime>? clock = null)
    {
        Path = path;
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _out = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    }

    public string Path { get; }

    /// <summary>Seconds since the timeline started; the <c>s</c> of the next line.</summary>
    public double Seconds => _steady.Elapsed.TotalSeconds;

    /// <summary>Writes one line: what happened, then name/value pairs (strings, numbers, booleans or null).</summary>
    public void Add(string ev, params (string Name, object? Value)[] fields)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("t", _clock().ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
            json.WriteNumber("s", Math.Round(Seconds, 3));
            json.WriteString("ev", ev);
            foreach (var (name, value) in fields)
            {
                switch (value)
                {
                    case null: json.WriteNull(name); break;
                    case bool b: json.WriteBoolean(name, b); break;
                    case int i: json.WriteNumber(name, i); break;
                    case long l: json.WriteNumber(name, l); break;
                    case double d: json.WriteNumber(name, Math.Round(d, 3)); break;
                    case float f: json.WriteNumber(name, Math.Round(f, 3)); break;
                    default: json.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
                }
            }
            json.WriteEndObject();
        }
        var line = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        lock (_lock)
        {
            if (_closed) return;
            try
            {
                _out.WriteLine(line);
                _out.Flush();
            }
            catch (IOException e)
            {
                // A full disk stops the timeline, never play.
                _closed = true;
                Log.Error("recording", $"writing {Path} failed; the timeline stops", e);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_closed) return;
            _closed = true;
            try { _out.Dispose(); }
            catch (IOException e) { Log.Warn("recording", $"could not close {Path}", e); }
        }
    }
}
