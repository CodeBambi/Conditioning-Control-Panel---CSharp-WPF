using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The settings-handover sentence is REACHABLE and TRUE about the build showing it.
///
/// <para>Draw-level only: visual tree and text. It claims nothing about a migration, because there
/// is none — the decision not to migrate is recorded in
/// <c>Views/Pages/SettingsHandoverNotices.cs</c> and this is the surface that stops it being a
/// silence.</para>
/// </summary>
public class SettingsHandoverHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window, string DataRoot)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-handover-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        // Through the DOOR with real input, the way the Motion control beside it is reached: a
        // sentence nobody can get to is the defect this file exists to prevent, not prove.
        Click(window, window.FindControl<RadioButton>("DoorSystem")!);
        return (host, window, dir);
    }

    private static void Click(MainWindow window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static TextBlock Named(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == name)
        ?? throw new InvalidOperationException($"no {name} reachable through the System door");

    [AvaloniaFact]
    public async Task TheSystemDoorSaysTheWindowsAppsSettingsAreNOTIMPORTED_AndNamesTHISBuildsOwnFolder()
    {
        var (host, window, dataRoot) = await BootAsync();

        Assert.Equal("Settings and the Windows app", Named(window, "SettingsHandoverTitle").Text);

        var line = Named(window, "SettingsHandoverState").Text!;
        Assert.Contains("does not import", line, StringComparison.Ordinal);

        // THE FACT THAT IS NOT A RESTATEMENT. The folder shown is the one the composition root is
        // ACTUALLY using — this run's isolated data root — not a recomputed
        // CompositionRoot.DefaultSettingsPath(), which under this harness would name the real user
        // profile and tell the user to look somewhere the app has never written.
        Assert.Equal(dataRoot, window.Session.DataFolder);
        Assert.Contains(dataRoot, line, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Path.GetDirectoryName(CompositionRoot.DefaultSettingsPath())!, line, StringComparison.Ordinal);

        // And the shipping app's folder is NAMED. Nothing opens it: the port's rule for that
        // directory is one file and no writes (Entitlement/ShippingAppDataLocation.cs), and the
        // settings file is not that file.
        Assert.Contains(ShippingAppDataLocation.Resolve(), line, StringComparison.Ordinal);

        await host.ShutdownAsync();
    }
}
