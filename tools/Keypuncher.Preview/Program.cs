// Plays a scripted session through the real engine, renderer and mixer with no window, and writes
// PNG frames and the audio to a folder. For checking how things look and sound without the app, and
// later for judging what the director's recipes look like.
//
//   dotnet run --project tools/Keypuncher.Preview -- --out preview --keys "kisa 3 mjá" --mash 4
//
// Each character in --keys is one press, 0.35 s apart (a space is a pause); ´ is the accent key.
// --mash N adds N palm mashes at the end. --calm Baby|Toddler|BigToddler picks the calm level (default Toddler). Frames are written after every press and at the end.
// --sheet instead writes one contact sheet per calm level of every animal and dinosaur, each in its own small scene at
// the size a word's picture is drawn.
// --scenes instead writes one contact sheet per calm level of every landscape in a few times of day and weathers.
// --tour instead records a video (as test mode does) of the backdrop moving through every landscape, time of day
// and weather, with the toy's real fades, to tour.avi.
// --calls instead writes every animal noise as a WAV, bold and baby (gentle) versions, for a quick listen.

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
if (args.Contains("--sheet")) return Sheet(outDir);
if (args.Contains("--calls")) return Calls(outDir);
if (args.Contains("--scenes")) return Scenes(outDir);
if (args.Contains("--tour")) return Tour(outDir, calm);

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

static int Sheet(string outDir)
{
    const int tileW = 640, tileH = 360, columns = 6;
    var animals = BuiltInRecipes.Animals.All;
    var rows = (animals.Count + columns - 1) / columns;
    using var renderer = new SceneRenderer(Fonts.LoadAndika());
    using var label = new SKFont(Fonts.LoadAndika(), 22);
    using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    foreach (var level in Enum.GetValues<CalmLevel>())
    {
        using var sheet = SKSurface.Create(new SKImageInfo(tileW * columns, tileH * rows));
        for (var i = 0; i < animals.Count; i++)
        {
            var world = new World { Style = ArtStyle.For(level) };
            world.Add(new Entity { Id = 1, Role = EntityRole.Thing, Recipe = animals[i], X = 0.5, Y = 0.45, Born = -1, Phase = 0.05, Scale = 1.8 });
            using var tile = SKSurface.Create(new SKImageInfo(tileW, tileH));
            renderer.Draw(tile.Canvas, tileW, tileH, world, null);
            using var image = tile.Snapshot();
            var (x, y) = (i % columns * tileW, i / columns * tileH);
            sheet.Canvas.DrawImage(image, x, y, SKSamplingOptions.Default);
            sheet.Canvas.DrawText(animals[i].Name, x + 10, y + 26, SKTextAlign.Left, label, ink);
        }
        using var snapshot = sheet.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 90);
        var path = Path.Combine(outDir, $"animals-{level}.png");
        using (var f = File.Create(path)) data.SaveTo(f);
        Console.WriteLine(path);
    }
    return 0;
}

