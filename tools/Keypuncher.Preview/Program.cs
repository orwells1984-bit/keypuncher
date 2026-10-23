// Plays a scripted session through the real engine, renderer and mixer with no window, and writes
// PNG frames and the audio to a folder. For checking how things look and sound without the app, and
// later for judging what the director's recipes look like.
//
//   dotnet run --project tools/Keypuncher.Preview -- --out preview --keys "kisa 3 mjá" --mash 4
//
// Each character in --keys is one press, 0.35 s apart (a space is a pause); ´ is the accent key.
// --mash N adds N palm mashes at the end. --calm Baby|Toddler|BigToddler picks the calm level (default Toddler). Frames are written after every press and at the end.

using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Rendering;
using Keypuncher.Storage;
using Keypuncher.Toy;
using SkiaSharp;

var outDir = Get("--out", "preview");
var keys = Get("--keys", "sól 3 köttur");
var mashes = int.Parse(Get("--mash", "2"));
var width = int.Parse(Get("--width", "1280"));
var height = int.Parse(Get("--height", "720"));
var calm = Enum.Parse<CalmLevel>(Get("--calm", nameof(CalmLevel.Toddler)), ignoreCase: true);
Directory.CreateDirectory(outDir);

var world = new World();
var mixer = new Mixer();
var speech = new SpeechLibrary(new ClipStore(Path.Combine(outDir, "clips")), null);
var engine = new ToyEngine(world, mixer, speech, ToySettings.Default with { Calm = calm }, new Random(1));
using var renderer = new SceneRenderer(Fonts.LoadAndika());
var audio = new List<float>();
var clock = DateTimeOffset.UnixEpoch;
var frame = 0;

void Advance(double seconds)
{
    const double dt = 1 / 60.0;
    for (var t = 0.0; t < seconds; t += dt)
    {
        engine.Update(dt);
        clock += TimeSpan.FromSeconds(dt);
        var buffer = new float[(int)(Mixer.SampleRate * dt) * Mixer.Channels];
        mixer.Read(buffer);
        audio.AddRange(buffer);
    }
}

void Snapshot(string label)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height));
    renderer.Draw(surface.Canvas, width, height, world, engine.Gate.Challenge is { } c ? $"Grown-up? Type: {c}" : null);
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 90);
    var path = Path.Combine(outDir, $"{frame++:000}-{label}.png");
    using (var f = File.Create(path)) data.SaveTo(f);
    Console.WriteLine(path);
}

void Press(int scan, string? ch, bool dead = false)
{
    engine.OnKey(new KeyPress(clock, scan, ch, dead, true));
    clock += TimeSpan.FromMilliseconds(60);
    engine.OnKey(new KeyPress(clock, scan, ch, dead, false));
}

foreach (var ch in keys)
{
    if (ch == ' ') { Advance(1.0); continue; }
    if (ch == '´') Press(0x28, "´", dead: true);
    else if (IcelandicKeys.ScanCode(ch) is { } scan) Press(scan, ch.ToString());
    else Press(IcelandicKeys.ScanCode(char.ToLowerInvariant(RemoveAccent(ch))) ?? 0x39, ch.ToString());
    Advance(0.35);
    Snapshot(char.IsLetterOrDigit(ch) ? ch.ToString() : "key");
}

for (var m = 0; m < mashes; m++)
{
    foreach (var scan in new[] { 0x1E, 0x1F, 0x20, 0x21, 0x2C })
    {
        engine.OnKey(new KeyPress(clock, scan + (m * 2), null, false, true));
        clock += TimeSpan.FromMilliseconds(8);
    }
    foreach (var scan in new[] { 0x1E, 0x1F, 0x20, 0x21, 0x2C })
        engine.OnKey(new KeyPress(clock, scan + (m * 2), null, false, false));
    Advance(0.25);
    Snapshot("mash");
}

Advance(2);
Snapshot("end");

using (var wav = File.Create(Path.Combine(outDir, "audio.wav")))
{
    // Downmix to mono for a quick listen.
    var mono = new float[audio.Count / 2];
    for (var i = 0; i < mono.Length; i++) mono[i] = (audio[2 * i] + audio[(2 * i) + 1]) / 2;
    WavFile.Write(wav, mono, Mixer.SampleRate);
    Console.WriteLine($"audio: {mono.Length / (double)Mixer.SampleRate:0.0} s, peak {mono.Max(Math.Abs):0.00}");
}
Console.WriteLine($"live {world.Live.Count}, things {world.LiveThingCount}");
return 0;

string Get(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

static char RemoveAccent(char c) => c.ToString().Normalize(System.Text.NormalizationForm.FormD)[0];

/// <summary>Set 1 scancodes for the unshifted Icelandic layout, enough to script a preview.</summary>
static class IcelandicKeys
{
    private static readonly Dictionary<char, int> Map = Build();

    public static int? ScanCode(char c) => Map.TryGetValue(c, out var s) ? s : null;

    private static Dictionary<char, int> Build()
    {
        var map = new Dictionary<char, int>();
        void Row(string chars, int first) { for (var i = 0; i < chars.Length; i++) map[chars[i]] = first + i; }
        Row("1234567890ö-", 0x02);
        Row("qwertyuiopð'", 0x10);
        Row("asdfghjklæ", 0x1E);
        Row("zxcvbnm,.þ", 0x2C);
        return map;
    }
}
