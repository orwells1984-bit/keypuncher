using System.Runtime.InteropServices;
using System.Text;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// A low-level keyboard hook on its own thread (so a busy UI thread never delays keys or trips
/// Windows' hook timeout). While the toy's window is in front, keys that <see cref="ShouldSwallow"/> picks
/// (in play: all of them) are kept from Windows: Win, Alt+Tab, Alt+F4, Ctrl+Esc, media keys and all. Ctrl+Alt+Del and
/// firmware Fn keys cannot be blocked. Characters are read with the Icelandic layout whatever the
/// account's layout is. Proven by tools/Keypuncher.KeySpike.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012;
    private const uint LLKHF_EXTENDED = 0x01;
    private const uint ToUnicodeKeepKeyboardState = 0x04;
    private const string IcelandicLayout = "0000040F";

    private readonly Action<KeyPress> _onKey;
    private readonly Func<IntPtr> _window;
    private readonly LowLevelKeyboardProc _proc;
    private readonly byte[] _keyState = new byte[256];
    private readonly StringBuilder _buffer = new(8);
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private IntPtr _hook;
    private IntPtr _layout;
    private uint _threadId;
    private bool _ctrl, _shift;

    /// <param name="onKey">Called on the hook thread; keep it short (enqueue and return).</param>
    /// <param name="window">The toy's top-level window handle; keys are only kept while it is in front.</param>
    public KeyboardHook(Action<KeyPress> onKey, Func<IntPtr> window)
    {
        _onKey = onKey;
        _window = window;
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Keyboard hook" };
        _thread.Start();
        _started.Wait();
    }

    /// <summary>Decides, per key, whether Windows also gets it. Keys always reach the handler first.
    /// Called on the hook thread, so it must be quick and thread-safe.</summary>
    public Func<KeyPress, bool> ShouldSwallow { get; set; } = static _ => true;

    /// <summary>False if the Icelandic layout could not be loaded and the account's layout is used instead.</summary>
    public bool UsingIcelandicLayout { get; private set; }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _layout = LoadKeyboardLayout(IcelandicLayout, 0);
        UsingIcelandicLayout = _layout != IntPtr.Zero;
        if (!UsingIcelandicLayout) _layout = GetKeyboardLayout(0);
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        _started.Set();
        if (_hook == IntPtr.Zero) return;

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        UnhookWindowsHookEx(_hook);
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        var down = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
        var scan = (int)k.scanCode | ((k.flags & LLKHF_EXTENDED) != 0 ? 0xE000 : 0);
        var (character, dead) = CharacterFor(k.vkCode, k.scanCode);

        // Track modifiers ourselves: a key this hook swallows never reaches Windows' key state, so
        // GetAsyncKeyState would report Ctrl and Shift as up while they are held.
        switch (k.vkCode)
        {
            case 0x11 or 0xA2 or 0xA3: _ctrl = down; break;
            case 0x10 or 0xA0 or 0xA1: _shift = down; break;
        }

        if (GetForegroundWindow() != _window()) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var press = new KeyPress(DateTimeOffset.Now, scan, character, dead, down,
            Ctrl: _ctrl,
            Shift: _shift,
            Alt: (k.flags & 0x20) != 0);
        var swallow = true;
        try
        {
            _onKey(press);
            swallow = ShouldSwallow(press);
        }
        catch (Exception)
        {
            // Never let a handler bug take the keyboard down with it.
        }
        return swallow ? 1 : CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>The unshifted character the Icelandic layout gives the key; for a dead key, the accent.</summary>
    private (string? Character, bool Dead) CharacterFor(uint vk, uint scan)
    {
        Array.Clear(_keyState);
        _buffer.Clear();
        var n = ToUnicodeEx(vk, scan, _keyState, _buffer, _buffer.Capacity, ToUnicodeKeepKeyboardState, _layout);
        if (n < 0) return (_buffer.Length > 0 ? _buffer.ToString(0, 1) : "´", true);
        if (n == 0) return (null, false);
        var s = _buffer.ToString(0, n);
        // Control characters (Enter, Tab, Backspace, Esc) are keys without a character for the toy.
        return (s.Length == 1 && char.IsControl(s[0]) ? null : s, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public UIntPtr wParam; public IntPtr lParam; public uint time; public int ptX, ptY; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
