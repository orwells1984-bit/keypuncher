using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Language;
using Keypuncher.Platform.Windows;
using Keypuncher.Storage;

namespace Keypuncher.App.Pages;

/// <summary>The grown-ups' screen, reached only through the exit challenge. Keys work normally here.</summary>
public sealed class ParentPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly Dictionary<string, Slider> _weights = [];
    private readonly Switch _upper, _words, _noises, _director, _directorTest, _recordPlay;
    private readonly Slider _volume;
    private readonly Picker _calm, _session;

    private static readonly (CalmLevel Level, string Name)[] CalmChoices =
    [
        (CalmLevel.Baby, "Baby (6 to 12 months): calmest"),
        (CalmLevel.Toddler, "Toddler (12 to 18 months)"),
        (CalmLevel.BigToddler, "Big toddler (18 months and up)"),
    ];

    /// <summary>Session lengths offered; null follows the calm level, 0 never ends.</summary>
    private static readonly int?[] SessionChoices = [null, 5, 10, 15, 20, 30, 0];

    public ParentPage(ToyHost host)
    {
        _host = host;
        var s = host.Engine.Settings;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        var layout = new VerticalStackLayout { Padding = 40, Spacing = 14, MaximumWidthRequest = 720 };
        layout.Add(new Label { Text = "Keypuncher, for grown-ups", FontSize = 32, FontAttributes = FontAttributes.Bold });

        // The two ways out first, where nobody has to look for them.
        var play = Button("Back to play", Play);
        play.BackgroundColor = Color.FromArgb("#2E7D32");
        var quit = Button("Quit Keypuncher", Quit);
        quit.BackgroundColor = Color.FromArgb("#C62828");
        layout.Add(new HorizontalStackLayout { Spacing = 12, Children = { play, quit } });

        layout.Add(Heading("Languages"));
        layout.Add(new Label { Text = "How often each language is picked for a play session. One session keeps one language, so each key keeps one sound. Icelandic leads." });
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
        _noises = new Switch { IsToggled = s.AnimalNoises };
        layout.Add(Row("Animals sometimes make their noise", _noises));
        _volume = new Slider(0, Mixer.MaxVolume, s.Volume) { WidthRequest = 300 };
        layout.Add(Row("Volume", _volume));

        layout.Add(Heading("Calm"));
        layout.Add(new Label { Text = "How busy the screen may get. Calmer levels keep fewer things on screen, let fewer move, draw them bigger and bolder in fewer colours, and end play sooner with a goodnight." });
        _calm = ChoicePicker(CalmChoices.Select(c => c.Name), Array.FindIndex(CalmChoices, c => c.Level == s.Calm));
        layout.Add(Row("Calm level", _calm));
        _session = ChoicePicker(SessionChoices.Select(SessionName), Math.Max(0, Array.IndexOf(SessionChoices, s.SessionMinutes)));
        layout.Add(Row("Goodnight after", _session));

        layout.Add(Heading("Real things"));
        layout.Add(new Label { Text = $"Every {Minutes(s.Limits.RestyleMinutes)} of play, the local model turns one key's thing into a real animal, extinct animal, plant, vehicle or object; a thing that really grows into something (a tadpole) later becomes it. Each new thing appears once on its own the moment it is chosen, and from then on whenever its key is pressed. Every {Minutes(s.Limits.SceneMinutes)} it also moves the scene a step: the weather changes, the time of day moves on, or the world travels to a new real place that looks different, fading in slowly. Each new thing and place is checked by a second question to the model, and story creatures and places are turned away. Names and facts below are for grown-ups only: the child never sees or hears them." });
        _director = new Switch { IsToggled = s.Director };
        layout.Add(Row("Let the model change things", _director));
        _directorTest = new Switch { IsToggled = s.DirectorTest };
        layout.Add(Row("Test mode", _directorTest));
        layout.Add(new Label { Text = $"For trying the model out, not for the child: keys and the scene change as fast as the model can answer (a few seconds each), the choice is written at the top of the screen, scenes fade in over 3 seconds and there is no goodnight. Switch it off before the child plays.", FontSize = 13 });
        _recordPlay = new Switch { IsToggled = s.RecordPlay };
        layout.Add(Row("Record play", _recordPlay));
        layout.Add(new Label { Text = $"Each play session is saved as a small video (4 frames a second, stamped with the time the log uses) in {Path.Combine(host.DataFolder, ToyHost.VideosName)}, so a problem can be looked at afterwards; test mode is always recorded. Nothing leaves this computer, and the oldest videos are deleted past 4 GB. When play starts and ends, the goodnight and how many keys were pressed go in {Path.Combine(host.DataFolder, ToyHost.DirectorLogName)}.", FontSize = 13 });
        layout.Add(new Label { Text = DirectorSummary(host), FontSize = 13 });

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

        layout.Add(Button("Start a fresh world", FreshWorld));

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
            _upper.IsToggled, (float)_volume.Value, _words.IsToggled,
            CalmChoices[Math.Max(0, _calm.SelectedIndex)].Level, SessionChoices[Math.Max(0, _session.SelectedIndex)],
            _noises.IsToggled, _director.IsToggled, _directorTest.IsToggled, _recordPlay.IsToggled));
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

    /// <summary>The model, the families now, and the last few picks with their facts and what the checks said.</summary>
    private static string DirectorSummary(ToyHost host)
    {
        var families = host.World.Families;
        var real = Enumerable.Range(0, BuiltInRecipes.InventedFamilies).Where(i => families.ChangedAt(i) is not null)
            .Select(i => families[i].GrowsInto is { } g ? $"{families[i].Name} (grows into {g})" : families[i].Name).ToList();
        var lines = new List<string>
        {
            $"Model: {host.Director.ModelName} at {host.OllamaAddress}{(host.Director.Busy ? ", thinking now" : "")}.",
            $"Keys now making real things: {(real.Count == 0 ? "none yet" : string.Join(", ", real))}; {BuiltInRecipes.InventedFamilies - real.Count} still make invented shapes.",
            $"Scene now: {host.World.Scene}.{(host.World.Scene.Fact is { } fact ? " " + fact : "")}",
        };
        if (host.Director.Log.Count == 0) lines.Add("No picks yet in this run.");
        foreach (var entry in host.Director.Log) lines.AddRange(entry.Lines());
        lines.Add($"Everything the model is asked and answers is also written to {Path.Combine(host.DataFolder, ToyHost.DirectorLogName)}.");
        return string.Join("\n", lines);
    }

    private static string Minutes(double minutes) =>
        minutes == 1 ? "minute" : minutes < 1 ? $"{minutes * 60:0} seconds" : $"{minutes:0.#} minutes";

    private static IEnumerable<string> SafeVoices()
    {
        try { return WindowsVoices.InstalledLanguages(); }
        catch (Exception) { return ["unknown"]; }
    }

    private static string SessionName(int? minutes) => minutes switch
    {
        null => "The calm level's default (5, 10 or 15 minutes)",
        0 => "Never",
        _ => $"{minutes} minutes",
    };

    /// <summary>A drop-down with explicit colours, so it reads whatever the Windows theme is.</summary>
    private static Picker ChoicePicker(IEnumerable<string> items, int selected)
    {
        var picker = new Picker { WidthRequest = 360, TextColor = Color.FromArgb("#222222"), BackgroundColor = Colors.White };
        foreach (var item in items) picker.Items.Add(item);
        picker.SelectedIndex = Math.Max(0, selected);
        return picker;
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
