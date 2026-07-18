using Avalonia;

namespace CcpClient.Desktop;

/// <summary>
/// Entry point and composition root. Explicit manual construction only —
/// no DI container, no static service locator (container admission is a
/// row-2 decision per client/docs/architecture-proposal.md §3).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect();
}
