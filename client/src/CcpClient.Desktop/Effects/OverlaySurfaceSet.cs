using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// A pool of overlay surfaces and the one correct way to put a frame on one.
///
/// <para><b>Why this is shared.</b> This machinery was built inside
/// <see cref="FlashSurfacePresenter"/>. Subliminals draws through the same capability with entirely
/// different geometry — one full-screen card instead of up to ten randomly placed rectangles, no
/// stagger, a lifetime computed per show — and the second presenter would have been ninety percent
/// the first one if this had not moved. What is genuinely shared turns out to be the part that is
/// easiest to get subtly wrong and hardest to see wrong: recycling presences instead of creating a
/// window class per image, presenting BEFORE painting, withdrawing a surface whose paint failed,
/// keeping every outcome verbatim, and refusing honestly when the OS enumerates no display.</para>
///
/// <para><b>Present is not a frame path.</b> <see cref="IOverlayPresence.Present"/> walks the OS's
/// whole top-level z-order and asks the window manager's hit test twice; it is called ONCE per
/// surface per show. Content goes through <see cref="IOverlayPresence.Paint"/>, which does none of
/// that. There is no render loop here.</para>
///
/// <para><b><see cref="IOverlayPresence.IsPresenting"/> is never consulted.</b> It is a latch over
/// the last operation's outcome, not a live fact about the screen. Every show re-presents its
/// surface, including a recycled one, and this class tracks its own slots.</para>
///
/// <para><b>Threading.</b> A native window belongs to the thread that created it. Every touch of a
/// presence happens on the thread that first showed one; clock callbacks are marshalled back to it
/// through the injected dispatch. Nothing here blocks that thread on anything.</para>
/// </summary>
public sealed class OverlaySurfaceSet : IDisposable
{
    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly Func<IOverlayPresence> _presenceFactory;
    private readonly int _maxSurfaces;
    private readonly TimeSpan? _topmostCadence;
    private readonly List<Slot> _slots = [];

    private IDisposable? _cadence;

