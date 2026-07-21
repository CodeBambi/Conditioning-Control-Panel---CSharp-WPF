using System;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>
/// Single source of truth for "web media is playing in the embedded browser" — regardless of
/// whether the app started it (Takeover, chaos HT-link bubble, remote control, avatar speech
/// link) or the user did by clicking play on a page they browsed to themselves.
/// <para>
/// Before this service, that state lived in <c>AutonomyService._webVideoActive</c> and was only
/// ever set by autonomy's own fullscreen takeover, because the JS that reports playback was
/// injected exclusively on <c>autoPlayFullscreen: true</c> navigations. User-started videos were
/// invisible to the app, so mandatory videos, bubble counts, lock cards, pop quizzes and every
/// autonomy effect fired straight over the top of them.
/// </para>
/// <para>
/// Two distinct concepts live here, deliberately kept apart:
/// <list type="bullet">
/// <item><b>Media session</b> — something is playing. Soft/protective: suppresses interruptions
/// while <see cref="ShouldDeferInterruptions"/> is true. Owned by whoever started it.</item>
/// <item><b>Takeover</b> — the app forced a fullscreen video on the user. Hard: holds the
/// <see cref="InteractionQueueService.InteractionType.WebVideo"/> slot, is bounded by the clip's
/// real duration (#484 auto-advance), and must always be force-endable.</item>
/// </list>
/// A user-started session also claims the queue slot when it can, which is what makes every
/// existing queue consumer (VideoService, BubbleCountService, LockCardService, PopQuizService)
/// correct for free without touching them.
/// </para>
/// </summary>
public sealed class BrowserMediaService
{
    /// <summary>Who caused the current media session to begin.</summary>
    public enum MediaOwner
    {
        /// <summary>Takeover's own WebVideo action.</summary>
        Autonomy,
        /// <summary>The user clicked play on a page they browsed to. Never force-ended.</summary>
        User,
        /// <summary>A link in an avatar speech bubble (user-clicked, but app-navigated).</summary>
        AvatarLink,
        /// <summary>Chaos-mode HT-link effect bubble.</summary>
        Chaos,
        /// <summary>Remote controller "play_hypnotube" command.</summary>
        Remote
    }

    private readonly object _lock = new();

    private bool _isPlaying;
    private bool _isTakeover;
    private MediaOwner? _owner;
    private double _durationSeconds;
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;
    private DateTime _lastEndedUtc = DateTime.MinValue;
    private bool _holdsQueueSlot;
    private int _sessionGeneration;

    private DispatcherTimer? _heartbeatTimer;

    /// <summary>How long we tolerate silence from the page's progress heartbeat before deciding
    /// the session is dead. The injected script ticks every 5s while playing, so 20s means four
    /// missed ticks — long enough to ride out a stall or a slow SPA route change.</summary>
    private const double HeartbeatTimeoutSeconds = 20;

    /// <summary>Grace added on top of a takeover clip's reported length before the hard stop.</summary>
    private const double TakeoverGraceSeconds = 15;

    /// <summary>Sanity ceiling for a single takeover, in case a page reports nonsense.</summary>
    private const double TakeoverMaxSeconds = 3600;

    /// <summary>True while any web media (video or audio) is playing in the embedded browser.</summary>
    public bool IsPlaying { get { lock (_lock) return _isPlaying; } }

    /// <summary>True while the current session is an app-forced fullscreen takeover.</summary>
    public bool IsTakeover { get { lock (_lock) return _isTakeover; } }

    /// <summary>Who started the current session, or null when idle.</summary>
    public MediaOwner? Owner { get { lock (_lock) return _owner; } }

    /// <summary>
    /// The one gate every interrupting subsystem asks. True while media is playing AND for a
    /// configurable cool-off afterwards — the cool-off is what stops a mandatory video or a fresh
    /// Takeover action from firing the instant a clip ends ("restarts during or immediately
    /// after"). Returns false when the user has opted out of protection.
    /// </summary>
    public bool ShouldDeferInterruptions
    {
        get
        {
            var settings = App.Settings?.Current;
            lock (_lock)
            {
                var sinceEnded = _lastEndedUtc == DateTime.MinValue
                    ? double.PositiveInfinity
                    : (DateTime.UtcNow - _lastEndedUtc).TotalSeconds;
                return ResolveDeferInterruptions(
                    settings?.ProtectBrowserVideoPlayback ?? false,
                    _isPlaying,
                    sinceEnded,
                    settings?.BrowserVideoGraceSeconds ?? 0);
            }
        }
    }

