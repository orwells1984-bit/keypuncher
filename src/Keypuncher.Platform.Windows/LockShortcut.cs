using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using Keypuncher.Core;

namespace Keypuncher.Platform.Windows;

/// <summary>
/// Turns off Win+L (and Lock on the Ctrl+Alt+Del screen) for as long as the toy runs. Windows locks the
/// screen before any keyboard hook sees the keys, so a baby's Win+L stopped play on 2026-11-04. This sets
/// the per-user policy <c>DisableLockWorkstation</c>, which takes effect at once.
/// <para>Windows lets an ordinary account read its own <c>Policies</c> key but not write to it, so on most
/// computers the first try is refused (it was on 2026-11-08). <see cref="AskForPermissionAsync"/> then asks a
/// grown-up, once, through the usual Windows admin prompt, to let this account change that one key; from then on
/// the toy sets and clears the value itself without asking again.</para>
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
    /// <summary>The Win32 error when the person closes the admin prompt with No.</summary>
    private const int Cancelled = 1223;

    private bool _changed;

    public LockShortcut() => TryTurnOff();

    /// <summary>True while this run has locking turned off.</summary>
    public bool Active => _changed;

    /// <summary>True when Windows refused the change because this account may not write its policy key; a
    /// grown-up can fix that once with <see cref="AskForPermissionAsync"/>.</summary>
    public bool NeedsPermission { get; private set; }

    /// <summary>Turns locking off if it is not off already. Safe to call again after permission is given.</summary>
    public void TryTurnOff()
    {
        if (_changed) return;
        try
        {
            RestoreFromNote();
            using var policy = Registry.CurrentUser.CreateSubKey(PolicyKey);
            var was = policy.GetValue(PolicyValue) is int v ? v : Absent;
            NeedsPermission = false;
            if (was == 1) return;
            using (var note = Registry.CurrentUser.CreateSubKey(NoteKey))
                note.SetValue(NoteValue, was, RegistryValueKind.DWord);
            policy.SetValue(PolicyValue, 1, RegistryValueKind.DWord);
            _changed = true;
        }
        catch (UnauthorizedAccessException e)
        {
            NeedsPermission = true;
            Log.Warn("windows", "could not turn off Win+L: this account may not change its policy key; "
                + "the grown-ups' page can ask for permission once. Until then the child can still lock the computer", e);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException)
        {
            // A locked-down account keeps Win+L; the toy still plays.
            Log.Warn("windows", "could not turn off Win+L; the child can still lock the computer", e);
        }
    }

    /// <summary>
    /// Shows the Windows admin prompt and, if a grown-up says yes, lets this account change its own
    /// <c>Policies\System</c> key (creating it if needed), then turns locking off. Returns whether locking is off.
    /// </summary>
    public async Task<bool> AskForPermissionAsync()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("this account has no security identifier");
        // The admin window is hidden, so the script writes what went wrong here for the log to show.
        var errorFile = Path.Combine(Path.GetTempPath(), "keypuncher-permission-error.txt");
        try { File.Delete(errorFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        var script = PermissionScript(sid, errorFile);
        Log.Info("windows", $"asking for admin permission to let {sid} change its policy key");
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand "
                    + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            }) ?? throw new InvalidOperationException("PowerShell did not start");
            await process.WaitForExitAsync();
            var why = File.Exists(errorFile) ? (await File.ReadAllTextAsync(errorFile)).Trim() : null;
            if (process.ExitCode == 0) Log.Info("windows", "the permission step finished");
            else Log.Warn("windows", $"the permission step failed with exit code {process.ExitCode}: {why ?? "it left no message"}");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == Cancelled)
        {
            Log.Info("windows", "the admin prompt was turned down; Win+L stays as it was");
            return false;
        }
        TryTurnOff();
        Log.Info("windows", _changed ? "Win+L turned off for this run" : "Win+L is still not turned off");
        return _changed || !NeedsPermission;
    }

    /// <summary>The PowerShell run as admin: create the account's <c>Policies\System</c> key if it is missing and give
    /// the account full control of that one key, addressed by SID so it is the right profile even when another
    /// account's password is typed into the prompt. Anything that goes wrong is written to <paramref name="errorFile"/>.</summary>
    /// <remarks>On 2026-11-08 the first version, which used Get-Acl and Set-Acl on a <c>Registry::</c> path, ended with
    /// exit code 1 and no message, so why is not known (the key's owner there is Administrators, who have full control).
    /// This one talks to the registry through .NET directly, changes only the key's permission list (Set-Acl writes
    /// the owner and group too), and leaves the error where the log can show it.</remarks>
    public static string PermissionScript(string sid, string errorFile)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(sid, @"^S-1-[0-9-]+$"))
            throw new ArgumentException($"not a SID: {sid}", nameof(sid));
        var quotedFile = errorFile.Replace("'", "''");
        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $sub = '{{sid}}\{{PolicyKey}}'
                $users = [Microsoft.Win32.Registry]::Users
                $users.CreateSubKey($sub).Close()
                $rights = [System.Security.AccessControl.RegistryRights]'ReadPermissions, ChangePermissions'
                $key = $users.OpenSubKey($sub, [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree, $rights)
                if ($null -eq $key) { throw "HKEY_USERS\$sub could not be opened" }
                $acl = $key.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access)
                $who = New-Object System.Security.Principal.SecurityIdentifier('{{sid}}')
                $rule = New-Object System.Security.AccessControl.RegistryAccessRule($who, 'FullControl', 'None', 'None', 'Allow')
                $acl.AddAccessRule($rule)
                $key.SetAccessControl($acl)
                $key.Close()
            } catch {
                try { ($_ | Out-String) | Set-Content -LiteralPath '{{quotedFile}}' -Encoding UTF8 } catch { }
                exit 1
            }
            exit 0
            """;
    }

    /// <summary>Puts locking back as it was.</summary>
    public void Dispose()
    {
        if (!_changed) return;
        _changed = false;
        try { RestoreFromNote(); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn("windows", "could not put Win+L back as it was; the next start tries again", e);
        }
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