    /// <param name="clock">The session clock: per-surface lifetimes and the topmost cadence ride it,
    /// so a test drives both without a wall-clock wait.</param>
    /// <param name="dispatch">Marshals a clock callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds one overlay presence. Called lazily, on the surface
    /// thread, only when a surface is really needed — a session that never draws creates no window,
    /// and a build with no overlay creates the refusal instead.</param>
    /// <param name="maxSurfaces">How many surfaces may be on screen at once.</param>
    /// <param name="topmostCadence">How often to re-assert the topmost band while anything is up,
    /// or null for a module whose surfaces are too short-lived for the band to be contested.</param>
    public OverlaySurfaceSet(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        int maxSurfaces,
        TimeSpan? topmostCadence)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(presenceFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxSurfaces, 0);
        _clock = clock;
        _dispatch = dispatch;
        _presenceFactory = presenceFactory;
        _maxSurfaces = maxSurfaces;
        _topmostCadence = topmostCadence;
    }

    /// <summary>True once <see cref="Dispose"/> has run; every caller checks it before doing work.</summary>
    public bool Disposed { get; private set; }

    /// <summary>How many surfaces this set believes are on screen right now. Its OWN bookkeeping,
    /// never <see cref="IOverlayPresence.IsPresenting"/>.</summary>
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

    /// <summary>Surfaces this set has ever placed. Diagnostics and facts; never a claim.</summary>
    public int SurfacesShown { get; private set; }

    /// <summary>The last placement outcome, verbatim. This is where "there is no overlay on this
    /// platform" is visible to a caller: it is a typed <see cref="CapabilityState.Unavailable"/>
    /// carrying the reason code and the manual gate, not a silence.</summary>
    public CapabilityState? LastPresent { get; private set; }

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint { get; private set; }

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw { get; private set; }

    /// <summary>The rectangles currently occupied, for a caller that places around them.</summary>
    public List<OverlayBounds> LiveRects()
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
    /// A free slot, or a new one up to the cap, or null when everything is busy. Presences are
    /// REUSED across shows: each carries a registered window class and a top-level window, and
    /// creating a pair per image per flash would churn both for the life of a session. WPF pools its
    /// windows too, for a different reason it names (<c>Services/Flash/FlashService.cs:3576-3607</c>
    /// and <c>Services/Subliminal/SubliminalService.cs:26-32</c> — a WPF render-thread hazard this
    /// path cannot have, divergence D54).
    /// </summary>
    public Slot? Acquire()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!_slots[i].Live)
            {
                return _slots[i];
            }
        }

        if (_slots.Count >= _maxSurfaces)
        {
            return null;
        }

        // Lazily, on the surface thread: the window belongs to whoever creates it, and a session
        // that never draws must never create one.
        var slot = new Slot(_presenceFactory());
        _slots.Add(slot);
        return slot;
    }

    /// <summary>
    /// Put one frame on one slot for <paramref name="lifetime"/>: present, then paint, then hold.
    /// Every outcome is recorded verbatim; false means nothing is on screen for this call.
    ///
    /// <para><b><paramref name="lifetime"/> may be null, and that is the one thing this
    /// class had to learn for a CONTINUOUS module.</b> Flash Images and Subliminals each place a
    /// surface for a computed span and it retires itself; Pink Filter's layer is up from START to
    /// STOP and has no span at all — WPF's overlay service arms no timer for it, only a reconcile
    /// loop (<c>Services/Notifications/OverlayService.cs:419-452</c>). Null means "hold until
    /// something retires it", and it is deliberately not expressed as a very large
    /// <see cref="TimeSpan"/>: a lifetime of ten hours is a timer that exists, that a stop has to
    /// remember to cancel, and that would fire in a session nobody meant it to reach.</para>
    /// </summary>
    public bool Place(Slot slot, OverlaySurfaceRequest request, OverlayFrame frame, TimeSpan? lifetime)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(frame);

        // ONCE per surface per show. Never per frame, and never as a way to change content.
        var present = slot.Presence.Present(request);
        LastPresent = present;
        if (present is not CapabilityState.Available)
        {
            return false;
        }

        var paint = slot.Presence.Paint(frame);
        LastPaint = paint;
        if (paint is not CapabilityState.Available)
        {
            // A surface the OS confirms is on screen and does NOT hold the frame is worse than no
            // surface: it is a rectangle of nothing over the user's work. Take it back off.
            LastWithdraw = slot.Presence.Withdraw();
            return false;
        }

        slot.Live = true;
        slot.Bounds = request.Bounds;
        SurfacesShown++;

        slot.Lifetime?.Dispose();
        slot.Lifetime = lifetime is { } span
            ? _clock.Schedule(span, () => _dispatch(() => Retire(slot)))
            : null;
        EnsureCadence();
        return true;
    }

    /// <summary>
    /// Put a NEW frame on a slot that is already up, without re-presenting it.
    ///
    /// <para><b>This is the one thing a MOVING module needed that neither a paced nor a static one
    /// did, and it is the frame path the overlay capability already documented and nobody
    /// had used.</b> <see cref="IOverlayPresence.Present"/> walks the OS's whole top-level z-order
    /// and asks the window manager's hit test in both polarities — with click-through momentarily
    /// cleared (<c>Overlay/Win32OverlayPresence.cs:547-576</c>) — which is right once per placement
    /// and, twenty times a second, would be a full-screen window catching the user's clicks twenty
    /// times a second. <see cref="IOverlayPresence.Paint"/> touches no style, walks no z-order and
    /// asks no hit test (<c>Overlay/IOverlayPresence.cs:80-85</c>: "A caller shows a surface once
    /// and paints it"). So a frame advance is a paint, and only a paint.</para>
    ///
    /// <para>The failure rule is <see cref="Place"/>'s, deliberately identical: a surface the OS
    /// confirms is on screen and does NOT hold the frame is worse than no surface, so it comes down
    /// rather than being left holding whatever it last held. <b>A moving module's dot is derived
    /// from this</b> — a repaint that failed took the surface with it, so the module reads "not
    /// running" rather than "running, frozen".</para>
    ///
    /// <para><b>It never touches the lifetime and never re-arms the cadence.</b> A repaint changes
    /// what is on a surface, not how long it is there for; a continuous module has no lifetime to
    /// extend and a paced one's must not be extended by content.</para>
    /// </summary>
    /// <returns>False when the slot was not up, or when the paint did not hold.</returns>
    public bool Repaint(Slot slot, OverlayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(frame);

        if (!slot.Live)
        {
            return false;
        }

        var paint = slot.Presence.Paint(frame);
        LastPaint = paint;
        if (paint is CapabilityState.Available)
        {
            return true;
        }

        Retire(slot);
        return false;
    }

    /// <summary>
    /// Record that there is nowhere a surface could legally go, and return what was recorded.
    ///
    /// <para>No display is not a failure of this code and not something to guess around: WPF
    /// enumerates monitors and places per monitor (<c>FlashService.cs:2204-2245</c>); with none
    /// enumerated there is nothing to place on. It must not be SILENT either, so the reason is
    /// recorded — and on a platform whose backend refuses outright, the backend's own refusal (which
    /// names the route and the manual gate) is the better answer, so it is asked for. Withdrawing a
    /// presence that has presented nothing costs nothing, changes nothing, and is the one query that
    /// returns it.</para>
    /// </summary>
    public CapabilityState RecordNoDisplay()
    {
        LastPresent = Acquire() is { } probe
            && probe.Presence.Withdraw() is CapabilityState.Unavailable refusal
            && refusal.Reason.Code == OverlayReasonCodes.OverlayMechanismAbsent
            ? refusal
            : new CapabilityState.Unavailable(new CapabilityReason(
                OverlayReasonCodes.OverlayNoDisplay,
                "the operating system enumerated no display, so there is no rectangle an overlay could legally "
                + "be placed on and nothing was attempted"));
        return LastPresent;
    }

    /// <summary>Take one surface off the screen now. A no-op on a slot that is not up.</summary>
    public void Retire(Slot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        slot.Lifetime?.Dispose();
        slot.Lifetime = null;
        if (!slot.Live)
        {
            return;
        }

        slot.Live = false;
        LastWithdraw = slot.Presence.Withdraw();
    }

    /// <summary>Take everything off the screen and stop the cadence. The visible half of stop.</summary>
    public void HideAll()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            Retire(_slots[i]);
        }

        _cadence?.Dispose();
        _cadence = null;
    }

    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;
        HideAll();
        for (var i = 0; i < _slots.Count; i++)
        {
            _slots[i].Presence.Dispose();
        }

        _slots.Clear();
    }

    /// <summary>
    /// One re-assertion of the topmost band per live surface, on the configured cadence, for as long
    /// as anything is up — WPF's <c>RaiseAllToFront</c> (<c>FlashService.cs:206-243</c>). It re-arms
    /// itself only while a surface is showing, so a stopped session leaves no timer behind, which is
    /// the property the stop facts are built on.
    /// </summary>
    private void EnsureCadence()
    {
        if (_topmostCadence is not { } cadence || _cadence is not null || Disposed)
        {
            return;
        }

        _cadence = _clock.Schedule(cadence, () => _dispatch(OnCadence));
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

    /// <summary>One pooled surface: its presence, whether it is up, where, and until when.</summary>
    public sealed class Slot(IOverlayPresence presence)
    {
        public IOverlayPresence Presence { get; } = presence;

        public bool Live { get; internal set; }

        public OverlayBounds Bounds { get; internal set; }

        internal IDisposable? Lifetime { get; set; }
    }
}
