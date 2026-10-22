// Phase 0 spike: can we see every key on the Icelandic layout and swallow the keys that leave the app?
//
// Run on Windows:  dotnet run --project tools/Keypuncher.KeySpike
// Press keys; each line shows scancode, virtual key, and the character ToUnicodeEx gives it under
// the Icelandic layout. Win, Alt+Tab, Alt+F4, Alt+Esc and Ctrl+Esc are swallowed while the spike runs.
// Press Ctrl+Shift+Q to quit (the stand-in for the grown-up exit). Ctrl+Alt+Del cannot be blocked.
//
// This is a console app rather than the MAUI app on purpose: it proves the hook and layout reading
// with no UI in the way. The same hook moves into Keypuncher.App/Platforms/Windows in Phase 1.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("windows")]

internal static class Program
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const uint KLF_ACTIVATE = 1;
    private const string IcelandicLayout = "0000040F";

    private static IntPtr _hook;
    private static IntPtr _icelandic;
    private static readonly LowLevelKeyboardProc Proc = HookCallback; // kept alive for the hook's lifetime
    private static readonly byte[] KeyState = new byte[256];
    private static int _count;

    private static int Main()
    {
        _icelandic = LoadKeyboardLayout(IcelandicLayout, KLF_ACTIVATE);
        if (_icelandic == IntPtr.Zero)
        {
            Console.Error.WriteLine("Icelandic layout (0000040F) is not installed on this PC; add it in Windows settings first.");
            return 2;
        }

        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            Console.Error.WriteLine($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            return 1;
        }

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("Hook installed. Icelandic layout forced. Mash away; Ctrl+Shift+Q quits.");
        Console.WriteLine("scan  vk    char  note");

        // A low-level hook needs a message loop on the installing thread.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hook);
        Console.WriteLine($"{_count} key-downs seen.");
        return 0;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var msg = (int)wParam;
        var down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        var alt = (k.flags & 0x20) != 0;
        var ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        var shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;

        if (down && ctrl && shift && k.vkCode == 'Q')
        {
            PostQuitMessage(0);
            return 1;
        }

        // Keys that leave the app: swallow them. (Ctrl+Alt+Del is handled before any hook sees it.)
        var swallow = k.vkCode is 0x5B or 0x5C                     // Win keys
                   || (alt && k.vkCode is 0x09 or 0x73 or 0x1B)    // Alt+Tab, Alt+F4, Alt+Esc
                   || (ctrl && k.vkCode == 0x1B)                   // Ctrl+Esc
                   || k.vkCode is 0x2C                             // PrintScreen
                   || k.vkCode is >= 0xA6 and <= 0xB7;             // browser and media keys

        if (down && !swallow)
        {
            var ch = CharacterFor(k.vkCode, k.scanCode, shift);
            var note = ch is null ? "non-character" : ch.Length == 0 ? "dead key (accent)" : "";
            Console.WriteLine($"{k.scanCode,4}  0x{k.vkCode:X2}  {(ch is { Length: > 0 } ? ch : "·"),-4}  {note}");
            _count++;
        }
        else if (down)
        {
            Console.WriteLine($"{k.scanCode,4}  0x{k.vkCode:X2}  ·     swallowed");
        }

        return swallow ? 1 : CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Returns the character, "" for a dead key, or null for a key with no character.</summary>
    private static string? CharacterFor(uint vk, uint scan, bool shift)
    {
        Array.Clear(KeyState);
        if (shift) KeyState[0x10] = 0x80;
        var buf = new StringBuilder(8);
        var n = ToUnicodeEx(vk, scan, KeyState, buf, buf.Capacity, 0, _icelandic);
        if (n < 0)
        {
            // Dead key: clear the layout's dead-key state so the next key is not combined.
            ToUnicodeEx(vk, scan, KeyState, buf, buf.Capacity, 0, _icelandic);
            return "";
        }
        return n == 0 ? null : buf.ToString(0, n);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int ptX, ptY; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int nExitCode);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
