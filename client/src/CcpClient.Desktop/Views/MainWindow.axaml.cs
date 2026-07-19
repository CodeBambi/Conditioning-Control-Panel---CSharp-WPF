using Avalonia.Controls;
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
        TraceText.Text = string.Join(Environment.NewLine, lines);

        // Async contract §5.4: the demonstrator's tick text reaches the window through
        // the real dispatch boundary; the reporter is invoked only inside a boundary post.
        foreach (var heartbeat in host.Participants.OfType<HeartbeatParticipant>())
        {
            heartbeat.TickReporter = text => HeartbeatText.Text = text;
        }
    }
}
