using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.Chaos;

/// <summary>
/// HARNESS-ONLY demonstrator drive (the --dtrh-demo/--loom-demo class; the greenfield
/// dashboard has no Chaos game entry point — typed named limit). `--tunnel-demo` shows the
/// backdrop through the REAL service path (capability check → build → sink → ready →
/// run-start); `--tunnel-drive "a,b,c"` runs timed steps (the SP-008 no-input-automation
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
/// </summary>
public static class ChaosTunnelDemoDrive
{
    private static readonly TimeSpan StepCadence = TimeSpan.FromSeconds(4);

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
                        topmost.Show(dashboard);
                        host.LogDiagnostic("tunnel-drive: topmost surface shown (DtrhVideoWindow, Topmost=True, DtrhVideoWindow.axaml:6)");
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
                host.LogDiagnostic($"tunnel-drive: {steps.Length} step(s) armed at {StepCadence.TotalSeconds:0}s cadence (timed drive — never an input claim)");
                stepTimer.Start();
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
