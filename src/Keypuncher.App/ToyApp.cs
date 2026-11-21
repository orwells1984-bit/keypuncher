using Keypuncher.App.Pages;
using Keypuncher.Core;

namespace Keypuncher.App;

/// <summary>One window, and pages swapped in place rather than navigated, so there is no back button
/// or navigation bar for the child to reach.</summary>
public sealed class ToyApp : Application
{
    private readonly ToyHost _host;

    public ToyApp()
    {
        try { _host = new ToyHost(); }
        catch (Exception e)
        {
            // Without its host the toy cannot run at all; a window with nothing behind it would be worse than closing.
            Log.Fatal("app", "the toy could not start", e);
            Log.Flush();
            Environment.Exit(1);
            throw;
        }
        // The grown-up pages are drawn on a light background, so they must not follow Windows' dark mode
        // (white text on cream was unreadable). Explicit colours as well, in case a control ignores the theme.
        UserAppTheme = AppTheme.Light;
        var ink = Color.FromArgb("#222222");
        Resources.Add(new Style(typeof(Label)) { Setters = { new Setter { Property = Label.TextColorProperty, Value = ink } } });
        Resources.Add(new Style(typeof(Button))
        {
            Setters =
            {
                new Setter { Property = Button.TextColorProperty, Value = Colors.White },
                new Setter { Property = Button.BackgroundColorProperty, Value = Color.FromArgb("#3B5BA5") },
            },
        });
        Resources.Add(new Style(typeof(Picker)) { Setters = { new Setter { Property = Picker.TextColorProperty, Value = ink } } });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // A model the toy wants and Ollama does not have is offered as a download before play, to the grown-up who
        // started the toy; a graphics card or an Ollama that another job is holding is warned about the same way.
        // Both looks began with the host and have usually answered by now; play does not wait long for them.
        var waiting = System.Diagnostics.Stopwatch.StartNew();
        var patience = TimeSpan.FromSeconds(3);
        var missing = _host.MissingModelsAtStart(patience);
        var left = patience - waiting.Elapsed;
        var trouble = missing.Count > 0 ? null : _host.CardTroubleAtStart(left > TimeSpan.Zero ? left : TimeSpan.Zero);
        Page first = missing.Count > 0 ? new ModelsPage(_host, missing)
            : trouble is not null ? new CardPage(_host, trouble)
            : new PlayPage(_host);
        var window = new Window(first) { Title = "Keypuncher" };
        window.Destroying += (_, _) =>
        {
            _host.Dispose();
            LeaveSoon();
        };
        Log.Info("app", "window created");
        return window;
    }

    /// <summary>How long the process may linger once everything is saved and closed.</summary>
    public static readonly TimeSpan MaxLinger = TimeSpan.FromSeconds(3);

    /// <summary>Ends the process if it has not ended by itself shortly after the window closed. On 2026-11-21 the
    /// toy stayed for 30 s after everything was saved, until something of the framework's gave up waiting. By now
    /// the world is saved, Win+L is back and the log is written, so nothing is lost by leaving.</summary>
    private static void LeaveSoon() =>
        new Thread(() =>
        {
            Thread.Sleep(MaxLinger);
            Log.Warn("app", $"still running {MaxLinger.TotalSeconds:0} s after the window closed; ending the process");
            Log.Flush();
            Environment.Exit(0);
        }) { IsBackground = true, Name = "Leave soon" }.Start();

    public static void Show(Page page) => Current!.Windows[0].Page = page;
}
