using System;
using System.IO;
using System.Linq;
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
    /// runs) on a throwaway profile, starts the engine with flash/subliminal/bouncing text saved On, then presses the panic key through the X
    /// SERVER - XTestFakeKeyEvent on a second connection, exactly the path a physical key takes -
    /// and asserts the engine and overlays stopped within 800 ms, the flags stayed On, the app stayed up; then that a press pauses a
    /// running session and Resume restarts only what has reached its start minute; and a double press exits. Default key
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

            int Overlays() => lifetime.Windows.Count(w => w.IsVisible && w.GetType().Namespace!.EndsWith(".Overlays"));
            DispatcherTimer.RunOnce(() =>
            {
                var s = CoreSettings.Current;
                s.PanicKey = key;
                s.PanicKeyEnabled = true;
                s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = true;
                shell.StartEngine();
                Check(X11PanicKey.IsListening, "App started the X11 panic listener");
                Check(CoreEngine.IsRunning && BouncingTextOverlay.IsRunning, "engine started, bouncing text on screen before the press");
                Check(Overlays() > 0, $"{Overlays()} overlay windows before the press");
                Press();

                DispatcherTimer.RunOnce(() =>
                {
                    Check(!CoreEngine.IsRunning && !CoreFlash.IsRunning && !CoreSubliminal.IsRunning && !BouncingTextOverlay.IsRunning,
                        "one press: engine and every effect stopped");
                    // Overlay windows only: the first-run wizard and avatar tube are the app's own.
                    var overlays = Overlays();
                    Check(overlays == 0, $"one press: {overlays} overlay windows left");
                    Check(s.FlashEnabled && s.SubliminalEnabled && s.BouncingTextEnabled, "one press: saved flags still On");
                    Check(!closed, "one press: the app is still up");
                    SessionPhase();
                }, TimeSpan.FromMilliseconds(800));
            }, TimeSpan.FromMilliseconds(1500));

            // A session: the press pauses it (WPF MainWindow.xaml.cs:1726) and Resume restarts only what has
            // reached its start minute - flash and subliminal at 0, bouncing text deferred to minute 1.
            void SessionPhase()
            {
                var session = new Models.Session { Id = "panic_check", Name = "Panic Check", DurationMinutes = 2 };
                session.Settings.FlashEnabled = session.Settings.SubliminalEnabled = session.Settings.BouncingTextEnabled = true;
                session.Settings.FlashPerHour = 600;
                session.Settings.BouncingTextStartMinute = 1;
                shell.StartSession(session);
                var runner = App.Sessions!;
                Check(runner.IsRunning && CoreFlash.IsRunning && CoreSubliminal.IsRunning && !BouncingTextOverlay.IsRunning,
                    "session: flash + subliminal running, bouncing text deferred to minute 1");
                DispatcherTimer.RunOnce(() =>
                {
                    Press();   // > 2 s after the last press: a new ladder
                    DispatcherTimer.RunOnce(() =>
                    {
                        Check(runner.IsRunning && runner.IsPaused && runner.PauseCount == 1 && runner.XPPenalty == 100,
                            "session press: the session is paused, not ended (1 pause, -100 XP)");
                        Check(!CoreEngine.IsRunning && !CoreFlash.IsRunning && !CoreSubliminal.IsRunning && !BouncingTextOverlay.IsRunning,
                            "session press: engine and every effect stopped");
                        Check(Overlays() == 0, $"session press: {Overlays()} overlay windows left");
                        shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Check(!runner.IsPaused && CoreFlash.IsRunning && CoreSubliminal.IsRunning && !BouncingTextOverlay.IsRunning,
                            "Resume: flash + subliminal restarted, bouncing text still waits for minute 1");
                        // Presses 1 and 2 of a new ladder: the first pauses again, the second exits.
                        DispatcherTimer.RunOnce(() => { Press(); Press(); }, TimeSpan.FromMilliseconds(2500));
                        DispatcherTimer.RunOnce(() => { if (!closed) { forced = true; lifetime.Shutdown(); } }, TimeSpan.FromMilliseconds(5000));
                    }, TimeSpan.FromMilliseconds(800));
                }, TimeSpan.FromMilliseconds(2500));
            }

            lifetime.Start(Array.Empty<string>());
            Check(closed && !forced, "double press: the app exited");
            XCloseDisplay(xtest);
            try { Directory.Delete(profile, recursive: true); } catch { }
            Console.WriteLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
    }
}
