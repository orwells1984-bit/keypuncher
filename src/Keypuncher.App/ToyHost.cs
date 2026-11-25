using System.Collections.Concurrent;
using System.Net.Http.Json;
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
/// The director talks to Ollama at OLLAMA_HOST (default http://localhost:11434) with the model a grown-up chose on
/// their page, else the one named by KEYPUNCHER_MODEL, else qwen3:4b; with no Ollama running, the built-in families
/// simply stay. At start-up it asks Ollama whether its models are there (<see cref="ModelsChecked"/>), and the app
/// offers to download a missing one before play.
/// </remarks>
public sealed class ToyHost : IDisposable
{
    private readonly ConcurrentQueue<KeyPress> _keys = new();
    private readonly SettingsStore _settingsStore;
    private readonly AccessibilityShortcuts _shortcuts;
    private readonly LockShortcut _lock;
    private readonly KeepAwake _keepAwake = new();
    private readonly OnnxCutOut[] _cutOuts;
    private readonly OllamaLanguageModel _author, _checker;
    private readonly HttpClient _ollamaHttp;
    private readonly CancellationTokenSource _stopChecks = new();
    private Task? _checking;
    private CancellationTokenSource? _thisCheck;
    private volatile bool _quitting;
    private readonly SdCliSetup? _imageModel;
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
        Log.Info("windows", _shortcuts.Summary);
        _lock = new LockShortcut();
        Log.Info("windows", $"keyboard hook installed; Win+L {(_lock.Active ? "turned off for this run" : "left as it was")}");

        var ollama = new Uri(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? (host.Contains("://") ? host : "http://" + host)
            : "http://localhost:11434");
        var model = PreferredDirectorModel(settings);
        var author = new OllamaLanguageModel(ollama, model, role: "author");
        var checker = new OllamaLanguageModel(ollama, model, temperature: 0, role: "checker");
        (_author, _checker) = (author, checker);
        Director = new DirectorLoop(
            new RealThingDirector(author, new FactChecker(checker)),
            scenes: new SceneDirector(author, new PlaceChecker(checker)));
        OllamaAddress = ollama;
        // Downloads take minutes, so this client has no timeout of its own; the short calls bring theirs.
        _ollamaHttp = new HttpClient { BaseAddress = ollama, Timeout = Timeout.InfiniteTimeSpan };
        Ollama = new OllamaModels(_ollamaHttp);
        // Asked once now, before the window exists, so the app can offer a download instead of a play without the model.
        ModelsChecked = Task.Run(() => CheckModelsAsync());
        Director.Noted += WriteDirectorLog;
        // A new thing appears once on its own as soon as it is chosen; before, it waited for its key, and a whole
        // play could pass without that key being pressed again.
        Director.Restyled += (_, recipe) => Engine.Show(recipe);
        Story($"started; model {model} at {ollama}; pictures checked by {PictureCheckModel}");
        if (Engine.Settings.Director)
        {
            // Another job of the grown-up's that uses Ollama is paused first, and has let go of the card before the
            // toy looks at it or loads its model: the job's pause unloads every model, the toy's too.
            var paused = PauseOtherJob(settings.PauseWhileRunning);
            // The card is looked at before the model is asked for, so the look sees what was there before the toy
            // came; the app warns the grown-up before play when another job holds the card or Ollama.
            var card = CardAtStart = Task.Run(async () =>
            {
                await paused.ConfigureAwait(false);
                return await CheckCardAsync().ConfigureAwait(false);
            });
            Task.Run(async () =>
            {
                await card.ConfigureAwait(false);
                await WarmUpAsync(author, ModelsChecked).ConfigureAwait(false);
            }).LogFailures("ollama", "warming up the model");
        }
        else Log.Info("director", "model changes are off in the settings; the model is not loaded");

