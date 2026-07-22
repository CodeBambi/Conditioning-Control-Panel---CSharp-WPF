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
    private readonly bool _avatarAnimate;
    private readonly bool _dtrhDemo;
    private readonly string _dtrhPage;
    private readonly int _dtrhAutoCloseSeconds;
    private readonly bool _dtrhQuick;
    private readonly int _dtrhPickerTimeoutSeconds;
    private readonly string? _dtrhFxDrive;
    private readonly bool _dtrhM2Test;
    private StreamWriter? _avatarTraceWriter;

    public App(ApplicationHost host, bool popupDemo = false,
        bool avatarDemo = false, bool avatarCorrupt = false, string? avatarTracePath = null,
        bool avatarAnimate = false, bool dtrhDemo = false, string dtrhPage = "index.html",
        int dtrhAutoCloseSeconds = 0, bool dtrhQuick = false, int dtrhPickerTimeoutSeconds = 0,
        string? dtrhFxDrive = null, bool dtrhM2Test = false)
    {
        _host = host;
        _popupDemo = popupDemo;
        _avatarDemo = avatarDemo;
        _avatarCorrupt = avatarCorrupt;
        _avatarTracePath = avatarTracePath;
        _avatarAnimate = avatarAnimate;
        _dtrhDemo = dtrhDemo;
        _dtrhPage = dtrhPage;
        _dtrhAutoCloseSeconds = dtrhAutoCloseSeconds;
        _dtrhQuick = dtrhQuick;
        _dtrhPickerTimeoutSeconds = dtrhPickerTimeoutSeconds;
        _dtrhFxDrive = dtrhFxDrive;
        _dtrhM2Test = dtrhM2Test;
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
                _host.LogDiagnostic("app: Exit — teardown begin");
                _host.ShutdownAsync().GetAwaiter().GetResult();
                _host.LogDiagnostic("app: teardown end");
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
                    // FileShare.Read: the evidence evaluator reads the trace WHILE the app
                    // still runs (headed gates correlate captures against live trace events).
                    var stream = new FileStream(_avatarTracePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _avatarTraceWriter = new StreamWriter(stream) { AutoFlush = true };
                    participant.TraceSink = args => _avatarTraceWriter.WriteLine(AvatarEvidence.SerializeTrace(args));
                }

                if (_avatarCorrupt)
                {
                    // Typed undecodable-asset evidence: corrupt the pulse pack's bytes in
                    // memory (embedded assets untouched) — the SP-006 Degraded path runs
                    // for real on the pack switch.
                    participant.CorruptPackForDemo(SyntheticAvatarPacks.Pulse.PackId);
                }

                var tube = new AvatarTubeDemonstratorWindow(_host, dashboard, participant, _avatarAnimate);
                // The owner must be visible before an owned window can show (Avalonia
                // EnsureParentStateBeforeShow): open the tube right after the dashboard opens.
                dashboard.Opened += (_, _) =>
                {
                    if (!tube.IsVisible)
                    {
                        tube.Show(dashboard);
                    }
                };
            }

            // SP-023/SP-024 DTRH DEMONSTRATOR (--dtrh-demo): b2 opens the save picker
            // first (hero-card outcome); --dtrh-quick skips it (Quick Start outcome).
            // The flow ending (host closed / picker cancelled) ends the app (exit 0
            // evidence); WSLg has no input automation — SP-008 named limit.
            if (_dtrhDemo)
            {
                var coordinator = new Features.Dtrh.DtrhLaunchCoordinator(_host, dashboard, _dtrhPage, _dtrhFxDrive, _dtrhM2Test);
                var flowEndedOnce = 0;
                coordinator.FlowEnded += () =>
                {
                    // One-shot: picker-cancel and host-close both raise FlowEnded, and the
                    // lifetime's own shutdown closes owned windows again (SP-023 ping-pong).
                    if (Interlocked.Exchange(ref flowEndedOnce, 1) != 0)
                    {
                        return;
                    }

                    _host.LogDiagnostic("dtrh: flow ended — shutting down the lifetime");
                    // Explicit Shutdown, not dashboard.Close(): on the GTK backend closing the
                    // MainWindow does not reliably end the classic lifetime here (SP-023 WX).
                    desktop.Shutdown();
                };
                coordinator.HostOpened += () =>
                {
                    if (_dtrhAutoCloseSeconds <= 0)
                    {
                        return;
                    }

                    // WSLg exit evidence without input automation (SP-008 named limit):
                    // the timed close exercises the same idempotent teardown path.
                    _host.LogDiagnostic($"dtrh: auto-close armed at {_dtrhAutoCloseSeconds}s");
                    _ = Task.Delay(TimeSpan.FromSeconds(_dtrhAutoCloseSeconds)).ContinueWith(
                        _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _host.LogDiagnostic("dtrh: auto-close firing");
                            if (coordinator.HostWindow?.IsVisible == true)
                            {
                                coordinator.HostWindow.Close();
                            }
                        }), TaskScheduler.Default);
                };
                dashboard.Opened += (_, _) =>
                {
                    if (coordinator.HostWindow is not null || coordinator.Picker is not null)
                    {
                        return;
                    }

                    if (_dtrhQuick)
                    {
                        _ = coordinator.QuickStartAsync();
                    }
                    else
                    {
                        _ = coordinator.LaunchWithPickerAsync();
                        if (_dtrhPickerTimeoutSeconds > 0)
                        {
                            // No-input platforms (SP-008): a TIMED commit of the picker's
                            // current selection — the same commit path DESCEND takes,
                            // honestly labeled as timed drive, never an input claim.
                            _host.LogDiagnostic($"dtrh: picker timeout armed at {_dtrhPickerTimeoutSeconds}s");
                            _ = Task.Delay(TimeSpan.FromSeconds(_dtrhPickerTimeoutSeconds)).ContinueWith(
                                _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    if (coordinator.Picker is { IsVisible: true } picker)
                                    {
                                        _host.LogDiagnostic("dtrh: picker timeout — committing current selection (timed drive, not input)");
                                        picker.CommitCurrentSelection();
                                    }
                                }), TaskScheduler.Default);
                        }
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
