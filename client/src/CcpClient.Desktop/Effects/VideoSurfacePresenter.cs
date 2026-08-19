using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Video;

namespace CcpClient.Desktop.Effects;

/// <summary>Where the Mandatory Video module's pictures go when they have somewhere to go.</summary>
public interface IVideoSurface
{
    /// <summary>
    /// Open a clip, put the surface up, show its first picture and start advancing. Called on the
    /// surface thread, from inside the module's firing.
    /// </summary>
    /// <param name="clipPath">The file to play.</param>
    /// <param name="maxLength">Upstream's max-length cap, or <see cref="TimeSpan.Zero"/> for none
    /// (<c>Services/Video/VideoService.cs:5509-5510</c>: the setting defaults to 0 and 0 means off).</param>
    /// <param name="onEnded">Raised on the surface thread when the clip finishes, is capped, or the
    /// surface stops holding it. Never raised from inside <see cref="Begin"/> itself.</param>
    CapabilityState Begin(string clipPath, TimeSpan maxLength, Action onEnded);

    /// <summary>Take the picture off the screen now and close the clip.</summary>
    void End();

    /// <summary>True while a surface this presenter placed is up.</summary>
    bool Showing { get; }

    /// <summary>
    /// True while this module's work is genuinely running — <b>the property the dot's third input
    /// reads</b>. It is not <see cref="Showing"/>: a video surface that is up and has stopped
    /// advancing is a frozen picture, and upstream's own worst failure is exactly that state with
    /// every call still returning success (<c>VideoService.cs:2680-2688</c>).
    /// </summary>
    bool Running { get; }

    /// <summary>True while there is anything to release — a surface, a cadence, or an open clip.
    /// The module's teardown tests this rather than <see cref="Showing"/>, because a clip whose
    /// surface refused is not showing and still holds an OPEN DECODER.</summary>
    bool Engaged { get; }

    /// <summary>The OS's answer to "could this process put a picture in front of anybody at all".</summary>
    bool CanReachADisplay { get; }

    /// <summary>How many pictures the DECODER produced for the open clip. Reported so it can be
    /// compared against <see cref="FramesHeld"/>; never a success on its own.</summary>
    int FramesDecoded { get; }

    /// <summary>How many of them the operating system was afterwards confirmed to be HOLDING.</summary>
    int FramesHeld { get; }

    /// <summary>Of those, how many CHANGED the OS's copy of the surface.</summary>
    int FramesAdvanced { get; }

    /// <summary>The clip that is playing, or null.</summary>
    string? PlayingClip { get; }

    /// <summary>The last thing the OS said when this surface tried to place or show something,
    /// verbatim, or null before anything was attempted.</summary>
    CapabilityState? LastPlacement { get; }

    /// <summary>The most recent read-back the capability actually took, or
    /// <see cref="VideoSurfaceObservation.NotAsked"/> before anything was asked. A remembered ANSWER,
    /// never a remembered request — the panel renders it, so what the user reads about the picture is
    /// the operating system's own.</summary>
    VideoSurfaceObservation LastObservation { get; }
}

