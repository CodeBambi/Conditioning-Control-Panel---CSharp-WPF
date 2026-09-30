// PORTED from ConditioningControlPanel/MainWindow/MainWindow.xaml.cs: OnGlobalKeyPressed's panic
// branch (:955), HandlePanicKeyPress (:1458) and RunPanicStopTail (:1694), engine-not-running path.
// The decision is the same Core PanicPolicy; the listener is Platform/X11PanicKey (XInput2 raw keys,
// non-consuming like the WH_KEYBOARD_LL hook - docs/avalonia-decisions.md, panic key row).
// ponytail: no game surfaces, video grace pause or bark on this head yet, so the
// stop pass is StopEngine (which pauses a running session first) plus the lock card; wire each as its surface arrives. The #919b off-thread watchdog is omitted: the listener is its own thread,
// so a wedged UI thread cannot drop the hook, but the queued stop still waits for the UI thread.
// ponytail: Windows has no panic listener on this head yet (WPF's WH_KEYBOARD_LL hook is not ported);
// X11PanicKey.Start returns false there and the tray's Stop everything is the only panic control.
// ponytail: a rebind while a portal session is open keeps the OLD trigger until the effects stop and
// the next effect re-binds; re-bind on PanicKey change if that matters.

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF _isCapturingPanicKey: Settings/Devices is waiting for the key to bind, so a
        /// press is a rebind, never a panic.</summary>
        internal static bool CapturingPanicKey { get; set; }

        private int _panicPressCount;
        private DateTime _lastPanicTime = DateTime.MinValue;

        /// <summary>App calls this once on the desktop path (never in tests or renders, so only the
        /// real app ever talks to the portal). False when no global listener is up.</summary>
        internal bool StartPanicKey()
        {
            _portalAllowed = true;
            return X11PanicKey.Start(() => CoreSettings.Current.PanicKey,
                () =>
                {
                    Serilog.Log.Information("Panic trigger: XInput2 key press");
                    Dispatcher.UIThread.Post(() => HandlePanicKeyPress(DateTime.Now));
                });
        }

        /// <summary>One press of the configured key (UI thread). Each press stops everything; a second
        /// counted press within 2 s exits the app, as WPF does when no engine is running.</summary>
        internal void HandlePanicKeyPress(DateTime now)
        {
            var s = CoreSettings.Current;
            if (CapturingPanicKey || !s.PanicKeyEnabled) return;

            // Same evaluation order as WPF: asking the palette closes it, so never ask with a card up.
            bool lockCardOpen = LockCardWindow.IsAnyOpen();
            bool paletteClaimed = !lockCardOpen
                && string.Equals(s.PanicKey, "Escape", StringComparison.OrdinalIgnoreCase)
                && SettingsPaletteWindow.TryConsumeEscape();
            var rung = PanicPolicy.Decide(lockCardOpen, paletteClaimed, PanicPolicy.OverrideEnabled(s));
            Serilog.Log.Information("Panic key pressed ({Rung})", rung);
            if (rung == PanicPolicy.Rung.DismissLockCard) StopLockCards();
            if (!PanicPolicy.StopsSurfaces(rung)) return;
            // WPF #735: with PanicOverridesAll off (RunLadder), the first press over a playing mandatory
            // video grace-pauses it instead (TryGracePause refuses when panic overrides all), and the
            // window's own handling of the same keystroke is deduped there. Not a ladder rung.
            if (rung == PanicPolicy.Rung.RunLadder && Views.Overlays.MandatoryVideoOverlay.Instance.TryGracePause(fromPanicKey: true))
            {
                Serilog.Log.Information("Panic press consumed as video grace pause");
                return;
            }
            bool wasRunning = CoreEngine.IsRunning;
            // WPF RunPanicStopTail: StopEngine while running, StopAdHocEffects otherwise - both are
            // CoreEngine.Stop here (it stops everything either way) and neither unticks a flag.
            // WPF PanicStopEverySurface (MainWindow.xaml.cs:1992): the toys go to zero first, bypassing
            // throttles and gates, whatever else the stop pass does.
            try { CoreHaptics.Service?.PanicStop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: haptics stop failed"); }
            StopEngine();
            StopLockCards();   // WPF StopAdHocEffects: App.LockCard.Stop(dismissOpenCards: true)
            if (wasRunning) ShowFromTray();   // WPF: Show + Activate the main window after a running stop

            if ((now - _lastPanicTime).TotalMilliseconds > 2000) _panicPressCount = 0;
            if (PanicPolicy.AdvancesExitLadder(rung)) { _panicPressCount++; _lastPanicTime = now; }
            if (_panicPressCount >= 2)
            {
                Serilog.Log.Information("Double panic! Exiting application...");
                RequestExit();
            }
        }

        // ---- Native Wayland windows: the GlobalShortcuts portal, bound only while effects run ----
        // Policy C (docs/avalonia-decisions.md, panic key row): the portal grab CONSUMES the key
        // desktop-wide, so it exists only while a desktop effect is on screen. The first effect
        // waits (<= 30 s, a KDE dialog) for the bind; refused/missing/timeout = XI2 only for this
        // run, one notice, and the effect starts anyway.
        private static Task? _portalBind;
        private static bool _portalFailed, _portalAllowed;
        private static DispatcherTimer? _portalWatch;

        private static bool AnyEffectRunning =>
            CoreEngine.IsRunning || CoreFlash.IsRunning || CoreSubliminal.IsRunning || BouncingTextOverlay.IsRunning;

        /// <summary>Every desktop-effect start goes through here. <paramref name="start"/> must re-check
        /// that the effect is still wanted: it can run up to 30 s later, after a panic or an untick.</summary>
        internal static void StartEffect(Action start)
        {
            if (!_portalAllowed || PortalPanicShortcut.IsBound || _portalFailed || !CoreSettings.Current.PanicKeyEnabled
                || !OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            {
                start();
                return;
            }
            _portalBind ??= BindPortalAsync();
            _portalBind.ContinueWith(_ => Dispatcher.UIThread.Post(start), TaskScheduler.Default);
        }

        private static async Task BindPortalAsync()
        {
            var trigger = await PortalPanicShortcut.BindAsync(CoreSettings.Current.PanicKey, Loc.Get("panic_portal_description"),
                () =>
                {
                    Serilog.Log.Information("Panic trigger: GlobalShortcuts portal Activated");
                    Dispatcher.UIThread.Post(() =>
                    ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow)
                        ?.HandlePanicKeyPress(DateTime.Now));
                });
            Dispatcher.UIThread.Post(() =>
            {
                if (trigger == null)
                {
                    _portalFailed = true;
                    App.Notifications.Show(Loc.Get("panic_portal_unavailable"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
                    return;
                }
                // trigger_description: what the compositor really bound, which the user may have changed.
                // Empty = the compositor assigned no key, so never claim the configured one.
                if (trigger.Length > 0)
                    App.Notifications.Show(Loc.GetF("panic_portal_bound", trigger), Helpers.NotificationType.Info, TimeSpan.FromSeconds(6));
                else
                    App.Notifications.Show(Loc.Get("panic_portal_unavailable"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
                // ponytail: 2 s poll for "last effect stopped" rather than a hook in every stop path.
                _portalWatch ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
                {
                    if (AnyEffectRunning) return;
                    _portalWatch!.Stop();
                    _portalWatch = null;
                    _portalBind = null;
                    _ = PortalPanicShortcut.UnbindAsync();
                });
                _portalWatch.Start();
            });
        }

        /// <summary>WPF LockCardService.Stop(dismissOpenCards: true).</summary>
        private static void StopLockCards()
        {
            LockCardWindow.ForceCloseAll();
            LockCardScheduler.Instance.Stop();
        }
    }
}
