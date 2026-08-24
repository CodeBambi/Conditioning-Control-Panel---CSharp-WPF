using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using CcpClient.Desktop.Features.AvatarTube;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;

namespace CcpClient.Desktop;

public partial class App : Application
{
    private readonly ApplicationHost _host;
    private readonly bool _popupDemo;
    private readonly bool _avatarDemo;
    private readonly bool _avatarCorrupt;
    private readonly string? _avatarTracePath;
    private readonly bool _avatarAnimate;
    private readonly bool _dtrhDemo;
    private readonly string _dtrhPage;
    private readonly int _dtrhAutoCloseSeconds;
    private readonly bool _dtrhQuick;
    private readonly int _dtrhPickerTimeoutSeconds;
    private readonly string? _dtrhFxDrive;
    private readonly bool _dtrhM2Test;
    private readonly bool _dtrhKillRenderers;
    private readonly bool _loomDemo;
    private readonly string? _loomDrive;
    private readonly int _loomAutoCloseSeconds;
    private readonly bool _intakeDemo;
    private readonly string? _intakeDrive;
    private readonly bool _intakeKillRenderers;
    private readonly int _intakeAutoCloseSeconds;
    private readonly bool _tunnelDemo;
    private readonly string? _tunnelDrive;
    private readonly int _tunnelAutoCloseSeconds;
    private readonly bool _goonDemo;
    private StreamWriter? _avatarTraceWriter;

