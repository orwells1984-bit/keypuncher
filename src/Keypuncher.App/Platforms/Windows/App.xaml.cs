using Keypuncher.Core;

namespace Keypuncher.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        // Before anything else, so even a failure while starting is in the log.
        AppLog.Start();
        InitializeComponent();
        // The last chance for an exception on the UI thread (a button handler, a timer, a page): logged in full, and
        // play carries on rather than the toy vanishing under the child.
        UnhandledException += (_, e) =>
        {
            Log.Error("ui", $"an exception nobody caught on the UI thread ({e.Message}); carrying on", e.Exception);
            e.Handled = true;
        };
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
