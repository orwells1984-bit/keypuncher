using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>
/// The pictures folder as the fetcher, the batch tool and the grown-ups' page see it: <c>&lt;slug&gt;.png</c> is what the
/// toy draws (see <c>PictureLibrary</c>), <c>&lt;slug&gt;.json</c> says where it came from, <c>raw/</c> keeps the photo or
/// generated image the cut-out was made from, <c>thumbs/</c> small copies for the grown-ups' page, <c>hidden/</c> pictures a
/// grown-up took out of play, and <c>wanted.json</c> the things with no picture yet. Every file is written under a
/// temporary name and renamed into place, so the toy never reads half a file. Safe from several threads.
/// </summary>
public sealed class PictureFolder
{
    public const string PictureExtension = ".png";
    public const int ThumbSide = 96;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Lock _lock = new();

    public PictureFolder(string root)
    {
        Root = root;
        Wanted = new WantedList(Path.Combine(root, "wanted.json"), _lock);
    }

    public string Root { get; }
    public string RawFolder => Path.Combine(Root, "raw");
    public string ThumbsFolder => Path.Combine(Root, "thumbs");
    public string HiddenFolder => Path.Combine(Root, "hidden");
    public WantedList Wanted { get; }

    public string PicturePath(string slug) => Path.Combine(Root, slug + PictureExtension);
    public string SidecarPath(string slug) => Path.Combine(Root, slug + ".json");
    public string ThumbPath(string slug) => Path.Combine(ThumbsFolder, slug + PictureExtension);
    public string HiddenPath(string slug) => Path.Combine(HiddenFolder, slug + PictureExtension);
    public string RawPath(string slug, string extension) => Path.Combine(RawFolder, slug + extension);

    /// <summary>Whether the toy has a picture of this thing in play (hidden ones do not count).</summary>
    public bool HasPicture(string slug) => File.Exists(PicturePath(slug));

    public PictureSidecar? ReadSidecar(string slug)
    {
        lock (_lock)
        {
            try
            {
                var path = SidecarPath(slug);
                return File.Exists(path) ? JsonSerializer.Deserialize<PictureSidecar>(File.ReadAllText(path), Json) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
        }
    }

    public void WriteSidecar(PictureSidecar sidecar)
    {
        lock (_lock) WriteAtomically(SidecarPath(sidecar.Slug), JsonSerializer.Serialize(sidecar, Json));
    }

    /// <summary>Puts a finished cut-out in play (and its thumbnail beside it). The sidecar is written first by the
    /// caller, so a picture never appears without its story.</summary>
    public void WritePicture(string slug, SKBitmap cutOut)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ThumbsFolder);
            using (var thumb = Thumbnail(cutOut)) WriteAtomically(ThumbPath(slug), thumb);
            WriteAtomically(PicturePath(slug), cutOut);
            var hidden = HiddenPath(slug);
            if (File.Exists(hidden)) File.Delete(hidden);
        }
    }

    /// <summary>Keeps the image a cut-out was (or will be) made from.</summary>
    public string WriteRaw(string slug, string extension, byte[] bytes)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(RawFolder);
            var path = RawPath(slug, extension);
            File.WriteAllBytes(path + ".part", bytes);
            File.Move(path + ".part", path, overwrite: true);
            return path;
        }
    }

    /// <summary>Takes a picture out of play: the toy draws the stock shape again at once.</summary>
    public void Hide(string slug)
    {
        lock (_lock)
        {
            var picture = PicturePath(slug);
            if (!File.Exists(picture)) return;
            Directory.CreateDirectory(HiddenFolder);
            File.Move(picture, HiddenPath(slug), overwrite: true);
            if (ReadSidecar(slug) is { } sidecar) WriteSidecar(sidecar with { Hidden = true });
        }
    }

    public void Unhide(string slug)
    {
        lock (_lock)
        {
            var hidden = HiddenPath(slug);
            if (!File.Exists(hidden)) return;
            File.Move(hidden, PicturePath(slug), overwrite: true);
            if (ReadSidecar(slug) is { } sidecar) WriteSidecar(sidecar with { Hidden = false });
        }
    }

    /// <summary>Every picture with a story, newest first.</summary>
    public IReadOnlyList<PictureSidecar> All()
    {
        lock (_lock)
        {
            if (!Directory.Exists(Root)) return [];
            return [.. Directory.GetFiles(Root, "*.json")
                .Where(f => !Path.GetFileName(f).Equals("wanted.json", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Equals("meetings.json", StringComparison.OrdinalIgnoreCase))
                .Select(f => ReadSidecar(Path.GetFileNameWithoutExtension(f)))
                .Where(s => s is not null).Select(s => s!)
                .OrderByDescending(s => s.MadeAt)];
        }
    }

    /// <summary>A small copy for the grown-ups' page, on a transparent background.</summary>
    public static SKBitmap Thumbnail(SKBitmap cutOut)
    {
        var k = Math.Min(1f, ThumbSide / (float)Math.Max(cutOut.Width, cutOut.Height));
        var thumb = new SKBitmap(new SKImageInfo(Math.Max(1, (int)(cutOut.Width * k)), Math.Max(1, (int)(cutOut.Height * k)), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        cutOut.ScalePixels(thumb, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return thumb;
    }

    private static void WriteAtomically(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static void WriteAtomically(string path, SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using (var f = File.Create(path + ".tmp")) data.SaveTo(f);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}

/// <summary>The real things with no picture yet, each with why and what was already turned down; <c>wanted.json</c>.</summary>
public sealed class WantedList
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _lock;
    private Dictionary<string, WantedEntry>? _entries;

    internal WantedList(string path, Lock sharedLock)
    {
        _path = path;
        _lock = sharedLock;
    }

    public IReadOnlyList<WantedEntry> All()
    {
        lock (_lock) return [.. Entries().Values.OrderBy(e => e.Since)];
    }

    public WantedEntry? Get(string slug)
    {
        lock (_lock) return Entries().GetValueOrDefault(slug);
    }

    /// <summary>Adds a thing unless it is listed already; returns the entry.</summary>
    public WantedEntry Add(string name, RealCategory category, DateTime now, bool categoryGuessed = false)
    {
        lock (_lock)
        {
            var slug = Slugs.For(name);
            var entries = Entries();
            if (entries.TryGetValue(slug, out var had)) return had;
            var entry = new WantedEntry(slug, name, category, now) { CategoryGuessed = categoryGuessed };
            entries[slug] = entry;
            Save();
            return entry;
        }
    }

    /// <summary>Records one more failed try and why.</summary>
    public void Bump(string slug, string reason, DateTime now, bool countTry = true, string? exclude = null)
    {
        lock (_lock)
        {
            var entries = Entries();
            if (!entries.TryGetValue(slug, out var e)) return;
            entries[slug] = e with
            {
                Tries = e.Tries + (countTry ? 1 : 0), LastTry = now, Reason = reason,
                Exclude = exclude is null || e.Exclude.Contains(exclude) ? e.Exclude : [.. e.Exclude, exclude],
            };
            Save();
        }
    }

    public void Remove(string slug)
    {
        lock (_lock)
        {
            if (Entries().Remove(slug)) Save();
        }
    }

    private Dictionary<string, WantedEntry> Entries()
    {
        if (_entries is not null) return _entries;
        try
        {
            _entries = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, WantedEntry>>(File.ReadAllText(_path), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _entries = []; }
        return _entries;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_entries, Json));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
