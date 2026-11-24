using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Keypuncher.Core;
using Keypuncher.Rendering;
using NAudio.Wave;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Records the child during play: the webcam as a Motion-JPEG video (<see cref="Fps"/> frames a second, about
/// <see cref="Width"/> pixels wide, each stamped with the clock time the log and the play timeline use) and the
/// microphone as a WAV file beside it (the camera and microphone a grown-up chose on the grown-ups' page, else the
/// first camera Windows lists and Windows' default microphone), both named by the time play started. For gauging the child's
/// reactions afterwards, on this computer only. A missing or busy camera, or a microphone that will not open, is
/// written to the log and play goes on without it; nothing here may ever hold play up.
/// </summary>
public sealed class CameraRecorder : IDisposable
{
    public const int Fps = 10;
    public const int Width = 640;
    public const int MicSampleRate = 16000;
    public const long MaxFileBytes = 1_000_000_000;
    /// <summary>Camera videos and their sound together are kept under this; the oldest plays go first.</summary>
    public const long MaxFolderBytes = 8_000_000_000;
    /// <summary>Starts each file's name, so the camera's files are never mistaken for the screen's.</summary>
    public const string Prefix = "cam";

    private readonly string _folder, _stamp;
    private readonly string? _cameraId, _cameraName, _micName;
    private readonly Func<DateTime> _clock;
    private readonly Stopwatch _steady = new();
    private readonly Channel<(byte[] Jpeg, int W, int H, int Copies)> _frames =
        Channel.CreateBounded<(byte[], int, int, int)>(new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly Thread _worker;
    private readonly Task _starting;
    private readonly SKFont _font = new(Fonts.LoadAndika(), 14);
    private readonly SKPaint _ink = new() { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    private readonly SKPaint _label = new() { Color = SKColors.White.WithAlpha(200) };
    private readonly Lock _lock = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private WaveIn? _mic;
    private WaveFileWriter? _wav;
    private MjpegAviWriter? _writer;
    private int _busy;
    private long _offered;
    private int _owed;
    private volatile bool _stopping;
    private FaceDetector? _faces;
    private bool _facesFailed;
    private double _lastLook = double.NegativeInfinity;
    private ChildInView? _view;
    private volatile bool _inView = true;
    private byte[]? _lastCells;
    private volatile int _lastFaces;
    private int _looks, _faceLooks, _movingLooks;
    private double _reportedAt;
    private volatile bool _filming;
    private SKImage? _preview;

    /// <summary>Starts recording at once: the microphone here (call on the UI thread, which its callbacks need), the
    /// camera in the background, as opening it can take a second or two.</summary>
    /// <param name="stamp">The play's start time as it appears in its other files' names (yyyy-MM-dd-HHmmss).</param>
    /// <param name="cameraId">The camera a grown-up chose (<see cref="CaptureDevice.Id"/>), with its name to find it by when
    /// its id has changed; null for the first camera Windows lists.</param>
    /// <param name="micName">The microphone a grown-up chose, by name; null for Windows' default.</param>
    public CameraRecorder(string folder, string stamp, string? cameraId = null, string? cameraName = null, string? micName = null,
        bool withMic = true, Func<DateTime>? clock = null)
    {
        _folder = folder;
        _stamp = stamp;
        (_cameraId, _cameraName, _micName) = (cameraId, cameraName, micName);
        _clock = clock ?? (() => DateTime.Now);
        Directory.CreateDirectory(folder);
        Prune(folder);
        _steady.Start();
        _worker = new Thread(Write) { IsBackground = true, Name = "Camera recorder", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
        if (withMic) StartMic();
        _starting = Task.Run(StartCameraAsync);
    }

    /// <summary>The camera being recorded, once it is open.</summary>
    public string? CameraName { get; private set; }

    /// <summary>Faces and movement are looked for in a frame this often.</summary>
    public const double LookEvery = 0.5;
    /// <summary>A line on what the looks found goes to the log (at Debug) this often.</summary>
    public const double ReportEvery = 10;

    /// <summary>Whether the camera can see the child, as <see cref="ChildInView"/> decides it from faces and movement
    /// (a child looking down at the keys shows no face); null while the camera is not filming or faces cannot be
    /// looked for. Read from any thread.</summary>
    public bool? ChildInFrame => !_filming || _facesFailed ? null : _inView;

    /// <summary>Whether the last look found the picture too dark to see anyone in (<see cref="MotionGrid.TooDark"/>):
    /// the room wants a light, not the child moving. Read from any thread.</summary>
    public bool TooDark => _tooDark;
    private volatile bool _tooDark;

    /// <summary>How many faces the last look found, for the timeline.</summary>
    public int FacesAtLastLook => _lastFaces;

    /// <summary>The newest camera picture (without its time stamp) while the child is out of frame, for the toy to
    /// show so a grown-up can see what the camera sees; null when there is no newer one than the last taken. The
    /// caller owns the image and disposes it.</summary>
    public SKImage? TakePreview() => Interlocked.Exchange(ref _preview, null);

    /// <summary>The video file, once the first frame has arrived.</summary>
    public string? VideoFile { get; private set; }

    /// <summary>The sound file, once the microphone is open.</summary>
    public string? SoundFile { get; private set; }

    /// <summary>The cameras Windows knows of, in its order, for the grown-ups' page. Empty when there are none or
    /// the question fails.</summary>
    public static async Task<IReadOnlyList<CaptureDevice>> FindCamerasAsync()
    {
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            return [.. groups.Where(g => ColorSource(g) is not null).Select(g => new CaptureDevice(g.Id, g.DisplayName))];
        }
        catch (Exception e)
        {
            Log.Warn("camera", "could not ask Windows for its cameras", e);
            return [];
        }
    }

    /// <summary>The microphones Windows knows of, by the names its sound recording gives them (at most 31 letters),
    /// for the grown-ups' page; each <see cref="CaptureDevice.Id"/> is its number now, which can change.</summary>
    public static IReadOnlyList<CaptureDevice> FindMicrophones()
    {
        try
        {
            return [.. Enumerable.Range(0, WaveIn.DeviceCount).Select(i => new CaptureDevice(i.ToString(), WaveIn.GetCapabilities(i).ProductName))];
        }
        catch (Exception e)
        {
            Log.Warn("camera", "could not ask Windows for its microphones", e);
            return [];
        }
    }

    /// <summary>One picture from a camera, as a JPEG about <see cref="Width"/> pixels wide, so a grown-up can see
    /// which camera is which; null when it cannot be had in a few seconds. Opens the camera alongside anything else
    /// using it, and waits a moment for it to settle, as a camera's first frames are often dark.</summary>
    public static async Task<byte[]?> SnapshotAsync(string cameraId, CancellationToken cancel = default)
    {
        MediaCapture? capture = null;
        MediaFrameReader? reader = null;
        try
        {
            var groups = await MediaFrameSourceGroup.FindAllAsync();
            var group = groups.FirstOrDefault(g => g.Id == cameraId && ColorSource(g) is not null);
            if (group is null) return null;
            var info = ColorSource(group)!;
            capture = new MediaCapture();
            try { await capture.InitializeAsync(Settings(group, MediaCaptureSharingMode.ExclusiveControl)); }
            catch (Exception)
            {
                capture.Dispose();
                capture = new MediaCapture();
                await capture.InitializeAsync(Settings(group, MediaCaptureSharingMode.SharedReadOnly));
            }
            reader = await capture.CreateFrameReaderAsync(capture.FrameSources[info.Id], MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            if (await reader.StartAsync() != MediaFrameReaderStartStatus.Success) return null;
            await Task.Delay(TimeSpan.FromSeconds(1.5), cancel);
            for (var tries = 0; tries < 30; tries++)
            {
                using (var frame = reader.TryAcquireLatestFrame())
                    if (frame?.VideoMediaFrame?.SoftwareBitmap is { } bitmap) return Jpeg(bitmap);
                await Task.Delay(100, cancel);
            }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception e)
        {
            Log.Warn("camera", "could not take a picture from the camera for the grown-ups' page", e);
            return null;
        }
        finally
        {
            if (reader is not null)
            {
                try { await reader.StopAsync(); }
                catch (Exception) { /* closing anyway */ }
                reader.Dispose();
            }
            capture?.Dispose();
        }
    }

    private static byte[] Jpeg(SoftwareBitmap bitmap)
    {
        using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var b = bgra ?? bitmap;
        var (w, h) = (b.PixelWidth, b.PixelHeight);
        var pixels = new byte[w * h * 4];
        b.CopyToBuffer(pixels.AsBuffer());
        using var image = SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, w * 4);
        var outW = Math.Min(Width, w);
        var outH = Math.Max(1, (int)Math.Round(outW * h / (double)w));
        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.DrawImage(image, new SKRect(0, 0, outW, outH), new SKSamplingOptions(SKFilterMode.Linear));
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    /// <summary>Deletes the oldest camera recordings until the folder is under <see cref="MaxFolderBytes"/> with room
    /// for one more play. A play's video and sound share a name and go together.</summary>
    public static void Prune(string folder)
    {
        if (!Directory.Exists(folder)) return;
        var plays = new DirectoryInfo(folder).GetFiles($"{Prefix}-*")
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name))
            .OrderBy(g => g.Min(f => f.LastWriteTimeUtc))
            .ToList();
        var total = plays.Sum(g => g.Sum(f => f.Length));
        foreach (var play in plays)
        {
            if (total + MaxFileBytes <= MaxFolderBytes) break;
            foreach (var f in play)
            {
                try
                {
                    f.Delete();
                    total -= f.Length;
                    Log.Info("camera", $"deleted the old camera recording {f.Name} ({f.Length / 1e6:0} MB) to stay under {MaxFolderBytes / 1e9:0} GB");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Warn("camera", $"could not delete the old camera recording {f.Name}", e); }
            }
        }
    }

    public void Dispose()
    {
        _stopping = true;
        StopMic();
        // Stopping the camera is asynchronous; it is waited for off the UI thread, and never for long.
        try { Task.Run(StopCameraAsync).Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception e) { Log.Warn("camera", "stopping the camera failed", e); }
        _frames.Writer.TryComplete();
        _worker.Join(TimeSpan.FromSeconds(10));
        Interlocked.Exchange(ref _preview, null)?.Dispose();
        _font.Dispose();
        _ink.Dispose();
        _label.Dispose();
    }

    private static MediaFrameSourceInfo? ColorSource(MediaFrameSourceGroup group) =>
        group.SourceInfos.FirstOrDefault(i => i.SourceKind == MediaFrameSourceKind.Color && i.MediaStreamType == MediaStreamType.VideoRecord)
        ?? group.SourceInfos.FirstOrDefault(i => i.SourceKind == MediaFrameSourceKind.Color && i.MediaStreamType == MediaStreamType.VideoPreview);

    /// <summary>How long the camera a grown-up chose is looked for before the first one Windows lists is taken.</summary>
    public static readonly TimeSpan WaitForChosen = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan LookAgainEvery = TimeSpan.FromSeconds(1);

    private static async Task<List<MediaFrameSourceGroup>> ColorGroupsAsync() =>
        [.. (await MediaFrameSourceGroup.FindAllAsync()).Where(g => ColorSource(g) is not null)];

    private int Chosen(List<MediaFrameSourceGroup> groups) =>
        DeviceChoice.Find([.. groups.Select(g => new CaptureDevice(g.Id, g.DisplayName))], _cameraId, _cameraName);

    private async Task StartCameraAsync()
    {
        try
        {
            var groups = await ColorGroupsAsync();
            var chosen = Chosen(groups);
            if (chosen < 0 && (_cameraId ?? _cameraName) is not null)
            {
                // Windows does not always list a camera straight away: on 2026-11-21 the chosen one was missing 4 s
                // after the toy started and there a minute later, and the play was filmed by the other camera, at
                // the floor. It is looked for again for a while before another is taken; nothing waits on this.
                var looking = Stopwatch.StartNew();
                while (chosen < 0 && looking.Elapsed < WaitForChosen && !_stopping)
                {
                    await Task.Delay(LookAgainEvery);
                    groups = await ColorGroupsAsync();
                    chosen = Chosen(groups);
                }
                if (_stopping) return;
                if (chosen >= 0) Log.Info("camera", $"the chosen camera {groups[chosen].DisplayName} was not listed at first; Windows listed it after {looking.Elapsed.TotalSeconds:0.0} s");
                else if (groups.Count > 0)
                    Log.Warn("camera", $"the chosen camera {_cameraName ?? _cameraId} is not plugged in (looked for {looking.Elapsed.TotalSeconds:0} s; Windows lists {string.Join(", ", groups.Select(g => g.DisplayName))}); "
                        + $"filming with the first camera Windows lists, {groups[0].DisplayName}");
            }
            if (groups.Count == 0)
            {
                Log.Warn("camera", "no camera found; play is not filmed");
                return;
            }
            var group = groups[Math.Max(0, chosen)];
            var info = ColorSource(group)!;
            var capture = new MediaCapture();
            var exclusive = true;
            try
            {
                await capture.InitializeAsync(Settings(group, MediaCaptureSharingMode.ExclusiveControl));
            }
            catch (Exception e)
            {
                // Another app has the camera: watching alongside it still works, at whatever size it chose.
                Log.Info("camera", $"{group.DisplayName} is in use elsewhere; sharing it ({e.Message})");
                capture.Dispose();
                capture = new MediaCapture();
                await capture.InitializeAsync(Settings(group, MediaCaptureSharingMode.SharedReadOnly));
                exclusive = false;
            }
            lock (_lock)
            {
                if (_stopping)
                {
                    capture.Dispose();
                    return;
                }
                _capture = capture;
            }

            var source = capture.FrameSources[info.Id];
            if (exclusive && BestFormat(source) is { } format) await source.SetFormatAsync(format);
            var current = source.CurrentFormat;
            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += OnFrame;
            var status = await reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                Log.Warn("camera", $"{group.DisplayName} would not start ({status}); play is not filmed. "
                    + "If it says it is denied, Windows' Settings > Privacy > Camera must let desktop apps use the camera.");
                reader.Dispose();
                return;
            }
            lock (_lock) _reader = reader;
            CameraName = group.DisplayName;
            // The child gets the first few seconds to be seen before the warning can show.
            _view = new ChildInView(_steady.Elapsed.TotalSeconds);
            _reportedAt = _steady.Elapsed.TotalSeconds;
            _filming = true;
            Log.Info("camera", $"filming with {group.DisplayName} at {current.VideoFormat.Width}×{current.VideoFormat.Height}, "
                + $"{Rate(current):0.#} frames a second from the camera, {Fps} kept");
            if (_stopping) await StopCameraAsync();
        }
        catch (UnauthorizedAccessException e)
        {
            Log.Warn("camera", "Windows did not let the toy use the camera; play is not filmed. "
                + "Windows' Settings > Privacy > Camera must let desktop apps use the camera.", e);
        }
        catch (Exception e)
        {
            Log.Warn("camera", "the camera could not be opened; play is not filmed", e);
        }
    }

    private static MediaCaptureInitializationSettings Settings(MediaFrameSourceGroup group, MediaCaptureSharingMode sharing) => new()
    {
        SourceGroup = group,
        SharingMode = sharing,
        MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        StreamingCaptureMode = StreamingCaptureMode.Video,
    };

    private static double Rate(MediaFrameFormat f) => f.FrameRate.Denominator == 0 ? 0 : f.FrameRate.Numerator / (double)f.FrameRate.Denominator;

    /// <summary>The camera's format nearest <see cref="Width"/> wide that gives at least <see cref="Fps"/> frames a
    /// second; the smallest that does so, as nothing larger is kept.</summary>
    private static MediaFrameFormat? BestFormat(MediaFrameSource source) => source.SupportedFormats
        .Where(f => f.VideoFormat.Width >= 320 && Rate(f) >= Fps)
        .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - Width))
        .ThenBy(f => Rate(f))
        .FirstOrDefault();

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // One frame at a time: a frame that arrives while the last is still being made is skipped.
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try { TakeFrame(sender); }
        catch (Exception e) { Log.Error("camera", "taking a frame from the camera failed", e); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private void TakeFrame(MediaFrameReader reader)
    {
        using var frame = reader.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null || _stopping) return;

        // Keeps one frame each 1/Fps s; a gap is filled by repeating this frame, so the video keeps real time.
        var due = (long)(_steady.Elapsed.TotalSeconds * Fps) + 1;
        if (due <= _offered) return;
        var copies = (int)Math.Min(due - _offered + _owed, Fps * 2);
        _offered = due;
        _owed = 0;

        using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? null : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var b = bgra ?? bitmap;
        var (w, h) = (b.PixelWidth, b.PixelHeight);
        var pixels = new byte[w * h * 4];
        b.CopyToBuffer(pixels.AsBuffer());
        using var image = SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), pixels, w * 4);

        var outW = Math.Min(Width, w) / 2 * 2;
        var outH = Math.Max(2, (int)Math.Round(outW * h / (double)w / 2) * 2);
        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.DrawImage(image, new SKRect(0, 0, outW, outH), new SKSamplingOptions(SKFilterMode.Linear));
        LookForFace(surface, outW, outH);
        if (ChildInFrame == false) Interlocked.Exchange(ref _preview, surface.Snapshot())?.Dispose();
        var stamp = _clock().ToString("HH:mm:ss");
        var tw = _font.MeasureText(stamp);
        surface.Canvas.DrawRect(outW - tw - 10, outH - 20, tw + 10, 20, _label);
        surface.Canvas.DrawText(stamp, outW - 5, outH - 6, SKTextAlign.Right, _font, _ink);
        using var snapshot = surface.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Jpeg, 75);
        if (!_frames.Writer.TryWrite((data.ToArray(), outW, outH, copies))) _owed = copies;
    }

    /// <summary>Every <see cref="LookEvery"/> s, asks Windows' built-in face detector (on the CPU, no model to
    /// download) whether there is a face in the frame, and measures how much of the picture where the child was has
    /// changed since the last look, before the time stamp is drawn on it; <see cref="ChildInView"/> decides from both.</summary>
    private void LookForFace(SKSurface surface, int w, int h)
    {
        var now = _steady.Elapsed.TotalSeconds;
        if (_facesFailed || _view is not { } view || now - _lastLook < LookEvery) return;
        _lastLook = now;
        try
        {
            _faces ??= FaceDetector.CreateAsync().AsTask().GetAwaiter().GetResult();
            using var frame = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.ReadPixels(frame.Info, frame.GetPixels(), frame.RowBytes, 0, 0);
            var pixels = frame.Bytes;
            using var bgra = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
            using var gray = SoftwareBitmap.Convert(bgra, BitmapPixelFormat.Gray8);
            var found = _faces.DetectFacesAsync(gray).AsTask().GetAwaiter().GetResult();

            // Brightness from the BGRA copy (the same weights as Windows' grey), as the grey bitmap's rows may be padded.
            var grayPixels = new byte[w * h];
            for (int i = 0, p = 0; i < grayPixels.Length; i++, p += 4)
                grayPixels[i] = (byte)((pixels[p] * 29 + pixels[p + 1] * 150 + pixels[p + 2] * 77) >> 8);
            var cells = MotionGrid.Cells(grayPixels, w, h);
            double? moved = _lastCells is null ? null : MotionGrid.ChangedShare(_lastCells, cells, view.Area);
            _lastCells = cells;
            var dark = MotionGrid.TooDark(cells);
            if (dark != _tooDark) Log.Info("camera", dark ? "the camera's picture is too dark to see anyone in" : "the camera's picture is light enough again");
            _tooDark = dark;
            var largest = found.OrderByDescending(f => f.FaceBox.Width).FirstOrDefault();
            ViewBox? face = largest is null ? null
                : new ViewBox(largest.FaceBox.X / (double)w, largest.FaceBox.Y / (double)h, largest.FaceBox.Width / (double)w, largest.FaceBox.Height / (double)h);
            _lastFaces = found.Count;
            view.Look(now, face, moved);
            _inView = view.InView;
            Report(now, face is not null, moved >= ChildInView.MovingShare);
        }
        catch (Exception e)
        {
            // Without the detector there is no warning; filming goes on.
            _facesFailed = true;
            Log.Warn("camera", "could not look for faces in the camera's picture; the out-of-frame warning is off", e);
        }
    }

    /// <summary>Counts the looks and, every <see cref="ReportEvery"/> s, writes to the log what they found, so a
    /// play can be judged from the log without the video.</summary>
    private void Report(double now, bool face, bool moving)
    {
        _looks++;
        if (face) _faceLooks++;
        else if (moving) _movingLooks++;
        if (now - _reportedAt < ReportEvery) return;
        Log.Debug("camera", $"{_looks} looks in {now - _reportedAt:0} s: a face in {_faceLooks}, no face but movement where the child is in {_movingLooks}; "
            + (_inView ? "the child is in view" : "the child is out of view"));
        _reportedAt = now;
        _looks = _faceLooks = _movingLooks = 0;
    }

    private void Write()
    {
        try
        {
            while (_frames.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                while (_frames.Reader.TryRead(out var item))
                    if (!WriteFrame(item.Jpeg, item.W, item.H, item.Copies)) return;
        }
        catch (Exception e)
        {
            // Nothing may escape this thread: an exception here would end the whole toy.
            Log.Error("camera", "the camera video stopped", e);
        }
        finally
        {
            try { _writer?.Dispose(); }
            catch (Exception e) { Log.Warn("camera", $"could not close {VideoFile}", e); }
            _writer = null;
            Log.Info("camera", $"camera video {VideoFile ?? "(no frames)"} closed");
        }
    }

    /// <returns>False when the recording has had to stop.</returns>
    private bool WriteFrame(byte[] jpeg, int w, int h, int copies)
    {
        try
        {
            if (_writer is null || _writer.Length > MaxFileBytes)
            {
                _writer?.Dispose();
                // A play long enough to pass the file limit carries on in a second file, named by when it started.
                VideoFile = Path.Combine(_folder, _writer is null ? $"{Prefix}-{_stamp}.avi" : $"{Prefix}-{_stamp}-{_clock():HHmmss}.avi");
                _writer = new MjpegAviWriter(VideoFile, w, h, Fps);
                Log.Info("camera", $"filming to {VideoFile} ({w}×{h}, {Fps} frames a second)");
            }
            for (var i = 0; i < copies; i++) _writer.AddFrame(jpeg);
            return true;
        }
        catch (IOException e)
        {
            Log.Error("camera", $"writing {VideoFile} failed; filming stops", e);
            return false;
        }
    }

    private async Task StopCameraAsync()
    {
        _filming = false;
        MediaFrameReader? reader;
        MediaCapture? capture;
        lock (_lock)
        {
            (reader, capture) = (_reader, _capture);
            (_reader, _capture) = (null, null);
        }
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrame;
            try { await reader.StopAsync(); }
            catch (Exception e) { Log.Warn("camera", "the camera's frame reader would not stop", e); }
            reader.Dispose();
        }
        capture?.Dispose();
    }

    private void StartMic()
    {
        try
        {
            SoundFile = Path.Combine(_folder, $"{Prefix}-{_stamp}.wav");
            var format = new WaveFormat(MicSampleRate, 16, 1);
            // -1 is Windows' default recording device, whichever that is now.
            var mics = FindMicrophones();
            var chosen = DeviceChoice.Find(mics, null, _micName);
            if (chosen < 0 && _micName is not null)
                Log.Warn("camera", $"the chosen microphone {_micName} is not plugged in; recording from Windows' default microphone");
            _mic = new WaveIn { DeviceNumber = chosen, WaveFormat = format, BufferMilliseconds = 100 };
            _wav = new WaveFileWriter(SoundFile, format);
            _mic.DataAvailable += OnSound;
            _mic.StartRecording();
            Log.Info("camera", $"recording sound from {(chosen < 0 ? "Windows' default microphone" : mics[chosen].Name)} to {SoundFile}");
        }
        catch (Exception e)
        {
            Log.Warn("camera", "the microphone could not be opened; play is filmed without sound", e);
            StopMic();
            SoundFile = null;
        }
    }

    private void OnSound(object? sender, WaveInEventArgs e)
    {
        try
        {
            lock (_lock) _wav?.Write(e.BufferSpan[..e.BytesRecorded]);
        }
        catch (Exception ex)
        {
            Log.Error("camera", "writing the microphone's sound failed; sound recording stops", ex);
            lock (_lock)
            {
                _wav?.Dispose();
                _wav = null;
            }
        }
    }

    private void StopMic()
    {
        try
        {
            if (_mic is not null)
            {
                _mic.DataAvailable -= OnSound;
                _mic.StopRecording();
                _mic.Dispose();
            }
        }
        catch (Exception e) { Log.Warn("camera", "the microphone would not close", e); }
        _mic = null;
        lock (_lock)
        {
            try { _wav?.Dispose(); }
            catch (Exception e) { Log.Warn("camera", $"could not close {SoundFile}", e); }
            _wav = null;
        }
    }
}
