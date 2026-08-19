using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>One mandatory video, as a subscriber sees it. No path and no file name: the clips are
/// the user's own media and this record reaches event handlers and, one day, a log.</summary>
/// <param name="Ordinal">Which firing this was, from 1.</param>
/// <param name="At">When it started, on the session clock.</param>
public readonly record struct MandatoryVideoEvent(int Ordinal, DateTimeOffset At);

/// <summary>What one firing produced, before it is delivered.</summary>
/// <param name="Event">The public half.</param>
/// <param name="Path">The clip. Stays inside the module and the surface.</param>
/// <param name="MaxLength">Upstream's max-length cap for this run, or zero for none.</param>
public sealed record MandatoryVideoFiring(MandatoryVideoEvent Event, string Path, TimeSpan MaxLength);

/// <summary>
/// <b>Mandatory Video</b> — WPF's second EFFECTS rack row
/// (<c>Views/Tabs/StudioTabView.xaml.cs:486-487</c>), and <b>the first module in the port that plays
/// a file</b>.
///
/// <para><b>It ships as its VIDEO half only, and that is stated in three places.</b> Upstream plays
/// the clip's soundtrack through the same player that draws it
/// (<c>Services/Video/VideoService.cs:1245-1260</c>, <c>:1300-1322</c>); this port's video
/// capability is a GDI surface fed decoded pictures and has no audio path at all. So: the row is
/// TITLED <see cref="DisplayTitle"/> so the user reads the scope before enabling anything; the
/// panel LEADS with the missing half; and <see cref="Ready"/> returns
/// <see cref="CapabilityState.Degraded"/> on EVERY run however healthy, carrying
/// <see cref="EffectReasonCodes.VideoSilentHalfAbsent"/>. That last one is the part that matters:
/// every other module in the rack reports a clean <c>Available</c> when everything works, and this
/// one must not, because the absence is a property of the BUILD rather than of the run. SP-109's
/// Brain Drain set the precedent and the reasoning is unchanged.</para>
///
/// <para><b>The two clocks, and why they are two.</b> The FIRING interval is upstream's
/// <c>ScheduleNext</c> (<c>VideoService.cs:2216-2229</c>) and belongs to
/// <see cref="PacedSessionEffect{TFiring}"/>. The FRAME cadence belongs to
/// <see cref="VideoSurfacePresenter"/>, on SP-105's precedent that a cadence keeping a SURFACE
/// correct is the surface's. This is the first module in the port that needs both, and neither had
/// to be invented for it.</para>
///
/// <para><b>What the dot means here — the seventh meaning, MOTION.</b> See
/// <see cref="WorkIsRunning"/>.</para>
/// </summary>
public sealed class MandatoryVideoEffect : PacedSessionEffect<MandatoryVideoFiring>
{
    /// <summary>WPF's rack key for this module (<c>StudioTabView.xaml.cs:486</c>), and the same key
    /// its quick-toggle switches on.</summary>
    public const string EffectId = "video";

    /// <summary>
    /// The row's label. WPF's is plain <c>"Mandatory Video"</c>
    /// (<c>StudioTabView.xaml.cs:486</c>); this row's says what it IS.
    ///
    /// <para><b>This string is load-bearing rather than cosmetic.</b> The dot on this row reads
    /// <see cref="EffectDotState.Live"/> while the picture is moving, and that is only honest
    /// because the row names its own scope where the user reads it. A later tidy of the label back
    /// to plain "Mandatory Video" turns a truthful dot into a false one — which is why a headless
    /// fact pins this exact string beside the dot it justifies.</para>
    /// </summary>
    public const string DisplayTitle = "Mandatory Video (silent half)";

    private readonly PersistenceStore<MandatoryVideoPresetDocument> _preset;
    private readonly IVideoClipPool _pool;
    private readonly IVideoSurface _surface;
    private readonly Random _random;
    private CapabilityState? _lastPlayback;

