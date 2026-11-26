using System.Runtime.InteropServices;
using Microsoft.Win32;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Keeps a mashing child from switching on Sticky Keys (Shift five times), Filter Keys (right Shift held eight
/// seconds), Toggle Keys (Num Lock held five seconds), Mouse Keys (left Alt + left Shift + Num Lock) or High Contrast
/// (left Alt + left Shift + Print Screen, which changes every colour on the screen). Windows handles these shortcuts before any keyboard hook
/// sees the keys, so the toy turns the shortcuts themselves off, from start-up until the process ends: in play, on
/// the grown-ups' page and in the small window that finishes the pictures after Quit, when the keyboard is back with
/// Windows but the child may still be at it (that window is where Sticky Keys got turned on on 2026-11-25, and left
/// Shift latched so left clicks opened new windows). Microsoft's own advice for games, see "Disabling Shortcut Keys
/// in Games".
/// <para>Sticky Keys or Filter Keys already on at start-up is turned off and left off when the toy closes: in this
/// home nobody uses them, so the likeliest reason is a child's earlier mashing, and a latched Shift or dropped short
/// presses would spoil play and the computer afterwards. Toggle Keys only beeps on Caps Lock, so it is left on.</para>
/// <para>The change is made for this sign-in only, never written to the profile. The flags it replaced are noted
/// under <c>HKCU\Software\Keypuncher</c> first, so a toy that was killed has the shortcuts put back by its next
/// start, as <see cref="LockShortcut"/> does for Win+L.</para>
/// </summary>
public sealed class AccessibilityShortcuts : IDisposable
{
    private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
    private const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
    private const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;
    private const uint SPI_GETMOUSEKEYS = 0x0036, SPI_SETMOUSEKEYS = 0x0037;
    private const uint SPI_GETHIGHCONTRAST = 0x0042, SPI_SETHIGHCONTRAST = 0x0043;
    // The same bit values serve all five features (SKF_, FKF_, TKF_, MKF_, HCF_).
    private const uint On = 0x01, HotkeyActive = 0x04, ConfirmHotkey = 0x08;
    private const uint Shortcut = HotkeyActive | ConfirmHotkey;

    private const string NoteKey = @"Software\Keypuncher";
    private const string StickyNote = "StickyKeysWas", FilterNote = "FilterKeysWas", ToggleNote = "ToggleKeysWas";
    private const string MouseNote = "MouseKeysWas", ContrastNote = "HighContrastWas";

    /// <summary>The flags put back at the end: as found (or as a killed run noted them), less any feature this run
    /// turned off.</summary>
    private uint _stickyWas, _filterWas, _toggleWas;
    /// <summary>Mouse Keys and High Contrast: their flags to put back, or null when this run left them alone.</summary>
    private uint? _mouseWas, _contrastWas;
    private readonly bool _saved;
    private bool _restored;

    public AccessibilityShortcuts()
    {
        var sticky = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
        var toggle = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf<TOGGLEKEYS>() };
        var filter = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
        _saved = SystemParametersInfo(SPI_GETSTICKYKEYS, sticky.cbSize, ref sticky, 0)
            & SystemParametersInfo(SPI_GETTOGGLEKEYS, toggle.cbSize, ref toggle, 0)
            & SystemParametersInfo(SPI_GETFILTERKEYS, filter.cbSize, ref filter, 0);
        if (!_saved)
        {
            Log.Warn("windows", "could not read the Sticky Keys settings; a child pressing Shift five times can turn Sticky Keys on");
            return;
        }

        // A run that was killed left the shortcuts off; what it noted is what the grown-up had.
        var noted = ReadNote();
        _stickyWas = Plan.Original(sticky.dwFlags, noted?.Sticky);
        _filterWas = Plan.Original(filter.dwFlags, noted?.Filter);
        _toggleWas = Plan.Original(toggle.dwFlags, noted?.Toggle);
        if (noted is not null) Log.Info("windows", "the last run did not close properly; its note of the Sticky Keys settings is used");
        // Mouse Keys and High Contrast are only ever stopped from being switched on by their shortcuts; whether
        // they are on is the grown-up's business. High Contrast that is on is left alone completely, since setting
        // it again can repaint the screen.
        var mouse = new MOUSEKEYS { cbSize = (uint)Marshal.SizeOf<MOUSEKEYS>() };
        if (SystemParametersInfo(SPI_GETMOUSEKEYS, mouse.cbSize, ref mouse, 0))
            _mouseWas = Plan.Original(mouse.dwFlags, noted?.Mouse);
        if (ReadContrast() is { } contrast && (contrast & On) == 0)
            _contrastWas = Plan.Original(contrast, noted?.Contrast);

        var turnedOff = new List<string>();
        if ((sticky.dwFlags & On) != 0) turnedOff.Add("Sticky Keys");
        if ((filter.dwFlags & On) != 0) turnedOff.Add("Filter Keys");
        _stickyWas = Plan.AtEnd(_stickyWas, turnFeatureOff: true);
        _filterWas = Plan.AtEnd(_filterWas, turnFeatureOff: true);
        _toggleWas = Plan.AtEnd(_toggleWas, turnFeatureOff: false);
        WriteNote();

        if (_mouseWas is not null)
        {
            mouse.dwFlags = Plan.DuringRun(mouse.dwFlags, turnFeatureOff: false);
            if (!SystemParametersInfo(SPI_SETMOUSEKEYS, mouse.cbSize, ref mouse, 0)) Log.Warn("windows", "Windows refused to turn the Mouse Keys shortcut off");
        }
        if (_contrastWas is { } contrastWas && !WriteContrast(Plan.DuringRun(contrastWas, turnFeatureOff: false)))
            Log.Warn("windows", "Windows refused to turn the High Contrast shortcut off");

        sticky.dwFlags = Plan.DuringRun(sticky.dwFlags, turnFeatureOff: true);
        filter.dwFlags = Plan.DuringRun(filter.dwFlags, turnFeatureOff: true);
        toggle.dwFlags = Plan.DuringRun(toggle.dwFlags, turnFeatureOff: false);
        var ok = SystemParametersInfo(SPI_SETSTICKYKEYS, sticky.cbSize, ref sticky, 0)
            & SystemParametersInfo(SPI_SETFILTERKEYS, filter.cbSize, ref filter, 0)
            & SystemParametersInfo(SPI_SETTOGGLEKEYS, toggle.cbSize, ref toggle, 0);
        if (!ok) Log.Warn("windows", "Windows refused part of turning the Sticky, Filter and Toggle Keys shortcuts off");
        Summary = turnedOff.Count == 0
            ? "Sticky, Filter, Toggle and Mouse Keys and High Contrast shortcuts turned off until the toy closes"
            : $"Sticky, Filter, Toggle and Mouse Keys and High Contrast shortcuts turned off until the toy closes; {string.Join(" and ", turnedOff)} "
              + "was on and is now off, and stays off afterwards";
    }

    /// <summary>One line for the log on what was changed.</summary>
    public string Summary { get; } = "Sticky Keys shortcuts left as they were";

    /// <summary>Puts the shortcuts back as they were. Call only when the process is about to end.</summary>
    public void Dispose()
    {
        if (!_saved || _restored) return;
        _restored = true;
        var sticky = new STICKYKEYS { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
        var toggle = new TOGGLEKEYS { cbSize = (uint)Marshal.SizeOf<TOGGLEKEYS>() };
        var filter = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
        // Read again so the other Filter Keys timings, and anything a grown-up changed meanwhile, are kept.
        SystemParametersInfo(SPI_GETSTICKYKEYS, sticky.cbSize, ref sticky, 0);
        SystemParametersInfo(SPI_GETTOGGLEKEYS, toggle.cbSize, ref toggle, 0);
        SystemParametersInfo(SPI_GETFILTERKEYS, filter.cbSize, ref filter, 0);
        sticky.dwFlags = Plan.Restore(sticky.dwFlags, _stickyWas);
        filter.dwFlags = Plan.Restore(filter.dwFlags, _filterWas);
        toggle.dwFlags = Plan.Restore(toggle.dwFlags, _toggleWas);
        SystemParametersInfo(SPI_SETSTICKYKEYS, sticky.cbSize, ref sticky, 0);
        SystemParametersInfo(SPI_SETTOGGLEKEYS, toggle.cbSize, ref toggle, 0);
        SystemParametersInfo(SPI_SETFILTERKEYS, filter.cbSize, ref filter, 0);
        if (_mouseWas is { } mouseWas)
        {
            var mouse = new MOUSEKEYS { cbSize = (uint)Marshal.SizeOf<MOUSEKEYS>() };
            if (SystemParametersInfo(SPI_GETMOUSEKEYS, mouse.cbSize, ref mouse, 0))
            {
                mouse.dwFlags = Plan.RestoreShortcut(mouse.dwFlags, mouseWas);
                SystemParametersInfo(SPI_SETMOUSEKEYS, mouse.cbSize, ref mouse, 0);
            }
        }
        // High Contrast a grown-up switched on meanwhile is left alone.
        if (_contrastWas is { } contrastWas && ReadContrast() is { } contrast && (contrast & On) == 0)
            WriteContrast(Plan.RestoreShortcut(contrast, contrastWas));
        DeleteNote();
    }

    /// <summary>The flag arithmetic, kept apart from Windows so it can be read (and tested) on its own.</summary>
    public static class Plan
    {
        /// <summary>What the grown-up had: a killed run's note when there is one, else what Windows says now.</summary>
        public static uint Original(uint now, uint? noted) => noted ?? now;

        /// <summary>The flags while the toy runs: no shortcut, and no Sticky or Filter Keys.</summary>
        public static uint DuringRun(uint flags, bool turnFeatureOff) =>
            flags & ~Shortcut & (turnFeatureOff ? ~On : ~0u);

        /// <summary>The flags to put back at the end: the original shortcut, but Sticky or Filter Keys stays off.</summary>
        public static uint AtEnd(uint original, bool turnFeatureOff) => turnFeatureOff ? original & ~On : original;

        /// <summary>Puts back the shortcut and on/off bits; every other bit is kept as it is now.</summary>
        public static uint Restore(uint now, uint atEnd) => (now & ~(Shortcut | On)) | (atEnd & (Shortcut | On));

        /// <summary>Puts back only the shortcut bits (Mouse Keys, High Contrast); the rest is kept as it is now.</summary>
        public static uint RestoreShortcut(uint now, uint original) => (now & ~Shortcut) | (original & Shortcut);
    }

    private sealed record Note(uint Sticky, uint Filter, uint Toggle, uint? Mouse, uint? Contrast);

    private static Note? ReadNote()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NoteKey);
            return key?.GetValue(StickyNote) is int s && key.GetValue(FilterNote) is int f && key.GetValue(ToggleNote) is int t
                ? new Note((uint)s, (uint)f, (uint)t,
                    key.GetValue(MouseNote) is int m ? (uint)m : null, key.GetValue(ContrastNote) is int c ? (uint)c : null)
                : null;
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("windows", "could not read the note of the Sticky Keys settings", e);
            return null;
        }
    }

    private void WriteNote()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(NoteKey);
            key.SetValue(StickyNote, unchecked((int)_stickyWas), RegistryValueKind.DWord);
            key.SetValue(FilterNote, unchecked((int)_filterWas), RegistryValueKind.DWord);
            key.SetValue(ToggleNote, unchecked((int)_toggleWas), RegistryValueKind.DWord);
            if (_mouseWas is { } mouse) key.SetValue(MouseNote, unchecked((int)mouse), RegistryValueKind.DWord);
            if (_contrastWas is { } contrast) key.SetValue(ContrastNote, unchecked((int)contrast), RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            // Not fatal: the change is for this sign-in only, so signing out puts the shortcuts back anyway.
            Log.Warn("windows", "could not note the Sticky Keys settings; if the toy is killed, they come back at sign-out", e);
        }
    }

    private static void DeleteNote()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NoteKey, writable: true);
            if (key is null) return;
            key.DeleteValue(StickyNote, throwOnMissingValue: false);
            key.DeleteValue(FilterNote, throwOnMissingValue: false);
            key.DeleteValue(ToggleNote, throwOnMissingValue: false);
            key.DeleteValue(MouseNote, throwOnMissingValue: false);
            key.DeleteValue(ContrastNote, throwOnMissingValue: false);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("windows", "could not remove the note of the Sticky Keys settings", e);
        }
    }

    /// <summary>High Contrast's flags, or null if Windows does not say. Its structure carries the name of the
    /// colour scheme, which Windows writes into a buffer the caller lends it, and which is handed back unchanged.</summary>
    private static uint? ReadContrast()
    {
        var (ok, flags) = WithContrast(null);
        return ok ? flags : null;
    }

    private static bool WriteContrast(uint flags) => WithContrast(flags).Ok;

    private static (bool Ok, uint Flags) WithContrast(uint? newFlags)
    {
        const int Chars = 260;
        var buffer = Marshal.AllocHGlobal(Chars * 2);
        try
        {
            var contrast = new HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<HIGHCONTRAST>(), lpszDefaultScheme = buffer };
            Marshal.WriteInt16(buffer, 0);
            if (!SystemParametersInfo(SPI_GETHIGHCONTRAST, contrast.cbSize, ref contrast, 0)) return (false, 0);
            if (newFlags is not { } flags) return (true, contrast.dwFlags);
            contrast.dwFlags = flags;
            return (SystemParametersInfo(SPI_SETHIGHCONTRAST, contrast.cbSize, ref contrast, 0), flags);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STICKYKEYS { public uint cbSize, dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEKEYS { public uint cbSize, dwFlags, iMaxSpeed, iTimeToMaxSpeed, iCtrlSpeed, dwReserved1, dwReserved2; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIGHCONTRAST { public uint cbSize, dwFlags; public IntPtr lpszDefaultScheme; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOGGLEKEYS { public uint cbSize, dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILTERKEYS { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref TOGGLEKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref FILTERKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref MOUSEKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);
}
