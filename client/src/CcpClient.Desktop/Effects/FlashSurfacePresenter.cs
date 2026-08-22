using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
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

    /// <summary>
    /// The last thing the OS said when this surface tried to place something, verbatim, or null
    /// before anything has been attempted.
    ///
    /// <para>It is on the INTERFACE because the Studio module panel has to tell the user where their
    /// flashes are going, and the only honest answer is the one the surface itself last got: an
    /// <see cref="CapabilityState.Available"/> on a Windows build with an overlay, and a typed
    /// refusal carrying its reason code and manual gate everywhere the overlay is absent. The panel
    /// used to assert the platform instead, and asserting a platform is exactly how it came to be
    /// telling Windows users something false (the record's own closing note).</para>
    /// </summary>
    CapabilityState? LastPlacement { get; }
}

/// <summary>
/// The consumer the overlay capability was built for: Flash Images, drawn on real overlay surfaces.
///
/// <para><b>One surface per image, as WPF spawns one window per image</b>
/// (<c>FlashService.cs:1110-1140</c>), staggered by <see cref="StaggerMilliseconds"/>
/// (<c>:1112</c>), each living <see cref="FlashDurationSeconds"/> plus
/// <see cref="LifetimeGraceMilliseconds"/> (<c>:1073</c>) and then leaving. Surfaces are pooled and
/// reused across flashes; the cap is WPF's <c>MAX_CONCURRENT_FLASH</c> of ten (<c>:50</c>), which
/// exists upstream for the per-flash layered-window path — the exact path this uses.</para>
///
/// <para><b>Topmost is re-asserted on a cadence</b>, because the band is contested and the port
/// measured a contender on the developer's own desktop (the packet record §1: the window that owned the
/// point was the shipping WPF product). WPF re-raises every live flash window about once a second
/// (<c>:206-243</c>); so does this, through the injected clock, for exactly as long as a surface is
/// up — never on a wall clock, and never when nothing is showing.</para>
///
/// <para><b>What moved out.</b> The slot pool, the present-then-paint sequence, the
/// withdraw-on-failed-paint rule, the verbatim outcome bookkeeping, the no-display refusal and the
/// cadence are now <see cref="OverlaySurfaceSet"/>, shared with the Subliminals card. What stays
/// here is what is genuinely a FLASH: the stagger, the placement roll, the geometry and the
/// constants. Not one call to the overlay changed order.</para>
///
/// <para><b>Three of those constants became DIALS.</b> The size, the opacity and the
/// duration are the Visuals row's three sliders (<see cref="VisualsDials"/>), and this presenter
/// pulls a <see cref="FlashDraw"/> reading for each flash instead of reading four fixed numbers.
/// The constants below stay, unchanged in value, as the DEFAULTS a build with no document draws
/// with — which is what every flash drew with before the dials existed. The reading is taken ONCE
/// per <see cref="Show"/>, never per surface, because upstream takes it once per flash
/// (<c>FlashService.cs:1028-1034</c>, <c>:655-656</c>) and this presenter staggers a flash's
/// surfaces across 300 ms each.</para>
/// </summary>
public sealed class FlashSurfacePresenter : IFlashSurface, IDisposable
{
    /// <summary>WPF's <c>MAX_CONCURRENT_FLASH</c> (<c>FlashService.cs:50</c>): the classic
    /// per-flash layered-window path is capped at ten because of "memory explosion / compositor
    /// backup from too many concurrent flash windows" (<c>:1174-1176</c>). This IS that path.</summary>
    public const int MaxConcurrentSurfaces = 10;

    /// <summary>WPF staggers the windows of one flash by 300 ms each (<c>:1112</c>).</summary>
    public const int StaggerMilliseconds = 300;

    /// <summary>WPF's <c>FlashDuration</c> DEFAULT, in seconds
    /// (<c>CCP.Core/Models/AppSettings.cs:925-930</c>). The dial itself is now ported
    /// (<see cref="VisualsDials"/>) and this is what a build with no document draws with.</summary>
    public const int FlashDurationSeconds = VisualsPresetDocument.DefaultFlashDurationSeconds;

    /// <summary>WPF's per-window lifetime is the duration plus one second (<c>:1073</c>). This is
    /// the grace, and it is NOT a dial in either build.</summary>
    public const int LifetimeGraceMilliseconds = 1000;

