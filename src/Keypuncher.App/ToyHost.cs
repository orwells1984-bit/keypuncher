using System.Collections.Concurrent;
using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Rendering;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;
using Keypuncher.Language;
using Keypuncher.Pictures;
using Keypuncher.Platform.Windows;
using Keypuncher.Storage;
using Keypuncher.Toy;

namespace Keypuncher.App;

public enum HostMode { Play, Parent, Recording }

/// <summary>
/// Owns everything that lives as long as the app: the world and its file, the mixer and the sound
/// card, the recordings, the settings, the keyboard hook, the Sticky Keys shortcuts being off and the screen kept on in play. Pages borrow it.
/// </summary>
/// <remarks>
/// Data lives in %LOCALAPPDATA%\Keypuncher, or in the folder named by the KEYPUNCHER_DATA environment
/// variable, so the child's account and a parent's account can share one set of recordings.
/// The director talks to Ollama at OLLAMA_HOST (default http://localhost:11434) with the model named by
/// KEYPUNCHER_MODEL (default qwen3:4b); with no Ollama running, the built-in families simply stay.
/// </remarks>
public sealed class ToyHost : IDisposable
{
    private readonly ConcurrentQueue<KeyPress> _keys = new();
    private readonly SettingsStore _settingsStore;
    private readonly AccessibilityShortcuts _shortcuts;
    private readonly LockShortcut _lock;
    private readonly KeepAwake _keepAwake = new();
    private readonly OnnxCutOut[] _cutOuts;
    private readonly OllamaLanguageModel _author;
    private readonly CancellationTokenSource _stopChecks = new();
    private Task? _checking;
    private volatile bool _quitting;
    private volatile HostMode _mode = HostMode.Play;

    /// <summary>Where the toy keeps its data: KEYPUNCHER_DATA if set, otherwise %LOCALAPPDATA%\Keypuncher.</summary>
    public static string ResolveDataFolder() =>
        Environment.GetEnvironmentVariable("KEYPUNCHER_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keypuncher");

