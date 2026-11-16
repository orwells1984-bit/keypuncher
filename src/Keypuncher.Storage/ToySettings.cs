using System.Text.Json;
using Keypuncher.Core;

namespace Keypuncher.Storage;

/// <summary>What the parent screen controls.</summary>
/// <param name="LanguageWeights">Share of sessions per language tag; Icelandic leads by default.</param>
/// <param name="UpperCase">Show letters as capitals (the keycaps) or small letters; small letters by default,
/// as Montessori introduces them first and the words that follow a letter are in small letters.</param>
/// <param name="Volume">Master volume 0..1; the mixer caps it lower.</param>
/// <param name="SayWords">After a letter, say a word that starts with it when the child pauses.</param>
/// <param name="Calm">How busy the screen may get and how boldly it is drawn; see <see cref="CalmLimits"/> and <see cref="ArtStyle"/>.</param>
/// <param name="SessionMinutes">Play time before the goodnight: null for the calm level's default, 0 for never.</param>
/// <param name="AnimalNoises">Animals now and then make their noise (moo, woof, roar) when they appear.</param>
/// <param name="Director">The local model now and then turns a key's invented thing into a real one.</param>
/// <param name="DirectorTest">For grown-ups trying the director out: keys and the scene change as fast as the model
/// can answer, each new thing is shown as soon as it is chosen, what was chosen is written at the top of the
/// screen, a new scene fades in over a few seconds, and there is no goodnight. Not for a child's play.</param>
/// <param name="RecordPlay">Record every play session as a small video on this computer, for looking back at what
/// happened alongside the log. Test mode is always recorded.</param>
/// <param name="Pictures">Fetch a photo of each real thing the director picks from the internet, in the background,
/// and cut it out for the toy to draw; off, the things are only noted as wanted.</param>
/// <param name="SayThingWords">Show and say the word a grown-up wrote for a real thing when the thing appears, and
/// let a letter bring such a word; off, the child only meets the toy's starter words.</param>
/// <param name="DirectorModel">The Ollama model that picks and checks real things, as a grown-up chose it; null for
/// the toy's own choice.</param>
/// <param name="PictureCheckModel">The Ollama model that looks at new pictures, as a grown-up chose it; null for the
/// toy's own choice.</param>
/// <param name="RecordCamera">During play, film the child with the webcam and record the microphone, on this computer
/// only, so the child's reactions can be gauged afterwards; while the camera cannot see the child (no face and nothing
/// moving where the child was), the toy shows a small camera picture so a grown-up can move the camera or the child.</param>
/// <param name="Camera">The camera a grown-up chose to film with, by Windows' id for it; null for the first camera
/// Windows lists.</param>
/// <param name="CameraName">The chosen camera's name, to find it by when its id changes (another USB port).</param>
/// <param name="Microphone">The microphone a grown-up chose to record with, by name; null for Windows' default.</param>
public sealed record ToySettings(
    Dictionary<string, double> LanguageWeights,
    bool UpperCase = false,
    float Volume = 0.5f,
    bool SayWords = true,
    CalmLevel Calm = CalmLevel.Toddler,
    int? SessionMinutes = null,
    bool AnimalNoises = true,
    bool Director = true,
    bool DirectorTest = false,
    bool RecordPlay = true,
    bool Pictures = true,
    bool SayThingWords = true,
    string? DirectorModel = null,
    string? PictureCheckModel = null,
    bool RecordCamera = true,
    string? Camera = null,
    string? CameraName = null,
    string? Microphone = null)
{
    public static ToySettings Default => new(new Dictionary<string, double> { ["is"] = 0.7, ["en"] = 0.15, ["bg"] = 0.15 });

    public CalmLimits Limits => DirectorTest
        ? CalmLimits.For(Calm) with { RestyleMinutes = 0, SceneMinutes = 0, SceneFadeSeconds = 3 }
        : CalmLimits.For(Calm);

    public ArtStyle Style => ArtStyle.For(Calm);

    /// <summary>The goodnight comes after this much play; null means never.</summary>
    public TimeSpan? SessionLength => DirectorTest ? null : (SessionMinutes ?? Limits.SessionMinutes) is > 0 and var m ? TimeSpan.FromMinutes(m) : null;
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ToySettings Load()
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<ToySettings>(File.ReadAllText(path), Options) is { } s)
                return s;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged or unreadable file falls back to defaults.
            Log.Warn("settings", $"could not read {path}; using the default settings", e);
        }
        return ToySettings.Default;
    }

    public void Save(ToySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
