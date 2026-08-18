using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// Where a flash goes when it has somewhere to go. The effect hands this the paths it drew; this
/// puts them on overlay surfaces and takes them away again.
/// </summary>
public interface IFlashSurface
{
    /// <summary>
    /// Show one flash's images. Called on the surface thread, from inside the effect's existing UI
    /// projection. Never throws: a flash that cannot be shown is a recorded outcome, because the
    /// schedule behind it must keep running whatever the screen can or cannot do.
    /// </summary>
    void Show(IReadOnlyList<string> paths);

    /// <summary>
    /// Take everything off the screen now. WPF closes every live flash window when the engine
    /// stops (<c>Services/Flash/FlashService.cs:3878-3884</c>), and that is the half of stop a user
    /// can actually see.
    /// </summary>
    void HideAll();
}

/// <summary>
/// The consumer SP-099 was built for: Flash Images, drawn on real overlay surfaces.
///
/// <para><b>One surface per image, as WPF spawns one window per image</b>
/// (<c>FlashService.cs:1110-1140</c>), staggered by <see cref="StaggerMilliseconds"/>
/// (<c>:1112</c>), each living <see cref="FlashDurationSeconds"/> plus
/// <see cref="LifetimeGraceMilliseconds"/> (<c>:1073</c>) and then leaving. Surfaces are pooled and
/// reused across flashes; the cap is WPF's <c>MAX_CONCURRENT_FLASH</c> of ten (<c>:50</c>), which
/// exists upstream for the per-flash layered-window path — the exact path this uses.</para>
///
/// <para><b>Present is not a frame path.</b> <see cref="IOverlayPresence.Present"/> walks the OS's
/// whole top-level z-order and asks the window manager's hit test twice; it is called ONCE per
/// surface per flash, at a cadence bounded below by the schedule's own three-second floor. Content
/// goes through <see cref="IOverlayPresence.Paint"/>, which does none of that. There is no render
/// loop here and no animation: a flash is one frame that lives for its lifetime.</para>
///
/// <para><b><see cref="IOverlayPresence.IsPresenting"/> is never consulted.</b> It is a latch over
/// the last operation's outcome, not a live fact about the screen. Every show re-presents its
/// surface, including a recycled one, and the presenter tracks its own slots.</para>
///
/// <para><b>Topmost is re-asserted on a cadence</b>, because the band is contested and the port
/// measured a contender on the developer's own desktop (SP-100 record §1: the window that owned the
/// point was the shipping WPF product). WPF re-raises every live flash window about once a second
/// (<c>:206-243</c>); so does this, through the injected clock, for exactly as long as a surface is
/// up — never on a wall clock, and never when nothing is showing.</para>
///
/// <para><b>Threading.</b> A native window belongs to the thread that created it. Every touch of a
/// presence happens on the thread that first showed one; clock callbacks are marshalled back to it
/// through the injected dispatch. Nothing here blocks that thread on anything.</para>
/// </summary>
public sealed class FlashSurfacePresenter : IFlashSurface, IDisposable
{
    /// <summary>WPF's <c>MAX_CONCURRENT_FLASH</c> (<c>FlashService.cs:50</c>): the classic
    /// per-flash layered-window path is capped at ten because of "memory explosion / compositor
    /// backup from too many concurrent flash windows" (<c>:1174-1176</c>). This IS that path.</summary>
    public const int MaxConcurrentSurfaces = 10;

    /// <summary>WPF staggers the windows of one flash by 300 ms each (<c>:1112</c>).</summary>
    public const int StaggerMilliseconds = 300;

    /// <summary>WPF's <c>FlashDuration</c> default, in seconds (<c>AppSettings.cs:926-931</c>). The
    /// dial itself is not ported (SP-098 divergence D49), so its shipped default is the constant.</summary>
    public const int FlashDurationSeconds = 5;

    /// <summary>WPF's per-window lifetime is the duration plus one second (<c>:1073</c>).</summary>
    public const int LifetimeGraceMilliseconds = 1000;

    /// <summary>WPF's <c>ImageScale</c> default (<c>AppSettings.cs:838-850</c>), same reasoning.</summary>
    public const int ImageScalePercent = 100;

    /// <summary>WPF's <c>FlashOpacity</c> default, 100 % (<c>AppSettings.cs:852-861</c>).</summary>
    public const int OpacityPercent = 100;

    /// <summary>WPF's <c>RaiseAllToFront</c> cadence (<c>:206-243</c>).</summary>
    public static readonly TimeSpan TopmostCadence = TimeSpan.FromSeconds(1);

