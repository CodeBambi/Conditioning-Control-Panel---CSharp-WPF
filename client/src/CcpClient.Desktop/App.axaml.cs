using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;

namespace CcpClient.Desktop;

public partial class App : Application
{
    private readonly ApplicationHost _host;

    public App(ApplicationHost host) => _host = host;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Phase 4 binds the real UI dispatch boundary (async contract §5.2). Phases
            // 1-3 ran before Avalonia existed, so this is the earliest honest binding point.
            _host.BindUiDispatch(new AvaloniaUiDispatch());

            // Window-close path (contract §6): closing the main window exits the
            // lifetime; Exit reaches the single guarded teardown entry point.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => _host.ShutdownAsync().GetAwaiter().GetResult();
            desktop.MainWindow = new MainWindow(_host);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