static int Scenes(string outDir)
{
    const int tileW = 480, tileH = 270;
    (TimeOfDay Time, Weather Weather)[] columns =
    [
        (TimeOfDay.Morning, Weather.Clear), (TimeOfDay.Dawn, Weather.Cloudy), (TimeOfDay.Midday, Weather.Rain),
        (TimeOfDay.Afternoon, Weather.Rainbow), (TimeOfDay.Evening, Weather.Fog), (TimeOfDay.Morning, Weather.Snow),
    ];
    var landscapes = Enum.GetValues<Landscape>();
    using var label = new SKFont(Fonts.LoadAndika(), 16);
    using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    var duck = BuiltInRecipes.Animals.All.First(a => a.Body.Shape == Shape.Duck);
    foreach (var level in Enum.GetValues<CalmLevel>())
    {
        using var sheet = SKSurface.Create(new SKImageInfo(tileW * columns.Length, tileH * landscapes.Length));
        for (var row = 0; row < landscapes.Length; row++)
            for (var col = 0; col < columns.Length; col++)
            {
                var (time, weather) = columns[col];
                var scene = new Scene(landscapes[row], time, weather == Weather.Snow && Scene.Hot(landscapes[row]) ? Weather.Clear : weather);
                // A thing in each tile, to judge it against the backdrop.
                var world = new World { Style = ArtStyle.For(level), Limits = CalmLimits.For(level) };
                world.Load(0, [new Entity { Id = 1, Role = EntityRole.Thing, Recipe = duck, X = 0.3, Y = 0.5, Born = -1, Phase = 0.05, Scale = 1 }], [], scene: scene);
                using var renderer = new SceneRenderer(Fonts.LoadAndika());
                using var tile = SKSurface.Create(new SKImageInfo(tileW, tileH));
                renderer.Draw(tile.Canvas, tileW, tileH, world, null);
                using var image = tile.Snapshot();
                var (x, y) = (col * tileW, row * tileH);
                sheet.Canvas.DrawImage(image, x, y, SKSamplingOptions.Default);
                sheet.Canvas.DrawText($"{scene.Landscape}, {scene.TimeOfDay}, {scene.Weather}", x + 8, y + 20, SKTextAlign.Left, label, ink);
            }
        using var snapshot = sheet.Snapshot();
        using var data = snapshot.Encode(SKEncodedImageFormat.Png, 90);
        var path = Path.Combine(outDir, $"scenes-{level}.png");
        using (var f = File.Create(path)) data.SaveTo(f);
        Console.WriteLine(path);
    }
    return 0;
}

static int Tour(string outDir, CalmLevel calm)
{
    const int w = 1280, h = 720;
    var world = new World { Style = ArtStyle.For(calm), Limits = CalmLimits.For(calm) with { SceneFadeSeconds = 3 } };
    var duck = BuiltInRecipes.Animals.All.First(a => a.Body.Shape == Shape.Duck);
    world.Load(0, [new Entity { Id = 1, Role = EntityRole.Thing, Recipe = duck, X = 0.3, Y = 0.5, Born = -1, Phase = 0.05, Scale = 1 }], []);
    var steps = new List<Scene>();
    foreach (var landscape in Enum.GetValues<Landscape>())
    {
        steps.Add(new Scene(landscape, TimeOfDay.Morning, Weather.Clear, landscape.ToString()));
        steps.Add(new Scene(landscape, TimeOfDay.Evening, Scene.Hot(landscape) ? Weather.Cloudy : Weather.Snow, landscape.ToString()));
    }
    using var renderer = new SceneRenderer(Fonts.LoadAndika());
    using var surface = SKSurface.Create(new SKImageInfo(w, h));
    var folder = Path.Combine(outDir, "tour");
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    var clock = new DateTime(2026, 1, 1, 12, 0, 0);
    using (var recorder = new SessionRecorder(folder, "tour", () => clock))
    {
        var t = 0.0;
        const double step = 1.0 / SessionRecorder.Fps;
        foreach (var scene in steps)
        {
            world.SetScene(scene);
            for (var end = t + 5; t < end; t += step)
            {
                world.Tick(step);
                clock = new DateTime(2026, 1, 1, 12, 0, 0).AddSeconds(t);
                renderer.Draw(surface.Canvas, w, h, world, $"Tour, step {steps.IndexOf(scene) + 1} of {steps.Count}.\nScene: {scene}.");
                recorder.Offer(surface, w, h, t);
            }
        }
    }
    var file = Directory.GetFiles(folder).Single();
    var path = Path.Combine(outDir, "tour.avi");
    File.Move(file, path, overwrite: true);
    Directory.Delete(folder);
    Console.WriteLine(path);
    return 0;
}

static int Calls(string outDir)
{
    foreach (var gentle in new[] { false, true })
        foreach (var shape in AnimalCalls.Callers)
        {
            var samples = AnimalCalls.For(shape, gentle)!;
            var path = Path.Combine(outDir, $"{shape}{(gentle ? "-baby" : "")}.wav".ToLowerInvariant());
            using (var f = File.Create(path)) WavFile.Write(f, samples, Mixer.SampleRate);
            Console.WriteLine($"{path}: {samples.Length / (double)Mixer.SampleRate:0.00} s, peak {samples.Max(Math.Abs):0.00}");
        }
    return 0;
}

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
