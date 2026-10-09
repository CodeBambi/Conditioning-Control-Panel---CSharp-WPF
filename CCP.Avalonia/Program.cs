using System;
using System.Linq;
using Avalonia;
using Serilog;
#if DEBUG
using Keincheck;
#endif

namespace ConditioningControlPanel.Avalonia
{
    internal static class Program
    {
        /// <summary>The Linux app id: .desktop file, WM_CLASS, portal registration (packaging/linux).</summary>
        internal const string AppId = "io.github.CodeBambi.ConditioningControlPanel";

        [STAThread]
        public static int Main(string[] args)
        {
            // WPF logs to a file under UserData/logs (App.xaml.cs:1967, LogPipeline.cs); stderr stays for CI and kc.
            // ponytail: WPF's path redaction, flight recorder and per-run naming come with the bug-report port.
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.Sink(new StderrSink())
                .WriteTo.File(System.IO.Path.Combine(ConditioningControlPanel.CorePaths.UserData, "logs", "ccp-avalonia-.log"),
                    rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 14,
                    fileSizeLimitBytes: 10_000_000, rollOnFileSizeLimit: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1))
                .CreateLogger();
            // The session's hold on the phrase pools (#906); WPF seeds the same three in App.xaml.cs:324.
            ConditioningControlPanel.Services.PhrasePoolCustody.Seed();

            // --smoke runs a headless self-check and exits, so CI can prove the head boots
            // without a display server. Without it the app starts normally.
            if (Array.IndexOf(args, "--smoke") >= 0)
                return HeadlessSmoke.Run();

            // --nav-check drives the shell's tab navigation headlessly and fails if a click
            // would land on a blank page. The render proofs are still frames; this is the one
            // check that exercises a click.
            if (Array.IndexOf(args, "--nav-check") >= 0)
                return NavCheck.Run();

            // --click-through <dir> dispatches REAL pointer clicks at real controls and saves a
            // frame per step. The only proof here that can see a button which draws but does not
            // take a click - which is what the nav doors were doing.
            var ct = Array.IndexOf(args, "--click-through");
            if (ct >= 0)
            {
                if (ct + 1 >= args.Length)
                {
                    Console.Error.WriteLine("usage: --click-through <out-dir>");
                    return 2;
                }
                return ClickThrough.Run(args[ct + 1]);
            }

            // --render <path> draws the real window offscreen and saves a PNG. Visual proof that
            // survives on a CI runner with no display server.
            var r = Array.IndexOf(args, "--render");
            if (r >= 0 && r + 1 < args.Length)
                return RenderProof.Run(args[r + 1]);

            // --render-view <TypeName> <path> renders ONE ported view by name, e.g.
            //   --render-view AchievementsTabView out.png
            // The name is matched against every Control under the Views namespace (simple name
            // or full name). Until this existed the flag ignored its argument and always drew
            // AppShell, so 20 of the first 21 ported views had never been rendered by anything.
            var rv = Array.IndexOf(args, "--render-view");
            if (rv >= 0)
            {
                if (rv + 2 >= args.Length)
                {
                    Console.Error.WriteLine("usage: --render-view <TypeName> <out.png>");
                    return 2;
                }
                return RenderProof.RunView(args[rv + 1], args[rv + 2]);
            }

            // --render-all <dir> renders every view under Views/ to <dir>/<TypeName>.png and
            // fails if any one throws. This is the per-view proof CI uploads.
            var ra = Array.IndexOf(args, "--render-all");
            if (ra >= 0)
            {
                if (ra + 1 >= args.Length)
                {
                    Console.Error.WriteLine("usage: --render-all <dir>");
                    return 2;
                }
                return RenderProof.RunAll(args[ra + 1]);
            }

            // --x11-probe drives the X11 overlay shim against a real window and asks the X
            // server which window owns the pointer. Every way that shim can fail returns
            // success and changes nothing, so only the server's answer proves it works.
            // Run it inside a nested compositor - scripts/x11-overlay-probe.sh does that.
            if (Array.IndexOf(args, "--x11-probe") >= 0)
                return X11OverlayProbe.Run();

            // --overlay-check opens one click-through override-redirect overlay per screen and
            // reads map state, override_redirect, depth, input shape and geometry back from the
            // X server. Non-zero on any mismatch. Safe on a live session (transparent, no input).
            if (Array.IndexOf(args, "--overlay-check") >= 0)
                return OverlayCheck.Run();

            // --tray-probe: prints whether a StatusNotifierWatcher owns its name (X hides to tray only then).
            if (Array.IndexOf(args, "--tray-probe") >= 0)
            {
                var present = Views.Windows.MainShellWindow.ProbeTrayHost();
                Console.WriteLine($"tray host present: {present}");
                return present ? 0 : 1;
            }

            // --notify-check: org.freedesktop.Notifications Notify -> id -> CloseNotification, live.
            if (Array.IndexOf(args, "--notify-check") >= 0)
                return Platform.OsNotifications.CheckAsync().GetAwaiter().GetResult();

