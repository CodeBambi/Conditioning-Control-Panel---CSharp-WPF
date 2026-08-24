using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// THE LOGGED LAYOUT PROBE MUST DESCRIBE THE WINDOW THAT EXISTS NOW, not the one that existed at
/// first layout.
///
/// <para><b>The defect these facts close, measured on WSLg/X11 rather than imagined.</b> The shell
/// recomputed <c>LayoutProbeText</c> on every <c>LayoutUpdated</c> but called
/// <c>LogDiagnostic</c> exactly once, on the first one. On Windows the first layout already carries
/// the final scale and placement, so nothing showed. On X11 it does not: at
/// <c>AVALONIA_GLOBAL_SCALE_FACTOR=1.75</c> the X window measured 1925x1330 while the single logged
/// line still read <c>175.0x44.0 DIP @ scale 1 @ screen 12,45</c> and the on-screen copy had moved
/// on to <c>174.9x44.0 DIP @ scale 1.75 @ screen 21,79</c>.</para>
///
/// <para><b>Why a stale probe is worse than no probe.</b> WSLg publishes no UIA, so
/// <c>client/tools/verify/capture-wslg.sh</c> reads the LOGGED line and crops and clicks at what it
/// says. A stale line does not fail loudly — it produces a plausible image of the wrong pixels: a
/// rail-door crop scored 0.926 off pixels that were not a border, and a click aimed at the System
/// door's stale coordinates landed on the Play door and photographed the wrong page while scoring
/// 0.982. A human opening the image is what caught it.</para>
///
/// <para><b>What these facts can and cannot see.</b> Headless has no X11 scale factor to change, so
/// the geometry change driven here is the window MOVING — the same shape as the real defect (a
/// value inside the probe line changing after the first layout has already been logged) through the
/// seam headless does expose. The Linux half — that the logged line agrees with the on-screen line
/// at scale 1, 1.75 and 2.0 — is a headed reading and is not claimed here. Nothing here claims
/// composited pixels, placement by a real window manager, or input.</para>
/// </summary>
public class LayoutProbeFreshnessHeadlessTests : HeadlessTest
{
    private const string ProbeNeedle = "layout-probe: door ";

    private async Task<(MainWindow Window, List<string> Diagnostics)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-layoutprobe-headless-" + Guid.NewGuid().ToString("N"));
        var diagnostics = new List<string>();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            LogSinkFactory = () => new CapturingLogSink(diagnostics),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();
        return (window, diagnostics);
    }

    /// <summary>The probe as the WINDOW currently renders it, flattened the way the log writes it.</summary>
    private static string OnScreen(MainWindow window) =>
        (window.FindControl<TextBlock>("LayoutProbeText")?.Text
         ?? throw new InvalidOperationException("the shell has no LayoutProbeText"))
        .Replace(Environment.NewLine, " | ");

    private static List<string> ProbeLines(List<string> diagnostics)
    {
        lock (diagnostics)
        {
            return diagnostics.Where(l => l.StartsWith(ProbeNeedle, StringComparison.Ordinal)).ToList();
        }
    }

    /// <summary>Forces a real layout pass over the rail, which is what raises <c>LayoutUpdated</c>.</summary>
    private static void Relayout(MainWindow window)
    {
        (window.FindControl<RadioButton>("DoorStudio")
         ?? throw new InvalidOperationException("the shell has no DoorStudio")).InvalidateMeasure();
        window.UpdateLayout();
    }

    /// <summary>
    /// After the geometry moves, the LAST logged line is the line on the screen. That equality is
    /// the contract the harness's crops and clicks rest on, so it is asserted as an equality
    /// against the on-screen copy rather than as a search for a substring.
    /// </summary>
    [AvaloniaFact]
    public async Task WhenTheGeometryMovesAfterFirstLayout_TheLastLoggedProbeIsTheOneOnScreen()
    {
        var (window, diagnostics) = await BootAsync();
        var atFirstLayout = Assert.Single(ProbeLines(diagnostics).Distinct());
        Assert.Equal(OnScreen(window), atFirstLayout);

        // The X11 shape: a value inside the line changes AFTER the first layout has been logged.
        // Headless offers neither of the two knobs the real defect moves — there is no scale
        // factor to set, and its PointToScreen ignores the window's Position (measured: moving the
        // window 640x480 left every probe line byte-identical) — so the change driven here is the
        // rail's own measured geometry, which is the third quantity the harness reads out of this
        // line. One door growing moves its own height AND every later door's screen origin.
        var studio = window.FindControl<RadioButton>("DoorStudio")!;
        studio.Height = studio.Bounds.Height + 16;
        Relayout(window);

        var onScreen = OnScreen(window);
        Assert.NotEqual(atFirstLayout, onScreen);
        Assert.Equal(onScreen, ProbeLines(diagnostics)[^1]);
    }

    /// <summary>
    /// And it stays a DIAGNOSTIC rather than becoming a firehose: layout passes that describe the
    /// same geometry log nothing. <c>LayoutUpdated</c> is raised after every pass, so keying the
    /// log on the event rather than on the change would bury every other line in the one log the
    /// Linux leg can read.
    /// </summary>
    [AvaloniaFact]
    public async Task WhenALayoutPassChangesNoGeometry_TheProbeSaysNothingNew()
    {
        var (window, diagnostics) = await BootAsync();
        var before = ProbeLines(diagnostics).Count;

        for (var i = 0; i < 5; i++)
        {
            Relayout(window);
        }

        Assert.Equal(before, ProbeLines(diagnostics).Count);
    }

    /// <summary>The host's diagnostic sink, captured: the logged copy is the only one Linux has.</summary>
    private sealed class CapturingLogSink(List<string> lines) : ILogSink
    {
        public void Log(string message)
        {
            lock (lines)
            {
                lines.Add(message);
            }
        }
    }
}
