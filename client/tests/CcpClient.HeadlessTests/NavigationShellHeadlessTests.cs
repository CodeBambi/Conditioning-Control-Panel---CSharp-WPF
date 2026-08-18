using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Tests;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-091: the navigation shell, driven by REAL headless input on the REAL controls
/// (<c>HeadlessWindowExtensions</c>), from a cold composition-root boot with NO command-line
/// arguments. The subject is the GESTURE reaching a DESTINATION, not the route dictionary —
/// a test that called <c>Router.Navigate("studio")</c> and asserted <c>Current == "studio"</c>
/// would be a test of a dictionary.
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, style-resolved
/// brushes, arranged DIP bounds, real input routing. Nothing here claims composited pixels,
/// window activation, z-order or that the Loom's web surface presents — those are
/// presentation-verified and belong to the headed capture.</para>
/// </summary>
public class NavigationShellHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp091-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        // No demo flags, no drive strings: this is the product path a user gets from a cold start.
        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return (host!, window);
    }

    private static RadioButton Door(MainWindow window, string name) =>
        window.FindControl<RadioButton>(name) ?? throw new InvalidOperationException($"no rail door '{name}'");

    private static T Descendant<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static bool HasDescendant(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

    /// <summary>Present AND actually reachable — a collapsed control is still in the tree.</summary>
    private static bool CanSee(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>()
            .Any(c => c.Name == name && c.IsEffectivelyVisible);

    private static void Click(MainWindow window, Control control, MouseButton button = MouseButton.Left)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>Replaces ONLY the final Show. The window handed to the seam is the real studio.</summary>
    private static List<object> CaptureLoomPresentations(MainWindow window)
    {
        var presented = new List<object>();
        window.Loom.Present = (studio, _) => presented.Add(studio);
        return presented;
    }

    [AvaloniaFact]
    public async Task ColdStart_NoArguments_DoorThenRowThenButton_ReachesTheLoomHost()
    {
        // THE ROW'S POINT: a landed surface reachable by a real user gesture, with no CLI flag.
        // Route verified live against v6.8.1 (wpf-surface-reachability.md §8.4):
        // DoorStudio -> rack row "Spiral Overlay" -> "THE LOOM — weave your own spiral".
        var (host, window) = await BootAsync();
        var presented = CaptureLoomPresentations(window);

        // Leave Studio first, so the Studio click below is a real navigation and not the
        // default page sitting there. The rack must actually leave the tree.
        Click(window, Door(window, "DoorCompanion"));
        Assert.Equal(ShellRoutes.Companion, window.Router.Current.Id);
        Assert.False(HasDescendant(window, "RowSpiralOverlay"));

        // Hop 1: the rail door.
        Click(window, Door(window, "DoorStudio"));
        Assert.Equal(ShellRoutes.Studio, window.Router.Current.Id);
        Assert.True(HasDescendant(window, "RowSpiralOverlay"));

        // The module panel is collapsed until a row is picked, so the Loom button is not
        // reachable yet ("Pick a module on the left").
        Assert.False(CanSee(window, "LoomButton"));

        // Hop 2: the rack row opens its module panel. The row is not a launcher — WPF's tiles
        // and rows navigate, never launch (MainWindow.Presets.cs:1007,1036).
        Click(window, Descendant<RadioButton>(window, "RowSpiralOverlay"));
        Assert.True(CanSee(window, "LoomButton"));
        Assert.Empty(presented); // still nothing launched — the row only selected the module

        // Hop 3: the ONE window-opening button on the destination page.
        Click(window, Descendant<Button>(window, "LoomButton"));

        var studio = Assert.IsType<DtrhLoomWindow>(Assert.Single(presented));
        Assert.Same(studio, window.Loom.Current);
        Assert.Equal(1, window.Loom.LaunchCount);
        Assert.Equal("The Loom", studio.Title); // WPF LoomHostService.cs:58 window title

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task LoomButton_PressedTwice_RefocusesInsteadOfOpeningASecondStudio()
    {
        // WPF LoomHostService.cs:29-31 — "idempotent - refocuses if already open".
        var (host, window) = await BootAsync();
        var presented = CaptureLoomPresentations(window);

        Click(window, Descendant<RadioButton>(window, "RowSpiralOverlay"));
        var button = Descendant<Button>(window, "LoomButton");
        Click(window, button);
        Click(window, button);

        Assert.Equal(2, window.Loom.LaunchCount);      // both presses arrived
        var studio = Assert.IsType<DtrhLoomWindow>(Assert.Single(presented)); // one studio only
        Assert.Same(studio, window.Loom.Current);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task CompanionDoor_TwoRealClicks_OpenTheRealCompanionWindow()
    {
        // The second door's destination, and the WPF two-hop grammar restored: the door
        // NAVIGATES (SettingsTabView.xaml:1864-1887), the page's control opens the window.
        var (host, window) = await BootAsync();

        Click(window, Door(window, "DoorCompanion"));
        Assert.Null(window.Companion); // navigating opened nothing

        Click(window, Descendant<Button>(window, "CompanionButton"));

        var companion = Assert.IsType<CompanionWindow>(window.Companion);
        Assert.True(companion.IsVisible);
        Assert.Same(window, companion.Owner); // owned modeless (W-04)
        Assert.True(window.IsVisible);        // the shell stays up

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task SystemDoor_RendersTheLiveStartupTraceAndCapabilityStates()
    {
        // The third door's destination is LIVE data from the real composition root, which is
        // what separates it from a decorative page. The SP-003/SP-006 proofs moved here when
        // the demonstrator card was retired; they are asserted where they now live.
        var (host, window) = await BootAsync();

        Click(window, Door(window, "DoorSystem"));

        var trace = Descendant<TextBlock>(window, "TraceText").Text ?? "";
        Assert.Contains("CompositionRoot", trace, StringComparison.Ordinal);
        Assert.Contains("CapabilityProbes", trace, StringComparison.Ordinal);
        // Every registered capability's typed state is rendered, not a fixed string.
        foreach (var name in host.Capabilities!.Names)
        {
            Assert.Contains($"capability {name}: ", trace, StringComparison.Ordinal);
        }

        Assert.NotEmpty(host.Capabilities.Names); // the loop above is not vacuous

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRail_DeclaresExactlyTheDeclaredRoutes_AndNoDtrhDoor()
    {
        // The anti-decoration pin. The rail is declared in MARKUP and the route table in code;
        // they can drift, and this is what catches it. A fifth door cannot appear without
        // reddening a named test.
        //
        // SP-094 opened the Play door and the DTRH assertion below is UNCHANGED and still
        // holds: DTRH is not a door. WPF reaches it in two hops — rail door then the Play
        // page's hero card (§3) — so the rail names the PAGE, never the feature behind it.
        var (host, window) = await BootAsync();

        var doors = window.GetVisualDescendants().OfType<RadioButton>()
            .Where(r => r.Classes.Contains("door")).ToList();

        Assert.Equal(
            ShellRoutes.Declared.Select(r => r.Label).ToArray(),
            doors.Select(d => d.Content as string).ToArray());
        Assert.Equal(
            new[] { "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSystem" },
            doors.Select(d => d.Name).ToArray());
        Assert.Equal(ShellRoutes.Declared.Count, window.Router.Routes.Count);

        // Every door reaches a page the shell actually mounts (ShellRouteBinding enforces the
        // same thing at composition; this is the rendered half of it).
        foreach (var route in ShellRoutes.Declared)
        {
            Assert.NotNull(window.PageFor(route.Id));
        }

        Assert.DoesNotContain(doors, d => (d.Content as string ?? "")
            .Contains("rabbit", StringComparison.OrdinalIgnoreCase));

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task SelectedDoor_ResolvesTheSelectedBrush_ThroughTheCheckedPseudoClass()
    {
        // A-012 mechanism proof, retargeted from the retired card's conditional-class ring onto
        // a product control: selection state is the control's own :checked pseudo-class and the
        // selector resolves a real brush. #FFE066FF is also the headed harness's seeded-
        // regression anchor (client/tools/verify/self-test.ps1).
        var (host, window) = await BootAsync();

        var studio = Door(window, "DoorStudio");
        var companion = Door(window, "DoorCompanion");

        Assert.Equal(Color.Parse("#FFE066FF"), ((ISolidColorBrush)studio.BorderBrush!).Color);
        Assert.Equal(Color.Parse("#FF3A2F3E"), ((ISolidColorBrush)companion.BorderBrush!).Color);

        Click(window, companion);

        Assert.Equal(Color.Parse("#FFE066FF"), ((ISolidColorBrush)companion.BorderBrush!).Color);
        Assert.Equal(Color.Parse("#FF3A2F3E"), ((ISolidColorBrush)studio.BorderBrush!).Color);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task PageHost_SwapsContent_AndTheOutgoingPageLeavesTheVisualTree()
    {
        // Retargeted from the retired card's load-bearing-IsVisible layout proof: the page swap
        // is a real tree change with real arranged bounds, not a stack of hidden panels.
        var (host, window) = await BootAsync();

        var studioPage = window.PageFor(ShellRoutes.Studio);
        var systemPage = window.PageFor(ShellRoutes.System);

        Assert.Contains(studioPage, window.GetVisualDescendants());
        Assert.DoesNotContain(systemPage, window.GetVisualDescendants());
        Assert.True(studioPage.Bounds.Height > 0); // arranged for real (in-memory layout)

        Click(window, Door(window, "DoorSystem"));

        Assert.DoesNotContain(studioPage, window.GetVisualDescendants());
        Assert.Contains(systemPage, window.GetVisualDescendants());
        Assert.True(systemPage.Bounds.Height > 0);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RackRow_SpaceWhenFocused_OpensTheSameModulePanel()
    {
        // Retargeted from the retired card's focused-Enter proof. The rack row is a RadioButton
        // as WPF's is (StudioTabView.xaml.cs:645-651), so keyboard activation is the control's
        // own and reaches the same destination the pointer does.
        var (host, window) = await BootAsync();

        var row = Descendant<RadioButton>(window, "RowSpiralOverlay");
        Assert.False(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);

        Assert.True(row.Focus());
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.UpdateLayout();

        Assert.True(row.IsChecked);
        Assert.True(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);
        Assert.True(CanSee(window, "LoomButton")); // the same destination the click reaches

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task DoorLabelMutation_LeavesRoutingIntact_AndTheLabelNeverResolves()
    {
        // The SP-014 lesson carried into navigation: the route id is dispatch identity and the
        // rendered label is display text. Mutating what the user reads must not move a door.
        var (host, window) = await BootAsync();

        var companion = Door(window, "DoorCompanion");
        companion.Content = "MUTATED-DISPLAY-LABEL";
        window.UpdateLayout();
        Assert.Equal("MUTATED-DISPLAY-LABEL", companion.Content); // the rendered text really changed

        Click(window, companion);
        Assert.Equal(ShellRoutes.Companion, window.Router.Current.Id); // still the companion door

        // And the mutated label never resolves as a route id.
        Assert.False(window.Router.Navigate("MUTATED-DISPLAY-LABEL"));
        Assert.Equal(ShellRoutes.Companion, window.Router.Current.Id);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnTheRackRow_OpensNoMenu_AndSelectsNothing()
    {
        // WPF quick-toggles the effect on a rack row's right-click (StudioTabView.xaml.cs:657-660).
        // The port has no spiral-overlay effect flag to flip, so the gesture is unhandled — the
        // one thing it must never be is swallowed by a fake toggle or a context menu.
        // Recorded as divergence §9 D6, NOT as parity.
        var (host, window) = await BootAsync();

        var row = Descendant<RadioButton>(window, "RowSpiralOverlay");
        Assert.Null(row.ContextMenu);
        Assert.Null(row.ContextFlyout);

        Click(window, row, MouseButton.Right);

        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());
        Assert.Null(row.ContextMenu);
        Assert.False(row.IsChecked);                                          // nothing selected
        Assert.False(Descendant<StackPanel>(window, "SpiralModulePanel").IsVisible);
        Assert.Equal(ShellRoutes.Studio, window.Router.Current.Id);           // nothing navigated

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ElementNameMirror_FollowsTheLiveHeartbeatText()
    {
        // Retargeted from the retired card's tick mirror: a compiled ElementName binding
        // resolving against a genuinely CHANGING source (a constant could be a hardcoded
        // literal). The tick lands through the REAL headless dispatcher.
        var (host, window) = await BootAsync();
        host.BindUiDispatch(new AvaloniaUiDispatch());

        Click(window, Door(window, "DoorSystem"));

        var heartbeat = Descendant<TextBlock>(window, "HeartbeatText");
        var mirror = Descendant<TextBlock>(window, "HeartbeatMirror");

        await TestWait.Until(
            () => heartbeat.Text is { } live
                  && !live.StartsWith("Heartbeat: waiting", StringComparison.Ordinal)
                  && mirror.Text == $"ElementName mirror: {live}",
            "the heartbeat advancing AND the ElementName mirror carrying the current heartbeat text",
            () => $"heartbeat='{heartbeat.Text}' mirror='{mirror.Text}'");

        Assert.StartsWith("ElementName mirror: ", mirror.Text, StringComparison.Ordinal);

        await host.ShutdownAsync();
    }
}
