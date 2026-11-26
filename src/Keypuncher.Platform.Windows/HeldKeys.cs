using System.Runtime.InteropServices;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// The keys Windows still thinks are held when the toy gives the keyboard back, and how long the keyboard and mouse
/// have been still.
/// </summary>
/// <remarks>In play the hook keeps every key from Windows, the releases too. A Shift, Ctrl, Alt or Win pressed just
/// before the toy came to the front (or while a mashing child held it as the toy changed pages) has its press in
/// Windows and its release swallowed, so Windows goes on treating it as held after the toy has closed: every key
/// then acts as Win+key or Ctrl+key. After the 2026-11-26 play the keyboard was dead until a restart.</remarks>
public static class HeldKeys
{
    private const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
        VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x01, KEYEVENTF_KEYUP = 0x02;

    private static readonly (int Vk, string Name, bool Extended)[] Modifiers =
    [
        (VK_LSHIFT, "left Shift", false), (VK_RSHIFT, "right Shift", false),
        (VK_LCONTROL, "left Ctrl", false), (VK_RCONTROL, "right Ctrl", true),
        (VK_LMENU, "left Alt", false), (VK_RMENU, "right Alt", true),
        (VK_LWIN, "left Win", true), (VK_RWIN, "right Win", true),
    ];

    /// <summary>Sends a release for each modifier Windows thinks is held, and logs which. The releases carry
    /// <see cref="KeyboardHook.OwnInput"/>, so the toy's own hook lets them through if it is still in. Returns the
    /// names released; never throws.</summary>
    public static IReadOnlyList<string> ReleaseModifiers(string when)
    {
        var released = new List<string>();
        try
        {
            foreach (var (vk, name, extended) in Modifiers)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
                var input = new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0), dwExtraInfo = (IntPtr)(nint)KeyboardHook.OwnInput },
                };
                if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1) released.Add(name);
                else Log.Warn("windows", $"could not release {name} {when} (error {Marshal.GetLastWin32Error()})");
            }
            Log.Info("windows", released.Count == 0 ? $"no key left held {when}" : $"released keys Windows still thought were held {when}: {string.Join(", ", released)}");
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException)
        {
            Log.Warn("windows", $"could not look for held keys {when}", e);
        }
        return released;
    }

    /// <summary>How long since the last key press, click or mouse move anywhere on this sign-in; null if Windows
    /// does not say.</summary>
    public static TimeSpan? SinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return null;
        // Both are milliseconds since start-up, the tick count wrapping every 49.7 days.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    // The union's largest member is MOUSEINPUT; its size is padded to it so SendInput accepts cbSize.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
        public KEYBDINPUT ki { readonly get => u.ki; set => u.ki = value; }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