    public ToyHost()
    {
        var starting = System.Diagnostics.Stopwatch.StartNew();
        DataFolder = ResolveDataFolder();
        Directory.CreateDirectory(DataFolder);
        _directorLog = Path.Combine(DataFolder, DirectorLogName);

        _settingsStore = new SettingsStore(Path.Combine(DataFolder, "settings.json"));
        Clips = new ClipStore(Path.Combine(DataFolder, "recordings"));
        Speech = new SpeechLibrary(Clips, new WindowsVoices());
        WorldStore = new WorldStore(Path.Combine(DataFolder, "world.db"));
        var loading = System.Diagnostics.Stopwatch.StartNew();
        WorldStore.Load(World);
        Log.Info("world", $"loaded in {loading.ElapsedMilliseconds} ms: {World.Live.Count} things on screen, "
            + $"{World.Families.Restyled().Count()} key families changed by the model, scene {World.Scene}");
        // What each changed key draws, so a run can be read without the world file: its shape, colour and size.
        foreach (var (slot, recipe, _) in World.Families.Restyled())
            Log.Info("world", $"key family {slot} is {recipe.Name}: {recipe}");
        // The toy's own things ease in over many plays; a real thing the director picked shows its picture at once.
        Pictures = new PictureLibrary(Path.Combine(DataFolder, PicturesName)) { Climbs = BuiltInRecipes.IsBuiltIn };

        try
        {
            Audio = new AudioOutput(Mixer);
            Log.Info("audio", "sound card opened");
        }
        catch (Exception e)
        {
            AudioError = e.Message;
            Log.Error("audio", "could not open the sound card; the toy plays silently", e);
        }

        var settings = _settingsStore.Load();
        Log.Info("settings", $"loaded: {settings}");
        Engine = new ToyEngine(World, Mixer, Speech, settings, clock: () => DateTime.Now);
        Keyboard = new KeyboardHook(OnKey, () => WindowHandle) { ShouldSwallow = ShouldSwallow };
        _shortcuts = new AccessibilityShortcuts();
        _lock = new LockShortcut();
        Log.Info("windows", $"keyboard hook installed; Win+L {(_lock.Active ? "turned off for this run" : "left as it was")}");

        var ollama = new Uri(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? (host.Contains("://") ? host : "http://" + host)
            : "http://localhost:11434");
        var model = Environment.GetEnvironmentVariable("KEYPUNCHER_MODEL") is { Length: > 0 } m ? m : DefaultModel;
        var author = new OllamaLanguageModel(ollama, model, role: "author");
        var checker = new OllamaLanguageModel(ollama, model, temperature: 0, role: "checker");
        Director = new DirectorLoop(
            new RealThingDirector(author, new FactChecker(checker)),
            scenes: new SceneDirector(author, new PlaceChecker(checker)));
        OllamaAddress = ollama;
        Director.Noted += WriteDirectorLog;
        // A new thing appears once on its own as soon as it is chosen; before, it waited for its key, and a whole
        // play could pass without that key being pressed again.
        Director.Restyled += (_, recipe) => Engine.Show(recipe);
        Story($"started; model {model} at {ollama}");
        _author = author;
        if (Engine.Settings.Director) Task.Run(() => WarmUpAsync(author)).LogFailures("ollama", "warming up the model");
        else Log.Info("director", "model changes are off in the settings; the model is not loaded");

        // Pictures of real things: fetched in the background as the director picks them, cut out on the CPU, and
        // handed to the renderer; the toy never waits on any of it. The two cut-out models are only used when a
        // grown-up has downloaded them; until then the photo is kept for later.
        PictureFolder = new PictureFolder(Path.Combine(DataFolder, PicturesName));
        Models = new ModelStore(DataFolder);
        _cutOuts = [new OnnxCutOut(OnnxCutOut.ISNet, Models.Folder), new OnnxCutOut(OnnxCutOut.BiRefNetLite, Models.Folder)];
        var fetcher = new PhotoFetcher(PictureFolder, new ToyNameFinder(new WikimediaPhotoFinder(PictureHttp.NewClient())), new CutOutChain(_cutOuts), note: NotePicture);
        PictureJobs = new PictureJobs(PictureFolder, fetcher, enabled: () => Engine.Settings.Pictures, note: NotePicture);
        PictureJobs.Landed += name => MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(name));
        Director.Picked += (recipe, category) => PictureJobs.Enqueue(recipe.Name, category);
        // Things picked before there were pictures, or whose fetch was lost, are asked for now; the wanted list
        // remembers everything that was tried and why.
        foreach (var (_, recipe, _) in World.Families.Restyled())
        {
            PictureJobs.Enqueue(recipe.Name, CategoryOf(recipe), categoryGuessed: true);
            // A grown form is an animal unless its family is a plant, as the director asks for it.
            if (recipe.GrowsInto is { } grown)
                PictureJobs.Enqueue(grown, recipe.Kind == RecipeKind.Plant ? RealCategory.Plant : RealCategory.Animal, categoryGuessed: true);
        }
        // The built-in animals and dinosaurs too, under their nursery names: the finder knows a rex is a Tyrannosaurus.
        foreach (var animal in BuiltInRecipes.Animals.All)
            PictureJobs.Enqueue(animal.Name, ToyNames.LookUp(animal.Name, RealCategory.Animal).Category);
        PictureJobs.RetryWanted();

