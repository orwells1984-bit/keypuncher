using System.Collections.Concurrent;
using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Director;
using Keypuncher.Inference.Ollama;
using Keypuncher.Language;
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
    private volatile HostMode _mode = HostMode.Play;

    public ToyHost()
    {
        DataFolder = Environment.GetEnvironmentVariable("KEYPUNCHER_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Keypuncher");
        Directory.CreateDirectory(DataFolder);

        _settingsStore = new SettingsStore(Path.Combine(DataFolder, "settings.json"));
        Clips = new ClipStore(Path.Combine(DataFolder, "recordings"));
        Speech = new SpeechLibrary(Clips, new WindowsVoices());
        WorldStore = new WorldStore(Path.Combine(DataFolder, "world.db"));
        WorldStore.Load(World);

        try { Audio = new AudioOutput(Mixer); }
        catch (Exception e) { AudioError = e.Message; }

        Engine = new ToyEngine(World, Mixer, Speech, _settingsStore.Load(), clock: () => DateTime.Now);
        Keyboard = new KeyboardHook(OnKey, () => WindowHandle) { ShouldSwallow = ShouldSwallow };
        _shortcuts = new AccessibilityShortcuts();
        _lock = new LockShortcut();

        var ollama = new Uri(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? (host.Contains("://") ? host : "http://" + host)
            : "http://localhost:11434");
        var model = Environment.GetEnvironmentVariable("KEYPUNCHER_MODEL") is { Length: > 0 } m ? m : DefaultModel;
        var author = new OllamaLanguageModel(ollama, model);
        var checker = new OllamaLanguageModel(ollama, model, temperature: 0);
        Director = new DirectorLoop(
            new RealThingDirector(author, new FactChecker(checker)),
            scenes: new SceneDirector(author, new PlaceChecker(checker)));
        OllamaAddress = ollama;
        _directorLog = Path.Combine(DataFolder, DirectorLogName);
        Director.Noted += WriteDirectorLog;
        // A new thing appears once on its own as soon as it is chosen; before, it waited for its key, and a whole
        // play could pass without that key being pressed again.
        Director.Restyled += (_, recipe) => Engine.Show(recipe);
        WriteDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  started; model {model} at {ollama}");
        if (Engine.Settings.Director) _ = WarmUpAsync(author);

        _ = Speech.PreloadAsync(KeyMeanings.Languages.SelectMany(ClipCatalog.For).Select(e => e.Id));
    }

    public const string DefaultModel = "qwen3:4b";
    public const string DirectorLogName = "director.log";
    /// <summary>The folder, next to the log, that play and test mode are recorded into.</summary>
    public const string VideosName = "videos";
    /// <summary>The log file is moved aside to director.old.log when it grows past this.</summary>
    private const long DirectorLogLimit = 2_000_000;
    private readonly string _directorLog;

    /// <summary>Set by MauiProgram once the native window exists.</summary>
    public static IntPtr WindowHandle { get; set; }

    public string DataFolder { get; }
    public World World { get; } = new();
    public Mixer Mixer { get; } = new();
    public AudioOutput? Audio { get; }
    public string? AudioError { get; }
    public ClipStore Clips { get; }
    public SpeechLibrary Speech { get; }
    public WorldStore WorldStore { get; }
    public ToyEngine Engine { get; }
    public KeyboardHook Keyboard { get; }
    public DirectorLoop Director { get; }
    public Uri OllamaAddress { get; }

    public HostMode Mode
    {
        get => _mode;
        set
        {
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
    }

    public void Dispose()
    {
        Keyboard.Dispose();
        _shortcuts.Dispose();
        _lock.Dispose();
        _keepAwake.Dispose();
        Director.Dispose();
        SaveWorld();
        WorldStore.Dispose();
        Audio?.Dispose();
    }

    /// <summary>Loads the model while the toy starts, so the first pick 30 s into play does not also wait for the
    /// load, and writes how long it took (or why it failed) to the log.</summary>
    private async Task WarmUpAsync(OllamaLanguageModel model)
    {
        try
        {
            var took = await model.WarmUpAsync().ConfigureAwait(false);
            WriteDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  model loaded in {took.TotalSeconds:0.0} s");
        }
        catch (Exception e)
        {
            WriteDirectorLog($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  could not load the model: {e.GetBaseException().Message}");
        }
    }

    private readonly object _directorLogLock = new();

    /// <summary>Appends a line to the director's log file; a failure to write it never stops play.</summary>
    public void WriteDirectorLog(string line)
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
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