    /// <summary>
    /// Pure decision behind <see cref="ShouldDeferInterruptions"/>, split out so it is testable
    /// without a WPF <c>Application</c> or the static settings singleton.
    /// </summary>
    /// <param name="protectEnabled">The user's ProtectBrowserVideoPlayback preference.</param>
    /// <param name="isPlaying">Whether browser media is currently playing.</param>
    /// <param name="secondsSinceEnded">Seconds since the last session ended; infinity if none.</param>
    /// <param name="graceSeconds">Configured cool-off window.</param>
    public static bool ResolveDeferInterruptions(
        bool protectEnabled, bool isPlaying, double secondsSinceEnded, int graceSeconds)
    {
        if (!protectEnabled) return false;
        if (isPlaying) return true;
        if (double.IsInfinity(secondsSinceEnded) || double.IsNaN(secondsSinceEnded)) return false;
        return secondsSinceEnded < Math.Max(0, graceSeconds);
    }

    /// <summary>Raised on the UI thread when <see cref="IsPlaying"/> flips.</summary>
    public event EventHandler<bool>? PlayingChanged;

    /// <summary>
    /// Claim the fullscreen slot for an app-driven takeover BEFORE navigating. Returns false when
    /// something else already owns the slot, in which case the caller must not navigate — this is
    /// the atomic guard that closes the race against VideoService's ~2s pre-roll window where a
    /// committed mandatory video still reads as not playing.
    /// </summary>
    public bool BeginTakeover(MediaOwner owner)
    {
        if (App.InteractionQueue != null && !App.InteractionQueue.TryStart(
                InteractionQueueService.InteractionType.WebVideo, () => { }, queue: false))
        {
            App.Logger?.Information("BrowserMedia: fullscreen slot busy ({Current}) - {Owner} takeover refused",
                App.InteractionQueue.CurrentInteraction, owner);
            return false;
        }

        lock (_lock)
        {
            _sessionGeneration++;
            _owner = owner;
            _isTakeover = true;
            _holdsQueueSlot = App.InteractionQueue != null;
            _durationSeconds = 0;
            // Not _isPlaying yet — that waits for the page to actually report playback. The
            // heartbeat timer covers the load window so a page that never plays still releases.
            _lastHeartbeatUtc = DateTime.UtcNow;
        }

        StartHeartbeatTimer();
        App.Logger?.Information("BrowserMedia: {Owner} takeover claimed the fullscreen slot", owner);
        return true;
    }

    /// <summary>
    /// Register a navigation that replaces whatever was playing, without going through the slot
    /// claim — used by paths the user themselves initiated (avatar speech-bubble link) where we
    /// must not refuse, but must still hand the session over cleanly rather than leaving the
    /// previous claim stranded and letting the new video run untracked.
    /// </summary>
    public void ReplaceSession(MediaOwner owner, bool takeover)
    {
        bool hadSession;
        lock (_lock)
        {
            hadSession = _isPlaying || _owner.HasValue;
            _sessionGeneration++;
            _owner = owner;
            _isTakeover = takeover;
            _durationSeconds = 0;
            _lastHeartbeatUtc = DateTime.UtcNow;

            if (!_holdsQueueSlot && App.InteractionQueue != null)
            {
                // Best-effort: if the slot is free, take it so the rest of the app defers.
                _holdsQueueSlot = App.InteractionQueue.TryStart(
                    InteractionQueueService.InteractionType.WebVideo, () => { }, queue: false);
            }
        }

        StartHeartbeatTimer();
        App.Logger?.Information("BrowserMedia: session replaced by {Owner} (takeover={Takeover}, had session={Had})",
            owner, takeover, hadSession);
    }

