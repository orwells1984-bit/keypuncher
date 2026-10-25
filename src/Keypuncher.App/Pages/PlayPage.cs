using System.Diagnostics;
using Keypuncher.Rendering;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Keypuncher.App.Pages;

/// <summary>The toy: one full-screen canvas. Keys come from the hook; taps and clicks from the canvas.</summary>
public sealed class PlayPage : ContentPage
{
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(2);

    private readonly ToyHost _host;
    private readonly SKCanvasView _canvas;
    private readonly SceneRenderer _renderer = new(Fonts.LoadAndika());
    private readonly IDispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<long> _fingers = [];
    private TimeSpan _last, _lastSave;
    private int _width = 1, _height = 1;
    private int _frames;
    private TimeSpan _fpsStart;
    private SessionRecorder? _recorder;

    public PlayPage(ToyHost host)
    {
        _host = host;
        BackgroundColor = Colors.LightBlue;
        // Draw at layout size rather than device pixels: on a 4K screen at 200% that is a quarter of the
        // pixels (17 frames a second at full resolution on the desktop), and the soft shapes don't need more.
        _canvas = new SKCanvasView { EnableTouchEvents = true, IgnorePixelScaling = true };
        _canvas.PaintSurface += OnPaint;
        _canvas.Touch += OnTouch;
        Content = _canvas;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(15);
        _timer.Tick += (_, _) => OnFrame();

        _host.Engine.GrownUpRequested += OnGrownUp;
    }

    /// <summary>Frames per second over the last few seconds, for the parent screen.</summary>
    public static double FramesPerSecond { get; private set; }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _host.Mode = HostMode.Play;
        _host.Engine.StartSession();
        if (_host.Engine.Settings.DirectorTest)
        {
            _recorder = new SessionRecorder(Path.Combine(_host.DataFolder, ToyHost.TestVideosName));
            _host.WriteDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  test mode: recording the screen to {Path.Combine(_host.DataFolder, ToyHost.TestVideosName)}");
        }
        _last = _clock.Elapsed;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        _host.SaveWorld();
        StopRecording();
        base.OnDisappearing();
    }

    private void OnFrame()
    {
        var now = _clock.Elapsed;
        var dt = Math.Min((now - _last).TotalSeconds, 0.1);
        _last = now;

        foreach (var key in _host.DrainKeys()) _host.Engine.OnKey(key);
        _host.Engine.Update(dt);
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
    }

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        (_width, _height) = (e.Info.Width, e.Info.Height);
        var gate = _host.Engine.Gate;
        var overlay = gate.Challenge is { } word
            ? $"Grown-up? Type: {word}   ({gate.SecondsLeft(_host.World.Time):0})"
            : _host.AudioError is { } err ? $"No sound: {err}"
            : _host.Engine.Settings.DirectorTest ? TestOverlay()
            : null;
        _renderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height, _host.World, overlay);
        _recorder?.Offer(e.Surface, e.Info.Width, e.Info.Height, _clock.Elapsed.TotalSeconds);
    }

    private void StopRecording()
    {
        if (_recorder is null) return;
        var file = _recorder.CurrentFile;
        _recorder.Dispose();
        _recorder = null;
        if (file is not null) _host.WriteDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  test mode: video saved as {file}");
    }

    /// <summary>In the director's test mode: the scene and the latest pick, at the top for the grown-up.</summary>
    private string TestOverlay()
    {
        var latest = _host.Director.Log.FirstOrDefault()?.Lines().LastOrDefault(l => !l.Contains("turned down"));
        return $"Test mode{(_host.Director.Busy ? ", asking the model" : "")}.\nScene: {_host.World.Scene}.{(latest is null ? "" : "\n" + latest)}";
    }

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _fingers.Add(e.Id);
                _host.Engine.OnTouch(e.Location.X / _width, e.Location.Y / _height, _fingers.Count);
                break;
            case SKTouchAction.Released or SKTouchAction.Cancelled or SKTouchAction.Exited:
                _fingers.Remove(e.Id);
                break;
        }
        e.Handled = true;
    }

    private void OnGrownUp()
    {
        _host.Engine.GrownUpRequested -= OnGrownUp;
        _timer.Stop();
        StopRecording();
        ToyApp.Show(new ParentPage(_host));
    }
}
