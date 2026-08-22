using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Pointer;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>The dials a running field needs. A value, so the presenter never reads a store.</summary>
/// <param name="PerMinute">Spawn frequency (<c>CCP.Core/Models/AppSettings.cs:2743-2747</c>).</param>
/// <param name="SizePercent">The user's size dial (<c>Services/BubbleSizing.cs:50-60</c>).</param>
/// <param name="SpeedBoostPercent">The travel-speed boost (<c>CCP.Core/Models/AppSettings.cs:2814-2818</c>).</param>
public readonly record struct BubblePopSettings(int PerMinute, int SizePercent, int SpeedBoostPercent);

/// <summary>Where Bubble Pop's clickable targets go when they have somewhere to go.</summary>
public interface IBubblePopSurface
{
    /// <summary>Start the field, or re-apply new dials to a running one. Called on the surface
    /// thread, from inside the module's arm.</summary>
    CapabilityState Engage(BubblePopSettings settings);

    /// <summary>Take every target off the desktop now and stop both cadences — upstream's
    /// <c>Stop()</c>, which stops the spawn timer, stops the animation driver and calls
    /// <c>PopAllBubbles()</c> (<c>Services/BubbleService.cs:725-739</c>).</summary>
    void Withdraw();

    /// <summary>True while this presenter has a field engaged.</summary>
    bool Showing { get; }

    /// <summary>
    /// True while the module's work is genuinely running, and <b>this is the property the dot's
    /// third clause reads.</b> Not <see cref="Showing"/>: a field whose targets the window manager
    /// has stopped routing to is a picture of a game — visible, and impossible to play. See
    /// <see cref="BubblePopSurfacePresenter.Running"/> for why the clause is a disjunction rather
    /// than a bare "something routes".
    /// </summary>
    bool Running { get; }

    /// <summary>How many targets are on the desktop right now.</summary>
    int TargetsUp { get; }

    /// <summary>How many of them the OS last said it routes a click at their own centre TO.</summary>
    int RoutableTargets { get; }

    /// <summary>Bubbles the user popped, this run.</summary>
    int Popped { get; }

    /// <summary>Bubbles that floated away unhit, this run.</summary>
    int Missed { get; }

    /// <summary>What the OS last said about placing or moving a target, verbatim, or null before
    /// anything was attempted.</summary>
    CapabilityState? LastPlacement { get; }

    /// <summary>Whether this process can put a clickable target in front of anybody at all.</summary>
    bool CanReachAPointer { get; }

    /// <summary>How many clicks the operating system delivered to this field's own windows, and how
    /// many of those were refused activation. Evidence for the panel and for a fact; never an input
    /// to a claim (see <see cref="IPointerSurface.MouseActivateRefusals"/>).</summary>
    (int Presses, int ActivationsRefused) Delivery { get; }
}

/// <summary>
/// <b>Bubble Pop, on a real pointer surface</b> — the port's first module the user is expected to
/// ACT on rather than watch.
///
/// <para><b>Where the cadences live, and why that is the established answer applied again.</b> There are
/// two: the spawn timer (<c>60000/frequency</c> ms, <c>Services/BubbleService.cs:188</c>) and the
/// animation step (<c>STEP_MS = 30</c>, <c>:53</c>). Both are here, in the presenter, on the injected
/// <see cref="ISessionClock"/>, and <see cref="BubblePopEffect"/> takes no clock at all — the same
/// split <see cref="SpiralSurfacePresenter"/> and <see cref="PinkFilterSurfacePresenter"/> already
/// carry, on the same rule: <i>an interval that decides when a MODULE is due</i> belongs to
/// <see cref="PacedSessionEffect{TFiring}"/>, while <i>a cadence that keeps a SURFACE correct</i> is
/// the surface's. Bubble Pop is never "due": it is on, and the field moves.</para>
///
/// <para><b>The move seam, which is the whole reason this needed a new capability.</b> Every step
/// repositions every live target through <see cref="IPointerSurface.Move"/>, which re-asks the
/// operating system everything <see cref="IPointerSurface.Open"/> asked. <c>Input/**</c> has ONE
/// <c>SetWindowPos</c> in the whole folder (<c>Win32InputPresence.cs:206</c>) and no move seam at
/// all — the earlier census, re-verified in this packet's plan.</para>
///
/// <para><b>What is NOT ported</b>, declared rather than stubbed: the pop SOUND and its pooled
/// <c>WaveOutEvent</c> devices (<c>:1971-2016</c>); XP, the lucky roll, achievements, haptics and
/// Discord presence — the whole <c>AwardAmbientPop</c> reward chain (<c>:951-980</c>) — because this
/// port has no progression subsystem at all; Trigger Bubbles and the payloads a pop fires
/// (<c>:1021-1076</c>); the entire Chaos Mode API this service is 90 % of (<c>:1228-1807</c>); the
/// gaze-pop targets (<c>:82</c>); the companion easter egg (<c>:599-632</c>); the shared-host and
/// compositor render paths and their global mouse hook (primer §5), whose absence is the divergence
/// this packet exists to make honest; the multi-monitor spawn (<c>:877-881</c>, D66's single-display
/// limit); and the sprite art, because this port bundles none (D86's rule).</para>
/// </summary>
public sealed class BubblePopSurfacePresenter : IBubblePopSurface, IDisposable
{
    /// <summary>The fill every target's client area is painted with, and the colour the ink
    /// differential's control points must read back EXACTLY.</summary>
    public const uint TargetFill = 0x00201020;

