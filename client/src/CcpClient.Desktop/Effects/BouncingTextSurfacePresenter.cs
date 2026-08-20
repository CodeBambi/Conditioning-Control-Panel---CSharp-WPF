using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Glyph;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>Where the Bouncing Text module's frames go when they have somewhere to go.</summary>
public interface IBouncingTextSurface
{
    /// <summary>Put the logo up and start it moving, or re-apply the dials to one already up.</summary>
    CapabilityState Engage(BouncingTextPresentation presentation);

    /// <summary>Take it off the screen now and stop the cadence.</summary>
    void Withdraw();

    /// <summary>True while a surface this presenter placed is up.</summary>
    bool Showing { get; }

    /// <summary>
    /// True while the module's work is genuinely running: the surface is up AND the operating
    /// system's own copy of it still carries the frame's opaque ink. <b>This is the eighth meaning
    /// of the dot</b> — see <see cref="BouncingTextEffect"/>.
    /// </summary>
    bool Running { get; }

    /// <summary>True while there is anything to release — a surface or a cadence.</summary>
    bool Engaged { get; }

    /// <summary>How many wall bounces this run has taken. Read by the panel; it is upstream's own
    /// event (<c>BouncingTextService.cs:513</c>) with the XP removed.</summary>
    int Bounces { get; }

    /// <summary>The last thing the OS said about this surface, verbatim, or null.</summary>
    CapabilityState? LastPlacement { get; }
}

/// <summary>
/// The bouncing logo, composited on a real per-pixel-alpha surface.
///
/// <para><b>The cadence lives HERE, not in the module</b> — SP-106's rule, unchanged for a second
/// moving module: an INTERVAL that decides when a MODULE is due belongs to
/// <see cref="PacedSessionEffect{TFiring}"/>, and a CADENCE that keeps a SURFACE correct belongs to
/// the surface. A bouncing logo is never "due"; it is on, and what is on screen moves. So
/// <see cref="BouncingTextEffect"/> takes no clock and owns no timer.</para>
///
/// <para><b>Two operations per frame, and the split is the whole reason this module is portable at
/// all.</b> Almost every frame is a MOVE — one <c>UpdateLayeredWindow</c> carrying a destination
/// point, no style write, no z-order walk, no hit test. A PAINT (a fresh raster, composited and read
/// back) happens only when the word or the colour changed, which upstream does on a bounce and not
/// on a frame (<c>BouncingTextService.cs:489-556</c>). D84 blocked this module because moving an
/// OVERLAY means re-<c>Present</c>ing it, which momentarily clears click-through and walks the whole
/// top-level z-order — sixty times a second over whatever the user is doing. A glyph surface's move
/// does none of that.</para>
///
/// <para><b>Two cadences, one clock.</b> The frame advance is <see cref="FrameCadence"/>; the
/// topmost re-assertion is <see cref="TopmostCadence"/>, which is WPF's own ~500 ms for this module
/// specifically and its own stated reason: "bouncing text is long-lived and will lose topmost when
/// competing with flash/video/overlay windows" (<c>:391-398</c>). A test drives both by hand and
/// neither is a wall-clock wait.</para>
/// </summary>
public sealed class BouncingTextSurfacePresenter : IBouncingTextSurface, IDisposable
{
    /// <summary>
    /// The frame interval the port drives the motion at.
    ///
    /// <para><b>A divergence, stated at the constant.</b> WPF rides
    /// <c>CompositionTarget.Rendering</c> — vsync-aligned, one callback per rendered frame, roughly
    /// 60 Hz on this machine (<c>BouncingTextService.cs:26-30</c>). This port has no composition
    /// clock to ride: the surface is a plain Win32 window and the session spine's only timer is
    /// <see cref="ISessionClock"/>. 20 ms is a deliberate 50 Hz, which is what the motion's
    /// delta-time step is FOR — the arithmetic is frame-rate independent and upstream says so at the
    /// same lines.</para>
    /// </summary>
    public static readonly TimeSpan FrameCadence = TimeSpan.FromMilliseconds(20);

    /// <summary>WPF's own topmost re-assertion period for this module (<c>:391-398</c>).</summary>
    public static readonly TimeSpan TopmostCadence = TimeSpan.FromMilliseconds(500);

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly Func<IGlyphSurface> _surfaceFactory;
    private readonly IGlyphTextSource _text;
    private readonly Func<(int X, int Y, int Width, int Height)?> _display;
    private readonly Func<Random> _randomFactory;
    private readonly IHapticLimb? _haptics;
    private readonly object _gate = new();

    private IGlyphSurface? _surface;
    private BouncingTextField? _field;
    private BouncingTextPresentation? _presentation;
    private IDisposable? _frameTimer;
    private double _sinceTopmost;
    private bool _lastFrameHeld;
    private bool _showing;
    private bool _disposed;

