using System.Runtime.InteropServices;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Keeps the display on, and the PC out of sleep, while <see cref="Active"/> is true: during play, so that a
/// child who pauses, or the goodnight's rest, never comes back to a dark screen or a sign-in. Without it the
/// power plan's timeout applies, because a swallowed key never reaches Windows and so does not count as use.
/// The request belongs to the thread that made it, so set <see cref="Active"/> from one thread (the UI thread);
/// Windows also drops it when that thread or the process ends, so a killed toy leaves nothing behind.
/// </summary>
public sealed class KeepAwake : IDisposable
{
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_DISPLAY_REQUIRED = 0x00000002;
    private const uint ES_CONTINUOUS = 0x80000000;

    private bool _active;

    /// <summary>True while the display is held on. Setting it to the value it has does nothing.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (value == _active) return;
            var flags = value ? ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED : ES_CONTINUOUS;
            // A zero return means the call failed; the toy still plays, the screen may just sleep as before.
            if (SetThreadExecutionState(flags) != 0) _active = value;
        }
    }

    /// <summary>Lets the display sleep again.</summary>
    public void Dispose() => Active = false;

    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint esFlags);
}
