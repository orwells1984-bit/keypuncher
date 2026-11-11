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
        // started the toy. The look at Ollama began with the host and has usually answered by now; play does not
        // wait long for it.
        var missing = _host.MissingModelsAtStart(TimeSpan.FromSeconds(3));
        Page first = missing.Count > 0 ? new ModelsPage(_host, missing) : new PlayPage(_host);
        var window = new Window(first) { Title = "Keypuncher" };
        window.Destroying += (_, _) => _host.Dispose();
        Log.Info("app", "window created");
        return window;
    }

    public static void Show(Page page) => Current!.Windows[0].Page = page;
}
