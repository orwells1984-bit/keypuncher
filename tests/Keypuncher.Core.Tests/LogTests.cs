using Keypuncher.Core;

namespace Keypuncher.Core.Tests;

/// <summary>The app-wide log. All in one class, so the tests (which share the static log) run one at a time; each
/// uses an area of its own, so nothing else that logs can confuse it.</summary>
public class LogTests
{
    private sealed class Collect(string area) : ILogSink
    {
        public List<LogRecord> Records { get; } = [];
        public void Write(in LogRecord record) { if (record.Area == area) lock (Records) Records.Add(record); }
        public void Flush(TimeSpan timeout) { }
    }

    private sealed class Throws : ILogSink
    {
        public void Write(in LogRecord record) => throw new InvalidOperationException("broken sink");
        public void Flush(TimeSpan timeout) => throw new InvalidOperationException("broken sink");
    }

    private static Collect Sink(string area)
    {
        var sink = new Collect(area);
        Log.AddSink(sink);
        return sink;
    }

    [Fact]
    public void An_entry_reaches_the_sink_with_its_level_area_exception_and_thread()
    {
        var sink = Sink("t-entry");
        try
        {
            var boom = new InvalidOperationException("boom");
            Log.Error("t-entry", "something failed", boom);
            var r = Assert.Single(sink.Records);
            Assert.Equal(LogLevel.Error, r.Level);
            Assert.Equal("something failed", r.Message);
            Assert.Same(boom, r.Exception);
            Assert.Equal(Environment.CurrentManagedThreadId, r.ThreadId);
        }
        finally { Log.RemoveSink(sink); }
    }

    [Fact]
    public void The_same_entry_again_within_the_window_is_counted_and_said_with_the_next_one()
    {
        var sink = Sink("t-repeat");
        var now = new DateTime(2026, 11, 8, 7, 0, 0);
        var clock = Log.Clock;
        Log.Clock = () => now;
        try
        {
            for (var i = 0; i < 60; i++) Log.Error("t-repeat", "a frame failed");
            Assert.Single(sink.Records);

            now += Log.RepeatWindow;
            Log.Error("t-repeat", "a frame failed");
            Assert.Equal(2, sink.Records.Count);
            Assert.Equal(59, sink.Records[1].Suppressed);

            // A different message is never held back.
            Log.Error("t-repeat", "another thing failed");
            Assert.Equal(3, sink.Records.Count);
        }
        finally
        {
            Log.Clock = clock;
            Log.RemoveSink(sink);
        }
    }

    [Fact]
    public void A_broken_sink_never_reaches_the_caller()
    {
        var broken = new Throws();
        var sink = Sink("t-broken");
        Log.AddSink(broken);
        try
        {
            Log.Warn("t-broken", "still written");
            Log.Flush(TimeSpan.FromMilliseconds(10));
            Assert.Single(sink.Records);
        }
        finally
        {
            Log.RemoveSink(broken);
            Log.RemoveSink(sink);
        }
    }

    [Fact]
    public void Safely_logs_what_its_step_throws_and_carries_on()
    {
        var sink = Sink("t-safely");
        try
        {
            Log.Safely("t-safely", "closing the thing", () => throw new IOException("locked"));
            var r = Assert.Single(sink.Records);
            Assert.Equal("closing the thing failed", r.Message);
            Assert.IsType<IOException>(r.Exception);
        }
        finally { Log.RemoveSink(sink); }
    }

    [Fact]
    public async Task A_fire_and_forget_task_that_fails_is_logged()
    {
        var sink = Sink("t-task");
        try
        {
            var task = Task.Run(new Action(() => throw new InvalidOperationException("lost")));
            task.LogFailures("t-task", "warming up");
            await Assert.ThrowsAsync<InvalidOperationException>(() => task);
            for (var i = 0; i < 100 && sink.Records.Count == 0; i++) await Task.Delay(10);
            var r = Assert.Single(sink.Records);
            Assert.Equal("warming up failed", r.Message);
            Assert.Equal("lost", r.Exception?.Message);
        }
        finally { Log.RemoveSink(sink); }
    }

    [Fact]
    public void The_file_has_one_line_an_entry_with_the_exception_indented_under_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kp-log-test-" + Guid.NewGuid().ToString("N"));
        var file = new FileLogSink(Path.Combine(folder, "test.log"));
        try
        {
            Exception boom;
            try { throw new InvalidOperationException("boom"); }
            catch (Exception e) { boom = e; }
            file.Write(new LogRecord(new DateTime(2026, 11, 8, 7, 1, 2, 345), LogLevel.Info, "director", "key family 3: zebra", null, 1, "main"));
            file.Write(new LogRecord(new DateTime(2026, 11, 8, 7, 1, 3), LogLevel.Error, "ollama", "call failed", boom, 9, null, Suppressed: 4));
            file.Flush(TimeSpan.FromSeconds(5));
            var lines = File.ReadAllLines(file.Path);
            Assert.Equal("2026-11-08 07:01:02.345 INFO  director  [1 main] key family 3: zebra", lines[0]);
            Assert.StartsWith("2026-11-08 07:01:03.000 ERROR ollama    [9] call failed  (and 4 more the same", lines[1]);
            Assert.Equal("    System.InvalidOperationException: boom", lines[2]);
            Assert.All(lines.Skip(2), l => Assert.StartsWith("    ", l));
        }
        finally
        {
            file.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_file_moves_aside_when_full_and_only_the_newest_few_are_kept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kp-log-test-" + Guid.NewGuid().ToString("N"));
        var file = new FileLogSink(Path.Combine(folder, "test.log"), maxBytes: 1_000, keep: 2);
        try
        {
            var line = new string('x', 300);
            for (var i = 0; i < 40; i++)
                file.Write(new LogRecord(DateTime.Now, LogLevel.Debug, "t", $"{i} {line}", null, 1, null));
            file.Flush(TimeSpan.FromSeconds(5));
            Assert.Equal(["test.1.log", "test.2.log", "test.log"], Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).Order().ToArray());
            Assert.All(Directory.GetFiles(folder), f => Assert.True(new FileInfo(f).Length < 1_000 + 400));
            Assert.Contains("39 ", File.ReadAllText(file.Path));
        }
        finally
        {
            file.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