    /// <summary>
    /// Reported by the always-on injected script the first time a media element actually plays.
    /// When no app-driven session is open this opens a <see cref="MediaOwner.User"/> one — that is
    /// the whole point of the always-on injection: playback the user started is now first-class.
    /// </summary>
    public void OnMediaPlaying(double durationSeconds)
    {
        bool becamePlaying;
        bool scheduleHardStop = false;
        int gen;

        lock (_lock)
        {
            becamePlaying = !_isPlaying;
            _isPlaying = true;
            _lastHeartbeatUtc = DateTime.UtcNow;

            if (_owner == null)
            {
                // Nobody claimed this — the user pressed play on a page they browsed to.
                _owner = MediaOwner.User;
                _isTakeover = false;
                _sessionGeneration++;
                if (!_holdsQueueSlot && App.InteractionQueue != null)
                {
                    _holdsQueueSlot = App.InteractionQueue.TryStart(
                        InteractionQueueService.InteractionType.WebVideo, () => { }, queue: false);
                }
                App.Logger?.Information("BrowserMedia: user-started web media detected (slot claimed={Claimed})",
                    _holdsQueueSlot);
            }

            if (IsUsableDuration(durationSeconds))
            {
                _durationSeconds = durationSeconds;
                // Only a takeover is bounded by the clip length. For user-owned playback,
                // auto-advancing to the next video is just the user browsing — the heartbeat
                // governs instead.
                scheduleHardStop = _isTakeover;
            }
            gen = _sessionGeneration;
        }

        StartHeartbeatTimer();
        ExtendQueueTimeout();
        if (scheduleHardStop) ScheduleTakeoverHardStop(gen, durationSeconds);
        if (becamePlaying) RaisePlayingChanged(true);
    }

    /// <summary>
    /// Progress heartbeat (~5s) from the injected script. Doubles as liveness — replacing the old
    /// fixed 30s load watchdog, which could not tell "page never played" from "long video still
    /// playing" — and re-measures duration when a site auto-advances to a new clip.
    /// </summary>
    public void OnMediaProgress(double positionSeconds, double durationSeconds)
    {
        bool durationChanged = false;
        int gen;
        lock (_lock)
        {
            if (!_isPlaying) return;
            _lastHeartbeatUtc = DateTime.UtcNow;

            if (IsUsableDuration(durationSeconds) && Math.Abs(durationSeconds - _durationSeconds) > 1.0)
            {
                _durationSeconds = durationSeconds;
                durationChanged = _isTakeover;
            }
            gen = _sessionGeneration;
        }

        if (durationChanged)
        {
            ExtendQueueTimeout();
            ScheduleTakeoverHardStop(gen, durationSeconds);
        }
    }

    /// <summary>
    /// Media stopped — ended, paused past the grace, navigated away, or the heartbeat died.
    /// Releases the queue slot and starts the cool-off. Idempotent.
    /// </summary>
    public void OnMediaStopped(string reason)
    {
        bool wasPlaying;
        bool releaseSlot;

        lock (_lock)
        {
            wasPlaying = _isPlaying;
            if (!wasPlaying && _owner == null) return; // already idle

            _sessionGeneration++;   // invalidates any pending hard stop
            _isPlaying = false;
            _isTakeover = false;
            _owner = null;
            _durationSeconds = 0;
            _lastEndedUtc = DateTime.UtcNow;
            releaseSlot = _holdsQueueSlot;
            _holdsQueueSlot = false;
        }

        StopHeartbeatTimer();

        if (releaseSlot)
            App.InteractionQueue?.CompleteIfCurrent(InteractionQueueService.InteractionType.WebVideo);

        App.Logger?.Information("BrowserMedia: session ended ({Reason}) - cool-off {Grace}s begins",
            reason, App.Settings?.Current?.BrowserVideoGraceSeconds ?? 0);

        if (wasPlaying) RaisePlayingChanged(false);
    }

