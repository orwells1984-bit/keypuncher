// Plays a scripted session through the real engine, renderer and mixer with no window, and writes
// PNG frames and the audio to a folder. For checking how things look and sound without the app, and
// later for judging what the director's recipes look like.
//
//   dotnet run --project tools/Keypuncher.Preview -- --out preview --keys "kisa 3 mjá" --mash 4
//
// Each character in --keys is one press, 0.35 s apart (a space is a pause); ´ is the accent key.
// --mash N adds N palm mashes at the end. --calm Baby|Toddler|BigToddler picks the calm level (default Toddler). Frames are written after every press and at the end.
// --taps N then taps the N newest things where they stand, so each tap lands on one and pops.
// --sheet instead writes one contact sheet per calm level of every animal and dinosaur, each in its own small scene at
// the size a word's picture is drawn.
// --concepts does the same for the starter words that are not animals (apple, moon, house, boat and the rest).
// --stock does the same for typical director picks drawn with the stock shapes the director's prompt names.
// --scenes instead writes one contact sheet per calm level of every landscape in a few times of day and weathers.
// --tour instead records a video (as test mode does) of the backdrop moving through every landscape, time of day
// and weather, with the toy's real fades, to tour.avi.
// --bench instead plays a minute of fast mashing at 1920x1080 and prints how long each part of drawing takes,
// before and during a scene's fade, with and without recording.
// --pictures DIR instead writes one contact sheet per calm level of every cut-out picture in DIR (sable-antelope.png
// and so on), each at the four stages of the realism ladder: stock shape, silhouette, flat colours, picture. With no
// pictures in DIR it first writes a drawn sample there, so the ladder can be seen without any photos.
// --calls instead writes every animal noise as a WAV, bold and baby (gentle) versions, for a quick listen.

using Keypuncher.Audio;
using Keypuncher.Core;
using Keypuncher.Pictures;
using Keypuncher.Rendering;
using Keypuncher.Storage;
using Keypuncher.Toy;
using SkiaSharp;

var outDir = Get("--out", "preview");
var keys = Get("--keys", "sól 3 köttur");
var mashes = int.Parse(Get("--mash", "2"));
var taps = int.Parse(Get("--taps", "0"));
var width = int.Parse(Get("--width", "1280"));
var height = int.Parse(Get("--height", "720"));
var calm = Enum.Parse<CalmLevel>(Get("--calm", nameof(CalmLevel.Toddler)), ignoreCase: true);
Directory.CreateDirectory(outDir);
if (args.Contains("--sheet")) return Sheet(outDir, "animals", [.. BuiltInRecipes.Animals.All, BuiltInRecipes.Concepts["butterfly"]]);
if (args.Contains("--stock")) return Sheet(outDir, "stock", StockPicks());
if (args.Contains("--concepts")) return Sheet(outDir, "concepts", [.. BuiltInRecipes.Concepts.Values.Where(r => !r.Body.Shape.IsAnimal())]);
if (args.Contains("--calls")) return Calls(outDir);
if (args.Contains("--pictures")) return PictureSheet(outDir, Get("--pictures", "pictures"));
if (args.Contains("--scenes")) return Scenes(outDir);
if (args.Contains("--tour")) return Tour(outDir, calm);
if (args.Contains("--bench")) return Bench(outDir, calm, int.Parse(Get("--width", "1920")), int.Parse(Get("--height", "1080")));

var world = new World();
var mixer = new Mixer();
var speech = new SpeechLibrary(new ClipStore(Path.Combine(outDir, "clips")), null);
var engine = new ToyEngine(world, mixer, speech, ToySettings.Default with { Calm = calm }, new Random(1));
using var renderer = new SceneRenderer(Fonts.LoadAndika());
var audio = new List<float>();
var pops = 0;
engine.Popped += _ => pops++;
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

