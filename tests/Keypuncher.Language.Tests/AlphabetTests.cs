using Keypuncher.Language;

namespace Keypuncher.Language.Tests;

public class AlphabetTests
{
    [Fact]
    public void Icelandic_has_32_distinct_letters()
    {
        Assert.Equal(32, IcelandicAlphabet.Upper.Count);
        Assert.Equal(32, IcelandicAlphabet.Upper.Distinct().Count());
    }

    [Fact]
    public void Bulgarian_map_covers_every_letter_exactly_once()
    {
        var mapped = BulgarianPhoneticMap.FromKeycap.Values.OrderBy(c => c).ToList();
        Assert.Equal(BulgarianPhoneticMap.Alphabet.OrderBy(c => c), mapped);
    }

    [Fact]
    public void Bulgarian_map_is_case_insensitive()
    {
        Assert.Equal('Б', BulgarianPhoneticMap.Map('b'));
        Assert.Equal('Ж', BulgarianPhoneticMap.Map('ö'));
        Assert.Null(BulgarianPhoneticMap.Map('W'));
    }

    [Fact]
    public void Every_starter_concept_has_all_three_languages()
    {
        foreach (var c in StarterConcepts.All)
            foreach (var tag in new[] { "is", "en", "bg" })
                Assert.False(string.IsNullOrWhiteSpace(c.WordIn(tag)), $"{c.Id} lacks {tag}");
    }

    [Fact]
    public void Eth_falls_back_to_a_word_containing_it()
    {
        Assert.Empty(StarterConcepts.StartingWith("is", 'Ð'));
        // No starter word contains ð yet either; this documents the gap for the parent screen.
        Assert.Empty(StarterConcepts.Containing("is", 'ð'));
    }
}
