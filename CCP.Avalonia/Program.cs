using System;
using Avalonia;
#if DEBUG
using Keincheck;
#endif

namespace ConditioningControlPanel.Avalonia
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // WPF logs to a file (App.xaml.cs); this head had no sink at all, so every Log.* line was lost.
            // ponytail: stderr only, Information+; a rolling file when Serilog.Sinks.File is worth a package.
            Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Information()
                .WriteTo.Sink(new StderrSink()).CreateLogger();
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

            // --audio-probe plays a clip through the REAL LibVLC output and ducks/unducks other
            // apps via CoreAudio, printing pactl's view of each step. Run with another stream
            // playing (e.g. a looping pw-play) to see its volume drop and come back.
            if (Array.IndexOf(args, "--audio-probe") >= 0)
                return AudioProbe.Run();
            if (Array.IndexOf(args, "--layers-probe") >= 0)
                return AudioProbe.RunLayers();

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

            var app = BuildAvaloniaApp();
#if DEBUG
            // Keincheck MCP server on http://127.0.0.1:3001, Debug builds only. Kept out of
            // BuildAvaloniaApp so the XAML previewer does not start a second server.
            app = app.UseMcpServer();
#endif
            app.StartWithClassicDesktopLifetime(args);
            return 0;
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()      // Win32 on Windows, X11 on Linux (12.1.2 has no Wayland backend)
                .WithInterFont()
                .LogToTrace();
            // Pinned, not detected: every desktop overlay is an X11 override-redirect window
            // (Platform/X11Overlay.cs), and under a future native Wayland backend those calls would
            // silently no-op. On a Wayland session this runs through XWayland.
            // See docs/avalonia-decisions.md (desktop overlays).
            return OperatingSystem.IsLinux() ? builder.UseX11() : builder;
        }
    }

    /// <summary>One line per event on stderr: time, level, rendered message, exception.</summary>
    internal sealed class StderrSink : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent e) =>
            Console.Error.WriteLine($"{e.Timestamp:HH:mm:ss.fff} [{e.Level}] {e.RenderMessage()}{(e.Exception is null ? "" : " " + e.Exception)}");
    }
}
