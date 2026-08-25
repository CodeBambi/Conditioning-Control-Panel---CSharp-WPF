using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>A MEASUREMENT, not an endorsement.</b> How far ONE pointer press moves the Visuals size
/// dial, pinned because it is the only demonstrated path by which
/// <see cref="VisualsPresetDocument.ImageScalePercent"/> reaches its ceiling of 250 without a
/// drag — and because it is a divergence from the shipping product that nothing else records.
///
/// <para><b>The forensic reason this file exists.</b> A flash capture on the owner's machine
/// matched <c>FlashGeometry.Size(..., 250)</c> exactly, including the <c>0.72 * 2.5</c>
/// truncation that only that code path produces. A complete census of
/// <c>VisualsPresetDocument</c>'s references found exactly one runtime writer of that member —
/// <c>Effects/FlashDraw.cs:141</c>, whose sole caller is the Size slider's handler
/// (<c>Views/Pages/StudioPage.axaml.cs:3175</c>) — and the slider's own
/// <c>Maximum</c> is 250 (<c>Views/Pages/StudioPage.axaml:631</c>). The document therefore
/// reached 250 by the slider's <c>Value</c> BEING 250, and nothing else. This file measures how
/// cheaply that happens.</para>
///
/// <para><b>What upstream does with the same gesture.</b> WPF's own size slider
/// (<c>Features/VisualsFeatureControl.xaml:54-56</c>) sets no <c>IsMoveToPointEnabled</c>, so it
/// keeps WPF's default of <c>false</c>: a press on the track page-steps toward the click by
/// <c>LargeChange</c> and stops. Avalonia's <c>Slider</c> has no such property at all — its press
/// always resolves through <c>Track.ValueFromPoint</c> — so the same gesture lands wherever the
/// pointer is. Nothing here CHANGES that: restoring upstream's outcome would also mean revisiting
/// the port's <c>LargeChange="10"</c> on every dial on the page, which is an owner decision about
/// twenty-five controls rather than a fix to one.</para>
///
/// <para>Draw-level only. It proves a gesture is AVAILABLE; it does not and cannot prove that any
/// particular user made it.</para>
/// </summary>
public class VisualsScaleGestureHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-scale-gesture-" + Guid.NewGuid().ToString("N"));
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
        ClickCentre(window, window.FindControl<RadioButton>("DoorStudio")!);
        ClickCentre(window, Descendant<RadioButton>(window, "RowVisuals"));
        window.UpdateLayout();
        return (host, window);
    }

    private static T Descendant<T>(MainWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {name} in the visual tree");

    private static void ClickCentre(MainWindow window, Control control) =>
        PressAt(window, control, control.Bounds.Width / 2);

    /// <summary>One press and release at <paramref name="x"/> device-independent pixels along the
    /// control, vertically centred — the whole gesture, with no drag between them.</summary>
    private static void PressAt(MainWindow window, Control control, double x)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(x, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task ONEPressOnTheSizeTrackMovesTheDialFurtherThanItsLargeChange_AndAPressAtTheFarEndIsTheCEILING()
    {
        var (host, window) = await BootAsync();
        var slider = Descendant<Slider>(window, "VisualsScaleSlider");
        Assert.True(slider.Bounds.Width > 0, "the size slider has no width to press on");

        // The dial opens where the document opens, which is WPF's default (AppSettings.cs:839).
        Assert.Equal(VisualsPresetDocument.DefaultImageScalePercent, window.Session.VisualsPreset.Current.ImageScalePercent);

        // A press at the MIDDLE of the track. Upstream's identical press moves the dial by
        // LargeChange and stops; here it lands where the pointer is, which is most of the way to
        // the ceiling in one gesture.
        PressAt(window, slider, slider.Bounds.Width / 2);
        var afterMiddle = window.Session.VisualsPreset.Current.ImageScalePercent;
        Assert.True(
            afterMiddle - VisualsPresetDocument.DefaultImageScalePercent > slider.LargeChange,
            $"one press moved the dial to {afterMiddle}, which is within LargeChange ({slider.LargeChange}) "
            + "of its starting value — the divergence this fact measures is gone, and the file's "
            + "reasoning about how the dial reached 250 has to be revisited rather than the number relaxed");

        // And a press at the FAR END is the ceiling itself, in one gesture, from a dial the user
        // never dragged. This is the whole finding: 250 is one click away, and 250 is the value a
        // flash capture on the owner's machine matched exactly.
        PressAt(window, slider, slider.Bounds.Width - 1);
        Assert.Equal(
            VisualsPresetDocument.MaxImageScalePercent,
            window.Session.VisualsPreset.Current.ImageScalePercent);

        await host.ShutdownAsync();
    }
}