        Speech.PreloadAsync(KeyMeanings.Languages.SelectMany(ClipCatalog.For).Select(e => e.Id)).LogFailures("speech", "loading the recordings");
        Log.Info("app", $"ready in {starting.ElapsedMilliseconds} ms; {PictureFolder.Wanted.All().Count} pictures wanted, "
            + $"cut-out models {(_cutOuts.Any(c => c.IsAvailable) ? "present" : "missing")}");
    }

    public const string DefaultModel = "qwen3:4b";
    public const string DirectorLogName = "director.log";
    /// <summary>The folder of realistic cut-out pictures of real things, one per name; see <c>PictureLibrary</c>.</summary>
    public const string PicturesName = "pictures";
    /// <summary>The folder, next to the log, that play and test mode are recorded into.</summary>
    public const string VideosName = "videos";
    /// <summary>The log file is moved aside to director.old.log when it grows past this.</summary>
    private const long DirectorLogLimit = 2_000_000;
    private readonly string _directorLog;

    /// <summary>Set by MauiProgram once the native window exists.</summary>
    public static IntPtr WindowHandle { get; set; }

    public string DataFolder { get; }
    /// <summary>Realistic pictures of real things and how often each has been met; see <see cref="PictureLibrary"/>.</summary>
    public PictureLibrary Pictures { get; }
    public World World { get; } = new();
    public Mixer Mixer { get; } = new();
    public AudioOutput? Audio { get; }
    public string? AudioError { get; }
    public ClipStore Clips { get; }
    public SpeechLibrary Speech { get; }
    public WorldStore WorldStore { get; }
    public ToyEngine Engine { get; }
    public KeyboardHook Keyboard { get; }
    /// <summary>Win+L being off while the toy runs, and the grown-up's one-time permission it may need.</summary>
    public LockShortcut WinL => _lock;
    public DirectorLoop Director { get; }
    public Uri OllamaAddress { get; }
    /// <summary>The pictures folder as the fetcher and the grown-ups' page see it: sidecars, raw photos, the wanted list.</summary>
    public PictureFolder PictureFolder { get; }
    /// <summary>The cut-out models on this computer, and where to get them.</summary>
    public ModelStore Models { get; }
    /// <summary>The background fetching of pictures.</summary>
    public PictureJobs PictureJobs { get; }

    /// <summary>The best guess at what a thing is from its recipe alone, for things picked before the director
    /// said: creatures are animals, plants are plants, and the rest are objects.</summary>
    public static RealCategory CategoryOf(Recipe recipe) => recipe.Kind switch
    {
        RecipeKind.Creature => RealCategory.Animal,
        RecipeKind.Plant => RealCategory.Plant,
        _ => RealCategory.Object,
    };

    /// <summary>
    /// Shows the pictures play has just fetched to the vision model, while the toy sleeps after its goodnight and the
    /// director has stopped choosing. One it fails (a school of fish, a mare and foal, a cloth for a coconut) is taken
    /// out of play and another photo is fetched; see <see cref="PictureChecks"/>. Then the director's model is loaded
    /// again, so a new play straight after does not wait for it. It also runs when the grown-ups' page opens, since
    /// that is how play usually ends. Quit waits for it (up to <see cref="MaxPictureCheckOnQuit"/>); what was not
    /// checked waits for the next time, or the console tool's <c>check</c>.
    /// </summary>
    public void CheckNewPictures()
    {
        if (!Engine.Settings.Pictures || !Engine.Settings.Director || _stopChecks.IsCancellationRequested) return;
        if (_checking is { IsCompleted: false }) return;
        _checking = Task.Run(() => CheckNewPicturesAsync(_stopChecks.Token));
        _checking.LogFailures("pictures", "checking new pictures");
    }

    /// <summary>The longest Quit waits for a picture check under way: about twenty pictures.</summary>
    public static readonly TimeSpan MaxPictureCheckOnQuit = TimeSpan.FromMinutes(2);

    /// <summary>True while the vision model is looking at new pictures.</summary>
    public bool PictureCheckRunning => _checking is { IsCompleted: false };

    /// <summary>Waits for a picture check under way to finish, without loading the director's model again after it,
    /// for at most <paramref name="max"/>. Returns whether it finished.</summary>
    public async Task<bool> FinishPictureCheckAsync(TimeSpan max)
    {
        _quitting = true;
        if (_checking is not { IsCompleted: false } checking) return true;
        Log.Info("pictures", "waiting for the picture check before quitting");
        var finished = await Task.WhenAny(checking, Task.Delay(max)).ConfigureAwait(false) == checking;
        if (!finished) Log.Info("pictures", $"the picture check took longer than {max.TotalMinutes:0} min; the rest wait for next time");
        return finished;
    }

    private async Task CheckNewPicturesAsync(CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = OllamaAddress, Timeout = TimeSpan.FromMinutes(3) };
        var checks = new PictureChecks(PictureFolder, new OllamaVision(http), note: NotePicture);
        var todo = checks.Unchecked();
        if (todo.Count == 0) return;
        Story($"checking {todo.Count} new picture{(todo.Count == 1 ? "" : "s")} with {checks.Model} while the toy sleeps");
        int passed = 0, failed = 0, silent = 0;
        try
        {
            foreach (var picture in todo)
            {
                ct.ThrowIfCancellationRequested();
                var check = await checks.CheckAsync(picture.Slug, ct).ConfigureAwait(false);
                if (check is null)
                {
                    if (++silent >= 2) { Story($"the picture checker ({checks.Model}) is not answering; the rest wait for next time"); break; }
                    continue;
                }
                if (check.Passed) { passed++; continue; }
                failed++;
                var name = picture.Name;
                MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(name));
                PictureJobs.Enqueue(name, picture.Category, again: true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Story($"picture check stopped: the toy is closing ({passed} looked right, {failed} taken out of play so far)");
            return;
        }
        Story($"picture check done: {passed} looked right, {failed} taken out of play, {todo.Count - passed - failed} not checked");
        if (ct.IsCancellationRequested || _quitting) return;
        try { await _author.WarmUpAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { Log.Warn("ollama", "loading the director's model again after the picture check failed; the next pick loads it", e); }
    }

    /// <summary>A line about a picture, stamped, in the director's log.</summary>
    private void NotePicture(string line) => Story(line);

    public HostMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value) Log.Info("app", $"mode {_mode} -> {value}");
            _mode = value;
            // Only play holds the screen on; the grown-ups' and recording pages let the power plan apply.
            _keepAwake.Active = value == HostMode.Play;
            while (_keys.TryDequeue(out _)) { }
            Engine.ResetInput();
        }
    }

    /// <summary>Keys on the recording screen that work as shortcuts (Space, arrows); raised on the UI thread.</summary>
    public event Action<KeyPress>? RecordingKey;

    public void SaveSettings(ToySettings settings)
    {
        Log.Info("settings", $"saved: {settings}");
        Engine.Settings = settings;
        _settingsStore.Save(settings);
    }

    /// <summary>Takes the keys that arrived since the last frame (play mode only).</summary>
    public IEnumerable<KeyPress> DrainKeys()
    {
        while (_keys.TryDequeue(out var k)) yield return k;
    }

    public void SaveWorld()
    {
        if (World.Dirty) WorldStore.Save(World);
        Pictures.Save();
    }

    /// <summary>Shuts everything down. Each step is on its own, so one failing (and being logged) never keeps the
    /// world from being saved or Win+L from being put back.</summary>
    public void Dispose()
    {
        Log.Info("app", "shutting down");
        // A picture check under way stops at once; the process ending does not wait for it.
        Log.Safely("pictures", "stopping the picture check", _stopChecks.Cancel);
        Log.Safely("app", "removing the keyboard hook", Keyboard.Dispose);
        Log.Safely("app", "putting the accessibility shortcuts back", _shortcuts.Dispose);
        Log.Safely("app", "putting Win+L back", _lock.Dispose);
        Log.Safely("app", "letting the screen sleep again", _keepAwake.Dispose);
        Log.Safely("app", "stopping the director", Director.Dispose);
        // The cut-out models are closed only once the worker has stopped: a cut it is still running cannot be
        // interrupted, so closing under it would hold the window; the process ending reclaims them instead.
        Log.Safely("app", "stopping the picture worker", () =>
        {
            if (PictureJobs.Dispose()) foreach (var cutOut in _cutOuts) cutOut.Dispose();
            else Log.Warn("pictures", "the picture worker did not stop within 2 s; its cut-out models are left open");
        });
        Log.Safely("world", "saving the world", SaveWorld);
        Log.Safely("world", "closing world.db", WorldStore.Dispose);
        Log.Safely("pictures", "closing the pictures", Pictures.Dispose);
        Log.Safely("audio", "closing the sound card", () => Audio?.Dispose());
        Story("stopped");
        Log.Flush();
    }

    /// <summary>Loads the model while the toy starts, so the first pick 30 s into play does not also wait for the
    /// load, and writes how long it took (or why it failed) to the log.</summary>
    /// <remarks>Around the load it also logs Ollama's version, the card's free memory before and after, and how much
    /// of the model ended up on the card: a model that spills to the CPU, or a card another job is holding, is the
    /// usual reason for slow or missing picks.</remarks>
    private async Task WarmUpAsync(OllamaLanguageModel model)
    {
        try { Log.Info("ollama", $"Ollama {await model.ServerVersionAsync().ConfigureAwait(false) ?? "(version unknown)"} at {OllamaAddress}"); }
        catch (Exception e) { Log.Warn("ollama", $"Ollama did not answer at {OllamaAddress}; is it running?", e); }
        LogFreeVram("before loading the model");
        try
        {
            var took = await model.WarmUpAsync().ConfigureAwait(false);
            Story($"model loaded in {took.TotalSeconds:0.0} s");
        }
        catch (Exception e)
        {
            // OllamaLanguageModel has logged the whole exception; the director's log gets the short reason.
            Story($"could not load the model: {e.GetBaseException().Message}");
            LogFreeVram("after the failed load");
            return;
        }
        try
        {
            if (await model.LoadedSizeAsync().ConfigureAwait(false) is { } size)
                Log.Info("ollama", $"{model.ModelName} takes {size.Size / 1e9:0.0} GB, {size.SizeVram / 1e9:0.0} GB of it on the card"
                    + (size.SizeVram < size.Size ? $" ({100.0 * size.SizeVram / Math.Max(1, size.Size):0}%): the rest runs on the CPU, several times slower" : " (all of it)"));
            else Log.Warn("ollama", $"{model.ModelName} is not in Ollama's list of loaded models just after loading");
            Log.Info("ollama", $"models loaded now: {string.Join(", ", await model.LoadedModelsAsync().ConfigureAwait(false))}");
        }
        catch (Exception e) { Log.Warn("ollama", "could not ask Ollama what it has loaded", e); }
        LogFreeVram("after loading the model");
    }

    /// <summary>The card's free memory in the log, from nvidia-smi; nothing on a computer without one.</summary>
    private static void LogFreeVram(string when)
    {
        if (Vram.FreeMiB() is { } free) Log.Info("gpu", $"{free} MiB free on the card {when}");
        else Log.Debug("gpu", $"free memory on the card {when}: unknown (no nvidia-smi)");
    }

    /// <summary>A line, stamped, in the director's log: the short story of a play that the grown-ups read. It goes
    /// to the app's log too, with <paramref name="e"/> in full if there is one.</summary>
    public void Story(string line, Exception? e = null)
    {
        Log.Write(e is null ? Core.LogLevel.Info : Core.LogLevel.Warn, "story", line, e);
        AppendDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}");
    }

    private readonly object _directorLogLock = new();

    /// <summary>Appends a line the director stamped to its log file, and to the app's log.</summary>
    private void WriteDirectorLog(string line)
    {
        // The director stamps its lines as "yyyy-MM-dd HH:mm:ss  "; the app's log has its own stamp.
        Log.Info("story", line.Length > 21 && line[4] == '-' && line[19] == ' ' ? line[21..] : line);
        AppendDirectorLog(line);
    }

    /// <summary>Appends a line to the director's log file; a failure to write it never stops play.</summary>
    private void AppendDirectorLog(string line)
    {
        // The warm-up writes from the thread pool; everything else from the UI thread.
        lock (_directorLogLock)
        {
            try
            {
                if (File.Exists(_directorLog) && new FileInfo(_directorLog).Length > DirectorLogLimit)
                    File.Move(_directorLog, Path.ChangeExtension(_directorLog, ".old.log"), overwrite: true);
                File.AppendAllText(_directorLog, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn("log", $"could not write to {_directorLog}", e);
            }
        }
    }

    public static bool IsRecordingShortcut(KeyPress k) => k.ScanCode is 0x39 or 0xE04B or 0xE04D or 0x1C;

    /// <summary>Called on the hook thread.</summary>
    private void OnKey(KeyPress press)
    {
        switch (_mode)
        {
            case HostMode.Play:
                _keys.Enqueue(press);
                break;
            case HostMode.Recording when IsRecordingShortcut(press):
                MainThread.BeginInvokeOnMainThread(() => RecordingKey?.Invoke(press));
                break;
        }
    }

    /// <summary>Called on the hook thread: everything in play, only the shortcuts while recording, nothing
    /// on the parent screen.</summary>
    private bool ShouldSwallow(KeyPress press) => _mode switch
    {
        HostMode.Play => true,
        HostMode.Recording => IsRecordingShortcut(press),
        _ => false,
    };
}