foreach (var thing in world.Live.Where(e => e.Role == EntityRole.Thing).TakeLast(taps).ToList())
{
    engine.OnTouch(thing.X, thing.Y, 1);
    Advance(0.8);
    Snapshot("tap");
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
Console.WriteLine($"live {world.Live.Count}, things {world.LiveThingCount}{(taps > 0 ? $", pops {pops}" : "")}");

return 0;

static int PictureSheet(string outDir, string folder)
{
    Directory.CreateDirectory(folder);
    var files = Directory.GetFiles(folder).Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)).ToList();
    if (files.Count == 0)
    {
        var sample = Path.Combine(folder, "sample-animal.png");
        using (var bitmap = SamplePicture.Draw())
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var f = File.Create(sample)) data.SaveTo(f);
        files.Add(sample);
        Console.WriteLine($"no pictures in {folder}; drew {sample}");
    }
    // How clear each cut-out's silhouette is; a lump keeps the stock shape at the silhouette rung.
    foreach (var file in files)
    {
        using var decoded = SKBitmap.Decode(file);
        if (decoded is null) continue;
        using var rgba = decoded.Copy(SKColorType.Rgba8888);
        var solidity = Outline.Solidity(rgba.GetPixelSpan(), rgba.Width, rgba.Height, rgba.RowBytes);
        Console.WriteLine($"{Path.GetFileNameWithoutExtension(file),-20} {rgba.Width}x{rgba.Height}, fills {solidity:0.00} of its hull: {(Outline.IsClear(rgba.Width, rgba.Height, solidity) ? "clear silhouette" : "a lump, the shape stays")}");
    }
    const int tileW = 480, tileH = 320;
    var stages = Enum.GetValues<PictureStage>();
    // The grown-ups' choices for each thing are drawn too: a skipped rung shows the rung drawn in its place, and a
    // rung given another picture shows that one.
    var pictureFolder = new PictureFolder(folder);
    using var book = new ChoiceBook(pictureFolder.Cards);
    using var pictures = new PictureLibrary(folder) { Choices = book.Get, PictureFile = pictureFolder.PathOf };
    using var label = new SKFont(Fonts.LoadAndika(), 20);
    using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(40, 40, 70) };
    foreach (var level in Enum.GetValues<CalmLevel>())
    {
        using var sheet = SKSurface.Create(new SKImageInfo(tileW * stages.Length, tileH * files.Count));
        for (var row = 0; row < files.Count; row++)
        {
            var name = Path.GetFileNameWithoutExtension(files[row]).Replace('-', ' ');
            // A built-in thing keeps its own colour, so the silhouette rung looks as it does in the toy.
            var recipe = (BuiltInRecipes.Concepts.TryGetValue(name, out var own) ? own : BuiltInRecipes.Animals.Cat) with { Name = name };
            for (var col = 0; col < stages.Length; col++)
            {
                pictures.Forced = stages[col];
                // The level's cap is shown too, so a rung the level does not reach draws the rung it does.
                var world = new World { Style = ArtStyle.For(level) };
                world.Add(new Entity { Id = (row * 10) + col + 1, Role = EntityRole.Thing, Recipe = recipe, X = 0.5, Y = 0.5, Born = -100, Phase = 0.05, Scale = 1.4 });
                using var tile = SKSurface.Create(new SKImageInfo(tileW, tileH));
                // Pictures are read and drawn on the pool, so the tile is drawn again once they are ready.
                using var scratch = new SceneRenderer(Fonts.LoadAndika()) { Pictures = pictures };
                scratch.Draw(tile.Canvas, tileW, tileH, world, null);
                pictures.WaitIdle(TimeSpan.FromSeconds(30));
                scratch.Draw(tile.Canvas, tileW, tileH, world, null);
                pictures.WaitIdle(TimeSpan.FromSeconds(30));
                scratch.Draw(tile.Canvas, tileW, tileH, world, null);
                using var image = tile.Snapshot();
                var (x, y) = (col * tileW, row * tileH);
                sheet.Canvas.DrawImage(image, x, y, SKSamplingOptions.Default);
                var shown = (PictureStage)Math.Min((int)stages[col], (int)ArtStyle.For(level).MaxPictureStage);
                var choice = book.Get(Path.GetFileNameWithoutExtension(files[row]))?.Rungs.For(stages[col]);
                sheet.Canvas.DrawText($"{name}: {stages[col]}{(shown != stages[col] ? $" (capped at {shown} here)" : "")}{(choice is { ByHand: true } by ? $" [chosen: {by}]" : "")}", x + 10, y + 24, SKTextAlign.Left, label, ink);
            }
        }
        using var snapshot = sheet.Snapshot();
        using var png = snapshot.Encode(SKEncodedImageFormat.Png, 90);
        var path = Path.Combine(outDir, $"pictures-{level}.png");
        using (var f = File.Create(path)) png.SaveTo(f);
        Console.WriteLine(path);
    }
    return 0;
}

