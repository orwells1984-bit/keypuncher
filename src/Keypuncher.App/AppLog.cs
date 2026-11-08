using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Keypuncher.Core;

namespace Keypuncher.App;

/// <summary>
/// Starts the app-wide log, <c>keypuncher.log</c> in the data folder, and the last-chance handlers that make sure no
/// exception goes unrecorded: one thrown on any thread and not caught (the toy is about to close), and a background
/// task that failed with nobody looking. The UI thread's own handler is in <c>Platforms/Windows/App.xaml.cs</c>.
/// <para>
/// The log keeps everything, with a level, the thread and the time to the millisecond on each line: every model call
/// with its prompt, answer and timings, every pick and why it was taken or turned down, which stage each picture is
/// drawn at and why, and every exception in full. It is capped at about 25 MB (five files of 5 MB). The director's
/// log, <c>director.log</c>, stays as the short story of each play; everything in it is in this log too.
/// KEYPUNCHER_LOG_LEVEL (Debug, Info, Warn, Error) sets how much is kept; Debug, the default, includes the prompts.
/// </para>
/// </summary>
public static class AppLog
{
    public const string FileName = "keypuncher.log";

    private static readonly object Lock = new();
    private static FileLogSink? _sink;

    /// <summary>The log file, once started.</summary>
    public static string? FilePath => _sink?.Path;

    /// <summary>Starts the log and the handlers; later calls do nothing.</summary>
    public static void Start()
    {
        lock (Lock)
        {
            if (_sink is not null) return;
            var folder = ToyHost.ResolveDataFolder();
            try
            {
                if (Environment.GetEnvironmentVariable("KEYPUNCHER_LOG_LEVEL") is { Length: > 0 } wanted
                    && Enum.TryParse<Core.LogLevel>(wanted, ignoreCase: true, out var level))
                    Log.MinLevel = level;
                _sink = new FileLogSink(Path.Combine(folder, FileName));
                Log.AddSink(_sink);
            }
            catch (Exception e)
            {
                // Nowhere to write the log; the toy still plays.
                Trace.WriteLine($"Keypuncher could not start its log in {folder}: {e}");
            }

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var error = e.ExceptionObject as Exception ?? new Exception($"non-exception thrown: {e.ExceptionObject}");
                Log.Fatal("app", $"an exception nobody caught{(e.IsTerminating ? "; the toy is closing" : "")}", error);
                Log.Flush();
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Log.Error("app", "a background task failed and nobody looked at its result", e.Exception);
                e.SetObserved();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                Log.Info("app", "process exiting");
                Log.Flush();
            };

            Banner(folder);
        }
    }

    /// <summary>What is running where: enough to tell one build and computer from another when reading a log later.</summary>
    private static void Banner(string folder)
    {
        var app = Assembly.GetEntryAssembly() ?? typeof(AppLog).Assembly;
        var version = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? app.GetName().Version?.ToString();
        Log.Info("app", new string('=', 60));
        Log.Info("app", $"Keypuncher {version} starting, process {Environment.ProcessId}, built {(Environment.ProcessPath is { } exe ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm") : "?")}");
        Log.Info("app", $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, {RuntimeInformation.FrameworkDescription}, "
            + $"{Environment.ProcessorCount} cores, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1e9:0.0} GB memory, culture {System.Globalization.CultureInfo.CurrentCulture.Name}");
        Log.Info("app", $"data folder {folder}; log {FilePath ?? "(none)"} at level {Log.MinLevel}");
        foreach (var name in new[] { "KEYPUNCHER_DATA", "KEYPUNCHER_MODEL", "KEYPUNCHER_LOG_LEVEL", "OLLAMA_HOST" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value) Log.Info("app", $"{name}={value}");
    }
}
