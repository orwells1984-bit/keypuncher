using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keypuncher.Core;

/// <summary>
/// Another job on this computer that uses the same local model server and should wait while the toy runs, as a
/// grown-up described it in the settings: a program to run to pause it, and one to run to let it go on.
/// </summary>
/// <param name="Name">What the job is called, for the log ("model tournament").</param>
/// <param name="Program">The program both commands run, such as <c>pwsh</c>.</param>
/// <param name="PauseArguments">The arguments that pause the job. The command should return once the job has let go
/// of the model server, or the job may unload the toy's model on its way out.</param>
/// <param name="ResumeArguments">The arguments that let the job go on again.</param>
/// <param name="PauseFlag">The file the job's own pause writes, if it has one. When it is there before the toy
/// pauses, someone else paused the job, and the toy leaves the job alone and does not resume it. The toy resumes only
/// when the file is still exactly as its own pause left it: one rewritten or removed meanwhile is someone else's
/// doing. With no file named, the pause command's exit code 0 means the toy paused the job.</param>
/// <param name="PauseTimeoutSeconds">The longest the toy waits for the pause command.</param>
public sealed record OtherJob(
    string Name,
    string Program,
    string PauseArguments,
    string ResumeArguments,
    string? PauseFlag = null,
    int PauseTimeoutSeconds = 90);

/// <summary>Runs a program and waits for it. Never throws.</summary>
public interface ICommandRunner
{
    /// <summary>The exit code and what it wrote; a null code when it could not be started or did not end in time
    /// (it is then killed).</summary>
    Task<(int? Code, string Output)> RunAsync(string program, string arguments, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Runs commands as hidden processes.</summary>
public sealed class ProcessCommandRunner : ICommandRunner
{
    public async Task<(int? Code, string Output)> RunAsync(string program, string arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        var output = new StringBuilder();
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(program, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.OutputDataReceived += (_, e) => { if (e.Data is { } line) lock (output) output.AppendLine(line); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) lock (output) output.AppendLine(line); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                lock (output) return (null, output.Append($"(still running after {timeout.TotalSeconds:0} s; stopped)").ToString());
            }
            lock (output) return (process.ExitCode, output.ToString());
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return (null, $"could not start {program}: {e.Message}");
        }
    }
}

/// <summary>
/// Pauses another job that uses the model server (see <see cref="OtherJob"/>) while the toy runs, and lets it go on
/// once the toy has finished with the server, but only when it was the toy that paused it: a job that someone else
/// paused stays paused, and nothing is started that the toy did not stop.
/// </summary>
/// <remarks>
/// <para>Ollama holds one model at a time on a 6 GB card, so a nightly model tournament of the grown-up's that ran
/// into the morning left the toy with no model on several plays (2026-11-10, -14, -21), and on 2026-11-25 its own
/// auto-resume restarted it after it had been stopped by hand.</para>
/// <para>What the toy paused is written to a lease file in its data folder before the pause command runs, so a toy
/// that was killed does not leave the job paused for good: a watcher started beside the toy (see
/// <see cref="WatcherScript"/>) resumes the job when the toy's process ends however it ends, and a toy started again
/// takes over a lease whose toy is gone, keeps the job paused and resumes it at its own end.</para>
/// </remarks>
public sealed class OtherJobPause
{
    private readonly OtherJob _job;
    private readonly string _leaseFile;
    private readonly ICommandRunner _runner;
    private readonly Func<int, long, bool> _isAlive;
    private readonly int _pid;
    private readonly long _started;
    private readonly Action<string> _story;
    private readonly Lock _lock = new();
    private bool _resumed;

    public const string LeaseName = "other-job-paused.json";

    /// <param name="isAlive">Whether a process with this id and start time (UTC ticks) is still running.</param>
    /// <param name="story">Where the toy tells the grown-ups what it did.</param>
    public OtherJobPause(OtherJob job, string leaseFile, ICommandRunner runner, Action<string> story,
        int? pid = null, long? startedTicks = null, Func<int, long, bool>? isAlive = null)
    {
        _job = job;
        _leaseFile = leaseFile;
        _runner = runner;
        _story = story;
        using var me = Process.GetCurrentProcess();
        _pid = pid ?? me.Id;
        _started = startedTicks ?? me.StartTime.ToUniversalTime().Ticks;
        _isAlive = isAlive ?? IsProcessAlive;
    }

