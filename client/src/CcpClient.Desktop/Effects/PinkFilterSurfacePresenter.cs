using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>Where the Pink Filter's tint goes when it has somewhere to go.</summary>
public interface IPinkFilterSurface
{
    /// <summary>
    /// Put the tint up, or re-tint the one that is already up, and say what the operating system
    /// answered. Called on the surface thread — a native window belongs to the thread that created
    /// it — from inside the module's arm, which is a gesture and therefore always the UI thread on
    /// a product path.
    ///
    /// <para>The return is the OS's own verbatim outcome, never a bool: this is the module's ONLY
    /// evidence that anything is on screen, and it is what its dot and its arm result are both
    /// derived from.</para>
    /// </summary>
    CapabilityState Engage(PinkFilterTint tint);

    /// <summary>Take the tint off the screen now. WPF's <c>StopPinkFilter</c>
    /// (<c>Services/Notifications/OverlayService.cs:1233-1249</c>).</summary>
    void Withdraw();

    /// <summary>True only while a tint this surface placed is up. The module's dot reads THIS —
    /// it is the continuous module's whole answer to "is the work really running".</summary>
    bool Showing { get; }

    /// <summary>The last thing the OS said when this surface tried to place a tint, verbatim, or
    /// null before anything has been attempted.</summary>
    CapabilityState? LastPlacement { get; }
}

/// <summary>
/// The Pink Filter, drawn on a real overlay surface.
///
/// <para><b>The third consumer of <see cref="OverlaySurfaceSet"/>, and the one that proved what the
/// shared part is really for.</b> Flash Images places up to ten staggered rectangles for six
/// seconds; Subliminals places one full-screen card for a fifth of a second; this places one
/// full-screen rectangle <b>and leaves it there</b>. All three want the same five things — recycle
/// the presence, present before painting, withdraw a surface whose paint failed, keep every outcome
/// verbatim, and refuse honestly when the OS enumerates no display — and exactly one thing had to
/// change for the third: a placement may have <b>no lifetime</b>
/// (<see cref="OverlaySurfaceSet.Place"/>).</para>
///
/// <para><b>Re-tinting is a re-place, not a second surface.</b> WPF's reconcile has three arms —
/// not showing and the flag is on, start it; the flag is off, stop it; showing already, update the
/// opacity in place (<c>OverlayService.cs:427-437</c>) — and the third arm mutates the existing
/// window's brush rather than making a new window (<c>:1252-1268</c>). Here the same slot is
/// re-presented and re-painted, which is the same window with new content; the presence pool means
/// no window is created for the second tint either.</para>
///
/// <para><b>The topmost cadence is REAL here, where it was optional for the other two.</b> A layer
/// that is up for a whole session loses the always-on-top band to anything that later claims it,
/// and WPF spends a periodic unconditional kick reclaiming it — every 5 seconds, ten ticks of its
/// 500 ms reconcile loop (<c>OverlayService.cs:666-673</c>). That number is this presenter's
/// cadence. WPF's <i>other</i> half — a conditional recovery pass every 500 ms that re-asserts only
/// when the band was actually lost (<c>:633-663</c>) — has no honest counterpart:
/// <see cref="IOverlayPresence.Reassert"/> confirms nothing by design and the capability exposes no
/// z-order query to condition on (divergence D79).</para>
/// </summary>
public sealed class PinkFilterSurfacePresenter : IPinkFilterSurface, IDisposable
{
    /// <summary>One tint at a time. WPF places one window per resolved screen
    /// (<c>OverlayService.cs:1149-1157</c>); the port places on the primary display only, the same
    /// single-display limit the other two modules already carry (D66).</summary>
    public const int MaxConcurrentSurfaces = 1;

    /// <summary>WPF's periodic unconditional topmost kick for the persistent overlays: 10 ticks of
    /// its 500 ms reconcile loop (<c>OverlayService.cs:666-671</c>).</summary>
    public static readonly TimeSpan TopmostCadence = TimeSpan.FromSeconds(5);

    private readonly OverlaySurfaceSet _surfaces;
    private readonly Func<OverlayBounds?> _display;

    private OverlaySurfaceSet.Slot? _slot;

