using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CcpSpike.WebView;

public partial class App : Application
{
    private readonly SpikeConfig _config;
    private readonly LoopbackServer _server;
    private readonly SpikeLog _log;

    public App() : this(null!, null!, null!) { } // designer only

    public App(SpikeConfig config, LoopbackServer server, SpikeLog log)
    {
        _config = config;
        _server = server;
        _log = log;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(_config, _server, _log);
            desktop.ShutdownRequested += (_, _) => _server.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