            // --portal-check: GlobalShortcuts CreateSession -> Response 0 -> Session.Close, live.
            if (Array.IndexOf(args, "--portal-check") >= 0)
                return Platform.PortalPanicShortcut.CheckAsync().GetAwaiter().GetResult();

            // --panic-check [Key] presses the panic key through XTest against the real app on a temp
            // profile and fails unless one press stops bouncing text and a double press exits.
            var pc = Array.IndexOf(args, "--panic-check");
            if (pc >= 0)
                return PanicCheck.Run(pc + 1 < args.Length ? args[pc + 1] : "Pause");
            // --win-panic-check [Key]: the Windows twin; injects F24 into the WH_KEYBOARD_LL hook (sandbox only).
            var wpc = Array.IndexOf(args, "--win-panic-check");
            if (wpc >= 0)
                return Win32PanicCheck.Run(wpc + 1 < args.Length && !args[wpc + 1].StartsWith("--") ? args[wpc + 1] : "F24");

            // --fx-bench prints ms per frame for the ambient presets (headless Skia) and exits.

            if (Array.IndexOf(args, "--fx-bench") >= 0)

            { RenderProof.EnsureSetUp(); Console.WriteLine(Controls.Fx.FxBench.Format(Controls.Fx.FxBench.Run())); return 0; }


            // --audio-probe plays a clip through the REAL LibVLC output and ducks/unducks other
            // apps via CoreAudio, printing pactl's view of each step. Run with another stream
            // playing (e.g. a looping pw-play) to see its volume drop and come back.
            if (Array.IndexOf(args, "--audio-probe") >= 0)
                return AudioProbe.Run();
            if (Array.IndexOf(args, "--layers-probe") >= 0)
                return AudioProbe.RunLayers();

            // --speech-check <modelRoot> <pulseSource> <phrase>: one real grammar session through
            // PulseMicSource on the given source (scripts/speech-capture-check.sh feeds a null sink's
            // monitor; never a real mic). Exit 0 = matched, 1 = not matched.
            var sc = Array.IndexOf(args, "--speech-check");
            if (sc >= 0 && sc + 3 >= args.Length)
            {
                Console.Error.WriteLine("usage: --speech-check <modelRoot> <pulseSource> <phrase>");
                return 2;
            }
            if (sc >= 0)
            {
                using var engine = new ConditioningControlPanel.Services.Speech.SpeechEngine(
                    new Platform.PulseMicSource(args[sc + 2]), new[] { args[sc + 1] });
                var heard = engine.RecognizePhraseAsync(args[sc + 3],
                    new ConditioningControlPanel.Services.Speech.RecognizeOptions { Timeout = TimeSpan.FromSeconds(12) })
                    .GetAwaiter().GetResult();
                Console.WriteLine($"speech-check: matched={heard.Matched} heard='{heard.Transcript}' score={heard.Score:0.00} " +
                                  $"loud={heard.LoudEnough} timedOut={heard.TimedOut} unavailable={heard.Unavailable}");
                // Still alive, so a recorder that Stop failed to kill would still be here (no SIGPIPE yet).
                System.Threading.Thread.Sleep(500);
                var src = args[sc + 2];
                var stray = System.IO.Directory.GetDirectories("/proc").Any(d =>
                {
                    try { var c = System.IO.File.ReadAllText(d + "/cmdline"); return c.Contains(src) && (c.StartsWith("parec") || c.StartsWith("pw-record")); }
                    catch { return false; }
                });
                if (stray) { Console.Error.WriteLine("speech-check: recorder still running after the session"); return 3; }
                return heard.Matched ? 0 : 1;
            }

            // --video-check <file> [out.png] plays a video in the real MiniPlayerWindow and fails
            // unless frames change, seek moves, pause freezes and close frees the player.
            var vc = Array.IndexOf(args, "--video-check");
            if (vc >= 0)
            {
                if (vc + 1 >= args.Length)
                {
                    Console.Error.WriteLine("usage: --video-check <file> [out.png]");
                    return 2;
                }
                return VideoCheck.Run(args[vc + 1], vc + 2 < args.Length ? args[vc + 2] : null);
            }

            // WPF's single-instance gate: a second launch hands its surface (or none) to the running app
            // and exits; the primary routes it (WPF RouteSurfaceHandoff / LauncherHost.OnBareRelaunch).
            using var instance = Platform.SingleInstance.Claim(Platform.SingleInstance.SandboxSuffix(), payload =>
                global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var w = (global::Avalonia.Application.Current?.ApplicationLifetime as
                        global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    if (w is Views.Windows.MainShellWindow shell) Views.Windows.LauncherWindow.RouteHandoff(shell, payload);
                    else w?.Activate();
                }).GetTask(), Services.Launcher.LauncherHandoff.Encode(args));
            if (instance is null)
            {
                Serilog.Log.Information("Another instance is running; asked it to show its window");
                return 0;
            }

