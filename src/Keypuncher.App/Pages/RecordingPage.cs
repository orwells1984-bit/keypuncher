using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Language;
using Keypuncher.Platform.Windows;

namespace Keypuncher.App.Pages;

/// <summary>
/// Vanya's recording screen. Pick a language, then work down the list: Space starts recording, Space
/// again stops, and the clip is trimmed, levelled, saved, played back and the next one comes up.
/// Left arrow goes back to redo one, right arrow skips. Letters are said by their sound.
/// </summary>
public sealed class RecordingPage : ContentPage
{
    private readonly ToyHost _host;
    private readonly MicRecorder _mic = new();
    private readonly Picker _language;
    private readonly CollectionView _list;
    private readonly Label _prompt, _hint, _status;
    private readonly ProgressBar _level;
    private readonly Button _record;
    private List<Row> _rows = [];
    private int _index;

    public RecordingPage(ToyHost host)
    {
        _host = host;
        BackgroundColor = Color.FromArgb("#FFF8EC");

        _language = new Picker { Title = "Language", ItemsSource = new[] { "Icelandic", "English", "Bulgarian" }, SelectedIndex = 0, WidthRequest = 200 };
        _language.SelectedIndexChanged += (_, _) => LoadLanguage();

        _list = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            WidthRequest = 280,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { Padding = new Thickness(10, 6), FontSize = 16 };
                label.SetBinding(Label.TextProperty, nameof(Row.Text));
                return label;
            }),
        };
        _list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is Row r) Select(_rows.IndexOf(r), scroll: false);
        };

        _prompt = new Label { FontSize = 140, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center };
        _hint = new Label { FontSize = 22, HorizontalOptions = LayoutOptions.Center };
        _status = new Label { FontSize = 18, HorizontalOptions = LayoutOptions.Center, TextColor = Colors.DimGray };
        _level = new ProgressBar { WidthRequest = 400, HorizontalOptions = LayoutOptions.Center, ProgressColor = Colors.OrangeRed };
        _record = new Button { Text = "● Record (Space)", FontSize = 22, Padding = new Thickness(30, 14) };
        _record.Clicked += (_, _) => ToggleRecording();

        var play = new Button { Text = "▶ Play", Padding = new Thickness(20, 10) };
        play.Clicked += (_, _) => PlayCurrent();
        var delete = new Button { Text = "Delete", Padding = new Thickness(20, 10) };
        delete.Clicked += (_, _) => DeleteCurrent();
        var back = new Button { Text = "Done", Padding = new Thickness(20, 10) };
        back.Clicked += (_, _) => ToyApp.Show(new ParentPage(_host));

        var centre = new VerticalStackLayout
        {
            Spacing = 18,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _prompt, _hint, _level, _record,
                new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center, Children = { play, delete, back } },
                _status,
                new Label
                {
                    Text = "Space: record / stop.  ←: back to redo.  →: skip.  Enter: play.",
                    HorizontalOptions = LayoutOptions.Center, TextColor = Colors.Gray,
                },
            },
        };

        var grid = new Grid
        {
            Padding = 24,
            ColumnSpacing = 24,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
        };
        grid.Add(_language, 0, 0);
        grid.Add(_list, 0, 1);
        grid.Add(centre, 1, 1);
        Content = grid;

        _mic.Level += peak => MainThread.BeginInvokeOnMainThread(() => _level.Progress = peak);
        LoadLanguage();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _host.Mode = HostMode.Recording;
        _host.RecordingKey += OnShortcut;
    }

    protected override void OnDisappearing()
    {
        _host.RecordingKey -= OnShortcut;
        if (_mic.Recording) _mic.Stop();
        base.OnDisappearing();
    }

    private string Language => KeyMeanings.Languages[Math.Max(0, _language.SelectedIndex)];

    private void LoadLanguage()
    {
        _rows = [.. ClipCatalog.For(Language).Select(e => new Row(e, _host.Clips.Exists(e.Id)))];
        _list.ItemsSource = _rows;
        var firstMissing = _rows.FindIndex(r => !r.Recorded && !r.Entry.Optional);
        Select(firstMissing < 0 ? 0 : firstMissing, scroll: true);
    }

    private void Select(int index, bool scroll)
    {
        if (_rows.Count == 0) return;
        _index = Math.Clamp(index, 0, _rows.Count - 1);
        var row = _rows[_index];
        _prompt.Text = row.Entry.Prompt;
        _hint.Text = row.Entry.Hint + (row.Entry.Optional ? " (optional)" : "");
        if (!ReferenceEquals(_list.SelectedItem, row)) _list.SelectedItem = row;
        if (scroll) _list.ScrollTo(_index, position: ScrollToPosition.Center, animate: false);
    }

    private void OnShortcut(KeyPress key)
    {
        if (!key.IsDown) return;
        switch (key.ScanCode)
        {
            case 0x39: ToggleRecording(); break;
            case 0xE04B: Select(_index - 1, scroll: true); break;
            case 0xE04D: Select(_index + 1, scroll: true); break;
            case 0x1C: PlayCurrent(); break;
        }
    }

    private void ToggleRecording()
    {
        if (!_mic.Recording)
        {
            _host.Mixer.StopSpeech();
            try { _mic.Start(); }
            catch (Exception e)
            {
                Log.Warn("recording", "could not open the microphone", e);
                _status.Text = "Could not open the microphone: " + e.Message + " (check Windows privacy settings for the microphone).";
                return;
            }
            _record.Text = "■ Stop (Space)";
            _status.Text = "Recording…";
            return;
        }

        var raw = _mic.Stop();
        _record.Text = "● Record (Space)";
        _level.Progress = 0;
        var clip = ClipProcessing.TrimAndNormalize(raw, MicRecorder.SampleRate);
        if (clip.Length == 0)
        {
            _status.Text = "Didn't hear anything. Try again a little closer to the microphone.";
            return;
        }

        var row = _rows[_index];
        _host.Clips.Save(row.Entry.Id, s => WavFile.Write(s, clip, MicRecorder.SampleRate));
        _host.Speech.Invalidate(row.Entry.Id);
        row.Recorded = true;
        _host.Mixer.Speak(ClipProcessing.Resample(clip, MicRecorder.SampleRate, Mixer.SampleRate));
        _status.Text = $"Saved {row.Entry.Prompt} ({clip.Length / (double)MicRecorder.SampleRate:0.0} s). ← to redo it.";
        Select(_index + 1, scroll: true);
    }

    private void PlayCurrent()
    {
        var id = _rows[_index].Entry.Id;
        if (!_host.Clips.Exists(id)) { _status.Text = "Not recorded yet."; return; }
        using var s = _host.Clips.OpenRead(id);
        var (mono, rate) = WavFile.Read(s);
        _host.Mixer.Speak(ClipProcessing.Resample(mono, rate, Mixer.SampleRate));
    }

    private void DeleteCurrent()
    {
        var row = _rows[_index];
        _host.Clips.Delete(row.Entry.Id);
        _host.Speech.Invalidate(row.Entry.Id);
        row.Recorded = false;
        _status.Text = $"Deleted {row.Entry.Prompt}.";
    }

    /// <summary>One line in the list; tells the list when its tick changes.</summary>
    private sealed class Row(ClipEntry entry, bool recorded) : BindableObject
    {
        public ClipEntry Entry { get; } = entry;

        public bool Recorded
        {
            get => recorded;
            set
            {
                recorded = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        public string Text => $"{(Recorded ? "✓" : "   ")}  {Entry.Prompt}{(Entry.Optional ? "  (optional)" : "")}";
    }
}
