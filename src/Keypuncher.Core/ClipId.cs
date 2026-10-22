namespace Keypuncher.Core;

public enum ClipKind { Letter, Number, Word }

/// <summary>Names one spoken clip: a letter sound, a number or a word, in one language.</summary>
/// <param name="Language">"is", "en" or "bg".</param>
/// <param name="Key">The lowercase letter, the digit, or the concept id.</param>
public sealed record ClipId(string Language, ClipKind Kind, string Key)
{
    public override string ToString() => $"{Language}/{Kind.ToString().ToLowerInvariant()}/{Key}";
}
