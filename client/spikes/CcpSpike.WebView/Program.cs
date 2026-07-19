using Avalonia;

namespace CcpSpike.WebView;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args);
}
