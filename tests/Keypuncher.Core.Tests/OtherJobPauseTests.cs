using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

public sealed class OtherJobPauseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kp-otherjob-" + Guid.NewGuid().ToString("N"));
    private string Flag => Path.Combine(_dir, "PAUSE");
    private string LeaseFile => Path.Combine(_dir, "data", OtherJobPause.LeaseName);
    private readonly List<string> _story = [];

    public OtherJobPauseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Stands in for the job's control script: "pause" writes the flag (when told to), "resume" removes it.</summary>
    private sealed class FakeJob(string? flag, bool pauseWrites = true, int pauseCode = 0) : ICommandRunner
    {
        public List<string> Calls { get; } = [];

        public Task<(int? Code, string Output)> RunAsync(string program, string arguments, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add(arguments);
            if (arguments == "pause")
            {
                if (pauseWrites && flag is not null) File.WriteAllText(flag, "{\"requestedAt\":\"" + Guid.NewGuid() + "\"}");
                return Task.FromResult<(int?, string)>((pauseCode, "paused"));
            }
            if (flag is not null && File.Exists(flag)) File.Delete(flag);
            return Task.FromResult<(int?, string)>((0, "resumed"));
        }
    }

    private OtherJob Job(bool withFlag = true) => new("tournament", "pwsh", "pause", "resume", withFlag ? Flag : null);

    private OtherJobPause New(ICommandRunner runner, bool withFlag = true, int pid = 100, Func<int, long, bool>? alive = null) =>
        new(Job(withFlag), LeaseFile, runner, _story.Add, pid, startedTicks: 1, isAlive: alive ?? ((_, _) => false));

    [Fact]
    public async Task Pauses_and_resumes_what_it_paused()
    {
        var job = new FakeJob(Flag);
        var pause = New(job);
        await pause.PauseAsync();
        Assert.True(pause.Owned);
        Assert.True(File.Exists(LeaseFile));
        Assert.True(pause.ShouldResume());

        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["pause", "resume"], job.Calls);
        Assert.False(File.Exists(Flag));
        Assert.False(File.Exists(LeaseFile));
        Assert.False(pause.Owned);
    }

    [Fact]
    public async Task Leaves_a_job_someone_else_paused_alone()
    {
        File.WriteAllText(Flag, "{\"by\":\"Stephen\"}");
        var job = new FakeJob(Flag);
        var pause = New(job);
        await pause.PauseAsync();
        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(job.Calls);
        Assert.True(File.Exists(Flag));
        Assert.False(pause.Owned);
    }

    [Fact]
    public async Task Does_not_resume_when_someone_paused_again_meanwhile()
    {
        var job = new FakeJob(Flag);
        var pause = New(job);
        await pause.PauseAsync();
        File.WriteAllText(Flag, "{\"by\":\"someone else\",\"hold\":true}");
        Assert.False(pause.ShouldResume());
        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["pause"], job.Calls);
        Assert.True(File.Exists(Flag));
        Assert.False(File.Exists(LeaseFile));
    }

    [Fact]
    public async Task Does_not_resume_when_someone_resumed_it_meanwhile()
    {
        var job = new FakeJob(Flag);
        var pause = New(job);
        await pause.PauseAsync();
        File.Delete(Flag);
        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["pause"], job.Calls);
    }

    [Fact]
    public async Task A_pause_that_wrote_no_flag_is_not_owned()
    {
        var job = new FakeJob(Flag, pauseWrites: false);
        var pause = New(job);
        await pause.PauseAsync();
        Assert.False(pause.Owned);
        Assert.False(File.Exists(LeaseFile));
    }

    [Fact]
    public async Task Without_a_flag_file_the_exit_code_decides()
    {
        var ok = New(new FakeJob(null), withFlag: false);
        await ok.PauseAsync();
        Assert.True(ok.Owned);
        await ok.ResumeAsync(TimeSpan.FromSeconds(5));

        var failed = New(new FakeJob(null, pauseCode: 1), withFlag: false);
        await failed.PauseAsync();
        Assert.False(failed.Owned);
    }

    [Fact]
    public async Task Takes_over_the_pause_of_a_toy_that_was_killed()
    {
        var first = New(new FakeJob(Flag), pid: 100);
        await first.PauseAsync();
        // The first toy is gone without resuming (killed, and its watcher did not run).
        var job = new FakeJob(Flag);
        var second = New(job, pid: 200);
        await second.PauseAsync();
        Assert.True(second.Owned);
        Assert.Empty(job.Calls);
        await second.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["resume"], job.Calls);
        Assert.False(File.Exists(Flag));
    }

    [Fact]
    public async Task Leaves_the_pause_to_another_toy_still_running()
    {
        var first = New(new FakeJob(Flag), pid: 100);
        await first.PauseAsync();
        var job = new FakeJob(Flag);
        var second = New(job, pid: 200, alive: (pid, _) => pid == 100);
        await second.PauseAsync();
        Assert.False(second.Owned);
        Assert.Empty(job.Calls);
        Assert.True(first.Owned);
    }

    [Fact]
    public async Task A_stale_lease_whose_pause_was_lifted_is_dropped_and_the_job_paused_anew()
    {
        var first = New(new FakeJob(Flag), pid: 100);
        await first.PauseAsync();
        File.Delete(Flag);
        var job = new FakeJob(Flag);
        var second = New(job, pid: 200);
        await second.PauseAsync();
        Assert.Equal(["pause"], job.Calls);
        Assert.True(second.Owned);
    }

    [Fact]
    public async Task Resumes_at_most_once()
    {
        var job = new FakeJob(Flag);
        var pause = New(job);
        await pause.PauseAsync();
        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        await pause.ResumeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["pause", "resume"], job.Calls);
    }

    [Fact]
    public async Task The_watcher_names_this_process_the_lease_and_the_flag_as_paused()
    {
        var pause = New(new FakeJob(Flag), pid: 4321);
        await pause.PauseAsync();
        var script = pause.WatcherScript();
        Assert.Contains("Wait-Process -Id 4321", script);
        Assert.Contains(LeaseFile.Replace("'", "''"), script);
        Assert.Contains(pause.FlagHash!, script);
        Assert.Contains("-ArgumentList 'resume'", script);
    }

    [Fact]
    public void The_watcher_quotes_single_quotes()
    {
        var job = new OtherJob("x", "pwsh", "pause", "-File 'C:\\it''s here\\a.ps1'");
        var pause = new OtherJobPause(job, LeaseFile, new FakeJob(null), _story.Add, 1, 1, (_, _) => false);
        Assert.Contains("-ArgumentList '-File ''C:\\it''''s here\\a.ps1'''", pause.WatcherScript());
    }

    /// <summary>The watcher itself, on Windows: it waits for the toy's process (a stand-in here) to end, then runs the
    /// resume command, which removes the flag, and removes the lease.</summary>
    [Fact]
    public async Task The_watcher_resumes_the_job_when_the_process_ends()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var toy = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 4") { CreateNoWindow = true, UseShellExecute = false })!;
        var resumed = Path.Combine(_dir, "resumed.txt");
        var job = new OtherJob("x", "powershell.exe", "pause",
            $"-NoProfile -Command \"Remove-Item -LiteralPath '{Flag}'; Set-Content -LiteralPath '{resumed}' -Value done\"", Flag);
        var pause = new OtherJobPause(job, LeaseFile, new FakeJob(Flag), _story.Add, toy.Id, toy.StartTime.ToUniversalTime().Ticks, (_, _) => true);
        await pause.PauseAsync();
        Assert.True(pause.Owned);
        var script = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(pause.WatcherScript()));
        using var watcher = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -EncodedCommand {script}") { CreateNoWindow = true, UseShellExecute = false })!;
        // Still waiting while the toy runs.
        await Task.Delay(1000);
        Assert.False(File.Exists(resumed));
        Assert.True(watcher.WaitForExit(TimeSpan.FromSeconds(60)));
        Assert.True(File.Exists(resumed));
        Assert.False(File.Exists(Flag));
        Assert.False(File.Exists(LeaseFile));
    }

    [Fact]
    public async Task The_watcher_does_not_resume_a_pause_someone_changed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var toy = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -Command Start-Sleep -Seconds 2") { CreateNoWindow = true, UseShellExecute = false })!;
        var resumed = Path.Combine(_dir, "resumed.txt");
        var job = new OtherJob("x", "powershell.exe", "pause", $"-NoProfile -Command \"Set-Content -LiteralPath '{resumed}' -Value done\"", Flag);
        var pause = new OtherJobPause(job, LeaseFile, new FakeJob(Flag), _story.Add, toy.Id, toy.StartTime.ToUniversalTime().Ticks, (_, _) => true);
        await pause.PauseAsync();
        File.WriteAllText(Flag, "{\"hold\":true,\"by\":\"someone else\"}");
        var script = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(pause.WatcherScript()));
        using var watcher = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -EncodedCommand {script}") { CreateNoWindow = true, UseShellExecute = false })!;
        Assert.True(watcher.WaitForExit(TimeSpan.FromSeconds(60)));
        Assert.False(File.Exists(resumed));
        Assert.True(File.Exists(Flag));
        Assert.False(File.Exists(LeaseFile));
    }
}
