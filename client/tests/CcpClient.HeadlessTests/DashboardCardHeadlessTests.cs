using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-008 headless admission spike: REAL interaction tests against the dashboard card
/// (demo.status-ticker) through the real composition root. Draw-level assertions ONLY
/// (verification-harness.md evidence-class rule): visual-tree classes, style-resolved
/// brush, arranged DIP bounds, compiled-binding resolution. No rendered frames, no
/// presentation claims (no compositor/window/DPI/activation/occlusion exists here).
/// </summary>
public class DashboardCardHeadlessTests
{
    private static async Task<ApplicationHost> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp008-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        return host!;
    }

    private static Border Card(MainWindow window) => window.FindControl<Border>("TickerCard")!;

    [AvaloniaFact]
    public async Task Card_Toggle_AppliesLitClass_AndStyleResolvesBorderBrush()
    {
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();

        var card = Card(window);
        var vm = (MainWindowViewModel)window.DataContext!;

        // Binding resolution + initial style: compiled Classes.lit binding resolved to
        // false; the base Border.feature-card selector supplies the unlit brush.
        Assert.DoesNotContain("lit", card.Classes);
        Assert.Equal(Color.Parse("#FF3A2F3E"), ((ISolidColorBrush)card.BorderBrush!).Color);

        // The ONE command path (A-004) — ring derives from the operation, not the flag.
        vm.ToggleCommand.Execute(null);

        Assert.Contains("lit", card.Classes); // conditional class applied from operation state
        Assert.Equal(Color.Parse("#FFE066FF"), ((ISolidColorBrush)card.BorderBrush!).Color); // .lit selector won

        vm.ToggleCommand.Execute(null);

        Assert.DoesNotContain("lit", card.Classes);
        Assert.Equal(Color.Parse("#FF3A2F3E"), ((ISolidColorBrush)card.BorderBrush!).Color);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Card_ArrangedBounds_GrowWithLoadBearinIsVisible()
    {
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();

        var card = Card(window);
        var vm = (MainWindowViewModel)window.DataContext!;
        var tick = window.FindControl<TextBlock>("TickText")!;

        var offHeight = card.Bounds.Height;
        Assert.True(offHeight > 0); // arranged for real (in-memory layout)
        Assert.False(tick.IsVisible); // binding resolved: row collapsed while off

        vm.ToggleCommand.Execute(null);

        Assert.True(tick.IsVisible);
        window.UpdateLayout(); // re-arrange with the tick row in layout
        var onHeight = card.Bounds.Height;
        Assert.True(onHeight > offHeight); // IsVisible is load-bearing in layout (DIP bounds)

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ElementNameMirror_FollowsLiveTickText()
    {
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();

        // Phase-4 bind through the REAL dispatch boundary: the headless UI thread is a
        // real Dispatcher, so the tick loop's skip-until-bound posts land here.
        host.BindUiDispatch(new AvaloniaUiDispatch());
        var vm = (MainWindowViewModel)window.DataContext!;
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        vm.ToggleCommand.Execute(null);

        // The #TickText.Text ElementName compiled binding resolves against a CHANGING
        // source (SP-007 consult: a constant could be a hardcoded literal).
        var ticks = ticker.TickCount;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        string? mirror = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(600);
            mirror = (window.Content as Control)?
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .FirstOrDefault(t => t.Text?.StartsWith("ElementName mirror:", StringComparison.Ordinal) == true)
                ?.Text;
            if (ticker.TickCount > ticks && mirror is not null && mirror.Contains($"tick {ticker.TickCount}", StringComparison.Ordinal))
            {
                break;
            }
        }

        Assert.NotNull(mirror);
        Assert.Matches(@"ElementName mirror: demo\.status-ticker: tick \d+", mirror);

        await host.ShutdownAsync();
    }
}
