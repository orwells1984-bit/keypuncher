using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Language;
using Keypuncher.Pictures;
using Keypuncher.Platform.Windows;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.App.Pages;

/// <summary>
/// The list of things on the grown-ups' page: one row for each thing the toy knows, with the four rungs of its
/// realism ladder drawn as play draws them. Tapping a rung opens a strip under the row to choose what is drawn
/// there: left to the toy, one of the thing's pictures, the drawn shape, or the rung skipped. Each row also asks for
/// another picture, adds one from a file, hides the picture, and sets the thing's kind and whether it climbs.
/// Beside the rungs is the thing's word in each language in use: typed, and recorded and played back on the spot.
/// What is chosen or typed is written to the thing's card at once (see <see cref="ThingCards"/>); a recording is
/// kept with the other clips, named by the thing (see <see cref="ClipCatalog.ThingClip"/>).
/// </summary>
/// <remarks>The rungs are drawn on a background thread, one at a time, and kept as PNG bytes by picture file, rung and
/// calm level, so a list drawn again (after a choice, or a search) shows at once. Everything else runs on the UI thread.</remarks>
public sealed class PicturesPanel : ContentView
{
    /// <summary>How many things are listed before "Show more" is pressed.</summary>
    private const int PageSize = 20;
    /// <summary>The side of one rung as shown, and as drawn (twice that, for a sharp picture on a dense screen).</summary>
    private const int CellSide = 96, RenderSide = 192;

    private static readonly Color Ink = Color.FromArgb("#222222");
    private static readonly Color Faint = Color.FromArgb("#D8CFBF");
    private static readonly Color Chosen = Color.FromArgb("#2E7D32");
    private static readonly Color Wrong = Color.FromArgb("#C62828");
    private static readonly PictureStage[] Rungs = [PictureStage.Shape, PictureStage.Silhouette, PictureStage.Flat, PictureStage.Photo];
    private static readonly RealCategory[] Kinds = [RealCategory.Animal, RealCategory.ExtinctAnimal, RealCategory.Plant, RealCategory.Vehicle, RealCategory.Object];
    private static readonly (string Tag, string Name)[] LanguageNames = [(KeyMeanings.Icelandic, "Icelandic"), (KeyMeanings.English, "English"), (KeyMeanings.Bulgarian, "Bulgarian")];
    private static readonly (ThingFilter Filter, string Name)[] Filters =
    [
        (ThingFilter.All, "Everything, newest first"),
        (ThingFilter.NeedsALook, "Needs a look (failed the check, hidden, or no picture yet)"),
        (ThingFilter.Starter, "The toy's own starter things"),
    ];

    private readonly ToyHost _host;
    private readonly Picker _filter;
    private readonly Entry _search = new() { Placeholder = "Search by name or word", WidthRequest = 240, TextColor = Ink, BackgroundColor = Colors.White };
    private readonly Label _count = new() { FontSize = 13 };
    private readonly Label _note = new() { FontSize = 13, TextColor = Wrong, IsVisible = false };
    private readonly VerticalStackLayout _rows = new() { Spacing = 10 };
    private readonly Dictionary<string, byte[]> _rendered = [];
    private readonly SemaphoreSlim _drawing = new(1, 1);
    private readonly HashSet<string> _adding = [];
    private readonly MicRecorder _mic = new();
    /// <summary>The clip being recorded, and the buttons and level bar of the row it is recorded from.</summary>
    private ClipId? _recording;
    private Action? _recordingShow;
    private ProgressBar? _recordingLevel;
    private IReadOnlyList<ThingEntry> _things = [];
    private int _shown = PageSize;
    private (string Slug, PictureStage Stage)? _open;
    private int _generation;

