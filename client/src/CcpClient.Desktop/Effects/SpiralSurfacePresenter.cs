using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>Where the Spiral Overlay's frames go when they have somewhere to go.</summary>
public interface ISpiralSurface
{
    /// <summary>
    /// Put the spiral up, or re-open/re-tint the one that is already up, and say what the operating
    /// system answered. Called on the surface thread, from inside the module's arm.
    /// </summary>
    CapabilityState Engage(string spiralPath, SpiralPresentation presentation);

    /// <summary>Take the spiral off the screen now, and stop advancing it. WPF's
    /// <c>StopSpiral</c> (<c>Services/Notifications/OverlayService.cs:430-441</c>).</summary>
    void Withdraw();

    /// <summary>True while a surface this presenter placed is up.</summary>
    bool Showing { get; }

    /// <summary>
    /// True while this module's work is genuinely running — <b>and this is the property the whole
    /// packet turns on</b>. It is not <see cref="Showing"/>: a spiral that is on screen and has
    /// stopped advancing is a frozen picture, and a dot that called that healthy would be the
    /// confident half-truth <see cref="EffectDotState"/> exists to refuse. See
    /// <see cref="SpiralSurfacePresenter.Running"/> for the three clauses and why the third one is
    /// conditional.
    /// </summary>
    bool Running { get; }

    /// <summary>True while there is anything to release — a surface, a cadence, or an open clip.
    /// The module's <c>ReleaseWork</c> tests this rather than <see cref="Showing"/>, because a
    /// spiral whose repaint failed is not showing and still holds an OPEN CLIP (its cadence handle
    /// is already null by then; the decoder is what would leak).</summary>
    bool Engaged { get; }

    /// <summary>How many frames the open clip has, or 0 when none is open. One means a still image,
    /// which is a legitimate spiral that never moves.</summary>
    int FrameCount { get; }

    /// <summary>The last thing the OS said when this surface tried to place the spiral, verbatim, or
    /// null before anything has been attempted.</summary>
    CapabilityState? LastPlacement { get; }
}

/// <summary>
/// The Spiral Overlay, drawn on a real overlay surface — <b>the port's first module whose picture
/// has to keep changing while it is on</b>.
///
/// <para><b>The fourth consumer of <see cref="OverlaySurfaceSet"/>, and the one that used the frame
/// path.</b> Flash Images places up to ten rectangles for six seconds; Subliminals places one
/// full-screen card for a fifth of a second; Pink Filter places one full-screen rectangle and leaves
/// it there. This places one full-screen rectangle, leaves it there, <b>and repaints it twenty times
/// a second for the length of the session</b>. Exactly one thing had to be added to the shared set
/// for it — <see cref="OverlaySurfaceSet.Repaint"/>, a paint with no present — and that is the same
/// grade of addition made for the nullable lifetime.</para>
///
/// <para><b>Where the cadence lives, and why that is the answer to this packet's question.</b> The
/// frame advance is a timer, and it is on the injected <see cref="ISessionClock"/> — but it is
/// <b>here</b>, in the presenter, and not in the module. Pink Filter set that precedent for exactly the
/// same reason and a reader can check it in one line: <see cref="PinkFilterSurfacePresenter"/> takes
/// a clock for the topmost cadence and <see cref="PinkFilterEffect"/> takes none
/// (<c>Session/SessionParticipant.cs:116-119</c>). The distinction is not stylistic. An
/// <b>interval that decides when a MODULE is due</b> is the module's business and belongs to
/// <see cref="PacedSessionEffect{TFiring}"/>; a <b>cadence that keeps a SURFACE correct</b> —
/// reclaiming the topmost band, or turning the spiral — is the surface's, and a module that is
/// simply <i>on</i> has neither an interval nor a firing. So <see cref="SpiralOverlayEffect"/>
/// takes no clock, implements no <c>NextInterval</c>, and the scheduler
/// <see cref="OwnedSessionEffect"/> exists to remove does not come back.</para>
///
/// <para><b>Two cadences, one clock, and they are not the same cadence.</b> The topmost kick is
/// WPF's 5 s (<c>OverlayService.cs:666-671</c>, ten ticks of its 500 ms reconcile loop) and rides
/// inside <see cref="OverlaySurfaceSet"/>; the frame advance is the GIF's own delay
/// (<see cref="SpiralFrameDelay"/>, WPF's <c>_gifFrameTimer</c> at <c>:1372-1377</c>). A test drives
/// both by hand and neither is a wall-clock wait.</para>
/// </summary>
public sealed class SpiralSurfacePresenter : ISpiralSurface, IDisposable
{
    /// <summary>One spiral at a time. WPF places one window per resolved screen
    /// (<c>OverlayService.cs:1358-1367</c>); the port places on the primary display only, the same
    /// single-display limit the other three modules already carry (D66).</summary>
    public const int MaxConcurrentSurfaces = 1;

