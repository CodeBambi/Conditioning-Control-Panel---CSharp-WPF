using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--panic-check [Key]`: boots the real desktop path (so App's own StartPanicKey wiring is what
    /// runs) on a throwaway profile, starts bouncing text, then presses the panic key through the X
    /// SERVER - XTestFakeKeyEvent on a second connection, exactly the path a physical key takes -
    /// and asserts the overlay stopped, the app stayed up, and a double press exits. Default key
    /// Pause, which nothing else on a desktop reacts to. Non-zero on any failure. Run it through
    /// scripts/panic-check.sh: on a live KWin session XTest never comes back into Xwayland.
    /// </summary>
    internal static class PanicCheck
    {
        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
        [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
        [DllImport("libXtst.so.6")] private static extern bool XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);

        public static int Run(string key)
        {
            var profile = Directory.CreateTempSubdirectory("ccp-panic-check-").FullName;
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", profile);   // before CorePaths is first read

            var fails = 0;
            void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }

            var xtest = XOpenDisplay(IntPtr.Zero);
            if (xtest == IntPtr.Zero) { Console.Error.WriteLine("FAIL: no X display"); return 1; }
            var keycode = XKeysymToKeycode(xtest, X11PanicKey.KeysymOf(key));
            Console.WriteLine($"panic key '{key}' -> keycode {keycode}");
            if (keycode == 0) { Console.Error.WriteLine("FAIL: key has no keycode"); return 1; }
            void Press()
            {
                XTestFakeKeyEvent(xtest, keycode, true, 0);
                XTestFakeKeyEvent(xtest, keycode, false, 0);
                XFlush(xtest);
            }

            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
            var shell = (MainShellWindow)lifetime.MainWindow!;
            bool closed = false, forced = false;
            shell.Closed += (_, _) => closed = true;

            DispatcherTimer.RunOnce(() =>
            {
                var s = CoreSettings.Current;
                s.PanicKey = key;
                s.PanicKeyEnabled = true;
                s.BouncingTextEnabled = true;
                BouncingTextOverlay.Start(shell);
                Check(X11PanicKey.IsListening, "App started the X11 panic listener");
                Check(BouncingTextOverlay.IsRunning, "bouncing text is on screen before the press");
                Press();

                DispatcherTimer.RunOnce(() =>
                {
                    Check(!BouncingTextOverlay.IsRunning, "one press: bouncing text stopped");
                    Check(!CoreSettings.Current.BouncingTextEnabled, "one press: the feature is unticked");
                    Check(!closed, "one press: the app is still up");
                    // The first press was > 2 s ago by now, so these are presses 1 and 2 of a new ladder.
                    DispatcherTimer.RunOnce(() => { Press(); Press(); }, TimeSpan.FromMilliseconds(1500));
                    DispatcherTimer.RunOnce(() => { if (!closed) { forced = true; lifetime.Shutdown(); } }, TimeSpan.FromMilliseconds(4000));
                }, TimeSpan.FromMilliseconds(800));
            }, TimeSpan.FromMilliseconds(1500));

            lifetime.Start(Array.Empty<string>());
            Check(closed && !forced, "double press: the app exited");
            XCloseDisplay(xtest);
            try { Directory.Delete(profile, recursive: true); } catch { }
            Console.WriteLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
    }
}