    /// <param name="clock">The session clock. This module has no pacing of its own; the clock is
    /// here for the TOPMOST CADENCE and nothing else, which is why it is a constructor parameter of
    /// the presenter and not of the effect.</param>
    /// <param name="dispatch">Marshals a cadence callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds the overlay presence, lazily, on the surface thread.</param>
    /// <param name="display">Where the tint may go; null when the OS reports no display.</param>
    public PinkFilterSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        Func<OverlayBounds?> display)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _surfaces = new OverlaySurfaceSet(
            clock, dispatch, presenceFactory, MaxConcurrentSurfaces, TopmostCadence);
    }

    /// <summary>The product composition: the real overlay backend for this platform and the OS's own
    /// primary display. There is no frame source — the payload is one colour, so
    /// <see cref="OverlayFrame.Solid"/> is the whole rasteriser.</summary>
    public static PinkFilterSurfacePresenter Product(ISessionClock clock, Action<Action> dispatch) =>
        new(clock, dispatch, OverlayPresenceFactory.Create,
            static () => OverlayDisplays.Enumerate() is [var primary, ..] ? primary.Bounds : null);

    /// <inheritdoc/>
    public bool Showing => _surfaces.LiveSurfaces > 0;

    /// <inheritdoc/>
    public CapabilityState? LastPlacement => _surfaces.LastPresent;

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint => _surfaces.LastPaint;

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw => _surfaces.LastWithdraw;

    /// <summary>Placements this presenter has made — one per engage that reached the screen, so a
    /// re-tint counts. Diagnostics and facts; never a claim.</summary>
    public int SurfacesShown => _surfaces.SurfacesShown;

    /// <summary>The tint currently believed to be up, or null when nothing is. What the module asked
    /// for, not what the screen holds.</summary>
    public PinkFilterTint? CurrentTint { get; private set; }

    /// <inheritdoc/>
    public CapabilityState Engage(PinkFilterTint tint)
    {
        if (_surfaces.Disposed)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                OverlayReasonCodes.OverlayPresenceDisposed,
                "the pink filter's overlay surfaces have been disposed, so no tint was placed"));
        }

        var display = _display();
        if (display is null)
        {
            CurrentTint = null;
            return _surfaces.RecordNoDisplay();
        }

        var monitor = display.Value;

        // Click-through, and the whole display: WPF's tint window is
        // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED, topmost,
        // non-activating and explicitly IsHitTestVisible=false, sized to the screen's own bounds
        // (OverlayService.cs:1186-1215). A full-screen tint that caught clicks would swallow every
        // gesture the user made for the whole session, which is the failure
        // OverlayInputNotPassingThrough exists to refuse.
        var request = new OverlaySurfaceRequest(monitor, tint.Opacity, ClickThrough: true);
        var frame = OverlayFrame.Solid(monitor.Width, monitor.Height, tint.Blue, tint.Green, tint.Red);

        // The SAME slot every time. A re-tint is a re-place on the window that is already up, which
        // is WPF's third reconcile arm (OverlayService.cs:434-437 -> :1252-1268): it changes the
        // brush on the live window rather than building a second one.
        _slot ??= _surfaces.Acquire();
        if (_slot is null)
        {
            CurrentTint = null;
            return _surfaces.RecordNoDisplay();
        }

        // No lifetime: the tint is up until the session takes it down. That is the ONE thing a
        // continuous module needed from the shared surface set that the two paced ones did not.
        var placed = _surfaces.Place(_slot, request, frame, lifetime: null);
        CurrentTint = placed ? tint : null;
        return Outcome(placed);
    }

    /// <summary>
    /// What the OS said, verbatim, for the placement that just ran. <see cref="OverlaySurfaceSet.Place"/>
    /// always records a present outcome and records a paint outcome only after the present was
    /// <see cref="CapabilityState.Available"/>, so the refusal a caller wants is the PAINT's whenever
    /// the surface went up and then failed to hold the frame — the set has already withdrawn it in
    /// that case, and returning the present's <c>Available</c> would claim a tint that is not there.
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
            "the pink filter's surface recorded no placement outcome at all, so nothing is claimed"));
    }

    /// <inheritdoc/>
    public void Withdraw()
    {
        CurrentTint = null;
        _surfaces.HideAll();
    }

    public void Dispose()
    {
        CurrentTint = null;
        _surfaces.Dispose();
    }
}
