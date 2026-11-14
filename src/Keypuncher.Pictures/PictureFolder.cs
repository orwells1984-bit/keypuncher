using System.Text.Json;
using Keypuncher.Core;
using Keypuncher.Director;
using SkiaSharp;

namespace Keypuncher.Pictures;

/// <summary>
/// The pictures folder as the fetcher, the batch tool and the grown-ups' page see it: <c>&lt;slug&gt;.png</c> is what the
/// toy draws (see <c>PictureLibrary</c>), <c>&lt;slug&gt;.json</c> says where it came from, <c>raw/</c> keeps the photo or
/// generated image the cut-out was made from, <c>thumbs/</c> small copies for the grown-ups' page, <c>hidden/</c> pictures a
/// grown-up took out of play, and <c>wanted.json</c> the things with no picture yet. A thing can have more pictures
/// than the one in play: when another replaces it, the old one is kept under <c>more/&lt;slug&gt;/</c> by its number
/// (<c>3.png</c>, <c>3.json</c>, <c>raw/3.jpg</c>, <c>thumbs/3.png</c>), for a grown-up to choose from. <c>things/</c>
/// holds what the grown-ups chose for each thing (see <see cref="ThingCards"/>). Every file is written under a
/// temporary name and renamed into place, so the toy never reads half a file. Safe from several threads.
/// </summary>
public sealed class PictureFolder
{
    public const string PictureExtension = ".png";
    public const int ThumbSide = 96;
    /// <summary>The types a raw photo or image may be kept as.</summary>
    public static readonly string[] RawExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Lock _lock = new();

    public PictureFolder(string root)
    {
        Root = root;
        Wanted = new WantedList(Path.Combine(root, "wanted.json"), _lock);
        Cards = new ThingCards(Path.Combine(root, "things"), _lock);
    }

    public string Root { get; }
    public string RawFolder => Path.Combine(Root, "raw");
    public string ThumbsFolder => Path.Combine(Root, "thumbs");
    public string HiddenFolder => Path.Combine(Root, "hidden");
    public WantedList Wanted { get; }
    /// <summary>What the grown-ups chose for each thing.</summary>
    public ThingCards Cards { get; }

    public string PicturePath(string slug) => Path.Combine(Root, slug + PictureExtension);
    public string SidecarPath(string slug) => Path.Combine(Root, slug + ".json");
    public string ThumbPath(string slug) => Path.Combine(ThumbsFolder, slug + PictureExtension);
    public string HiddenPath(string slug) => Path.Combine(HiddenFolder, slug + PictureExtension);
    public string RawPath(string slug, string extension) => Path.Combine(RawFolder, slug + extension);

    /// <summary>Where a thing's kept pictures are, the ones another picture took the place of.</summary>
    public string MoreFolder(string slug) => Path.Combine(Root, "more", slug);
    public string KeptPicturePath(string slug, int id) => Path.Combine(MoreFolder(slug), Number(id) + PictureExtension);
    public string KeptSidecarPath(string slug, int id) => Path.Combine(MoreFolder(slug), Number(id) + ".json");
    public string KeptThumbPath(string slug, int id) => Path.Combine(MoreFolder(slug), "thumbs", Number(id) + PictureExtension);
    public string KeptRawPath(string slug, int id, string extension) => Path.Combine(MoreFolder(slug), "raw", Number(id) + extension);

    /// <summary>Whether the toy has a picture of this thing in play (hidden ones do not count).</summary>
    public bool HasPicture(string slug) => File.Exists(PicturePath(slug));

    /// <summary>Whether a grown-up has hidden this thing's picture.</summary>
    public bool IsHidden(string slug) => File.Exists(HiddenPath(slug));

    /// <summary>The photo or image under <c>raw/</c> this thing's cut-out was (or will be) made from, whatever its type; null when there is none.</summary>
    public string? FindRaw(string slug)
    {
        lock (_lock)
        {
            if (!Directory.Exists(RawFolder)) return null;
            return RawExtensions.Select(e => RawPath(slug, e)).FirstOrDefault(File.Exists);
        }
    }

