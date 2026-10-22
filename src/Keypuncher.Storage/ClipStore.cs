using Keypuncher.Core;

namespace Keypuncher.Storage;

/// <summary>
/// Recorded clips as plain WAV files, one per letter, number or word:
/// <c>{root}/{language}/{letters|numbers|words}/{key}.wav</c>. Plain files on purpose, so a recording
/// can be replaced from Explorer and the folder backed up by copying it.
/// </summary>
public sealed class ClipStore(string root)
{
    public string Root { get; } = root;

    public string PathFor(ClipId id) =>
        Path.Combine(Root, id.Language, id.Kind.ToString().ToLowerInvariant() + "s", id.Key + ".wav");

    public bool Exists(ClipId id) => File.Exists(PathFor(id));

    public Stream OpenRead(ClipId id) => File.OpenRead(PathFor(id));

    /// <summary>Writes through a temporary file so a crash never leaves half a clip.</summary>
    public void Save(ClipId id, Action<Stream> write)
    {
        var path = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var s = File.Create(temp)) write(s);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(ClipId id) => File.Delete(PathFor(id));
}
