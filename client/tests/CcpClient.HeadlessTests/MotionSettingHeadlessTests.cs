using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Motion;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The motion preference's SURFACE: the control exists on a page a user can reach, it opens on
/// what is actually stored, and moving it changes what the five hosted surfaces will read.
///
/// <para>Draw-level only (verification-harness.md evidence class): visual tree and real property
/// changes. <b>Nothing here claims a hosted page moved</b> — that needs a browser engine, a headed
/// run and a machine with Windows animation effects off.</para>
/// </summary>
public class MotionSettingHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-motion-shell-" + Guid.NewGuid().ToString("N"));
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
        // Through the DOOR, with real input: the control has to be reachable from the rail, not
        // merely constructed. The shell mounts one page at a time, so this is also what puts the
        // System page in the visual tree.
        Click(window, window.FindControl<RadioButton>("DoorSystem")!);
        return (host, window);
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

    private static ComboBox Picker(MainWindow window) =>
        window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(c => c.Name == "MotionLevelPicker")
        ?? throw new InvalidOperationException("no MotionLevelPicker reachable through the System door");

    private static TextBlock State(MainWindow window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "MotionLevelState")
        ?? throw new InvalidOperationException("no MotionLevelState reachable through the System door");

    [AvaloniaFact]
    public async Task ThePicker_OpensOnFull_AndOffersUpstreamsThreeLevelsInEnumOrder()
    {
        var (host, window) = await BootAsync();
        try
        {
            var picker = Picker(window);

            // The three items ARE the enum ordinals (PerformanceSettingsSection.xaml:164 — "never
            // reorder"), and a fresh data root opens on Full (Models/AppSettings.cs:3954).
            Assert.Equal(
                new[] { "Full", "Reduced", "Off" },
                picker.Items.OfType<ComboBoxItem>().Select(i => i.Content as string).ToArray());
            Assert.Equal((int)MotionLevel.Full, picker.SelectedIndex);
            Assert.Equal(MotionLevel.Full, HostedMotion.LevelOf(host));
            Assert.Contains(HostedMotion.NoReducedArgument, State(window).Text, StringComparison.Ordinal);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    [AvaloniaFact]
    public async Task PickingOff_IsWhatMakesAHostedPageStop_AndReducedIsNot()
    {
        var (host, window) = await BootAsync();
        try
        {
            var picker = Picker(window);
            var store = HostedMotion.StoreOf(host);
            Assert.NotNull(store);

            // Reduced: calmer chrome, and hosted pages still told to move.
            picker.SelectedIndex = (int)MotionLevel.Reduced;
            window.UpdateLayout();
            Assert.Equal(MotionLevel.Reduced, store!.Current.Level);
            Assert.Equal(HostedMotion.NoReducedArgument, HostedMotion.BrowserArgument(host, "test", _ => { }));
            Assert.Contains(HostedMotion.NoReducedArgument, State(window).Text, StringComparison.Ordinal);

            // Off: the one level forwarded to a hosted page.
            picker.SelectedIndex = (int)MotionLevel.Off;
            window.UpdateLayout();
            Assert.Equal(MotionLevel.Off, store.Current.Level);
            Assert.Equal(HostedMotion.ReducedArgument, HostedMotion.BrowserArgument(host, "test", _ => { }));
            Assert.Contains(HostedMotion.ReducedArgument, State(window).Text, StringComparison.Ordinal);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }
}
