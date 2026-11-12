using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>A photo that will do for a real thing, with everything the sidecar needs and where to download it.</summary>
/// <param name="Provenance">The file and its terms, as the sidecar records them.</param>
/// <param name="DownloadUrl">The copy to fetch: a 1280 px or 960 px thumbnail when the original is bigger.</param>
/// <param name="Doubt">Why the vetting was not sure (a painting rather than a photo); null when it was.</param>
public sealed record FoundPhoto(PhotoProvenance Provenance, string DownloadUrl, string? Doubt = null);

/// <summary>What looking for a photo came to.</summary>
/// <param name="Photo">The photo to use, or null.</param>
/// <param name="Offline">True when the network, not the content, let us down: the try does not count.</param>
/// <param name="Reason">Why there is no photo, for the wanted list: "no article", "no photo found".</param>
/// <param name="Notes">What was looked at and turned down, one line each, for the log.</param>
public sealed record PhotoSearch(FoundPhoto? Photo, bool Offline, string? Reason, IReadOnlyList<string> Notes)
{
    public static PhotoSearch Found(FoundPhoto photo, IReadOnlyList<string> notes) => new(photo, false, null, notes);
    public static PhotoSearch Nothing(string reason, IReadOnlyList<string> notes) => new(null, false, reason, notes);
    public static PhotoSearch NoNetwork(string why, IReadOnlyList<string> notes) => new(null, true, why, notes);
}

/// <summary>Finds a photo of a real thing on the web; the fetcher downloads and cuts out what it finds.</summary>
public interface IPhotoFinder
{
    /// <param name="name">The thing's name as the director gave it: "sable antelope".</param>
    /// <param name="exclude">Commons file titles already turned down for this thing, skipped.</param>
    Task<PhotoSearch> FindAsync(string name, RealCategory category, IReadOnlyCollection<string> exclude, CancellationToken ct = default);
}
