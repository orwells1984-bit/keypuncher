using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Which window is in front, and bringing the toy's window back there. In play the keyboard hook keeps every key
/// from Windows even while another window is in front (a notification, an update prompt, the Xbox app); the toy then
/// asks for its window back, so taps and the screen are the child's again too.
/// </summary>
public static class Foreground
{
    /// <summary>The window in front, as "program: title", for the log; never throws.</summary>
    public static string Describe()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return "no window (the desktop is switching, or a secure screen such as Ctrl+Alt+Del is up)";
            GetWindowThreadProcessId(window, out var pid);
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            string program;
            try { using var process = Process.GetProcessById((int)pid); program = process.ProcessName; }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { program = $"process {pid}"; }
            return title.Length > 0 ? $"{program}: \"{title}\"" : program;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return "unknown";
        }
    }

    /// <summary>Asks for <paramref name="window"/> to come to the front. Windows refuses a program that is not
    /// in front already unless it borrows the input of the window that is, so this joins that window's input for a
    /// moment first. Returns true if the toy's window is in front afterwards; never throws.</summary>
    public static bool TryBringToFront(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        try
        {
            var front = GetForegroundWindow();
            if (front == window) return true;
            var ours = GetCurrentThreadId();
            var theirs = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
            var joined = theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true);
            try
            {
                if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
                BringWindowToTop(window);
                SetForegroundWindow(window);
            }
            finally
            {
                if (joined) AttachThreadInput(ours, theirs, false);
            }
            return GetForegroundWindow() == window;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Warn("windows", "could not bring the toy's window to the front", e);
            return false;
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
