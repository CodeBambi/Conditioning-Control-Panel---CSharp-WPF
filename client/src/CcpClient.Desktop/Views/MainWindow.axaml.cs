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
    }
}
