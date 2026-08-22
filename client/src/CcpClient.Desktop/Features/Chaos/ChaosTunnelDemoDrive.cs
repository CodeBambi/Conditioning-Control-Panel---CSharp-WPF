using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// HARNESS-ONLY demonstrator drive (the --dtrh-demo/--loom-demo class). `--tunnel-demo` shows
/// the backdrop through the REAL service path (capability check → build → sink → ready →
/// run-start); `--tunnel-drive "a,b,c"` runs timed steps (the no-input-automation
/// class — honestly labeled timed drive, never an input claim):
///
///   topmost-show / topmost-hide — the REAL DtrhVideoWindow (Topmost=True,
///       DtrhVideoWindow.axaml:6) over a never-playing stub backend (black letterbox
///       surface — the occlusion capture's Topmost rect)
///   tunnel-close / tunnel-show — full CloseActive / Show cycles (the show/hide cycle the
///       layering contract names; re-show lands the tunnel UNDER an already-visible
///       Topmost surface — the direction that actually proves the sink)
///   finish — CloseActive (graceful exit path), then shut the lifetime down when the
///       window is gone (exit-0 evidence)
///
/// `--tunnel-auto-close N` = timed CloseActive without a drive (WSLg exit-evidence class).
/// On a typed-unavailable surface the demo logs the typed line and exits 0 honestly
/// (consult ruling 3 — never a hang, never an empty window).
///
/// <para><b>WHY THIS SURFACE HAS NO DOOR — a correction.</b> This comment used to open
/// with "the greenfield dashboard has no Chaos game entry point — typed named limit", which read
/// as a port gap waiting for a door. It is not one. <b>WPF has no tunnel entry point either.</b>
/// The tunnel is "the endless three.js 'rabbit hole' tunnel rendered UNDER the whole Chaos game"
/// (<c>ConditioningControlPanel/Chaos/ChaosTunnelService.cs:20</c>) — a single non-topmost
/// fullscreen window carrying <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c> so it cannot take focus
/// and never appears in Alt-Tab (<c>:31-32</c>), gated on <c>ChaosTunnelEnabled</c> and
/// <b>default OFF</b> (<c>:34,:58</c>). Nothing navigates to it and no button opens it: every
/// caller is the classic descent's own service — <c>Services/Chaos/ChaosModeService.cs:345</c>
/// (preload under the countdown), <c>:518</c> (show at run start), <c>:3042</c> (zone hint),
/// <c>:3246</c> (close). Its user-facing control is a CHECKBOX, <c>ChkTunnel</c>, in the Chaos
/// SETUP LOBBY — the Warren (<c>Chaos/ChaosHubWindow.xaml.cs:1566</c> read, <c>:1667</c> write) —
/// which is a pre-run screen: all three of its construction sites return early when a descent is
/// already running (<c>MainWindow/MainWindow.Lab.cs:242</c> then <c>:262-264</c>;
/// <c>Chaos/ChaosOverlayWindow.xaml.cs:873</c> then <c>:880</c>;
/// <c>Services/Chaos/DtrhHostService.cs:879</c> then <c>:886</c>). A setting on a setup screen is
/// still not a destination, so a rail door here would be a port invention with no WPF
/// counterpart. What the port is actually missing is the Chaos RUN — and the lobby that
/// configures it — for the backdrop to sit under: a feature row, not a door. Until that lands
/// <c>--tunnel-demo</c> is the correct and only way to render this surface. Recorded at
/// wpf-surface-reachability.md §11 D30.</para>
///
/// <para><b>An earlier revision of this comment said the lobby was "reachable only from inside a
/// running classic descent", which is the INVERSE of what its own citation does</b> — and it is
/// left named here rather than silently rewritten, because the failure mode matters more than the
/// sentence. The citation pointed at real lines; the prose described the conclusion the author
/// had already reached, and it happened to make WPF's tunnel control look less reachable than it
/// is, which flattered the no-door verdict. Third instance in this project of a citation aimed at
/// real lines while describing their opposite (wpf-surface-reachability.md §8.5's occluded title;
/// §10 D24's two-term grant recorded as one term). Read the cited lines, then write the
/// sentence.</para>
/// </summary>
public static class ChaosTunnelDemoDrive
{
    private static readonly TimeSpan StepCadence = TimeSpan.FromSeconds(10);

