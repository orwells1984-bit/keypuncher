using System.Runtime.InteropServices;
using Keypuncher.Core;
using Microsoft.UI.Windowing;

namespace Keypuncher.App.Pages;

/// <summary>
/// Shown after Quit while new pictures are still being checked (and things with no photo drawn): the toy's window
/// shrinks to a corner of the screen, says what it is doing, and closes the toy when the work is done. The keyboard
/// and Win+L are already back to normal. "Close now" stops at once; what was not finished waits for the next time.
/// Before this page Quit waited 15 s and stopped the check, so most new pictures were never looked at.
/// </summary>
public sealed class FinishingPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly Label _status = new() { FontSize = 14 };
    private readonly IDispatcherTimer _timer;
    private readonly DateTime _since = DateTime.Now;
    private Task? _work;
    private bool _closing;

    public FinishingPage(ToyHost host)
    {
        _host = host;
        BackgroundColor = Color.FromArgb("#FFF8EC");
        var close = new Button { Text = "Close now", Padding = new Thickness(20, 8), HorizontalOptions = LayoutOptions.Start };
        close.Clicked += (_, _) => Close("a grown-up chose Close now; what was not finished waits for next time");
        Content = new VerticalStackLayout
        {
            Padding = 20,
            Spacing = 10,
            Children =
            {
                new Label { Text = "Finishing the pictures", FontSize = 22, FontAttributes = FontAttributes.Bold },
                new Label { Text = "The new pictures are being checked so they are ready for next time. The keyboard is back to normal. This window closes by itself.", FontSize = 14 },
                _status,
                close,
            },
        };
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Look();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_work is not null) return;
        try
        {
            _work = _host.FinishPicturesAfterQuit();
            ShrinkWindow();
        }
        catch (Exception e)
        {
            Log.Error("app", "carrying on with the pictures after Quit failed; quitting", e);
            Close("the pictures could not be finished");
            return;
        }
        Look();
        _timer.Start();
    }

    private void Look()
    {
        if (_work is null or { IsCompleted: true })
        {
            Close(_host.PictureCheckRunning ? $"the pictures were not finished in {ToyHost.MaxFinishing.TotalMinutes:0} min; the rest wait for next time" : "the pictures are finished");
            return;
        }
        var work = _host.PictureWork;
        var waited = DateTime.Now - _since;
        _status.Text = (work.Length > 0 ? work : "Waiting for the picture checker") + $" · {(int)waited.TotalMinutes}:{waited.Seconds:00}";
    }

    private void Close(string why)
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();
        Log.Info("app", $"closing after Quit: {why}");
        Application.Current!.Quit();
    }

    /// <summary>Takes the window out of full screen and puts it, small, in the bottom right corner of its screen.
    /// If Windows refuses, the page stays full screen and works the same.</summary>
    private static void ShrinkWindow()
    {
        try
        {
            var handle = ToyHost.WindowHandle;
            if (handle == IntPtr.Zero) return;
            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
            var window = AppWindow.GetFromWindowId(id);
            window.SetPresenter(AppWindowPresenterKind.Overlapped);
            var scale = GetDpiForWindow(handle) is var dpi and > 0 ? dpi / 96.0 : 1.0;
            var area = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Nearest).WorkArea;
            int width = (int)(560 * scale), height = (int)(300 * scale), margin = (int)(24 * scale);
            window.MoveAndResize(new Windows.Graphics.RectInt32(area.X + area.Width - width - margin, area.Y + area.Height - height - margin, width, height));
        }
        catch (Exception e)
        {
            Log.Warn("app", "the window could not be made small after Quit; it stays as it is", e);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
