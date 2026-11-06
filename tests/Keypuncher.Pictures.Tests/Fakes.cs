using System.Net;
using Keypuncher.Director;
using Keypuncher.Pictures;
using Keypuncher.Rendering;
using SkiaSharp;

namespace Keypuncher.Pictures.Tests;

/// <summary>Answers web requests from canned files: the first route whose key the (decoded) URL contains wins, and
/// anything else is a 404. Remembers every URL asked, in order.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly List<(string Key, Func<CancellationToken, Task<HttpResponseMessage>> Answer)> _routes = [];

    public List<string> Requests { get; } = [];

    public static string TestData(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

    public FakeHandler Json(string key, string file) => Text(key, File.ReadAllText(TestData(file)));

    public FakeHandler Text(string key, string json) => Add(key, _ => Task.FromResult(Response(HttpStatusCode.OK, new StringContent(json, System.Text.Encoding.UTF8, "application/json"))));

    /// <summary>The Commons look-up of one file, keyed so that "male.jpg" and "female.jpg" cannot be confused.</summary>
    public FakeHandler ImageInfo(string file, string json) => Json($"titles=File:{file}&prop=imageinfo|categories", json);

    public FakeHandler Bytes(string key, byte[] bytes, string mime) => Add(key, _ =>
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        return Task.FromResult(Response(HttpStatusCode.OK, content));
    });

    public FakeHandler Status(string key, HttpStatusCode code) => Add(key, _ => Task.FromResult(Response(code, new StringContent(""))));

    public FakeHandler Throws(string key, Exception e) => Add(key, _ => Task.FromException<HttpResponseMessage>(e));

    /// <summary>Never answers: for the client's timeout.</summary>
    public FakeHandler Hangs(string key) => Add(key, async ct => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });

    private FakeHandler Add(string key, Func<CancellationToken, Task<HttpResponseMessage>> answer)
    {
        _routes.Add((key, answer));
        return this;
    }

    private static HttpResponseMessage Response(HttpStatusCode code, HttpContent content) => new(code) { Content = content };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Requests.Add(url);
        foreach (var (key, answer) in _routes)
            if (url.Contains(key, StringComparison.Ordinal)) return answer(cancellationToken);
        return Task.FromResult(Response(HttpStatusCode.NotFound, new StringContent("{\"type\":\"https://mediawiki.org/wiki/HyperSwitch/errors/not_found\",\"title\":\"Not found.\"}")));
    }
}

/// <summary>Cuts nothing out: hands back the photo as it is, or fails as told.</summary>
internal sealed class FakeCutOut : ICutOut
{
    public string Name => "fake";
    public bool IsAvailable { get; set; } = true;
    public string? FailWith { get; set; }
    public int Calls { get; private set; }

    public CutOutResult Cut(SKBitmap photo)
    {
        Calls++;
        if (FailWith is { } why) return new CutOutResult(null, Name, 0.95, why);
        var copy = new SKBitmap(new SKImageInfo(photo.Width, photo.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var c = new SKCanvas(copy)) c.DrawBitmap(photo, 0, 0, SKSamplingOptions.Default);
        return new CutOutResult(copy, Name, 0.4, null);
    }
}

/// <summary>Finds what it is told to and remembers what it was asked.</summary>
internal sealed class FakePhotoFinder : IPhotoFinder
{
    public PhotoSearch Answer { get; set; } = PhotoSearch.Nothing("no photo found", []);
    public List<(string Name, RealCategory Category, IReadOnlyCollection<string> Exclude)> Asked { get; } = [];

    public Task<PhotoSearch> FindAsync(string name, RealCategory category, IReadOnlyCollection<string> exclude, CancellationToken ct = default)
    {
        Asked.Add((name, category, exclude));
        return Task.FromResult(Answer);
    }

    public static FoundPhoto Photo(string fileTitle, string downloadUrl, string? doubt = null) => new(new PhotoProvenance(
        "Sable antelope", "https://en.wikipedia.org/wiki/Sable_antelope", fileTitle, downloadUrl,
        $"https://commons.wikimedia.org/wiki/{fileTitle.Replace(' ', '_')}", "Charles J. Sharp", "Own work", "CC BY-SA 4.0",
        "https://creativecommons.org/licenses/by-sa/4.0", null, "abc123", 2048, 1365, "image/jpeg", "Wikidata's picture of it"), downloadUrl, doubt);
}

/// <summary>Pretends to fetch: answers each name as told, after waiting for a gate when there is one.</summary>
internal sealed class FakeFetcher : IPictureFetcher
{
    private readonly Lock _lock = new();
    public Dictionary<string, FetchOutcome> Outcomes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public FetchOutcome Default { get; set; } = FetchOutcome.Pictured;
    public SemaphoreSlim? Gate { get; set; }
    public List<(string Name, RealCategory Category)> Calls { get; } = [];
    public Action<string>? OnFetch { get; set; }

    public async Task<FetchOutcome> FetchAsync(string name, RealCategory category, CancellationToken ct = default)
    {
        lock (_lock) Calls.Add((name, category));
        if (Gate is { } gate) await gate.WaitAsync(ct);
        OnFetch?.Invoke(name);
        return Outcomes.GetValueOrDefault(name, Default);
    }

    public int CallsFor(string name)
    {
        lock (_lock) return Calls.Count(c => c.Name == name);
    }
}

internal static class TestPhotos
{
    /// <summary>A "photo": the sample animal on a white background, as JPEG bytes.</summary>
    public static byte[] Jpeg(int width = 480, int height = 360)
    {
        using var sample = SamplePicture.Draw();
        using var photo = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(photo))
        {
            c.Clear(SKColors.White);
            c.DrawBitmap(sample, 0, 0, SKSamplingOptions.Default);
        }
        using var data = photo.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    /// <summary>A "photo" stored on its side the way a camera does: the left half red and the right half blue as
    /// stored, with an EXIF tag that says which way is up (6 = turn a quarter clockwise, 8 = anticlockwise).</summary>
    public static byte[] JpegStoredSideways(int orientation, int width = 40, int height = 20)
    {
        using var photo = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(photo))
        {
            c.Clear(SKColors.Blue);
            c.DrawRect(0, 0, width / 2f, height, new SKPaint { Color = SKColors.Red });
        }
        using var data = photo.Encode(SKEncodedImageFormat.Jpeg, 100);
        var jpeg = data.ToArray();
        // An APP1 segment right after the start-of-image marker: "Exif\0\0", a little-endian TIFF header, one IFD
        // with the one Orientation (0x0112) entry, and no next IFD.
        byte[] tiff =
        [
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    /// <summary>A small transparent cut-out, as a PNG file at the path.</summary>
    public static void WritePng(string path)
    {
        using var sample = SamplePicture.Draw();
        using var data = sample.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var f = File.Create(path);
        data.SaveTo(f);
    }
}
