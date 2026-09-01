using System;
using Avalonia;

namespace ConditioningControlPanel.Avalonia
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // --smoke runs a headless self-check and exits, so CI can prove the head boots
            // without a display server. Without it the app starts normally.
            if (Array.IndexOf(args, "--smoke") >= 0)
                return HeadlessSmoke.Run();

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

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()      // X11 or Wayland on Linux, Win32 on Windows - one binary
                .WithInterFont()
                .LogToTrace();
    }
}