            var app = BuildAvaloniaApp();
#if DEBUG
            // Keincheck MCP server on http://127.0.0.1:3001, Debug builds only. Kept out of
            // BuildAvaloniaApp so the XAML previewer does not start a second server.
            app = app.UseMcpServer();
#endif
            // SIGTERM (logout, systemd, kill) is not handled by Avalonia's X11 backend: the app ignored it and needed kill -9.
            // Shut down through the lifetime instead, so settings flush exactly as on tray Exit.
            using var term = System.Runtime.InteropServices.PosixSignalRegistration.Create(
                System.Runtime.InteropServices.PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        (global::Avalonia.Application.Current?.ApplicationLifetime
                            as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
                });
            App.SplashOnStartup = true;
            app.StartWithClassicDesktopLifetime(args);
            App.StartupFailure?.Throw();   // a failed startup crashes non-zero, as it did before the splash
            return 0;
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()      // Win32 on Windows, X11 on Linux (12.1.2 has no Wayland backend)
                .WithInterFont()
                .LogToTrace()
                // Home keeps ~85 MB of tile pictures on the GPU; Avalonia's default 28 MB Skia cache
                // re-uploaded them every frame (Platform/RenderBudget.cs has the trace numbers).
                .With(new SkiaOptions { MaxGpuResourceSizeBytes = Platform.RenderBudget.GpuResourceCacheBytes });
            if (OperatingSystem.IsWindows())
                builder = builder.With(Win32Options());
            // Pinned, not detected: every desktop overlay is an X11 override-redirect window
            // (Platform/X11Overlay.cs), and under a future native Wayland backend those calls would
            // silently no-op. On a Wayland session this runs through XWayland.
            // See docs/avalonia-decisions.md (desktop overlays).
            // WM_CLASS = the app id, so desktops match windows to the .desktop file (StartupWMClass).
            return OperatingSystem.IsLinux()
                ? builder.UseX11().With(new X11PlatformOptions { WmClass = AppId })
                : builder;
        }

        /// <summary>The Win32 presentation path. Avalonia's defaults (WinUI composition over ANGLE on
        /// the primary adapter) unless CCP_WIN32_COMPOSITION / CCP_WIN32_RENDERING / CCP_GPU_ADAPTER
        /// say otherwise (Platform/RenderBudget.cs); the adapter list is logged once either way, so a
        /// log says which GPU drew the frames.</summary>
        private static global::Avalonia.Win32PlatformOptions Win32Options()
        {
            var o = new global::Avalonia.Win32PlatformOptions();
            var comp = Platform.RenderBudget.ParseModes<global::Avalonia.Win32CompositionMode>(
                Environment.GetEnvironmentVariable("CCP_WIN32_COMPOSITION"), s => s switch
                {
                    "winui" => global::Avalonia.Win32CompositionMode.WinUIComposition,
                    "dcomp" => global::Avalonia.Win32CompositionMode.DirectComposition,
                    "swapchain" => global::Avalonia.Win32CompositionMode.LowLatencyDxgiSwapChain,
                    "redirection" => global::Avalonia.Win32CompositionMode.RedirectionSurface,
                    _ => null,
                });
            if (comp != null) o.CompositionMode = comp;
            var rend = Platform.RenderBudget.ParseModes<global::Avalonia.Win32RenderingMode>(
                Environment.GetEnvironmentVariable("CCP_WIN32_RENDERING"), s => s switch
                {
                    "angle" => global::Avalonia.Win32RenderingMode.AngleEgl,
                    "wgl" => global::Avalonia.Win32RenderingMode.Wgl,
                    "vulkan" => global::Avalonia.Win32RenderingMode.Vulkan,
                    "software" => global::Avalonia.Win32RenderingMode.Software,
                    _ => null,
                });
            if (rend != null) o.RenderingMode = rend;
            if (comp != null || rend != null)
                Serilog.Log.Information("Win32 presentation override: composition {C}, rendering {R}",
                    comp == null ? "default" : string.Join(",", comp), rend == null ? "default" : string.Join(",", rend));
            o.GraphicsAdapterSelectionCallback = adapters =>
            {
                var names = adapters.Select(a => a.Description).ToList();
                int pick = Platform.RenderBudget.ChooseAdapter(names, Environment.GetEnvironmentVariable("CCP_GPU_ADAPTER"));
                Serilog.Log.Information("GPU adapters: {List}; rendering on #{Pick} {Name}",
                    string.Join(" | ", names.Select((n, i) => $"#{i} {n}")), pick, names.Count > pick ? names[pick] : "?");
                return pick;
            };
            return o;
        }
    }

    /// <summary>One line per event on stderr: time, level, rendered message, exception.</summary>
    internal sealed class StderrSink : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent e) =>
            Console.Error.WriteLine($"{e.Timestamp:HH:mm:ss.fff} [{e.Level}] {e.RenderMessage()}{(e.Exception is null ? "" : " " + e.Exception)}");
    }
}
