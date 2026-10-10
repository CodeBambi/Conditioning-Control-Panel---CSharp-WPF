using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The Arcademy host, the safety half (WPF 7.1.5 ArcademyHostService.cs): the panic ladder
    /// (HandlePanicPress :415, OnResumeRequest :443), <c>suspend</c> for a mandatory video
    /// (:5612-5700), the graceful host close (<c>end-run</c> + the 1200 ms exit watchdog, CloseActive
    /// :348 / :6002) and the boot deadline (:5895).
    ///
    /// <para>THE LADDER. Press 1 freezes everything: <c>suspend {reason: panic}</c> drops every effect,
    /// pauses the class and shows the Resume card; only this host may un-freeze (the page asks with
    /// <c>resume-request</c>). Press 2 inside two seconds closes the Arcademy. A slower second press is
    /// a fresh press 1. It is never weaker than a plain close where a close is what protects the
    /// player: a page that is not ready, not beating, or already asked to leave is closed at once, and
    /// a suspended page that then goes silent is closed by the heartbeat watchdog.</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>WPF PanicDoublePressWindow.</summary>
        internal static readonly TimeSpan ArcPanicDoublePressWindow = TimeSpan.FromSeconds(2);
        /// <summary>WPF BootDeadline: a page that reports nothing for this long failed silently.</summary>
        internal static readonly TimeSpan ArcBootDeadline = TimeSpan.FromSeconds(45);
        internal static readonly TimeSpan ArcExitWatchdog = TimeSpan.FromMilliseconds(1200);

        /// <summary>One switch for the whole ladder: false = every panic press closes the Arcademy at
        /// once (the port's behaviour before the ladder was ported).</summary>
        internal static bool ArcademyPanicLadder = true;

        /// <summary>WPF BootFailedThisSession: the door's own "did not start" memory for this run of the app.</summary>
        internal static bool ArcademyBootFailedThisSession { get; private set; }

        private bool _arcPanicSuspended, _arcExiting, _arcVideoSuspended, _arcVideoHooked;
        private DateTime _arcLastPanicPressUtc = DateTime.MinValue;
        private DateTime _arcLastProgressUtc = DateTime.UtcNow;
        private DispatcherTimer? _arcExitWatchdog, _arcBootWatch, _arcVideoWatch;

        internal bool ArcademyPanicSuspended => _arcPanicSuspended;
        internal bool ArcademyExiting => _arcExiting;

        // ---- open / close -----------------------------------------------------------------------

        /// <summary>Window opened: arm the boot deadline and the video watch (WPF Launch).</summary>
        /// <summary>WPF Launch step 4 (owner ruling): an audio-only day SKIPS the Arcademy rather than substituting
        /// classes. A toast, not a modal: the click was a launch. The attendance streak is frozen, not broken.
        /// Runs after the tier gate, as WPF orders them.</summary>
        internal static bool ArcademyLaunchAllowed()
        {
            if (CoreSettings.Current?.AudioOnlySession != true) return true;
            Log.Information("[Game] arcademy: launch refused - AudioOnlySession is active");
            try
            {
                Platform.OsNotifications.Show(ConditioningControlPanel.Services.Arcademy.ArcademyHostService.ProductName,
                    "Audio-only session is running - the Arcademy stays shut until it ends. Your attendance streak is safe.");
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy: audio-only refusal toast failed: {E}", ex.Message); }
            return false;
        }

        private void OpenArcademy()
        {
            // WPF Launch: friends see "in the Arcademy" while the window is up.
            try { Platform.FriendsHead.Service?.EnterActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Arcademy); }
            catch (Exception ex) { Log.Debug("[Game] arcademy friends presence: {E}", ex.Message); }
            // SEAM(emi-desk): WPF also tells the desk mascot (NoteOpen, FarewellForArcademy, Fire arcademyOpened / arcademyClosed);
            // the port's desk has no event bus yet. SEAM(shell): WPF tucks the main window into the tray while the Arcademy is up.
            ArmArcademyBootDeadline();
            HookArcademyVideo(true);
        }

        private void CloseArcademySafety()
        {
            try { Platform.FriendsHead.Service?.LeaveActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Arcademy); }
            catch (Exception ex) { Log.Debug("[Game] arcademy friends presence: {E}", ex.Message); }
            CancelArcademyExitWatchdog();
            CancelArcademyBootDeadline();
            HookArcademyVideo(false);
            StopArcademyVideoWatch();
            _arcPanicSuspended = false;
            _arcVideoSuspended = false;
            _arcLastPanicPressUtc = DateTime.MinValue;
            _arcExiting = false;
        }

        // ---- the panic ladder -------------------------------------------------------------------

        /// <summary>One panic press for this window. True = the press was taken as a suspend and the
        /// window stays up, frozen; false = the caller closes the window now.</summary>
        internal bool ArcademyPanicPress(DateTime nowUtc)
        {
            if (!ArcademyPanicLadder) return false;
            // A page that cannot be told to freeze is closed, never left running.
            if (!IsReady || !_beating || _arcExiting || IsClosedOrClosing) return false;
            double limit = InRun ? RunSilenceLimitSeconds(Spec.Id) : HubSilenceLimitSeconds;
            if ((nowUtc - _lastHeartbeatUtc).TotalSeconds > limit) return false;

            bool doubleTap = _arcPanicSuspended && (nowUtc - _arcLastPanicPressUtc) <= ArcPanicDoublePressWindow;
            _arcLastPanicPressUtc = nowUtc;
            if (doubleTap)
            {
                Log.Information("[Game] arcademy: panic press 2 - closing the Arcademy");
                _arcPanicSuspended = false;
                // The stop gesture never waits on the page: suspend is already standing, so close now.
                return false;
            }

            _arcPanicSuspended = true;
            // An open Discord link-up is part of what the emergency stop stops.
            CancelArcademyLink("panic", tellPage: true);
            Log.Information("[Game] arcademy: panic press 1 - suspending{Mid} (press again to leave)", _arcClassActive ? " mid-class" : "");
            PostArcademySuspend(true, "panic");
            return true;
        }

        private void PostArcademySuspend(bool on, string reason)
        {
            try { Post(new { type = "suspend", on, reason }); }
            catch (Exception ex) { Log.Debug("[Game] arcademy suspend: {E}", ex.Message); }
        }

        /// <summary>The page asking to come back from a PANIC suspend, the only suspend with no natural
        /// end. The host stays the only thing that may un-freeze a class.</summary>
        private void OnArcademyResumeRequest(JObject o)
        {
            var reason = ((string?)o["reason"] ?? "panic").Trim();
            if (reason != "panic")
            {
                Log.Debug("[Game] arcademy: resume-request for '{Reason}' refused - only panic resumes on request", reason);
                return;
            }
            if (!_arcPanicSuspended)
            {
                Log.Debug("[Game] arcademy: resume-request with no panic suspend outstanding - ignored");
                return;
            }
            // A mandatory video or an audio-only session outranks the panic resume.
            if (CoreEngine.Video?.IsPlaying == true || CoreSettings.Current?.AudioOnlySession == true)
            {
                Log.Information("[Game] arcademy: resume-request held - a video / audio-only session still owns the screen");
                return;
            }
            _arcPanicSuspended = false;
            _arcLastPanicPressUtc = DateTime.MinValue;   // the ladder re-arms at rung 1
            Log.Information("[Game] arcademy: panic resume granted");
            PostArcademySuspend(false, "panic");
        }

        // ---- graceful host close (end-run) ------------------------------------------------------

        /// <summary>WPF CloseActive: ask the page to wind down (it banks nothing, the class simply never
        /// ended), force the close after 1200 ms. Idempotent. Not the panic path: panic closes at once.</summary>
        internal void ArcademyCloseActive()
        {
            try
            {
                if (IsClosedOrClosing) return;
                if (IsReady && !_arcExiting)
                {
                    _arcExiting = true;
                    Post(new { type = "end-run", reason = "host" });
                    ArmArcademyExitWatchdog();
                }
                else if (!_arcExiting) Close();
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy close: {E}", ex.Message); Close(); }
        }

        /// <summary>The page said <c>exit</c> (its own leave): WPF arms the same watchdog and waits for exit-done.</summary>
        private void OnArcademyPageExit()
        {
            if (_arcExiting) return;
            _arcExiting = true;
            ArmArcademyExitWatchdog();
        }

        private void ArmArcademyExitWatchdog()
        {
            CancelArcademyExitWatchdog();
            _arcExitWatchdog = new DispatcherTimer { Interval = ArcExitWatchdog };
            _arcExitWatchdog.Tick += (_, _) => { CancelArcademyExitWatchdog(); if (!IsClosedOrClosing) Close(); };
            _arcExitWatchdog.Start();
        }

        private void CancelArcademyExitWatchdog()
        {
            try { _arcExitWatchdog?.Stop(); } catch { }
            _arcExitWatchdog = null;
        }

        // ---- boot deadline ----------------------------------------------------------------------

        private void ArmArcademyBootDeadline()
        {
            CancelArcademyBootDeadline();
            _arcLastProgressUtc = DateTime.UtcNow;
            _arcBootWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _arcBootWatch.Tick += (_, _) => CheckArcademyBootDeadline(DateTime.UtcNow);
            _arcBootWatch.Start();
        }

        private void CancelArcademyBootDeadline()
        {
            try { _arcBootWatch?.Stop(); } catch { }
            _arcBootWatch = null;
        }

        /// <summary>One boot-deadline tick; returns what it did, for tests.</summary>
        internal string CheckArcademyBootDeadline(DateTime nowUtc)
        {
            if (IsReady || _arcExiting || IsClosedOrClosing) { CancelArcademyBootDeadline(); return "done"; }
            if (nowUtc - _arcLastProgressUtc < ArcBootDeadline) return "waiting";
            CancelArcademyBootDeadline();
            OnArcademyBootError($"boot deadline: no progress for {ArcBootDeadline.TotalSeconds:0}s");
            return "failed";
        }

        /// <summary>The page's boot failed (or never started). Tear down and SAY so: a black window the
        /// user has to guess about is the worse failure.</summary>
        private void OnArcademyBootError(string? msg)
        {
            Log.Warning("[Game] arcademy: boot-error: {Msg}", msg);
            ArcademyBootFailedThisSession = true;
            if (!IsClosedOrClosing) Close();
            try
            {
                var owner = global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                    ? d.MainWindow : null;
                if (owner != null && owner.IsVisible)
                    _ = MessageDialog.ShowAsync(owner, Loc.Get(Spec.TitleKey), Loc.GetF("arcademy_boot_error_body", Loc.Get(Spec.TitleKey), msg ?? string.Empty));
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy boot-error dialog: {E}", ex.Message); }
        }

        // ---- mandatory video --------------------------------------------------------------------

        /// <summary>A mandatory video fully covers the class: the page drops every effect and pauses.
        /// The port's scheduler raises the start only, so the end is watched (1 s) while the suspend stands.
        /// SEAM(browser-video): WPF also suspends for a browser video takeover (BrowserMediaService
        /// PlayingChanged under ProtectBrowserVideoPlayback); the port has no browser media service.</summary>
        private void HookArcademyVideo(bool on)
        {
            try
            {
                var video = CoreEngine.Video;
                if (on && !_arcVideoHooked && video != null) { video.VideoStarted += OnArcademyVideoStarted; _arcVideoHooked = true; }
                else if (!on)
                {
                    if (_arcVideoHooked && video != null) video.VideoStarted -= OnArcademyVideoStarted;
                    _arcVideoHooked = false;
                }
            }
            catch (Exception ex) { Log.Debug("[Game] arcademy video hook: {E}", ex.Message); }
        }

        private void OnArcademyVideoStarted() => Dispatcher.UIThread.Post(() => ArcademyVideoSuspend(true));

        /// <summary>WPF OnVideoStarted / OnVideoEnded. The un-freeze never lifts a panic suspend or an
        /// audio-only one; those end on their own terms.</summary>
        internal void ArcademyVideoSuspend(bool on)
        {
            if (IsClosedOrClosing) return;
            if (on)
            {
                _arcVideoSuspended = true;
                PostArcademySuspend(true, "video");
                StartArcademyVideoWatch();
                return;
            }
            if (!_arcVideoSuspended) return;
            _arcVideoSuspended = false;
            StopArcademyVideoWatch();
            if (_arcPanicSuspended) { Log.Debug("[Game] arcademy: video ended but a panic suspend still stands"); return; }
            if (CoreSettings.Current?.AudioOnlySession == true) return;
            PostArcademySuspend(false, "video");
            try { Web.Focus(); } catch { }   // video clicks steal activation: hand the keyboard back
        }

        private void StartArcademyVideoWatch()
        {
            if (_arcVideoWatch != null) return;
            _arcVideoWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _arcVideoWatch.Tick += (_, _) => { if (CoreEngine.Video?.IsPlaying != true) ArcademyVideoSuspend(false); };
            _arcVideoWatch.Start();
        }

        private void StopArcademyVideoWatch()
        {
            try { _arcVideoWatch?.Stop(); } catch { }
            _arcVideoWatch = null;
        }
    }
}
