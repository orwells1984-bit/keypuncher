using System.Security.Cryptography;

namespace Keypuncher.Pictures;

/// <summary>One cut-out model file: where it comes from and how to know it arrived whole.</summary>
/// <param name="Name">What the cut-out calls itself: "isnet", "birefnet-lite".</param>
/// <param name="FileName">The file under the models folder.</param>
/// <param name="Url">Where to download it.</param>
/// <param name="Size">Its exact size in bytes.</param>
/// <param name="Md5">Its MD5, lower-case hex.</param>
public sealed record CutOutModel(string Name, string FileName, string Url, long Size, string Md5);

/// <summary>How far a download has got.</summary>
public readonly record struct ModelProgress(CutOutModel Model, long Done, long Total);

/// <summary>
/// The cut-out models under <c>&lt;data folder&gt;/models</c>. The toy never downloads one by itself: the grown-ups'
/// page button and the console tool do, to a <c>.part</c> file that is renamed into place once its size and MD5
/// match, so a half-downloaded model is never mistaken for a whole one.
/// </summary>
public sealed class ModelStore
{
    public const string FolderName = "models";

    /// <summary>The general-use ISNet: quick, and good enough for most photos.</summary>
    public static readonly CutOutModel IsNet = new("isnet", "isnet-general-use.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx", 178_648_008, "fc16ebd8b0c10d971d3513d564d01e29");

    /// <summary>BiRefNet general lite: slower, tried when ISNet's matte fails the gate.</summary>
    public static readonly CutOutModel BiRefNet = new("birefnet-lite", "birefnet-general-lite.onnx",
        "https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx", 224_005_088, "4fab47adc4ff364be1713e97b7e66334");

    public static readonly IReadOnlyList<CutOutModel> All = [IsNet, BiRefNet];

    private readonly HttpClient _http;

    /// <param name="dataFolder">The toy's data folder; the models go in <see cref="FolderName"/> under it.</param>
    /// <param name="http">The client downloads go through; a test passes its own.</param>
    public ModelStore(string dataFolder, HttpClient? http = null)
    {
        Folder = Path.Combine(dataFolder, FolderName);
        _http = http ?? PictureHttp.NewClient();
    }

    public string Folder { get; }

    public string PathOf(CutOutModel model) => Path.Combine(Folder, model.FileName);

    /// <summary>Whether the model is on disk, whole (by size; the MD5 was checked when it arrived).</summary>
    public bool IsPresent(CutOutModel model)
    {
        var file = new FileInfo(PathOf(model));
        return file.Exists && file.Length == model.Size;
    }

    public bool AnyPresent => All.Any(IsPresent);
    public bool AllPresent => All.All(IsPresent);

    /// <summary>The models still to download.</summary>
    public IReadOnlyList<CutOutModel> Missing => [.. All.Where(m => !IsPresent(m))];

    /// <summary>Downloads every model that is missing, one after the other.</summary>
    public async Task DownloadMissingAsync(IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        foreach (var model in Missing) await DownloadAsync(model, progress, ct);
    }

    /// <summary>Downloads one model to <c>.part</c>, checks its size and MD5, and renames it into place. Throws
    /// <see cref="InvalidDataException"/> when what arrived is not the model, leaving nothing behind.</summary>
    public async Task DownloadAsync(CutOutModel model, IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var path = PathOf(model);
        var part = path + ".part";
        try
        {
            using var md5 = MD5.Create();
            long done = 0;
            using (var response = await _http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var chunk = new byte[1 << 16];
                long reported = 0;
                int read;
                progress?.Report(new ModelProgress(model, 0, model.Size));
                while ((read = await stream.ReadAsync(chunk, ct)) > 0)
                {
                    await file.WriteAsync(chunk.AsMemory(0, read), ct);
                    md5.TransformBlock(chunk, 0, read, null, 0);
                    done += read;
                    if (done > model.Size) throw new InvalidDataException($"{model.FileName} is bigger than it should be");
                    if (done - reported >= 1 << 20) { progress?.Report(new ModelProgress(model, done, model.Size)); reported = done; }
                }
            }
            md5.TransformFinalBlock([], 0, 0);
            var hash = Convert.ToHexStringLower(md5.Hash ?? []);
            if (done != model.Size) throw new InvalidDataException($"{model.FileName} arrived at {done} bytes, not {model.Size}");
            if (hash != model.Md5) throw new InvalidDataException($"{model.FileName} does not match its MD5");
            File.Move(part, path, overwrite: true);
            progress?.Report(new ModelProgress(model, done, model.Size));
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    /// <summary>The MD5 of a file, lower-case hex: for checking a model by hand.</summary>
    public static string Md5Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }
}
