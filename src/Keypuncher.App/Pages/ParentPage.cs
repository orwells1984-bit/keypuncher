using Keypuncher.Audio;
using Keypuncher.Language;
using Keypuncher.Platform.Windows;
using Keypuncher.Storage;

namespace Keypuncher.App.Pages;

/// <summary>The grown-ups' screen, reached only through the exit challenge. Keys work normally here.</summary>
public sealed class ParentPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly Dictionary<string, Slider> _weights = [];
    private readonly Switch _upper, _words;
    private readonly Slider _volume;

    public ParentPage(ToyHost host)
    {
        _host = host;
        var s = host.Engine.Settings;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        var layout = new VerticalStackLayout { Padding = 40, Spacing = 14, MaximumWidthRequest = 720 };
        layout.Add(new Label { Text = "Keypuncher, for grown-ups", FontSize = 32, FontAttributes = FontAttributes.Bold });

        layout.Add(Heading("Languages"));
        layout.Add(new Label { Text = "How often each language is used for a key. Icelandic leads." });
        foreach (var (tag, name) in new[] { ("is", "Icelandic"), ("en", "English"), ("bg", "Bulgarian") })
        {
            var slider = new Slider(0, 1, s.LanguageWeights.GetValueOrDefault(tag)) { WidthRequest = 300 };
            _weights[tag] = slider;
            layout.Add(Row(name, slider));
        }

        _upper = new Switch { IsToggled = s.UpperCase };
        layout.Add(Row("Capital letters", _upper));
        _words = new Switch { IsToggled = s.SayWords };
        layout.Add(Row("Say a word after a letter", _words));
        _volume = new Slider(0, Mixer.MaxVolume, s.Volume) { WidthRequest = 300 };
        layout.Add(Row("Volume", _volume));

        layout.Add(Heading("Voices"));
        var recorded = KeyMeanings.Languages
            .Select(tag => $"{tag}: {ClipCatalog.For(tag).Count(e => host.Clips.Exists(e.Id))} of {ClipCatalog.For(tag).Count(e => !e.Optional)} recorded");
        layout.Add(new Label { Text = string.Join("    ", recorded) });
        layout.Add(new Label { Text = "Windows voices installed: " + string.Join(", ", SafeVoices()) + ". Used only for numbers and words that are not recorded yet." });
        layout.Add(Button("Record voices", () => ToyApp.Show(new RecordingPage(_host))));

        layout.Add(Heading("This computer"));
        layout.Add(new Label
        {
            Text = $"Keyboard: {(host.Keyboard.UsingIcelandicLayout ? "Icelandic layout" : "Icelandic layout NOT found; using the account's layout")}.  "
                 + $"Sound: {(host.Audio is { } a ? $"{a.LatencyMilliseconds} ms buffer" : "off, " + host.AudioError)}.  "
                 + $"Drawing: {PlayPage.FramesPerSecond:0} frames a second.  "
                 + $"Things: {host.World.Live.Count} live, {host.World.Baked.Count} in the background.",
        });
        layout.Add(new Label { Text = $"Data folder: {host.DataFolder}", FontSize = 12 });

        var buttons = new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Add(Button("Back to play", Play));
        buttons.Add(Button("Start a fresh world", FreshWorld));
        buttons.Add(Button("Quit", Quit));
        layout.Add(buttons);

        Content = new ScrollView { Content = layout };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _host.Mode = HostMode.Parent;
    }

    protected override void OnDisappearing()
    {
        _host.SaveSettings(new ToySettings(
            _weights.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value.Value, 2)),
            _upper.IsToggled, (float)_volume.Value, _words.IsToggled));
        base.OnDisappearing();
    }

    private void Play() => ToyApp.Show(new PlayPage(_host));

    private async void FreshWorld()
    {
        if (!await DisplayAlertAsync("Start a fresh world?", "Everything the child has made so far is cleared. This cannot be undone.", "Clear it", "Keep it"))
            return;
        _host.World.Clear();
        _host.SaveWorld();
    }

    private void Quit()
    {
        _host.SaveWorld();
        Application.Current!.Quit();
    }

    private static IEnumerable<string> SafeVoices()
    {
        try { return WindowsVoices.InstalledLanguages(); }
        catch (Exception) { return ["unknown"]; }
    }

    private static Label Heading(string text) =>
        new() { Text = text, FontSize = 22, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 0) };

    private static HorizontalStackLayout Row(string label, View control) =>
        new() { Spacing = 16, Children = { new Label { Text = label, WidthRequest = 220, VerticalOptions = LayoutOptions.Center }, control } };

    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, Padding = new Thickness(20, 10) };
        b.Clicked += (_, _) => action();
        return b;
    }
}