        // Pictures of real things: fetched in the background as the director picks them, cut out on the CPU, and
        // handed to the renderer; the toy never waits on any of it. The two cut-out models are only used when a
        // grown-up has downloaded them; until then the photo is kept for later.
        PictureFolder = new PictureFolder(Path.Combine(DataFolder, PicturesName));
        // The grown-ups' choices for each thing's ladder, and the kept pictures a choice can name by number.
        Choices = new ChoiceBook(PictureFolder.Cards);
        Pictures.Choices = Choices.Get;
        Pictures.PictureFile = PictureFolder.PathOf;
        Choices.Changed += slug => MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(slug));
        // The grown-ups' words for things, which play shows and says.
        Words = new WordBook(PictureFolder.Cards);
        Engine.ThingWords = Words.In;
        Models = new ModelStore(DataFolder);
        _cutOuts = [new OnnxCutOut(OnnxCutOut.ISNet, Models.Folder), new OnnxCutOut(OnnxCutOut.BiRefNetLite, Models.Folder)];
        CutOut = new CutOutChain(_cutOuts);
        var fetcher = new PhotoFetcher(PictureFolder, new ToyNameFinder(new WikimediaPhotoFinder(PictureHttp.NewClient())), CutOut, note: NotePicture);
        PictureJobs = new PictureJobs(PictureFolder, fetcher, enabled: () => Engine.Settings.Pictures, note: NotePicture);
        PictureJobs.Landed += name => MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(name));
        Director.Picked += (recipe, category) => PictureJobs.Enqueue(recipe.Name, category);
        // Things picked before there were pictures, or whose fetch was lost, are asked for now; the wanted list
        // remembers everything that was tried and why.
        foreach (var (_, recipe, _) in World.Families.Restyled())
        {
            PictureJobs.Enqueue(recipe.Name, CategoryOf(recipe), categoryGuessed: recipe.Category is null);
            // A grown form is an animal unless its family is a plant, as the director asks for it.
            if (recipe.GrowsInto is { } grown)
                PictureJobs.Enqueue(grown, recipe.Kind == RecipeKind.Plant ? RealCategory.Plant : RealCategory.Animal, categoryGuessed: true);
        }
        // The built-in animals and dinosaurs too, under their nursery names: the finder knows a rex is a Tyrannosaurus.
        foreach (var animal in BuiltInRecipes.Animals.All)
            PictureJobs.Enqueue(animal.Name, ToyNames.LookUp(animal.Name, RealCategory.Animal).Category);
        // Photos fetched under older file-name rules are held to today's before anything is shown or fetched: no
        // model is asked, and what is taken out is on the wanted list for the fetch below.
        Log.Safely("pictures", "holding the photos in play to today's file-name rules", () =>
        {
            var sweep = new PictureChecks(PictureFolder, new OllamaVision(_ollamaHttp, PictureCheckModel), note: NotePicture);
            if (sweep.SweepFileNames() is not { Count: > 0 } taken) return;
            foreach (var name in taken) Pictures.Invalidate(name);
            Story($"{taken.Count} photo{(taken.Count == 1 ? "" : "s")} taken out of play by file name: {string.Join(", ", taken)}");
        });
        PictureJobs.RetryWanted();
        _imageModel = FindImageModel(DataFolder);
        Log.Info("pictures", _imageModel is null
            ? "no image model found (KEYPUNCHER_SD, the data folder's sd, or a keypuncher-sd folder beside the program); things with no usable photo keep their shape"
            : $"image model {SdCliSetup.ModelName} found at {Path.GetDirectoryName(_imageModel.Exe)}; things with no usable photo are drawn while the toy sleeps");

        Speech.PreloadAsync(KeyMeanings.Languages.SelectMany(tag => ClipCatalog.For(tag, Words.In(tag))).Select(e => e.Id)).LogFailures("speech", "loading the recordings");
        Log.Info("app", $"ready in {starting.ElapsedMilliseconds} ms; {PictureFolder.Wanted.All().Count} pictures wanted, "
            + $"cut-out models {(_cutOuts.Any(c => c.IsAvailable) ? "present" : "missing")}");
    }

    public const string DefaultModel = "qwen3:4b";

    private OtherJobPause? _otherJob;
    private bool _watcherStarted;

    /// <summary>Pauses the other job named in the settings, if any, and once the toy holds it paused starts the
    /// watcher that resumes it when the toy's process ends, however it ends. Never fails.</summary>
    private Task PauseOtherJob(OtherJob? job)
    {
        if (job is null || string.IsNullOrWhiteSpace(job.Program)) return Task.CompletedTask;
        var pause = _otherJob = new OtherJobPause(job, Path.Combine(DataFolder, OtherJobPause.LeaseName), new ProcessCommandRunner(), line => Story(line));
        return Task.Run(async () =>
        {
            await pause.PauseAsync().ConfigureAwait(false);
            if (pause.Owned) _watcherStarted = StartWatcher(pause);
        });
    }

    /// <summary>Starts Windows PowerShell, hidden and on its own, to wait for this process to end and then resume the
    /// paused job (see <see cref="OtherJobPause.WatcherScript"/>). False when it could not be started; the toy then
    /// resumes the job itself as it closes.</summary>
    private static bool StartWatcher(OtherJobPause pause)
    {
        try
        {
            using var _ = System.Diagnostics.Process.Start(pause.WatcherStartInfo());
            Log.Info("other job", "watcher started: the paused job is resumed when the toy's process ends");
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn("other job", "could not start the watcher; the toy resumes the paused job itself as it closes, but not if it is killed", e);
            return false;
        }
    }

    /// <summary>At the very end, with the toy finished with Ollama: its own models are dropped from the card so the
    /// other job finds it free, and the job is resumed by the watcher once the process has ended, or here when there
    /// is no watcher.</summary>
    private void ReleaseOtherJob()
    {
        if (_otherJob is not { Owned: true } job) return;
        foreach (var model in new[] { DirectorModel, PictureCheckModel }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var _ = _ollamaHttp.PostAsJsonAsync("api/generate", new { model, keep_alive = 0 }, limit.Token).GetAwaiter().GetResult();
            }
            catch (Exception e) when (e is HttpRequestException or OperationCanceledException or ObjectDisposedException)
            {
                Log.Debug("ollama", $"dropping {model} from the card before resuming the other job failed", e);
            }
        }
        if (_watcherStarted)
        {
            Story($"{job.Name}: the toy has finished with Ollama; it is resumed as the toy's process ends{(job.ShouldResume() ? "" : " (or not, as its pause was changed by someone else)")}");
            return;
        }
        job.ResumeAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    /// <summary>The model for real things: the grown-ups' choice, else KEYPUNCHER_MODEL, else <see cref="DefaultModel"/>.</summary>
    public static string PreferredDirectorModel(ToySettings settings) =>
        settings.DirectorModel is { Length: > 0 } chosen ? chosen
        : Environment.GetEnvironmentVariable("KEYPUNCHER_MODEL") is { Length: > 0 } m ? m : DefaultModel;

    /// <summary>The model that looks at pictures: the grown-ups' choice, else gemma3:4b.</summary>
    public static string PreferredPictureCheckModel(ToySettings settings) =>
        settings.PictureCheckModel is { Length: > 0 } chosen ? chosen : OllamaVision.DefaultModel;

    /// <summary>Ollama's models on this computer, and downloading one.</summary>
    public OllamaModels Ollama { get; }

    /// <summary>The newest look at Ollama's models (the first is started with the toy); never fails.</summary>
    public Task<ModelCheck> ModelsChecked
    {
        get => _modelsChecked;
        private set => _modelsChecked = value;
    }

    private volatile Task<ModelCheck> _modelsChecked = Task.FromResult(ModelCheck.Unreachable);

    /// <summary>The models the start-up look found missing, waiting at most <paramref name="wait"/> for it; none when
    /// Ollama has not answered by then, since play must not wait on it.</summary>
    public IReadOnlyList<string> MissingModelsAtStart(TimeSpan wait)
    {
        var looking = ModelsChecked;
        try { return looking.Wait(wait) ? looking.Result.Missing : []; }
        catch (AggregateException e)
        {
            Log.Warn("ollama", "the start-up look at Ollama's models failed", e);
            return [];
        }
    }

    /// <summary>The look at the card and at what Ollama has loaded that began with the toy, before its own model
    /// was asked for; never fails. With model changes off nothing is looked at.</summary>
    public Task<CardState> CardAtStart { get; } = Task.FromResult(CardState.Unknown);

    /// <summary>Asks the card for its free memory and Ollama for what it has loaded, and writes both to the log. Never throws.</summary>
    public async Task<CardState> CheckCardAsync()
    {
        var state = await Ollama.CardAsync(Vram.FreeMiB).ConfigureAwait(false);
        Log.Info("gpu", $"{(state.FreeMiB is { } free ? $"{free} MiB free on the card" : "the card's free memory is unknown (no nvidia-smi)")}; "
            + (state.OllamaReachable ? $"Ollama has loaded: {(state.Loaded.Count == 0 ? "nothing" : string.Join(", ", state.Loaded.Select(m => $"{m.Name} ({m.SizeVram / 1e9:0.0} GB on the card)")))}" : "Ollama did not say what it has loaded"));
        return state;
    }

    /// <summary>Why the director's model will be slow or silent with the card as <paramref name="state"/> has it, in
    /// words for a grown-up; null when all looks well, nothing is known, or model changes are off.</summary>
    public string? CardTrouble(CardState state)
    {
        if (!Engine.Settings.Director) return null;
        long? size = ModelsChecked is { IsCompletedSuccessfully: true, Result: var known }
            && known.Installed.FirstOrDefault(m => OllamaModels.Same(m.Name, DirectorModel)) is { Size: > 0 } installed ? installed.Size : null;
        // Both of the toy's models count as its own whatever is switched on: Ollama swaps between them itself.
        return state.Trouble(DirectorModel, [DirectorModel, PictureCheckModel], size);
    }

    /// <summary>The trouble the start-up look found, waiting at most <paramref name="wait"/> for it; null when there
    /// is none or the look has not answered by then, since play must not wait on it.</summary>
    public string? CardTroubleAtStart(TimeSpan wait)
    {
        var looking = CardAtStart;
        try
        {
            if (!looking.Wait(wait)) return null;
            var trouble = CardTrouble(looking.Result);
            if (trouble is not null) Story($"warning the grown-up before play: {trouble}");
            return trouble;
        }
        catch (AggregateException e)
        {
            Log.Warn("gpu", "the start-up look at the card failed", e);
            return null;
        }
    }

    /// <summary>Roughly how big a model the toy knows is to download, for the offer; null for any other.</summary>
    public static string? DownloadSize(string model) =>
        OllamaModels.Same(model, DefaultModel) ? "about 2.5 GB"
        : OllamaModels.Same(model, OllamaVision.DefaultModel) ? "about 3.3 GB" : null;

    /// <summary>The model that picks and checks real things now.</summary>
    public string DirectorModel => _author.ModelName;

    /// <summary>The model that looks at new pictures now.</summary>
    public string PictureCheckModel => PreferredPictureCheckModel(Engine.Settings);

    /// <summary>The models the toy needs for what is switched on: the director's, and the picture checker's when
    /// pictures are fetched. With model changes off it needs none.</summary>
    public IReadOnlyList<string> WantedModels =>
        !Engine.Settings.Director ? []
        : Engine.Settings.Pictures ? [DirectorModel, PictureCheckModel] : [DirectorModel];

    /// <summary>Asks Ollama which models it has and which of the toy's are missing, and writes it to the log. Never throws.</summary>
    public async Task<ModelCheck> CheckModelsAsync(bool withCapabilities = false)
    {
        var wanted = WantedModels;
        var check = await Ollama.CheckAsync(wanted, withCapabilities).ConfigureAwait(false);
        if (!check.Reachable) Story($"Ollama did not answer at {OllamaAddress}, so the toy cannot tell whether its models are there");
        else if (check.Missing.Count > 0) Story($"NOT installed in Ollama: {string.Join(", ", check.Missing)}. Ollama has: {(check.Installed.Count == 0 ? "no models" : string.Join(", ", check.Installed.Select(m => m.Name)))}. The grown-ups' page can download them.");
        else Log.Info("ollama", $"models wanted ({string.Join(", ", wanted)}) are installed; Ollama has {string.Join(", ", check.Installed.Select(m => m.Name))}");
        ModelsChecked = Task.FromResult(check);
        return check;
    }

    /// <summary>Downloads a model through Ollama, then looks again at what is there; when it is the director's, it is
    /// loaded so the next play does not wait. Throws what <see cref="OllamaModels.PullAsync"/> throws.</summary>
    public async Task DownloadModelAsync(string name, IProgress<PullProgress>? progress = null, CancellationToken ct = default)
    {
        Story($"downloading {name} through Ollama");
        try { await Ollama.PullAsync(name, progress, ct).ConfigureAwait(false); }
        catch (Exception e)
        {
            Story($"downloading {name} did not finish: {e.GetBaseException().Message}", e);
            throw;
        }
        Story($"{name} downloaded");
        var check = await CheckModelsAsync().ConfigureAwait(false);
        if (Engine.Settings.Director && OllamaModels.Same(name, DirectorModel))
            Task.Run(() => WarmUpAsync(_author, Task.FromResult(check))).LogFailures("ollama", "loading the downloaded model");
    }

    /// <summary>A grown-up chose the model for real things: used from the next request on, remembered in the
    /// settings, and loaded now if it is installed (Ollama drops the one before, as it holds one at a time).</summary>
    public void ChooseDirectorModel(string name)
    {
        if (OllamaModels.Same(name, DirectorModel) && Engine.Settings.DirectorModel is not null) return;
        Story($"a grown-up chose {name} for real things (was {DirectorModel})");
        _author.ModelName = name;
        _checker.ModelName = name;
        SaveSettings(Engine.Settings with { DirectorModel = name });
        if (Engine.Settings.Director)
            Task.Run(async () => await WarmUpAsync(_author, CheckModelsAsync()).ConfigureAwait(false)).LogFailures("ollama", "loading the chosen model");
    }

    /// <summary>A grown-up chose the model that looks at pictures: used from the next check on.</summary>
    public void ChoosePictureCheckModel(string name)
    {
        if (OllamaModels.Same(name, PictureCheckModel) && Engine.Settings.PictureCheckModel is not null) return;
        Story($"a grown-up chose {name} to check pictures (was {PictureCheckModel})");
        SaveSettings(Engine.Settings with { PictureCheckModel = name });
    }
    public const string DirectorLogName = "director.log";
    /// <summary>The folder of realistic cut-out pictures of real things, one per name; see <c>PictureLibrary</c>.</summary>
    public const string PicturesName = "pictures";
    /// <summary>The folder, next to the log, that play and test mode are recorded into.</summary>
    public const string VideosName = "videos";
    /// <summary>The folder, next to the log, that the webcam's video and the microphone's sound of each play go into.</summary>
    public const string CameraName = "camera";
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
    /// <summary>What the grown-ups chose about how each thing is drawn, held in memory for the renderer.</summary>
    public ChoiceBook Choices { get; }
    /// <summary>The words the grown-ups wrote for things, held in memory for play.</summary>
    public WordBook Words { get; }
    /// <summary>The cut-out models on this computer, and where to get them.</summary>
    public ModelStore Models { get; }
    /// <summary>The cut-out models the fetcher uses, for a picture a grown-up adds from their own files.</summary>
    public ICutOut CutOut { get; }
    /// <summary>The background fetching of pictures.</summary>
    public PictureJobs PictureJobs { get; }

    /// <summary>What a thing is: what the director filed it as, or for things picked before it kept that, the best
    /// guess from the recipe alone: a creature drawn as a car or a boat is a vehicle, one drawn as a ball an object,
    /// other creatures animals, plants plants, and the rest objects.</summary>
    public static RealCategory CategoryOf(Recipe recipe) => recipe.Category ?? recipe.Kind switch
    {
        RecipeKind.Creature when recipe.Body.Shape is Core.Shape.Car or Core.Shape.Boat => RealCategory.Vehicle,
        RecipeKind.Creature when recipe.Body.Shape is Core.Shape.Ball => RealCategory.Object,
        RecipeKind.Creature => RealCategory.Animal,
        RecipeKind.Plant => RealCategory.Plant,
        _ => RealCategory.Object,
    };

    /// <summary>
    /// Shows the pictures play has just fetched to the vision model, while the toy sleeps after its goodnight and the
    /// director has stopped choosing. One it fails (a school of fish, a mare and foal, a cloth for a coconut) is taken
    /// out of play and another photo is fetched; see <see cref="PictureChecks"/>. Then the director's model is loaded
    /// again, so a new play straight after does not wait for it. It also runs when the grown-ups' page opens, since
    /// that is how play usually ends. After Quit it carries on behind a small window (see
    /// <see cref="FinishPicturesAfterQuit"/>); what was not checked waits for the next time, or the console tool's <c>check</c>.
    /// </summary>
    public void CheckNewPictures()
    {
        if (!Engine.Settings.Pictures || !Engine.Settings.Director || _stopChecks.IsCancellationRequested) return;
        if (_checking is { IsCompleted: false }) return;
        var thisCheck = CancellationTokenSource.CreateLinkedTokenSource(_stopChecks.Token);
        _thisCheck = thisCheck;
        _checking = Task.Run(async () =>
        {
            try { await CheckNewPicturesAsync(thisCheck.Token).ConfigureAwait(false); }
            finally { thisCheck.Dispose(); }
        });
        _checking.LogFailures("pictures", "checking new pictures");
    }

    /// <summary>
    /// Stops a picture check under way because play is starting; what it had not looked at waits for the goodnight or
    /// the grown-ups' page. Ollama holds one model, so a check left running takes the card from the director at every
    /// picture: on 2026-11-20 a check of 60 pictures started on the grown-ups' page ran on through the next play, the
    /// director's calls took 27 to 64 s (20 to 54 s of each loading its model again) and it made one pick in six
    /// minutes. The director's model is loaded again at once, so the first pick does not wait for it.
    /// </summary>
    public void StopPictureCheckForPlay()
    {
        if (_checking is not { IsCompleted: false }) return;
        Log.Info("pictures", "play is starting; the picture check stops and the rest wait for the goodnight");
        _stoppedForPlay = true;
        Log.Safely("pictures", "stopping the picture check for play", () => { try { _thisCheck?.Cancel(); } catch (ObjectDisposedException) { } });
    }

    private volatile bool _stoppedForPlay;

    /// <summary>The longest the toy stays behind after Quit to finish the pictures. Quit used to wait 15 s and then
    /// stop the check; the checker's model alone takes 40 s to load, so play after play two or so of a dozen new
    /// pictures were looked at (2 of 13 on 2026-11-24) and nothing was ever drawn for the things with no photo.</summary>
    public static readonly TimeSpan MaxFinishing = TimeSpan.FromMinutes(15);

    private volatile bool _finishing;
    private volatile string _pictureWork = "";

    /// <summary>What the picture check is doing now, in a few words, for the window shown after Quit.</summary>
    public string PictureWork => _pictureWork;

    /// <summary>
    /// A grown-up chose Quit while the pictures were still being checked: the toy gives the keyboard, Win+L and the
    /// screen's sleep back at once and saves the world (the Sticky Keys shortcuts stay off until the toy closes), and the check (and the drawing of things with no photo)
    /// carries on. The task ends when the pictures are done or after <see cref="MaxFinishing"/>; the caller then
    /// closes the toy. The director's model is not loaded again afterwards.
    /// </summary>
    public Task FinishPicturesAfterQuit()
    {
        _finishing = true;
        Story("a grown-up chose Quit with the picture check under way; it carries on behind a small window");
        Mode = HostMode.Parent;
        Log.Safely("world", "saving the world", SaveWorld);
        Log.Safely("app", "removing the keyboard hook", Keyboard.Dispose);
        // The Sticky Keys shortcuts stay off until the process ends (Dispose): the child may still be at the keyboard.
        Log.Safely("app", "putting Win+L back", _lock.Dispose);
        Log.Safely("app", "letting the screen sleep again", _keepAwake.Dispose);
        return _checking is { IsCompleted: false } checking ? Task.WhenAny(checking, Task.Delay(MaxFinishing)) : Task.CompletedTask;
    }

    /// <summary>The longest the check waits for the director to finish a pick before it starts: Ollama holds one model,
    /// so a check started under a pick waits for the card and runs out of time.</summary>
    public static readonly TimeSpan MaxWaitForDirector = TimeSpan.FromSeconds(90);

    /// <summary>How many times over the check looks at what came in while it ran (the photos fetched in place of the
    /// ones it turned down), and how long it waits for those fetches each time.</summary>
    public const int MaxCheckRounds = 4;
    public static readonly TimeSpan MaxWaitForReplacements = TimeSpan.FromSeconds(45);

    /// <summary>The most pictures the image model draws in one sleep: each takes the card for half a minute or more.</summary>
    public const int MaxDrawnPerSleep = 4;
    /// <summary>The least free memory on the card, in MiB, at which the image model is started.</summary>
    public const int MinFreeToDraw = 3000;

    /// <summary>Where the image model is, complete: the folder named by KEYPUNCHER_SD, else <c>sd</c> in the data
    /// folder (the console tool's default), else a <c>keypuncher-sd</c> folder beside the program or any folder above
    /// it (beside the clone, when the toy is run from its sources). Null when none has every piece.</summary>
    public static SdCliSetup? FindImageModel(string dataFolder)
    {
        IEnumerable<string> Places()
        {
            if (Environment.GetEnvironmentVariable("KEYPUNCHER_SD") is { Length: > 0 } named) yield return named;
            yield return Path.Combine(dataFolder, "sd");
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                yield return Path.Combine(dir.FullName, "keypuncher-sd");
        }
        try
        {
            return Places().Where(Directory.Exists).Select(SdCliSetup.Find).FirstOrDefault(setup => setup.AllPresent);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn("pictures", "looking for the image model failed", e);
            return null;
        }
    }

    /// <summary>The wanted things the image model should draw this sleep: those the photo sources have given up on
    /// (see <see cref="PictureMaker.IsGap"/>) with nothing in play and no fetch under way, the longest left alone
    /// first, so one the checker keeps turning down does not hold up the rest. A name the encyclopedia has no article
    /// for is left to the console tool: most of those are names the model made up ("viking rose", "lilly pillay"),
    /// and a drawing of something is not a picture of a real thing. Empty without the image model or a cut-out model.</summary>
    private IReadOnlyList<WantedEntry> GapsToDraw()
    {
        if (_imageModel is null || !CutOut.IsAvailable) return [];
        return [.. PictureFolder.Wanted.All()
            .Where(w => PictureMaker.IsGap(w) && !string.Equals(w.Reason, "no article", StringComparison.OrdinalIgnoreCase)
                && !PictureFolder.HasPicture(w.Slug) && !PictureJobs.IsPending(w.Name))
            .OrderBy(w => w.LastTry ?? DateTime.MinValue).Take(MaxDrawnPerSleep)];
    }

    /// <summary>
    /// Draws pictures for things no usable photo was found for, with the image model, while the toy sleeps: laver's
    /// only photo was laverbread on toast, the checker turned it down, and the thing stayed a shape. Ollama is asked
    /// to let go of the toy's own models first, since the image model needs the card to itself; when another job's
    /// model is on the card, or too little of it is free, nothing is drawn. Each drawing waits out of play for the
    /// picture check. Returns how many were drawn.
    /// </summary>
    private async Task<int> DrawGapsAsync(IReadOnlyList<WantedEntry> gaps, CancellationToken ct)
    {
        if (_imageModel is not { } setup || gaps.Count == 0) return 0;
        var card = await Ollama.CardAsync(() => null).ConfigureAwait(false);
        string[] own = [DirectorModel, PictureCheckModel];
        if (card.Loaded.Where(m => !own.Any(o => OllamaModels.Same(m.Name, o))).Select(m => m.Name).ToList() is { Count: > 0 } others)
        {
            Story($"{gaps.Count} picture{(gaps.Count == 1 ? "" : "s")} not drawn: Ollama holds {string.Join(", ", others)} for another job");
            return 0;
        }
        foreach (var mine in card.Loaded)
        {
            // A call with keep_alive 0 and no prompt drops the model from the card at once.
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            try { using var _ = await _ollamaHttp.PostAsJsonAsync("api/generate", new { model = mine.Name, keep_alive = 0 }, limit.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is HttpRequestException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                Log.Warn("ollama", $"asking Ollama to let go of {mine.Name} before drawing failed", e);
            }
        }
        // The card takes a moment to be given back.
        int? free = null;
        for (var i = 0; i < 10; i++)
        {
            free = Vram.FreeMiB();
            if (free is null or >= MinFreeToDraw) break;
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
        if (free is < MinFreeToDraw)
        {
            Story($"{gaps.Count} picture{(gaps.Count == 1 ? "" : "s")} not drawn: only {free} MiB free on the card");
            return 0;
        }
        Story($"drawing {gaps.Count} picture{(gaps.Count == 1 ? "" : "s")} with {SdCliSetup.ModelName} while the toy sleeps ({(free is { } mib ? $"{mib} MiB free on the card" : "the card's free memory is unknown")}): {string.Join(", ", gaps.Select(g => g.Name))}");
        var maker = new PictureMaker(PictureFolder, new SdCli(setup, note: NotePicture), CutOut, note: NotePicture);
        int drawn = 0, failures = 0;
        foreach (var gap in gaps)
        {
            ct.ThrowIfCancellationRequested();
            if (_quitting) break;
            _pictureWork = $"Drawing {gap.Name} (this takes a minute or so)";
            // What the grown-ups or the director filed the thing as wins over the wanted list's older guess. The seed
            // is the picture's number, so a thing drawn again after the checker turned its drawing down is drawn anew.
            var category = PictureFolder.Cards.Get(gap.Slug)?.Category ?? gap.Category;
            var outcome = await maker.MakeAsync(gap.Name, category, seed: PictureFolder.NextId(gap.Slug), ct: ct, hold: true).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (outcome == MakeOutcome.Made) drawn++;
            if (outcome == MakeOutcome.ModelMissing) break;
            // Two that fail one after the other mean the card or the program, not the thing.
            failures = outcome == MakeOutcome.Failed ? failures + 1 : 0;
            if (failures >= 2) break;
        }
        Story($"drawing done: {drawn} of {gaps.Count} drawn; they wait for the picture check");
        return drawn;
    }

    /// <summary>What the model, the director, the picture work and the card are doing now, in one line, for the
    /// drawing's pause watch. Called off the drawing thread; it asks nvidia-smi, which takes a moment.</summary>
    public string WhatIsGoingOn()
    {
        var jobs = PictureJobs.Status;
        var free = Vram.FreeMiB();
        return FormattableString.Invariant($"{OllamaLanguageModel.CallsUnderWay()}; director {(Director.Busy ? "choosing" : "idle")}; ")
            + $"picture check {(PictureCheckRunning ? "running" : "idle")}; {(jobs.Fetching is { } fetching ? $"fetching a picture of {fetching}" : "no picture fetch")}"
            + $"{(jobs.Queued > 0 ? $" ({jobs.Queued} waiting)" : "")}; card {(free is { } mib ? $"{mib} MiB free" : "not read")}; "
            + FormattableString.Invariant($"collections {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)} with {GC.GetTotalPauseDuration().TotalSeconds:0.0} s paused in all; ")
            + $"{ThreadPool.PendingWorkItemCount} waiting for {ThreadPool.ThreadCount} pool threads";
    }

    /// <summary>True while the vision model is looking at new pictures.</summary>
    public bool PictureCheckRunning => _checking is { IsCompleted: false };

    private async Task CheckNewPicturesAsync(CancellationToken ct)
    {
        _stoppedForPlay = false;
        using var http = new HttpClient { BaseAddress = OllamaAddress, Timeout = TimeSpan.FromMinutes(3) };
        var checks = new PictureChecks(PictureFolder, new OllamaVision(http, PictureCheckModel), note: NotePicture);
        var todo = checks.Unchecked();
        var gaps = GapsToDraw();
        if (todo.Count == 0 && gaps.Count == 0) return;
        // A checker that is not installed answers nothing; said once here rather than as two pictures "not answered".
        var models = await Ollama.CheckAsync([checks.Model], cancellationToken: ct).ConfigureAwait(false);
        if (models.Reachable && models.Missing.Count > 0)
        {
            // Without the checker nothing is drawn either: a drawing goes into play only once it has been looked at.
            Story($"{todo.Count} new picture{(todo.Count == 1 ? "" : "s")} not checked{(gaps.Count > 0 ? $" and {gaps.Count} not drawn" : "")}: {checks.Model} is not installed in Ollama (the grown-ups' page can download it)");
            return;
        }
        // The director's model and the checker cannot share the card; a pick under way is let finish first.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Director.Busy && waited.Elapsed < MaxWaitForDirector && !_quitting)
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        if (Director.Busy || _quitting)
        {
            if (todo.Count > 0) Story($"{todo.Count} new picture{(todo.Count == 1 ? "" : "s")} not checked: {(_quitting ? "the toy is closing" : "the director is still choosing")}; they wait for next time");
            return;
        }
        int passed = 0, failed = 0, all = todo.Count;
        var seen = new HashSet<(string Slug, int Id)>();
        var silent = false;
        // A failed picture's replacement is fetched at once and waits out of play for this same check, so the
        // rounds go on until nothing new has come; MaxCheckRounds stops a thing with no good photo at all.
        async Task LookAsync()
        {
            if (todo.Count > 0) Story($"checking {todo.Count} new picture{(todo.Count == 1 ? "" : "s")} with {checks.Model} while the toy sleeps");
            for (var round = 0; todo.Count > 0 && round < MaxCheckRounds; round++)
            {
                foreach (var picture in todo)
                {
                    ct.ThrowIfCancellationRequested();
                    seen.Add((picture.Slug, picture.Id));
                    _pictureWork = $"Looking at {picture.Name} (picture {seen.Count} of {all})";
                    var check = await checks.CheckAsync(picture.Slug, ct).ConfigureAwait(false);
                    if (check is null)
                    {
                        // One that did not answer in its two minutes means the card is taken; the next would wait as long.
                        Story($"the picture checker ({checks.Model}) is not answering; the rest wait for next time");
                        silent = true;
                        break;
                    }
                    var name = picture.Name;
                    if (check.Passed)
                    {
                        passed++;
                        // A picture that was waiting for this goes into play now.
                        if (picture.AwaitsCheck) MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(name));
                        continue;
                    }
                    failed++;
                    MainThread.BeginInvokeOnMainThread(() => Pictures.Invalidate(name));
                    PictureJobs.Enqueue(name, picture.Category, again: true);
                }
                if (silent) break;
                var fetching = System.Diagnostics.Stopwatch.StartNew();
                while (PictureJobs.Busy && fetching.Elapsed < MaxWaitForReplacements)
                    await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
                todo = [.. checks.Unchecked().Where(p => !seen.Contains((p.Slug, p.Id)))];
                all += todo.Count;
            }
        }
        try
        {
            await LookAsync().ConfigureAwait(false);
            // With the photos settled, what still has none is drawn, and the drawings are looked at in their turn.
            // The list is read again: a photo the check has just turned down may have had no other to take its place.
            if (!silent && !_quitting && (gaps = GapsToDraw()).Count > 0 && await DrawGapsAsync(gaps, ct).ConfigureAwait(false) > 0)
            {
                todo = [.. checks.Unchecked().Where(p => !seen.Contains((p.Slug, p.Id)))];
                all += todo.Count;
                await LookAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            var forPlay = _stoppedForPlay && !_stopChecks.IsCancellationRequested && !_quitting;
            Story($"picture check stopped: {(forPlay ? "play has started" : "the toy is closing")} ({passed} looked right, {failed} taken out of play so far)");
            if (!forPlay) return;
            // The checker held the card; the director's model goes back on it now rather than at the first pick.
            try { await _author.WarmUpAsync(_stopChecks.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Log.Warn("ollama", "loading the director's model again after the picture check failed; the next pick loads it", e); }
            return;
        }
        Story($"picture check done: {passed} looked right, {failed} taken out of play, {all - passed - failed} not checked");
        _pictureWork = "";
        // After Quit nothing plays again in this run, so the director's model is not put back on the card.
        if (ct.IsCancellationRequested || _quitting || _finishing) return;
        if (ModelsChecked is { IsCompletedSuccessfully: true, Result: { Reachable: true } known } && !OllamaModels.Has(known.Installed, DirectorModel)) return;
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
        _quitting = true;
        // A picture check under way stops at once; the process ending does not wait for it.
        Log.Safely("pictures", "stopping the picture check", _stopChecks.Cancel);
        Log.Safely("app", "removing the keyboard hook", Keyboard.Dispose);
        Log.Safely("app", "putting the accessibility shortcuts back", _shortcuts.Dispose);
        Log.Safely("app", "putting Win+L back", _lock.Dispose);
        Log.Safely("app", "letting the screen sleep again", _keepAwake.Dispose);
        Log.Safely("app", "stopping the director", Director.Dispose);
        // Everything that asks Ollama has stopped (the pictures after Quit are done by now), so another job the toy
        // paused may have it back.
        Log.Safely("other job", "letting the paused job go on", ReleaseOtherJob);
        Log.Safely("ollama", "closing the connection to Ollama", _ollamaHttp.Dispose);
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
        Log.Safely("pictures", "letting go of the choices", Choices.Dispose);
        Log.Safely("pictures", "letting go of the words", Words.Dispose);
        Log.Safely("audio", "closing the sound card", () => Audio?.Dispose());
        Story("stopped");
        Log.Flush();
    }

    /// <summary>Loads the model while the toy starts, so the first pick 30 s into play does not also wait for the
    /// load, and writes how long it took (or why it failed) to the log.</summary>
    /// <remarks>Around the load it also logs Ollama's version, the card's free memory before and after, and how much
    /// of the model ended up on the card: a model that spills to the CPU, or a card another job is holding, is the
    /// usual reason for slow or missing picks.</remarks>
    private async Task WarmUpAsync(OllamaLanguageModel model, Task<ModelCheck> models)
    {
        // A model that is not installed cannot load; the look at Ollama's list has already said so in the log.
        var check = await models.ConfigureAwait(false);
        if (check.Reachable && !OllamaModels.Has(check.Installed, model.ModelName))
        {
            Story($"the model {model.ModelName} is not installed in Ollama, so nothing is loaded; the toy plays with what it has until it is downloaded");
            return;
        }
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
