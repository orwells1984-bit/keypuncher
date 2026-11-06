using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>
/// The toy's own names for things, and what to look for on the web under them. The built-in palette has invented
/// things (a wiggrass, a driftle) that no photo can show, and real ones with nursery names (rex, longneck) that an
/// encyclopaedia knows otherwise. The picture still lives under the toy's name, so the slug does not change; only
/// the search does.
/// </summary>
public static class ToyNames
{
    private static readonly HashSet<string> Invented =
        BuiltInRecipes.Palette.Take(BuiltInRecipes.InventedFamilies).Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, (string Name, RealCategory Category)> Nursery = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rex"] = ("Tyrannosaurus", RealCategory.ExtinctAnimal),
        ["longneck"] = ("Brachiosaurus", RealCategory.ExtinctAnimal),
        ["triceratops"] = ("Triceratops", RealCategory.ExtinctAnimal),
        ["stegosaurus"] = ("Stegosaurus", RealCategory.ExtinctAnimal),
    };

    /// <summary>Whether the name is one of the invented built-in things, which no photo can show.</summary>
    public static bool IsInvented(string name) => Invented.Contains(name.Trim());

    /// <summary>What to look for on the web for a thing of this name: the encyclopaedia's name and the right
    /// category for a nursery name, else the name and category as given.</summary>
    public static (string Name, RealCategory Category) LookUp(string name, RealCategory category) =>
        Nursery.TryGetValue(name.Trim(), out var real) ? real : (name, category);
}

/// <summary>A finder that looks nursery names up under their encyclopaedia names, and finds nothing for invented ones.</summary>
public sealed class ToyNameFinder(IPhotoFinder inner) : IPhotoFinder
{
    public Task<PhotoSearch> FindAsync(string name, RealCategory category, IReadOnlyCollection<string> exclude, CancellationToken ct = default)
    {
        if (ToyNames.IsInvented(name)) return Task.FromResult(PhotoSearch.Nothing("an invented thing", [$"{name} is one of the toy's invented things"]));
        var (real, realCategory) = ToyNames.LookUp(name, category);
        return inner.FindAsync(real, realCategory, exclude, ct);
    }
}