    /// <summary>What the job is called, for the log.</summary>
    public string Name => _job.Name;

    /// <summary>True once this run holds the job paused and owes it a resume.</summary>
    public bool Owned { get; private set; }

    /// <summary>The job's pause flag as this run's pause left it, as a SHA-256 in hex; null with no flag file named.</summary>
    public string? FlagHash { get; private set; }

    /// <summary>
    /// Pauses the job unless someone else has: a lease left by a toy that is gone is taken over (the job is still
    /// paused for the toy), a lease of a toy still running is left to it, and a pause flag already there is someone
    /// else's. Never throws.
    /// </summary>
    public async Task PauseAsync(CancellationToken ct = default)
    {
        try
        {
            if (ReadLease() is { } lease)
            {
                if (lease.Pid != _pid && _isAlive(lease.Pid, lease.Started))
                {
                    _story($"{_job.Name}: another Keypuncher (process {lease.Pid}) has it paused; this run leaves it to that one");
                    return;
                }
                if (lease.Flag is not null && _job.PauseFlag is not null && FlagNow() == lease.Flag)
                {
                    // The last toy was killed with the job paused, and the watcher did not resume it (a restart, say).
                    Take(lease.Flag);
                    _story($"{_job.Name}: still paused by a Keypuncher run that did not close properly; this run keeps it paused and resumes it at the end");
                    return;
                }
                // The last toy's pause was lifted or replaced by someone else since; its lease is stale.
                DeleteLease();
            }
            if (_job.PauseFlag is { } flag && File.Exists(flag))
            {
                _story($"{_job.Name}: already paused by someone else ({flag} is there); the toy leaves it paused and will not resume it");
                return;
            }

            // The lease is written before the command, so a toy killed while pausing still has the job resumed.
            WriteLease(null);
            var started = Stopwatch.StartNew();
            _story($"{_job.Name}: pausing it while the toy runs");
            var (code, output) = await _runner.RunAsync(_job.Program, _job.PauseArguments, TimeSpan.FromSeconds(_job.PauseTimeoutSeconds), ct).ConfigureAwait(false);
            Log.Info("other job", $"pause command for the {_job.Name} ended with {(code is { } c ? $"exit code {c}" : "no exit code")} after {started.Elapsed.TotalSeconds:0.0} s: {Log.Short(output.Trim(), 1000)}");
            if (_job.PauseFlag is not null)
            {
                if (FlagNow() is { } hash)
                {
                    Take(hash);
                    _story($"{_job.Name}: paused in {started.Elapsed.TotalSeconds:0} s{(code is null ? " (the pause command did not finish in time, so it may still be letting go of the model server)" : code != 0 ? $" (the pause command said exit code {code})" : "")}");
                }
                else
                {
                    DeleteLease();
                    _story($"{_job.Name}: the pause did not take (no {_job.PauseFlag} after the pause command{(code is { } c2 ? $", exit code {c2}" : ", which did not finish")}); it may still use the model server");
                }
            }
            else if (code == 0)
            {
                Take(null);
                _story($"{_job.Name}: paused in {started.Elapsed.TotalSeconds:0} s");
            }
            else
            {
                DeleteLease();
                _story($"{_job.Name}: the pause command failed ({(code is { } c3 ? $"exit code {c3}" : "it did not finish")}); it may still use the model server");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("other job", $"pausing the {_job.Name} failed", e);
        }
    }

    /// <summary>
    /// What resuming would do now: resume when this run paused the job and its flag is as the pause left it; with
    /// the flag gone or rewritten, someone else resumed or paused it again, and the toy only lets go of its lease.
    /// </summary>
    public bool ShouldResume()
    {
        if (!Owned) return false;
        if (_job.PauseFlag is null) return true;
        var now = FlagNow();
        return now is not null && now == FlagHash;
    }

    /// <summary>
    /// Lets the job go on if this run paused it and nobody has changed its pause since; at most once, and never
    /// throws. Called when the toy has finished with the model server, after the pictures are done.
    /// </summary>
    public async Task ResumeAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_resumed || !Owned) return;
            _resumed = true;
        }
        try
        {
            if (!ShouldResume())
            {
                _story($"{_job.Name}: not resumed, since its pause was {(FlagNow() is null ? "lifted" : "changed")} by someone else while the toy ran");
                return;
            }
            var (code, output) = await _runner.RunAsync(_job.Program, _job.ResumeArguments, timeout, ct).ConfigureAwait(false);
            Log.Info("other job", $"resume command for the {_job.Name} ended with {(code is { } c ? $"exit code {c}" : "no exit code")}: {Log.Short(output.Trim(), 1000)}");
            _story(code == 0 ? $"{_job.Name}: resumed" : $"{_job.Name}: the resume command failed ({(code is { } c2 ? $"exit code {c2}" : "it did not finish")}); it may still be paused");
        }
        finally
        {
            Owned = false;
            Log.Safely("other job", "removing the lease", DeleteLease);
        }
    }

    /// <summary>
    /// A Windows PowerShell script that waits for the toy's process to end, then resumes the job when the lease is
    /// still this run's and the job's pause flag is as the toy's pause left it, and removes the lease. Started beside
    /// the toy once it holds the job paused, so a toy that is killed or crashes does not leave the job paused; at a
    /// normal close the toy leaves the resume to it too, since the process may be ended before a resume it ran
    /// itself had finished.
    /// </summary>
    public string WatcherScript()
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var flagCheck = _job.PauseFlag is { } flag && FlagHash is { } hash
            ? $"if (-not (Test-Path -LiteralPath {Q(flag)})) {{ $ok = $false }} elseif ((Get-FileHash -LiteralPath {Q(flag)} -Algorithm SHA256).Hash -ne {Q(hash)}) {{ $ok = $false }}; "
            : "";
        return "$ErrorActionPreference = 'SilentlyContinue'; "
            + $"Wait-Process -Id {_pid}; "
            + $"$lease = {Q(_leaseFile)}; "
            + "if (-not (Test-Path -LiteralPath $lease)) { exit 0 }; "
            + $"if ((Get-Content -LiteralPath $lease -Raw | ConvertFrom-Json).pid -ne {_pid}) {{ exit 0 }}; "
            + "$ok = $true; " + flagCheck
            + $"if ($ok) {{ Start-Process -FilePath {Q(_job.Program)} -ArgumentList {Q(_job.ResumeArguments)} -WindowStyle Hidden -Wait }}; "
            + "Remove-Item -LiteralPath $lease";
    }

    private void Take(string? flagHash)
    {
        FlagHash = flagHash;
        Owned = true;
        WriteLease(flagHash);
    }

    /// <summary>The job's pause flag now, as a SHA-256 in upper-case hex (as PowerShell's Get-FileHash writes it); null
    /// when there is none.</summary>
    private string? FlagNow()
    {
        if (_job.PauseFlag is not { } flag || !File.Exists(flag)) return null;
        try { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(flag))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("other job", $"could not read {flag}", e);
            return null;
        }
    }

    private sealed record Lease(
        [property: JsonPropertyName("pid")] int Pid,
        [property: JsonPropertyName("started")] long Started,
        [property: JsonPropertyName("job")] string Job,
        [property: JsonPropertyName("flag")] string? Flag);

    private Lease? ReadLease()
    {
        if (!File.Exists(_leaseFile)) return null;
        try { return JsonSerializer.Deserialize<Lease>(File.ReadAllText(_leaseFile)); }
        catch (JsonException e)
        {
            Log.Warn("other job", $"{_leaseFile} is damaged; it is ignored", e);
            return null;
        }
    }

    private void WriteLease(string? flagHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_leaseFile))!);
        File.WriteAllText(_leaseFile, JsonSerializer.Serialize(new Lease(_pid, _started, _job.Name, flagHash)));
    }

    private void DeleteLease()
    {
        if (File.Exists(_leaseFile)) File.Delete(_leaseFile);
    }

    private static bool IsProcessAlive(int pid, long startedTicks)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // An id Windows has given to a new process since is not the old toy.
            return !p.HasExited && Math.Abs(p.StartTime.ToUniversalTime().Ticks - startedTicks) < TimeSpan.TicksPerSecond;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