    /// <summary>WPF's periodic unconditional topmost kick for the persistent overlays: 10 ticks of
    /// its 500 ms reconcile loop (<c>OverlayService.cs:666-671</c>). The same constant the tint
    /// uses, for the same reason — a layer that is up for a whole session loses the band.</summary>
    public static readonly TimeSpan TopmostCadence = TimeSpan.FromSeconds(5);

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly ISpiralFrameSource _frames;
    private readonly Func<OverlayBounds?> _display;
    private readonly OverlaySurfaceSet _surfaces;

    private OverlaySurfaceSet.Slot? _slot;
    private ISpiralAnimation? _animation;
    private string? _openPath;
    private OverlayBounds _openBounds;
    private IDisposable? _advance;
    private bool _lastFrameHeld;

    /// <param name="clock">The session clock. It carries the topmost cadence AND the frame advance,
    /// and nothing else; the MODULE has no clock at all.</param>
    /// <param name="dispatch">Marshals a cadence callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds the overlay presence, lazily, on the surface thread.</param>
    /// <param name="frames">The decoder seam. A build that cannot decode is an ordinary outcome.</param>
    /// <param name="display">Where the spiral may go; null when the OS reports no display.</param>
    public SpiralSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        ISpiralFrameSource frames,
        Func<OverlayBounds?> display)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(display);
        _clock = clock;
        _dispatch = dispatch;
        _frames = frames;
        _display = display;
        _surfaces = new OverlaySurfaceSet(
            clock, dispatch, presenceFactory, MaxConcurrentSurfaces, TopmostCadence);
    }

    /// <summary>The product composition: the real overlay backend for this platform, the GDI+
    /// decoder, and the OS's own primary display.</summary>
    public static SpiralSurfacePresenter Product(ISessionClock clock, Action<Action> dispatch) =>
        new(clock, dispatch, OverlayPresenceFactory.Create, new GdiPlusSpiralFrameSource(),
            static () => OverlayDisplays.Enumerate() is [var primary, ..] ? primary.Bounds : null);

    /// <inheritdoc/>
    public bool Showing => _surfaces.LiveSurfaces > 0;

    /// <summary>
    /// <b>The moving module's answer to "is the work really running", in three clauses.</b>
    ///
    /// <list type="number">
    /// <item>A surface this presenter placed is up. (Pink Filter's whole answer — the SCREEN.)</item>
    /// <item>The last frame it painted was really held by the OS. A repaint that failed has already
    /// taken the surface down (<see cref="OverlaySurfaceSet.Repaint"/>), so this clause is what
    /// covers a frame the DECODER could not produce: the picture is still up and has stopped
    /// changing.</item>
    /// <item>And, <b>only if this clip has more than one frame</b>, the next advance is on the
    /// clock. The condition is not a softening: WPF starts no frame timer for a one-frame spiral
    /// (<c>OverlayService.cs:1370</c>), so a still image that sits there is the module working
    /// exactly as asked, and demanding motion from it would make the dot lie in the other
    /// direction.</item>
    /// </list>
    ///
    /// <para>So "on screen but frozen" is <b>two</b> states here, and only one of them is healthy.
    /// That is what a paced module's <c>Live</c> (a claim about the CLOCK) and a static continuous
    /// module's (a claim about the SCREEN) both had no way to express.</para>
    /// </summary>
    public bool Running =>
        Showing && _lastFrameHeld && (_animation is not { FrameCount: > 1 } || _advance is not null);

    /// <inheritdoc/>
    public bool Engaged => Showing || _advance is not null || _animation is not null;

    /// <inheritdoc/>
    public int FrameCount => _animation?.FrameCount ?? 0;

    /// <inheritdoc/>
    public CapabilityState? LastPlacement => _surfaces.LastPresent;

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint => _surfaces.LastPaint;

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw => _surfaces.LastWithdraw;

    /// <summary>Frames this presenter has put on screen since it was constructed — placements and
    /// repaints together. Diagnostics and facts; never a claim.</summary>
    public int FramesShown { get; private set; }

    /// <summary>Which frame of the clip is believed to be up. WPF's <c>_currentGifFrameIndex</c>.</summary>
    public int FrameIndex { get; private set; }

    /// <summary>The spiral file currently open, or null when none is. What the module asked for,
    /// not what the screen holds.</summary>
    public string? CurrentPath => _openPath;

    /// <inheritdoc/>
    public CapabilityState Engage(string spiralPath, SpiralPresentation presentation)
    {
        ArgumentException.ThrowIfNullOrEmpty(spiralPath);

        if (_surfaces.Disposed)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                OverlayReasonCodes.OverlayPresenceDisposed,
                "the spiral's overlay surfaces have been disposed, so no frame was placed"));
        }

        // Defensive, and it must be a refusal rather than an exception: OverlaySurfaceRequest
        // REFUSES to construct an invisible surface (that is its whole point), so an opacity that
        // reached here at zero would throw out of an arm. The module answers this case first
        // (SpiralOverlayEffect.Engage); this is the second lock on the same door.
        if (presentation.IsInvisible)
        {
            Withdraw();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.SpiralTransparent,
                "the spiral's opacity dial is at 0 %, so there is nothing to put on screen"));
        }

        var display = _display();
        if (display is null)
        {
            return _surfaces.RecordNoDisplay();
        }

        var monitor = display.Value;
        if (!EnsureAnimation(spiralPath, monitor))
        {
            Withdraw();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.SpiralNotDecoded,
                $"this build could not decode the spiral at '{Path.GetFileName(spiralPath)}', so nothing was "
                + "drawn — the file may be missing, corrupt, or in a format with no codec here"));
        }

        var frame = _animation!.Render(FrameIndex);
        if (frame is null)
        {
            Withdraw();
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.SpiralNotDecoded,
                $"the spiral at '{Path.GetFileName(spiralPath)}' opened but produced no frame {FrameIndex}, so "
                + "nothing was drawn"));
        }

        // Click-through, and the whole display: WPF's spiral window is
        // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED, topmost,
        // non-activating and explicitly IsHitTestVisible=false, sized to the screen's own bounds
        // (OverlayService.cs:1709-1734). A full-screen layer that caught clicks would swallow every
        // gesture the user made for the whole session.
        var request = new OverlaySurfaceRequest(monitor, presentation.Opacity, ClickThrough: true);

        // The SAME slot every time. Re-engaging (an opacity move, or a quick-toggle) re-places the
        // window that is already up rather than building a second one — WPF's third reconcile arm
        // (OverlayService.cs:446, UpdateSpiralOpacity), which changes the live window's opacity.
        _slot ??= _surfaces.Acquire();
        if (_slot is null)
        {
            return _surfaces.RecordNoDisplay();
        }

        // No lifetime: the spiral is up until the session takes it down (the nullable lifetime).
        // Its frames change; its presence does not expire.
        var placed = _surfaces.Place(_slot, request, frame, lifetime: null);
        _lastFrameHeld = placed;
        if (placed)
        {
            FramesShown++;
            ArmAdvance();
        }

        return Outcome(placed);
    }

    /// <inheritdoc/>
    public void Withdraw()
    {
        _advance?.Dispose();
        _advance = null;
        _lastFrameHeld = false;
        FrameIndex = 0;
        _surfaces.HideAll();
        CloseAnimation();
    }

    public void Dispose()
    {
        Withdraw();
        _surfaces.Dispose();
    }

    /// <summary>
    /// One frame advance — WPF's <c>GifFrameTimer_Tick</c>
    /// (<c>Services/Notifications/OverlayService.cs:1636-1650</c>): step the index modulo the frame
    /// count, which LOOPS forever and never stops on the last frame, then show that frame.
    ///
    /// <para><b>A failed frame does not stop the cadence</b>, which is upstream's shape too — its
    /// tick catches and logs and the timer keeps running (<c>:1644-1648</c>). What changes is what
    /// the module may CLAIM: <see cref="Running"/> goes false while the picture is stuck, and comes
    /// back by itself if the next frame lands.</para>
    /// </summary>
    private void OnAdvance()
    {
        _advance = null;
        if (_surfaces.Disposed || _slot is null || !Showing || _animation is not { FrameCount: > 1 } clip)
        {
            return;
        }

        FrameIndex = (FrameIndex + 1) % clip.FrameCount;
        var frame = clip.Render(FrameIndex);
        if (frame is null)
        {
            _lastFrameHeld = false;
        }
        else
        {
            _lastFrameHeld = _surfaces.Repaint(_slot, frame);
            if (_lastFrameHeld)
            {
                FramesShown++;
            }
        }

        // Re-armed only while something is still up, so a stopped session leaves no timer behind —
        // the property the stop facts are built on, and the same rule OverlaySurfaceSet's own
        // topmost cadence follows.
        if (Showing)
        {
            ArmAdvance();
        }
    }

    /// <summary>The frame cadence, armed only for a clip that really has more than one frame
    /// (<c>OverlayService.cs:1370</c>: <c>if (_spiralGifFrames.Count &gt; 1 …)</c>).</summary>
    private void ArmAdvance()
    {
        if (_advance is not null || _surfaces.Disposed || _animation is not { FrameCount: > 1 } clip)
        {
            return;
        }

        _advance = _clock.Schedule(clip.FrameDelay, () => _dispatch(OnAdvance));
    }

    /// <summary>
    /// Open the clip if the file or the display geometry changed, and leave the open one alone
    /// otherwise. Re-opening on every engage would re-decode the whole file every time the opacity
    /// slider moved.
    /// </summary>
    private bool EnsureAnimation(string spiralPath, OverlayBounds monitor)
    {
        if (_animation is not null
            && string.Equals(_openPath, spiralPath, StringComparison.Ordinal)
            && _openBounds.Width == monitor.Width
            && _openBounds.Height == monitor.Height)
        {
            return true;
        }

        CloseAnimation();
        FrameIndex = 0;
        _animation = _frames.Open(spiralPath, monitor.Width, monitor.Height);
        if (_animation is null)
        {
            return false;
        }

        _openPath = spiralPath;
        _openBounds = monitor;
        return true;
    }

    private void CloseAnimation()
    {
        _animation?.Dispose();
        _animation = null;
        _openPath = null;
    }

    /// <summary>
    /// What the OS said, verbatim, for the placement that just ran — the same three-arm rule
    /// <see cref="PinkFilterSurfacePresenter"/> uses and for the same reason: a surface that went up
    /// and then failed to hold its frame has already been withdrawn, so returning the present's
    /// <c>Available</c> would claim a spiral that is not there.
    /// </summary>
    private CapabilityState Outcome(bool placed)
    {
        if (placed && _surfaces.LastPresent is { } confirmed)
        {
            return confirmed;
        }

        if (_surfaces.LastPresent is CapabilityState.Available && _surfaces.LastPaint is { } paint)
        {
            return paint;
        }

        if (_surfaces.LastPresent is { } refusal)
        {
            return refusal;
        }

        // Unreachable: Place records a present outcome on every path. Kept rather than a null
        // suppression, because a suppression here would be a claim about another class's invariant
        // with nothing behind it.
        return new CapabilityState.Unavailable(new CapabilityReason(
            OverlayReasonCodes.OverlayNothingPresented,
            "the spiral's surface recorded no placement outcome at all, so nothing is claimed"));
    }
}
