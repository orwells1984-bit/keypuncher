using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Keypuncher.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        AppLog.Start();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<ToyApp>()
            .UseSkiaSharp()
            .ConfigureLifecycleEvents(events => events.AddWindows(w => w.OnWindowCreated(window =>
            {
                // Borderless full screen: no title bar or taskbar for small hands to find.
                window.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                ToyHost.WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            })));
        return builder.Build();
    }
}