    public PictureSidecar? ReadSidecar(string slug)
    {
        lock (_lock)
        {
            try
            {
                var path = SidecarPath(slug);
                return File.Exists(path) ? JsonSerializer.Deserialize<PictureSidecar>(File.ReadAllText(path), Json) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn("pictures", $"the notes for picture {slug} could not be read", e);
                return null;
            }
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

    /// <summary>Keeps the image a cut-out was (or will be) made from, in place of any earlier one of another type,
    /// so that <see cref="FindRaw"/> finds the newest and never a stale photo of the thing.</summary>
    public string WriteRaw(string slug, string extension, byte[] bytes)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(RawFolder);
            var path = RawPath(slug, extension);
            File.WriteAllBytes(path + ".part", bytes);
            File.Move(path + ".part", path, overwrite: true);
            foreach (var stale in RawExtensions.Where(e => !e.Equals(extension, StringComparison.OrdinalIgnoreCase)).Select(e => RawPath(slug, e)).Where(File.Exists))
                File.Delete(stale);
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

    /// <summary>Puts a hidden picture back in play and stops any search for another.</summary>
    public void Unhide(string slug)
    {
        lock (_lock)
        {
            var hidden = HiddenPath(slug);
            if (!File.Exists(hidden)) return;
            File.Move(hidden, PicturePath(slug), overwrite: true);
            if (ReadSidecar(slug) is { } sidecar) WriteSidecar(sidecar with { Hidden = false, ShownByGrownUp = true });
            // A grown-up putting back a picture the checker turned down wants that one, not another.
            Wanted.Remove(slug);
        }
    }

    /// <summary>The stories of a thing's kept pictures, by number. The picture in play is not among them.</summary>
    public IReadOnlyList<PictureSidecar> Kept(string slug)
    {
        lock (_lock)
        {
            var more = MoreFolder(slug);
            if (!Directory.Exists(more)) return [];
            return [.. Directory.GetFiles(more, "*.json")
                .Select(ReadSidecarFile)
                .Where(s => s is not null && File.Exists(KeptPicturePath(slug, s.Id))).Select(s => s!)
                .OrderBy(s => s.Id)];
        }
    }

    /// <summary>The slugs of the things with a folder of kept pictures.</summary>
    public IReadOnlyList<string> KeptSlugs()
    {
        lock (_lock)
        {
            var more = Path.Combine(Root, "more");
            return Directory.Exists(more) ? [.. Directory.GetDirectories(more).Select(d => Path.GetFileName(d))] : [];
        }
    }

    /// <summary>The number the thing's next picture gets: one more than any it has, in play, hidden or kept, so a
    /// number a grown-up chose always means the same picture.</summary>
    public int NextId(string slug)
    {
        lock (_lock)
        {
            var current = (HasPicture(slug) || IsHidden(slug)) && ReadSidecar(slug) is { } sidecar ? sidecar.Id : 0;
            return Math.Max(current, Kept(slug).Select(k => k.Id).DefaultIfEmpty(0).Max()) + 1;
        }
    }

    /// <summary>Copies the thing's picture (in play or hidden) under <c>more/</c> with its story, its photo and its
    /// thumbnail, just before another takes its place. Returns its number, or null when there is no picture to keep.</summary>
    public int? Keep(string slug)
    {
        lock (_lock)
        {
            var picture = HasPicture(slug) ? PicturePath(slug) : IsHidden(slug) ? HiddenPath(slug) : null;
            if (picture is null || ReadSidecar(slug) is not { } sidecar) return null;
            var id = sidecar.Id;
            Directory.CreateDirectory(MoreFolder(slug));
            // The story goes last: a kept picture is listed by its story, and only once its cut-out is there.
            CopyAtomically(picture, KeptPicturePath(slug, id));
            if (File.Exists(ThumbPath(slug))) CopyAtomically(ThumbPath(slug), KeptThumbPath(slug, id));
            if (FindRaw(slug) is { } raw) CopyAtomically(raw, KeptRawPath(slug, id, Path.GetExtension(raw)));
            // Hidden is about the place in play, which the kept copy has left; what the check said stays with it.
            WriteAtomically(KeptSidecarPath(slug, id), JsonSerializer.Serialize(sidecar with { Hidden = false }, Json));
            return id;
        }
    }

    /// <summary>The cut-out with this number: the thing's picture in play, or a kept one. Null when there is none
    /// (a hidden picture is out of play under every number).</summary>
    public string? PathOf(string slug, int id)
    {
        lock (_lock)
        {
            if (HasPicture(slug) && ReadSidecar(slug)?.Id == id) return PicturePath(slug);
            var kept = KeptPicturePath(slug, id);
            return File.Exists(kept) ? kept : null;
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
        // Scaled in premultiplied space, so the edge keeps the thing's colour rather than the photo's background.
        return CutOutPost.Scale(cutOut, Math.Max(1, (int)(cutOut.Width * k)), Math.Max(1, (int)(cutOut.Height * k)));
    }

    private static string Number(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private PictureSidecar? ReadSidecarFile(string path)
    {
        try { return JsonSerializer.Deserialize<PictureSidecar>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"the notes for the kept picture {path} could not be read", e);
            return null;
        }
    }

    private static void CopyAtomically(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to + ".tmp", overwrite: true);
        File.Move(to + ".tmp", to, overwrite: true);
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

/// <summary>The real things with no picture yet, each with why and what was already turned down; <c>wanted.json</c>.
/// The file is the truth: the toy and the console tool each hold their own list over the same file, so every look
/// reads it again when it has changed since this list last read or wrote it, and every change is made to what the
/// file says now.</summary>
public sealed class WantedList
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    /// <summary>How often, and how long apart, a save is tried again when another process has the file open.</summary>
    private const int SaveTries = 3;
    private static readonly TimeSpan SaveRetryPause = TimeSpan.FromMilliseconds(50);
    private readonly string _path;
    private readonly Lock _lock;
    private Dictionary<string, WantedEntry>? _entries;
    /// <summary>The file's last-write time and length as last read or written, to tell when another process changed it.</summary>
    private (DateTime WrittenAt, long Length)? _read;

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

    /// <summary>Adds a thing unless it is listed already; returns the entry. A known category replaces a listed one
    /// that was guessed or differs; a guess never replaces anything.</summary>
    public WantedEntry Add(string name, RealCategory category, DateTime now, bool categoryGuessed = false)
    {
        lock (_lock)
        {
            var slug = Slugs.For(name);
            var entries = Entries();
            if (entries.TryGetValue(slug, out var had))
            {
                if (categoryGuessed || (!had.CategoryGuessed && had.Category == category)) return had;
                entries[slug] = had = had with { Category = category, CategoryGuessed = false };
                Save();
                return had;
            }
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

    /// <summary>What the file says now: read again whenever it has changed under us.</summary>
    private Dictionary<string, WantedEntry> Entries()
    {
        var stamp = Stamp();
        if (_entries is not null && stamp == _read) return _entries;
        try
        {
            _entries = stamp is not null
                ? JsonSerializer.Deserialize<Dictionary<string, WantedEntry>>(File.ReadAllText(_path), Json) ?? []
                : [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("pictures", $"the wanted list {_path} could not be read; starting it afresh", e);
            _entries = [];
        }
        _read = stamp;
        return _entries;
    }

    /// <summary>The file's last-write time and length; null when there is no file.</summary>
    private (DateTime WrittenAt, long Length)? Stamp()
    {
        var file = new FileInfo(_path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
    }

    /// <summary>Writes the list whole, under a temporary name first. Another process reading the file at that
    /// moment makes the rename fail on Windows, so it is tried <see cref="SaveTries"/> times before the failure is
    /// let through to the caller: a change that is lost must not be lost quietly.</summary>
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_entries, Json));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(_path + ".tmp", _path, overwrite: true);
                _read = Stamp();
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < SaveTries)
            {
                Log.Debug("pictures", $"the wanted list could not be renamed into place (try {attempt} of {SaveTries}); trying again", e);
                Thread.Sleep(SaveRetryPause);
            }
        }
    }
}
