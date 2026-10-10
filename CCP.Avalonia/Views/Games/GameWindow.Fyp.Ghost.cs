using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// For You ghost mode (WPF 7.1.5 FypHostService "window mechanics: GHOST MODE"): the page's
    /// <c>clickThrough</c> setting parks the real feed window and shows a see-through, click-through
    /// live mirror of it over whatever the player is doing. Audio, blink and gaze keep driving the
    /// real page; the mirror only shows the result.
    ///
    /// <para>The native half is behind <see cref="IFypGhostPlatform"/> (Windows:
    /// <see cref="WindowsFypGhost"/>; Linux: unavailable, a documented exception). This file owns
    /// the state and the frames, exactly WPF's: nothing on a successful enter (the page already
    /// flipped its toggle), <c>clickThrough:false</c> on every exit, and <c>clickThrough:false</c>
    /// then <c>ghost-unavailable {reason}</c> when the mirror cannot be made to compose.</para>
    ///
    /// <para>Session only: ghost mode always starts off, so a relaunch can never come up as a pane
    /// nobody can grab. SAFETY: the mirror dies with its window (panic closes every game window, so
    /// the feed and its mirror go on the same press), and with a minimise, a hide, a page re-init,
    /// the gaze calibration and a failed health probe. A platform failure is always
    /// "unavailable", never an opaque topmost window.</para>
    ///
    /// <para>Differences from WPF, on purpose (coordinator brief, 10 Oct 2026): panic closes the
    /// feed and the mirror at once (WPF: press 1 drops the ghost, press 2 closes), and a minimise of
    /// the parked window drops the ghost (WPF vetoes SC_MINIMIZE and heals).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>The platform seam. Unseeded (every headless test, Linux): unavailable. Program
        /// seeds the Windows one at startup. Process-wide: tests swap it in a RunsAlone collection.</summary>
        internal static IFypGhostPlatform FypGhostPlatform = FypGhostUnavailable.Instance;

        /// <summary>WPF's watchdog period: the parked window's state is probed every few seconds.</summary>
        internal static readonly TimeSpan FypGhostWatchPeriod = TimeSpan.FromSeconds(5);

        private IFypGhostSession? _fypGhost;
        private WindowState _fypGhostPrevState = WindowState.Normal;
        private bool _fypGhostHooked, _fypGhostChanging;
        private DispatcherTimer? _fypGhostWatch;

        internal bool IsFypGhosted => _fypGhost != null;

        /// <summary>WPF FypHostService.IsGhosted: is the open feed parked behind its mirror?</summary>
        internal static bool IsFypGhostedAny()
        {
            lock (Open) return Open.Any(w => w.Spec.Id == FypId && w._fypGhost != null);
        }

        /// <summary>Drop every live ghost (the feed stays open, solid). For callers that suspend
        /// the desktop under it; panic does not need this, it closes the window.</summary>
        internal static void DropFypGhosts()
        {
            GameWindow[] all;
            lock (Open) all = Open.Where(w => w.Spec.Id == FypId).ToArray();
            foreach (var w in all) w.ExitFypGhost();
        }

        /// <summary>WPF EnterGhost. Idempotent.</summary>
        private void EnterFypGhost()
        {
            if (_fypGhost != null || IsClosedOrClosing) return;
            HookFypGhost();
            var s = CoreSettings.Current;
            var prev = WindowState;
            IFypGhostSession? session = null;
            string? reason = null;
            try
            {
                // A maximised or fullscreen window has no rect worth restoring: normalise first and
                // remember to put it back (WPF _ghostWasMaximized).
                _fypGhostChanging = true;
                try { if (prev != WindowState.Normal) WindowState = WindowState.Normal; }
                finally { _fypGhostChanging = false; }
                var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                session = FypGhostPlatform.Enter(hwnd, s.FypWindowOpacity, s.FypMuted, FypGhostGearClicked, FypGhostMuteClicked, out reason);
            }
            catch (Exception ex)
            {
                Log.Warning("[Game] fyp: ghost enter failed: {E}", ex.Message);
                try { session?.Dispose(); } catch { }
                session = null;
                reason = "mirror-threw";
            }
            if (session == null)
            {
                // Could not compose: the feed stays solid and the page says so. Never a sheet.
                RestoreFypGhostState(prev);
                Log.Warning("[Game] fyp: ghost mode unavailable ({Why}), staying solid", reason ?? "unknown");
                Post(new { type = "clickThrough", on = false });
                Post(new { type = "ghost-unavailable", reason = reason ?? "unknown" });
                return;
            }
            _fypGhost = session;
            _fypGhostPrevState = prev;
            StartFypGhostWatch();
            Log.Information("[Game] fyp: ghost mode ON");
        }

        /// <summary>WPF ExitGhost: the mirror goes, the real window comes back, the page hears
        /// <c>clickThrough:false</c>. Idempotent.</summary>
        internal void ExitFypGhost()
        {
            if (!DropFypGhostMirror()) return;
            if (IsClosedOrClosing) return;
            RestoreFypGhostState(_fypGhostPrevState);
            Post(new { type = "clickThrough", on = false });
            Log.Information("[Game] fyp: ghost mode off");
        }

        /// <summary>The native half only: stop the watchdog, dispose the session. True when a
        /// ghost was live. Safe on a closing window.</summary>
        private bool DropFypGhostMirror()
        {
            var g = _fypGhost;
            _fypGhost = null;
            try { _fypGhostWatch?.Stop(); } catch { }
            _fypGhostWatch = null;
            if (g == null) return false;
            try { g.Dispose(); }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost close: {E}", ex.Message); }
            return true;
        }

        private void RestoreFypGhostState(WindowState state)
        {
            if (IsClosedOrClosing) return;
            _fypGhostChanging = true;
            try
            {
                // A minimise can have slipped through while parked: un-minimise first, or the
                // restored rect lands on a window nobody can see.
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                if (state != WindowState.Normal && state != WindowState.Minimized && WindowState != state) WindowState = state;
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost state restore: {E}", ex.Message); }
            finally { _fypGhostChanging = false; }
        }

        private void HookFypGhost()
        {
            if (_fypGhostHooked) return;
            _fypGhostHooked = true;
            // The mirror must not outlive its source: panic, the page's close, Alt+F4, the shell
            // shutting down all come through here, before the window is gone.
            Closing += (_, _) => DropFypGhostMirror();
            Closed += (_, _) => DropFypGhostMirror();
            PropertyChanged += OnFypGhostWindowChanged;
        }

        private void OnFypGhostWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_fypGhost == null || _fypGhostChanging) return;
            // A minimised or hidden source stops being composed and the mirror would freeze on its
            // last frame over the desktop: drop it instead.
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized) ExitFypGhostKeepingMinimised();
            else if (e.Property == IsVisibleProperty && !IsVisible) ExitFypGhost();
        }

        private void ExitFypGhostKeepingMinimised()
        {
            if (!DropFypGhostMirror()) return;
            Post(new { type = "clickThrough", on = false });
            Log.Information("[Game] fyp: ghost mode off (the feed was minimised)");
        }

        private void StartFypGhostWatch()
        {
            try { _fypGhostWatch?.Stop(); } catch { }
            _fypGhostWatch = new DispatcherTimer { Interval = FypGhostWatchPeriod };
            _fypGhostWatch.Tick += (_, _) => FypGhostWatchTick();
            _fypGhostWatch.Start();
        }

        /// <summary>WPF's diag tick: a mirror that stopped being see-through or click-through, or
        /// whose source stopped composing, comes down and the page is told why.</summary>
        internal void FypGhostWatchTick()
        {
            var g = _fypGhost;
            if (g == null) return;
            bool ok;
            try { ok = g.Healthy(); }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost probe: {E}", ex.Message); ok = true; }
            if (ok) return;
            Log.Warning("[Game] fyp: ghost mirror stopped composing, dropping ghost mode");
            ExitFypGhost();
            if (!IsClosedOrClosing) Post(new { type = "ghost-unavailable", reason = "colorkey-lost" });
        }

        /// <summary>WPF GhostGearClicked: give the window back and open the options in one click.</summary>
        private void FypGhostGearClicked()
        {
            try
            {
                if (IsClosedOrClosing) return;
                ExitFypGhost();
                Activate();
                Post(new { type = "openOptions" });
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost gear failed: {E}", ex.Message); }
        }

        /// <summary>WPF GhostMuteClicked: flip mute WITHOUT leaving ghost mode (the parked window
        /// keeps playing audio, so this is the one control worth reaching).</summary>
        private void FypGhostMuteClicked()
        {
            try
            {
                if (IsClosedOrClosing) return;
                var s = CoreSettings.Current;
                s.FypMuted = !s.FypMuted;
                Post(new { type = "setMuted", on = s.FypMuted });
                _fypGhost?.SetMuted(s.FypMuted);
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost mute failed: {E}", ex.Message); }
        }
    }
}
