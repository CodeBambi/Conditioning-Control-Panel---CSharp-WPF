using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Integration proof (contract §10.1): the window displays the phase-outcome trace,
    /// the demonstrator participants' running state, and the capability states — the
    /// composition root's products are visibly reachable from a user path. The dashboard
    /// card (demo.status-ticker) is the first visible slice (SP-007).
    /// </summary>
    public MainWindow(ApplicationHost host, bool popupDemo = false)
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(host);

        var lines = host.Trace.Entries.Concat(
            host.Participants.Select(p => $"{p.Name}: {(p.Running ? "running" : "stopped")}"));
        if (host.Capabilities is { } capabilities)
        {
            // Capability contract §9.1: the window surfaces each capability's typed state —
            // the composition root's probe results are visibly reachable from a user path.
            lines = lines.Concat(capabilities.Names.Select(n => $"capability {n}: {Describe(capabilities.GetState(n))}"));
        }

        TraceText.Text = string.Join(Environment.NewLine, lines);

        // Async contract §5.4: the demonstrator's tick text reaches the window through
        // the real dispatch boundary; the reporter is invoked only inside a boundary post.
        foreach (var heartbeat in host.Participants.OfType<HeartbeatParticipant>())
        {
            heartbeat.TickReporter = text => HeartbeatText.Text = text;
        }

        // WPF parity outcome (capability-inventory §Feature-card interaction): plain
        // right-click anywhere on the card body quick-toggles — no popup, no context menu.
        // Pointer events with an explicit button check (cheat sheet §Events), e.Handled = true.
        // The same ONE command path as the keyboard KeyBinding (A-004).
        TickerCard.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(TickerCard).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
            {
                e.Handled = true;
                ((MainWindowViewModel)DataContext!).ToggleCommand.Execute(null);
            }
        };
        // Left-click opens the demonstrator's settings popup (SP-013; W-04 contract):
        // one-at-a-time via FeaturePopupManager, owned modeless, focus restoration on close.
        // DEMONSTRATOR popup — superseded by the first real feature popup.
        _popups = new FeaturePopupManager(
            this,
            () => popupDemo
                ? new FeaturePopupWindow { DiagnosticSink = host.LogDiagnostic }
                : new FeaturePopupWindow(),
            FeaturePopupManager.CreateFocusRestoration(this));
        TickerCard.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(TickerCard).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            {
                e.Handled = true;
                _popups.Show();
            }
        };

        if (popupDemo)
        {
            // SP-013 WSLg evidence: open the demonstrator popup at startup — WSLg has no
            // input automation (SP-008 named limit), so the popup cannot be left-clicked.
            Opened += (_, _) => _popups.Show();
        }

        // SP-007 layout probe (demonstrator-slice diagnostic): measured card bounds + actual
        // RenderScaling, visible in the window and logged once for the headed harness.
        TickerCard.LayoutUpdated += (_, _) =>
        {
            var bounds = TickerCard.Bounds;
            var topLeft = TickerCard.PointToScreen(new Point(0, 0));
            LayoutProbeText.Text = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"layout-probe: card {bounds.Width:F1}x{bounds.Height:F1} DIP @ scale {RenderScaling:0.##} @ screen {topLeft.X},{topLeft.Y}");
            if (!_layoutProbeLogged)
            {
                _layoutProbeLogged = true;
                host.LogDiagnostic(LayoutProbeText.Text);
            }
        };
    }

    private bool _layoutProbeLogged;
    private FeaturePopupManager? _popups;

    /// <summary>Demonstrator popup manager (SP-013); public so tests drive the real wiring.</summary>
    public FeaturePopupManager Popups => _popups!;

    private static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code}) — {unavailable.Reason.Detail}",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — survives: {degraded.SurvivingSemantics}; {degraded.Reason.Detail}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code}) — {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency}) — {missing.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code}) — {faulted.Reason.Detail}",
        _ => state.GetType().Name,
    };
}
