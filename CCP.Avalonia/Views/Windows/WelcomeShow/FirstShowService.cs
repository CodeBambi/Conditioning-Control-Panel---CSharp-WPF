using System;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.FirstShow;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowService.cs: owns one disposable
    /// show using the player's actual desktop EMI. Doors: the first-run wizard's far side
    /// (MainShellWindow.FirstRun), Help's "Watch Emi's demo" and the same button in EMI's book.
    ///
    /// <para>Two things WPF left to its callers are rules here: the show never opens over a running
    /// session, Lockdown or Strict Lock (<see cref="FirstShowScript.MayOpen"/>), and it refuses a platform
    /// where the stage cannot be made click-through (it would eat every click on the desktop).</para>
    ///
    /// <para>ponytail: WPF's DEBUG "--first-show-preview" launch (the show without the app) is not here.</para>
    /// </summary>
    internal static class FirstShowService
    {
        private static FirstShowStageWindow? _window;
        public static bool IsActive => _window != null;
        internal static FirstShowStageWindow? Window => _window;

        /// <summary>Test seams: what owns the player right now, and whether a stage that is not
        /// click-through is acceptable (headless has no native window to shape).</summary>
        internal static Func<bool> SessionRunning = () => CoreSession.IsSessionRunning;
        internal static Func<bool> LockdownActive = () => MainShellWindow.LockdownActive;
        internal static Func<bool> StrictLock = () => CoreSettings.Current?.StrictLockEnabled == true;
        internal static bool RequireClickThrough = true;

        internal static bool MayOpenNow() => FirstShowScript.MayOpen(SessionRunning(), LockdownActive(), StrictLock());

        /// <summary>While it runs: a session or Lockdown that starts ends the show. Strict Lock is a
        /// setting, so it only gates the door.</summary>
        internal static bool MayRun() => FirstShowScript.MayOpen(SessionRunning(), LockdownActive(), false);

        /// <summary>Open the show. False when it is refused or could not start.</summary>
        public static bool Open(MainShellWindow? owner = null)
        {
            if (_window != null) return true;
            if (!MayOpenNow())
            {
                Log.Information("[FirstShow] not opened: a session, Lockdown or Strict Lock owns the player");
                return false;
            }
            var desk = EmiDeskService.Instance.BeginPresentation();
            if (desk == null) return false;
            FirstShowStageWindow? window = null;
            try
            {
                window = new FirstShowStageWindow(desk, owner ?? MainShellWindow.Current);
                _window = window;
                window.Closing += (_, _) => desk.EndPresentation();
                window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
                // An unowned visible window would keep the app alive after the shell goes: stop with it.
                if ((owner ?? MainShellWindow.Current) is { } shell) shell.Closing += (_, _) => Stop();
                bool clickThrough = window.Launch();
                if (!clickThrough && RequireClickThrough)
                {
                    Log.Warning("[FirstShow] not opened: this desktop cannot make the stage click-through");
                    window.Close();
                    return false;
                }
                desk.BeginPresentation(() => Stop());
                window.BeginEntrance();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FirstShow] could not open");
                try { window?.Close(); } catch { /* already down */ }
                desk.EndPresentation(); _window = null;
                return false;
            }
        }

        /// <summary>Stop the show at once. Idempotent; true when one was running.</summary>
        public static bool Stop()
        {
            if (_window == null) return false;
            var window = _window; _window = null;
            try { window.Close(); } catch (Exception ex) { Log.Debug(ex, "[FirstShow] close failed"); }
            return true;
        }

        internal const string PanicSurfaceId = "first-show";

        /// <summary>Puts the show's stop in the panic registry at runtime, as TutorialHead.HookPanic and
        /// LeashTaskHost.HookPanic do: FIRST in the list (WPF HandlePanicKeyPress stops the show before
        /// anything else). Idempotent.</summary>
        internal static void HookPanic()
        {
            var all = PanicSurfaces.All.ToList();
            if (all.Any(s => s.Id == PanicSurfaceId)) return;
            all.Insert(0, new PanicSurfaces.Surface(PanicSurfaceId, _ => Stop()));
            PanicSurfaces.All = all.ToArray();
        }
    }
}
