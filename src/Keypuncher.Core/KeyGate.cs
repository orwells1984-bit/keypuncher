namespace Keypuncher.Core;

/// <summary>How much of the keyboard the toy keeps from Windows at a given moment.</summary>
public enum KeyBlock
{
    /// <summary>Everything goes to Windows.</summary>
    None,
    /// <summary>Ordinary typing goes to Windows; the keys Windows itself acts on (<see cref="SystemKeys"/>) do not.</summary>
    SystemOnly,
    /// <summary>No key reaches Windows: play.</summary>
    All,
}

/// <summary>
/// Decides, key by key, whether the keyboard hook keeps a key from Windows, and makes sure a key's release always
/// goes the same way as its press. A press Windows saw with its release kept back leaves Windows thinking the key
/// is still held (a Shift or Ctrl held like that made every key a shortcut after the 2026-11-26 play), so a release
/// follows its press whatever the toy's mode has become meanwhile, and so does a held key's auto-repeat.
/// </summary>
/// <remarks>Not thread-safe: the hook calls it from its own thread only.</remarks>
public sealed class KeyGate
{
    private enum Seen : byte { Up, Passed, Kept }

    private readonly Seen[] _keys = new Seen[256];

    /// <summary>A Win key is down (kept from Windows or not).</summary>
    public bool Win => IsDown(SystemKeys.LeftWin) || IsDown(SystemKeys.RightWin);

    /// <summary>True if the hook should keep this key transition from Windows.</summary>
    /// <param name="vk">The virtual-key code.</param>
    /// <param name="down">A press (or auto-repeat) rather than a release.</param>
    /// <param name="ctrl">Ctrl is held, as the hook tracks it.</param>
    /// <param name="shift">Shift is held, as the hook tracks it.</param>
    /// <param name="alt">Alt is held, as Windows flags it on the key.</param>
    /// <param name="block">What the toy keeps from Windows right now.</param>
    public bool Keep(int vk, bool down, bool ctrl, bool shift, bool alt, KeyBlock block)
    {
        vk &= 0xFF;
        var seen = _keys[vk];
        if (!down)
        {
            _keys[vk] = Seen.Up;
            // A release whose press was never seen (held since before the toy started) goes to Windows: a stray
            // release does nothing, a missing one leaves a key stuck.
            return seen == Seen.Kept;
        }
        // Auto-repeat of a key already held goes where its first press went.
        if (seen != Seen.Up) return seen == Seen.Kept;
        var keep = block switch
        {
            KeyBlock.All => true,
            KeyBlock.SystemOnly => SystemKeys.IsSystem(vk, ctrl, shift, alt, Win),
            _ => false,
        };
        _keys[vk] = keep ? Seen.Kept : Seen.Passed;
        return keep;
    }

    private bool IsDown(int vk) => _keys[vk] != Seen.Up;
}