    public static void Attach(
        ApplicationHost host, Window dashboard, IClassicDesktopStyleApplicationLifetime desktop,
        string? drive, int autoCloseSeconds)
    {
        var service = new ChaosTunnelService(host, dashboard);
        DtrhVideoWindow? topmost = null;
        var steps = (drive ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var stepIndex = 0;
        var stepTimer = new DispatcherTimer { Interval = StepCadence };
        var exitPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };

        void ShutdownWhenGone(string why)
        {
            host.LogDiagnostic($"tunnel-drive: finishing ({why}) — CloseActive, shutdown when the window is gone");
            service.CloseActive();
            exitPoll.Start();
        }

        exitPoll.Tick += (_, _) =>
        {
            if (!service.WindowLive)
            {
                exitPoll.Stop();
                host.LogDiagnostic("tunnel-drive: tunnel window gone — shutting down the lifetime");
                desktop.Shutdown();
            }
        };

        stepTimer.Tick += (_, _) =>
        {
            if (stepIndex >= steps.Length)
            {
                stepTimer.Stop();
                return;
            }

            var step = steps[stepIndex++];
            host.LogDiagnostic($"tunnel-drive: step '{step}'");
            switch (step)
            {
                case "topmost-show":
                    if (topmost is null)
                    {
                        topmost = new DtrhVideoWindow(new NeverPlayingVideoBackend());
                        // HARNESS sizing: a deliberate sub-screen occlusion rect (the stock
                        // 960x540 DIPs cover nearly the whole 175%-scaled laptop screen,
                        // which would weaken the inside/outside patch comparison).
                        topmost.Width = 480;
                        topmost.Height = 270;
                        topmost.WindowStartupLocation = WindowStartupLocation.Manual;
                        topmost.Position = new PixelPoint(200, 150);
                        topmost.Show(dashboard);
                        host.LogDiagnostic("tunnel-drive: topmost surface shown (DtrhVideoWindow, Topmost=True, DtrhVideoWindow.axaml:6; harness-sized 480x270 DIP @ (200,150))");
                    }

                    break;
                case "topmost-hide":
                    topmost?.Close();
                    topmost = null;
                    host.LogDiagnostic("tunnel-drive: topmost surface hidden");
                    break;
                case "tunnel-close":
                    service.CloseActive();
                    host.LogDiagnostic("tunnel-drive: tunnel CloseActive (graceful exit path)");
                    break;
                case "tunnel-show":
                    service.Show();
                    host.LogDiagnostic("tunnel-drive: tunnel re-shown (fresh build + sink under the live Topmost surface)");
                    break;
                case "finish":
                    stepTimer.Stop();
                    ShutdownWhenGone("drive step");
                    break;
                default:
                    host.LogDiagnostic($"tunnel-drive: unknown step '{step}' (typed, skipped)");
                    break;
            }
        };

        dashboard.Opened += (_, _) =>
        {
            if (service.SurfaceState is not CapabilityState.Available)
            {
                host.LogDiagnostic("tunnel-demo: surface unavailable (typed line above) — exiting honestly, no window");
                desktop.Shutdown();
                return;
            }

            service.Preload();
            service.Show();
            host.LogDiagnostic("tunnel-demo: tunnel shown through the real service path (--tunnel-demo)");
            if (steps.Length > 0)
            {
                // Arm the timed steps on the REAL boot signal (first page ready) — never a
                // guessed delay that could fire a step before the tunnel renders.
                service.PageReady += () =>
                {
                    if (!stepTimer.IsEnabled && stepIndex < steps.Length)
                    {
                        host.LogDiagnostic($"tunnel-drive: page ready — {steps.Length} step(s) armed at {StepCadence.TotalSeconds:0}s cadence (timed drive — never an input claim)");
                        stepTimer.Start();
                    }
                };
            }

            if (autoCloseSeconds > 0)
            {
                host.LogDiagnostic($"tunnel-demo: auto-close armed at {autoCloseSeconds}s");
                _ = Task.Delay(TimeSpan.FromSeconds(autoCloseSeconds)).ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => ShutdownWhenGone($"auto-close {autoCloseSeconds}s")),
                    TaskScheduler.Default);
            }
        };
    }

    /// <summary>The layering proof's Topmost surface needs a backend seam but no playback:
    /// the black letterbox window IS the occluding rect. Every member is an honest no-op.</summary>
    private sealed class NeverPlayingVideoBackend : IDtrhVideoBackend
    {
        public long FrameCount => 0;
        public double PositionSec => 0;
        public Avalonia.Media.Imaging.WriteableBitmap? CurrentFrame => null;
#pragma warning disable CS0067 // events never raised — the stub never plays (HARNESS-ONLY)
        public event EventHandler? FramePresented;
        public event EventHandler? PlaybackEnded;
        public event EventHandler? EncounteredError;
#pragma warning restore CS0067
        public bool TryPlay(string path) => false;
        public void SetPaused(bool paused) { }
        public void Stop() { }
    }
}