// Typical director picks drawn the way the director's prompt tells it to draw them, for judging the stock shapes a
// real thing with no picture yet is drawn with.
static List<Recipe> StockPicks()
{
    static Recipe Pick(string name, RecipeKind kind, Shape shape, int lobes, int hue, double size, int eyes = 0) => new()
    {
        Kind = kind, Name = name, Body = new Body(shape, lobes, hue, size), Eyes = eyes,
        Motion = new Motion(MotionType.Still, 0), Voice = new Voice(Waveform.Sine, 0.01, 0.5, "pentatonic", "C4"),
    };
    return
    [
        Pick("tomato (Blob)", RecipeKind.Plant, Shape.Blob, 2, 0, 0.45),
        Pick("ladybird (Blob)", RecipeKind.Creature, Shape.Blob, 1, 0, 0.35, 2),
        Pick("frog (Blob)", RecipeKind.Creature, Shape.Blob, 3, 120, 0.4, 2),
        Pick("hedgehog (Blob)", RecipeKind.Creature, Shape.Blob, 5, 25, 0.4, 2),
        Pick("caterpillar (Stripe)", RecipeKind.Creature, Shape.Stripe, 4, 110, 0.4, 2),
        Pick("tulip (Petal)", RecipeKind.Plant, Shape.Petal, 6, 340, 0.5),
        Pick("oak tree (Cloud)", RecipeKind.Plant, Shape.Cloud, 6, 110, 0.9),
        Pick("pine tree (Star)", RecipeKind.Plant, Shape.Star, 5, 130, 0.8),
        Pick("meadow grass (Wave)", RecipeKind.Plant, Shape.Wave, 4, 100, 0.5),
        Pick("blueberries (DotField)", RecipeKind.Plant, Shape.DotField, 4, 230, 0.4),
        Pick("kite (Star)", RecipeKind.Creature, Shape.Star, 4, 0, 0.5),
        Pick("balloon (Blob)", RecipeKind.Creature, Shape.Blob, 1, 0, 0.4),
        Pick("fire engine (Car)", RecipeKind.Creature, Shape.Car, 2, 0, 0.7),
        Pick("bus (Car)", RecipeKind.Creature, Shape.Car, 2, 50, 0.7),
        Pick("fire engine (old Stripe)", RecipeKind.Creature, Shape.Stripe, 2, 0, 0.7),
        Pick("fennec fox (old Stripe)", RecipeKind.Creature, Shape.Stripe, 3, 340, 0.8, 2),
        Pick("fennec fox (now Dog)", RecipeKind.Creature, Shape.Dog, 3, 30, 0.6, 2),
        Pick("monarch (Butterfly)", RecipeKind.Creature, Shape.Butterfly, 4, 30, 0.5, 2),
    ];
}

static int Sheet(string outDir, string name, IReadOnlyList<Recipe> animals)
{
    const int tileW = 640, tileH = 360, columns = 6;
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
        var path = Path.Combine(outDir, $"{name}-{level}.png");
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

static int Bench(string outDir, CalmLevel calm, int w, int h)
{
    var world = new World();
    var engine = new ToyEngine(world, new Mixer(), new SpeechLibrary(new ClipStore(Path.Combine(outDir, "clips")), null),
        ToySettings.Default with { Calm = calm, SessionMinutes = 0 }, new Random(1));
    world.Load(0, [], [], scene: new Scene(Landscape.Lake, TimeOfDay.Afternoon, Weather.Clear, "Lake Bled, Slovenia"));
    using var renderer = new SceneRenderer(Fonts.LoadAndika());
    using var surface = SKSurface.Create(new SKImageInfo(w, h));
    var random = new Random(7);
    var clock = DateTimeOffset.UnixEpoch;
    var letters = "asdfghjklæqwertyuiopðzxcvbnmþ1234567890";
    var nextPress = 0.0;
    var t = 0.0;
    SessionRecorder? recorder = null;

    void Run(string label, double seconds)
    {
        renderer.Timings.Reset();
        var offer = 0L;
        var things = 0;
        for (var end = t + seconds; t < end; t += 1 / 60.0)
        {
            if (t >= nextPress)
            {
                // A palm on the keys: one to four presses at once, a few times a second.
                for (var i = random.Next(1, 5); i > 0; i--)
                {
                    var ch = letters[random.Next(letters.Length)];
                    var scan = IcelandicKeys.ScanCode(ch) ?? 0x39;
                    engine.OnKey(new KeyPress(clock, scan, ch.ToString(), false, true));
                    engine.OnKey(new KeyPress(clock, scan, ch.ToString(), false, false));
                }
                nextPress = t + 0.15 + (random.NextDouble() * 0.6);
            }
            engine.Update(1 / 60.0);
            clock += TimeSpan.FromSeconds(1 / 60.0);
            renderer.Draw(surface.Canvas, w, h, world, null);
            var before = System.Diagnostics.Stopwatch.GetTimestamp();
            recorder?.Offer(surface, w, h, t);
            offer += System.Diagnostics.Stopwatch.GetTimestamp() - before;
            things = Math.Max(things, world.LiveThingCount);
        }
        var frames = renderer.Timings.Frames;
        Console.WriteLine($"{label,-28} {renderer.Timings}; recording {offer * 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames:0.0}; up to {things} things, {world.Live.Count} live");
    }

    Console.WriteLine($"{calm}, {w}x{h}");
    Run("mashing", 20);
    if (Environment.GetEnvironmentVariable("KP_SHORT") == "1") return 0;
    world.SetScene(new Scene(Landscape.Lake, TimeOfDay.Evening, Weather.Clear, "Lake Bled, Slovenia"));
    Run("mashing, scene fading", world.Limits.SceneFadeSeconds);
    Run("mashing after the fade", 10);
    recorder = new SessionRecorder(Path.Combine(outDir, "bench-videos"), "bench");
    Run("mashing, recording", 10);
    recorder.Dispose();
    Directory.Delete(Path.Combine(outDir, "bench-videos"), recursive: true);
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
