// PORTED from ConditioningControlPanel/Services/Notifications/TrayIconService.cs plus the tray
// half of MainWindow.WindowChrome.cs OnClosing (X -> tray) and MainWindow.Launcher.cs RequestExit.
//
// Avalonia's TrayIcon is StatusNotifierItem over D-Bus on Linux and a native icon on Windows.
// Divergences, on purpose (docs/avalonia-decisions.md, tray row):
//   - The icon stays visible while the window is up (WPF shows it only while hidden). On this head
//     the tray is the panic control a takeover needs without a global hotkey, so it must always be
//     reachable.
//   - "Stop everything" is an extra item for that reason: it is StopEngine - overlays down, saved
//     flags untouched, so the next Start brings them back. The app stays up.
//   - With no tray host (Linux desktop without a StatusNotifierWatcher) Avalonia's tray falls back
//     silently, so X would hide the window behind nothing. X is gated on a host probe: no host, X exits.
// The first-minimize balloon (TrayIconService.cs:165-171) goes through Platform/OsNotifications.
// ponytail: "launcher_back_to_client" is omitted until the boot surface lands (launcher slice 2): WPF
// shows it while LauncherHost.SurfaceInPlay; here the title-bar door (LauncherWindow.BackToLauncher) is the way.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Localization;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Set by App on the desktop path; null in renders and tests, where X closes.</summary>
        internal TrayIcon? Tray { get; private set; }

        /// <summary>Is there a tray host to hide behind? Re-probed on every X; tests inject it.</summary>
        internal Func<bool> TrayHostPresent { get; set; } = ProbeTrayHost;

        private bool _exitRequested;
        // WPF TrayIconService._windowClosed: Show() on a closed window throws.
        private bool _windowClosed;
        // WPF TrayIconService._hasShownFirstMinimizeNotification.
        private bool _shownFirstMinimizeNotification;

        internal TrayIcon CreateTray()
        {
            Tray = new TrayIcon
            {
                ToolTipText = "Conditioning Control Panel",
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/app.ico"))),
                Menu = BuildTrayMenu(),
                IsVisible = true,
            };
            // SNI Activate (left click) arrives as Clicked; WPF restores on single and double click.
            Tray.Clicked += (_, _) => ShowFromTray();
            Closed += (_, _) => _windowClosed = true;
            return Tray;
        }

        /// <summary>WPF order: Show, Wake, separator, Exit - with Stop everything above Exit.</summary>
        internal NativeMenu BuildTrayMenu()
        {
            var menu = new NativeMenu();
            menu.Add(Item("tray_show", ShowFromTray));
            // WPF reads the label once at tray creation (TrayIconService.cs Initialize);
            // App.Mods.IsBambiMode there is the active-mod check AppSettings.IsBambiMode makes here.
            menu.Add(Item(CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", WakeBambiUp));
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("tray_stop_everything", StopEverything));
            menu.Add(Item("tray_exit", RequestExit));
            return menu;
        }

        private static NativeMenuItem Item(string key, Action action) =>
            new(Loc.Get(key)) { Command = new CompanionRelayCommand(action) };

        /// <summary>The panic item: WPF StopEngine (a running session is paused, as the panic key does).
        /// Saved flags stay as the user set them.</summary>
        internal static void StopEverything()
        {
            Serilog.Log.Information("Tray: Stop everything");
            if (RefuseStopUnderLockdown()) return;   // WPF refuses every Stop under Lockdown (StartStop.cs:45)
            CancelPendingAi();
            try { CoreHaptics.Service?.PanicStop(); } catch (System.Exception ex) { Serilog.Log.Warning(ex, "Tray stop: haptics stop failed"); }
            Current?.StopAutonomyForPanic();
            StopEngine();
            StopCameraForPanic();   // decision C: the only panic control on Windows, so it closes the camera too
        }

        /// <summary>TrayIconService.ShowWindow + MainWindow's OnShowRequested (ShowAvatarTube).</summary>
        public void ShowFromTray()
        {
            if (_windowClosed) return;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            ShowAvatarTube();
        }

        /// <summary>MainWindow.Launcher.cs RequestExit: the one real exit; OnDesktopExit saves. Circe's bill
        /// shows only on the user's own exits - the tray's Exit and Settings' Exit button (both land
        /// here); double panic and the 18+ refusals come through <see cref="ExitWithoutBill"/>.</summary>
        public void RequestExit()
        {
            if (LockdownActive) { Serilog.Log.Information("Lockdown: Exit refused"); return; }   // WPF Launcher.cs:184
            if (CoreEngine.IsRunning) StopEngine();                  // WPF Launcher.cs:187
            // Circe's bill holds the exit for a few seconds, then calls back in here (Launcher.cs:188).
            if (TryShowExitBill(RequestExit)) return;
            _exitRequested = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }

        /// <summary>WPF OnClosing: X goes to the tray (WindowChrome.cs:337-349); only RequestExit, or
        /// a missing tray host, really closes.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            // WPF WindowChrome.cs:131: under Lockdown the user's close is refused (not even hidden),
            // after the Close tripwire. OS / application shutdown still goes through: the recovery
            // file puts the real panic key and Strict Lock back on the next start.
            if (LockdownActive && e.CloseReason == WindowCloseReason.WindowClosing && !_exitRequested)
            {
                try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                e.Cancel = true;
                base.OnClosing(e);
                return;
            }
            if (Tray is not null && !_exitRequested && e.CloseReason == WindowCloseReason.WindowClosing
                && TrayHostPresent())
            {
                e.Cancel = true;
                Hide();
                HideAvatarTube();
                if (!_shownFirstMinimizeNotification)
                {
                    _shownFirstMinimizeNotification = true;
                    Platform.OsNotifications.Show(Loc.Get("app_title"), Loc.Get("tray_balloon_body"), ShowFromTray);
                }
            }
            base.OnClosing(e);
        }

        /// <summary>Windows always has a tray; Linux has one only while a StatusNotifierWatcher owns its name.</summary>
        internal static bool ProbeTrayHost()
        {
            if (OperatingSystem.IsWindows()) return true;
            try
            {
                var probe = Task.Run(async () =>
                {
                    var bus = DBusConnection.Session;
                    await bus.ConnectAsync();
                    var w = bus.GetMessageWriter();
                    w.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                        @interface: "org.freedesktop.DBus", member: "NameHasOwner", signature: "s");
                    w.WriteString("org.kde.StatusNotifierWatcher");
                    return await bus.CallMethodAsync(w.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
                });
                return probe.Wait(TimeSpan.FromSeconds(1)) && probe.Result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Tray host probe failed; X will exit instead of hiding");
                return false;
            }
        }
    }
}
