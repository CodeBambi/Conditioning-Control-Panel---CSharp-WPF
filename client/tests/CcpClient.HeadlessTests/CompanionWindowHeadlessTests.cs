using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-046 c7 headless draw-level tests for the companion surface. Draw-level ONLY
/// (verification-harness.md evidence-class rule): real in-memory layout, real style
/// resolution, real binding application — NO compositor/window-manager/pixel claims.
/// Badge pixels and interaction evidence are the avalonia-live headed seat's job (Step 3).
/// </summary>
public class CompanionWindowHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp046-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        var window = new MainWindow(host!);
        window.Show();
        return (host!, window);
    }

    private static CompanionWindow OpenViaRealClick(MainWindow window)
    {
        var button = window.FindControl<Button>("CompanionButton")!;
        var center = button.TranslatePoint(
            new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        var companion = window.Companion;
        Assert.NotNull(companion);
        companion!.UpdateLayout();
        return companion;
    }

    [AvaloniaFact]
    public async Task OpenFromDashboard_OwnedModeless_DashboardStays()
    {
        var (host, window) = await BootAsync();
        var companion = OpenViaRealClick(window); // the real open path (button Click wiring)

        Assert.True(companion.IsVisible);
        Assert.Same(window, companion.Owner); // owned modeless (W-04)
        Assert.False(companion.ShowInTaskbar);
        Assert.True(window.IsVisible); // modeless: the dashboard stays up

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Surface_Renders_ChatInput_Settings_ConfirmHidden_StatusHonest()
    {
        var (host, window) = await BootAsync();
        var companion = new CompanionWindow(host);
        companion.Show(window);
        companion.UpdateLayout();

        Assert.NotNull(companion.FindControl<TextBox>("ChatInput"));
        Assert.NotNull(companion.FindControl<Button>("SendButton"));
        Assert.NotNull(companion.FindControl<Button>("StopButton"));
        Assert.NotNull(companion.FindControl<CheckBox>("AwarenessConsentToggle"));
        Assert.NotNull(companion.FindControl<CheckBox>("MemoryConsentToggle"));
        Assert.NotNull(companion.FindControl<Button>("ClearMemoryButton"));

        // Confirm overlay hidden by default; the status line honestly FOLLOWS the probed
        // capability state (a real Ollama may answer on this box — either way the status
        // is the typed state, never a selection-derived claim).
        var overlay = companion.FindControl<Border>("ConfirmOverlay")!;
        Assert.False(overlay.IsVisible);
        var state = host.Capabilities!.GetState(
            CcpClient.Desktop.Ai.AiOperationPipeline.CapabilityName(CcpClient.Desktop.Ai.AiProviderId.LocalOllama));
        Assert.Equal(state is CcpClient.Desktop.Capabilities.CapabilityState.Available, companion.ViewModel.StatusAvailable);
        Assert.Contains("Companion provider:", companion.ViewModel.StatusText);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task UnavailableReply_RendersSubduedBubble_WithoutBadge()
    {
        var (host, window) = await BootAsync();
        var companion = new CompanionWindow(host);
        companion.Show(window);
        companion.UpdateLayout();

        // Draw-level proof the bubble classes render: a typed Unavailable reply bubble
        // (subdued, never badged) next to a user bubble. Environment-independent — the
        // live-send badge pixels are the headed seat's job (Step 3).
        companion.ViewModel.Bubbles.Add(CompanionBubbleModel.User("hello"));
        companion.ViewModel.Bubbles.Add(CompanionBubbleModel.ForReply(
            new CcpClient.Desktop.Ai.AiReply.Unavailable(CcpClient.Desktop.Ai.AiReplyCodes.Offline)));
        companion.UpdateLayout();

        var bubbles = companion.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("chat-bubble")).ToList();
        Assert.Equal(2, bubbles.Count);
        Assert.DoesNotContain(bubbles, b => b.Classes.Contains("badged"));
        Assert.Contains(bubbles, b => b.Classes.Contains("subdued"));
        Assert.Contains(bubbles, b => b.Classes.Contains("user"));

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ClearConfirm_ShowsOverlay_EscapeCancels_YesNeverDefaultPosition()
    {
        var (host, window) = await BootAsync();
        var companion = new CompanionWindow(host);
        companion.Show(window);
        companion.UpdateLayout();

        companion.ViewModel.RequestClearCommand.Execute(null);
        companion.UpdateLayout();

        var overlay = companion.FindControl<Border>("ConfirmOverlay")!;
        Assert.True(overlay.IsVisible);
        Assert.NotNull(companion.FindControl<Button>("ConfirmYesButton"));
        Assert.NotNull(companion.FindControl<Button>("ConfirmNoButton"));
        // Focus-default-NO is a real-focus interaction fact — proven headed (avalonia-live
        // get_focused_element, Step 3), not at draw level.

        // Esc = the default-NO path (WPF MessageBox default): the overlay cancels.
        companion.ViewModel.EscapeCommand.Execute(null);
        companion.UpdateLayout();
        Assert.False(overlay.IsVisible);
        Assert.True(companion.ViewModel.CanSend || companion.ViewModel.InputText.Length == 0);

        await host.ShutdownAsync();
    }
}
