using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Arcademy;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// Slice 8 of the Arcademy row, rendered half: <b>the Play-tab entry exists and IS NOT
/// REACHABLE</b>, from a cold composition-root boot with NO command-line arguments.
///
/// <para>Upstream withholds this surface at v6.8.4 and does it in two places at once: the card
/// ships <c>Visibility="Collapsed"</c> (<c>Views/Tabs/PlayTabView.xaml:1312</c>) AND
/// <c>RefreshPlayCards</c> re-asserts that visibility from <c>ArcademyHostService.DoorAvailable</c>
/// on every repaint (<c>MainWindow/MainWindow.PlayTab.cs:106-112</c>). Both halves are ported, and
/// the second is the one these facts are really about: a strip that was only collapsed in markup
/// is one edit from shipping a feature the upstream product is deliberately not shipping.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, effective
/// visibility, real input routing. Nothing here claims composited pixels, that a class is
/// playable, or that any page or browser exists — there is none in this build.</para>
/// </summary>
public class ArcademyEntryHeadlessTests
{
    private static async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        // No demo flags, no drive strings: the product path a user gets from a cold start.
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

    /// <summary>Present AND actually reachable — a hidden control is still in the tree.</summary>
    private static bool CanSee(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Control>()
            .Any(c => c.Name == name && c.IsEffectivelyVisible);

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task ColdStart_ThePlayDoor_ShowsNoArcademyEntry_AndNothingInTheBuildRevealsIt()
    {
        var (host, window) = await BootAsync();

        Click(window, Door(window, "DoorPlay"));
        Assert.Equal(ShellRoutes.Play, window.Router.Current.Id);

        // The strip is MOUNTED — it is a real entry with a real launcher behind it, not a plan —
        // and it is not visible, so no gesture on this page can reach the Arcademy.
        Assert.NotNull(Descendant<StackPanel>(window, "ArcademyEntry"));
        Assert.False(CanSee(window, "ArcademyEntry"));
        Assert.False(CanSee(window, "ArcademyAttendButton"));
        Assert.False(CanSee(window, "ArcademyTitle"));

        // The sibling doors on the same page ARE reachable, so "not visible" above is a statement
        // about the Arcademy rather than about a page that failed to mount.
        Assert.True(CanSee(window, "FallInButton"));
        Assert.True(CanSee(window, "GoonPracticeButton"));

        // And the flag it is hidden from has no override seam: nothing in this build — no flag,
        // no argument, no environment variable — turns it on (ArcademyDoor's class remarks).
        Assert.False(ArcademyDoor.Available);
        Assert.Equal(0, window.Arcademy.AttendCount);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AnEntryForcedVisibleByHand_IsPulledBackDarkOnTheNextRepaint()
    {
        var (host, window) = await BootAsync();
        Click(window, Door(window, "DoorPlay"));
        var entry = Descendant<StackPanel>(window, "ArcademyEntry");

        // THE MUTATION, PERFORMED: exactly what a markup edit or a stray line elsewhere would do.
        entry.IsVisible = true;
        window.UpdateLayout();
        Assert.True(CanSee(window, "ArcademyEntry"));

        // The port's repaint is a re-mount, and the page is mounted every time the Play door is
        // chosen. RefreshPlayCards does this on every repaint upstream for the same reason
        // (MainWindow/MainWindow.PlayTab.cs:106-112): the visibility is DERIVED from the door,
        // never merely declared once in markup.
        Click(window, Door(window, "DoorStudio"));
        Click(window, Door(window, "DoorPlay"));

        Assert.False(CanSee(window, "ArcademyEntry"));
        Assert.False(CanSee(window, "ArcademyAttendButton"));
        Assert.Equal(0, window.Arcademy.AttendCount);

        await host.ShutdownAsync();
    }
}
