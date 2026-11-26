using System.Runtime.InteropServices;
using System.Text;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// A low-level keyboard hook on its own thread (so a busy UI thread never delays keys or trips Windows' hook
/// timeout). Every key reaches the toy's handler first; <see cref="Blocking"/> then says how much of the keyboard is
/// kept from Windows (see <see cref="KeyGate"/>): in play all of it, even when another window has come in front;
/// elsewhere, as long as the toy runs, the keys Windows itself acts on (<see cref="SystemKeys"/>: the Win key and
/// every Win shortcut such as the Xbox Game Bar and full screen experience, Alt+Tab, Alt+F4, Ctrl+Esc, media and
/// launcher keys, Print Screen). Ctrl+Alt+Del, Win+L and keys a keyboard handles itself (Fn) cannot be kept from
/// Windows by any program; <see cref="LockShortcut"/> turns Win+L off another way. Characters are read with the
/// Icelandic layout whatever the account's layout is. First proven by the Phase 0 key spike (removed).
/// <para>Windows passes a key on by itself when a hook takes longer than about a second to answer, and after a few
/// such timeouts removes the hook without saying so; the toy would then let every key through for the rest of
/// the run. So the hook logs a key that reached it late, and is put in again every few seconds.</para>
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113;
    private const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_ALTDOWN = 0x20;
    /// <summary>How often the hook is put in again, in case Windows removed it after timeouts.</summary>
    private const uint RenewMilliseconds = 5000;
    /// <summary>A key this late reaching the hook was close to, or past, Windows' timeout.</summary>
    private const int LateMilliseconds = 500;
    private const uint ToUnicodeKeepKeyboardState = 0x04;
    private const string IcelandicLayout = "0000040F";

    private readonly Action<KeyPress, bool> _onKey;
    private readonly KeyGate _gate = new();
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
    private long _lastLateLog;

    /// <param name="onKey">Called on the hook thread for every key, with whether the toy's window is in front;
    /// keep it short (enqueue and return).</param>
    /// <param name="window">The toy's top-level window handle.</param>
    public KeyboardHook(Action<KeyPress, bool> onKey, Func<IntPtr> window)
    {
        _onKey = onKey;
        _window = window;
        _proc = Callback;
        _thread = new Thread(Run) { IsBackground = true, Name = "Keyboard hook" };
        _thread.Start();
        _started.Wait();
    }

    /// <summary>How much of the keyboard Windows is kept from, per key, given whether the toy's window is in front.
    /// Keys always reach the handler first. Called on the hook thread, so it must be quick and thread-safe.</summary>
    public Func<KeyPress, bool, KeyBlock> Blocking { get; set; } = static (_, _) => KeyBlock.All;

    /// <summary>The value the toy's own synthesized keys carry, so the hook lets them through (see
    /// <see cref="HeldKeys"/>).</summary>
    public const nuint OwnInput = 0x4B50_5543; // "KPUC"

    /// <summary>False if the Icelandic layout could not be loaded and the account's layout is used instead.</summary>
    public bool UsingIcelandicLayout { get; private set; }

    public void Dispose()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }

    private void Run()
    {
        // Nothing may escape this thread: an exception here would end the whole toy.
        try
        {
            _threadId = GetCurrentThreadId();
            _layout = LoadKeyboardLayout(IcelandicLayout, 0);
            UsingIcelandicLayout = _layout != IntPtr.Zero;
            if (!UsingIcelandicLayout)
            {
                _layout = GetKeyboardLayout(0);
                Log.Warn("keyboard", "the Icelandic layout could not be loaded; using this account's layout");
            }
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            _started.Set();
            if (_hook == IntPtr.Zero)
            {
                Log.Error("keyboard", "the keyboard hook could not be installed: keys reach Windows and the toy sees none");
                return;
            }

            var timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, RenewMilliseconds, IntPtr.Zero);
            if (timer == UIntPtr.Zero) Log.Warn("keyboard", "the timer that puts the hook in again could not be set");
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_TIMER && msg.hwnd == IntPtr.Zero) { Renew(); continue; }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (timer != UIntPtr.Zero) KillTimer(IntPtr.Zero, timer);
            UnhookWindowsHookEx(_hook);
            Log.Info("keyboard", "keyboard hook removed");
        }
        catch (Exception e)
        {
            Log.Error("keyboard", "the keyboard hook's thread failed; keys reach Windows from now on", e);
            _started.Set();
        }
    }

    /// <summary>Puts a new hook in, then takes the old one out, so keys are never left unhooked in between. If the
    /// old one is already gone, Windows removed it (it answered too slowly too often) and keys went through.</summary>
    private void Renew()
    {
        var fresh = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (fresh == IntPtr.Zero)
        {
            Log.Warn("keyboard", $"the keyboard hook could not be put in again (error {Marshal.GetLastWin32Error()}); the old one stays");
            return;
        }
        var old = _hook;
        _hook = fresh;
        if (!UnhookWindowsHookEx(old))
            Log.Warn("keyboard", $"Windows had taken the keyboard hook out (error {Marshal.GetLastWin32Error()}), most likely after it answered too slowly; it is back in now, but keys went to Windows meanwhile");
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // An exception must never travel back into Windows from here.
        try { return Handle(nCode, wParam, lParam); }
        catch (Exception e)
        {
            Log.Error("keyboard", "the keyboard hook failed on a key; it goes to Windows", e);
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }

    private IntPtr Handle(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        // The toy's own synthesized keys (the releases at the end) go straight to Windows.
        if ((k.flags & LLKHF_INJECTED) != 0 && k.dwExtraInfo == OwnInput) return CallNextHookEx(_hook, nCode, wParam, lParam);
        NoteLateness(k.time);

        var down = (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN;
        var scan = (int)k.scanCode | ((k.flags & LLKHF_EXTENDED) != 0 ? 0xE000 : 0);
        var (character, dead) = CharacterFor(k.vkCode, k.scanCode);
        var alt = (k.flags & LLKHF_ALTDOWN) != 0;
        // Modifiers as they were before this key: a kept key never reaches Windows' key state, so GetAsyncKeyState
        // would report Ctrl and Shift as up while they are held.
        var (ctrl, shift) = (_ctrl, _shift);
        switch (k.vkCode)
        {
            case 0x11 or 0xA2 or 0xA3: _ctrl = down; break;
            case 0x10 or 0xA0 or 0xA1: _shift = down; break;
        }

        var front = GetForegroundWindow() == _window();
        var press = new KeyPress(DateTimeOffset.Now, scan, character, dead, down, Ctrl: _ctrl, Shift: _shift, Alt: alt);
        var block = KeyBlock.All;
        try
        {
            _onKey(press, front);
            block = Blocking(press, front);
        }
        catch (Exception e)
        {
            // Never let a handler bug take the keyboard down with it. The log writes on its own thread, so this
            // does not hold up the hook.
            Log.Error("keyboard", $"handling key {press.ScanCode:X} ({(down ? "down" : "up")}) failed", e);
        }
        return _gate.Keep((int)k.vkCode, down, ctrl, shift, alt, block) ? 1 : CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>Logs, at most every 10 s, a key that reached the hook late: the process was held up (a long
    /// garbage collection, say), and past about a second Windows hands the key on by itself.</summary>
    private void NoteLateness(uint keyTime)
    {
        var late = unchecked((int)((uint)Environment.TickCount - keyTime));
        if (late < LateMilliseconds || late > 60_000) return;
        var now = Environment.TickCount64;
        if (now - _lastLateLog < 10_000) return;
        _lastLateLog = now;
        Log.Warn("keyboard", $"a key reached the keyboard hook {late} ms late; Windows hands a key on by itself after about a second, so keys may have reached Windows");
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
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
