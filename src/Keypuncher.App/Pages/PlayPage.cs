using System.Diagnostics;
using Keypuncher.Core;
using Keypuncher.Platform.Windows;
using Keypuncher.Rendering;
using Keypuncher.Storage;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Keypuncher.App.Pages;

/// <summary>The toy: one full-screen canvas. Keys come from the hook; taps and clicks from the canvas.</summary>
public sealed class PlayPage : ContentPage
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DrawLogEvery = TimeSpan.FromMinutes(1);

    private readonly ToyHost _host;
    private readonly SKCanvasView _canvas;
    private readonly SceneRenderer _renderer = new(Fonts.LoadAndika());
    private readonly IDispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<long> _fingers = [];
    private TimeSpan _last, _lastSave, _lastDrawLog;
    private int _width = 1, _height = 1;
    private int _frames;
    private TimeSpan _fpsStart;
    private SessionRecorder? _recorder;
    private CameraRecorder? _camera;
    private PlayTimeline? _timeline;
    private SKImage? _cameraPicture;
    private bool? _childInFrame;
    private readonly SKPaint _cameraFrame = new() { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
    private readonly SKPaint _cameraLabelBack = new() { Color = SKColors.White.WithAlpha(220) };
    private readonly SKPaint _cameraLabelInk = new() { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    private readonly SKFont _cameraFont = new(Fonts.LoadAndika(), 20);
    private readonly Stopwatch _session = new();
    private readonly HashSet<int> _held = [];
    private int _keys, _taps;
    private bool _wasAsleep;

    public PlayPage(ToyHost host)
    {
        _host = host;
        _renderer.Pictures = host.Pictures;
        BackgroundColor = Colors.LightBlue;
        // Draw at layout size rather than device pixels: on a 4K screen at 200% that is a quarter of the
        // pixels (17 frames a second at full resolution on the desktop), and the soft shapes don't need more.
        _canvas = new SKCanvasView { EnableTouchEvents = true, IgnorePixelScaling = true };
        _canvas.PaintSurface += OnPaint;
        _canvas.Touch += OnTouch;
        Content = _canvas;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(15);
        _timer.Tick += (_, _) => SafeFrame();

        _host.Engine.GrownUpRequested += OnGrownUp;
        _host.Engine.RestyledShown += (slot, recipe) =>
        {
            // Once per thing per play, so the log says what the child actually saw of the model's choices.
            if (_shown.Add((slot, recipe.Name))) Story($"shown: {recipe.Name} (key family {slot})");
        };
    }

    private readonly HashSet<(int, string)> _shown = [];

    /// <summary>Frames per second over the last few seconds, for the parent screen.</summary>
    public static double FramesPerSecond { get; private set; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _host.Mode = HostMode.Play;
        // The card is the director's during play; a check begun on the grown-ups' page carries on after the goodnight.
        _host.StopPictureCheckForPlay();
        _host.Engine.StartSession();
        var s = _host.Engine.Settings;
        (_keys, _taps, _wasAsleep) = (0, 0, false);
        _shown.Clear();
        _session.Restart();
        if (s.RecordPlay || s.DirectorTest)
            _recorder = new SessionRecorder(Path.Combine(_host.DataFolder, ToyHost.VideosName), s.DirectorTest ? "test" : "play");
        StartTimeline(s);
        Story($"play started: calm {s.Calm}, language {_host.Engine.SessionLanguage}, model changes {(s.Director ? "on" : "off")}"
            + $"{(s.DirectorTest ? ", test mode" : "")}, goodnight {(s.SessionLength is { } l ? $"after {l.TotalMinutes:0.#} min" : "never")}"
            + $"{(_recorder is null ? ", not recorded" : $", recording to {Path.Combine(_host.DataFolder, ToyHost.VideosName)}")}");
        _last = _lastDrawLog = _clock.Elapsed;
        _renderer.Timings.Reset();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        _host.SaveWorld();
        StopRecording();
        base.OnDisappearing();
    }

    /// <summary>One frame, with anything it throws logged (once every half minute at most, for the same error) and
    /// play carrying on: a fault in one frame must not end the toy under the child.</summary>
    private void SafeFrame()
    {
        try { OnFrame(); }
        catch (Exception e) { Log.Error("play", "a frame failed; play carries on", e); }
    }

    private void OnFrame()
    {
        var now = _clock.Elapsed;
        var elapsed = (now - _last).TotalSeconds;
        _last = now;

        foreach (var key in _host.DrainKeys())
        {
            // A held key repeats its down; only the first counts.
            if (!key.IsDown) _held.Remove(key.ScanCode);
            else if (_held.Add(key.ScanCode))
            {
                _keys++;
                _timeline?.Add("key", ("code", key.ScanCode), ("char", key.Character));
            }
            _host.Engine.OnKey(key);
        }
        _host.Engine.Advance(elapsed);
        if (_host.Engine.IsAsleep && !_wasAsleep)
        {
            Story($"goodnight after {Minutes(_session.Elapsed)}: {_keys} key presses, {_taps} taps");
            _timeline?.Add("goodnight", ("keys", _keys), ("taps", _taps));
            // The director has stopped choosing, so the card is free for the vision model.
            _host.CheckNewPictures();
        }
        _wasAsleep = _host.Engine.IsAsleep;
        WatchCamera();
        var settings = _host.Engine.Settings;
        _host.Director.Update(_host.World, settings.Limits, settings.Director && !_host.Engine.IsAsleep);
        _canvas.InvalidateSurface();

        if (now - _lastSave > SaveEvery)
        {
            _lastSave = now;
            _host.SaveWorld();
        }

        _frames++;
        if (now - _fpsStart > TimeSpan.FromSeconds(3))
        {
            FramesPerSecond = _frames / (now - _fpsStart).TotalSeconds;
            _frames = 0;
            _fpsStart = now;
        }
        // How smoothly it draws, in the log once a minute, so a slow run can be told from the log alone.
        if (now - _lastDrawLog > DrawLogEvery)
        {
            _lastDrawLog = now;
            Story($"drawing {FramesPerSecond:0} frames a second; {_renderer.Timings}; {_host.World.Live.Count} on screen");
            _renderer.Timings.Reset();
        }
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        try { DrawFrame(e); }
        catch (Exception ex) { Log.Error("drawing", "drawing a frame failed; play carries on", ex); }
    }

    private void DrawFrame(SKPaintSurfaceEventArgs e)
    {
        (_width, _height) = (e.Info.Width, e.Info.Height);
        var gate = _host.Engine.Gate;
        var touchGate = _host.Engine.TouchGate;
        var now = _host.World.Time;
        var overlay = gate.Challenge is { } word
            ? $"Grown-up? Type: {word}   ({gate.SecondsLeft(now):0})"
            : touchGate.Challenge is { } shapes
            ? $"Grown-up? Tap in this order: {string.Join(", ", shapes.Select(s => s.ToString().ToLowerInvariant()))}   ({touchGate.SecondsLeft(now):0})"
            : _host.AudioError is { } err ? $"No sound: {err}"
            : _host.Engine.Settings.DirectorTest ? TestOverlay()
            : null;
        // Both holds fill the same ring; whichever is going shows.
        var escCue = gate.EscCue(DateTimeOffset.Now);
        var touchCue = touchGate.HoldCue(now);
        _renderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height, _host.World, overlay,
            touchCue.Opacity > escCue.Opacity ? touchCue : escCue, touchGate.Targets);

        DrawCameraPicture(e.Surface.Canvas, e.Info.Width, e.Info.Height);

        // Once the goodnight has faded to dark there is nothing more to see.
        if (_host.World.Night < 1) _recorder?.Offer(e.Surface, e.Info.Width, e.Info.Height, _clock.Elapsed.TotalSeconds);
    }

    /// <summary>Ends the session's record: its length and presses in the log, and the videos and timeline closed.</summary>
    private void StopRecording()
    {
        if (!_session.IsRunning) return;
        _session.Stop();
        var file = _recorder?.CurrentFile;
        _recorder?.Dispose();
        _recorder = null;
        var camera = _camera?.VideoFile;
        _camera?.Dispose();
        _camera = null;
        _cameraPicture?.Dispose();
        _cameraPicture = null;
        _childInFrame = null;
        Story($"play ended after {Minutes(_session.Elapsed)}: {_keys} key presses, {_taps} taps{(file is null ? "" : $"; video saved as {file}")}"
            + $"{(camera is null ? "" : $"; camera video saved as {camera}")}");
        StopTimeline();
    }

    /// <summary>Starts the play's timeline, and the camera when it is wanted, all named by the same start time.</summary>
    private void StartTimeline(ToySettings s)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        try
        {
            _timeline = new PlayTimeline(Path.Combine(_host.DataFolder, ToyHost.VideosName, $"{(s.DirectorTest ? "test" : "play")}-{stamp}.timeline.jsonl"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("recording", "could not start the play timeline", e);
        }
        if (s.RecordCamera)
        {
            try { _camera = new CameraRecorder(Path.Combine(_host.DataFolder, ToyHost.CameraName), stamp, s.Camera, s.CameraName, s.Microphone); }
            catch (Exception e) { Log.Warn("camera", "could not start filming; play goes on", e); }
        }
        _timeline?.Add("start", ("calm", s.Calm.ToString()), ("language", _host.Engine.SessionLanguage), ("director", s.Director),
            ("test", s.DirectorTest), ("screen", _recorder is not null), ("camera", _camera is not null),
            ("scene", _host.World.Scene.Landscape.ToString()), ("time", _host.World.Scene.TimeOfDay.ToString()));
        _host.World.Added += OnAdded;
        _host.World.SceneChanged += OnSceneChanged;
        _host.Mixer.SpeechStarted += OnSpeech;
        _host.Engine.AnimalCalled += OnCall;
        _host.Engine.Popped += OnPop;
        _host.Director.Picked += OnPicked;
    }

    private void StopTimeline()
    {
        _host.World.Added -= OnAdded;
        _host.World.SceneChanged -= OnSceneChanged;
        _host.Mixer.SpeechStarted -= OnSpeech;
        _host.Engine.AnimalCalled -= OnCall;
        _host.Engine.Popped -= OnPop;
        _host.Director.Picked -= OnPicked;
        _timeline?.Add("end", ("keys", _keys), ("taps", _taps), ("minutes", _session.Elapsed.TotalMinutes));
        _timeline?.Dispose();
        _timeline = null;
    }

    private void OnAdded(Entity e) => _timeline?.Add("shown", ("role", e.Role.ToString()), ("name", e.Recipe?.Name), ("text", e.Text),
        ("kind", e.Recipe?.Kind.ToString()), ("category", e.Recipe is { } r ? ToyHost.CategoryOf(r).ToString() : null),
        ("shape", e.Recipe?.Body.Shape.ToString()), ("x", e.X), ("y", e.Y));

    private void OnSceneChanged(Scene scene) => _timeline?.Add("scene", ("landscape", scene.Landscape.ToString()),
        ("time", scene.TimeOfDay.ToString()), ("weather", scene.Weather.ToString()), ("place", scene.Place));

    private void OnSpeech(double seconds) => _timeline?.Add("speech", ("seconds", seconds));
    private void OnCall(Core.Shape shape) => _timeline?.Add("call", ("shape", shape.ToString()));
    private void OnPop(double x) => _timeline?.Add("pop", ("x", x));
    private void OnPicked(Recipe recipe, RealCategory category) => _timeline?.Add("pick", ("name", recipe.Name), ("category", category.ToString()));

    /// <summary>Once a frame: notes in the timeline and the log when the child leaves or comes back into the camera's
    /// view, and takes the newest camera picture to show while the child is out of it.</summary>
    private void WatchCamera()
    {
        var inFrame = _camera?.ChildInFrame;
        if (inFrame != _childInFrame)
        {
            if (inFrame is { } seen)
            {
                _timeline?.Add(seen ? "inframe" : "outframe", ("faces", _camera?.FacesAtLastLook));
                Log.Info("camera", seen ? "the camera can see the child" : "the camera cannot see the child; showing the camera picture");
            }
            _childInFrame = inFrame;
        }
        if (inFrame == false)
        {
            if (_camera?.TakePreview() is { } picture)
            {
                _cameraPicture?.Dispose();
                _cameraPicture = picture;
            }
        }
        else if (_cameraPicture is not null)
        {
            _cameraPicture.Dispose();
            _cameraPicture = null;
        }
    }

    /// <summary>While the camera cannot see the child: what it sees, small in the bottom-right corner, with a line
    /// for the grown-up above it.</summary>
    private void DrawCameraPicture(SKCanvas canvas, int width, int height)
    {
        if (_cameraPicture is not { } picture) return;
        var w = width * 0.22f;
        var h = w * picture.Height / picture.Width;
        var rect = SKRect.Create(width - w - 24, height - h - 24, w, h);
        canvas.DrawImage(picture, rect, new SKSamplingOptions(SKFilterMode.Linear));
        canvas.DrawRect(rect, _cameraFrame);
        const string text = "The camera can't see the child";
        var tw = _cameraFont.MeasureText(text);
        canvas.DrawRect(rect.Right - tw - 16, rect.Top - 34, tw + 16, 30, _cameraLabelBack);
        canvas.DrawText(text, rect.Right - 8, rect.Top - 12, SKTextAlign.Right, _cameraFont, _cameraLabelInk);
    }

    /// <summary>A line in the director's log (and the app's log).</summary>
    private void Story(string line) => _host.Story(line);

    private static string Minutes(TimeSpan t) => t.TotalMinutes < 1 ? $"{t.TotalSeconds:0} s" : $"{t.TotalMinutes:0.#} min";

    /// <summary>In the director's test mode: the scene and the latest pick, at the top for the grown-up.</summary>
    private string TestOverlay()
    {
        var latest = _host.Director.Log.FirstOrDefault()?.Lines().LastOrDefault(l => !l.Contains("turned down"));
        return $"Test mode{(_host.Director.Busy ? ", asking the model" : "")}.\nScene: {_host.World.Scene}.{(latest is null ? "" : "\n" + latest)}";
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        try { HandleTouch(e); }
        catch (Exception ex) { Log.Error("play", $"handling a touch ({e.ActionType}) failed", ex); }
        e.Handled = true;
    }

    private void HandleTouch(SKTouchEventArgs e)
    {
        var (x, y, aspect) = (e.Location.X / _width, e.Location.Y / _height, (double)_width / _height);
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _fingers.Add(e.Id);
                _taps++;
                _timeline?.Add("tap", ("x", x), ("y", y), ("fingers", _fingers.Count));
                _host.Engine.OnFingerDown(e.Id, x, y, aspect, _fingers.Count);
                break;
            case SKTouchAction.Moved:
                _host.Engine.OnFingerMove(e.Id, x, y, aspect);
                break;
            case SKTouchAction.Released or SKTouchAction.Cancelled or SKTouchAction.Exited:
                _fingers.Remove(e.Id);
                _host.Engine.OnFingerUp(e.Id);
                break;
        }
    }

    private void OnGrownUp()
    {
        Story("a grown-up passed the gate; showing the grown-ups' page");
        _host.Engine.GrownUpRequested -= OnGrownUp;
        _timer.Stop();
        StopRecording();
        // This page is never shown again: the grown-ups' page makes a new one.
        _canvas.PaintSurface -= OnPaint;
        _renderer.Dispose();
        _cameraFont.Dispose();
        _cameraFrame.Dispose();
        _cameraLabelBack.Dispose();
        _cameraLabelInk.Dispose();
        ToyApp.Show(new ParentPage(_host));
    }
}
