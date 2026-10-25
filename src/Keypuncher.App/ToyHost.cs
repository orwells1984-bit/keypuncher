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
/// card, the recordings, the settings, the keyboard hook and the Sticky Keys shortcuts being off. Pages borrow it.
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

        Engine = new ToyEngine(World, Mixer, Speech, _settingsStore.Load());
        Keyboard = new KeyboardHook(OnKey, () => WindowHandle) { ShouldSwallow = ShouldSwallow };
        _shortcuts = new AccessibilityShortcuts();

        var ollama = new Uri(Environment.GetEnvironmentVariable("OLLAMA_HOST") is { Length: > 0 } host
            ? (host.Contains("://") ? host : "http://" + host)
            : "http://localhost:11434");
        var model = Environment.GetEnvironmentVariable("KEYPUNCHER_MODEL") is { Length: > 0 } m ? m : DefaultModel;
        Director = new DirectorLoop(new RealThingDirector(
            new OllamaLanguageModel(ollama, model),
            new FactChecker(new OllamaLanguageModel(ollama, model, temperature: 0))));
        OllamaAddress = ollama;

        _ = Speech.PreloadAsync(KeyMeanings.Languages.SelectMany(ClipCatalog.For).Select(e => e.Id));
    }

    public const string DefaultModel = "qwen3:4b";

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
        Director.Dispose();
        SaveWorld();
        WorldStore.Dispose();
        Audio?.Dispose();
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
