using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Inference.Ollama;
using Keypuncher.Language;
using Keypuncher.Pictures;
using Keypuncher.Platform.Windows;
using Keypuncher.Rendering;
using Keypuncher.Storage;

namespace Keypuncher.App.Pages;

/// <summary>The grown-ups' screen, reached only through the exit challenge. Keys work normally here.</summary>
public sealed class ParentPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly Dictionary<string, Slider> _weights = [];
    private readonly Switch _upper, _words, _thingWords, _noises, _director, _directorTest, _recordPlay, _pictures;
    private readonly Label _pictureStatus = new() { FontSize = 13 };
    private readonly Label _lockStatus = new();
    private readonly Button _lockButton;
    private Button _quitButton = null!;
    private readonly PicturesPanel _pictureList;
    private readonly Slider _volume;
    private readonly Picker _calm, _session;
    private readonly Action<string> _fetchFinished;
    private readonly Picker _directorModel, _checkModel;
    private readonly List<string> _directorModels = [], _checkModels = [];
    private readonly Label _modelStatus = new() { FontSize = 13 };
    private readonly Button _modelDownload;
    private IReadOnlyList<string> _modelsMissing = [];
    private bool _fillingModels;
    private CancellationTokenSource? _modelDownloading;

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
        // A fetch finishes on the picture worker; the list is redrawn on the UI thread so a new picture shows at once.
        _fetchFinished = _ => MainThread.BeginInvokeOnMainThread(RefreshPictures);
        var s = host.Engine.Settings;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        _lockButton = Button("Let Keypuncher turn off Win+L (asks for admin once)", AskToTurnOffLock);
        // The list asks the controls below what they say now; it is first drawn after they are made.
        _pictureList = new PicturesPanel(host,
            () => CalmChoices[Math.Max(0, _calm!.SelectedIndex)].Level,
            tag => _weights.TryGetValue(tag, out var weight) ? weight.Value : 0);
        var layout = new VerticalStackLayout { Padding = 40, Spacing = 14, MaximumWidthRequest = 1040 };
        layout.Add(new Label { Text = "Keypuncher, for grown-ups", FontSize = 32, FontAttributes = FontAttributes.Bold });

        // The two ways out first, where nobody has to look for them.
        var play = Button("Back to play", Play);
        play.BackgroundColor = Color.FromArgb("#2E7D32");
        _quitButton = Button("Quit Keypuncher", Quit);
        _quitButton.BackgroundColor = Color.FromArgb("#C62828");
        layout.Add(new HorizontalStackLayout { Spacing = 12, Children = { play, _quitButton } });

        layout.Add(Heading("Languages"));
        layout.Add(new Label { Text = "How often each language is picked for a play session. One session keeps one language, so each key keeps one sound. Icelandic leads." });
        foreach (var (tag, name) in new[] { ("is", "Icelandic"), ("en", "English"), ("bg", "Bulgarian") })
        {
            var slider = new Slider(0, 1, s.LanguageWeights.GetValueOrDefault(tag)) { WidthRequest = 300 };
            _weights[tag] = slider;
            // A language coming into use or going out of it changes which word boxes the list of things shows.
            slider.ValueChanged += (_, e) => { if ((e.OldValue > 0) != (e.NewValue > 0)) _pictureList.Reload(); };
            layout.Add(Row(name, slider));
        }

        _upper = new Switch { IsToggled = s.UpperCase };
        layout.Add(Row("Capital letters", _upper));
        _words = new Switch { IsToggled = s.SayWords };
        layout.Add(Row("Say a word after a letter", _words));
        _thingWords = new Switch { IsToggled = s.SayThingWords };
        layout.Add(Row("Show and say your words", _thingWords));
        layout.Add(new Label { Text = "A real thing with a word you wrote for it under Pictures, in the language of the play session, is named the first time it appears in a session: the word is shown by it and said, in your recording if there is one. A letter can also bring such a word. Off, the child only meets the toy's own starter words.", FontSize = 13 });
        _noises = new Switch { IsToggled = s.AnimalNoises };
        layout.Add(Row("Animals sometimes make their noise", _noises));
        _volume = new Slider(0, Mixer.MaxVolume, s.Volume) { WidthRequest = 300 };
        layout.Add(Row("Volume", _volume));

        layout.Add(Heading("Calm"));
        layout.Add(new Label { Text = "How busy the screen may get. Calmer levels keep fewer things on screen, let fewer move, draw them bigger and bolder in fewer colours, and end play sooner with a goodnight." });
        _calm = ChoicePicker(CalmChoices.Select(c => c.Name), Array.FindIndex(CalmChoices, c => c.Level == s.Calm));
        // The calm level decides how far up its ladder each thing in the list below is drawn.
        _calm.SelectedIndexChanged += (_, _) => _pictureList.Reload();
        layout.Add(Row("Calm level", _calm));
        _session = ChoicePicker(SessionChoices.Select(SessionName), Math.Max(0, Array.IndexOf(SessionChoices, s.SessionMinutes)));
        layout.Add(Row("Goodnight after", _session));

        layout.Add(Heading("Real things"));
        layout.Add(new Label { Text = $"Every {Minutes(s.Limits.RestyleMinutes)} of play, the local model turns one key's thing into a real animal, extinct animal, plant, vehicle or object; a thing that really grows into something (a tadpole) later becomes it. Each new thing appears once on its own the moment it is chosen, and from then on whenever its key is pressed. Every {Minutes(s.Limits.SceneMinutes)} it also moves the scene a step: the weather changes, the time of day moves on, or the world travels to a new real place that looks different, fading in slowly. Each new thing and place is checked by a second question to the model, and story creatures and places are turned away. Names and facts below are for grown-ups only: the child never sees or hears them, only a word you wrote for the thing yourself." });
        _director = new Switch { IsToggled = s.Director };
        layout.Add(Row("Let the model change things", _director));
        _directorTest = new Switch { IsToggled = s.DirectorTest };
        layout.Add(Row("Test mode", _directorTest));
        layout.Add(new Label { Text = $"For trying the model out, not for the child: keys and the scene change as fast as the model can answer (a few seconds each), the choice is written at the top of the screen, scenes fade in over 3 seconds and there is no goodnight. Switch it off before the child plays.", FontSize = 13 });
        _recordPlay = new Switch { IsToggled = s.RecordPlay };
        layout.Add(Row("Record play", _recordPlay));
        layout.Add(new Label { Text = $"Each play session is saved as a small video (4 frames a second, stamped with the time the log uses) in {Path.Combine(host.DataFolder, ToyHost.VideosName)}, so a problem can be looked at afterwards; test mode is always recorded. Nothing leaves this computer, and the oldest videos are deleted past 4 GB. When play starts and ends, the goodnight and how many keys were pressed go in {Path.Combine(host.DataFolder, ToyHost.DirectorLogName)}.", FontSize = 13 });
        layout.Add(new Label { Text = DirectorSummary(host), FontSize = 13 });

        layout.Add(Heading("Models"));
        layout.Add(new Label { Text = $"The toy's models run on this computer through Ollama at {host.OllamaAddress}. One chooses and checks the real things and places (the toy was tuned with {ToyHost.DefaultModel}); one that can see looks at each new picture ({OllamaVision.DefaultModel}). Any model Ollama has can be chosen here; a change is used from the next request on and is remembered. A bigger model that does not fit on the graphics card answers several times slower." });
        _directorModel = ChoicePicker([host.DirectorModel], 0);
        _directorModel.SelectedIndexChanged += (_, _) => ModelChosen(_directorModel, _directorModels, host.ChooseDirectorModel);
        layout.Add(Row("Model for real things", _directorModel));
        _checkModel = ChoicePicker([host.PictureCheckModel], 0);
        _checkModel.SelectedIndexChanged += (_, _) => ModelChosen(_checkModel, _checkModels, host.ChoosePictureCheckModel);
        layout.Add(Row("Model that checks pictures", _checkModel));
        layout.Add(_modelStatus);
        _modelDownload = Button("Download", DownloadOllamaModels);
        _modelDownload.IsVisible = false;
        layout.Add(_modelDownload);

        layout.Add(Heading("Pictures"));
        layout.Add(new Label { Text = $"Each real thing the model picks gets a photo of the real thing, fetched in the background from Wikipedia (Wikidata and iNaturalist as backups), cut out on this computer and kept in {host.PictureFolder.Root} with its credit beside it. The toy draws it step by step as the child meets the thing: shape, silhouette, a few flat colours, then the photo. Nothing is shown before it is cut out, and play never waits on the internet. The cut-out needs two model files (about 400 MB) that are only downloaded from here." });
        _pictures = new Switch { IsToggled = s.Pictures };
        layout.Add(Row("Fetch pictures from the internet", _pictures));
        layout.Add(_pictureStatus);
        var modelButtons = new HorizontalStackLayout { Spacing = 12 };
        if (!host.Models.AllPresent) modelButtons.Add(Button("Download the cut-out models (about 400 MB)", DownloadModels));
        modelButtons.Add(Button("Fetch wanted pictures now", () => { _host.PictureJobs.RetryWanted(again: true); RefreshPictures(); }));
        layout.Add(modelButtons);
        layout.Add(new Label { Text = "Every thing the toy knows is listed below with the four rungs of its ladder, drawn as the child sees them; the rung drawn now has a green edge, and a pale rung is one this calm level does not reach. Tap a rung to choose what is drawn there: leave it to the toy, pick one of the thing's pictures, keep the drawn shape, or skip the rung. \"Try another\" fetches a different photo and keeps the old one to choose from; \"Add from a file\" takes a picture of your own (one with a clear background goes in as it is, any other is cut out). A picture you chose or added is never taken out of play by the check: its doubt is only shown here. Beside the rungs is the thing's word in each language that play uses: type it and press Record to say it, Stop when done; it is played back at once. The words also appear in the list under \"Record voices\" below, for recording many in one go.", FontSize = 13 });
        layout.Add(_pictureList);
        RefreshPictures();

        layout.Add(Heading("Voices"));
        var recorded = KeyMeanings.Languages
            .Select(tag => (Tag: tag, Clips: ClipCatalog.For(tag, host.Words.In(tag)).ToList()))
            .Select(l => $"{l.Tag}: {l.Clips.Count(e => host.Clips.Exists(e.Id))} of {l.Clips.Count(e => !e.Optional)} recorded");
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
        layout.Add(_lockStatus);
        layout.Add(_lockButton);
        RefreshLock();
        layout.Add(new Label { Text = $"Data folder: {host.DataFolder}", FontSize = 12 });

        layout.Add(Button("Start a fresh world", FreshWorld));

        Content = new ScrollView { Content = layout };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _host.Mode = HostMode.Parent;
        _host.PictureJobs.Finished += _fetchFinished;
        RefreshPictures();
        RefreshModels();
        // Play has stopped and the director does not choose on this page, so the card is free for the vision model.
        // Grown-ups usually end play here rather than waiting for the goodnight (both plays on 2026-11-08 did), and
        // the check used to run only after a goodnight, so new pictures went unchecked.
        _host.CheckNewPictures();
    }

    protected override void OnDisappearing()
    {
        _host.PictureJobs.Finished -= _fetchFinished;
        // A model download under way is stopped; Ollama keeps what it has and goes on from there next time.
        _modelDownloading?.Cancel();
        _pictureList.Close();
        // Choices made with the console tool while this page was open are picked up before play goes on.
        _host.Choices.Reload();
        _host.Words.Reload();
        _host.SaveSettings(new ToySettings(
            _weights.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value.Value, 2)),
            _upper.IsToggled, (float)_volume.Value, _words.IsToggled,
            CalmChoices[Math.Max(0, _calm.SelectedIndex)].Level, SessionChoices[Math.Max(0, _session.SelectedIndex)],
            _noises.IsToggled, _director.IsToggled, _directorTest.IsToggled, _recordPlay.IsToggled, _pictures.IsToggled, _thingWords.IsToggled,
            // The models are saved the moment they are chosen; carried over here.
            _host.Engine.Settings.DirectorModel, _host.Engine.Settings.PictureCheckModel));
        base.OnDisappearing();
    }

    private void Play() => ToyApp.Show(new PlayPage(_host));

    /// <summary>Fills the status line and the list of things from the folder as it is now.</summary>
    private void RefreshPictures()
    {
        var host = _host;
        var all = host.PictureFolder.All();
        var wanted = host.PictureFolder.Wanted.All();
        var jobs = host.PictureJobs.Status;
        var models = host.Models.AllPresent ? "both cut-out models are here"
            : host.Models.AnyPresent ? "one cut-out model is here" : "the cut-out models are not downloaded yet, so photos wait as they are";
        _pictureStatus.Text = $"{all.Count} pictures ({all.Count(p => p.Source == PictureSource.Photo)} photos, {all.Count(p => p.Source == PictureSource.Generated)} made by an image model, {all.Count(p => p.Hidden)} hidden); "
            + $"{wanted.Count} wanted; {models}. "
            + (jobs.Fetching is { } fetching ? $"Fetching {fetching} now" : jobs.Queued > 0 ? $"{jobs.Queued} waiting" : "Nothing being fetched")
            + (jobs.Last is { } last ? $"; last: {last}." : ".");

        _pictureList.Reload();
    }

    /// <summary>Downloads the two cut-out models with a running line of progress, then fetches what was waiting.</summary>
    private async void DownloadModels()
    {
        var progress = new Progress<ModelProgress>(p =>
            _pictureStatus.Text = $"Downloading {p.Model.FileName}: {p.Done / 1_000_000} of {p.Total / 1_000_000} MB");
        try
        {
            await _host.Models.DownloadMissingAsync(progress);
            _host.PictureJobs.RetryWanted(again: true);
        }
        catch (Exception e)
        {
            Log.Error("pictures", "downloading the cut-out models did not finish", e);
            _pictureStatus.Text = $"The download did not finish: {e.GetBaseException().Message}";
            return;
        }
        RefreshPictures();
    }

    /// <summary>Asks Ollama what it has, fills the two model lists from it and says what is missing.</summary>
    /// <param name="first">Something to say before it, such as why a download stopped.</param>
    private async void RefreshModels(string? first = null)
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        try
        {
            var check = await _host.CheckModelsAsync(withCapabilities: true);
            _fillingModels = true;
            try
            {
                Fill(_directorModel, _directorModels, check, _host.DirectorModel, ToyHost.DefaultModel, m => m.Writes);
                Fill(_checkModel, _checkModels, check, _host.PictureCheckModel, OllamaVision.DefaultModel, m => m.Sees);
            }
            finally { _fillingModels = false; }

            // Both are listed here whatever is switched on, so a model can be fetched before its switch is turned on.
            _modelsMissing = !check.Reachable ? []
                : [.. new[] { _host.DirectorModel, _host.PictureCheckModel }.Where(m => !OllamaModels.Has(check.Installed, m)).Distinct(StringComparer.OrdinalIgnoreCase)];
            if (_modelDownloading is not null) return;
            _modelStatus.Text = (first is null ? "" : first + " ") + (!check.Reachable ? $"Ollama is not answering at {_host.OllamaAddress}, so its models cannot be listed. Start Ollama and come back to this page."
                : _modelsMissing.Count == 0 ? "Both models are installed."
                : "NOT installed: " + string.Join("; ", _modelsMissing.Select(m => ModelsPage.Describe(_host, m))) + ". Until then that part of the toy does nothing. Download below, or choose a model that is installed.");
            _modelDownload.IsVisible = _modelsMissing.Count > 0;
            _modelDownload.IsEnabled = true;
            _modelDownload.Text = "Download " + string.Join(" and ", _modelsMissing);
        }
        catch (Exception e)
        {
            Log.Error("ollama", "listing Ollama's models for the grown-ups' page failed", e);
            _modelStatus.Text = $"Ollama's models could not be listed: {e.GetBaseException().Message}";
        }
    }

    /// <summary>Fills a model list: what Ollama has that can do the job, plus the toy's own choice and the one in use
    /// even when they are not installed (marked so), with the one in use selected.</summary>
    private static void Fill(Picker picker, List<string> names, ModelCheck check, string current, string toyDefault, Func<InstalledModel, bool> fits)
    {
        names.Clear();
        names.AddRange(check.Installed.Where(fits).Select(m => m.Name).Order(StringComparer.OrdinalIgnoreCase));
        foreach (var extra in new[] { toyDefault, current })
            if (!names.Any(n => OllamaModels.Same(n, extra))) names.Add(extra);
        picker.Items.Clear();
        foreach (var name in names)
        {
            var installed = check.Installed.FirstOrDefault(m => OllamaModels.Same(m.Name, name));
            picker.Items.Add(installed is not null ? $"{name}  ({installed.Size / 1e9:0.0} GB)"
                : check.Reachable ? $"{name}  (not installed)" : name);
        }
        picker.SelectedIndex = names.FindIndex(n => OllamaModels.Same(n, current));
    }

    /// <summary>A grown-up picked a model from a list (not the list being filled).</summary>
    private void ModelChosen(Picker picker, List<string> names, Action<string> choose)
    {
        if (_fillingModels || picker.SelectedIndex < 0 || picker.SelectedIndex >= names.Count) return;
        try
        {
            choose(names[picker.SelectedIndex]);
        }
        catch (Exception e)
        {
            Log.Error("ollama", "changing the model failed", e);
            _modelStatus.Text = $"The model could not be changed: {e.GetBaseException().Message}";
            return;
        }
        RefreshModels();
    }

    /// <summary>Downloads the chosen models Ollama lacks, with a running line of progress.</summary>
    private async void DownloadOllamaModels()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        if (_modelDownloading is not null) return;
        var stop = _modelDownloading = new CancellationTokenSource();
        try
        {
            _modelDownload.IsEnabled = false;
            var progress = new Progress<PullProgress>(p => _modelStatus.Text = ModelsPage.ProgressLine(p));
            foreach (var model in _modelsMissing.ToList())
            {
                _modelStatus.Text = $"Asking Ollama for {model}...";
                await _host.DownloadModelAsync(model, progress, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            Log.Error("ollama", "downloading a model from the grown-ups' page did not finish", e);
            _modelDownloading = null;
            RefreshModels($"The download did not finish: {e.GetBaseException().Message}.");
            return;
        }
        finally
        {
            if (ReferenceEquals(_modelDownloading, stop)) _modelDownloading = null;
            stop.Dispose();
        }
        RefreshModels();
    }

    /// <summary>Says whether Win+L is off, and offers the one-time admin step when Windows refused it.</summary>
    private void RefreshLock()
    {
        var winL = _host.WinL;
        _lockStatus.Text = winL.Active ? "Win+L: turned off while Keypuncher runs, put back when it quits."
            : winL.NeedsPermission ? "Win+L: NOT turned off. Windows does not let this account change the setting, so the child can lock the computer. The button below asks for an administrator's permission once (the usual Windows prompt); after that Keypuncher turns Win+L off and back on by itself."
            : "Win+L: left as it was (already off, or this computer does not allow changing it; see keypuncher.log).";
        _lockButton.IsVisible = winL.NeedsPermission;
    }

    private async void AskToTurnOffLock()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        try
        {
            _lockButton.IsEnabled = false;
            await _host.WinL.AskForPermissionAsync();
        }
        catch (Exception e)
        {
            Log.Error("windows", "asking for permission to turn off Win+L failed", e);
            _lockStatus.Text = $"Win+L: the permission step failed: {e.GetBaseException().Message}";
            _lockButton.IsEnabled = true;
            return;
        }
        _lockButton.IsEnabled = true;
        RefreshLock();
    }

    private async void FreshWorld()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        try
        {
            if (!await DisplayAlertAsync("Start a fresh world?", "Everything the child has made so far is cleared. This cannot be undone.", "Clear it", "Keep it"))
                return;
            Log.Info("world", "a grown-up started a fresh world");
            _host.World.Clear();
            _host.SaveWorld();
        }
        catch (Exception e)
        {
            Log.Error("world", "starting a fresh world failed", e);
        }
    }

    private async void Quit()
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        try
        {
            Log.Info("app", "a grown-up chose Quit");
            _host.SaveWorld();
            if (_host.PictureCheckRunning)
            {
                // A few pictures take a few seconds each; quitting would throw that work away until the next time.
                _quitButton.IsEnabled = false;
                _quitButton.Text = "Finishing the picture check...";
                await _host.FinishPictureCheckAsync(ToyHost.MaxPictureCheckOnQuit);
            }
        }
        catch (Exception e)
        {
            Log.Error("app", "waiting for the picture check before quitting failed; quitting anyway", e);
        }
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
        catch (Exception e)
        {
            Log.Warn("speech", "could not list the installed Windows voices", e);
            return ["unknown"];
        }
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
