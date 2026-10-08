using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Input;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--win-panic-check [BoundKey]`: the Windows twin of <see cref="PanicCheck"/>. Boots the real
    /// desktop path (App's own StartPanicKey wiring) on the sandbox profile, binds the panic key to
    /// BoundKey (default F24), starts the engine silent (Audio-Only, no filter, no spiral), and injects
    /// F24 through keybd_event, the path a physical key takes into the WH_KEYBOARD_LL hook. Asserts:
    /// one press stops the engine and the app stays up, the hook raised F24's down AND up, a HELD key
    /// (nine downs, one up) is one press (the app stays up), and two presses exit. Non-zero on any
    /// failure, so `--win-panic-check F23` (bound to a key that is never pressed) must fail.
    /// Only ever injects F24. Refuses to run without CCP_USERDATA_DIR (CorePaths is resolved before
    /// any argument is read, so the check cannot move the profile itself) and refuses Escape.
    /// </summary>
    internal static class Win32PanicCheck
    {
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        private const uint KEYEVENTF_KEYUP = 0x2;
        private const byte Injected = VirtualKeys.F24;

        public static int Run(string boundKey)
        {
            if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("FAIL: Windows only (Linux: --panic-check)"); return 1; }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CCP_USERDATA_DIR")))
            { Console.Error.WriteLine("FAIL: set CCP_USERDATA_DIR to a sandbox profile first"); return 1; }
            var boundVk = VirtualKeys.Of(boundKey);
            if (boundVk == 0 || boundVk == VirtualKeys.Escape) { Console.Error.WriteLine($"FAIL: refusing panic key '{boundKey}'"); return 1; }

            var fails = 0;
            void Check(bool ok, string what) { Console.WriteLine($"  [{(ok ? "ok" : "FAIL")}] {what}"); if (!ok) fails++; }
            int downs = 0, ups = 0;
            Win32PanicKey.KeyDown += (vk, _) => { if (vk == Injected) Interlocked.Increment(ref downs); };
            Win32PanicKey.KeyUp += (vk, _) => { if (vk == Injected) Interlocked.Increment(ref ups); };
            static void Down() => keybd_event(Injected, 0, 0, UIntPtr.Zero);
            static void Up() => keybd_event(Injected, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            static void Press() { Down(); Up(); }

            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Program.BuildAvaloniaApp().SetupWithLifetime(lifetime);
            var shell = (MainShellWindow)lifetime.MainWindow!;
            bool closed = false, forced = false;
            shell.Closed += (_, _) => closed = true;

            void StartSilentEngine()
            {
                var s = CoreSettings.Current;
                s.AudioOnlySession = true;   // the engine runs, nothing is drawn on the owner's screens
                s.PinkFilterEnabled = s.SpiralEnabled = false;
                shell.StartEngine();
            }

            DispatcherTimer.RunOnce(() =>
            {
                var s = CoreSettings.Current;
                s.PanicKey = boundKey;
                s.PanicKeyEnabled = true;
                StartSilentEngine();
                Check(Win32PanicKey.IsListening, "App started the WH_KEYBOARD_LL hook");
                Check(CoreEngine.IsRunning, "engine running before the press");
                Console.WriteLine($"panic key '{boundKey}' -> vk 0x{boundVk:X2}; injecting F24");
                Press();

                DispatcherTimer.RunOnce(() =>
                {
                    Check(Win32PanicKey.BoundVirtualKey == boundVk, $"hook bound vk 0x{Win32PanicKey.BoundVirtualKey:X2}");
                    Check(!CoreEngine.IsRunning, "one press: engine stopped");
                    Check(!closed, "one press: the app is still up");
                    Check(Volatile.Read(ref downs) == 1 && Volatile.Read(ref ups) == 1, $"hook raised F24 down x{downs}, up x{ups}");
                    HeldPhase();
                }, TimeSpan.FromMilliseconds(800));
            }, TimeSpan.FromMilliseconds(1500));

            // > 2 s after the last press: a new ladder. Nine downs 40 ms apart then one up is what a held
            // key sends; counted as presses, the second one would quit the app.
            void HeldPhase() => DispatcherTimer.RunOnce(() =>
            {
                StartSilentEngine();
                Check(CoreEngine.IsRunning, "held: engine running again");
                new Thread(() => { for (var i = 0; i < 9; i++) { Down(); Thread.Sleep(40); } Up(); }) { IsBackground = true }.Start();
                DispatcherTimer.RunOnce(() =>
                {
                    Check(!CoreEngine.IsRunning, "held: engine stopped");
                    Check(!closed, "held: nine downs were ONE press, the app is still up");
                    // Two separate presses of a new ladder: the second exits.
                    DispatcherTimer.RunOnce(() =>
                    {
                        Press();
                        DispatcherTimer.RunOnce(Press, TimeSpan.FromMilliseconds(300));
                    }, TimeSpan.FromMilliseconds(2500));
                    DispatcherTimer.RunOnce(() => { if (!closed) { forced = true; lifetime.Shutdown(); } }, TimeSpan.FromMilliseconds(6000));
                }, TimeSpan.FromMilliseconds(1200));
            }, TimeSpan.FromMilliseconds(2500));

            lifetime.Start(Array.Empty<string>());
            Check(closed && !forced, "double press: the app exited");
            Win32PanicKey.Stop();
            Console.WriteLine(fails == 0 ? "PASS" : $"FAIL ({fails})");
            return fails == 0 ? 0 : 1;
        }
    }
}
