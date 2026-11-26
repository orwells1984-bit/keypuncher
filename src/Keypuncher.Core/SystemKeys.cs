namespace Keypuncher.Core;

/// <summary>
/// The keys and key combinations that make Windows itself do something (open Start, the Xbox Game Bar or the Xbox
/// full screen experience, switch or close windows, take a screenshot, change the volume, open a browser), as
/// opposed to typing into the window in front. The keyboard hook keeps these from Windows for as long as the toy
/// runs, on the grown-ups' pages and in the small window after Quit too, where ordinary typing does go through.
/// </summary>
/// <remarks>Virtual-key codes, so the list does not depend on the keyboard layout. Every Win key combination
/// is covered by keeping the Win key itself from Windows: Windows never sees it held, so Win+G (Game Bar), Win+F11
/// (Xbox full screen experience), Win+Alt+R (recording), Win+Tab, Win+D, Win+R, Win+X, Win+Ctrl+Enter (Narrator),
/// Win+Plus (Magnifier), Win+H, Win+V and the Copilot key (Win+Shift+F23) all become plain key presses.</remarks>
public static class SystemKeys
{
    public const int Tab = 0x09, Escape = 0x1B, Space = 0x20, PrintScreen = 0x2C, Help = 0x2F, F4 = 0x73;
    public const int Shift = 0x10, LeftShift = 0xA0, RightShift = 0xA1, Alt = 0x12, LeftAlt = 0xA4, RightAlt = 0xA5;
    public const int LeftWin = 0x5B, RightWin = 0x5C, Apps = 0x5D, Sleep = 0x5F;
    public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    /// <summary>Browser, volume, media and launcher keys: VK_BROWSER_BACK (0xA6) to VK_LAUNCH_APP2 (0xB7).</summary>
    public const int FirstMediaKey = 0xA6, LastMediaKey = 0xB7;
    public const int Play = 0xFA, Zoom = 0xFB;

    /// <summary>True for the Win keys themselves.</summary>
    public static bool IsWin(int vk) => vk is LeftWin or RightWin;

    /// <summary>True if Windows would act on this key rather than hand it to the window in front. Decided on the
    /// key going down; the hook gives a key's release the same answer as its press, so Windows never ends up
    /// thinking a key is held that is not.</summary>
    /// <param name="vk">The key's virtual-key code.</param>
    /// <param name="ctrl">A Ctrl key is held (as the hook tracks it).</param>
    /// <param name="shift">A Shift key is held.</param>
    /// <param name="alt">An Alt key is held. Right Alt is AltGr on the Icelandic layout (@, €, {), which Windows
    /// reports as Ctrl+Alt, so the Alt shortcuts below are only taken without Ctrl.</param>
    /// <param name="win">A Win key is held.</param>
    public static bool IsSystem(int vk, bool ctrl, bool shift, bool alt, bool win)
    {
        if (IsWin(vk) || win) return true;
        if (vk is Apps or Sleep or PrintScreen or Help or Play or Zoom) return true;
        if (vk is >= FirstMediaKey and <= LastMediaKey) return true;
        if (alt && !ctrl)
        {
            // Alt+Tab, Alt+Esc (switch windows), Alt+F4 (close), Alt+Space (window menu), and Alt+Shift, Windows'
            // shortcut for the next input language.
            if (vk is Tab or Escape or F4 or Space or Shift or LeftShift or RightShift) return true;
        }
        if (shift && !ctrl && vk is Alt or LeftAlt) return true;
        // Ctrl+Esc opens Start, Ctrl+Shift+Esc Task Manager; Ctrl+Alt+Del cannot be kept from Windows at all.
        if (ctrl && vk == Escape) return true;
        // Ctrl+Alt+arrow turns the screen on some graphics drivers. AltGr is Ctrl+Alt to Windows, and AltGr with an
        // arrow types nothing, so this costs the grown-ups nothing.
        if (ctrl && alt && vk is Left or Up or Right or Down) return true;
        return false;
    }
}
