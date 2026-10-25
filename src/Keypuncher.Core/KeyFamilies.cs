namespace Keypuncher.Core;

/// <summary>
/// What each key and each spot on the screen makes: <see cref="BuiltInRecipes.Palette"/> to begin with. The
/// director restyles the invented families at the front of the palette, one at a time, into real things
/// (a "petalpop" key becomes a daisy key, later a tulip key). A family keeps its kind, so a plant key stays a
/// plant key, and the same key still makes the same family; the real animals after them never change.
/// Single-threaded, like <see cref="World"/>.
/// </summary>
public sealed class KeyFamilies
{
    private readonly Recipe[] _recipes = new Recipe[BuiltInRecipes.Palette.Count];
    private readonly double?[] _changedAt = new double?[BuiltInRecipes.Palette.Count];

    public KeyFamilies() => Reset();

    public int Count => _recipes.Length;

    public Recipe this[int slot] => _recipes[slot];

    public IReadOnlyList<Recipe> All => _recipes;

    /// <summary>Raised after a family changes, so the world can be saved.</summary>
    public event Action? Changed;

    /// <summary>The families the director may restyle: the invented ones at the front of the palette.</summary>
    public static bool CanRestyle(int slot) => slot is >= 0 and < BuiltInRecipes.InventedFamilies;

    /// <summary>World time the family was last restyled; null while it is still the built-in one.</summary>
    public double? ChangedAt(int slot) => _changedAt[slot];

    /// <summary>The family a key or screen spot maps to; the same number always gives the same family.</summary>
    public Recipe For(uint family) => _recipes[family % (uint)_recipes.Length];

    /// <summary>Gives a family a new recipe of the same kind. Returns false, changing nothing, for a family that
    /// may not be restyled or a recipe of another kind.</summary>
    public bool Restyle(int slot, Recipe recipe, double at)
    {
        if (!CanRestyle(slot) || recipe.Kind != BuiltInRecipes.Palette[slot].Kind) return false;
        _recipes[slot] = recipe;
        _changedAt[slot] = at;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The restyled families, for saving.</summary>
    public IEnumerable<(int Slot, Recipe Recipe, double At)> Restyled() =>
        Enumerable.Range(0, Count).Where(i => _changedAt[i] is not null).Select(i => (i, _recipes[i], _changedAt[i]!.Value));

    /// <summary>Back to the built-in palette, then the saved families on top; unusable ones are skipped.</summary>
    public void Load(IEnumerable<(int Slot, Recipe Recipe, double At)> restyled)
    {
        Reset();
        foreach (var (slot, recipe, at) in restyled)
        {
            if (!CanRestyle(slot) || recipe.Kind != BuiltInRecipes.Palette[slot].Kind) continue;
            _recipes[slot] = recipe;
            _changedAt[slot] = at;
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _recipes.Length; i++)
        {
            _recipes[i] = BuiltInRecipes.Palette[i];
            _changedAt[i] = null;
        }
    }
}
