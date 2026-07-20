using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-014 draw-level dispatch interaction proofs through REAL headless input routing
/// (HeadlessWindowExtensions): right-click press and focused Enter both resolve the toggle
/// through the ONE stable-ID command path; mutating the rendered title cannot break
/// dispatch; no context menu exists on the card. Draw-level ONLY (visual tree, classes,
/// text, arranged bounds — no pixels, no presentation claims).
/// </summary>
public class QuickToggleDispatchHeadlessTests
{
    private static async Task<ApplicationHost> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp014-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        return host!;
    }

    private static Point CenterOf(Control control, TopLevel window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? throw new InvalidOperationException("control is not in the window's visual tree");

    [AvaloniaFact]
    public async Task RightClickPress_OnCardBody_TogglesThroughStableIdPath()
    {
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();
        var card = window.FindControl<Border>("TickerCard")!;
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();

        var point = CenterOf(card, window);
        window.MouseDown(point, MouseButton.Right);
        Assert.True(ticker.IsOperationLive); // immediate toggle on press — no menu intermediate
        Assert.Contains("lit", card.Classes);
        window.MouseUp(point, MouseButton.Right);

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Assert.False(ticker.IsOperationLive);
        Assert.DoesNotContain("lit", card.Classes);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task EnterKey_WhenFocused_TogglesSameOperation_AsRightClick()
    {
        // Load-bearing (pre-approach consult): proves KeyBinding.CommandParameter={Binding CardId}
        // resolved — a silently-broken parameter binding would no-op this path.
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();
        var card = window.FindControl<Border>("TickerCard")!;
        var vm = (MainWindowViewModel)window.DataContext!;
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();

        // Structural: one key binding, the ONE command, the stable ID as parameter.
        var binding = Assert.Single(card.KeyBindings);
        Assert.Same(vm.ToggleCommand, binding.Command);
        Assert.Equal(StatusTickerParticipant.FeatureId, binding.CommandParameter);

        Assert.True(card.Focus());
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(ticker.IsOperationLive); // same operation the right-click path toggles

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.False(ticker.IsOperationLive);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TitleMutation_RendersMutatedTitle_AndRightClickStillToggles()
    {
        // The row's core claim at draw level: the MUTATED title is what the card renders,
        // and dispatch still resolves via the stable ID (never the displayed text).
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();
        var card = window.FindControl<Border>("TickerCard")!;
        var title = window.FindControl<TextBlock>("CardTitleText")!;
        var vm = (MainWindowViewModel)window.DataContext!;
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();

        Assert.Equal("Demo: Status Ticker", title.Text);
        vm.CardTitle = "MUTATED-DISPLAY-TITLE";
        Assert.Equal("MUTATED-DISPLAY-TITLE", title.Text); // the rendered text really changed

        // Right-click directly over the mutated TITLE text (title region ≡ body region).
        var titlePoint = CenterOf(title, window);
        window.MouseDown(titlePoint, MouseButton.Right);
        window.MouseUp(titlePoint, MouseButton.Right);
        Assert.True(ticker.IsOperationLive);

        // And the mutated title string itself never resolves as a key.
        Assert.False(vm.QuickToggle.TryToggle("MUTATED-DISPLAY-TITLE"));

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task PlainRightClick_NoContextMenu_ExistsOrOpens()
    {
        // Structural half of the no-context-menu negative (headed pixel/UIA proof in the
        // SP-014 smoke): the card carries no menu/flyout, and a right-click opens none.
        var host = await BootAsync();
        var window = new MainWindow(host);
        window.Show();
        var card = window.FindControl<Border>("TickerCard")!;

        Assert.Null(card.ContextMenu);
        Assert.Null(card.ContextFlyout);

        var point = CenterOf(card, window);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);

        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.Null(card.ContextMenu); // nothing attached one on press

        await host.ShutdownAsync();
    }
}
