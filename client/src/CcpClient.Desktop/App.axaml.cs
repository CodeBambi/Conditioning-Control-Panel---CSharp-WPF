using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CcpClient.Desktop.Features.AvatarTube;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;

namespace CcpClient.Desktop;

public partial class App : Application
{
    private readonly ApplicationHost _host;
    private readonly bool _popupDemo;
    private readonly bool _avatarDemo;
    private readonly bool _avatarCorrupt;
    private readonly string? _avatarTracePath;
    private StreamWriter? _avatarTraceWriter;

    public App(ApplicationHost host, bool popupDemo = false,
        bool avatarDemo = false, bool avatarCorrupt = false, string? avatarTracePath = null)
    {
        _host = host;
        _popupDemo = popupDemo;
        _avatarDemo = avatarDemo;
        _avatarCorrupt = avatarCorrupt;
        _avatarTracePath = avatarTracePath;
    }

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
            desktop.Exit += (_, _) =>
            {
                _avatarTraceWriter?.Dispose();
                _host.ShutdownAsync().GetAwaiter().GetResult();
            };

            var dashboard = new MainWindow(_host, _popupDemo);
            desktop.MainWindow = dashboard;

            // SP-015 AvatarTube DEMONSTRATOR (--avatartube-demo): opens the tube at
            // startup (WSLg has no input automation — SP-008 named limit).
            if (_avatarDemo)
            {
                var participant = _host.Participants.OfType<AvatarTubeParticipant>().Single();
                if (_avatarTracePath is not null)
                {
                    _avatarTraceWriter = new StreamWriter(_avatarTracePath, append: false) { AutoFlush = true };
                    participant.TraceSink = args => _avatarTraceWriter.WriteLine(AvatarEvidence.SerializeTrace(args));
                }

                if (_avatarCorrupt)
                {
                    // Typed undecodable-asset evidence: corrupt the pulse pack's bytes in
                    // memory (embedded assets untouched) — the SP-006 Degraded path runs
                    // for real on the pack switch.
                    participant.CorruptPackForDemo(SyntheticAvatarPacks.Pulse.PackId);
                }

                var tube = new AvatarTubeDemonstratorWindow(_host, dashboard, participant);
                tube.Show(dashboard);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