    /// <summary>The disc's colour. Chosen only to differ from <see cref="TargetFill"/>; this port
    /// bundles no bubble art (D86), so the target is a plain disc and says so.</summary>
    public const uint TargetInk = 0x00E0C0FF;

    /// <summary>How many messages one pump drains. Bounded iteration, never a wall-clock wait.</summary>
    public const int PumpBudget = 64;

    private readonly ISessionClock _clock;
    private readonly Action<Action> _dispatch;
    private readonly Func<IPointerSurface> _surfaceFactory;
    private readonly Func<PointerBounds?> _playArea;
    private readonly Func<Random> _randomFactory;
    private readonly object _gate = new();

    private IPointerSurface? _surface;
    private BubblePopField? _field;
    private Dictionary<int, int> _targetByBubble = [];
    private Dictionary<int, int> _bubbleByTarget = [];
    private IDisposable? _spawnTimer;
    private IDisposable? _stepTimer;
    private BubblePopSettings _settings;
    private bool _engaged;
    private int _routable;
    private bool _disposed;

    /// <param name="clock">The session clock. It carries BOTH cadences and nothing else; the MODULE
    /// has no clock.</param>
    /// <param name="dispatch">Marshals a cadence callback onto the surface thread. A native window
    /// belongs to the thread that made it.</param>
    /// <param name="surfaceFactory">Builds the pointer surface, lazily, on the surface thread.</param>
    /// <param name="playArea">Where the field may run; null when the OS reports no display.</param>
    /// <param name="randomFactory">The spawn source for each run, injected so a field is
    /// reproducible.</param>
    public BubblePopSurfacePresenter(
        ISessionClock clock,
        Action<Action> dispatch,
        Func<IPointerSurface> surfaceFactory,
        Func<PointerBounds?> playArea,
        Func<Random>? randomFactory = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(surfaceFactory);
        ArgumentNullException.ThrowIfNull(playArea);
        _clock = clock;
        _dispatch = dispatch;
        _surfaceFactory = surfaceFactory;
        _playArea = playArea;
        _randomFactory = randomFactory ?? (() => new Random());
    }

    /// <summary>The product composition: the real pointer backend for this platform, and the OS's
    /// own primary display as the play area.</summary>
    public static BubblePopSurfacePresenter ForProduct(ISessionClock clock, Action<Action> dispatch) =>
        new(clock, dispatch, PointerSurfaceFactory.Create, PrimaryPlayArea);

    /// <summary>The primary display, as a play area. Single-display, the same limit every other
    /// drawing module in this port carries (D66).</summary>
    public static PointerBounds? PrimaryPlayArea()
    {
        var display = PrimaryDisplayPlacement.PrimaryBounds();
        return display is null
            ? null
            : new PointerBounds(display.Value.X, display.Value.Y, display.Value.Width, display.Value.Height);
    }

    /// <inheritdoc/>
    public bool Showing
    {
        get { lock (_gate) { return _engaged; } }
    }

    /// <summary>
    /// <b>The dot's third clause.</b> A field is running when it is engaged AND either nothing of
    /// its own is up yet, or the operating system says it routes a click at its own centre to at
    /// least one of the targets that are.
    ///
    /// <para><b>Why a disjunction and not a bare "something routes".</b> Between two spawns there
    /// is legitimately nothing on the desktop — at the bottom of the dial that is fifty-nine seconds
    /// out of every sixty (<c>60000/1</c> ms, <c>Services/BubbleService.cs:188</c>) — and a dot that
    /// went dark there would report the module broken for almost the whole session. The clause
    /// darkens for the state that is really wrong: targets ARE up and the window manager routes
    /// none of them here, which is a field the user can see and cannot play.</para>
    ///
    /// <para><b>Why "at least one" and not "all".</b> Upstream's own targets overlap freely — the
    /// spawn band is a random x per bubble with no separation rule (<c>:2852</c>) — so one bubble
    /// covering another's centre is an ordinary state of the game and not a failure of the channel.
    /// A field with one hittable bubble in it is a game.</para>
    /// </summary>
    public bool Running
    {
        get
        {
            lock (_gate)
            {
                return _engaged && (_targetByBubble.Count == 0 || _routable > 0);
            }
        }
    }