    public PicturesPanel(ToyHost host)
    {
        _host = host;
        _filter = new Picker { WidthRequest = 360, TextColor = Ink, BackgroundColor = Colors.White };
        foreach (var (_, name) in Filters) _filter.Items.Add(name);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => { _shown = PageSize; Render(); };
        _search.TextChanged += (_, _) => { _shown = PageSize; Render(); };
        _mic.Level += peak => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_recordingLevel is { } bar) bar.Progress = peak;
        });
        Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new HorizontalStackLayout { Spacing = 12, Children = { _filter, _search } },
                _count, _note, _rows,
            },
        };
    }

    /// <summary>Reads the pictures folder again and draws the list from it.</summary>
    public void Reload()
    {
        Read();
        Render();
    }

    /// <summary>Stops a recording in progress without keeping it: for when the page is left.</summary>
    public void Close()
    {
        if (_mic.Recording) _mic.Stop();
        _recording = null;
        _recordingShow = null;
        _recordingLevel = null;
    }

    private void Read()
    {
        try
        {
            _things = ThingIndex.Build(_host.PictureFolder, StarterConcepts.All.Select(c => c.Id));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", "the pictures folder could not be read for the grown-ups' page", e);
            Say($"The pictures folder could not be read: {e.Message}");
        }
    }

    /// <summary>Draws the rows the filter and the search leave, from the things as last read.</summary>
    private void Render()
    {
        // Rungs still being drawn for the rows about to go are dropped; the ones already drawn are remembered.
        var generation = Interlocked.Increment(ref _generation);
        var filter = Filters[Math.Max(0, _filter.SelectedIndex)].Filter;
        var listed = ThingIndex.Select(_things, filter, _search.Text);
        var jobs = new List<RenderJob>();
        _rows.Clear();
        foreach (var thing in listed.Take(_shown)) _rows.Add(ThingRow(thing, jobs));
        _count.Text = listed.Count == _things.Count ? $"{listed.Count} things." : $"{listed.Count} of {_things.Count} things.";
        if (listed.Count > _shown)
            _rows.Add(Press($"Show {Math.Min(PageSize, listed.Count - _shown)} more ({listed.Count - _shown} not shown)", () => { _shown += PageSize; Render(); }));
        Draw(jobs, generation);
    }

    /// <summary>One thing: what it is, its four rungs, its buttons, and the chooser strip when one of its rungs is open.</summary>
    private Border ThingRow(ThingEntry thing, List<RenderJob> jobs)
    {
        var host = _host;
        var style = host.Engine.Settings.Style;
        var recipe = RecipeFor(thing);
        var rungs = thing.Card?.Rungs ?? new RungChoices();
        var meetings = host.Pictures.Meetings(thing.Name);
        var climbs = host.Pictures.Climbing(thing.Name);
        var cap = host.Pictures.Cap(thing.Name, style);
        var hasPicture = thing.InPlay || thing.Kept.Count > 0;
        var now = hasPicture ? host.Pictures.Settled(thing.Name, climbs ? PictureLibrary.StageFor(meetings) : PictureStage.Photo, style) : PictureStage.Shape;

        var column = new VerticalStackLayout { Spacing = 8 };
        column.Add(new Label { Text = thing.Name + (thing.Category is { } kind ? $" ({KindName(kind).ToLowerInvariant()})" : ""), FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Ink });
        column.Add(new Label { Text = Story(thing, meetings, climbs, now), FontSize = 13, TextColor = Ink });
        if (thing.InPlay && thing.Picture?.Check is { Passed: false } failed)
            column.Add(new Label { Text = $"Checked by {failed.Model}: looks WRONG, it saw {failed.Saw}", FontSize = 13, TextColor = Wrong });

        var cells = new HorizontalStackLayout { Spacing = 8 };
        foreach (var stage in Rungs)
        {
            var choice = rungs.For(stage);
            var image = RungImage(FileFor(thing, choice), recipe, stage, style, jobs);
            // A rung the calm level does not reach, or one that is skipped, is shown pale: play never draws it.
            if (stage > cap || choice.Kind == RungKind.Skip) image.Opacity = 0.3;
            var caption = StageName(stage) + "\n" + (choice.Kind == RungKind.Skip ? "skipped" : stage > cap ? "not at this calm level" : ChoiceName(choice));
            var open = _open == (thing.Slug, stage);
            cells.Add(Cell(image, caption, stage == now && choice.Kind != RungKind.Skip ? Chosen : open ? Ink : Faint, stage == now || open ? 3 : 1, () =>
            {
                _open = open ? null : (thing.Slug, stage);
                Render();
            }));
        }
        var left = new VerticalStackLayout { Spacing = 8 };
        left.Add(cells);

        var buttons = new HorizontalStackLayout { Spacing = 8 };
        // While a fetch for this thing is running or waiting, the button waits too. Each press turns down the photo
        // in play, so pressing again before the list showed the new picture used to turn the new one down as well
        // (the octopus on 2026-11-10: four presses, the good second photo excluded by the third).
        var looking = host.PictureJobs.IsPending(thing.Name);
        var another = Press(looking ? "Looking..." : thing.Picture is null ? "Find a picture" : "Try another", () => TryAnother(thing, recipe));
        another.IsEnabled = !looking;
        buttons.Add(another);
        var adding = _adding.Contains(thing.Slug);
        var add = Press(adding ? "Adding..." : "Add from a file...", () => AddFromFile(thing, recipe));
        add.IsEnabled = !adding;
        buttons.Add(add);
        if (thing.InPlay || thing.Hidden)
        {
            buttons.Add(Press(thing.Hidden ? "Show" : "Hide", () =>
            {
                if (thing.Hidden) host.PictureFolder.Unhide(thing.Slug);
                else host.PictureFolder.Hide(thing.Slug);
                host.Pictures.Invalidate(thing.Name);
                Reload();
            }));
        }
        left.Add(buttons);

        var kindPicker = Choice(
            [$"Kind: as it was filed{((thing.Picture?.Category ?? thing.Wanted?.Category) is { } filed ? $" ({KindName(filed).ToLowerInvariant()})" : "")}", .. Kinds.Select(k => "Kind: " + KindName(k).ToLowerInvariant())],
            thing.Card?.Category is { } byHand ? 1 + Array.IndexOf(Kinds, byHand) : 0, 230,
            i => Change(thing, card => card with { Category = i == 0 ? null : Kinds[i - 1] }));
        var climbPicker = Choice(
            [$"Ladder: as the toy does ({(host.Pictures.Climbs(thing.Name) ? "climbs" : "real at once")})", "Ladder: climbs rung by rung", "Ladder: real at once"],
            (int)(thing.Card?.Climb ?? ClimbChoice.Auto), 270,
            i => Change(thing, card => card with { Climb = (ClimbChoice)i }));
        left.Add(new HorizontalStackLayout { Spacing = 8, Children = { kindPicker, climbPicker } });
        // The words sit beside the ladder; the chooser strip, when open, runs under both.
        column.Add(new HorizontalStackLayout { Spacing = 20, Children = { left, WordsPanel(thing) } });

        if (_open is { } o && o.Slug == thing.Slug) column.Add(Chooser(thing, recipe, o.Stage, style, jobs));

        return new Border
        {
            Stroke = new SolidColorBrush(Faint), StrokeThickness = 1, Padding = 12, BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = column,
        };
    }

    /// <summary>The strip under a row for one rung: everything that can be drawn there, each as it would look.</summary>
    private VerticalStackLayout Chooser(ThingEntry thing, Recipe recipe, PictureStage stage, ArtStyle style, List<RenderJob> jobs)
    {
        var current = (thing.Card?.Rungs ?? new RungChoices()).For(stage);
        var options = new HorizontalStackLayout { Spacing = 8 };

        void Offer(RungChoice choice, string caption, Image? image, bool doubted = false)
        {
            if (!RungChoices.Allows(stage, choice)) return;
            var picked = choice == current;
            options.Add(Cell(image, caption, picked ? Chosen : Faint, picked ? 3 : 1, () => Choose(thing, stage, choice), doubted ? Wrong : Ink));
        }

        Offer(RungChoice.Auto, stage == PictureStage.Shape ? "Left to the toy" : thing.InPlay ? "Left to the toy:\nthe picture in play" : "Left to the toy:\nno picture in play",
            RungImage(FileFor(thing, RungChoice.Auto), recipe, stage, style, jobs));
        if (stage != PictureStage.Shape)
        {
            foreach (var picture in thing.Choosable)
            {
                var inPlay = thing.InPlay && thing.Picture?.Id == picture.Id;
                var wrong = picture.Check is { Passed: false };
                Offer(RungChoice.OfPicture(picture.Id),
                    $"Picture {picture.Id}{(inPlay ? ", in play" : "")}\n{(wrong ? "the check says wrong" : Short(picture.CreditLine))}",
                    RungImage(_host.PictureFolder.PathOf(thing.Slug, picture.Id), recipe, stage, style, jobs), wrong);
            }
            Offer(RungChoice.Shape, "The drawn shape", RungImage(null, recipe, stage, style, jobs));
        }
        Offer(RungChoice.Skip, "Skip this rung", null);

        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = $"{StageName(stage)} rung of {thing.Name}: tap what is drawn there.", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ink },
                new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = options },
            },
        };
    }

    /// <summary>The thing's word in each language in use: a box to type it in, and buttons to record it, hear it
    /// and delete the recording. The toy's own word for a starter thing is shown until a grown-up writes another.</summary>
    private VerticalStackLayout WordsPanel(ThingEntry thing)
    {
        var panel = new VerticalStackLayout { Spacing = 4, WidthRequest = 360 };
        panel.Add(new Label { Text = "Its word, written and spoken", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Ink });
        var starter = StarterConcepts.All.FirstOrDefault(c => c.Id == thing.Slug);
        var weights = _host.Engine.Settings.LanguageWeights;
        foreach (var (tag, name) in LanguageNames)
        {
            // A language never picked for play needs no word; all three show when none has a weight.
            if (weights.GetValueOrDefault(tag) <= 0 && weights.Values.Any(w => w > 0)) continue;
            var toyWord = starter?.WordIn(tag);
            var id = ClipCatalog.ThingClip(tag, thing.Slug);
            var word = new Entry { Text = thing.Card?.WordIn(tag) ?? toyWord ?? "", Placeholder = name, WidthRequest = 120, FontSize = 14, TextColor = Ink, BackgroundColor = Colors.White };
            word.Completed += (_, _) => SaveWord(thing, tag, word.Text, toyWord);
            word.Unfocused += (_, _) => SaveWord(thing, tag, word.Text, toyWord);
            var record = Small("● Record");
            var play = Small("▶");
            var delete = Small("Delete");
            var level = new ProgressBar { WidthRequest = 130, ProgressColor = Colors.OrangeRed, IsVisible = false };

            void Show()
            {
                var recorded = _host.Clips.Exists(id);
                var recording = _recording == id;
                record.Text = recording ? "■ Stop" : recorded ? "● Again" : "● Record";
                play.IsVisible = recorded && !recording;
                delete.IsVisible = recorded && !recording;
                level.IsVisible = recording;
                if (!recording) level.Progress = 0;
            }

            record.Clicked += (_, _) => ToggleRecording(id, Show, level);
            play.Clicked += (_, _) => PlayClip(id);
            delete.Clicked += (_, _) =>
            {
                try
                {
                    _host.Clips.Delete(id);
                    _host.Speech.Invalidate(id);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("recording", $"the recording {id} could not be deleted", e);
                    Say($"The recording could not be deleted: {e.Message}");
                }
                Show();
            };
            // A list drawn again while this word is being recorded hands the recording on to its new buttons.
            if (_recording == id)
            {
                _recordingShow = Show;
                _recordingLevel = level;
            }
            Show();
            panel.Add(new Label { Text = name, FontSize = 12, TextColor = Ink });
            panel.Add(new HorizontalStackLayout { Spacing = 6, Children = { word, record, play, delete } });
            panel.Add(level);
        }
        return panel;
    }

    /// <summary>Writes the word typed for a thing to its card, when it differs from what the card has. The toy's
    /// own word for a starter thing, typed back, is no word of the grown-ups'. The list is not drawn again, so
    /// the box being typed in keeps its place.</summary>
    private void SaveWord(ThingEntry thing, string tag, string? text, string? toyWord)
    {
        var word = string.IsNullOrWhiteSpace(text) || text.Trim() == toyWord ? null : text.Trim();
        try
        {
            var cards = _host.PictureFolder.Cards;
            var card = cards.Get(thing.Slug) ?? new ThingCard(thing.Slug, thing.Name);
            if (card.WordIn(tag) == word) return;
            cards.Save(card.WithWord(tag, word), DateTime.Now);
            Log.Info("pictures", $"a grown-up wrote the {tag} word for {thing.Name}: {word ?? "(none)"}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"the word for {thing.Name} could not be saved", e);
            Say($"The word for {thing.Name} could not be saved: {e.Message}");
            return;
        }
        Read();
    }

    /// <summary>Starts recording a word, or stops and keeps it. Starting one while another is being recorded
    /// stops and keeps that one first.</summary>
    private void ToggleRecording(ClipId id, Action show, ProgressBar level)
    {
        if (_mic.Recording)
        {
            var same = _recording == id;
            FinishRecording();
            if (same) return;
        }
        _host.Mixer.StopSpeech();
        try
        {
            _mic.Start();
        }
        catch (Exception e)
        {
            Log.Warn("recording", "could not open the microphone", e);
            Say("Could not open the microphone: " + e.Message + " (check Windows privacy settings for the microphone).");
            return;
        }
        Say(null);
        _recording = id;
        _recordingShow = show;
        _recordingLevel = level;
        show();
    }

    /// <summary>Stops the microphone and keeps what was said, trimmed and levelled, as the clip being recorded;
    /// plays it back.</summary>
    private void FinishRecording()
    {
        var raw = _mic.Stop();
        var id = _recording;
        var show = _recordingShow;
        _recording = null;
        _recordingShow = null;
        _recordingLevel = null;
        if (id is null) return;
        var clip = ClipProcessing.TrimAndNormalize(raw, MicRecorder.SampleRate);
        if (clip.Length == 0)
        {
            Say("Didn't hear anything. Try again a little closer to the microphone.");
        }
        else
        {
            try
            {
                _host.Clips.Save(id, s => WavFile.Write(s, clip, MicRecorder.SampleRate));
                _host.Speech.Invalidate(id);
                _host.Mixer.Speak(ClipProcessing.Resample(clip, MicRecorder.SampleRate, Mixer.SampleRate));
                Say(null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("recording", $"the recording {id} could not be saved", e);
                Say($"The recording could not be saved: {e.Message}");
            }
        }
        show?.Invoke();
    }

    private void PlayClip(ClipId id)
    {
        try
        {
            using var s = _host.Clips.OpenRead(id);
            var (mono, rate) = WavFile.Read(s);
            _host.Mixer.Speak(ClipProcessing.Resample(mono, rate, Mixer.SampleRate));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log.Warn("recording", $"the recording {id} could not be played", e);
            Say($"The recording could not be played: {e.Message}");
        }
    }

    private void Choose(ThingEntry thing, PictureStage stage, RungChoice choice)
    {
        _open = null;
        Change(thing, card => card with { Rungs = card.Rungs.With(stage, choice) });
    }

    /// <summary>Writes a change to the thing's card and draws the list again.</summary>
    private void Change(ThingEntry thing, Func<ThingCard, ThingCard> change)
    {
        try
        {
            var cards = _host.PictureFolder.Cards;
            cards.Save(change(cards.Get(thing.Slug) ?? new ThingCard(thing.Slug, thing.Name)), DateTime.Now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"the choice for {thing.Name} could not be saved", e);
            Say($"The choice for {thing.Name} could not be saved: {e.Message}");
            return;
        }
        Say(null);
        _host.Pictures.Invalidate(thing.Name);
        Reload();
    }

    /// <summary>Asks for another photo of the thing, turning down the one in play; the old one is kept to choose from.</summary>
    private void TryAnother(ThingEntry thing, Recipe recipe)
    {
        var host = _host;
        if (host.PictureJobs.IsPending(thing.Name)) return;
        var category = thing.Category ?? ToyHost.CategoryOf(recipe);
        var now = DateTime.Now;
        try
        {
            host.PictureFolder.Wanted.Add(thing.Name, category, now);
            if (thing.Picture is { } picture)
                host.PictureFolder.Wanted.Bump(thing.Slug, "another was asked for", now, countTry: false, exclude: picture.Photo?.FileTitle);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"could not ask for another picture of {thing.Name}", e);
            Say($"Could not write the wanted list: {e.Message}");
            return;
        }
        Say(host.PictureJobs.Enqueue(thing.Name, category, again: true) || host.PictureJobs.IsPending(thing.Name) ? null
            : $"{thing.Name} is on the wanted list, but nothing is being fetched: fetching pictures from the internet is switched off (the switch above counts once this page has been closed).");
        Reload();
    }

    /// <summary>Lets a grown-up pick a picture file for the thing; it is cut out if it has a background and put in play.</summary>
    private async void AddFromFile(ThingEntry thing, Recipe recipe)
    {
        // An async void method's exception would go straight to the app's last-chance handler; caught here instead.
        try
        {
            var file = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync(new Microsoft.Maui.Storage.PickOptions { PickerTitle = $"A picture of {thing.Name}", FileTypes = Microsoft.Maui.Storage.FilePickerFileType.Images });
            if (file is null) return;
            _adding.Add(thing.Slug);
            Say(null);
            Render();
            var host = _host;
            var category = thing.Category ?? ToyHost.CategoryOf(recipe);
            var result = await Task.Run(() =>
                HandPictures.Add(host.PictureFolder, host.CutOut, thing.Name, category, File.ReadAllBytes(file.FullPath), Path.GetExtension(file.FullPath), DateTime.Now));
            Log.Info("pictures", $"a grown-up added {Path.GetFileName(file.FullPath)} as a picture of {thing.Name}: {result.Outcome}{(result.Reason is { } why ? ", " + why : "")}");
            Say(result.Outcome switch
            {
                HandOutcome.Added => null,
                HandOutcome.NotAPicture => $"{Path.GetFileName(file.FullPath)} is not a picture the toy can read (it reads PNG, JPEG and WebP).",
                HandOutcome.NeedsCutOutModel => "That picture has a background, and cutting it out needs the cut-out models: download them with the button above, or pick a picture with a clear background.",
                _ => $"The thing could not be cut out of that picture ({result.Reason}). A picture of one whole {thing.Name} on a plain background works best.",
            });
        }
        catch (Exception e)
        {
            Log.Error("pictures", $"adding a picture of {thing.Name} from a file failed", e);
            Say($"The picture could not be added: {e.GetBaseException().Message}");
        }
        _adding.Remove(thing.Slug);
        _host.Pictures.Invalidate(thing.Name);
        Reload();
    }

    private void Say(string? note)
    {
        _note.Text = note ?? "";
        _note.IsVisible = note is not null;
    }

    /// <summary>What is known of the thing, in a line or two: how far up its ladder it is, where its picture came
    /// from, and why it has none.</summary>
    private static string Story(ThingEntry thing, int meetings, bool climbs, PictureStage now)
    {
        var lines = new List<string>
        {
            $"Met {meetings} {(meetings == 1 ? "time" : "times")}; {(climbs ? "climbs the ladder" : "real at once")}; drawn now as {StageName(now).ToLowerInvariant()}.",
        };
        if (thing.Picture is { } picture)
            lines.Add($"Picture {picture.Id}{(thing.Hidden ? ", hidden" : "")}: {picture.CreditLine}{(picture.Photo?.DescriptionUrl is { } url ? ", " + url : "")}");
        else if (thing.Kept.Count == 0) lines.Add("No picture.");
        if (thing.Kept.Count > 0) lines.Add($"{thing.Kept.Count} kept from before.");
        if (thing.Wanted is { } w)
            lines.Add($"Wanted since {w.Since:d MMM}{(w.Reason is { } r ? ", " + r : "")}{(w.Tries > 0 ? $", tried {w.Tries}" : "")}.");
        return string.Join("\n", lines);
    }

    /// <summary>The picture file a rung with this choice is made from; null for the drawn shape.</summary>
    private string? FileFor(ThingEntry thing, RungChoice choice)
    {
        var inPlay = thing.InPlay ? _host.PictureFolder.PicturePath(thing.Slug) : null;
        return choice.Kind switch
        {
            RungKind.Shape => null,
            RungKind.Picture => _host.PictureFolder.PathOf(thing.Slug, choice.Picture) ?? inPlay,
            _ => inPlay,
        };
    }

    /// <summary>The recipe a thing is drawn by, for its shape and its colour: the one a key makes now, else the
    /// toy's own of that name, else any, since a thing no key makes any more has left no recipe behind.</summary>
    private Recipe RecipeFor(ThingEntry thing)
    {
        foreach (var recipe in _host.World.Families.All)
            if (Slugs.For(recipe.Name) == thing.Slug) return recipe;
        foreach (var entity in _host.World.Live)
            if (entity.Recipe is { } recipe && Slugs.For(recipe.Name) == thing.Slug) return recipe;
        if (BuiltInRecipes.Concepts.TryGetValue(thing.Slug, out var concept)) return concept;
        return BuiltInRecipes.Palette.FirstOrDefault(r => Slugs.For(r.Name) == thing.Slug) ?? BuiltInRecipes.Palette[0] with { Name = thing.Name };
    }

    /// <summary>An image for one rung: filled at once when that rung was drawn before, otherwise when its turn
    /// on the background thread comes.</summary>
    private Image RungImage(string? file, Recipe recipe, PictureStage stage, ArtStyle style, List<RenderJob> jobs)
    {
        var image = new Image { WidthRequest = CellSide, HeightRequest = CellSide, Aspect = Aspect.AspectFit };
        // The file's time is in the key: a picture keeps its name when another takes its place.
        var stamp = file is null ? 0 : File.GetLastWriteTimeUtc(file).Ticks;
        var key = $"{file}|{stamp}|{stage}|{_host.Engine.Settings.Calm}|{recipe.Name}|{recipe.Body.Hue}";
        if (_rendered.TryGetValue(key, out var png))
        {
            byte[] bytes = png;
            image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        else
        {
            jobs.Add(new RenderJob(key, file, recipe, stage, style, image));
        }
        return image;
    }

    /// <summary>Draws the rungs not drawn before, one at a time off the UI thread, and stops when the list has been
    /// drawn again since.</summary>
    private void Draw(List<RenderJob> jobs, int generation)
    {
        if (jobs.Count == 0) return;
        _ = Task.Run(async () =>
        {
            var done = new Dictionary<string, byte[]>();
            foreach (var job in jobs)
            {
                if (generation != Volatile.Read(ref _generation)) return;
                if (!done.TryGetValue(job.Key, out var png))
                {
                    await _drawing.WaitAsync();
                    try
                    {
                        var render = PictureLibrary.RenderRung(job.File, job.Recipe, job.Stage, job.Style, RenderSide);
                        using var image = render.Image;
                        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                        png = data.ToArray();
                    }
                    catch (Exception e)
                    {
                        // One rung that cannot be drawn (a broken file) leaves its cell empty; the rest go on.
                        Log.Warn("pictures", $"the {job.Stage} rung of {job.Recipe.Name} could not be drawn for the grown-ups' page", e);
                        continue;
                    }
                    finally
                    {
                        _drawing.Release();
                    }
                    done[job.Key] = png;
                }
                byte[] bytes = png;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _rendered[job.Key] = bytes;
                    job.Into.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                });
            }
        });
    }

    /// <summary>A bordered square with a picture (or none) over a caption, that does something when tapped.</summary>
    private static Border Cell(Image? image, string caption, Color stroke, double thickness, Action tapped, Color? captionColour = null)
    {
        var inner = new VerticalStackLayout { Spacing = 4, WidthRequest = CellSide + 8 };
        View picture = image is not null ? image : new BoxView { WidthRequest = CellSide, HeightRequest = CellSide, Color = Colors.Transparent };
        inner.Add(picture);
        inner.Add(new Label { Text = caption, FontSize = 12, TextColor = captionColour ?? Ink, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap });
        var cell = new Border
        {
            Stroke = new SolidColorBrush(stroke), StrokeThickness = thickness, Padding = 6, BackgroundColor = Color.FromArgb("#FFF8EC"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Content = inner,
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => tapped();
        cell.GestureRecognizers.Add(tap);
        return cell;
    }

    private static Button Press(string text, Action action)
    {
        var b = new Button { Text = text, Padding = new Thickness(14, 8), FontSize = 13, HorizontalOptions = LayoutOptions.Start };
        b.Clicked += (_, _) => action();
        return b;
    }

    private static Button Small(string text) => new() { Text = text, Padding = new Thickness(10, 6), FontSize = 13 };

    /// <summary>A drop-down with explicit colours, so it reads whatever the Windows theme is. The change is acted
    /// on after the event has returned, since acting on it draws the list again and takes the drop-down away.</summary>
    private Picker Choice(IReadOnlyList<string> items, int selected, double width, Action<int> changed)
    {
        var picker = new Picker { WidthRequest = width, FontSize = 13, TextColor = Ink, BackgroundColor = Colors.White };
        foreach (var item in items) picker.Items.Add(item);
        picker.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        var was = picker.SelectedIndex;
        picker.SelectedIndexChanged += (_, _) =>
        {
            var index = picker.SelectedIndex;
            if (index < 0 || index == was) return;
            was = index;
            Dispatcher.Dispatch(() => changed(index));
        };
        return picker;
    }

    private static string StageName(PictureStage stage) => stage switch
    {
        PictureStage.Shape => "Shape",
        PictureStage.Silhouette => "Silhouette",
        PictureStage.Flat => "Flat colours",
        _ => "Photo",
    };

    private static string ChoiceName(RungChoice choice) => choice.Kind switch
    {
        RungKind.Picture => $"picture {choice.Picture}",
        RungKind.Shape => "the drawn shape",
        RungKind.Skip => "skipped",
        _ => "left to the toy",
    };

    private static string KindName(RealCategory kind) => kind == RealCategory.ExtinctAnimal ? "Extinct animal" : kind.ToString();

    private static string Short(string text) => text.Length <= 34 ? text : text[..33] + "…";

    private sealed record RenderJob(string Key, string? File, Recipe Recipe, PictureStage Stage, ArtStyle Style, Image Into);
}
