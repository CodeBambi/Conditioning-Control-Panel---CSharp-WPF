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

        /// <summary>WPF order: Show, Back to CC Labs, Wake, Cut leash (leashed only), separator, Exit - with Stop everything above Exit.</summary>
        internal NativeMenu BuildTrayMenu()
        {
            var menu = new NativeMenu();
            TrayLabels.Clear();
            HookTrayRelabel();
            menu.Add(Item("tray_show", ShowFromTray));
            // WPF TrayIconService.cs:102: the way back to the launcher, only while it is part of this
            // run, greyed under Lockdown (BackToLauncher refuses anyway).
            var back = Item("launcher_back_to_client", () => LauncherWindow.BackToLauncher(this));
            void RefreshBack(object? s, EventArgs e) { back.IsVisible = LauncherWindow.SurfaceInPlay; back.IsEnabled = !LockdownActive; }
            RefreshBack(null, EventArgs.Empty);
            menu.Opening += RefreshBack;
            menu.NeedsUpdate += RefreshBack;
            menu.Add(back);
            // WPF reads the label once at tray creation (TrayIconService.cs Initialize);
            // App.Mods.IsBambiMode there is the active-mod check AppSettings.IsBambiMode makes here.
            menu.Add(Item(() => CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", WakeBambiUp));
            // WPF TrayIconService.cs:119-125: Cut leash, one click, only while someone holds this
            // account's leash. Never gated, never priced, never greyed (Platform/LeashHead.Cut).
            var cutLeash = Item("leash_cut", Platform.LeashHead.Cut);
            void RefreshCut(object? s, EventArgs e) => cutLeash.IsVisible = Platform.LeashHead.IsLeashed;
            RefreshCut(null, EventArgs.Empty);
            menu.Opening += RefreshCut;
            menu.NeedsUpdate += RefreshCut;
            menu.Add(cutLeash);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("tray_stop_everything", StopEverything));
            menu.Add(Item("tray_exit", RequestExit));
            return menu;
        }

        private static NativeMenuItem Item(string key, Action action) => Item(() => key, action);

        private static NativeMenuItem Item(Func<string> key, Action action)
        {
            var item = new NativeMenuItem(Loc.Get(key())) { Command = new CompanionRelayCommand(action) };
            TrayLabels.Add((item, key));
            return item;
        }

        // G17: WPF reads the tray labels once; here they follow a language switch and a mod switch
        // (the wake item's wording is the mod's).
        private static readonly System.Collections.Generic.List<(NativeMenuItem Item, Func<string> Key)> TrayLabels = new();
        private bool _trayRelabelHooked;

        private void HookTrayRelabel()
        {
            if (_trayRelabelHooked) return;
            _trayRelabelHooked = true;
            EventHandler onLanguage = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(RelabelTray);
            EventHandler<ConditioningControlPanel.Models.ModPackage> onMod = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(RelabelTray);
            LocalizationManager.Instance.LanguageChanged += onLanguage;
            CoreMods.ModChanged += onMod;
            Closed += (_, _) => { LocalizationManager.Instance.LanguageChanged -= onLanguage; CoreMods.ModChanged -= onMod; };
        }

        internal static void RelabelTray()
        {
            foreach (var (item, key) in TrayLabels)
            {
                try { item.Header = Loc.Get(key()); } catch { /* a label never breaks the tray */ }
            }
        }

        /// <summary>The tray's panic item (WPF's tray has none). Hard rule 6: never more permissive than the
        /// panic key, so it answers to the same rule the 6-blink stop does (Core BlinkStopGate): refused
        /// under Lockdown (with the WPF Stop message), with the panic key switched off and under Strict Lock.
        /// Cut leash, the item above it, is never gated. Saved flags stay as the user set them.</summary>
        internal static void StopEverything()
        {
            Serilog.Log.Information("Tray: Stop everything");
            if (RefuseStopUnderLockdown()) return;   // WPF refuses every Stop under Lockdown (StartStop.cs:45)
            var block = TrayStopBlock();
            if (block != ConditioningControlPanel.Services.Safety.BlinkStopGate.Block.None)
            {
                Serilog.Log.Information("Tray: Stop everything refused ({Reason})", block);
                return;
            }
            PanicSurfaces.StopAll("tray");   // the same stop pass as the key, camera included (decision C)
        }

        /// <summary>Why the tray stop is refused right now; None when the panic key would run too.</summary>
        internal static ConditioningControlPanel.Services.Safety.BlinkStopGate.Block TrayStopBlock()
        {
            var s = CoreSettings.Current;
            return ConditioningControlPanel.Services.Safety.BlinkStopGate.Check(
                blinkTrainerRunning: false,
                lockdownActive: LockdownActive,
                panicKeyEnabled: s.PanicKeyEnabled,
                strictLockEnabled: s.StrictLockEnabled);
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
            // WPF WindowChrome.cs:140: closing (real exit OR minimize-to-tray) always ends a Chaos run.
            try { Views.Chaos.ChaosRunHost.ForceShutdown(); } catch { }
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
            // WPF WindowChrome.cs:180: a confirmed library delete still in its undo grace is honoured.
            if (!e.Cancel) try { CommitPendingDeeperDeletesOnExit(); } catch { }
            // WPF WindowChrome.cs:176: the Companion drawers' state, when quitting from that tab.
            if (!e.Cancel) try { PersistCompanionDrawerStatesOnExit(); } catch { }
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
