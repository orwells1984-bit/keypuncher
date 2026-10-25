using System.Runtime.InteropServices;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Turns off the Sticky Keys, Filter Keys and Toggle Keys shortcuts (Shift five times, right Shift held
/// eight seconds, Num Lock held five seconds) for as long as the toy runs. Windows handles these before
/// any keyboard hook sees the keys, so a palm resting on Shift pops up a "turn on Filter Keys?" box that
/// takes the focus from the toy, and once Filter Keys is on, short presses are dropped. A feature the
/// grown-up already has switched on is left alone. The change is never written to the profile, so it
/// also goes away at sign-out if the toy is killed. Microsoft's own advice for games, see
/// "Disabling Shortcut Keys in Games".
/// </summary>
public sealed class AccessibilityShortcuts : IDisposable
{
    private const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
    private const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
    private const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;
    // The same bit values serve all three features (SKF_, FKF_, TKF_).
    private const uint On = 0x01, HotkeyActive = 0x04, ConfirmHotkey = 0x08;

    private STICKYKEYS _sticky = new() { cbSize = (uint)Marshal.SizeOf<STICKYKEYS>() };
    private TOGGLEKEYS _toggle = new() { cbSize = (uint)Marshal.SizeOf<TOGGLEKEYS>() };
    private FILTERKEYS _filter = new() { cbSize = (uint)Marshal.SizeOf<FILTERKEYS>() };
    private readonly bool _saved;
    private bool _restored;

    public AccessibilityShortcuts()
    {
        _saved = SystemParametersInfo(SPI_GETSTICKYKEYS, _sticky.cbSize, ref _sticky, 0)
            & SystemParametersInfo(SPI_GETTOGGLEKEYS, _toggle.cbSize, ref _toggle, 0)
            & SystemParametersInfo(SPI_GETFILTERKEYS, _filter.cbSize, ref _filter, 0);
        if (!_saved) return;

        if ((_sticky.dwFlags & On) == 0)
        {
            var off = _sticky;
            off.dwFlags &= ~(HotkeyActive | ConfirmHotkey);
            SystemParametersInfo(SPI_SETSTICKYKEYS, off.cbSize, ref off, 0);
        }
        if ((_toggle.dwFlags & On) == 0)
        {
            var off = _toggle;
            off.dwFlags &= ~(HotkeyActive | ConfirmHotkey);
            SystemParametersInfo(SPI_SETTOGGLEKEYS, off.cbSize, ref off, 0);
        }
        if ((_filter.dwFlags & On) == 0)
        {
            var off = _filter;
            off.dwFlags &= ~(HotkeyActive | ConfirmHotkey);
            SystemParametersInfo(SPI_SETFILTERKEYS, off.cbSize, ref off, 0);
        }
    }

    /// <summary>Puts the shortcuts back as they were.</summary>
    public void Dispose()
    {
        if (!_saved || _restored) return;
        _restored = true;
        SystemParametersInfo(SPI_SETSTICKYKEYS, _sticky.cbSize, ref _sticky, 0);
        SystemParametersInfo(SPI_SETTOGGLEKEYS, _toggle.cbSize, ref _toggle, 0);
        SystemParametersInfo(SPI_SETFILTERKEYS, _filter.cbSize, ref _filter, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STICKYKEYS { public uint cbSize, dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOGGLEKEYS { public uint cbSize, dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILTERKEYS { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref STICKYKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref TOGGLEKEYS pvParam, uint fWinIni);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref FILTERKEYS pvParam, uint fWinIni);
}
