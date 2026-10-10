using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy and the rest of the desk (WPF 7.1.5 ArcademyHostService.Launch :186-325, DisposeAll
    /// :6064): the control panel is tucked away while the Arcademy owns the screen and comes back on
    /// EVERY close path (title-bar X, page exit, boot error, the watchdog, panic: they all reach Closed);
    /// the desk mascot counts the open, says goodbye and winks off (she does not follow you to school);
    /// a browser video takeover freezes the class like a mandatory video does.
    ///
    /// <para>Two EMIs: nothing here tells the desktop companion the name the campus EMI keeps.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        // ---- seams (tests swap them; the defaults talk to the live shell and the desk) ------------

        /// <summary>Tuck the panel away. Returns what was done: "tray", "minimized" or null (nothing to undo).</summary>
        internal static Func<string?> ArcShellTuck = DefaultArcShellTuck;
        /// <summary>Undo <see cref="ArcShellTuck"/>; the argument is what it returned.</summary>
        internal static Action<string> ArcShellRestore = DefaultArcShellRestore;
        internal static Func<bool> ArcEmiIsOut = DefaultArcEmiIsOut;
        internal static Action ArcEmiDismiss = DefaultArcEmiDismiss;

        /// <summary>WPF EmiDeskService.ArcademyByeMoment / ArcademyByeDismissMs (her line, then the wink off).</summary>
        internal const string ArcademyByeMoment = "arcademyBye";
        internal const int ArcademyByeDismissMs = 1360 + 1600;

        private string? _arcShellTucked;
        private static WindowState _arcShellStateBefore = WindowState.Normal;
        private DispatcherTimer? _arcFarewellTimer;
        private bool _arcFarewellClaimed, _arcBrowserVideoHooked, _arcBrowserVideoSuspended;

        /// <summary>True when her goodbye claimed this open: <c>arcademyOpened</c> must not talk over it.</summary>
        internal bool ArcademyFarewellClaimed => _arcFarewellClaimed;
        internal string? ArcademyShellTucked => _arcShellTucked;
        internal bool ArcademyFarewellPending => _arcFarewellTimer != null;

        internal static bool DefaultArcEmiIsOut() => Windows.EmiDesk.EmiDeskService.Instance.IsOut;
        internal static void DefaultArcEmiDismiss() => Windows.EmiDesk.EmiDeskService.Instance.Dismiss();

        // ---- the panel ---------------------------------------------------------------------------

        internal static string? DefaultArcShellTuck()
        {
            var shell = Windows.MainShellWindow.Current;
            // The user's last word on their own window stands: a hidden or minimized panel is left alone.
            if (shell == null || !shell.IsVisible || shell.WindowState == WindowState.Minimized) return null;
            if (shell.MinimizeToTrayForChaos()) return "tray";
            // No tray on this desktop: a plain minimize, so the panel is never left unreachable.
            _arcShellStateBefore = shell.WindowState;
            shell.WindowState = WindowState.Minimized;
            return "minimized";
        }

        internal static void DefaultArcShellRestore(string how)
        {
            var shell = Windows.MainShellWindow.Current;
            if (shell == null) return;
            if (how == "tray") { if (!shell.IsVisible) shell.ShowFromTray(); return; }
            if (!shell.IsVisible || shell.WindowState != WindowState.Minimized) return;   // the user got there first
            shell.WindowState = _arcShellStateBefore == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            shell.Activate();
        }

        private void TuckShellForArcademy()
        {
            if (_arcShellTucked != null) return;
            try
            {
                _arcShellTucked = ArcShellTuck();
                if (_arcShellTucked != null) { try { Activate(); Web.Focus(); } catch { } }
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy: tuck the panel failed: {E}", ex.Message); }
        }

        /// <summary>Every close path funnels through here so the panel can never stay away.</summary>
        private void RestoreShellAfterArcademy()
        {
            var how = _arcShellTucked;
            _arcShellTucked = null;
            if (how == null) return;
            try { ArcShellRestore(how); }
            catch (Exception ex) { Log.Warning("[Game] arcademy: bringing the panel back failed: {E}", ex.Message); }
        }

        // ---- the desk mascot ---------------------------------------------------------------------

        /// <summary>WPF NoteOpen("arcademy") then FarewellForArcademy. The ring learns from every open; if
        /// she is out she says goodbye and winks herself off a couple of seconds later. A no-op while she
        /// is away. The goodbye goes through the bus; the bye line claims this open, so
        /// <c>arcademyOpened</c> is not fired over it (GameWindow.Emi.cs).</summary>
        private void EmiArcademyOpen()
        {
            try { EmiState.NoteUsage("arcademy"); }
            catch (Exception ex) { Log.Debug("[Game] arcademy: emi usage note failed: {E}", ex.Message); }
            try
            {
                if (!ArcEmiIsOut()) return;
                _arcFarewellClaimed = true;
                EmiDeskBus.Fire(ArcademyByeMoment);
                StopArcademyFarewell(dismissNow: false);
                _arcFarewellTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ArcademyByeDismissMs) };
                _arcFarewellTimer.Tick += (_, _) => StopArcademyFarewell(dismissNow: true);
                _arcFarewellTimer.Start();
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy: emi farewell failed: {E}", ex.Message); }
        }

        /// <summary>The wink off. Also runs on close with the timer still pending: she was leaving anyway,
        /// and a timer never outlives its window.</summary>
        internal void StopArcademyFarewell(bool dismissNow)
        {
            var t = _arcFarewellTimer;
            _arcFarewellTimer = null;
            if (t == null) return;
            try { t.Stop(); } catch { }
            if (!dismissNow) return;
            try { if (ArcEmiIsOut()) ArcEmiDismiss(); }
            catch (Exception ex) { Log.Debug("[Game] arcademy: emi farewell dismiss failed: {E}", ex.Message); }
        }

        // ---- browser video takeover --------------------------------------------------------------

        /// <summary>WPF HookBrowserVideoEvents: <c>init.protectBrowserVideo</c> is a real promise. The
        /// signal is <see cref="Platform.BrowserVideoSignal"/>; whatever plays a web video on this head
        /// raises it.</summary>
        private void HookArcademyBrowserVideo(bool on)
        {
            if (on && !_arcBrowserVideoHooked) { Platform.BrowserVideoSignal.PlayingChanged += OnArcademyBrowserVideo; _arcBrowserVideoHooked = true; }
            else if (!on && _arcBrowserVideoHooked) { Platform.BrowserVideoSignal.PlayingChanged -= OnArcademyBrowserVideo; _arcBrowserVideoHooked = false; }
            if (!on) _arcBrowserVideoSuspended = false;
        }

        private void OnArcademyBrowserVideo(bool playing)
        {
            if (Dispatcher.UIThread.CheckAccess()) ArcademyBrowserVideoChanged(playing);
            else Dispatcher.UIThread.Post(() => ArcademyBrowserVideoChanged(playing));
        }

        /// <summary>WPF OnBrowserVideoPlayingChanged. The preference is read live. The un-freeze never
        /// lifts a panic suspend, a mandatory video or an audio-only session.</summary>
        internal void ArcademyBrowserVideoChanged(bool playing)
        {
            if (IsClosedOrClosing) return;
            if (playing)
            {
                if (CoreSettings.Current?.ProtectBrowserVideoPlayback != true) return;
                _arcBrowserVideoSuspended = true;
                PostArcademySuspend(true, "video");
                return;
            }
            // Only what this path froze is un-frozen here, whatever the preference says by now.
            if (!_arcBrowserVideoSuspended) return;
            _arcBrowserVideoSuspended = false;
            if (_arcPanicSuspended || _arcVideoSuspended || CoreEngine.Video?.IsPlaying == true
                || CoreSettings.Current?.AudioOnlySession == true) return;
            PostArcademySuspend(false, "video");
        }
    }
}