    /// <summary>WPF's <c>ImageScale</c> DEFAULT (<c>AppSettings.cs:839</c>), same reasoning.</summary>
    public const int ImageScalePercent = VisualsPresetDocument.DefaultImageScalePercent;

    /// <summary>WPF's <c>FlashOpacity</c> DEFAULT, 100 % (<c>AppSettings.cs:853</c>).</summary>
    public const int OpacityPercent = VisualsPresetDocument.DefaultFlashOpacityPercent;

    /// <summary>WPF's <c>RaiseAllToFront</c> cadence (<c>:206-243</c>).</summary>
    public static readonly TimeSpan TopmostCadence = TimeSpan.FromSeconds(1);

    /// <summary>How long one surface stays up AT THE DEFAULT DURATION: WPF's <c>lifetimeMs</c>
    /// (<c>:1073</c>). The live figure is <see cref="FlashDraw.Lifetime"/>, which is this arithmetic
    /// over the user's own dial.</summary>
    public static readonly TimeSpan SurfaceLifetime =
        TimeSpan.FromSeconds(FlashDurationSeconds) + TimeSpan.FromMilliseconds(LifetimeGraceMilliseconds);

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly OverlaySurfaceSet _surfaces;
    private readonly IFlashFrameSource _frames;
    private readonly Func<OverlayBounds?> _display;
    private readonly Func<FlashDraw> _draw;
    private readonly Random _random;
    private readonly IHapticLimb? _haptics;
    private readonly List<IDisposable> _pending = [];

