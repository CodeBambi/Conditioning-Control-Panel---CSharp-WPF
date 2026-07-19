using Avalonia.Controls;
using Avalonia.Input;
using CcpClient.Desktop.Capabilities;
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
    public MainWindow(ApplicationHost host)
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
        // Left-click settings popup: CARVED OUT (A-005 per-window contract, dashboard/feature
        // rows own it). No left-click handler is wired — a no-op would be a claim.
    }

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
