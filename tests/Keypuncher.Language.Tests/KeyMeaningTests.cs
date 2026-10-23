using Keypuncher.Core;
using Keypuncher.Language;

namespace Keypuncher.Language.Tests;

public class KeyMeaningTests
{
    [Theory]
    [InlineData("´", "a", "á")]
    [InlineData("´", "y", "ý")]
    [InlineData("¨", "o", "ö")]
    [InlineData("´", "k", "k")]   // no ḱ in Icelandic: the letter comes through unchanged
    public void Accent_key_combines_with_the_next_letter(string accent, string next, string expected)
    {
        var c = new DeadKeyComposer();
        Assert.True(c.FeedDeadKey(accent));
        Assert.Equal(expected, c.Feed(next));
        Assert.Equal("a", c.Feed("a")); // the accent is used up
    }

    [Fact]
    public void Icelandic_letter_says_its_sound_clip_and_has_no_synthetic_fallback()
    {
        var m = KeyMeanings.For("is", "þ")!;
        Assert.Equal("Þ", m.Display);
        Assert.Equal(new ClipId("is", ClipKind.Letter, "þ"), m.Clip);
        Assert.Null(m.FallbackText);
        Assert.Equal('Þ', m.Letter);
    }

    [Fact]
    public void English_shows_Icelandic_only_keys_but_says_nothing()
    {
        var m = KeyMeanings.For("en", "ð")!;
        Assert.Equal("Ð", m.Display);
        Assert.Null(m.Clip);
    }

    [Theory]
    [InlineData("b", "Б")]
    [InlineData("ö", "Ж")]
    [InlineData("ú", "Ю")]
    [InlineData("á", "А")]   // accented vowels fall back to the plain letter's mapping
    public void Bulgarian_uses_the_phonetic_map(string key, string cyrillic)
    {
        var m = KeyMeanings.For("bg", key)!;
        Assert.Equal(cyrillic, m.Display);
        Assert.Equal(ClipKind.Letter, m.Clip!.Kind);
    }

    [Fact]
    public void Digits_count_in_each_language()
    {
        Assert.Equal("þrír", KeyMeanings.For("is", "3")!.FallbackText);
        Assert.Equal("three", KeyMeanings.For("en", "3")!.FallbackText);
        Assert.Equal("три", KeyMeanings.For("bg", "3")!.FallbackText);
        Assert.Equal(3, KeyMeanings.For("is", "3")!.Number);
    }

    [Fact]
    public void Punctuation_means_nothing_but_is_not_an_error()
    {
        Assert.Null(KeyMeanings.For("is", ","));
        Assert.Null(KeyMeanings.For("is", null));
    }

    [Fact]
    public void Language_mix_follows_the_weights()
    {
        var mix = new LanguageMix(new Dictionary<string, double> { ["is"] = 1, ["en"] = 0, ["bg"] = 0 });
        var r = new Random(3);
        Assert.All(Enumerable.Range(0, 50), _ => Assert.Equal("is", mix.Pick(r)));

        var picks = Enumerable.Range(0, 2000).Select(_ => LanguageMix.Default.Pick(r)).ToList();
        Assert.InRange(picks.Count(p => p == "is") / 2000.0, 0.6, 0.8);
    }

    [Fact]
    public void Recording_list_covers_letters_numbers_and_words()
    {
        var icelandic = ClipCatalog.For("is").ToList();
        Assert.Equal(32, icelandic.Count(e => e.Id.Kind == ClipKind.Letter && !e.Optional));
        Assert.Equal(4, icelandic.Count(e => e.Optional));
        Assert.Equal(10, icelandic.Count(e => e.Id.Kind == ClipKind.Number));
        Assert.Equal(StarterConcepts.All.Count, icelandic.Count(e => e.Id.Kind == ClipKind.Word));
        Assert.Equal(30, ClipCatalog.For("bg").Count(e => e.Id.Kind == ClipKind.Letter));
        Assert.Equal(26, ClipCatalog.For("en").Count(e => e.Id.Kind == ClipKind.Letter));
        Assert.Equal(icelandic.Count, icelandic.Select(e => e.Id).Distinct().Count());
    }
}
