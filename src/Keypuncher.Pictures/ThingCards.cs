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

    /// <summary>The part of the card the renderer draws by, or null when it says nothing about drawing.</summary>
    [JsonIgnore]
    public ThingChoices? Choices => Climb == ClimbChoice.Auto && !Rungs.AnyByHand ? null : new ThingChoices(Climb, Rungs);

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

/// <summary>Every thing's drawing choices held in memory, for the renderer, which asks several times a frame and
/// must not wait on a file. Read from the cards once and kept up to date as cards are saved or removed through the
/// same <see cref="ThingCards"/>; <see cref="Reload"/> picks up changes made by another program, such as the
/// console tool. Safe from several threads.</summary>
public sealed class ChoiceBook : IDisposable
{
    private readonly ThingCards _cards;
    private System.Collections.Concurrent.ConcurrentDictionary<string, ThingChoices> _choices = new();

    public ChoiceBook(ThingCards cards)
    {
        _cards = cards;
        _cards.Changed += OnChanged;
        Reload();
    }

    /// <summary>Raised, on the thread that made the change, with the slug of a thing whose choices may have changed.</summary>
    public event Action<string>? Changed;

    /// <summary>The thing's choices, or null when the grown-ups chose nothing about how it is drawn.</summary>
    public ThingChoices? Get(string slug) => _choices.GetValueOrDefault(slug);

    /// <summary>Reads every card again; returns the slugs whose choices differ from before.</summary>
    public IReadOnlyList<string> Reload()
    {
        var fresh = new System.Collections.Concurrent.ConcurrentDictionary<string, ThingChoices>();
        foreach (var card in _cards.All())
            if (card.Choices is { } choices) fresh[card.Slug] = choices;
        var before = Interlocked.Exchange(ref _choices, fresh);
        List<string> changed = [.. before.Keys.Union(fresh.Keys).Where(slug => !Equals(before.GetValueOrDefault(slug), fresh.GetValueOrDefault(slug)))];
        foreach (var slug in changed) Changed?.Invoke(slug);
        return changed;
    }

    public void Dispose() => _cards.Changed -= OnChanged;

    private void OnChanged(string slug)
    {
        if (_cards.Get(slug)?.Choices is { } choices) _choices[slug] = choices;
        else _choices.TryRemove(slug, out _);
        Changed?.Invoke(slug);
    }
}
