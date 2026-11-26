using System.Runtime.InteropServices;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Turns off Windows' touch swipes in from the screen edges (Start, notifications, widgets, task view) over the
/// toy's window while it is full screen, so a child's taps and swipes stay in play. This is a property of the toy's
/// own window (<c>System.EdgeGesture.DisableTouchWhenFullscreen</c>), not a setting: nothing is stored, nothing needs
/// putting back, and it ends with the window. The small window shown after Quit is not full screen, so the swipes
/// work there again.
/// </summary>
public static class EdgeSwipes
{
    // PKEY_EdgeGesture_DisableTouchWhenFullscreen = {32CE38B2-2C9A-41B1-9BC5-B3784394AA44}, 2
    private static readonly Guid EdgeGestureKey = new("32CE38B2-2C9A-41B1-9BC5-B3784394AA44");
    private static readonly Guid IPropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private const ushort VT_BOOL = 11;
    private const short VARIANT_TRUE = -1;

    /// <summary>Turns the edge swipes off over <paramref name="window"/> while it is full screen. Never throws;
    /// returns false (and logs why) if Windows did not take it.</summary>
    public static bool TurnOff(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            var iid = IPropertyStoreId;
            var hr = SHGetPropertyStoreForWindow(window, ref iid, out var store);
            if (hr < 0 || store is null)
            {
                Log.Warn("windows", $"could not turn the touch edge swipes off (error 0x{hr:X8}); a swipe in from an edge can open Windows' panels");
                return false;
            }
            try
            {
                var key = new PROPERTYKEY { fmtid = EdgeGestureKey, pid = 2 };
                var value = new PROPVARIANT { vt = VT_BOOL, boolVal = VARIANT_TRUE };
                hr = store.SetValue(ref key, ref value);
                if (hr >= 0) hr = store.Commit();
                if (hr < 0)
                {
                    Log.Warn("windows", $"Windows refused to turn the touch edge swipes off (error 0x{hr:X8})");
                    return false;
                }
                return true;
            }
            finally { Marshal.ReleaseComObject(store); }
        }
        catch (Exception e) when (e is COMException or EntryPointNotFoundException or DllNotFoundException or InvalidCastException or NotSupportedException)
        {
            Log.Warn("windows", "could not turn the touch edge swipes off", e);
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    /// <summary>Only the VT_BOOL case is needed; the size covers the whole union on 64-bit.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public short boolVal;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? propertyStore);
}
