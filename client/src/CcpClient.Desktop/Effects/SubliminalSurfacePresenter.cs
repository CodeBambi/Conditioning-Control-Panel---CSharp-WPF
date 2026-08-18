using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>One subliminal card as the surface needs it: what it says, how solid it is, how long it
/// stays. Composed by the effect from its dials, so the presenter reads no settings of its own.</summary>
/// <param name="Text">The phrase. It reaches the rasteriser and nothing else — never a log, never an
/// event, never the UI (the media-logging rule the port holds everywhere).</param>
/// <param name="OpacityPercent">WPF's <c>SubliminalOpacity</c> (<c>AppSettings.cs:1256</c>).</param>
/// <param name="Lifetime">How long the card is on screen, envelope included.</param>
public sealed record SubliminalCard(string Text, int OpacityPercent, TimeSpan Lifetime);

/// <summary>Where a subliminal goes when it has somewhere to go.</summary>
public interface ISubliminalSurface
{
    /// <summary>Show one card. Called on the surface thread, from inside the effect's UI projection.
    /// Never throws: a card that cannot be shown is a recorded outcome, because the schedule behind
    /// it must keep running whatever the screen can or cannot do.</summary>
    void Show(SubliminalCard card);

    /// <summary>Take the card off the screen now. WPF blanks and hides its keep-alive windows on
    /// stop (<c>Services/Subliminal/SubliminalService.cs:116-127</c>).</summary>
    void HideAll();

    /// <summary>The last thing the OS said when this surface tried to place a card, verbatim, or
    /// null before anything has been attempted.</summary>
    CapabilityState? LastPlacement { get; }
}

/// <summary>
/// Subliminals, drawn on a real overlay surface.
///
/// <para><b>How it differs from the flash presenter, and why that is the point.</b> This is the
/// second consumer of <see cref="OverlaySurfaceSet"/>, and the differences are what proved the
/// shared part was really shared: <b>one</b> card at a time, not up to ten (WPF keeps ONE keep-alive
/// window per screen and swaps its content — <c>SubliminalService.cs:26-32</c>, <c>:790-800</c>);
/// <b>full screen</b>, not a random 40 %-of-monitor rectangle (<c>:1046</c> sizes the window to the
/// screen's own bounds); <b>no stagger</b>, because a subliminal is one card and not a burst; a
/// lifetime <b>computed per show</b> from the duration dial instead of a constant; and <b>no topmost
/// cadence</b>, because the card is on screen for a fifth of a second and upstream re-asserts the
/// band once, per show, at <c>:1042-1046</c> — which is what <see cref="OverlaySurfaceSet.Place"/>
/// does anyway.</para>
///
/// <para><b>The envelope, and the divergence inside it.</b> WPF animates the window's opacity: 50 ms
/// in, <c>hold</c> at the target, 50 ms out (<c>:1253-1281</c>). This surface has one uniform alpha
/// and no frame loop — SP-099's own residual is that <c>Present</c> is wrong per frame, and SP-100
/// took the same decision for the flash fade (D60). So the card appears at its target opacity
/// immediately, stays for the WHOLE envelope, and leaves. The time it occupies is exactly WPF's
/// (<c>50 + max(100, frames*17) + 50</c> ms); what is missing is the ramp at each end.</para>
/// </summary>
public sealed class SubliminalSurfacePresenter : ISubliminalSurface, IDisposable
{
    /// <summary>One card at a time. WPF holds one keep-alive window per screen and replaces its
    /// content outright on the next show (<c>SubliminalService.cs:29-32</c>); the port places on the
    /// primary display only (the same single-display limit SP-100 recorded for flashes), so one.</summary>
    public const int MaxConcurrentSurfaces = 1;

    /// <summary>WPF's fade-in, in milliseconds (<c>SubliminalService.cs:1253</c>).</summary>
    public const int FadeInMilliseconds = 50;

    /// <summary>WPF's fade-out, in milliseconds (<c>SubliminalService.cs:1255</c>).</summary>
    public const int FadeOutMilliseconds = 50;

