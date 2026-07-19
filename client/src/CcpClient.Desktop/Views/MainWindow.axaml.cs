using Avalonia.Controls;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Integration proof (contract §10.1): the window displays the phase-outcome trace
    /// and the demonstrator participant's running state — the composition root's products
    /// are visibly reachable from a user path.
    /// </summary>
    public MainWindow(ApplicationHost host)
    {
        InitializeComponent();

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