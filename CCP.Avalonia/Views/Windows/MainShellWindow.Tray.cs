// PORTED from ConditioningControlPanel/Services/Notifications/TrayIconService.cs plus the tray
// half of MainWindow.WindowChrome.cs OnClosing (X -> tray) and MainWindow.Launcher.cs RequestExit.
//
// Avalonia's TrayIcon is StatusNotifierItem over D-Bus on Linux and a native icon on Windows.
// Divergences, on purpose:
//   - The icon stays visible while the window is up (WPF shows it only while hidden). On this head
//     the tray is the panic control a takeover needs without a global hotkey
//     (docs/avalonia-decisions.md, desktop overlays), so it must always be reachable.
//   - "Stop everything" is an extra item for that reason: the shell-close overlay stop, app stays up.
//   - Balloons: Avalonia has no OS-notification API, so the one-time minimize balloon goes to the
//     in-app toast (App.Notifications). ShowNotification's other WPF callers (scheduler, remote,
//     level-up) have no counterpart here yet.
// ponytail: "launcher_back_to_client" is omitted - WPF hides it unless LauncherHost.SurfaceInPlay,
// and this head has no launcher, so it is never visible. Add it with the launcher.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Set by App on the desktop path; null in renders and tests, where X closes.</summary>
        internal TrayIcon? Tray { get; private set; }
        private bool _exitRequested;
        private bool _hasShownFirstMinimizeNotification;

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
            menu.Add(Item("tray_stop_everything", App.StopDesktopOverlays));
            menu.Add(Item("tray_exit", RequestExit));
            return menu;
        }

        private static NativeMenuItem Item(string key, Action action) =>
            new(Loc.Get(key)) { Command = new CompanionRelayCommand(action) };

        /// <summary>TrayIconService.ShowWindow: show, un-minimize, bring forward.</summary>
        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>TrayIconService.MinimizeToTray.</summary>
        public void MinimizeToTray()
        {
            Hide();
            if (_hasShownFirstMinimizeNotification) return;
            _hasShownFirstMinimizeNotification = true;
            App.Notifications.Show(Loc.Get("tray_balloon_body"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(2));
        }

        /// <summary>MainWindow.Launcher.cs RequestExit: the one real exit; OnDesktopExit saves.</summary>
        public void RequestExit()
        {
            // ponytail: WPF also refuses under Lockdown, stops the engine and shows Circe's exit
            // bill; this head has none of those services yet.
            _exitRequested = true;
            Close();
        }

        /// <summary>WPF OnClosing: X always goes to the tray; only RequestExit really closes.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (Tray is not null && !_exitRequested && e.CloseReason == WindowCloseReason.WindowClosing)
            {
                e.Cancel = true;
                MinimizeToTray();
            }
            base.OnClosing(e);
        }
    }
}
