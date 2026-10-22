using System.Text;

namespace Keypuncher.Language;

/// <summary>
/// Combines an accent key with the next letter the way the Icelandic layout does (´ then a gives á).
/// The keyboard hook clears Windows' own dead-key state so it can show something for the accent key
/// on its own; this puts the combining back.
/// </summary>
public sealed class DeadKeyComposer
{
    private static readonly Dictionary<string, char> Combining = new()
    {
        ["´"] = '́', ["'"] = '́', ["`"] = '̀', ["¨"] = '̈',
        ["^"] = '̂', ["~"] = '̃', ["°"] = '̊',
    };

    private char? _pending;

    /// <summary>Feeds a dead key. Returns false if the accent is not one this composer knows.</summary>
    public bool FeedDeadKey(string accent)
    {
        _pending = Combining.TryGetValue(accent, out var c) ? c : null;
        return _pending is not null;
    }

    /// <summary>Feeds a normal key's character and returns what it becomes: the accented letter if an
    /// accent is pending and the pair makes an Icelandic letter (á, ö, ý...), otherwise the character
    /// unchanged, as Windows' own Icelandic layout would.</summary>
    public string? Feed(string? character)
    {
        var pending = _pending;
        _pending = null;
        if (pending is null || character is not { Length: 1 }) return character;

        var composed = (character + pending).Normalize(NormalizationForm.FormC);
        return composed.Length == 1 && IcelandicAlphabet.Contains(composed[0]) ? composed : character;
    }
}
