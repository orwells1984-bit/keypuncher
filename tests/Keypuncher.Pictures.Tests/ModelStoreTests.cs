using System.Net;
using System.Security.Cryptography;
using Keypuncher.Pictures;

namespace Keypuncher.Pictures.Tests;

public sealed class ModelStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("keypuncher-model-tests").FullName;
    private static readonly byte[] Bytes = Enumerable.Range(0, 3_000_000).Select(i => (byte)(i * 31 % 251)).ToArray();
    private static readonly CutOutModel Tiny = new("tiny", "tiny.onnx", "https://example.test/tiny.onnx", Bytes.Length, Convert.ToHexStringLower(MD5.HashData(Bytes)));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Collect : IProgress<ModelProgress>
    {
        public List<ModelProgress> Seen { get; } = [];
        public void Report(ModelProgress value) => Seen.Add(value);
    }

    [Fact]
    public void The_two_models_live_under_models_in_the_data_folder()
    {
        var store = new ModelStore(_dir);
        Assert.Equal(Path.Combine(_dir, "models"), store.Folder);
        Assert.Equal(Path.Combine(_dir, "models", "isnet-general-use.onnx"), store.PathOf(ModelStore.IsNet));
        Assert.Equal(Path.Combine(_dir, "models", "birefnet-general-lite.onnx"), store.PathOf(ModelStore.BiRefNet));
        Assert.Equal((178_648_008L, "fc16ebd8b0c10d971d3513d564d01e29"), (ModelStore.IsNet.Size, ModelStore.IsNet.Md5));
        Assert.Equal((224_005_088L, "4fab47adc4ff364be1713e97b7e66334"), (ModelStore.BiRefNet.Size, ModelStore.BiRefNet.Md5));
        Assert.StartsWith("https://github.com/danielgatis/rembg/releases/download/v0.0.0/", ModelStore.BiRefNet.Url);
        Assert.False(store.AnyPresent);
        Assert.Equal(2, store.Missing.Count);
    }

    [Fact]
    public void A_model_is_present_only_when_its_file_is_whole()
    {
        var store = new ModelStore(_dir);
        Directory.CreateDirectory(store.Folder);
        File.WriteAllBytes(store.PathOf(ModelStore.IsNet), [1, 2, 3]);
        Assert.False(store.IsPresent(ModelStore.IsNet));
        using (var f = File.Create(store.PathOf(ModelStore.IsNet))) f.SetLength(ModelStore.IsNet.Size);
        Assert.True(store.IsPresent(ModelStore.IsNet));
        Assert.True(store.AnyPresent);
        Assert.False(store.AllPresent);
        Assert.Equal([ModelStore.BiRefNet], store.Missing);
    }

    [Fact]
    public async Task A_download_goes_to_a_part_file_and_is_renamed_once_its_md5_matches()
    {
        var web = new FakeHandler().Bytes("tiny.onnx", Bytes, "application/octet-stream");
        var store = new ModelStore(_dir, new HttpClient(web));
        var progress = new Collect();

        await store.DownloadAsync(Tiny, progress);

        Assert.True(store.IsPresent(Tiny));
        Assert.Equal(Bytes, File.ReadAllBytes(store.PathOf(Tiny)));
        Assert.Empty(Directory.GetFiles(store.Folder, "*.part"));
        Assert.Equal((0L, Bytes.Length), (progress.Seen[0].Done, progress.Seen[0].Total));
        Assert.Equal(Bytes.Length, progress.Seen[^1].Done);
        Assert.True(progress.Seen.Count >= 3);
        Assert.Equal(Tiny.Md5, ModelStore.Md5Of(store.PathOf(Tiny)));
    }

    [Fact]
    public async Task A_download_with_the_wrong_md5_leaves_nothing_behind()
    {
        var wrong = Bytes.ToArray();
        wrong[^1] ^= 0xFF;
        var store = new ModelStore(_dir, new HttpClient(new FakeHandler().Bytes("tiny.onnx", wrong, "application/octet-stream")));

        var e = await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(Tiny));

        Assert.Contains("MD5", e.Message);
        Assert.False(store.IsPresent(Tiny));
        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    [Fact]
    public async Task A_download_of_the_wrong_size_leaves_nothing_behind()
    {
        var store = new ModelStore(_dir, new HttpClient(new FakeHandler().Bytes("tiny.onnx", Bytes[..1000], "application/octet-stream")));
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(Tiny));
        Assert.Contains("1000 bytes", e.Message);
        Assert.Empty(Directory.GetFiles(store.Folder));

        var big = new ModelStore(_dir, new HttpClient(new FakeHandler().Bytes("tiny.onnx", [.. Bytes, 0], "application/octet-stream")));
        await Assert.ThrowsAsync<InvalidDataException>(() => big.DownloadAsync(Tiny));
        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    [Fact]
    public async Task A_server_that_has_no_model_throws_and_leaves_nothing_behind()
    {
        var store = new ModelStore(_dir, new HttpClient(new FakeHandler().Status("tiny.onnx", HttpStatusCode.InternalServerError)));
        await Assert.ThrowsAsync<HttpRequestException>(() => store.DownloadAsync(Tiny));
        Assert.Empty(Directory.GetFiles(store.Folder));
    }

    [Fact]
    public async Task A_cancelled_download_leaves_nothing_behind()
    {
        var store = new ModelStore(_dir, new HttpClient(new FakeHandler().Hangs("tiny.onnx")));
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DownloadAsync(Tiny, ct: cts.Token));
        Assert.Empty(Directory.GetFiles(store.Folder));
    }
}
