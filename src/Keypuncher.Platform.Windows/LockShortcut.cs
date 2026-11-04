using Microsoft.Win32;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Turns off Win+L (and Lock on the Ctrl+Alt+Del screen) for as long as the toy runs. Windows locks the
/// screen before any keyboard hook sees the keys, so a baby's Win+L stopped play on 2026-11-04. This sets
/// the per-user policy <c>DisableLockWorkstation</c>, which needs no admin rights and takes effect at once.
/// <para>Unlike the Sticky Keys shortcuts, the policy is stored in the profile, so a killed toy would leave
/// locking off. The value it replaced is therefore noted under <c>HKCU\Software\Keypuncher</c> first, and the
/// next start puts it back before doing anything else. A grown-up who already turned locking off is left alone.</para>
/// </summary>
public sealed class LockShortcut : IDisposable
{
    private const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string PolicyValue = "DisableLockWorkstation";
    private const string NoteKey = @"Software\Keypuncher";
    private const string NoteValue = "LockWorkstationWas";
    /// <summary>Noted when the policy value did not exist, so restoring deletes it.</summary>
    private const int Absent = -1;

    private bool _changed;

    public LockShortcut()
    {
        try
        {
            RestoreFromNote();
            using var policy = Registry.CurrentUser.CreateSubKey(PolicyKey);
            var was = policy.GetValue(PolicyValue) is int v ? v : Absent;
            if (was == 1) return;
            using (var note = Registry.CurrentUser.CreateSubKey(NoteKey))
                note.SetValue(NoteValue, was, RegistryValueKind.DWord);
            policy.SetValue(PolicyValue, 1, RegistryValueKind.DWord);
            _changed = true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // A locked-down account keeps Win+L; the toy still plays.
        }
    }

    /// <summary>True while this run has locking turned off.</summary>
    public bool Active => _changed;

    /// <summary>Puts locking back as it was.</summary>
    public void Dispose()
    {
        if (!_changed) return;
        _changed = false;
        try { RestoreFromNote(); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    /// <summary>Puts back the value a previous run replaced, if it noted one, and removes the note.</summary>
    private static void RestoreFromNote()
    {
        using var note = Registry.CurrentUser.OpenSubKey(NoteKey, writable: true);
        if (note?.GetValue(NoteValue) is not int was) return;
        using (var policy = Registry.CurrentUser.CreateSubKey(PolicyKey))
        {
            if (was == Absent) policy.DeleteValue(PolicyValue, throwOnMissingValue: false);
            else policy.SetValue(PolicyValue, was, RegistryValueKind.DWord);
        }
        note.DeleteValue(NoteValue, throwOnMissingValue: false);
    }
}