/// <summary>
/// Mandatory Video, drawn on a real video surface — the port's SECOND module whose picture has to
/// keep changing while it is on, and the first whose frames come from a file rather than from
/// arithmetic.
///
/// <para><b>Where the cadence lives, and why.</b> In the PRESENTER, on the injected
/// <see cref="ISessionClock"/>, exactly as <see cref="SpiralSurfacePresenter"/> puts it — SP-105's
/// precedent and SP-106's finding: an <b>interval that decides when a MODULE is due</b> belongs to
/// <see cref="PacedSessionEffect{TFiring}"/>, and a <b>cadence that keeps a SURFACE correct</b> is
/// the surface's. Mandatory Video has BOTH, which is the first time one module has needed the two
/// apart: its firing interval is upstream's <c>ScheduleNext</c>
/// (<c>Services/Video/VideoService.cs:2216-2229</c>) and its frame advance is the clip's own frame
/// rate.</para>
///
/// <para><b>What is NOT ported, and is recorded rather than stubbed:</b> the clip's SOUND (this row
/// is a silent half — see <see cref="MandatoryVideoEffect"/>); attention checks and the whole
/// strict/retry apparatus (<c>VideoService.cs:4846</c> and the mercy path); the blurred-background
/// composite (<c>:3436</c>); dual-monitor mirroring (<c>Services/Video/DualMonitorVideoService.cs</c>);
/// the browser engine and remote clips (<c>VideoService.Browser.cs</c>, <c>:6628-6650</c>); pack
/// clips and their decryption (<c>:6665-6680</c>); the vout watchdog, the native quarantine and the
/// poisoning cooldown (<c>:5541</c>, <c>:790</c>, <c>:1031</c>); and seek, pause and the random
/// segment (<c>:356</c>, <c>:444</c>, <c>:467</c>). Each is a subsystem this port does not have; a
/// silent no-op would make the module look complete.</para>
/// </summary>
public sealed class VideoSurfacePresenter : IVideoSurface, IDisposable
{
    /// <summary>One video surface at a time. Upstream places one window per screen
    /// (<c>VideoService.cs:2040-2045</c>); the port places on the primary display only, the same
    /// single-display limit every other ported module already carries (D66).</summary>
    public const int MaxConcurrentSurfaces = 1;

    /// <summary>
    /// The frame period used when the container reports no frame rate at all. Twelve and a half per
    /// second: slow enough that a rate-less container cannot spin the clock, fast enough that a
    /// picture visibly moves. Not upstream's — upstream hands the whole problem to LibVLC's own
    /// clock — so it is a divergence and it is named as one (D125).
    /// </summary>
    public static readonly TimeSpan FallbackFrameInterval = TimeSpan.FromMilliseconds(80);

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly Func<IVideoPresence> _presenceFactory;
    private readonly IVideoClipSource _clips;
    private readonly Func<VideoBounds?> _display;
    private readonly object _gate = new();

    private IVideoPresence? _presence;
    private IVideoClip? _clip;
    private IDisposable? _advance;
    private Action? _onEnded;
    private DateTimeOffset _startedAt;
    private TimeSpan _maxLength;
    private TimeSpan _frameInterval = FallbackFrameInterval;
    private bool _disposed;

