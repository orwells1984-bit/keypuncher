using Keypuncher.App.Pages;

namespace Keypuncher.App;

/// <summary>One window, and pages swapped in place rather than navigated, so there is no back button
/// or navigation bar for the child to reach.</summary>
public sealed class ToyApp : Application
{
    private readonly ToyHost _host = new();

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new PlayPage(_host)) { Title = "Keypuncher" };
        window.Destroying += (_, _) => _host.Dispose();
        return window;
    }

    public static void Show(Page page) => Current!.Windows[0].Page = page;
}