    /// <param name="owner">This module's operation owner: one generation per armed schedule.</param>
    /// <param name="signal">The one boundary a state change or a draw is allowed to arrive on.</param>
    /// <param name="clock">The clock the FIRING schedule paces on. Never <c>DateTime.Now</c>.</param>
    /// <param name="preset">This module's persisted dials.</param>
    /// <param name="pool">Where the clips come from.</param>
    /// <param name="surface">Where the pictures go.</param>
    /// <param name="random">The jitter's source; injected so a fact can pin the arithmetic.</param>
    public MandatoryVideoEffect(
        AsyncOperationOwner owner,
        EffectSignal signal,
        ISessionClock clock,
        PersistenceStore<MandatoryVideoPresetDocument> preset,
        IVideoClipPool pool,
        IVideoSurface surface,
        Random? random = null)
        : base(owner, signal, clock, "mandatory-video")
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(surface);
        _preset = preset;
        _pool = pool;
        _surface = surface;
        _random = random ?? new Random();
    }

    /// <inheritdoc/>
    public override string Id => EffectId;

    /// <inheritdoc/>
    public override string Title => DisplayTitle;

    /// <inheritdoc/>
    public override bool Enabled => _preset.Current.Enabled;

    /// <summary>How many clips this module's folder holds. The panel shows it so an empty folder has
    /// an answer.</summary>
    public int ClipCount => _pool.ActiveCount;

    /// <summary>Where the clips are read from. A path, never a file name.</summary>
    public string ClipFolder => _pool.Folder;

    /// <summary>Firings that really started a clip. A firing that found an empty pool, an unreachable
    /// display, or a video already playing is not one of these — upstream counts nothing for those
    /// either.</summary>
    public int PlayedCount => FireCount;

    /// <summary>The most recent firing, or null if none has happened yet.</summary>
    public MandatoryVideoEvent? Last => LastFiring?.Event;

    /// <summary>
    /// What the video capability said about the last clip this module started, verbatim. Null before
    /// anything played. The panel renders this, so what the user reads about the picture is the
    /// capability's own typed outcome rather than a fixed sentence.
    /// </summary>
    public CapabilityState? LastPlayback
    {
        get { lock (Gate) { return _lastPlayback; } }
    }

    /// <summary>How many pictures the DECODER produced for the clip that is playing. Exposed beside
    /// <see cref="FramesHeld"/> precisely so the two can be compared: a run where they diverge is the
    /// failure this whole capability exists to make visible.</summary>
    public int FramesDecoded => _surface.FramesDecoded;

    /// <summary>How many of them the operating system was confirmed to be HOLDING.</summary>
    public int FramesHeld => _surface.FramesHeld;

    /// <summary>Of those, how many CHANGED the OS's own copy of the surface.</summary>
    public int FramesAdvanced => _surface.FramesAdvanced;

    /// <summary>True while a clip this module started is on screen.</summary>
    public bool Playing => _surface.Showing;

    /// <summary>Raised on the UI thread, inside the dispatch boundary, once per clip that really
    /// started.</summary>
    public event Action<MandatoryVideoEvent>? Fired;

    /// <inheritdoc/>
    public override void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        _preset.Mutate(p => p.Enabled = enabled);
        RaiseChanged();
    }

    /// <summary>The frequency dial. Writes and RE-PACES: upstream's own frequency slider takes effect
    /// on the next scheduled firing rather than after the old interval expires, which is what
    /// <see cref="PacedSessionEffect{TFiring}.RefreshSchedule"/> does.</summary>
    public void SetPerHour(int perHour)
    {
        var clamped = Math.Clamp(perHour, MandatoryVideoSchedule.MinPerHour, MandatoryVideoSchedule.MaxPerHour);
        if (_preset.Current.PerHour == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.PerHour = perHour);
        Refresh();
    }

    /// <summary>The max-length cap, in seconds; 0 is upstream's own "no cap"
    /// (<c>VideoService.cs:5509-5510</c>). It applies to the NEXT clip: upstream arms its cap timer
    /// when playback starts (<c>:2440</c>) and never re-arms it mid-clip.</summary>
    public void SetMaxSeconds(int seconds)
    {
        var clamped = Math.Clamp(
            seconds, MandatoryVideoPresetDocument.MinMaxSeconds, MandatoryVideoPresetDocument.MaxMaxSeconds);
        if (_preset.Current.MaxSeconds == clamped)
        {
            return;
        }

        _preset.Mutate(p => p.MaxSeconds = seconds);
        RaiseChanged();
    }

    /// <summary>
    /// <b>THE DOT'S SEVENTH MEANING — MOTION.</b>
    ///
    /// <code>
    /// Live = a firing is on the clock
    ///     &amp;&amp; the OS says this process can put a video surface on a display
    ///     &amp;&amp; ( nothing is playing  OR  the OS's copy of the surface ADVANCED on the last frame )
    /// </code>
    ///
    /// <para>The six before it were the <b>clock</b> (paced), the <b>screen</b> (continuous),
    /// <b>change</b> (moving), <b>custody</b> (non-drawing), <b>reach</b> (audio) and <b>demand</b>
    /// (input). CHANGE is the closest and it is a different fact: SP-098's "moving" is a claim about
    /// this process's own animation state, which this process authors and knows. <b>MOTION is a claim
    /// about what the operating system is HOLDING, read back — the first of the seven that can be
    /// false while every call this process made succeeded.</b></para>
    ///
    /// <para><b>Not "the decoder is fed."</b> That is this packet's central trap in dot form, and
    /// upstream's own worst failure is exactly the state it would light for: "the window stays white
    /// and MediaEnded never fires, wedging cleanup" (<c>VideoService.cs:2677-2678</c>), and a frozen
    /// final frame after a suspend (<c>:1394-1397</c>). Upstream watches the same thing from the
    /// other side (<c>_voutLostSinceTicks</c> <c>:104</c>, <c>_frameReady</c> <c>:3788</c>,
    /// <c>StartBlurFrameWatchdog</c> <c>:3265</c>) — its own comment on the vout watchdog is that
    /// "decode-side health signals (TimeChanged, EndReached) are useless here" (<c>:5538-5540</c>),
    /// which is this rule in upstream's own words.</para>
    ///
    /// <para><b>Why the third clause is a DISJUNCTION.</b> A session spends almost all of its time
    /// between clips. A bare "the picture is moving" conjunct would darken the dot for the 95 % of a
    /// session with nothing playing, which is the opposite lie from the one it prevents — the same
    /// shape, and the same answer, as SP-110's third clause.</para>
    /// </summary>
    protected override bool WorkIsRunning =>
        ScheduleArmed && _surface.CanReachADisplay && (!_surface.Showing || _surface.Running);

    /// <summary>The interval to the next firing, from this module's own dial
    /// (<c>VideoService.cs:2225-2229</c>).</summary>
    protected override TimeSpan NextInterval() =>
        MandatoryVideoSchedule.Interval(_preset.Current.PerHour, _random.NextDouble());

    /// <summary>
    /// One firing comes due. Upstream's own guards, in upstream's own order: a video already playing
    /// is skipped without counting (<c>VideoService.cs:2237</c>, <c>if (… &amp;&amp; !_videoPlaying …)</c>),
    /// and an empty pool returns before anything is shown or counted (<c>:6647-6650</c>,
    /// <c>Services/BubbleCountService.cs:210-217</c>). The display check sits with them: upstream has
    /// no analogue because a WPF window on a WPF desktop is not in doubt, and here it is.
    /// </summary>
    protected override MandatoryVideoFiring? Compose()
    {
        // A clip is already on screen. Upstream drops the firing rather than stacking a second
        // window, and the schedule keeps running.
        if (_surface.Showing)
        {
            return null;
        }

        // Nowhere to put a picture. Not counted, not played, and the schedule keeps running so a
        // display that comes back is used at the next firing rather than at the next session.
        if (!_surface.CanReachADisplay)
        {
            return null;
        }

        var path = _pool.Draw();
        if (path is null)
        {
            return null;
        }

        return new MandatoryVideoFiring(
            new MandatoryVideoEvent(0, default), path, _preset.Current.MaxLength);
    }

    /// <inheritdoc/>
    protected override MandatoryVideoFiring Stamp(MandatoryVideoFiring firing, int ordinal, DateTimeOffset at) =>
        firing with { Event = firing.Event with { Ordinal = ordinal, At = at } };

    /// <summary>
    /// Start the clip, then raise <see cref="Fired"/> — that order, for the same reason the drawing
    /// modules place their surface before they notify: the visible half is the user's outcome and
    /// must not be hostage to whatever a UI subscriber does.
    ///
    /// <para>The capability's own typed outcome is REMEMBERED rather than discarded. It is the only
    /// place a user or a bug report can learn that a clip opened and the operating system's copy of
    /// the surface never carried it — which is precisely the state a module that trusted its own
    /// call would show as working.</para>
    /// </summary>
    protected override void Deliver(MandatoryVideoFiring firing)
    {
        var outcome = _surface.Begin(firing.Path, firing.MaxLength, OnClipEnded);
        lock (Gate)
        {
            _lastPlayback = outcome;
        }

        if (outcome is not CapabilityState.Available)
        {
            // A refusal takes the surface straight back down — the presenter already did — and the
            // dot must stop claiming a picture on this frame rather than at the next repaint.
            RaiseChanged();
        }

        Fired?.Invoke(firing.Event);
    }

    /// <summary>
    /// Stop the clip. Unconditional and first on the way down, the same rule the audio modules'
    /// teardown follows: a panic must be able to clear the screen even with a wedged UI thread.
    /// </summary>
    protected override void OnDisarmed() => _surface.End();

    /// <summary>
    /// Narrow the arm result to what this row can honestly claim. THREE outcomes, and they are
    /// deliberately different states.
    /// </summary>
    protected override CapabilityState Ready(CapabilityState scheduled)
    {
        if (scheduled is not CapabilityState.Available)
        {
            return scheduled;
        }

        if (!_surface.CanReachADisplay)
        {
            // The whole channel is gone. Unavailable, not Degraded: there is no surviving half to
            // name. The capability's own reason travels verbatim where it has one, so a Linux run
            // reads the six-step manual gate and a display-less Windows run reads the OS's own
            // counts — never a sentence this module made up about a platform.
            var detail = _surface.LastPlacement switch
            {
                CapabilityState.Unavailable u => u.Reason.Detail,
                CapabilityState.DependencyMissing m => m.Reason.Detail,
                CapabilityState.Faulted f => f.Reason.Detail,
                CapabilityState.PermissionRequired p => p.Reason.Detail,
                _ => "the video capability reports no display, no compositor or no usable media stack for "
                    + "this process",
            };

            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.VideoSurfaceUnavailable,
                $"the '{Id}' module is armed and nothing it plays can reach anybody: {detail}"));
        }

        // DEGRADED ON EVERY RUN, HOWEVER HEALTHY. The silent half is a property of the build.
        // An empty pool is a property of the RUN, and where both are true BOTH travel: the
        // run-level cause comes first, with its own code, because it is the one the user can fix.
        var poolIsEmpty = _pool.ActiveCount == 0;
        var code = poolIsEmpty ? EffectReasonCodes.VideoNoClip : EffectReasonCodes.VideoSilentHalfAbsent;
        var detailPrefix = poolIsEmpty
            ? $"there is no video in {_pool.Folder}, so every firing that comes due will play nothing and "
                + "count nothing. Dropping an .mp4/.mov/.avi/.wmv/.mkv/.webm in there (or in a subfolder) is "
                + "picked up at the next firing, without restarting the session. AND: "
            : string.Empty;

        return new CapabilityState.Degraded(
            poolIsEmpty
                ? "the schedule is armed on a display the operating system confirms, and every firing will "
                    + "play nothing"
                : "the schedule is armed on a display the operating system confirms, and the clips will play "
                    + "SILENTLY",
            new CapabilityReason(code, detailPrefix + VideoPanelNoticeText));
    }

    /// <summary>
    /// The missing half, in the words the panel renders and the arm result carries. ONE string, so
    /// the two cannot drift into two accounts of one absence — SP-109's rule, kept.
    /// </summary>
    public const string VideoPanelNoticeText =
        "This row is the VIDEO half of upstream's Mandatory Video. The clip's SOUND is not ported: upstream "
        + "plays the soundtrack through the same player that draws the picture, at the app's video volume and "
        + "through the user's chosen output device (Services/Video/VideoService.cs:1245-1260, :1300-1322), and "
        + "this port's video capability is a surface fed decoded pictures with no audio path at all. Wiring one "
        + "would mean an audio/video synchronisation subsystem, not a setting — so the clips here play SILENTLY, "
        + "and that is ABSENT rather than broken. Also not ported and not shown as dead controls: attention "
        + "checks and the strict/retry apparatus, the blurred-background composite, dual-monitor mirroring, the "
        + "browser engine, remote and content-pack clips, and seek/pause/random-segment.";

    /// <summary>
    /// The clip finished, was capped, or the surface stopped holding it. Upstream re-schedules from
    /// the END of a video rather than from its start (<c>VideoService.cs:2264</c>: "Cleanup() will
    /// call ScheduleNext() when video ends"), so the port re-paces here.
    /// </summary>
    private void OnClipEnded()
    {
        RefreshSchedule();
        RaiseChanged();
    }
}