    /// <param name="clock">The session clock. It carries the FRAME advance and nothing else; the
    /// module owns the firing interval.</param>
    /// <param name="dispatch">Marshals a cadence callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds the video presence, lazily, on the surface thread.</param>
    /// <param name="clips">The decoder seam. A build that cannot decode is an ordinary outcome.</param>
    /// <param name="display">Where the surface may go; null when the OS reports no display.</param>
    public VideoSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IVideoPresence> presenceFactory,
        IVideoClipSource clips,
        Func<VideoBounds?> display)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(presenceFactory);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(display);
        _clock = clock;
        _dispatch = dispatch;
        _presenceFactory = presenceFactory;
        _clips = clips;
        _display = display;
    }

    /// <summary>
    /// The product presenter: the real video capability, the real decoder, and the primary display.
    /// Nothing is created until a clip really plays — the presence builds its window lazily, so a
    /// session that never fires and every headless or unit run creates no window at all.
    /// </summary>
    public static VideoSurfacePresenter Product(ISessionClock clock, Action<Action> dispatch) =>
        new(clock, dispatch, VideoPresenceFactory.Create, VideoPresenceFactory.CreateClipSource(), DefaultPlacement);

    /// <summary>
    /// How wide the surface is, as a fraction of the display it is centred on.
    ///
    /// <para><b>The divergence from upstream's fullscreen-per-monitor cover lives here</b>, and it
    /// is deliberate. Upstream sizes its video window to the FULL screen bounds of every screen
    /// (<c>Services/Video/VideoService.cs:2631-2635</c>, <c>:2040-2045</c>). This port places one
    /// bounded rectangle on the primary display, for the same two reasons SP-110's card did
    /// (D110): five other modules draw through a click-through overlay that a fullscreen cover
    /// would blank, and a fullscreen topmost surface the user cannot dismiss is the one shape this
    /// port must not ship while it has no panic key. Recorded as D123.</para>
    /// </summary>
    public const double WidthFraction = 0.55;

    /// <summary>See <see cref="WidthFraction"/>.</summary>
    public const double HeightFraction = 0.42;

    /// <inheritdoc/>
    public bool Showing
    {
        get
        {
            lock (_gate)
            {
                return _presence?.IsPresenting == true;
            }
        }
    }

    /// <inheritdoc/>
    public bool Running
    {
        get
        {
            IVideoPresence? presence;
            bool advancing;
            lock (_gate)
            {
                presence = _presence;
                advancing = _advance is not null;
            }

            // BOTH clauses. Without the cadence, a surface holding its last frame for ever reports
            // itself alive; without the OS's own answer, a cadence firing into a dead surface does.
            return advancing && presence?.PictureIsMoving == true;
        }
    }

    /// <inheritdoc/>
    public bool Engaged
    {
        get
        {
            lock (_gate)
            {
                return _clip is not null || _advance is not null || _presence?.IsPresenting == true;
            }
        }
    }

    /// <inheritdoc/>
    public bool CanReachADisplay
    {
        get
        {
            lock (_gate)
            {
                _presence ??= _presenceFactory();
                return _presence.CanReachADisplay;
            }
        }
    }

    /// <inheritdoc/>
    public int FramesDecoded
    {
        get
        {
            lock (_gate)
            {
                return _clip?.DecodedFrames ?? 0;
            }
        }
    }

    /// <inheritdoc/>
    public int FramesHeld
    {
        get
        {
            lock (_gate)
            {
                return _presence?.FramesHeld ?? 0;
            }
        }
    }

    /// <inheritdoc/>
    public int FramesAdvanced
    {
        get
        {
            lock (_gate)
            {
                return _presence?.FramesAdvanced ?? 0;
            }
        }
    }

    /// <inheritdoc/>
    public string? PlayingClip { get; private set; }

    /// <inheritdoc/>
    public CapabilityState? LastPlacement { get; private set; }

    /// <inheritdoc/>
    public VideoSurfaceObservation LastObservation
    {
        get
        {
            lock (_gate)
            {
                return _presence?.LastObservation ?? VideoSurfaceObservation.NotAsked;
            }
        }
    }

    /// <inheritdoc/>
    public CapabilityState Begin(string clipPath, TimeSpan maxLength, Action onEnded)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipPath);
        ArgumentNullException.ThrowIfNull(onEnded);
        if (_disposed)
        {
            return Record(new CapabilityState.Unavailable(new CapabilityReason(
                VideoReasonCodes.VideoPresenceDisposed, "this video surface was disposed")));
        }

        var bounds = _display();
        if (bounds is not { } placement)
        {
            return Record(new CapabilityState.Unavailable(new CapabilityReason(
                VideoReasonCodes.VideoNoDisplay,
                "the operating system reports no display to put a video surface on, so no clip was opened")));
        }

        var open = _clips.Open(clipPath, out var clip);
        if (open is not CapabilityState.Available || clip is null)
        {
            clip?.Dispose();
            return Record(open);
        }

        lock (_gate)
        {
            _presence ??= _presenceFactory();
        }

        var present = _presence!.Present(new VideoSurfaceRequest(placement));
        if (present is not CapabilityState.Available)
        {
            clip.Dispose();
            return Record(present);
        }

        var first = clip.ReadFrame();
        if (first is null)
        {
            // The container parsed and produced nothing. The surface is up and empty, so it comes
            // straight back down: a black rectangle over the user's desktop with no picture in it is
            // strictly worse than no rectangle at all.
            _presence.Withdraw();
            clip.Dispose();
            return Record(new CapabilityState.Unavailable(new CapabilityReason(
                VideoReasonCodes.VideoClipHasNoPicture,
                $"the operating system opened '{Path.GetFileName(clipPath)}' and produced no picture from it")));
        }

        var shown = _presence.Show(first);
        if (shown is not CapabilityState.Available)
        {
            _presence.Withdraw();
            clip.Dispose();
            return Record(shown);
        }

        lock (_gate)
        {
            _clip = clip;
            _onEnded = onEnded;
            _startedAt = _clock.UtcNow;
            _maxLength = maxLength;
            _frameInterval = clip.Info.FrameInterval > TimeSpan.Zero
                ? clip.Info.FrameInterval
                : FallbackFrameInterval;
        }

        PlayingClip = clipPath;
        ScheduleAdvance();
        return Record(shown);
    }

    /// <inheritdoc/>
    public void End()
    {
        IDisposable? advance;
        IVideoClip? clip;
        IVideoPresence? presence;
        lock (_gate)
        {
            advance = _advance;
            _advance = null;
            clip = _clip;
            _clip = null;
            _onEnded = null;
            presence = _presence;
        }

        advance?.Dispose();
        clip?.Dispose();
        if (presence?.IsPresenting == true)
        {
            presence.Withdraw();
        }

        PlayingClip = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        End();

        IVideoPresence? presence;
        lock (_gate)
        {
            presence = _presence;
            _presence = null;
        }

        presence?.Dispose();
    }

    /// <summary>
    /// Where the surface goes when nobody says otherwise: centred on the primary display at
    /// <see cref="WidthFraction"/> x <see cref="HeightFraction"/> of it. The display comes from the
    /// overlay capability's own enumeration, which is CONSUMED and never edited
    /// (<c>Overlay/OverlayDisplays.cs</c>).
    /// </summary>
    private static VideoBounds? DefaultPlacement()
    {
        var displays = Overlay.OverlayDisplays.Enumerate();
        if (displays.Count == 0)
        {
            // No display at all. Null rather than a minimum rectangle: the module's own dot and its
            // arm result both read CanReachADisplay, and a one-pixel surface would be a picture
            // nobody could see reported as a picture.
            return null;
        }

        var chosen = 0;
        for (var i = 0; i < displays.Count; i++)
        {
            if (displays[i].IsPrimary)
            {
                chosen = i;
                break;
            }
        }

        var bounds = displays[chosen].Bounds;
        var width = Math.Max(1, (int)(bounds.Width * WidthFraction));
        var height = Math.Max(1, (int)(bounds.Height * HeightFraction));
        return new VideoBounds(
            bounds.X + ((bounds.Width - width) / 2),
            bounds.Y + ((bounds.Height - height) / 2),
            width,
            height);
    }

    private CapabilityState Record(CapabilityState state)
    {
        LastPlacement = state;
        return state;
    }

    private void ScheduleAdvance()
    {
        TimeSpan due;
        lock (_gate)
        {
            if (_disposed || _clip is null)
            {
                return;
            }

            due = _frameInterval;
            _advance?.Dispose();
            _advance = _clock.Schedule(due, () => _dispatch(Advance));
        }
    }

    /// <summary>One frame comes due. Runs on the surface thread.</summary>
    private void Advance()
    {
        IVideoClip? clip;
        IVideoPresence? presence;
        Action? ended;
        bool capped;
        lock (_gate)
        {
            if (_disposed || _clip is null || _advance is null)
            {
                return;
            }

            clip = _clip;
            presence = _presence;
            ended = _onEnded;
            capped = _maxLength > TimeSpan.Zero && _clock.UtcNow - _startedAt >= _maxLength;
        }

        if (capped || presence is null)
        {
            Finish(ended);
            return;
        }

        var frame = clip!.ReadFrame();
        if (frame is null)
        {
            if (clip.Ended)
            {
                Finish(ended);
                return;
            }

            // A stream tick or a format-change notification carries no picture. Not an end and not a
            // fault; ask again on the next beat rather than tearing the clip down.
            ScheduleAdvance();
            return;
        }

        var shown = presence.Show(frame);
        Record(shown);
        if (shown is not CapabilityState.Available)
        {
            // The surface stopped holding the picture — occluded, hidden, or the OS's own copy no
            // longer carries it. Upstream's answer to a video that is no longer on screen is to end
            // it and let the scheduler bring the next one round (VideoService.cs:5514-5519 for the
            // cap path, which is the same teardown).
            Finish(ended);
            return;
        }

        ScheduleAdvance();
    }

    private void Finish(Action? ended)
    {
        End();
        try
        {
            ended?.Invoke();
        }
        catch (Exception)
        {
            // A caller's continuation must never take the surface thread down with it; the module's
            // own signal boundary is where faults belong.
        }
    }
}
