using System.Threading.Channels;
using SkiaSharp;

namespace Keypuncher.Rendering;

/// <summary>
/// Records what the screen shows as a small video, for looking back at a test session or a child's play alongside
/// the log: <see cref="Fps"/> frames a second at <see cref="Width"/> pixels wide, each stamped with the clock time
/// that the log's lines carry. The frame is shrunk on the calling (UI) thread, which is cheap; encoding and
/// writing happen on a worker, and frames are dropped rather than ever holding up play. Each session is one
/// .avi file, started afresh past <see cref="MaxFileBytes"/>; the oldest are deleted to keep the folder under
/// <see cref="MaxFolderBytes"/>.
/// </summary>
public sealed class SessionRecorder : IDisposable
{
    public const int Fps = 4;
    public const int Width = 960;
    public const long MaxFileBytes = 1_000_000_000;
    public const long MaxFolderBytes = 4_000_000_000;

    private readonly string _folder, _prefix;
    private readonly Func<DateTime> _clock;
    private readonly Channel<(SKImage Frame, int Copies)> _frames =
        Channel.CreateBounded<(SKImage, int)>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly Task _worker;
    private readonly SKFont _font = new(Fonts.LoadAndika(), 18);
    private readonly SKPaint _ink = new() { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    private readonly SKPaint _label = new() { Color = SKColors.White.WithAlpha(200) };
    private double? _start;
    private long _offered;
    private MjpegAviWriter? _writer;

    /// <param name="prefix">Starts each file's name: "test" for test mode, "play" for a child's play.</param>
    public SessionRecorder(string folder, string prefix = "test", Func<DateTime>? clock = null)
    {
        _folder = folder;
        _prefix = prefix;
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(folder);
        Prune();
        _worker = Task.Run(WriteAsync);
    }

    /// <summary>The file being written, once the first frame has arrived.</summary>
    public string? CurrentFile { get; private set; }

    /// <summary>Called after each frame is drawn, with seconds on a steady clock; keeps one frame each
    /// 1/<see cref="Fps"/> s, repeated when the screen was slower than that so the video keeps real time.</summary>
    public void Offer(SKSurface surface, int width, int height, double seconds)
    {
        _start ??= seconds;
        var due = (long)((seconds - _start.Value) * Fps) + 1;
        if (due <= _offered) return;
        var copies = (int)Math.Min(due - _offered, Fps * 2);
        _offered = due;

        var h = Math.Max(2, (int)Math.Round(Width * height / (double)Math.Max(1, width) / 2) * 2);
        using var screen = surface.Snapshot();
        using var small = SKSurface.Create(new SKImageInfo(Width, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        small.Canvas.DrawImage(screen, new SKRect(0, 0, Width, h), new SKSamplingOptions(SKFilterMode.Linear));
        var stamp = _clock().ToString("HH:mm:ss");
        var w = _font.MeasureText(stamp);
        small.Canvas.DrawRect(Width - w - 12, h - 24, w + 12, 24, _label);
        small.Canvas.DrawText(stamp, Width - 6, h - 7, SKTextAlign.Right, _font, _ink);
        var frame = small.Snapshot();
        if (!_frames.Writer.TryWrite((frame, copies))) frame.Dispose();
    }

    public void Dispose()
    {
        _frames.Writer.TryComplete();
        try { _worker.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        _writer?.Dispose();
        _font.Dispose();
        _ink.Dispose();
        _label.Dispose();
    }

    private async Task WriteAsync()
    {
        await foreach (var (frame, copies) in _frames.Reader.ReadAllAsync())
            using (frame)
            {
                try
                {
                    using var data = frame.Encode(SKEncodedImageFormat.Jpeg, 72);
                    if (_writer is null || _writer.Length > MaxFileBytes)
                    {
                        _writer?.Dispose();
                        CurrentFile = Path.Combine(_folder, $"{_prefix}-{_clock():yyyy-MM-dd-HHmmss}.avi");
                        _writer = new MjpegAviWriter(CurrentFile, frame.Width, frame.Height, Fps);
                    }
                    for (var i = 0; i < copies; i++) _writer.AddFrame(data.AsSpan());
                }
                catch (IOException)
                {
                    // A full disk or a locked file stops the recording, never play.
                    _writer?.Dispose();
                    _writer = null;
                    return;
                }
            }
    }

    /// <summary>Deletes the oldest recordings until the folder is under its limit with room for one more file.</summary>
    private void Prune()
    {
        var files = new DirectoryInfo(_folder).GetFiles("*.avi").OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var f in files)
        {
            if (total + MaxFileBytes <= MaxFolderBytes) break;
            total -= f.Length;
            try { f.Delete(); }
            catch (IOException) { }
        }
    }
}