    /// <summary>How long one surface stays up: WPF's <c>lifetimeMs</c> (<c>:1073</c>).</summary>
    public static readonly TimeSpan SurfaceLifetime =
        TimeSpan.FromSeconds(FlashDurationSeconds) + TimeSpan.FromMilliseconds(LifetimeGraceMilliseconds);

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly Func<IOverlayPresence> _presenceFactory;
    private readonly IFlashFrameSource _frames;
    private readonly Func<OverlayBounds?> _display;
    private readonly Random _random;
    private readonly List<Slot> _slots = [];
    private readonly List<IDisposable> _pending = [];

    private IDisposable? _cadence;
    private bool _disposed;

    /// <param name="clock">The session clock: the stagger, the lifetime and the topmost cadence all
    /// ride it, so a test drives every one of them without a wall-clock wait.</param>
    /// <param name="dispatch">Marshals a clock callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds one overlay presence. Called lazily, on the surface
    /// thread, only when a surface is really needed — a session that never flashes creates no
    /// window, and a build with no overlay creates the refusal instead.</param>
    /// <param name="frames">Turns a path into pixels.</param>
    /// <param name="display">Where surfaces may go; null when the OS reports no display.</param>
    /// <param name="random">Placement rolls.</param>
    public FlashSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        IFlashFrameSource frames,
        Func<OverlayBounds?> display,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(presenceFactory);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(display);
        _clock = clock;
        _dispatch = dispatch;
        _presenceFactory = presenceFactory;
        _frames = frames;
        _display = display;
        _random = random ?? new Random();
    }

    /// <summary>The product composition: the real overlay backend for this platform, the GDI+
    /// frame source, and the OS's own primary display.</summary>
    public static FlashSurfacePresenter Product(ISessionClock clock, Action<Action> dispatch, Random? random = null) =>
        new(clock, dispatch, OverlayPresenceFactory.Create, new GdiPlusFlashFrameSource(),
            static () => OverlayDisplays.Enumerate() is [var primary, ..] ? primary.Bounds : null,
            random);

    /// <summary>How many surfaces this presenter believes are on screen right now. Its OWN
    /// bookkeeping, never <see cref="IOverlayPresence.IsPresenting"/>.</summary>
    public int LiveSurfaces
    {
        get
        {
            var live = 0;
            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Live)
                {
                    live++;
                }
            }

            return live;
        }
    }

    /// <summary>Surfaces this presenter has ever placed. Diagnostics and facts; never a claim.</summary>
    public int SurfacesShown { get; private set; }

    /// <summary>Images a flash drew that produced no pixels — missing, corrupt, or a format this
    /// build cannot decode. WPF's own normal case (<c>LoadImagesUntilAsync</c>).</summary>
    public int UndecodableImages { get; private set; }

    /// <summary>The last placement outcome, verbatim. This is where "there is no overlay on this
    /// platform" is visible to a caller: it is a typed <see cref="CapabilityState.Unavailable"/>
    /// carrying the reason code and the manual gate, not a silence.</summary>
    public CapabilityState? LastPresent { get; private set; }

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint { get; private set; }

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw { get; private set; }

    /// <inheritdoc/>
    public void Show(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_disposed || paths.Count == 0)
        {
            return;
        }

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (i == 0)
            {
                // WPF spawns the first window of a flash synchronously and defers the rest
                // (FlashService.cs:1114-1121): the flash starts the moment it comes due.
                ShowOne(path);
                continue;
            }

            var pending = new PendingShow();
            _pending.Add(pending);
            pending.Handle = _clock.Schedule(
                TimeSpan.FromMilliseconds((long)StaggerMilliseconds * i),
                () => _dispatch(() =>
                {
                    // Off the list before the work: a spent one-shot that stays in the list is a
                    // slow leak across a session's worth of flashes, and HideAll would spend its
                    // time disposing handles that fired minutes ago.
                    _pending.Remove(pending);
                    ShowOne(path);
                }));
        }
    }

    /// <inheritdoc/>
    public void HideAll()
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            _pending[i].Dispose();
        }

        _pending.Clear();

        for (var i = 0; i < _slots.Count; i++)
        {
            Retire(_slots[i]);
        }

        _cadence?.Dispose();
        _cadence = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HideAll();
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].Presence.Dispose();
        }

        _slots.Clear();
    }

    private void ShowOne(string path)
    {
        if (_disposed)
        {
            return;
        }

        var display = _display();
        if (display is null)
        {
            // No display is not a failure of this code and not something to guess around: WPF
            // enumerates monitors and places per monitor (FlashService.cs:2204-2245); with none
            // enumerated there is nowhere a surface could legally go. It must not be SILENT
            // either, so the reason is recorded — and on a platform whose backend refuses
            // outright, the backend's own refusal (which names the route and the manual gate) is
            // the better answer, so it is asked for. Withdrawing a presence that has presented
            // nothing costs nothing, changes nothing, and is the one query that returns it.
            LastPresent = AcquireSlot() is { } probe
                && probe.Presence.Withdraw() is CapabilityState.Unavailable refusal
                && refusal.Reason.Code == OverlayReasonCodes.OverlayMechanismAbsent
                ? refusal
                : new CapabilityState.Unavailable(new CapabilityReason(
                    OverlayReasonCodes.OverlayNoDisplay,
                    "the operating system enumerated no display, so there is no rectangle a flash could legally "
                    + "be placed on and nothing was attempted"));
            return;
        }

        if (LiveSurfaces >= MaxConcurrentSurfaces)
        {
            return;
        }

        var monitor = display.Value;
        var frame = _frames.Render(
            path,
            (sourceWidth, sourceHeight) =>
                FlashGeometry.Size(sourceWidth, sourceHeight, monitor.Width, monitor.Height, ImageScalePercent));

        if (frame is null)
        {
            UndecodableImages++;
            return;
        }

        var slot = AcquireSlot();
        if (slot is null)
        {
            return;
        }

        var placement = FlashGeometry.Place(monitor, frame.Width, frame.Height, LiveRects(), _random);

        // Click-through, which is WPF's WS_EX_TRANSPARENT arm (:3668). WPF's other arm exists to
        // serve pop / hydra / XP mechanics this port does not have, and a surface that catches
        // clicks it does nothing with would swallow the user's input — the exact desktop-breaking
        // failure OverlayInputNotPassingThrough exists to refuse (SP-100 divergence).
        var request = new OverlaySurfaceRequest(placement, OpacityPercent / 100.0, ClickThrough: true);

        // ONCE per surface per flash. Never per frame, and never as a way to change content.
        var present = slot.Presence.Present(request);
        LastPresent = present;
        if (present is not CapabilityState.Available)
        {
            return;
        }

        var paint = slot.Presence.Paint(frame);
        LastPaint = paint;
        if (paint is not CapabilityState.Available)
        {
            // A surface the OS confirms is on screen and does NOT hold the frame is worse than no
            // surface: it is a rectangle of nothing over the user's work. Take it back off.
            LastWithdraw = slot.Presence.Withdraw();
            return;
        }

        slot.Live = true;
        slot.Bounds = placement;
        SurfacesShown++;

        slot.Lifetime?.Dispose();
        slot.Lifetime = _clock.Schedule(SurfaceLifetime, () => _dispatch(() => Retire(slot)));
        EnsureCadence();
    }

    /// <summary>
    /// A free slot, or a new one up to the cap. Presences are REUSED across flashes: each carries a
    /// registered window class and a top-level window, and creating a pair per image per flash
    /// would churn both for the life of a session. WPF pools its windows too, for a different
    /// reason it names (<c>:3576-3607</c>, a WPF render-thread hazard this path cannot have —
    /// SP-099 divergence D54).
    /// </summary>
    private Slot? AcquireSlot()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].Live)
            {
                return _slots[i];
            }
        }

        if (_slots.Count >= MaxConcurrentSurfaces)
        {
            return null;
        }

        // Lazily, on the surface thread: the window belongs to whoever creates it, and a session
        // that never flashes must never create one.
        var slot = new Slot(_presenceFactory());
        _slots.Add(slot);
        return slot;
    }

    private void Retire(Slot slot)
    {
        slot.Lifetime?.Dispose();
        slot.Lifetime = null;
        if (!slot.Live)
        {
            return;
        }

        slot.Live = false;
        LastWithdraw = slot.Presence.Withdraw();
    }

    private List<OverlayBounds> LiveRects()
    {
        var rects = new List<OverlayBounds>(_slots.Count);
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Live)
            {
                rects.Add(_slots[i].Bounds);
            }
        }

        return rects;
    }

    /// <summary>
    /// One re-assertion of the topmost band per live surface, once a second, for as long as
    /// anything is up — WPF's <c>RaiseAllToFront</c> (<c>:206-243</c>). It re-arms itself only
    /// while a surface is showing, so a stopped session leaves no timer behind, which is the
    /// property SP-098's stop facts are built on.
    /// </summary>
    private void EnsureCadence()
    {
        if (_cadence is not null || _disposed)
        {
            return;
        }

        _cadence = _clock.Schedule(TopmostCadence, () => _dispatch(OnCadence));
    }

    private void OnCadence()
    {
        _cadence = null;
        var anyLive = false;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Live)
            {
                anyLive = true;
                _slots[i].Presence.Reassert();
            }
        }

        if (anyLive)
        {
            EnsureCadence();
        }
    }

    private sealed class PendingShow : IDisposable
    {
        public IDisposable? Handle { get; set; }

        public void Dispose() => Handle?.Dispose();
    }

    private sealed class Slot(IOverlayPresence presence)
    {
        public IOverlayPresence Presence { get; } = presence;

        public bool Live { get; set; }

        public OverlayBounds Bounds { get; set; }

        public IDisposable? Lifetime { get; set; }
    }
}
