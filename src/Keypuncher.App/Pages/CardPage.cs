using Keypuncher.Core;

namespace Keypuncher.App.Pages;

/// <summary>
/// Shown instead of play when the toy starts and the graphics card or Ollama is taken by another job: says what
/// holds it, looks again every few seconds, and lets the grown-up play anyway. For the grown-up who started the
/// toy; keys work normally here. Before this page such a play looked like any other, with one new thing in six
/// minutes, and only the log said why.
/// </summary>
public sealed class CardPage : ContentPage
{
    private static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(5);

    private readonly ToyHost _host;
    private readonly Label _title = new() { FontSize = 32, FontAttributes = FontAttributes.Bold };
    private readonly Label _trouble = new() { FontSize = 16, FontAttributes = FontAttributes.Bold };
    private readonly Label _advice = new() { FontSize = 16 };
    private readonly Label _status = new() { FontSize = 14 };
    private readonly Button _play, _again;
    private readonly IDispatcherTimer _timer;
    private bool _looking;

    public CardPage(ToyHost host, string trouble)
    {
        _host = host;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        var layout = new VerticalStackLayout { Padding = 40, Spacing = 16, MaximumWidthRequest = 900, VerticalOptions = LayoutOptions.Center };
        layout.Add(_title);
        layout.Add(_trouble);
        layout.Add(_advice);
        _play = Button("Play anyway", () => Leave(new PlayPage(_host)));
        _again = Button("Look again", Look);
        var quit = Button("Quit Keypuncher", () => Application.Current!.Quit());
        quit.BackgroundColor = Color.FromArgb("#C62828");
        layout.Add(new HorizontalStackLayout { Spacing = 12, Children = { _play, _again, Button("Grown-ups' page", () => Leave(new ParentPage(_host))), quit } });
        layout.Add(_status);
        Content = new ScrollView { Content = layout };
        Show(trouble);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = LookEvery;
        _timer.Tick += (_, _) => Look();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Not play: the keyboard is left alone and the screen may sleep.
        _host.Mode = HostMode.Parent;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private void Leave(Page page)
    {
        _timer.Stop();
        ToyApp.Show(page);
    }

    /// <summary>The page for this trouble, or for none: the card has come free while the grown-up was reading.</summary>
    private void Show(string? trouble)
    {
        if (trouble is null)
        {
            _title.Text = "The graphics card is free now";
            _trouble.Text = "Nothing else is holding the card or Ollama any more.";
            _advice.Text = "The toy's model can load. If the other job starts again during play, new things will come slowly again.";
            _play.Text = "Play";
            _play.BackgroundColor = Color.FromArgb("#2E7D32");
            return;
        }
        _title.Text = "The graphics card is busy";
        _trouble.Text = trouble;
        _advice.Text = "The toy still plays with the things it already has. For new ones, stop the other job first "
            + "(\"ollama ps\" in a terminal shows what Ollama is running), then play. This page looks again every few seconds.";
        _play.Text = "Play anyway";
        _play.BackgroundColor = Color.FromArgb("#3B5BA5");
    }

    private async void Look()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        if (_looking) return;
        _looking = true;
        try
        {
            _again.IsEnabled = false;
            var state = await _host.CheckCardAsync();
            Show(_host.CardTrouble(state));
            _status.Text = $"Looked at {DateTime.Now:HH:mm:ss}"
                + (state.FreeMiB is { } free ? $": {free:N0} MiB of the card free" : "")
                + (state.OllamaReachable ? $"; Ollama has loaded {(state.Loaded.Count == 0 ? "nothing" : string.Join(", ", state.Loaded.Select(m => m.Name)))}." : "; Ollama did not answer.");
        }
        catch (Exception e)
        {
            Log.Error("gpu", "looking at the card again failed", e);
            _status.Text = $"Could not look: {e.GetBaseException().Message}";
        }
        finally
        {
            _again.IsEnabled = true;
            _looking = false;
        }
    }

    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, Padding = new Thickness(20, 10) };
        b.Clicked += (_, _) => action();
        return b;
    }
}
