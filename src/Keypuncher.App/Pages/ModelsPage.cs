using Keypuncher.Core;
using Keypuncher.Inference.Ollama;

namespace Keypuncher.App.Pages;

/// <summary>
/// Shown instead of play when the toy starts and Ollama lacks a model it wants: says which, and offers to download
/// it. For the grown-up who started the toy; keys work normally here. Before this page a play without its models
/// looked like any other, and only the log said why nothing new ever appeared.
/// </summary>
public sealed class ModelsPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly IReadOnlyList<string> _missing;
    private readonly Label _status = new() { FontSize = 16 };
    private readonly Button _download, _play;
    private CancellationTokenSource? _downloading;

    public ModelsPage(ToyHost host, IReadOnlyList<string> missing)
    {
        _host = host;
        _missing = missing;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        var layout = new VerticalStackLayout { Padding = 40, Spacing = 16, MaximumWidthRequest = 900, VerticalOptions = LayoutOptions.Center };
        layout.Add(new Label { Text = missing.Count == 1 ? "A model is missing" : "Models are missing", FontSize = 32, FontAttributes = FontAttributes.Bold });
        layout.Add(new Label
        {
            Text = $"Keypuncher uses models that run on this computer through Ollama, and Ollama at {host.OllamaAddress} does not have "
                 + (missing.Count == 1 ? "this one:" : "these:"),
            FontSize = 16,
        });
        foreach (var model in missing)
            layout.Add(new Label { Text = "    " + Describe(host, model), FontSize = 16, FontAttributes = FontAttributes.Bold });
        layout.Add(new Label
        {
            Text = "Without them the toy still plays, with the things it already has: no new real things or places appear, and new pictures are not checked. "
                 + "The download runs through Ollama and can be stopped and picked up again later. Another model can be chosen on the grown-ups' page.",
            FontSize = 16,
        });

        _download = Button(missing.Count == 1 ? "Download it now" : "Download them now", Download);
        _download.BackgroundColor = Color.FromArgb("#2E7D32");
        _play = Button("Play without", Play);
        var quit = Button("Quit Keypuncher", () => Application.Current!.Quit());
        quit.BackgroundColor = Color.FromArgb("#C62828");
        layout.Add(new HorizontalStackLayout { Spacing = 12, Children = { _download, _play, Button("Grown-ups' page", () => Leave(new ParentPage(_host))), quit } });
        layout.Add(_status);
        Content = new ScrollView { Content = layout };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Not play: the keyboard is left alone and the screen may sleep.
        _host.Mode = HostMode.Parent;
    }

    /// <summary>What a missing model is for, and how big it is when the toy knows.</summary>
    public static string Describe(ToyHost host, string model)
    {
        var use = OllamaModels.Same(model, host.DirectorModel) ? "chooses the real things and places"
            : OllamaModels.Same(model, host.PictureCheckModel) ? "checks new pictures" : "used by the toy";
        return $"{model}: {use}{(ToyHost.DownloadSize(model) is { } size ? $", {size}" : "")}";
    }

    private void Play() => Leave(new PlayPage(_host));

    /// <summary>Goes to another page; a download under way is stopped first (Ollama keeps what it has so far).</summary>
    private void Leave(Page page)
    {
        _downloading?.Cancel();
        ToyApp.Show(page);
    }

    private async void Download()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        var stop = _downloading = new CancellationTokenSource();
        try
        {
            _download.IsEnabled = false;
            _play.Text = "Stop and play without";
            var progress = new Progress<PullProgress>(p => _status.Text = ProgressLine(p));
            foreach (var model in _missing)
            {
                _status.Text = $"Asking Ollama for {model}...";
                await _host.DownloadModelAsync(model, progress, stop.Token);
            }
            if (stop.IsCancellationRequested) return;
            _status.Text = "Done.";
            ToyApp.Show(new PlayPage(_host));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The grown-up left the page; nothing to say.
        }
        catch (Exception e)
        {
            Log.Error("ollama", "downloading the missing models did not finish", e);
            _status.Text = $"The download did not finish: {e.GetBaseException().Message}";
            _download.IsEnabled = true;
            _download.Text = "Try again";
            _play.Text = "Play without";
        }
        finally
        {
            if (ReferenceEquals(_downloading, stop)) _downloading = null;
            stop.Dispose();
        }
    }

    /// <summary>One line for a download's progress: megabytes while files come, Ollama's own word otherwise.</summary>
    public static string ProgressLine(PullProgress p) =>
        p.Total > 0 && p.Completed < p.Total
            ? $"Downloading {p.Model}: {p.Completed / 1_000_000:N0} of {p.Total / 1_000_000:N0} MB ({100.0 * p.Completed / p.Total:0}%)"
            : $"Downloading {p.Model}: {p.Status}";

    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, Padding = new Thickness(20, 10) };
        b.Clicked += (_, _) => action();
        return b;
    }
}