    /// <summary>
    /// Hard-terminate from outside the page's own lifecycle (queue stuck recovery, panic, session
    /// end, takeover overrunning its clip): tears the browser takeover down, then runs the normal
    /// stopped bookkeeping. Marshals to the UI thread; runs synchronously when already on it.
    /// </summary>
    public void ForceEnd(string reason)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
        {
            OnMediaStopped(reason);   // no UI to tear down; still release flag + slot
            return;
        }
        if (dispatcher.CheckAccess()) ForceEndCore(reason);
        else dispatcher.BeginInvoke(() => ForceEndCore(reason));
    }

    /// <summary>
    /// Synchronous body of <see cref="ForceEnd"/> — UI thread only. Callers that must guarantee
    /// "teardown happened before the slot frees" call this directly, so no dispatcher hop can
    /// interleave a fresh video in between and have it killed by the previous one's timer.
    /// </summary>
    internal void ForceEndCore(string reason)
    {
        try
        {
            var mainWindow = App.MainWindowRef
                ?? System.Windows.Application.Current?.MainWindow as MainWindow;
            mainWindow?.EndWebVideoTakeover();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "BrowserMedia: force-ending browser takeover failed");
        }
        finally
        {
            OnMediaStopped(reason);
        }
    }

    /// <summary>
    /// The user exited fullscreen (Esc, F11, double-click). That ends the <i>takeover</i> — the
    /// app no longer owns the screen — but the page may well keep playing, and if the user is
    /// still watching we must keep deferring interruptions. Previously these were conflated and
    /// any fullscreen change ended the session outright.
    /// </summary>
    public void OnFullscreenExited()
    {
        lock (_lock)
        {
            if (!_isTakeover) return;
            _isTakeover = false;
            // Ownership passes to the user: they chose to leave fullscreen but kept the video up.
            if (_isPlaying) _owner = MediaOwner.User;
        }
        App.Logger?.Information("BrowserMedia: fullscreen exited - takeover released, media session continues");
    }

    private static bool IsUsableDuration(double seconds) =>
        !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > 0;   // live streams report 0/Inf

    private void ExtendQueueTimeout()
    {
        double seconds;
        lock (_lock)
        {
            if (!_holdsQueueSlot) return;
            // Unknown length (live stream, metadata miss) must NOT inherit the queue's 5-minute
            // stuck window — that would force-end a healthy long video mid-play. The heartbeat
            // is the real liveness check now, so give the slot a generous ceiling.
            seconds = _durationSeconds > 0 ? _durationSeconds + TakeoverGraceSeconds : TakeoverMaxSeconds;
        }
        App.InteractionQueue?.ExtendTimeout(seconds, InteractionQueueService.InteractionType.WebVideo);
    }

    /// <summary>
    /// Bound a takeover to the clip's real length. Kept from the #484 fix: sites auto-advance to
    /// the next clip once our handlers fire, so without this the takeover outlives the video it
    /// announced. Generation-checked twice (timer fire, and again on the UI thread) so a session
    /// that ended and restarted in between can't be killed by the old timer.
    /// </summary>
    private void ScheduleTakeoverHardStop(int generation, double durationSeconds)
    {
        var cap = TimeSpan.FromSeconds(Math.Min(durationSeconds + TakeoverGraceSeconds, TakeoverMaxSeconds));
        App.Logger?.Information("BrowserMedia: takeover clip reports {Seconds:F0}s - hard stop at +{Cap:F0}s",
            durationSeconds, cap.TotalSeconds);

        System.Threading.Tasks.Task.Delay(cap).ContinueWith(_ =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            lock (_lock)
            {
                if (_sessionGeneration != generation || !_isTakeover) return;
            }
            dispatcher.BeginInvoke(() =>
            {
                lock (_lock)
                {
                    if (_sessionGeneration != generation || !_isTakeover) return;
                }
                App.Logger?.Information("BrowserMedia: takeover ran past its clip length - force-ending");
                ForceEndCore("takeover-overran");
            });
        });
    }

    private void StartHeartbeatTimer()
    {
        try
        {
            DispatcherHelper.RunOnUISync(() =>
            {
                if (_heartbeatTimer != null) return;   // already running
                _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _heartbeatTimer.Tick += OnHeartbeatTick;
                _heartbeatTimer.Start();
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("BrowserMedia: failed to start heartbeat timer: {Error}", ex.Message);
        }
    }

    private void StopHeartbeatTimer()
    {
        try
        {
            DispatcherHelper.RunOnUISync(() =>
            {
                _heartbeatTimer?.Stop();
                if (_heartbeatTimer != null) _heartbeatTimer.Tick -= OnHeartbeatTick;
                _heartbeatTimer = null;
            });
        }
        catch { /* shutdown */ }
    }

    private void OnHeartbeatTick(object? sender, EventArgs e)
    {
        double silentFor;
        bool takeover;
        lock (_lock)
        {
            if (_owner == null && !_isPlaying) { StopHeartbeatTimer(); return; }
            silentFor = (DateTime.UtcNow - _lastHeartbeatUtc).TotalSeconds;
            takeover = _isTakeover;
        }

        if (silentFor < HeartbeatTimeoutSeconds) return;

        App.Logger?.Warning("BrowserMedia: no heartbeat for {Silent:F0}s - ending session", silentFor);
        if (takeover) ForceEndCore("heartbeat-lost");
        else OnMediaStopped("heartbeat-lost");
    }

    private void RaisePlayingChanged(bool playing)
    {
        try
        {
            DispatcherHelper.RunOnUI(() => PlayingChanged?.Invoke(this, playing));
        }
        catch { /* shutdown */ }
    }
}