    /// <inheritdoc/>
    public int TargetsUp
    {
        get { lock (_gate) { return _targetByBubble.Count; } }
    }

    /// <inheritdoc/>
    public int RoutableTargets
    {
        get { lock (_gate) { return _routable; } }
    }

    /// <inheritdoc/>
    public int Popped
    {
        get { lock (_gate) { return _field?.Popped ?? 0; } }
    }

    /// <inheritdoc/>
    public int Missed
    {
        get { lock (_gate) { return _field?.Missed ?? 0; } }
    }

    /// <inheritdoc/>
    public CapabilityState? LastPlacement
    {
        get { lock (_gate) { return _surface?.LastPlacement; } }
    }

    /// <inheritdoc/>
    public bool CanReachAPointer
    {
        get
        {
            lock (_gate)
            {
                _surface ??= _surfaceFactory();
                return _surface.CanReachAPointer;
            }
        }
    }

    /// <inheritdoc/>
    public (int Presses, int ActivationsRefused) Delivery
    {
        get
        {
            lock (_gate)
            {
                return _surface is null ? (0, 0) : (_surface.PressesSeen, _surface.MouseActivateRefusals);
            }
        }
    }

    /// <inheritdoc/>
    public CapabilityState Engage(BubblePopSettings settings)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.PointerSurfaceUnavailable,
                    "this Bubble Pop presenter was disposed; no field will run on it again"));
            }

            _settings = settings;
            _surface ??= _surfaceFactory();

            // Asked of the OS before a single window exists, and the ONLY thing that decides whether
            // the channel is there at all. A platform check never produces Available and never
            // produces this refusal either: UnsupportedPointerSurface answers false here because it
            // never asked, and Win32PointerSurface answers it out of the window station.
            if (!_surface.CanReachAPointer)
            {
                var station = _surface.ObserveStation();
                return new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.PointerSurfaceUnavailable,
                    $"the OS reports asked={station.Asked}, window-station-visible={station.WindowStationVisible}, "
                    + $"displays={station.DisplayCount}, desktop-reachable={station.DesktopReachable}; this process "
                    + "cannot put a clickable target in front of anybody, so no bubbles were placed"));
            }

            var area = _playArea();
            if (area is null)
            {
                return new CapabilityState.Unavailable(new CapabilityReason(
                    EffectReasonCodes.PointerSurfaceUnavailable,
                    "the OS reports no display to run a bubble field on, so nothing was placed"));
            }

            if (_engaged)
            {
                // Re-applying dials to a running field. Upstream's own second entry point:
                // RefreshFrequency() re-times the spawn timer in place rather than bouncing the
                // service (Services/BubbleService.cs, primer §3a).
                RestartSpawnTimerLocked();
                return Placed();
            }

            var bounds = area.Value;
            _field = new BubblePopField(bounds.X, bounds.Y, bounds.Width, bounds.Height, _randomFactory());
            _targetByBubble = [];
            _bubbleByTarget = [];
            _routable = 0;
            _surface.OnPress = OnPress;
            _engaged = true;

            RestartSpawnTimerLocked();
            _stepTimer = _clock.Schedule(BubblePopField.StepInterval, () => _dispatch(StepOnce));

            // Upstream spawns one immediately rather than waiting out the first interval
            // (Services/BubbleService.cs:200, "Spawn first bubble immediately").
            SpawnOnceLocked();
            return Placed();
        }
    }

    /// <inheritdoc/>
    public void Withdraw()
    {
        lock (_gate)
        {
            _spawnTimer?.Dispose();
            _spawnTimer = null;
            _stepTimer?.Dispose();
            _stepTimer = null;

            if (_surface is not null)
            {
                foreach (var target in _targetByBubble.Values.ToArray())
                {
                    _surface.Close(target);
                }

                _surface.OnPress = null;
            }

            _field?.Clear();
            _targetByBubble.Clear();
            _bubbleByTarget.Clear();
            _routable = 0;
            _engaged = false;
        }
    }

    public void Dispose()
    {
        Withdraw();

        IPointerSurface? surface;
        lock (_gate)
        {
            _disposed = true;
            surface = _surface;
            _surface = null;
        }

        surface?.Dispose();
    }

    /// <summary>
    /// Drive one animation step by hand. Exposed so a fact can advance the field with no wall-clock
    /// wait at all — the same seam every other presenter in this port offers its tests.
    /// </summary>
    public void StepOnce()
    {
        lock (_gate)
        {
            if (!_engaged || _field is null || _surface is null)
            {
                return;
            }

            // Presses first: a click that arrived since the last step belongs to the position the
            // bubble was at when the OS routed it, not to the one it is about to move to.
            _surface.Pump(PumpBudget);

            foreach (var (bubble, _) in _field.Step())
            {
                if (_targetByBubble.Remove(bubble.Id, out var goneTarget))
                {
                    _bubbleByTarget.Remove(goneTarget);
                    _surface.Close(goneTarget);
                }
            }

            var routable = 0;
            foreach (var bubble in _field.Bubbles)
            {
                if (!_targetByBubble.TryGetValue(bubble.Id, out var target))
                {
                    continue;
                }

                var (x, y, size) = bubble.Rect;
                var state = _surface.Move(target, new PointerBounds(x, y, size, size));
                if (state is CapabilityState.Available)
                {
                    routable++;
                }
            }

            _routable = routable;
            RescheduleStepLocked();
        }
    }

    /// <summary>Spawn one bubble by hand, for the same reason <see cref="StepOnce"/> exists.</summary>
    public void SpawnOnce()
    {
        lock (_gate)
        {
            SpawnOnceLocked();
        }
    }

    private void SpawnOnceLocked()
    {
        if (!_engaged || _field is null || _surface is null)
        {
            return;
        }

        var spawned = _field.Spawn(_settings.SizePercent, _settings.SpeedBoostPercent);
        if (spawned is not { } bubble)
        {
            return;
        }

        var (x, y, size) = bubble.Rect;
        var state = _surface.Open(
            new PointerTargetRequest(new PointerBounds(x, y, size, size), TargetFill, TargetInk),
            out var target);

        if (target == 0)
        {
            // Nothing was placed at all, so the field must not keep a bubble nobody can click. It is
            // dropped without counting a pop OR a miss: neither happened.
            _field.Hit(bubble.Id);
            _field.Clear();
            _targetByBubble.Clear();
            _bubbleByTarget.Clear();
            _routable = 0;
            return;
        }

        _targetByBubble[bubble.Id] = target;
        _bubbleByTarget[target] = bubble.Id;
        if (state is CapabilityState.Available)
        {
            _routable++;
        }
    }

    /// <summary>
    /// A press the operating system routed to one of this field's own windows.
    ///
    /// <para><b>Nothing here decides WHICH bubble was clicked.</b> The window manager did, at the
    /// instant of the click, over the rectangle the OS itself holds — which is the whole reason each
    /// target is its own top-level window. Upstream's hosted path decides this in user space from a
    /// snapshot rebuilt once per UI tick (primer §4c/§4e), and that is the race predicted for
    /// this packet.</para>
    /// </summary>
    private void OnPress(PointerPress press)
    {
        if (press.Kind != PointerPressKind.Down)
        {
            // The UP half is delivered and counted by the capability and deliberately does nothing
            // here: upstream pops on the DOWN (MouseLeftButtonDown, Services/BubbleService.cs:3113),
            // so a press that is never released still pops, exactly as it does upstream.
            return;
        }

        lock (_gate)
        {
            if (_field is not null && _bubbleByTarget.TryGetValue(press.Target, out var bubbleId))
            {
                _field.Hit(bubbleId);
            }
        }
    }

    private void RestartSpawnTimerLocked()
    {
        _spawnTimer?.Dispose();
        _spawnTimer = _clock.Schedule(
            BubblePopField.SpawnInterval(_settings.PerMinute), () => _dispatch(SpawnAndReschedule));
    }

    private void SpawnAndReschedule()
    {
        lock (_gate)
        {
            if (!_engaged)
            {
                return;
            }

            SpawnOnceLocked();
            RestartSpawnTimerLocked();
        }
    }

    private void RescheduleStepLocked()
    {
        _stepTimer?.Dispose();
        _stepTimer = _clock.Schedule(BubblePopField.StepInterval, () => _dispatch(StepOnce));
    }

    private CapabilityState Placed()
    {
        var last = _surface?.LastPlacement;
        return last ?? new CapabilityState.Available(
            "the field is engaged and its first spawn is due; nothing has been placed yet");
    }
}
