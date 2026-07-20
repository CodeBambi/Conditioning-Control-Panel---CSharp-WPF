using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// SP-013 headless interaction tests for the demonstrator feature popup. Draw-level ONLY
/// (verification-harness.md evidence-class rule): real in-memory layout, real input routing,
/// real Extent/Viewport/Offset — but NO compositor/window-manager/DPI/activation claims.
/// Presentation facts (working-area containment, real wheel/trackpad/touch) are the
/// Windows-headed evidence matrix's job.
/// </summary>
public class FeaturePopupHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp013-headless-" + Guid.NewGuid().ToString("N"));
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

    private static ScrollViewer Scroller(FeaturePopupWindow popup) =>
        popup.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PopupScroller");

    private static TextBlock Probe(FeaturePopupWindow popup) =>
        popup.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "ScrollProbeText");

    private static void Pump(Window window) => window.UpdateLayout();

    /// <summary>Control center in WINDOW-relative DIP (Bounds are parent-relative — translate or headless input misses the target).</summary>
    private static Point CenterIn(Window window, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    [AvaloniaFact]
    public async Task LeftClick_OpensOwnedModelessPopup_WithContractChrome()
    {
        var (host, window) = await BootAsync();
        var card = window.FindControl<Border>("TickerCard")!;

        // The demonstrator card's LEFT-click path (real pointer routing).
        var center = card.Bounds.Center;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);

        var popup = Assert.IsType<FeaturePopupWindow>(window.Popups.Active);
        Assert.True(popup.IsVisible);
        Assert.Same(window, popup.Owner); // owned modeless (W-04)
        Assert.False(popup.ShowInTaskbar);
        Assert.False(popup.CanResize);
        Assert.Equal(WindowDecorations.None, popup.WindowDecorations);
        Assert.True(window.IsVisible); // modeless: dashboard stays up

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TallContent_CapsHeight_AndFinalControlStartsBelowFold()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show(); // TALL is the default variant
        Pump(popup);

        var scroller = Scroller(popup);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height); // real overflow
        Assert.Contains("final-in-viewport false", Probe(popup).Text); // starts below the fold

        // Capped by the OWNER monitor's working area (headless screen), WPF-parity constants.
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary!;
        var cap = PopupPlacement.CapHeightDip(screen.WorkingArea.Height, screen.Scaling);
        Assert.Equal(cap, popup.MaxHeight, precision: 6);
        Assert.True(popup.Height <= cap + 0.5);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ShortContent_Compact_NoScrollbarMetrics()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show();
        popup.SetVariant(FeaturePopupWindow.ContentVariant.Short);
        Pump(popup);

        var scroller = Scroller(popup);
        Assert.True(scroller.Extent.Height <= scroller.Viewport.Height); // no overflow -> no scroll range
        Assert.Equal(PopupPlacement.MinHeightDip, popup.Height, precision: 3); // compact (WPF min), not the 640 fixed height

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Wheel_ReachesFinalControl_OffsetChangesMonotonically()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show();
        Pump(popup);
        var scroller = Scroller(popup);
        var probe = Probe(popup);

        var lastOffset = scroller.Offset.Y;
        var reached = false;
        for (var i = 0; i < 40 && !reached; i++)
        {
            // Real pointer wheel routing over the popup content (negative delta = scroll down).
            popup.MouseWheel(CenterIn(popup, scroller), new Vector(0, -3), RawInputModifiers.None);
            Pump(popup);
            var offset = scroller.Offset.Y;
            Assert.True(offset >= lastOffset - 0.001); // never scrolls back up
            lastOffset = offset;
            reached = probe.Text?.Contains("final-in-viewport true") == true;
        }

        Assert.True(reached, "final control must enter the viewport via wheel");
        Assert.True(lastOffset > 0);
        Assert.True(scroller.Offset.Y + scroller.Viewport.Height >= scroller.Extent.Height - 0.5); // at the bottom

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task NestedList_ScrollsItself_ThenChainsToPopup()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show();
        popup.SetVariant(FeaturePopupWindow.ContentVariant.Nested);
        Pump(popup);

        var outer = Scroller(popup);
        var innerList = popup.GetVisualDescendants().OfType<ListBox>().First();
        var inner = innerList.GetVisualDescendants().OfType<ScrollViewer>().First();

        // v12 default: chaining enabled on the inner viewer (12.1.0 source-verified).
        Assert.True(ScrollViewer.GetIsScrollChainingEnabled(inner));

        var innerPoint = CenterIn(popup, inner);
        // First wheels: the inner list scrolls itself, the popup does not move.
        for (var i = 0; i < 3; i++)
        {
            popup.MouseWheel(innerPoint, new Vector(0, -3), RawInputModifiers.None);
            Pump(popup);
        }

        Assert.True(inner.Offset.Y > 0);
        Assert.Equal(0, outer.Offset.Y);

        // Keep wheeling: at the inner end, remaining movement CHAINS to the popup.
        var chained = false;
        for (var i = 0; i < 60 && !chained; i++)
        {
            popup.MouseWheel(innerPoint, new Vector(0, -3), RawInputModifiers.None);
            Pump(popup);
            chained = outer.Offset.Y > 0;
        }

        Assert.True(chained, "movement must chain to the popup once the inner list hits its end");

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Escape_And_CloseButton_BothClose_ThroughTheOnePath()
    {
        var (host, window) = await BootAsync();

        // Path 1: Escape (window KeyBinding -> CloseCommand -> ClosePopup()).
        var popup = (FeaturePopupWindow)window.Popups.Show();
        Pump(popup);
        popup.Focus();
        popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
        Assert.Null(window.Popups.Active);

        // Path 2: title-bar close button (Click -> ClosePopup()). Same observable operation.
        popup = (FeaturePopupWindow)window.Popups.Show();
        Pump(popup);
        var closeButton = popup.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CloseButton");
        var center = CenterIn(popup, closeButton);
        popup.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        popup.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        Assert.Null(window.Popups.Active);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task OneAtATime_CloseExistingBeforeNew()
    {
        var (host, window) = await BootAsync();
        var first = (FeaturePopupWindow)window.Popups.Show();
        var firstClosed = false;
        first.Closed += (_, _) => firstClosed = true;

        var second = (FeaturePopupWindow)window.Popups.Show();

        Assert.True(firstClosed); // close-existing-before-new (Presets.cs:852)
        Assert.False(first.IsVisible);
        Assert.Same(second, window.Popups.Active);
        Assert.True(second.IsVisible);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task Popup_StaysInsideOwnerWorkingArea()
    {
        var (host, window) = await BootAsync();
        var popup = (FeaturePopupWindow)window.Popups.Show();
        Pump(popup);

        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary!;
        var wa = screen.WorkingArea; // physical px
        var size = PixelSize.FromSize(popup.ClientSize, popup.RenderScaling);
        Assert.InRange(popup.Position.X, wa.X, Math.Max(wa.X, wa.Right - size.Width));
        Assert.InRange(popup.Position.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - size.Height));

        await host.ShutdownAsync();
    }
}