    private readonly OverlaySurfaceSet _surfaces;
    private readonly ISubliminalFrameSource _frames;
    private readonly Func<OverlayBounds?> _display;

    /// <param name="clock">The session clock: the card's lifetime rides it, so a test drives it with
    /// no wall-clock wait.</param>
    /// <param name="dispatch">Marshals a clock callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds the overlay presence, lazily, on the surface thread.</param>
    /// <param name="frames">Turns a phrase into pixels.</param>
    /// <param name="display">Where the card may go; null when the OS reports no display.</param>
    public SubliminalSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        ISubliminalFrameSource frames,
        Func<OverlayBounds?> display)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(display);
        _frames = frames;
        _display = display;
        // No cadence: nothing this short-lived can lose a contested band in a way a re-assertion
        // would win back, and a timer per card would outlive the card it was armed for.
        _surfaces = new OverlaySurfaceSet(clock, dispatch, presenceFactory, MaxConcurrentSurfaces, topmostCadence: null);
    }

    /// <summary>The product composition: the real overlay backend for this platform, the GDI+ text
    /// rasteriser, and the OS's own primary display.</summary>
    public static SubliminalSurfacePresenter Product(ISessionClock clock, Action<Action> dispatch) =>
        new(clock, dispatch, OverlayPresenceFactory.Create, new GdiPlusSubliminalFrameSource(),
            static () => OverlayDisplays.Enumerate() is [var primary, ..] ? primary.Bounds : null);

    /// <summary>How many cards this presenter believes are on screen right now.</summary>
    public int LiveSurfaces => _surfaces.LiveSurfaces;

    /// <summary>Cards this presenter has ever placed. Diagnostics and facts; never a claim.</summary>
    public int SurfacesShown => _surfaces.SurfacesShown;

    /// <summary>Phrases that produced no pixels — a build with no rasteriser, or a raster that
    /// failed. Counted rather than swallowed, so a card that never appears is visible in the port's
    /// own numbers instead of being indistinguishable from one nobody looked at.</summary>
    public int UnrasterisedPhrases { get; private set; }

    /// <inheritdoc/>
    public CapabilityState? LastPlacement => _surfaces.LastPresent;

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint => _surfaces.LastPaint;

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw => _surfaces.LastWithdraw;

    /// <inheritdoc/>
    public void Show(SubliminalCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (_surfaces.Disposed)
        {
            return;
        }

        var display = _display();
        if (display is null)
        {
            _surfaces.RecordNoDisplay();
            return;
        }

        var monitor = display.Value;
        var frame = _frames.Render(card.Text, monitor.Width, monitor.Height);
        if (frame is null)
        {
            UnrasterisedPhrases++;
            return;
        }

        // A card that arrives while one is still up REPLACES it, which is upstream's behaviour: the
        // keep-alive window's content is swapped and a fresh envelope starts, invalidating the
        // previous one (SubliminalService.cs:1285-1292 — the show-generation guard exists precisely
        // so the old envelope's completion cannot blank the new phrase). With a pool of one, that
        // is "retire whatever is up, then place" — and it happens AFTER the raster succeeded, so a
        // phrase this build cannot draw never takes a live card off the screen for nothing.
        _surfaces.HideAll();
        var slot = _surfaces.Acquire();
        if (slot is null)
        {
            return;
        }

        // Click-through, and full screen: WPF's card window is WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
        // sized to the screen's own physical bounds (SubliminalService.cs:1030-1046). A card that
        // caught clicks would swallow the user's input over whatever it covers for the whole
        // envelope, which is the failure OverlayInputNotPassingThrough exists to refuse.
        var request = new OverlaySurfaceRequest(monitor, card.OpacityPercent / 100.0, ClickThrough: true);
        _surfaces.Place(slot, request, frame, card.Lifetime);
    }

    /// <inheritdoc/>
    public void HideAll() => _surfaces.HideAll();

    public void Dispose() => _surfaces.Dispose();
}