    /// <param name="clock">The session clock. It carries the frame advance AND the topmost cadence,
    /// and nothing else; the MODULE has no clock at all.</param>
    /// <param name="dispatch">Marshals a cadence callback onto the surface thread — a native window
    /// belongs to the thread that created it.</param>
    /// <param name="surfaceFactory">Builds the glyph surface, lazily, on the surface thread.</param>
    /// <param name="text">The rasteriser seam. A build that cannot raster is an ordinary outcome.</param>
    /// <param name="display">Where the logo may bounce; null when the OS reports no display.</param>
    /// <param name="randomFactory">A fresh source per engage, so a session's trajectory can be
    /// pinned in a test and is unpredictable in the product.</param>
    public BouncingTextSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IGlyphSurface> surfaceFactory,
        IGlyphTextSource text,
        Func<(int X, int Y, int Width, int Height)?> display,
        Func<Random> randomFactory,
        IHapticLimb? haptics = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(surfaceFactory);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(randomFactory);
        _clock = clock;
        _dispatch = dispatch;
        _surfaceFactory = surfaceFactory;
        _text = text;
        _display = display;
        _randomFactory = randomFactory;
        _haptics = haptics;
    }

    /// <summary>The product composition: the real per-pixel-alpha backend for this platform, the
    /// GDI+ rasteriser, and the OS's own primary display.</summary>
    public static BouncingTextSurfacePresenter Product(
        ISessionClock clock, Action<Action> dispatch, IHapticLimb? haptics = null) =>
        new(clock, dispatch, GlyphSurfaceFactory.Create, new GdiPlusGlyphTextSource(),
            static () => Overlay.OverlayDisplays.Enumerate() is [var primary, ..]
                ? (primary.Bounds.X, primary.Bounds.Y, primary.Bounds.Width, primary.Bounds.Height)
                : null,
            static () => new Random(),
            haptics);

    /// <inheritdoc/>
    public bool Showing
    {
        get
        {
            lock (_gate)
            {
                return _showing;
            }
        }
    }

    /// <summary>
    /// <b>The eighth meaning of the dot, in three clauses.</b> The surface is up, the last frame the
    /// operating system was asked about really carried the frame's opaque ink, and the cadence is
    /// still armed. The second clause is the one no earlier module could make: a layered window can
    /// be present, on top and compositing NOTHING, and that state is invisible to every other
    /// question this process can ask.
    /// </summary>
    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _showing && _lastFrameHeld && _frameTimer is not null;
            }
        }
    }

    /// <inheritdoc/>
    public bool Engaged
    {
        get
        {
            lock (_gate)
            {
                return _surface is not null || _frameTimer is not null;
            }
        }
    }

    /// <inheritdoc/>
    public int Bounces
    {
        get
        {
            lock (_gate)
            {
                return _field?.Bounces ?? 0;
            }
        }
    }

    /// <inheritdoc/>
    public CapabilityState? LastPlacement { get; private set; }

    /// <inheritdoc/>
    public CapabilityState Engage(BouncingTextPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var opacity = presentation.SurfaceOpacity;
        if (opacity is null)
        {
            Withdraw();
            return Record(new CapabilityState.Degraded(
                "the module is engaged and its logo is fully transparent, so nothing is drawn",
                new CapabilityReason(
                    EffectReasonCodes.BouncingTextTransparent,
                    "the bouncing text's opacity dial is at 0 %, so there is nothing to put on screen")));
        }

        var display = _display();
        if (display is null)
        {
            Withdraw();
            return Record(new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.BouncingTextNoDisplay,
                "the operating system reports no display this process can put a surface on, so the logo has "
                + "nowhere to bounce and nothing was attempted")));
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return Record(new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.BouncingTextNoDisplay,
                    "this presenter was disposed; it will never place another surface")));
            }

            _presentation = presentation;
            _field = new BouncingTextField(
                presentation,
                display.Value,
                word => _text.Measure(word, presentation),
                _randomFactory(),
                _haptics);
        }

        var placed = PlaceCurrent(firstPlacement: true);
        if (placed is not CapabilityState.Available)
        {
            Withdraw();
            return Record(placed);
        }

        lock (_gate)
        {
            _showing = true;
            _sinceTopmost = 0;

            // The PLACEMENT itself is a confirmed composite: Present's Available is earned by
            // reading the surface back out of the operating system, so the module is running from
            // this instant rather than from the first cadence tick. Leaving it false here would
            // make the dot dark for one frame after every engage, which is a lie about a surface
            // the OS has already confirmed.
            _lastFrameHeld = true;
        }

        ArmNextFrame();
        return Record(placed);
    }

    /// <inheritdoc/>
    public void Withdraw()
    {
        IDisposable? timer;
        IGlyphSurface? surface;
        lock (_gate)
        {
            timer = _frameTimer;
            surface = _surface;
            _frameTimer = null;
            _surface = null;
            _field = null;
            _showing = false;
            _lastFrameHeld = false;
        }

        // The cadence dies WITH the surface. A moving module whose timer outlived its window would
        // keep re-compositing something nobody can see, which is SP-106's own finding about the
        // shared body applied to a second moving module.
        timer?.Dispose();
        if (surface is not null)
        {
            surface.Withdraw();
            surface.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Withdraw();
    }

    /// <summary>
    /// One frame. A MOVE unless the word or the colour changed, in which case a fresh raster is
    /// composited and read back.
    /// </summary>
    private void Advance()
    {
        BouncingTextField? field;
        lock (_gate)
        {
            if (_disposed || !_showing)
            {
                return;
            }

            field = _field;
        }

        if (field is null)
        {
            return;
        }

        field.Advance(FrameCadence.TotalSeconds);

        var state = field.NeedsRaster ? PlaceCurrent(firstPlacement: false) : MoveCurrent();
        var held = state is CapabilityState.Available;

        lock (_gate)
        {
            _lastFrameHeld = held;
            _sinceTopmost += FrameCadence.TotalSeconds;
            if (_sinceTopmost >= TopmostCadence.TotalSeconds)
            {
                _sinceTopmost = 0;
                _surface?.Reassert();
            }
        }

        if (!held)
        {
            // A surface that stopped holding its composite is not a surface. Retire it rather than
            // keep feeding a window the OS no longer draws — the same rule SP-111's presenter has.
            LastPlacement = state;
            Withdraw();
            return;
        }

        LastPlacement = state;
        ArmNextFrame();
    }

    /// <summary>Composite a fresh raster at the field's current rectangle.</summary>
    private CapabilityState PlaceCurrent(bool firstPlacement)
    {
        BouncingTextField? field;
        BouncingTextPresentation? presentation;
        lock (_gate)
        {
            field = _field;
            presentation = _presentation;
        }

        if (field is null || presentation is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.BouncingTextNoDisplay, "nothing is engaged, so there is nothing to place"));
        }

        var logo = field.Logo;
        var (x, y, width, height) = field.Rectangle;
        if (width <= 0 || height <= 0)
        {
            return new CapabilityState.Degraded(
                "the module is engaged and this build could not measure the text, so nothing is on screen",
                new CapabilityReason(
                    EffectReasonCodes.BouncingTextNoRaster,
                    "this build has no text rasteriser, so the logo has no size and nothing was composited"));
        }

        var frame = _text.Render(logo.Text, logo.Colour, width, height, presentation);
        if (frame is null)
        {
            return new CapabilityState.Degraded(
                "the module is engaged and this build could not raster the text, so nothing is on screen",
                new CapabilityReason(
                    EffectReasonCodes.BouncingTextNoRaster,
                    $"the rasteriser returned no frame for '{logo.Text}', so there is nothing whose composite "
                    + "could be confirmed and nothing was put on screen"));
        }

        field.RasterTaken();

        IGlyphSurface surface;
        lock (_gate)
        {
            // A resize means a new composite, because the layered surface IS the frame — so a
            // changed word takes the Present path rather than the move path, and Present is where
            // the z-order, the hit test and the read-back are all re-earned.
            _surface ??= _surfaceFactory();
            surface = _surface;
        }

        return surface.Present(
            new GlyphSurfaceRequest(
                new GlyphBounds(x, y, width, height),
                presentation.SurfaceOpacity ?? 1.0,
                ClickThrough: true),
            frame);
    }

    /// <summary>The cheap path: one call, the rectangle re-asked from the OS, nothing else.</summary>
    private CapabilityState MoveCurrent()
    {
        BouncingTextField? field;
        IGlyphSurface? surface;
        lock (_gate)
        {
            field = _field;
            surface = _surface;
        }

        if (field is null || surface is null)
        {
            return new CapabilityState.Unavailable(new CapabilityReason(
                EffectReasonCodes.BouncingTextNoDisplay, "nothing is engaged, so there is nothing to move"));
        }

        var (x, y, width, height) = field.Rectangle;
        return surface.MoveTo(new GlyphBounds(x, y, width, height));
    }

    private void ArmNextFrame()
    {
        lock (_gate)
        {
            if (_disposed || !_showing)
            {
                return;
            }

            _frameTimer?.Dispose();
            _frameTimer = _clock.Schedule(FrameCadence, () => _dispatch(Advance));
        }
    }

    private CapabilityState Record(CapabilityState state)
    {
        LastPlacement = state;
        return state;
    }
}
