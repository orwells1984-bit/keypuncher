using System.Text.Json;
using System.Text.Json.Serialization;
using Keypuncher.Core;
using Keypuncher.Director;

namespace Keypuncher.Pictures;

/// <summary>What the grown-ups decided about one thing, kept as <c>things/&lt;slug&gt;.json</c> in the pictures folder:
/// what is drawn on each rung of its realism ladder, whether it climbs the ladder at all, its kind when the
/// director filed it wrongly, and the word for it in each language. A thing with no card is drawn as the toy
/// always drew it, and so is every rung a card leaves on "auto".</summary>
/// <remarks>The card lives with the pictures and not in the world: a fresh world clears what the child made, and
/// these choices belong to the family's pictures and words, which outlast any one world.</remarks>
public sealed record ThingCard(
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("name")] string Name)
{
    /// <summary>The thing's kind as a grown-up set it; null leaves it as the director filed it.</summary>
    [JsonPropertyName("category")] public RealCategory? Category { get; init; }
    [JsonPropertyName("climb")] public ClimbChoice Climb { get; init; }
    [JsonPropertyName("rungs")] public RungChoices Rungs { get; init; } = new();
    /// <summary>Language tag ("is", "en", "bg") to the word a grown-up wrote for the thing. Only these words, and the
    /// starter words, are ever shown or said to the child: the model never writes them.</summary>
    [JsonPropertyName("words")] public IReadOnlyDictionary<string, string> Words { get; init; } = new Dictionary<string, string>();
    [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; init; }

    /// <summary>Whether the card says anything the toy would not do by itself; one that does not need not be kept.</summary>
    [JsonIgnore]
    public bool IsEmpty => Category is null && Climb == ClimbChoice.Auto && !Rungs.AnyByHand && Words.Count == 0;

    public string? WordIn(string languageTag) => Words.TryGetValue(languageTag, out var w) && !string.IsNullOrWhiteSpace(w) ? w : null;
}

/// <summary>The thing cards in the pictures folder. Every look reads the file, so the toy and the console tool,
/// each with its own instance, see each other's changes; every write goes through a temporary file. Safe from
/// several threads.</summary>
public sealed class ThingCards
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Lock _lock;

    internal ThingCards(string folder, Lock sharedLock)
    {
        Folder = folder;
        _lock = sharedLock;
    }

    public string Folder { get; }

    /// <summary>Raised, on the thread that made the change, with the slug of a thing whose card was written or removed.</summary>
    public event Action<string>? Changed;

    public string PathFor(string slug) => Path.Combine(Folder, slug + ".json");

    /// <summary>The thing's card, or null when it has none or the file cannot be read.</summary>
    public ThingCard? Get(string slug)
    {
        if (slug.Length == 0) return null;
        lock (_lock)
        {
            try
            {
                var path = PathFor(slug);
                return File.Exists(path) ? JsonSerializer.Deserialize<ThingCard>(File.ReadAllText(path), Json) : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn("pictures", $"the card for {slug} could not be read; the thing is drawn as if it had none", e);
                return null;
            }
        }
    }

    /// <summary>The thing's card, or a new one that says nothing yet.</summary>
    public ThingCard GetOrNew(string name) => Get(Slugs.For(name)) ?? new ThingCard(Slugs.For(name), name);

    /// <summary>Every card, by name.</summary>
    public IReadOnlyList<ThingCard> All()
    {
        lock (_lock)
        {
            if (!Directory.Exists(Folder)) return [];
            return [.. Directory.GetFiles(Folder, "*.json")
                .Select(f => Get(Path.GetFileNameWithoutExtension(f)))
                .Where(c => c is not null).Select(c => c!)
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>Writes a card, stamped with the time; a card that says nothing is removed instead.</summary>
    public void Save(ThingCard card, DateTime now)
    {
        if (card.IsEmpty) { Remove(card.Slug); return; }
        lock (_lock)
        {
            Directory.CreateDirectory(Folder);
            var path = PathFor(card.Slug);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(card with { UpdatedAt = now }, Json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        Changed?.Invoke(card.Slug);
    }

    public void Remove(string slug)
    {
        lock (_lock)
        {
            var path = PathFor(slug);
            if (!File.Exists(path)) return;
            File.Delete(path);
        }
        Changed?.Invoke(slug);
    }

    /// <summary>Whether a grown-up chose this picture of the thing for one of its rungs.</summary>
    public bool ChoseByHand(string slug, int picture) => Get(slug)?.Rungs.Uses(picture) == true;
}