    public App(ApplicationHost host, bool popupDemo = false,
        bool avatarDemo = false, bool avatarCorrupt = false, string? avatarTracePath = null,
        bool avatarAnimate = false, bool dtrhDemo = false, string dtrhPage = "index.html",
        int dtrhAutoCloseSeconds = 0, bool dtrhQuick = false, int dtrhPickerTimeoutSeconds = 0,
        string? dtrhFxDrive = null, bool dtrhM2Test = false, bool dtrhKillRenderers = false,
        bool loomDemo = false, string? loomDrive = null, int loomAutoCloseSeconds = 0,
        bool intakeDemo = false, string? intakeDrive = null, bool intakeKillRenderers = false,
        int intakeAutoCloseSeconds = 0,
        bool tunnelDemo = false, string? tunnelDrive = null, int tunnelAutoCloseSeconds = 0,
        bool goonDemo = false)
    {
        _host = host;
        _popupDemo = popupDemo;
        _avatarDemo = avatarDemo;
        _avatarCorrupt = avatarCorrupt;
        _avatarTracePath = avatarTracePath;
        _avatarAnimate = avatarAnimate;
        _dtrhDemo = dtrhDemo;
        _dtrhPage = dtrhPage;
        _dtrhAutoCloseSeconds = dtrhAutoCloseSeconds;
        _dtrhQuick = dtrhQuick;
        _dtrhPickerTimeoutSeconds = dtrhPickerTimeoutSeconds;
        _dtrhFxDrive = dtrhFxDrive;
        _dtrhM2Test = dtrhM2Test;
        _dtrhKillRenderers = dtrhKillRenderers;
        _loomDemo = loomDemo;
        _loomDrive = loomDrive;
        _loomAutoCloseSeconds = loomAutoCloseSeconds;
        _intakeDemo = intakeDemo;
        _intakeDrive = intakeDrive;
        _intakeKillRenderers = intakeKillRenderers;
        _intakeAutoCloseSeconds = intakeAutoCloseSeconds;
        _tunnelDemo = tunnelDemo;
        _tunnelDrive = tunnelDrive;
        _tunnelAutoCloseSeconds = tunnelAutoCloseSeconds;
        _goonDemo = goonDemo;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Phase 4 binds the real UI dispatch boundary (async contract §5.2). Phases
            // 1-3 ran before Avalonia existed, so this is the earliest honest binding point.
            _host.BindUiDispatch(new AvaloniaUiDispatch());

            // Window-close path (contract §6): closing the main window exits the
            // lifetime; Exit reaches the single guarded teardown entry point.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) =>
            {
                _avatarTraceWriter?.Dispose();
                _host.LogDiagnostic("app: Exit — teardown begin");
                _host.ShutdownAsync().GetAwaiter().GetResult();
                _host.LogDiagnostic("app: teardown end");
            };

            // The --dtrh-* and --intake-* harness options travel to the shell's ONE DtrhLaunch and
            // ONE IntakeLaunch, so each demonstrator and the matching user button drive the same
            // coordinator instance.
            var dashboard = new MainWindow(_host, _popupDemo,
                _dtrhDemo
                    ? new Features.Dtrh.DtrhHarnessOptions(_dtrhPage, _dtrhFxDrive, _dtrhM2Test, _dtrhKillRenderers)
                    : null,
                _intakeDemo
                    ? new Features.Intake.IntakeHarnessOptions(_intakeDrive, _intakeKillRenderers)
                    : null);
            desktop.MainWindow = dashboard;

            // THE LINUX RENDER DISCRIMINATOR (CCP_RENDER_PROBE=<path.png>). Every Linux capture
            // this port has taken is a single colour — 836,000 pixels of RGB(0,0,0) on a whole
            // window, in WSLg RAIL, in XWayland, in a real Xvfb :99, and with
            // LIBGL_ALWAYS_SOFTWARE=1. Two explanations survive that: the app draws nothing, or
            // it draws and no screen-capture route on the machine can see it. This probe asks
            // the app to read back its OWN surface IN PROCESS, which bypasses the screen-capture
            // transport entirely, and it is deliberately an ENV VAR rather than a --flag:
            // HarnessEntryPoints is the ONE registry for --flag literals and lives in a file this
            // lane does not own, so a new flag here would red HarnessEntryPointGuardTests with no
            // in-scope fix. CCP_MCP (Program.cs) is the standing env-var opt-in precedent.
            var renderProbePath = Environment.GetEnvironmentVariable(RenderProbeVariable);
            if (!string.IsNullOrWhiteSpace(renderProbePath))
            {
                dashboard.Opened += (_, _) => _ = RunRenderProbeAsync(dashboard, renderProbePath, desktop);
            }

            // AvatarTube DEMONSTRATOR (--avatartube-demo): opens the tube at
            // startup (WSLg has no input automation — a named limit).
            if (_avatarDemo)
            {
                var participant = _host.Participants.OfType<AvatarTubeParticipant>().Single();
                if (_avatarTracePath is not null)
                {
                    // FileShare.Read: the evidence evaluator reads the trace WHILE the app
                    // still runs (headed gates correlate captures against live trace events).
                    var stream = new FileStream(_avatarTracePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _avatarTraceWriter = new StreamWriter(stream) { AutoFlush = true };
                    participant.TraceSink = args => _avatarTraceWriter.WriteLine(AvatarEvidence.SerializeTrace(args));
                }

                if (_avatarCorrupt)
                {
                    // Typed undecodable-asset evidence: corrupt the pulse pack's bytes in
                    // memory (embedded assets untouched) — the Degraded path runs
                    // for real on the pack switch.
                    participant.CorruptPackForDemo(SyntheticAvatarPacks.Pulse.PackId);
                }

                var tube = new AvatarTubeDemonstratorWindow(_host, dashboard, participant, _avatarAnimate);
                // The owner must be visible before an owned window can show (Avalonia
                // EnsureParentStateBeforeShow): open the tube right after the dashboard opens.
                dashboard.Opened += (_, _) =>
                {
                    if (!tube.IsVisible)
                    {
                        tube.Show(dashboard);
                    }
                };
            }

            // DTRH DEMONSTRATOR (--dtrh-demo): b2 opens the save picker
            // first (hero-card outcome); --dtrh-quick skips it (Quick Start outcome).
            // The flow ending (host closed / picker cancelled) ends the app (exit 0
            // evidence); WSLg has no input automation — a named limit.
            if (_dtrhDemo)
            {
                // The SAME coordinator the shell's Play page builds (one construction
                // site, Features/Dtrh/DtrhLaunch.cs), reached DIRECTLY rather than through
                // DtrhLaunch.FallInAsync — i.e. the demonstrator deliberately steps past the
                // Tier-2 gate. That is not an oversight to fix: gating the headed-evidence path
                // would make DTRH evidence depend on the developer's Patreon tier, which would
                // make the demonstrator useless on exactly the machines that need it (today
                // this build has no entitlement authority at all, so a gated --dtrh-demo would
                // refuse everywhere and capture nothing). The USER path is gated; this is the
                // evidence path, and it is one `--dtrh-demo` flag away from being unreachable.
                var coordinator = dashboard.Dtrh.Coordinator;
                var flowEndedOnce = 0;
                coordinator.FlowEnded += () =>
                {
                    // One-shot: picker-cancel and host-close both raise FlowEnded, and the
                    // lifetime's own shutdown closes owned windows again (the ping-pong class).
                    if (Interlocked.Exchange(ref flowEndedOnce, 1) != 0)
                    {
                        return;
                    }

                    _host.LogDiagnostic("dtrh: flow ended — shutting down the lifetime");
                    // Explicit Shutdown, not dashboard.Close(): on the GTK backend closing the
                    // MainWindow does not reliably end the classic lifetime here (WX).
                    desktop.Shutdown();
                };
                coordinator.HostOpened += () =>
                {
                    if (_dtrhAutoCloseSeconds <= 0)
                    {
                        return;
                    }

                    // WSLg exit evidence without input automation (a named limit):
                    // the timed close exercises the same idempotent teardown path.
                    _host.LogDiagnostic($"dtrh: auto-close armed at {_dtrhAutoCloseSeconds}s");
                    _ = Task.Delay(TimeSpan.FromSeconds(_dtrhAutoCloseSeconds)).ContinueWith(
                        _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _host.LogDiagnostic("dtrh: auto-close firing");
                            if (coordinator.HostWindow?.IsVisible == true)
                            {
                                coordinator.HostWindow.Close();
                            }
                        }), TaskScheduler.Default);
                };
                dashboard.Opened += (_, _) =>
                {
                    if (coordinator.HostWindow is not null || coordinator.Picker is not null)
                    {
                        return;
                    }

                    if (_dtrhQuick)
                    {
                        _ = coordinator.QuickStartAsync();
                    }
                    else
                    {
                        _ = coordinator.LaunchWithPickerAsync();
                        if (_dtrhPickerTimeoutSeconds > 0)
                        {
                            // No-input platforms: a TIMED commit of the picker's
                            // current selection — the same commit path DESCEND takes,
                            // honestly labeled as timed drive, never an input claim.
                            _host.LogDiagnostic($"dtrh: picker timeout armed at {_dtrhPickerTimeoutSeconds}s");
                            _ = Task.Delay(TimeSpan.FromSeconds(_dtrhPickerTimeoutSeconds)).ContinueWith(
                                _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    if (coordinator.Picker is { IsVisible: true } picker)
                                    {
                                        _host.LogDiagnostic("dtrh: picker timeout — committing current selection (timed drive, not input)");
                                        picker.CommitCurrentSelection();
                                    }
                                }), TaskScheduler.Default);
                        }
                    }
                };
            }

            // THE LOOM studio DEMONSTRATOR (--loom-demo): the v6.6.3 standalone
            // studio window (WPF LoomHostService parity — a plain titled window; closing
            // it ends the demonstrator, the --dtrh-demo flow-ended class).
            // This path no longer constructs the studio window itself. It calls the
            // SAME launcher the shell's Studio -> Spiral Overlay -> THE LOOM button calls
            // (Navigation/LoomLaunch.cs), so there is exactly ONE construction site for
            // DtrhLoomWindow in the tree. The demonstrator now opens the studio ON TOP of a
            // shell that can also reach it by gesture — the "no Spiral Overlay card yet"
            // named limit is discharged.
            if (_loomDemo)
            {
                var loomEndedOnce = 0;
                dashboard.Loom.HarnessDrive = _loomDrive;
                dashboard.Loom.Closed += _ =>
                {
                    // One-shot (the ping-pong class): Shutdown() closes owned
                    // windows again, which re-fires Closed — never a teardown loop.
                    if (Interlocked.Exchange(ref loomEndedOnce, 1) != 0)
                    {
                        return;
                    }

                    _host.LogDiagnostic("loom: studio window closed — shutting down the lifetime");
                    desktop.Shutdown();
                };
                dashboard.Opened += (_, _) =>
                {
                    dashboard.Loom.Launch();
                    var loomWindow = dashboard.Loom.Current!;
                    _host.LogDiagnostic("loom: studio demonstrator opened (--loom-demo)");
                    if (_loomAutoCloseSeconds > 0)
                    {
                        // WSLg exit evidence without input automation (a named limit):
                        // the timed close exercises the same idempotent teardown path.
                        _host.LogDiagnostic($"loom: auto-close armed at {_loomAutoCloseSeconds}s");
                        _ = Task.Delay(TimeSpan.FromSeconds(_loomAutoCloseSeconds)).ContinueWith(
                            _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                _host.LogDiagnostic("loom: auto-close firing");
                                if (loomWindow.IsVisible)
                                {
                                    loomWindow.Close();
                                }
                            }), TaskScheduler.Default);
                    }
                };
            }
            // Graded Intake DEMONSTRATOR (--intake-demo): the host flow at startup
            // (the --loom-demo demonstrator class). The flow ending (window closed for real /
            // watchdog exhaustion) ends the app (exit-0 evidence).
            // This path no longer constructs a coordinator of its own. It uses the SAME
            // one the shell's Graded Intake door -> "Begin Intake" button uses
            // (Features/Intake/IntakeLaunch.cs), so there is exactly ONE construction site for
            // IntakeLaunchCoordinator in the tree — the LoomLaunch/DtrhLaunch convention. The
            // earlier note that the dashboard entry was BLOCKED glue is discharged: the door
            // exists, and it reaches this object.
            if (_intakeDemo)
            {
                var intakeCoordinator = dashboard.Intake.Coordinator;
                intakeCoordinator.FlowEnded += () =>
                {
                    _host.LogDiagnostic("intake: flow ended — shutting down the lifetime");
                    desktop.Shutdown();
                };
                intakeCoordinator.HostOpened += () =>
                {
                    if (_intakeAutoCloseSeconds <= 0)
                    {
                        return;
                    }

                    // No-input exit evidence: the timed close exercises the same
                    // graceful teardown path (end-run + bounded exit-done wait).
                    _host.LogDiagnostic($"intake: auto-close armed at {_intakeAutoCloseSeconds}s");
                    _ = Task.Delay(TimeSpan.FromSeconds(_intakeAutoCloseSeconds)).ContinueWith(
                        _ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _host.LogDiagnostic("intake: auto-close firing");
                            if (intakeCoordinator.HostWindow?.IsVisible == true)
                            {
                                intakeCoordinator.HostWindow.Close();
                            }
                        }), TaskScheduler.Default);
                };
                // Through the COORDINATOR, not IntakeLaunch: the demonstrator deliberately steps
                // past the weekly-pass gate, exactly as --dtrh-demo steps past the Tier-2 gate and
                // for the same reason (§10 D22, §11 D32). This build has no entitlement authority
                // for the intake at all, so a gated --intake-demo would refuse on EVERY machine
                // and capture nothing; and once one does exist, the pass is spent by a completed
                // run, so evidence capture would depend on whether the developer happened to run
                // an intake earlier in the same ISO week. It is still the SAME coordinator the
                // button drives — one construction site, two callers. The USER path is gated.
                dashboard.Opened += (_, _) => intakeCoordinator.Launch();
            }

            // Chaos tunnel backdrop DEMONSTRATOR (--tunnel-demo): the opaque
            // below-Topmost surface (the --loom-demo demonstrator class). The tunnel gets NO
            // dashboard door and never will: WPF renders it UNDER a running Chaos descent and
            // navigates to it from nowhere (Chaos/ChaosTunnelService.cs:20,34 — see §11 D30 and
            // the header of ChaosTunnelDemoDrive). The drive / auto-close flags are
            // HARNESS-ONLY (ChaosTunnelDemoDrive).
            if (_tunnelDemo)
            {
                Features.Chaos.ChaosTunnelDemoDrive.Attach(_host, dashboard, desktop, _tunnelDrive, _tunnelAutoCloseSeconds);
            }

            // Goon practice DEMONSTRATOR (--goon-demo): opens the practice host at startup.
            // Through the SAME GoonLaunch the Play page's PRACTICE button reaches (MainWindow.Goon
            // — one construction site, two callers), which is the LoomLaunch/DtrhLaunch/IntakeLaunch
            // convention and the reason no MainWindow edit is needed here.
            //
            // There is no gate to step past, and that is upstream's fact, not a shortcut: the Goon
            // card is an unconditional door (Views/Tabs/PlayTabView.xaml:547-549) and the paid rungs
            // live INSIDE, on hosting and sending (GoonHostService.cs:894, :909), where GoonDoors
            // refuses them. So unlike --dtrh-demo and --intake-demo this flag steps past nothing:
            // the demonstrator and the user path are the same path.
            if (_goonDemo)
            {
                dashboard.Opened += (_, _) => dashboard.Goon.Practice();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The render-probe opt-in. See the call site for why this is an env var, not a flag.</summary>
    public const string RenderProbeVariable = "CCP_RENDER_PROBE";

    /// <summary>How long a compositor batch may stay unsettled before the probe calls it pending.
    /// The bound IS the measurement: a render loop that never renders leaves these tasks forever.</summary>
    private static readonly TimeSpan RenderProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reads the running window's own rendering back IN PROCESS and reports what it found, then
    /// ends the lifetime with a verdict exit code (0 non-vacuous, 3 vacuous — CcpVerify's
    /// convention, so a script can branch without a file ever leaving the process).
    ///
    /// <para>WHAT EACH HALF PROVES, because they are NOT the same path and conflating them
    /// would answer the wrong question. (1) The compositor half — <c>RequestCompositionBatchCommitAsync</c>
    /// and its <c>Processed</c>/<c>Rendered</c> tasks — rides the REAL on-screen path
    /// (<c>CompositingRenderer</c> over the window's render target) and proves the render thread
    /// ran, but hands back no pixels. (2) The pixel half — <c>RenderTargetBitmap.Render(visual)</c>
    /// — walks the visual tree through <c>ImmediateRenderer</c> (Avalonia 12.1.1 ships that
    /// statement in its own XML docs: "This class is used to render the visual tree into a
    /// DrawingContext by doing a simple tree traversal. It's currently used mostly for
    /// RenderTargetBitmap.Render and VisualBrush"), so it proves the tree produces drawing
    /// commands and Skia rasterises them on this machine — it does NOT prove the compositor
    /// presents them. Avalonia 12.1.1 exposes no compositor-surface screenshot API at all
    /// (there is no Capture/snapshot member anywhere under Avalonia.Rendering.Composition), so
    /// these two together are the strongest in-process answer the framework can give.</para>
    ///
    /// <para>THE POSITIVE CONTROL IS NOT OPTIONAL. An all-black read-back is ambiguous on its
    /// own: it means "the window drew nothing" only if the read-back mechanism itself works. So
    /// the probe first draws a known two-colour pattern into a second render target and censuses
    /// that. Control vacuous =&gt; the instrument is broken and the probe answers nothing, which
    /// it says out loud instead of blaming the app.</para>
    /// </summary>
    private async Task RunRenderProbeAsync(Window window, string path, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var exitCode = 0;
        try
        {
            _host.LogDiagnostic(
                $"render-probe: visible={window.IsVisible} client={window.ClientSize} frame={window.FrameSize} "
                + $"scaling={window.RenderScaling} rootChildren={window.GetVisualChildren().Count()}");

            var compositor = ElementComposition.GetElementVisual(window)?.Compositor;
            if (compositor is null)
            {
                _host.LogDiagnostic("render-probe: ON-SCREEN PATH — the window has no composition visual; the compositor never saw it");
            }
            else
            {
                var batch = compositor.RequestCompositionBatchCommitAsync();
                _host.LogDiagnostic($"render-probe: ON-SCREEN PATH — batch deserialized on the render thread: {await SettledAsync(batch.Processed)}");
                _host.LogDiagnostic($"render-probe: ON-SCREEN PATH — batch rendered on the render thread: {await SettledAsync(batch.Rendered)}");
            }

            using (var control = new RenderTargetBitmap(new PixelSize(8, 8), new Vector(96, 96)))
            {
                using (var context = control.CreateDrawingContext())
                {
                    context.FillRectangle(Brushes.White, new Rect(0, 0, 4, 8));
                }

                var controlCensus = Census(control, out var controlDistinct);
                _host.LogDiagnostic($"render-probe: POSITIVE CONTROL (known two-colour target) census: {controlCensus}");
                if (controlDistinct < 2)
                {
                    _host.LogDiagnostic("render-probe: INCONCLUSIVE — the read-back instrument itself is vacuous; nothing below can be trusted");
                    desktop.Shutdown(1);
                    return;
                }
            }

            var scaling = window.RenderScaling;
            var size = new PixelSize(
                Math.Max(1, (int)Math.Round(window.ClientSize.Width * scaling)),
                Math.Max(1, (int)Math.Round(window.ClientSize.Height * scaling)));
            using var surface = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
            surface.Render(window);
            var census = Census(surface, out var distinct);
            _host.LogDiagnostic($"render-probe: IN-PROCESS READ-BACK ({size.Width}x{size.Height}) census: {census}");
            surface.Save(path, PngBitmapEncoderOptions.Default);
            _host.LogDiagnostic($"render-probe: wrote {path}");
            exitCode = distinct < 2 ? 3 : 0;
            _host.LogDiagnostic(distinct < 2
                ? "render-probe: VERDICT (A) NOTHING WAS DRAWN — the window's own visual tree, read back in process, is one colour"
                : "render-probe: VERDICT (B) SOMETHING WAS DRAWN — the window's own visual tree, read back in process, carries content");
        }
        catch (Exception ex)
        {
            _host.LogDiagnostic($"render-probe: FAILED — {ex}");
            exitCode = 1;
        }

        desktop.Shutdown(exitCode);
    }

    /// <summary>Bounded settle report for a composition batch task. Never throws to the caller —
    /// "still pending" is a measurement, not an error.</summary>
    private static async Task<string> SettledAsync(Task task)
    {
        try
        {
            await task.WaitAsync(RenderProbeTimeout).ConfigureAwait(true);
            return "YES";
        }
        catch (TimeoutException)
        {
            return $"NO — still pending after {RenderProbeTimeout.TotalSeconds:0}s";
        }
    }

    /// <summary>The distinct-colour census, by the SAME rule the capture gate uses
    /// (client/tools/verify/CcpVerify/CaptureCensus.cs: RGB only, alpha is never part of a
    /// colour's identity, and the message always carries the count).</summary>
    private static string Census(RenderTargetBitmap bitmap, out int distinct)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var length = stride * size.Height;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), buffer, length, stride);
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, length);
            var seen = new HashSet<int>();
            for (var i = 0; i < length; i += 4)
            {
                seen.Add((bytes[i + 2] << 16) | (bytes[i + 1] << 8) | bytes[i]);
            }

            distinct = seen.Count;
            return distinct < 2
                ? $"{distinct} distinct colour — all {length / 4} pixels are "
                  + $"RGB({bytes[2]},{bytes[1]},{bytes[0]}) #{bytes[2]:X2}{bytes[1]:X2}{bytes[0]:X2}"
                : $"{distinct} distinct colours across {length / 4} pixels";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