    /// <param name="clock">The session clock: the stagger, the lifetime and the topmost cadence all
    /// ride it, so a test drives every one of them without a wall-clock wait.</param>
    /// <param name="dispatch">Marshals a clock callback onto the surface thread.</param>
    /// <param name="presenceFactory">Builds one overlay presence. Called lazily, on the surface
    /// thread, only when a surface is really needed — a session that never flashes creates no
    /// window, and a build with no overlay creates the refusal instead.</param>
    /// <param name="frames">Turns a path into pixels.</param>
    /// <param name="display">Where surfaces may go; null when the OS reports no display.</param>
    /// <param name="random">Placement rolls.</param>
    /// <param name="draw">The Visuals row's three dials, read once per flash. Null means
    /// <see cref="FlashDraw.Defaults"/> — WPF's shipped numbers, which is what this presenter drew
    /// with before the dials existed.</param>
    /// <param name="haptics">The haptic limb, told once per PLACED IMAGE. Null is ABSENT
    /// rather than silent — a build with no limb never reaches the sink at all, and the difference
    /// between "no limb here" and "the limb decided nothing" must stay legible.</param>
    public FlashSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IOverlayPresence> presenceFactory,
        IFlashFrameSource frames,
        Func<OverlayBounds?> display,
        Random? random = null,
        Func<FlashDraw>? draw = null,
        IHapticLimb? haptics = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(presenceFactory);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(display);
        _clock = clock;
        _dispatch = dispatch;
        _frames = frames;
        _display = display;
        _draw = draw ?? (static () => FlashDraw.Defaults);
        _random = random ?? new Random();
        _haptics = haptics;
        _surfaces = new OverlaySurfaceSet(clock, dispatch, presenceFactory, MaxConcurrentSurfaces, TopmostCadence);
    }

    /// <summary>The product composition: the real overlay backend for this platform, the GDI+
    /// frame source, and the OS's own primary display.</summary>
    public static FlashSurfacePresenter Product(
        ISessionClock clock, Action<Action> dispatch, Random? random = null, Func<FlashDraw>? draw = null,
        IHapticLimb? haptics = null) =>
        new(clock, dispatch, OverlayPresenceFactory.Create, new GdiPlusFlashFrameSource(),
            static () => OverlayDisplays.Enumerate() is [var primary, ..] ? primary.Bounds : null,
            random,
            draw,
            haptics);

    /// <summary>How many surfaces this presenter believes are on screen right now. Its OWN
    /// bookkeeping, never <see cref="IOverlayPresence.IsPresenting"/>.</summary>
    public int LiveSurfaces => _surfaces.LiveSurfaces;

    /// <summary>Surfaces this presenter has ever placed. Diagnostics and facts; never a claim.</summary>
    public int SurfacesShown => _surfaces.SurfacesShown;

    /// <summary>Images a flash drew that produced no pixels — missing, corrupt, or a format this
    /// build cannot decode. WPF's own normal case (<c>LoadImagesUntilAsync</c>).</summary>
    public int UndecodableImages { get; private set; }

    /// <summary>The last placement outcome, verbatim. This is where "there is no overlay on this
    /// platform" is visible to a caller: it is a typed <see cref="CapabilityState.Unavailable"/>
    /// carrying the reason code and the manual gate, not a silence.</summary>
    public CapabilityState? LastPresent => _surfaces.LastPresent;

    /// <summary>The last content outcome, verbatim.</summary>
    public CapabilityState? LastPaint => _surfaces.LastPaint;

    /// <summary>The last withdrawal outcome, verbatim.</summary>
    public CapabilityState? LastWithdraw => _surfaces.LastWithdraw;

    /// <inheritdoc/>
    public CapabilityState? LastPlacement => _surfaces.LastPresent;

    /// <summary>The reading the last <see cref="Show"/> took, or null before any flash. Diagnostics
    /// and facts; never a claim about a screen.</summary>
    public FlashDraw? LastDraw { get; private set; }

    /// <inheritdoc/>
    public void Show(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_surfaces.Disposed || paths.Count == 0)
        {
            return;
        }

        // ONCE per flash, before the first surface and before the stagger — upstream reads its
        // duration once at the top of ShowImages (FlashService.cs:1028-1034) and its scale once in
        // LoadImagesUntilAsync (:655-656), then applies the same numbers to every window of that
        // flash. Reading inside ShowOne instead would let a dial moved during the 300 ms stagger
        // give one flash's pictures two different sizes.
        var draw = _draw();
        LastDraw = draw;

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (i == 0)
            {
                // WPF spawns the first window of a flash synchronously and defers the rest
                // (FlashService.cs:1114-1121): the flash starts the moment it comes due.
                ShowOne(path, draw);
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
                    ShowOne(path, draw);
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
        _surfaces.HideAll();
    }

    public void Dispose()
    {
        if (_surfaces.Disposed)
        {
            return;
        }

        HideAll();
        _surfaces.Dispose();
    }

    private void ShowOne(string path, FlashDraw draw)
    {
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

        if (_surfaces.LiveSurfaces >= MaxConcurrentSurfaces)
        {
            return;
        }

        var monitor = display.Value;
        var frame = _frames.Render(
            path,
            (sourceWidth, sourceHeight) =>
                FlashGeometry.Size(sourceWidth, sourceHeight, monitor.Width, monitor.Height, draw.ScalePercent));

        if (frame is null)
        {
            UndecodableImages++;
            return;
        }

        var slot = _surfaces.Acquire();
        if (slot is null)
        {
            return;
        }

        var placement = FlashGeometry.Place(monitor, frame.Width, frame.Height, _surfaces.LiveRects(), _random);

        // Click-through, which is WPF's WS_EX_TRANSPARENT arm (:3668). WPF's other arm exists to
        // serve pop / hydra / XP mechanics this port does not have, and a surface that catches
        // clicks it does nothing with would swallow the user's input — the exact desktop-breaking
        // failure OverlayInputNotPassingThrough exists to refuse (a recorded divergence).
        var request = new OverlaySurfaceRequest(placement, draw.Opacity, ClickThrough: true);
        _surfaces.Place(slot, request, frame, draw.Lifetime);

        // Haptic census sites 1-3. WPF fires FlashDecayVibeAsync() from all THREE of
        // SpawnFlashWindow's mutually exclusive spawn arms (FlashService.cs:1453, :1480, :1516) —
        // one ladder per WINDOW, not one per flash — and each call replaces the ladder already
        // running (HapticService.cs:774-776). AFTER the placement, because upstream's arms fire
        // once the window exists, and because a decode that produced no pixels returned above.
        _haptics?.FlashPlaced();
    }

    private sealed class PendingShow : IDisposable
    {
        public IDisposable? Handle { get; set; }

        public void Dispose() => Handle?.Dispose();
    }
}
