using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Core.Services.Chaos;

/// <summary>
/// Platform-agnostic engine for ambient clickable bubbles and chaos-mode effect bubbles.
/// Owns spawn timing, physics, lifecycle, and delegates all visuals to an <see cref="IBubbleRenderer"/>.
/// </summary>
public sealed class BubbleEngine
{
    private const int MaxAmbientBubbles = 3;
    private const double TickIntervalSec = 0.032; // ~30 FPS
    private const int MaxSpawnsPerFrame = 1;
    private const double DefaultDarterSpeed = 360.0;
    /// <summary>WPF's fixed-step tick assumed a flat 32ms frame; per-frame WPF speeds convert
    /// to this engine's per-second velocities through this factor.</summary>
    private const double WpfFramesPerSecond = 1000.0 / 32.0;
    private const int DefaultDarterTelegraphMs = 500;
    private const double DefaultChainReachDip = 120.0;

    // ---- Stage 2c field hazard tuning ----
    private const double RIPPLE_RADIUS_PX = 430.0;
    private const double RIPPLE_LIFE_MS = 550.0;
    private const double RESIDUE_RADIUS_PX = 170.0;
    private const double RESIDUE_LIFE_MS = 2000.0;
    private const double TRAIL_POP_RADIUS_PX = 46.0;
    private const double TRAIL_GAP_PX = 40.0;
    private const int TRAIL_MAX_POINTS = 60;
    private const double RESIDUE_FUSE_MULT = 2.0;
    // Electrified Rabbits free discharge (WPF BubbleService.cs:297 ESTIM_ARCS_PER_POP,
    // :402 ESTIM_BURST_RANGE_PX).
    private const int ESTIM_ARCS_PER_POP = 3;
    private const double ESTIM_BURST_RANGE_PX = 620.0;

    private readonly List<BubbleState> _bubbles = new();
    private readonly Random _random = new();
    private readonly IScreenProvider _screenProvider;
    private readonly ISettingsService _settings;
    private readonly IBubbleRenderer _renderer;
    private readonly IPointerState _pointerState;
    private readonly ILogger<BubbleEngine>? _logger;

    private DispatcherTimer? _spawnTimer;
    private DispatcherTimer? _animTimer;
    private bool _isRunning;
    private bool _isPaused;

    // ---- Chaos mode state (Stage 2a/2b) ----
    private bool _chaosActive;
    private bool _chaosFrozen;
    private double _chaosTimeScale = 1.0;
    private bool _chaosInputLocked;
    private Action<ChaosBubbleSpec>? _onBenignPop;
    private Action<ChaosBubbleSpec, double, bool>? _onDefuse;
    private Action<ChaosBubbleSpec>? _onDetonate;
    private Action<ChaosBubbleSpec, bool>? _onDarterCaught;
    private Action<ChaosBubbleSpec>? _onFreezeCaught;
    private Action<ChaosBubbleSpec, bool>? _onChaperoneShieldBroken;
    private Action<ChaosBubbleSpec>? _onBoundEnraged;
    private Action<ChaosBubbleSpec>? _onTeaseTouched;
    private Action<ChaosBubbleSpec>? _onTeaseDenied;
    private Action<ChaosBubbleSpec>? _onBrittleShattered;
    private Action<ChaosBubbleSpec>? _onTreatExpired;
    /// <summary>Live run knobs the engine reads at use sites (WPF live-lambda equivalent —
    /// see <see cref="ChaosRunKnobs"/>). The chaos service mutates these mid-run.</summary>
    public ChaosRunKnobs Knobs { get; } = new();

    private Action<ChaosBubbleSpec, bool>? _onDarterSpanked;

    // S4b-4 head follow-up (owner-authorized Q10b arc slice): the Electrified-Rabbits free
    // discharge emits arc bolts (fromPx -> each victim CenterPx) for the head to render through
    // the compositor (ChaosEStimArcLayer) + play a throttled estim_zap cue. Endpoints are
    // PHYSICAL px (CenterPx x Scaling); no charge consumed, arcs never chain onward.
    private Action<IReadOnlyList<(Point From, Point To)>>? _onEStimArc;

    // Owner-authorized: darter/sweeper sparkle trail. Fires once per emitted trail point so the
    // head renders a fading sparkle via ChaosFieldFxLayer.TrailDot (WPF BubbleService.cs:3128-3129
    // fallback ChaosFieldFxOverlay.TrailDot(nowPx, trailSec, warm: sweeper)). Args: (physical-px
    // point, sparkle lifeSec = trailSec, warm = IsSweeper amber).
    private Action<Point, double, bool>? _onDarterTrail;

    // ---- hold-to-defuse channel state (Avalonia port parity) ----
    private Func<ChaosBubbleSpec, bool>? _canChannel;
    private Action<ChaosBubbleSpec>? _onChannelStarted;
    private Action<ChaosBubbleSpec, string>? _onChannelBroken;
    private Guid? _channelBubbleId;
    private DateTime _channelStartUtc = DateTime.MinValue;

    private readonly Queue<ChaosBubbleSpec> _chaosSpawnQueue = new();
    private readonly Dictionary<Guid, Guid> _pendingChaperoneEscorts = new();
    private readonly Dictionary<Guid, Guid> _pendingChaperoneLives = new();
    private readonly Dictionary<Guid, int> _pendingBoundPairIds = new();
    private int _nextBoundPairId = 1;

    // ---- Stage 2c field hazard state ----
    private readonly List<(Point CenterPx, double AgeMs)> _ripples = new();
    private readonly List<(Point CenterPx, DateTime Until)> _residues = new();
    private readonly List<PlayerRipple> _playerRipples = new();

    // Trigger Bubbles: head-supplied factory mapping an effect-variant id → a firable payload.
    // Null = feature unavailable (the roll in SpawnBubble then no-ops).
    private readonly Func<string, EffectPayload?>? _effectPayloadFactory;

    public BubbleEngine(
        IScreenProvider screenProvider,
        ISettingsService settings,
        IBubbleRenderer renderer,
        IPointerState pointerState,
        ILogger<BubbleEngine>? logger = null,
        Func<string, EffectPayload?>? effectPayloadFactory = null)
    {
        _screenProvider = screenProvider;
        _settings = settings;
        _renderer = renderer;
        _pointerState = pointerState;
        _logger = logger;
        _effectPayloadFactory = effectPayloadFactory;
    }

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public int ActiveBubbles => _bubbles.Count;
    public bool ChaosActive => _chaosActive;

    /// <summary>Freeze pickups currently alive — the spawn director's FREEZE_MAX_ON_SCREEN
    /// cap re-pick reads this (WPF BubbleService.ActiveFreezeBubbles, consumed at
    /// WPF ChaosModeService.cs:1155-1162).</summary>
    public int ActiveFreezeBubbles => _bubbles.Count(b => b.Spec is { IsFreeze: true } && !b.IsPopping);

    /// <summary>Engine-logical X of the last chaos bubble popped — spawn-at-pop-point
    /// consumers (gg-rabbit sweepers, gold droplets) pin here. Mirrors the WPF
    /// BubbleService.ChaosLastPopXPx/YPx statics stamped on every chaos pop path
    /// (WPF BubbleService.cs:120-122, :3654-3860); this engine's spawn-pin space is
    /// engine-logical units (see ComputeChaosSpawn), so the stamp is logical too.</summary>
    public double ChaosLastPopX { get; private set; }

    /// <summary>Engine-logical Y of the last chaos bubble popped (see <see cref="ChaosLastPopX"/>).</summary>
    public double ChaosLastPopY { get; private set; }

    /// <summary>Seconds of rabbit/darter trail currently active (Tail-Plug boon) — thin
    /// wrapper over <see cref="ChaosRunKnobs.RabbitTrailSec"/> (S4b-4 knob migration).</summary>
    public double ChaosRabbitTrailSecNow => Knobs.RabbitTrailSec;

    public event Action? OnBubblePopped;
    public event Action? OnBubbleMissed;

    /// <summary>Raised when an echo bubble requests to split into child bubbles at the given DIP position.</summary>
    public event Action<ChaosBubbleSpec, double, double>? EchoSplitRequested;

    private sealed class PlayerRipple
    {
        public Point CenterPx;
        public double AgeMs;
        public double RadiusPx = RIPPLE_RADIUS_PX;
        public double LifeMs = RIPPLE_LIFE_MS;
        public readonly HashSet<BubbleState> Hit = new();
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _isPaused = false;

        // Kick the spawn timer and spawn the first bubble right away.
        RefreshFrequency();
        SpawnBubble();

        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
        _animTimer.Tick += (_, _) => Tick();
        _animTimer.Start();
    }

    public void Stop()
    {
        if (!_isRunning) return;
        _isRunning = false;
        _isPaused = false;

        _spawnTimer?.Stop();
        _spawnTimer = null;
        _animTimer?.Stop();
        _animTimer = null;

        foreach (var bubble in _bubbles.ToList())
        {
            _renderer.Destroy(bubble.Id);
        }
        _bubbles.Clear();
        _chaosSpawnQueue.Clear();
        _pendingChaperoneEscorts.Clear();
        _pendingChaperoneLives.Clear();
        _pendingBoundPairIds.Clear();
        _ripples.Clear();
        _residues.Clear();
        _playerRipples.Clear();
        _missedBuffer.Clear();
        _movedBuffer.Clear();
        _tickSnapshot.Clear();
        Knobs.Reset();
        _chaosActive = false;
    }

    public void PauseAndClear()
    {
        if (!_isRunning) return;
        _isPaused = true;
        foreach (var bubble in _bubbles.ToList())
        {
            _renderer.Destroy(bubble.Id);
        }
        _bubbles.Clear();
    }

    public void Resume()
    {
        if (!_isRunning) return;
        _isPaused = false;
    }

    public void RefreshFrequency()
    {
        _spawnTimer?.Stop();
        if (!_isRunning) return;

        var frequency = Math.Max(1, _settings.Current.BubblesFrequency);
        var interval = TimeSpan.FromMilliseconds(60000.0 / frequency);
        _spawnTimer = new DispatcherTimer { Interval = interval };
        _spawnTimer.Tick += (_, _) =>
        {
            if (_isRunning && !_isPaused && _bubbles.Count < MaxAmbientBubbles
                && !ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed)
                SpawnBubble();
        };
        _spawnTimer.Start();
    }

    public void SpawnOnce()
    {
        if (_isRunning && !_isPaused
            && !ConditioningControlPanel.Core.Services.DisplayChangeCoordinator.SpawnsSuppressed)
            SpawnBubble();
    }

    public void PopAllBubbles()
    {
        if (!_isRunning) return;
        foreach (var bubble in _bubbles.ToList())
            PopBubble(bubble.Id);
    }

    public void PopBubble(Guid id)
    {
        var idx = _bubbles.FindIndex(b => b.Id == id && !b.IsPopping);
        if (idx < 0) return;

        var bubble = _bubbles[idx];

        if (bubble.Spec is { } spec)
        {
            if (_chaosInputLocked) return;
            if (spec.IsChaperoneLive && bubble.IsShielded) return;

            bubble.IsPopping = true;
            // Stamp the pop point BEFORE any callback so spawn-at-pop consumers read a live
            // value inside their handlers (the WPF Bubble pop paths stamp the statics the
            // same way, WPF BubbleService.cs:3654-3860).
            ChaosLastPopX = bubble.X + bubble.Size / 2.0;
            ChaosLastPopY = bubble.Y + bubble.Size / 2.0;

            if (spec.IsTease)
            {
                _onTeaseTouched?.Invoke(spec);
                _onDetonate?.Invoke(spec);
                _renderer.Pop(bubble, () => _bubbles.Remove(bubble));
                return;
            }

            if (spec.IsBrittle)
            {
                _onBrittleShattered?.Invoke(spec);
                _onDetonate?.Invoke(spec);
                _renderer.Pop(bubble, () => _bubbles.Remove(bubble));
                return;
            }

            if (spec.IsEcho && !bubble.IsDefused)
            {
                _onDetonate?.Invoke(spec);
                EchoSplitRequested?.Invoke(spec, bubble.X + bubble.Size / 2.0, bubble.Y + bubble.Size / 2.0);
                _renderer.Pop(bubble, () => _bubbles.Remove(bubble));
                return;
            }

            if (spec.IsEscort)
            {
                BreakChaperoneShield(spec, bubble, escortPopped: true);
                _onBenignPop?.Invoke(spec);
                _renderer.Pop(bubble, () =>
                {
                    _bubbles.Remove(bubble);
                    OnBubblePopped?.Invoke();
                });
                return;
            }

            if (spec.IsDarter)
            {
                bool wasQuick = spec.QuickWindowMs > 0 && bubble.AgeMs <= spec.QuickWindowMs;
                _onDarterCaught?.Invoke(spec, wasQuick);
                _renderer.Pop(bubble, () =>
                {
                    _bubbles.Remove(bubble);
                    OnBubblePopped?.Invoke();
                });
                return;
            }

            if (spec.IsFreeze)
            {
                _onFreezeCaught?.Invoke(spec);
                _renderer.Pop(bubble, () =>
                {
                    _bubbles.Remove(bubble);
                    OnBubblePopped?.Invoke();
                });
                return;
            }

            if (spec.IsLive && !bubble.IsDefused && !bubble.IsDetonated)
            {
                bubble.IsDefused = true;
                _onDefuse?.Invoke(spec, bubble.FuseRemainingMs / 1000.0, false);

                if (bubble.BoundPairId != 0)
                {
                    var mate = _bubbles.FirstOrDefault(b =>
                        b != bubble
                        && b.BoundPairId == bubble.BoundPairId
                        && !b.IsPopping
                        && !b.IsDefused
                        && !b.IsDetonated);
                    if (mate != null)
                    {
                        mate.BoundHalfResolved = true;
                        mate.BoundResolveTimeRemainingMs = spec.BoundWindowMs > 0 ? spec.BoundWindowMs : 3500;
                    }
                }
            }
            else
            {
                _onBenignPop?.Invoke(spec);

                // Live knob read at the use site (S4b-4): a mid-run Chain Reaction level-up
                // widens the very next pop (WPF re-invokes the chainReach lambda per pop,
                // BubbleService.cs:1610).
                if (!spec.IsDarter && !spec.IsFreeze && Knobs.ChainReachDip > 0)
                {
                    ChainPop(bubble);
                }
            }

            _renderer.Pop(bubble, () =>
            {
                _bubbles.Remove(bubble);
                OnBubblePopped?.Invoke();
            });
            return;
        }

        bubble.IsPopping = true;
        // Trigger Bubbles: a gated ambient effect bubble fires its payload on pop.
        if (bubble.EffectPayload is { } payload)
        {
            try { payload.Fire(); } catch { }
        }
        _renderer.Pop(bubble, () =>
        {
            _bubbles.Remove(bubble);
            OnBubblePopped?.Invoke();
        });
    }

    // ---- Chaos mode public API ----

    public void BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Action<ChaosBubbleSpec, bool>? onDarterCaught = null,
        Action<ChaosBubbleSpec>? onFreezeCaught = null,
        Action<ChaosBubbleSpec, bool>? onChaperoneShieldBroken = null,
        Action<ChaosBubbleSpec>? onBoundEnraged = null,
        Action<ChaosBubbleSpec>? onTeaseTouched = null,
        Action<ChaosBubbleSpec>? onTeaseDenied = null,
        Action<ChaosBubbleSpec>? onBrittleShattered = null,
        Action<ChaosBubbleSpec>? onTreatExpired = null,
        Action<ChaosBubbleSpec, bool>? onDarterSpanked = null,
        double chainReachDip = DefaultChainReachDip,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null,
        Action<IReadOnlyList<(Point From, Point To)>>? onEStimArc = null,
        Action<Point, double, bool>? onDarterTrail = null)
    {
        _onBenignPop = onBenignPop;
        _onDefuse = onDefuse;
        _onDetonate = onDetonate;
        _onDarterCaught = onDarterCaught;
        _onFreezeCaught = onFreezeCaught;
        _onChaperoneShieldBroken = onChaperoneShieldBroken;
        _onBoundEnraged = onBoundEnraged;
        _onTeaseTouched = onTeaseTouched;
        _onTeaseDenied = onTeaseDenied;
        _onBrittleShattered = onBrittleShattered;
        _onTreatExpired = onTreatExpired;
        _onDarterSpanked = onDarterSpanked;
        // WPF BeginChaosMode resets every live-knob sample at run start (WPF BubbleService.cs:
        // 1092-1093, :1106); the chainReachDip parameter is only the SEED written into Knobs so
        // existing callers/fakes keep the pre-S4b-4 default reach — the chaos service overwrites
        // all knobs via SyncKnobsFromState right after this call.
        Knobs.Reset();
        Knobs.ChainReachDip = chainReachDip > 0 ? chainReachDip : DefaultChainReachDip;
        _canChannel = canChannel;
        _onChannelStarted = onChannelStarted;
        _onChannelBroken = onChannelBroken;
        _onEStimArc = onEStimArc;
        _onDarterTrail = onDarterTrail;
        _chaosActive = true;
        _chaosFrozen = false;
        _chaosTimeScale = 1.0;
        _chaosInputLocked = false;
        _channelBubbleId = null;

        if (!_isRunning)
            Start();
        else if (_animTimer == null)
        {
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(32) };
            _animTimer.Tick += (_, _) => Tick();
            _animTimer.Start();
        }
    }

    public void EndChaosMode()
    {
        _chaosActive = false;
        _onBenignPop = null;
        _onDefuse = null;
        _onDetonate = null;
        _onDarterCaught = null;
        _onFreezeCaught = null;
        _onChaperoneShieldBroken = null;
        _onBoundEnraged = null;
        _onTeaseTouched = null;
        _onTeaseDenied = null;
        _onBrittleShattered = null;
        _onTreatExpired = null;
        _onDarterSpanked = null;
        _canChannel = null;
        _onChannelStarted = null;
        _onChannelBroken = null;
        _onEStimArc = null;
        _onDarterTrail = null;
        _channelBubbleId = null;
        _chaosSpawnQueue.Clear();
        _pendingChaperoneEscorts.Clear();
        _pendingChaperoneLives.Clear();
        _pendingBoundPairIds.Clear();
        _ripples.Clear();
        _residues.Clear();
        _playerRipples.Clear();
        // WPF ClearChaos resets every live-knob static on teardown (WPF BubbleService.cs:1674-1687).
        Knobs.Reset();

        foreach (var bubble in _bubbles.Where(b => b.Spec != null).ToList())
        {
            _bubbles.Remove(bubble);
            _renderer.Destroy(bubble.Id);
        }
    }

    public void SpawnChaosBubble(ChaosBubbleSpec spec)
    {
        if (!_chaosActive) return;
        _chaosSpawnQueue.Enqueue(spec);
    }

    public void SpawnChaosChaperone(ChaosBubbleSpec live, ChaosBubbleSpec escort)
    {
        if (!_chaosActive) return;
        _pendingChaperoneEscorts[live.Id] = escort.Id;
        _pendingChaperoneLives[escort.Id] = live.Id;
        _chaosSpawnQueue.Enqueue(live);
        _chaosSpawnQueue.Enqueue(escort);
    }

    public void SpawnChaosBoundPair(ChaosBubbleSpec a, ChaosBubbleSpec b)
    {
        if (!_chaosActive) return;
        var pairId = _nextBoundPairId++;
        _pendingBoundPairIds[a.Id] = pairId;
        _pendingBoundPairIds[b.Id] = pairId;
        _chaosSpawnQueue.Enqueue(a);
        _chaosSpawnQueue.Enqueue(b);
    }

    public void PopNearestBenign()
    {
        if (!_chaosActive) return;
        var cursorPhys = _pointerState.GetCursorPosition();
        if (!cursorPhys.HasValue) return;

        var cursorDipX = cursorPhys.Value.X;
        var cursorDipY = cursorPhys.Value.Y;

        BubbleState? best = null;
        double bestDistSq = double.MaxValue;

        foreach (var bubble in _bubbles)
        {
            if (bubble.IsPopping || bubble.Spec == null) continue;
            var spec = bubble.Spec;
            if (spec.IsLive || spec.IsDarter || spec.IsFreeze) continue;

            var cx = (bubble.X + bubble.Size / 2.0) * bubble.Scaling;
            var cy = (bubble.Y + bubble.Size / 2.0) * bubble.Scaling;
            var dx = cursorDipX - cx;
            var dy = cursorDipY - cy;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = bubble;
            }
        }

        if (best != null)
            PopBubble(best.Id);
    }

    public void DefuseAllLive()
    {
        if (!_chaosActive) return;
        foreach (var bubble in _bubbles.ToList())
        {
            if (bubble.IsPopping || bubble.Spec == null) continue;
            var spec = bubble.Spec;
            if (spec.IsLive && !spec.IsDarter && !spec.IsFreeze)
                PopBubble(bubble.Id);
        }
    }

    public void PopAllChaosPaid()
    {
        if (!_chaosActive) return;
        foreach (var bubble in _bubbles.ToList())
        {
            if (bubble.IsPopping || bubble.Spec == null) continue;
            var spec = bubble.Spec;
            if (!spec.IsDarter && !spec.IsFreeze)
                PopBubble(bubble.Id);
        }
    }

    public void SetChaosFrozen(bool frozen) => _chaosFrozen = frozen;

    public void SetChaosTimeScale(double scale) => _chaosTimeScale = Math.Max(0.0, scale);

    public void SetChaosInputLocked(bool locked) => _chaosInputLocked = locked;

    /// <summary>
    /// Starts a player hold-to-defuse channel on the given live bubble.
    /// If the focus gate rejects the channel, the bubble detonates immediately.
    /// </summary>
    public void BeginChaosChannel(Guid bubbleId)
    {
        if (!_chaosActive || _chaosInputLocked) return;

        var bubble = GetChaosBubble(bubbleId);
        if (bubble?.Spec is not { IsLive: true } spec || bubble.IsPopping || bubble.IsChanneling)
            return;

        if (spec.IsChaperoneLive && bubble.IsShielded)
            return; // chaperone shield bounces the press

        // WPF parity (BubbleService.cs:3579): a NULL gate means "channel allowed" —
        // `_canChannelDefuse?.Invoke(this) == false` only detonates when a wired gate says no.
        // Do not invert this back to `?.Invoke(spec) != true`: that made a missing gate an
        // instant detonation for every live press.
        if (_canChannel != null && !_canChannel(spec))
        {
            _onChannelBroken?.Invoke(spec, "nofocus");
            DetonateBubble(bubble, spec);
            return;
        }

        bubble.IsChanneling = true;
        _channelBubbleId = bubbleId;
        _channelStartUtc = DateTime.UtcNow;
        _onChannelStarted?.Invoke(spec);
    }

    /// <summary>
    /// Ends a hold-to-defuse channel because the pointer was released.
    /// A short press below the click threshold detonates with reason "click";
    /// a longer incomplete hold detonates with reason "release".
    /// </summary>
    public void EndChaosChannel(Guid bubbleId)
    {
        if (!_chaosActive || _channelBubbleId != bubbleId) return;

        var bubble = GetChaosBubble(bubbleId);
        if (bubble?.Spec is not { } spec)
        {
            _channelBubbleId = null;
            return;
        }

        bubble.IsChanneling = false;
        _channelBubbleId = null;

        var elapsedMs = (DateTime.UtcNow - _channelStartUtc).TotalMilliseconds;
        var reason = elapsedMs < ChaosTuning.CLICK_THRESHOLD_MS ? "click" : "release";
        _onChannelBroken?.Invoke(spec, reason);
        DetonateBubble(bubble, spec);
    }

    /// <summary>
    /// Ends whichever live bubble channel is currently active, regardless of which
    /// bubble initiated it. Used by shared-host mouse-hook release events.
    /// </summary>
    public void EndActiveChaosChannel()
    {
        if (!_chaosActive || !_channelBubbleId.HasValue) return;
        EndChaosChannel(_channelBubbleId.Value);
    }

    private void DetonateBubble(BubbleState bubble, ChaosBubbleSpec spec)
    {
        if (bubble.IsPopping) return;
        bubble.IsDetonated = true;
        // WPF Detonate() sets _isPopping BEFORE _onDetonate (BubbleService.cs:3960-3961), and every
        // pay path guards on it, so a same-frame AoE cannot re-pay the detonated live (IMP-ECON1).
        bubble.IsPopping = true;
        _onDetonate?.Invoke(spec);
        _renderer.Pop(bubble, () => _bubbles.Remove(bubble));
    }

    /// <summary>Sets the Tail-Plug rabbit/darter trail duration in seconds — thin wrapper over
    /// <see cref="ChaosRunKnobs.RabbitTrailSec"/> with the WPF per-tick clamp
    /// (WPF BubbleService.cs:490 <c>Max(0, …)</c>). Kept so existing callers don't break.</summary>
    public void SetRabbitTrailSec(double seconds) => Knobs.RabbitTrailSec = Math.Max(0.0, seconds);

    /// <summary>Size Queen: add an expanding chaos ripple from the given physical pixel center.</summary>
    public void TriggerChaosRipple(Point centerPx)
    {
        if (!_chaosActive) return;
        _ripples.Add((centerPx, 0.0));
    }

    /// <summary>Aftermath: add a residue zone at the given physical pixel center.</summary>
    public void AddChaosResidue(Point centerPx)
    {
        if (!_chaosActive) return;
        _residues.Add((centerPx, DateTime.UtcNow.AddMilliseconds(RESIDUE_LIFE_MS)));
    }

    /// <summary>The Ripple: add a strong player ripple from a right-click at the given physical pixel center.</summary>
    public void TriggerPlayerRipple(Point centerPx)
    {
        TriggerPlayerRipple(centerPx, RIPPLE_RADIUS_PX, RIPPLE_LIFE_MS);
    }

    /// <summary>The Ripple: add a strong player ripple with explicit reach and duration.</summary>
    public void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (!_chaosActive) return;
        _playerRipples.Add(new PlayerRipple
        {
            CenterPx = centerPx,
            RadiusPx = radiusPx > 0 ? radiusPx : RIPPLE_RADIUS_PX,
            LifeMs = lifeMs > 0 ? lifeMs : RIPPLE_LIFE_MS,
            AgeMs = 0.0
        });
    }

    /// <summary>
    /// Shared-host left-click hit test. Pops the top-most clickable chaos bubble under the point.
    /// Returns true if the click should be swallowed (instant pops); false if it should propagate
    /// (miss, or a hold-to-defuse live bubble so GetAsyncKeyState can read the held button).
    /// </summary>
    public bool OnSharedHostLeftDown(Point centerPx)
    {
        if (!_chaosActive) return false;

        // Iterate in reverse: the most recently spawned bubble is drawn on top.
        for (int i = _bubbles.Count - 1; i >= 0; i--)
        {
            var bubble = _bubbles[i];
            if (bubble.IsPopping || bubble.Spec == null) continue;
            var spec = bubble.Spec;
            if (!spec.IsLive && !bubble.Clickable) continue;

            var cx = (bubble.X + bubble.Size / 2.0) * bubble.Scaling;
            var cy = (bubble.Y + bubble.Size / 2.0) * bubble.Scaling;
            // Hit disc radius is the spawn-stamped HIT size, not the visual size — the Wand/
            // Mesmer/Silk Touch enlargement (WPF BubbleService.cs:2423 HitDiscPx = _hitSize/2 × dpi).
            var r = (bubble.HitSize / 2.0) * bubble.Scaling;
            var dx = cx - centerPx.X;
            var dy = cy - centerPx.Y;
            if (dx * dx + dy * dy > r * r) continue;

            // Live hold-to-defuse bubbles start a channel; everything else pops instantly.
            bool isLiveHold = spec.IsLive && !bubble.IsDefused && !bubble.IsDetonated && !bubble.IsShielded;
            if (isLiveHold)
            {
                BeginChaosChannel(bubble.Id);
                return false;
            }

            // S4b-3 (WPF BubbleService.cs:3706-3708): with the Spanker on — or ALWAYS for GG
            // sweepers — a darter pointer-down SMACKS the rabbit instead of catching it; spank
            // and catch are mutually exclusive ("No catch, no slow-mo: that's the trade").
            // The rabbit_caller lesson hook fires on the FIRST smack only (WPF :3789
            // `if (!_isSpanked)`); sweepers are born spanked and never reach it.
            if (spec.IsDarter && (Knobs.SpankerOn || spec.IsSweeper))
            {
                // WPF Spank() (BubbleService.cs:3773-3801): cooldown-gated; EVERY smack redirects
                // to a random heading at +18% pace (capped 2.2× natural) and resets its bounces;
                // the swell + latch + lesson hook happen ONCE on the first smack only.
                if (bubble.SpankCooldownMs > 0)
                    return true; // click consumed, smack ignored (WPF :3775 early-return)
                bubble.SpankCooldownMs = 250.0;   // WPF :3776
                var naturalSpeed = spec.DarterSpeed > 0
                    ? spec.DarterSpeed * WpfFramesPerSecond
                    : DefaultDarterSpeed;
                var spd = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);
                if (spd < 0.01 * WpfFramesPerSecond) spd = naturalSpeed;      // WPF :3778 (per-frame 0.01 floor)
                spd = Math.Min(spd * 1.18, naturalSpeed * 2.2);               // WPF :3780
                var heading = _random.NextDouble() * Math.PI * 2.0;           // WPF :3781
                bubble.Vx = Math.Cos(heading) * spd;
                bubble.Vy = Math.Sin(heading) * spd;
                bubble.BounceCount = 0;                                       // WPF :3785 fresh bounces
                if (!bubble.IsSpanked)
                {
                    bubble.IsSpanked = true;
                    // The swell happens ONCE, on the first smack; re-smacks only steer and hurry
                    // it — no compounding (WPF :3794-3796 _spankGrowth = Max(1.0, ChaosSpankGrowNow)).
                    bubble.SpankGrowth = Math.Max(1.0, Knobs.SpankGrow);
                    // WPF renders the swell through the darter's per-frame scale
                    // (_scale = _spankGrowth + throb, WPF :3001); the engine has no throb wobble,
                    // so the stamped growth IS the render scale.
                    bubble.Scale = bubble.SpankGrowth;
                    bool quick = spec.QuickWindowMs > 0 && bubble.AgeMs <= spec.QuickWindowMs;
                    _onDarterSpanked?.Invoke(spec, quick);
                }
                return true; // click consumed; the darter survives (never caught)
            }

            PopBubble(bubble.Id);
            return true;
        }

        return false;
    }

    /// <summary>Returns the chaos bubble with the given id, or null if it does not exist.</summary>
    public BubbleState? GetChaosBubble(Guid id)
    {
        return _bubbles.FirstOrDefault(b => b.Id == id);
    }

    /// <summary>Testability seam: advances the simulation by one fixed-step tick (the body the
    /// _animTimer fires). Production never calls this — it exists so engine-path unit tests can
    /// step the spawn queue / physics / lifecycle deterministically without a live DispatcherTimer.</summary>
    internal void TickOnceForTesting() => Tick();

    /// <summary>
    /// Returns ids of chaos bubbles whose bounds intersect the given rectangle in
    /// PHYSICAL virtual-desktop pixels (the space the WH_MOUSE_LL hook and the gaze
    /// pipeline report). Engine-internal bubble geometry is per-screen logical units
    /// (physical / screen scaling), so each bubble rect is converted with its own
    /// screen's scaling before the compare — correct on mixed-DPI setups.
    /// </summary>
    public IReadOnlyList<Guid> GetChaosBubblesInRect(PixelRect rectPx)
    {
        var result = new List<Guid>();
        if (!_chaosActive) return result;

        foreach (var bubble in _bubbles)
        {
            if (bubble.Spec == null || bubble.IsPopping) continue;
            var s = bubble.Scaling > 0 ? bubble.Scaling : 1.0;
            // The gaze/rect target is the HIT box centred on the bubble, not the visual box —
            // WPF GetGazeBounds builds the same _hitSize-centred rect (WPF BubbleService.cs:4383).
            var pad = (bubble.HitSize - bubble.Size) / 2.0;
            var r = new PixelRect((bubble.X - pad) * s, (bubble.Y - pad) * s, bubble.HitSize * s, bubble.HitSize * s);
            if (r.X < rectPx.Right && r.Right > rectPx.X && r.Y < rectPx.Bottom && r.Bottom > rectPx.Y)
                result.Add(bubble.Id);
        }

        return result;
    }

    // IMP-9: reusable per-tick buffers, avoiding ~4 Gen0 allocs/frame in bubble-mode steady
    // state. SAFE ONLY because Tick is non-reentrant (single UI-thread DispatcherTimer; no
    // pop/miss callback pumps a nested dispatcher frame) — a nested Tick would Clear() these
    // mid-enumeration. Cleared in Stop() so a stopped run does not root the last tick's bubbles.
    // _tickSnapshot is the ONE shared hazard-pass snapshot; safety note at the call site below.
    private readonly List<BubbleState> _missedBuffer = new();
    private readonly List<BubbleState> _movedBuffer = new();
    private readonly List<BubbleState> _tickSnapshot = new();

    private void Tick()
    {
        if (!_isRunning || _isPaused) return;

        // Global FIELD_PACE folds in here so the one knob slows BOTH ambient and chaos bubbles:
        // every motion step and countdown reads this. See ChaosTuning.FIELD_PACE for the why.
        var dt = TickIntervalSec * _chaosTimeScale * ChaosTuning.FIELD_PACE;
        if (_chaosFrozen) dt = 0.0;

        // Manual pause quietly cancels any active channel (no detonation, no completion).
        if (_chaosInputLocked && _channelBubbleId.HasValue)
        {
            var pausedBubble = GetChaosBubble(_channelBubbleId.Value);
            if (pausedBubble != null) pausedBubble.IsChanneling = false;
            _channelBubbleId = null;
        }

        _missedBuffer.Clear();
        _movedBuffer.Clear();
        var missed = _missedBuffer;
        var moved = _movedBuffer;

        // Spawn queued chaos bubbles first so they appear this frame.
        DrainChaosSpawnQueue();

        foreach (var bubble in _bubbles)
        {
            if (bubble.IsPopping) continue;

            if (bubble.Spec is { } spec)
            {
                TickChaosBubble(bubble, spec, dt, missed, moved);
                continue;
            }

            bubble.X += bubble.Vx * dt;
            bubble.Y += bubble.Vy * dt;
            bubble.LifeRemainingSec -= dt;

            // Wrap horizontal drift inside the screen bounds.
            if (bubble.X < bubble.ScreenBounds.X)
                bubble.X = bubble.ScreenBounds.X;
            else if (bubble.X + bubble.Size > bubble.ScreenBounds.Right)
                bubble.X = bubble.ScreenBounds.Right - bubble.Size;

            if (bubble.LifeRemainingSec <= 0 || bubble.Y + bubble.Size < bubble.ScreenBounds.Y)
                missed.Add(bubble);
            else
                moved.Add(bubble);
        }

        // Spanked-rabbit body mows (runs on a snapshot — pops mutate _bubbles) BEFORE the field
        // hazards, mirroring WPF where sweeps fire inside each darter's AnimateFrame and
        // TickFieldHazards runs after the animate pass (WPF BubbleService.cs:3075-3076, :505ff).
        // ONE shared snapshot for both hazard passes (reused buffer, zero-alloc steady state):
        // PopBubble sets IsPopping before any removal and every pass guards on IsPopping, so a
        // bubble popped by the spank pass is skipped by the field pass exactly as it was when
        // each pass took its own _bubbles.ToArray(). Built only in chaos mode — both passes
        // early-return on !_chaosActive/_chaosFrozen, so ambient ticks skip the copy entirely
        // (matching pre-IMP-9 behaviour where neither pass reached its ToArray).
        if (_chaosActive && !_chaosFrozen)
        {
            _tickSnapshot.Clear();
            _tickSnapshot.AddRange(_bubbles);
            TickSpankSweeps(_tickSnapshot);
            TickFieldHazards(dt, _tickSnapshot);
        }

        foreach (var bubble in missed)
        {
            if (bubble.Spec?.IsEscort == true)
                BreakChaperoneShield(bubble.Spec, bubble, escortPopped: false);

            _bubbles.Remove(bubble);
            _renderer.Destroy(bubble.Id);
            OnBubbleMissed?.Invoke();
        }

        foreach (var bubble in moved)
        {
            _renderer.Move(bubble);
        }
    }

    private void DrainChaosSpawnQueue()
    {
        if (!_chaosActive) return;

        int spawned = 0;
        while (spawned < MaxSpawnsPerFrame && _chaosSpawnQueue.TryDequeue(out var spec))
        {
            spawned++;
            var state = MaterializeChaosSpec(spec);
            if (state != null)
            {
                _bubbles.Add(state);
                _renderer.Create(state);
            }
        }
    }

    private BubbleState? MaterializeChaosSpec(ChaosBubbleSpec spec)
    {
        // WPF parity (BubbleService.cs:855): bubbles confine to the primary monitor
        // when DualMonitorEnabled is off.
        var screens = _screenProvider.GetEffectScreens(_settings.Current?.DualMonitorEnabled != false);
        int screenIndex;
        ScreenInfo screen;
        if (screens.Count == 0)
        {
            screenIndex = 0;
            screen = new ScreenInfo("fallback",
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1080),
                1.0);
        }
        else
        {
            screenIndex = _random.Next(screens.Count);
            screen = screens[screenIndex];
        }

        var scaling = screen.Scaling;
        var working = screen.WorkingArea;
        var bounds = new PixelRect(
            working.X / scaling,
            working.Y / scaling,
            working.Width / scaling,
            working.Height / scaling);

        var size = Math.Max(20.0, spec.SizePx);
        var (x, y, vx, vy) = ComputeChaosSpawn(spec, bounds, size);

        var lifeMs = spec.TreatLifeMs > 0 ? spec.TreatLifeMs : spec.LifetimeMs;
        if (lifeMs <= 0) lifeMs = 5000;

        // Hit-disc enlargement + Blindfold opacity are sampled AT SPAWN, exactly like the WPF
        // Bubble ctor (WPF BubbleService.cs:2532-2542) — a mid-run silk_touch/Blindfold change
        // affects new spawns only, never bubbles already on screen. "Plain" effect bubbles are
        // lives + ordinary treats; darters/freeze keep precision hitboxes and pickups/prism/
        // escort/tease/brittle stay fully visible with natural hitboxes (WPF :2532-2538).
        bool plainEffectBubble = !spec.IsDarter && !spec.IsFreeze && !spec.IsGolden && !spec.IsHeart
                                 && !spec.IsDroplet && !spec.IsPrism && !spec.IsEscort
                                 && !spec.IsTease && !spec.IsBrittle;
        double hitMult = Math.Clamp(Knobs.HitboxScale, 1.0, 2.0);                       // WPF :2539
        if (Knobs.LiveMagnet && spec.IsLive) hitMult = Math.Clamp(hitMult * 1.4, 1.0, 2.0); // WPF :2540

        var state = new BubbleState
        {
            Id = spec.Id,
            ScreenIndex = screenIndex,
            ScreenBounds = bounds,
            Scaling = scaling,
            X = x,
            Y = y,
            Vx = vx * spec.SpeedMult,
            Vy = vy * spec.SpeedMult,
            Size = size,
            MaxLifeSec = lifeMs / 1000.0,
            LifeRemainingSec = lifeMs / 1000.0,
            Clickable = true,
            Spec = spec,
            FuseRemainingMs = spec.IsLive ? spec.FuseMs : 0.0,
            AgeMs = 0.0,
            HitSize = plainEffectBubble ? Math.Max(size, Math.Round(size * hitMult)) : size, // WPF :2541
            Opacity = plainEffectBubble ? Math.Clamp(Knobs.BubbleOpacity, 0.2, 1.0) : 1.0    // WPF :2542
        };

        if (spec.IsDarter)
        {
            // GG sweepers are born spanked (WPF BubbleService.cs:3787 "sweepers are born spanked
            // and never land here") — the first-smack lesson latch is pre-set so the
            // rabbit_caller hook never fires for a sweeper.
            state.IsSpanked = spec.IsSweeper;
            var angle = _random.NextDouble() * Math.PI * 2.0;
            // WPF DARTER_SPEED is DIPs PER 32ms FRAME (WPF ChaosBubbleVariants.cs:141 "9.0
            // DIPs/frame"); this engine integrates velocities per SECOND (X += Vx*dt), so a
            // catalog-built spec converts at materialize (9.0/frame ≈ 281 DIP/s).
            var speed = spec.DarterSpeed > 0 ? spec.DarterSpeed * WpfFramesPerSecond : DefaultDarterSpeed;
            state.Vx = Math.Cos(angle) * speed;
            state.Vy = Math.Sin(angle) * speed;
            state.TelegraphRemainingMs = spec.TelegraphMs > 0 ? spec.TelegraphMs : DefaultDarterTelegraphMs;
            state.LastTrailEmitPx = new Point((x + size / 2.0) * scaling, (y + size / 2.0) * scaling);
        }

        if (_pendingChaperoneEscorts.TryGetValue(spec.Id, out var escortId))
        {
            state.IsShielded = true;
            state.ChaperoneEscortId = escortId;
            _pendingChaperoneEscorts.Remove(spec.Id);
        }

        if (_pendingChaperoneLives.TryGetValue(spec.Id, out var liveId))
        {
            state.ChaperoneLiveId = liveId;
            _pendingChaperoneLives.Remove(spec.Id);
        }

        if (_pendingBoundPairIds.TryGetValue(spec.Id, out var pairId))
        {
            state.BoundPairId = pairId;
            _pendingBoundPairIds.Remove(spec.Id);
        }

        return state;
    }

    private (double x, double y, double vx, double vy) ComputeChaosSpawn(ChaosBubbleSpec spec, PixelRect bounds, double size)
    {
        if (spec.SpawnAtPxX.HasValue && spec.SpawnAtPxY.HasValue)
        {
            return (spec.SpawnAtPxX.Value, spec.SpawnAtPxY.Value, 0, 0);
        }

        var baseSpeed = spec.IsDarter && spec.DarterSpeed > 0 ? spec.DarterSpeed : 80.0;

        switch (spec.Motion)
        {
            case ChaosMotion.RainDown:
            {
                var x = bounds.X + _random.NextDouble() * Math.Max(1, bounds.Width - size);
                var y = bounds.Y - size;
                return (x, y, (_random.NextDouble() - 0.5) * 20.0, baseSpeed);
            }
            case ChaosMotion.RoamBounce:
            {
                var x = bounds.X + (bounds.Width - size) / 2.0 + (_random.NextDouble() - 0.5) * (bounds.Width / 4.0);
                var y = bounds.Y + (bounds.Height - size) / 2.0 + (_random.NextDouble() - 0.5) * (bounds.Height / 4.0);
                var angle = _random.NextDouble() * Math.PI * 2.0;
                var vx = Math.Cos(angle) * baseSpeed;
                var vy = Math.Sin(angle) * baseSpeed;
                return (x, y, vx, vy);
            }
            case ChaosMotion.SideDrift:
            {
                var fromLeft = _random.NextDouble() > 0.5;
                var x = fromLeft ? bounds.X - size : bounds.Right;
                var y = bounds.Y + _random.NextDouble() * Math.Max(1, bounds.Height - size);
                var vx = fromLeft ? baseSpeed : -baseSpeed;
                return (x, y, vx, (_random.NextDouble() - 0.5) * 30.0);
            }
            case ChaosMotion.FloatUp:
            default:
            {
                var x = bounds.X + _random.NextDouble() * Math.Max(1, bounds.Width - size);
                var y = bounds.Bottom;
                return (x, y, (_random.NextDouble() - 0.5) * 20.0, -baseSpeed);
            }
        }
    }

    private void TickChaosBubble(BubbleState bubble, ChaosBubbleSpec spec, double dt, List<BubbleState> missed, List<BubbleState> moved)
    {
        bubble.AgeMs += dt * 1000.0;

        // Spank cooldown burns in real time, unscaled by slow-mo/freeze (WPF BubbleService.cs:3073
        // decrements a flat -32 per frame regardless of ts).
        if (bubble.SpankCooldownMs > 0)
            bubble.SpankCooldownMs -= TickIntervalSec * 1000.0;

        if (bubble.BoundHalfResolved && bubble.BoundResolveTimeRemainingMs > 0 && !bubble.BoundEnraged)
        {
            bubble.BoundResolveTimeRemainingMs -= dt * 1000.0;
            if (bubble.BoundResolveTimeRemainingMs <= 0)
            {
                // S4b-1: ENRAGE the survivor in place — do NOT detonate/pop it. WPF halves the
                // remaining fuse (min 600ms) and scales drift by BOUND_ENRAGE_SPEED_MULT, then the
                // bubble STAYS ALIVE as a normal defusable live (WPF BubbleService.cs:2321-2335
                // Enrage()). The BoundEnraged latch + the !BoundEnraged guard above stop re-entry;
                // fall through to the normal fuse/motion tick below so the survivor keeps burning.
                bubble.BoundEnraged = true;
                bubble.FuseRemainingMs = Math.Max(600.0, bubble.FuseRemainingMs / 2.0);
                bubble.Vx *= ChaosTuning.BOUND_ENRAGE_SPEED_MULT;
                bubble.Vy *= ChaosTuning.BOUND_ENRAGE_SPEED_MULT;
                _onBoundEnraged?.Invoke(spec);
            }
        }

        if (spec.IsBrittle)
        {
            var cursorPhys = _pointerState.GetCursorPosition();
            if (cursorPhys.HasValue)
            {
                var cursorPx = cursorPhys.Value.X;
                var cursorPy = cursorPhys.Value.Y;
                var bubbleCxPhys = (bubble.X + bubble.Size / 2.0) * bubble.Scaling;
                var bubbleCyPhys = (bubble.Y + bubble.Size / 2.0) * bubble.Scaling;
                var dx = cursorPx - bubbleCxPhys;
                var dy = cursorPy - bubbleCyPhys;
                var hitRadiusPhys = (bubble.Size / 2.0) * bubble.Scaling;
                if (dx * dx + dy * dy <= hitRadiusPhys * hitRadiusPhys)
                {
                    bubble.IsPopping = true;
                    _onBrittleShattered?.Invoke(spec);
                    _onDetonate?.Invoke(spec);
                    missed.Add(bubble);
                    return;
                }
            }
        }

        if (spec.IsDarter && !bubble.TelegraphComplete)
        {
            bubble.TelegraphRemainingMs -= dt * 1000.0;
            if (bubble.TelegraphRemainingMs <= 0)
            {
                bubble.TelegraphComplete = true;
                bubble.Scale = 1.0;
            }
            else
            {
                bubble.Scale = 1.0 + 0.15 * Math.Sin(bubble.AgeMs / 80.0);
            }
        }
        else if (!bubble.IsChanneling)
        {
            // The Pull: rabbits fly AT you — steer the darter's velocity toward the cursor with a
            // capped turn rate (WPF BubbleService.cs:3023-3039: maxTurn = 0.065 × Max(ts, 0.4)
            // rad/frame; one engine tick == one WPF 32ms frame, ts == dt/TickIntervalSec since
            // WPF's TimeScale folds FIELD_PACE too, WPF :2169). The engine has no darter-escape
            // phase (max bounces despawn instead), so WPF's !_darterEscaping gate has no analogue.
            if (spec.IsDarter && bubble.TelegraphComplete && Knobs.RabbitHoming && dt > 0)
            {
                var homeCursor = _pointerState.GetCursorPosition();
                if (homeCursor.HasValue)
                {
                    double hcx = homeCursor.Value.X / bubble.Scaling;
                    double hcy = homeCursor.Value.Y / bubble.Scaling;
                    double hdx = hcx - (bubble.X + bubble.Size / 2.0);
                    double hdy = hcy - (bubble.Y + bubble.Size / 2.0);
                    if (hdx * hdx + hdy * hdy > 1)
                    {
                        double want = Math.Atan2(hdy, hdx);
                        double cur = Math.Atan2(bubble.Vy, bubble.Vx);
                        double diff = want - cur;
                        while (diff > Math.PI) diff -= 2 * Math.PI;
                        while (diff < -Math.PI) diff += 2 * Math.PI;
                        double ts = dt / TickIntervalSec;
                        double maxTurn = 0.065 * Math.Max(ts, 0.4);   // WPF :3037 (~3.7°/frame)
                        double na = cur + Math.Clamp(diff, -maxTurn, maxTurn);
                        double spd = Math.Sqrt(bubble.Vx * bubble.Vx + bubble.Vy * bubble.Vy);
                        bubble.Vx = Math.Cos(na) * spd;
                        bubble.Vy = Math.Sin(na) * spd;
                    }
                }
            }

            // WPF parity (BubbleService.cs:2971): a channeling bubble is PINNED in place so it
            // cannot drift out of its own hit circle mid-hold "for free". Motion resumes if the
            // channel breaks.
            bubble.X += bubble.Vx * dt;
            bubble.Y += bubble.Vy * dt;

            if (spec.Motion == ChaosMotion.RoamBounce || spec.IsDarter)
            {
                bool bounced = false;
                if (bubble.X < bubble.ScreenBounds.X)
                {
                    bubble.X = bubble.ScreenBounds.X;
                    bubble.Vx = Math.Abs(bubble.Vx);
                    bounced = true;
                }
                else if (bubble.X + bubble.Size > bubble.ScreenBounds.Right)
                {
                    bubble.X = bubble.ScreenBounds.Right - bubble.Size;
                    bubble.Vx = -Math.Abs(bubble.Vx);
                    bounced = true;
                }

                if (bubble.Y < bubble.ScreenBounds.Y)
                {
                    bubble.Y = bubble.ScreenBounds.Y;
                    bubble.Vy = Math.Abs(bubble.Vy);
                    bounced = true;
                }
                else if (bubble.Y + bubble.Size > bubble.ScreenBounds.Bottom)
                {
                    bubble.Y = bubble.ScreenBounds.Bottom - bubble.Size;
                    bubble.Vy = -Math.Abs(bubble.Vy);
                    bounced = true;
                }

                if (bounced && spec.IsDarter)
                {
                    var maxBounces = spec.DarterMaxBounces > 0 ? spec.DarterMaxBounces : 3;
                    bubble.BounceCount++;
                    if (bubble.BounceCount >= maxBounces)
                    {
                        missed.Add(bubble);
                        return;
                    }
                }
            }
            else
            {
                // Loose screen containment; mark missed once well off-screen.
                const double margin = 200.0;
                if (bubble.X + bubble.Size < bubble.ScreenBounds.X - margin
                    || bubble.X > bubble.ScreenBounds.Right + margin
                    || bubble.Y + bubble.Size < bubble.ScreenBounds.Y - margin
                    || bubble.Y > bubble.ScreenBounds.Bottom + margin)
                {
                    missed.Add(bubble);
                    return;
                }
            }

            // The Pull / Cam Girl: the whole field leans toward (or squirms away from) the cursor
            // (WPF BubbleService.cs:3213-3247). Ordinary chaos bubbles only — darters have their
            // own homing above, escorts orbit their live and the tease wiggles in place (both
            // skip the pull via `goto Visuals` in WPF, :3117-3166). CursorPull is in WPF DIPs per
            // 32ms frame; × WpfFramesPerSecond × dt reproduces WPF's `step = pull * ts` exactly.
            if (Knobs.CursorPull != 0 && dt > 0 && !spec.IsDarter && !spec.IsEscort && !spec.IsTease)
            {
                var pullCursor = _pointerState.GetCursorPosition();
                if (pullCursor.HasValue)
                {
                    double pull = Knobs.CursorPull;
                    double pcx = pullCursor.Value.X / bubble.Scaling;
                    double pcy = pullCursor.Value.Y / bubble.Scaling;
                    double bcx = bubble.X + bubble.Size / 2.0;
                    double bcy = bubble.Y + bubble.Size / 2.0;
                    if (pull > 0)
                    {
                        double pdx = pcx - bcx, pdy = pcy - bcy;
                        double pd = Math.Sqrt(pdx * pdx + pdy * pdy);
                        if (pd > 30)   // dead zone — no jitter right under the cursor (WPF :3221)
                        {
                            double step = pull * WpfFramesPerSecond * dt;   // WPF :3223 step = pull * ts
                            bubble.X += pdx / pd * step;
                            bubble.Y += pdy / pd * step;
                        }
                    }
                    else
                    {
                        // Cam Girl: repulsion only bites when the cursor closes in, fades with
                        // distance, and is clamped on-screen so the flee can't shove a bubble out
                        // of reach for good (WPF :3230-3245, FLEE_RADIUS = 260).
                        const double FleeRadiusDip = 260.0;
                        double fdx = bcx - pcx, fdy = bcy - pcy;
                        double fd = Math.Sqrt(fdx * fdx + fdy * fdy);
                        if (fd < FleeRadiusDip && fd > 1)
                        {
                            double step = -pull * WpfFramesPerSecond * dt * (1.0 - fd / FleeRadiusDip);   // WPF :3240
                            double maxX = Math.Max(bubble.ScreenBounds.X, bubble.ScreenBounds.Right - bubble.Size);
                            double maxY = Math.Max(bubble.ScreenBounds.Y, bubble.ScreenBounds.Bottom - bubble.Size);
                            bubble.X = Math.Clamp(bubble.X + fdx / fd * step, bubble.ScreenBounds.X, maxX);
                            bubble.Y = Math.Clamp(bubble.Y + fdy / fd * step, bubble.ScreenBounds.Y, maxY);
                        }
                    }
                }
            }
        }

        // Tail-Plug: record trail points for darter/rabbit bubbles while the boon holds. GG
        // sweepers ALWAYS drag a trail — an amber flair streak, min 0.5s — boon or not (WPF
        // BubbleService.cs:3078-3082); without the boon that trail never POPS anything
        // (the trail-pop sweep gates on the knob, WPF :1427).
        double trailSec = spec.IsSweeper ? Math.Max(0.5, Knobs.RabbitTrailSec) : Knobs.RabbitTrailSec;
        if (trailSec > 0 && spec.IsDarter && bubble.TelegraphComplete && !bubble.IsPopping)
        {
            var nowPx = CenterPx(bubble);
            var tdx = nowPx.X - bubble.LastTrailEmitPx.X;
            var tdy = nowPx.Y - bubble.LastTrailEmitPx.Y;
            if (tdx * tdx + tdy * tdy >= TRAIL_GAP_PX * TRAIL_GAP_PX || bubble.TrailPoints.Count == 0)
            {
                bubble.LastTrailEmitPx = nowPx;
                bubble.TrailPoints.Add((nowPx, DateTime.UtcNow));
                // Render a fading sparkle at the new trail point (WPF BubbleService.cs:3128-3129).
                _onDarterTrail?.Invoke(nowPx, trailSec, spec.IsSweeper);
            }
            var cutoff = DateTime.UtcNow.AddSeconds(-trailSec);
            while (bubble.TrailPoints.Count > 0 && bubble.TrailPoints[0].T < cutoff)
                bubble.TrailPoints.RemoveAt(0);
            if (bubble.TrailPoints.Count > TRAIL_MAX_POINTS)
                bubble.TrailPoints.RemoveAt(0);
        }
        else if (!spec.IsDarter || trailSec <= 0)
        {
            bubble.TrailPoints.Clear();
        }

        if (bubble.LifeRemainingSec <= 0)
        {
            if (spec.IsTease)
                _onTeaseDenied?.Invoke(spec);
            else if (IsRottingTreat(spec))
                _onTreatExpired?.Invoke(spec);

            missed.Add(bubble);
            return;
        }

        bubble.LifeRemainingSec -= dt;

        if (spec.IsLive && !bubble.IsDefused && !bubble.IsDetonated && !bubble.IsShielded)
        {
            if (bubble.IsChanneling)
            {
                // WPF parity (BubbleService.cs:3614-3631 TickChannel): the hold only counts while
                // the cursor stays on the bubble's hit disc — straying off it mid-hold detonates
                // with reason "release". Compared in PHYSICAL pixels: the pointer is physical and
                // the disc is engine-logical * this bubble's screen scaling (same math as
                // OnSharedHostLeftDown), so mixed-DPI rigs behave.
                var strayCursor = _pointerState.GetCursorPosition();
                if (strayCursor.HasValue)
                {
                    var discCx = (bubble.X + bubble.Size / 2.0) * bubble.Scaling;
                    var discCy = (bubble.Y + bubble.Size / 2.0) * bubble.Scaling;
                    // Stray reach is the spawn-stamped HIT size — WPF TickChannel measures against
                    // _hitSize/2, the enlarged disc (WPF BubbleService.cs:3623).
                    var discR = (bubble.HitSize / 2.0) * bubble.Scaling;
                    var strayDx = strayCursor.Value.X - discCx;
                    var strayDy = strayCursor.Value.Y - discCy;
                    if (strayDx * strayDx + strayDy * strayDy > discR * discR)
                    {
                        bubble.IsChanneling = false;
                        _channelBubbleId = null;
                        _onChannelBroken?.Invoke(spec, "release");
                        DetonateBubble(bubble, spec);
                        return;
                    }
                }

                // Hold-to-defuse: the fuse pauses while the channel is held.
                var elapsedMs = (DateTime.UtcNow - _channelStartUtc).TotalMilliseconds;
                double t = Math.Clamp(elapsedMs / ChaosTuning.DEFUSE_HOLD_MS, 0.0, 1.0);
                bubble.Scale = 1.0 - (1.0 - ChaosTuning.CHANNEL_MIN_SCALE) * t;

                if (elapsedMs >= ChaosTuning.DEFUSE_HOLD_MS)
                {
                    bubble.IsChanneling = false;
                    _channelBubbleId = null;
                    bubble.IsDefused = true;
                    // WPF CompleteDefuse() sets _isPopping BEFORE _onDefuse (BubbleService.cs:3688-3689)
                    // so a same-frame AoE cannot re-pay this defused live (IMP-ECON1).
                    bubble.IsPopping = true;
                    bubble.Scale = ChaosTuning.CHANNEL_MIN_SCALE;

                    if (bubble.BoundPairId != 0)
                    {
                        var mate = _bubbles.FirstOrDefault(b =>
                            b != bubble
                            && b.BoundPairId == bubble.BoundPairId
                            && !b.IsPopping
                            && !b.IsDefused
                            && !b.IsDetonated);
                        if (mate != null)
                        {
                            mate.BoundHalfResolved = true;
                            mate.BoundResolveTimeRemainingMs = spec.BoundWindowMs > 0 ? spec.BoundWindowMs : 3500;
                        }
                    }

                    _onDefuse?.Invoke(spec, bubble.FuseRemainingMs / 1000.0, true);
                    _renderer.Pop(bubble, () =>
                    {
                        _bubbles.Remove(bubble);
                        OnBubblePopped?.Invoke();
                    });
                    return;
                }

                _renderer.SetFuse(bubble.Id, Math.Clamp(bubble.FuseRemainingMs / spec.FuseMs, 0.0, 1.0));
            }
            else
            {
                bubble.FuseRemainingMs -= dt * 1000.0;
                if (bubble.FuseRemainingMs <= 0)
                {
                    bubble.FuseRemainingMs = 0;
                    bubble.IsDetonated = true;
                    // WPF parity (Detonate BubbleService.cs:3960-3961): set the pop latch before the
                    // payload callback so a same-tick AoE cannot re-pay this detonated live (IMP-ECON1).
                    bubble.IsPopping = true;

                    if (spec.IsEcho)
                    {
                        EchoSplitRequested?.Invoke(spec, bubble.X + bubble.Size / 2.0, bubble.Y + bubble.Size / 2.0);
                    }

                    _onDetonate?.Invoke(spec);
                    missed.Add(bubble);
                    return;
                }

                _renderer.SetFuse(bubble.Id, Math.Clamp(bubble.FuseRemainingMs / spec.FuseMs, 0.0, 1.0));
            }
        }

        moved.Add(bubble);
    }

    /// <summary>WPF parity (BubbleService.cs:2516 <c>_isTreat</c> def, Dissolve() :3907): the treat
    /// set that rots and fires the treat-expired callback when its screen life runs out. Ordinary
    /// treats (flash/subliminal/...), goldens and prisms rot; hearts, droplets, escorts, tease and
    /// brittle do NOT (kindness pickups never punish a miss; tease runs its own Denied expiry;
    /// brittle just drifts off). Mirrors WPF's <c>_isTreat</c> definition exactly.</summary>
    private static bool IsRottingTreat(ChaosBubbleSpec spec) =>
        !spec.IsLive && !spec.IsDarter && !spec.IsFreeze
        && !spec.IsHeart && !spec.IsDroplet && !spec.IsEscort
        && !spec.IsTease && !spec.IsBrittle;

    private void ChainPop(BubbleState source)
    {
        if (source.Spec == null || Knobs.ChainReachDip <= 0) return;

        var cx = source.X + source.Size / 2.0;
        var cy = source.Y + source.Size / 2.0;
        // Live knob read (S4b-4): WPF re-invokes the chainReach lambda on every pop
        // (BubbleService.cs:1610), so a mid-run boon level-up widens the next burst.
        var reachSq = Knobs.ChainReachDip * Knobs.ChainReachDip;

        foreach (var other in _bubbles.ToList())
        {
            if (other == source || other.IsPopping || other.Spec == null) continue;

            var ospec = other.Spec;
            if (!(ospec.IsGolden || ospec.IsHeart || ospec.IsDroplet)) continue;
            if (ospec.IsDarter || ospec.IsFreeze || ospec.IsTease || ospec.IsBrittle) continue;

            var ox = other.X + other.Size / 2.0;
            var oy = other.Y + other.Size / 2.0;
            var dx = ox - cx;
            var dy = oy - cy;
            if (dx * dx + dy * dy <= reachSq)
            {
                PopBubble(other.Id);
            }
        }
    }

    private void TickFieldHazards(double dt, List<BubbleState> snapshot)
    {
        if (!_chaosActive || _chaosFrozen) return;
        if (_ripples.Count == 0 && _residues.Count == 0 && _playerRipples.Count == 0
            && Knobs.RabbitTrailSec <= 0) return;

        static double DistSq(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        var dtMs = dt * 1000.0;

        // Size Queen ripples: only treats pop (the ring is a reward wave, never a threat trigger).
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            var (c, age) = _ripples[i];
            age += dtMs;
            double r = RIPPLE_RADIUS_PX * Math.Min(1.0, age / RIPPLE_LIFE_MS);
            foreach (var b in snapshot)
            {
                if (b.IsPopping || b.Spec == null) continue;
                if (b.Spec.IsLive || b.Spec.IsDarter || b.Spec.IsFreeze || b.IsShielded) continue;
                if (DistSq(CenterPx(b), c) <= r * r)
                    PopBubble(b.Id);
            }
            if (age >= RIPPLE_LIFE_MS) _ripples.RemoveAt(i);
            else _ripples[i] = (c, age);
        }

        // The Ripple (right-click): treats and lives pop; darters are flung onward.
        for (int i = _playerRipples.Count - 1; i >= 0; i--)
        {
            var pr = _playerRipples[i];
            pr.AgeMs += dtMs;
            double r = pr.RadiusPx * Math.Min(1.0, pr.AgeMs / pr.LifeMs);
            foreach (var b in snapshot)
            {
                if (b.IsPopping || b.Spec == null || pr.Hit.Contains(b)) continue;
                if (b.Spec.IsFreeze || b.Spec.IsTease || b.Spec.IsBrittle) continue;
                if (DistSq(CenterPx(b), pr.CenterPx) > r * r) continue;
                pr.Hit.Add(b);
                if (b.Spec.IsDarter)
                    FlingDarter(b, pr.CenterPx);
                else
                    PopBubble(b.Id);
            }
            if (pr.AgeMs >= pr.LifeMs) _playerRipples.RemoveAt(i);
        }

        // Aftermath residue: accelerate fuse and jitter velocity while inside the zone.
        var now = DateTime.UtcNow;
        for (int i = _residues.Count - 1; i >= 0; i--)
        {
            var (c, until) = _residues[i];
            if (now >= until) { _residues.RemoveAt(i); continue; }
            foreach (var b in snapshot)
            {
                if (b.IsPopping || b.Spec == null) continue;
                if (b.Spec.IsDarter || b.Spec.IsFreeze || b.IsShielded) continue;
                if (DistSq(CenterPx(b), c) <= RESIDUE_RADIUS_PX * RESIDUE_RADIUS_PX)
                {
                    // Accelerate fuse countdown.
                    if (b.Spec.IsLive && !b.IsDefused && !b.IsDetonated && b.FuseRemainingMs > 0)
                        b.FuseRemainingMs = Math.Max(0.0, b.FuseRemainingMs - dtMs * (RESIDUE_FUSE_MULT - 1.0));

                    // Small random velocity jitter.
                    b.Vx += (_random.NextDouble() - 0.5) * 120.0 * dt;
                    b.Vy += (_random.NextDouble() - 0.5) * 120.0 * dt;
                }
            }
        }

        // Tail-Plug: every rabbit's recorded trail brushes treats and live bubbles open.
        // Live knob gate (WPF BubbleService.cs:1427): with the boon off, even a sweeper's
        // always-on flair trail pops nothing.
        if (Knobs.RabbitTrailSec > 0)
        {
            foreach (var darter in snapshot)
            {
                if (darter.Spec?.IsDarter != true || darter.IsPopping || darter.TrailPoints.Count == 0) continue;
                foreach (var b in snapshot)
                {
                    if (b.IsPopping || b.Spec == null || ReferenceEquals(b, darter)) continue;
                    if (b.Spec.IsDarter || b.Spec.IsFreeze || b.IsShielded) continue;
                    foreach (var (px, _) in darter.TrailPoints)
                    {
                        if (DistSq(CenterPx(b), px) <= TRAIL_POP_RADIUS_PX * TRAIL_POP_RADIUS_PX)
                        {
                            PopBubble(b.Id);
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>A spanked rabbit's body mows plain bubbles — live ones snap, treats pop; other
    /// darters and freeze pickups are immune (WPF BubbleService.cs:1564-1579 SpankSweepFromDarter,
    /// invoked per-frame from the darter's AnimateFrame :3075-3076). The swath is the darter's
    /// body box grown by its one-time spank swell (WPF :2443 <c>SpankReach = ChainReach(Max(1.0,
    /// _spankGrowth))</c>, :2433 grown about the centre). Electrified Rabbits: each victim also
    /// discharges free E-Stim arcs into its neighbours (WPF :1576). Boxes compare in PHYSICAL px
    /// (engine bubbles live in per-screen logical units; WPF compares DIP boxes on one plane).
    /// The tease and the Brittle stay cursor-only like every other auto-sweep (WPF :1387-1389
    /// states the auto-sweep rule; this engine's PopBubble would route a tease as a TOUCH — a
    /// detonation — where WPF's raw Pop() paid it as a plain pop, so sweeping them would punish
    /// where WPF rewards).</summary>
    private void TickSpankSweeps(List<BubbleState> snapshot)
    {
        if (!_chaosActive || _chaosFrozen) return;

        foreach (var darter in snapshot)
        {
            if (darter.Spec?.IsDarter != true || !darter.IsSpanked || darter.IsPopping) continue;

            var ds = darter.Scaling > 0 ? darter.Scaling : 1.0;
            var grow = darter.Size * (Math.Max(1.0, darter.SpankGrowth) - 1.0);
            var reachX = (darter.X - grow / 2.0) * ds;
            var reachY = (darter.Y - grow / 2.0) * ds;
            var reachW = (darter.Size + grow) * ds;

            foreach (var b in snapshot)
            {
                if (b.IsPopping || b.Spec == null || ReferenceEquals(b, darter)) continue;
                if (b.Spec.IsDarter || b.Spec.IsFreeze) continue;           // WPF :1571
                if (b.Spec.IsTease || b.Spec.IsBrittle) continue;           // cursor-only (see doc)

                var bs = b.Scaling > 0 ? b.Scaling : 1.0;
                var bx = b.X * bs;
                var by = b.Y * bs;
                var bsz = b.Size * bs;
                bool intersects = reachX < bx + bsz && reachX + reachW > bx
                                  && reachY < by + bsz && reachY + reachW > by;   // WPF :1572 IntersectsWith
                if (!intersects) continue;

                var victimPx = CenterPx(b);
                PopBubble(b.Id);
                // Live knob read (S4b-4): Electrified Rabbits fires free arcs per mowed victim
                // (WPF :1576). FX/audio (Strike overlay, estim_zap cue) are head-side follow-ups.
                if (Knobs.ElectrifiedRabbits)
                    EStimBurstAt(victimPx, ESTIM_ARCS_PER_POP);
            }
        }
    }

    /// <summary>Free E-Stim discharge: pops up to <paramref name="maxArcs"/> nearest suitable
    /// bubbles within <see cref="ESTIM_BURST_RANGE_PX"/> of <paramref name="fromPx"/> — treats
    /// pop, live ones snap; rabbits and freeze pickups don't conduct, nor do the tease/brittle/
    /// shielded chaperones (WPF BubbleService.cs:407-441 EStimBurstAt + the IsChainable filter
    /// :2386). No charge is consumed and the arcs never chain onward; WPF's per-arc hop timers
    /// (ESTIM_HOP_MS stagger) are collapsed to immediate pops, matching this engine's
    /// pre-existing ChainPop simplification. Strike/zap FX are head-side follow-ups.</summary>
    private void EStimBurstAt(Point fromPx, int maxArcs)
    {
        if (!_chaosActive || maxArcs <= 0) return;

        static double DistSq(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        var pool = new List<BubbleState>();
        var rangeSq = ESTIM_BURST_RANGE_PX * ESTIM_BURST_RANGE_PX;
        foreach (var b in _bubbles.ToArray())
        {
            if (b.IsPopping || b.Spec == null) continue;
            if (b.Spec.IsDarter || b.Spec.IsFreeze || b.Spec.IsTease || b.Spec.IsBrittle) continue;
            if (b.IsShielded) continue;   // chains and arcs route around a shielded live (WPF :2386)
            if (DistSq(CenterPx(b), fromPx) <= rangeSq) pool.Add(b);
        }
        if (pool.Count == 0) return;

        pool.Sort((x, y) => DistSq(CenterPx(x), fromPx).CompareTo(DistSq(CenterPx(y), fromPx)));
        // Emit an arc bolt to each victim BEFORE popping it (CenterPx must be read while the
        // bubble is still alive), then hand the bolt list to the head for compositor render +
        // throttled zap cue (WPF BubbleService.EStimBurstAt builds bolts (fromPx, CenterPx) then
        // Strike()s them, :407-441). The visual is never throttled; the head throttles the cue.
        var arcs = new List<(Point From, Point To)>(Math.Min(pool.Count, maxArcs));
        for (int i = 0; i < pool.Count && i < maxArcs; i++)
        {
            arcs.Add((fromPx, CenterPx(pool[i])));
            PopBubble(pool[i].Id);
        }
        if (arcs.Count > 0) _onEStimArc?.Invoke(arcs);
    }

    private void FlingDarter(BubbleState darter, Point originPx)
    {
        if (darter.Spec == null) return;
        var c = CenterPx(darter);
        var dx = c.X - originPx.X;
        var dy = c.Y - originPx.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1)
        {
            var angle = _random.NextDouble() * Math.PI * 2.0;
            dx = Math.Cos(angle);
            dy = Math.Sin(angle);
            len = 1;
        }

        var speed = (darter.Spec.DarterSpeed > 0 ? darter.Spec.DarterSpeed * WpfFramesPerSecond : DefaultDarterSpeed) * 2.0;
        darter.Vx = (dx / len) * speed;
        darter.Vy = (dy / len) * speed;
    }

    private static Point CenterPx(BubbleState b) => new(
        (b.X + b.Size / 2.0) * b.Scaling,
        (b.Y + b.Size / 2.0) * b.Scaling);

    private void BreakChaperoneShield(ChaosBubbleSpec escortSpec, BubbleState escort, bool escortPopped)
    {
        var liveId = escort.ChaperoneLiveId;
        if (liveId == null) return;

        var live = _bubbles.FirstOrDefault(b => b.Id == liveId.Value);
        if (live == null) return;

        live.IsShielded = false;
        if (live.Spec != null)
            _onChaperoneShieldBroken?.Invoke(live.Spec, escortPopped);
    }

    private void SpawnBubble()
    {
        // WPF parity (BubbleService.cs:855): primary-only when DualMonitorEnabled is off.
        var screens = _screenProvider.GetEffectScreens(_settings.Current?.DualMonitorEnabled != false);
        int screenIndex;
        ScreenInfo screen;
        if (screens.Count == 0)
        {
            screenIndex = 0;
            screen = new ScreenInfo("fallback",
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 0, 1920, 1080),
                1.0);
        }
        else
        {
            screenIndex = _random.Next(screens.Count);
            screen = screens[screenIndex];
        }

        var scaling = screen.Scaling;
        var working = screen.WorkingArea;
        var bounds = new PixelRect(
            working.X / scaling,
            working.Y / scaling,
            working.Width / scaling,
            working.Height / scaling);

        const double minSize = 70.0;
        const double maxSize = 130.0;
        var size = minSize + _random.NextDouble() * (maxSize - minSize);
        var x = bounds.X + _random.NextDouble() * Math.Max(1, bounds.Width - size);
        var y = bounds.Bottom;
        var vx = (_random.NextDouble() - 0.5) * 30.0;
        var vy = -(20.0 + _random.NextDouble() * 45.0);
        var life = 8.0 + _random.NextDouble() * 6.0;

        var state = new BubbleState
        {
            ScreenIndex = screenIndex,
            ScreenBounds = bounds,
            Scaling = scaling,
            X = x,
            Y = y,
            Vx = vx,
            Vy = vy,
            Size = size,
            MaxLifeSec = life,
            LifeRemainingSec = life,
            Clickable = _settings.Current.BubblesClickable
        };

        state.EffectPayload = RollTriggerPayload();

        _bubbles.Add(state);
        _renderer.Create(state);
    }

    /// <summary>
    /// Trigger Bubbles roll: when enabled, a configurable share of ambient bubbles become effect
    /// bubbles that fire a payload on pop. Gated entirely on BubbleTriggersEnabled (default off), so
    /// this is a no-op for the normal pop game. Ported from the WPF BubbleService.RollTriggerSpec.
    /// </summary>
    private EffectPayload? RollTriggerPayload()
    {
        if (_effectPayloadFactory == null) return null;
        var s = _settings.Current;
        if (s?.BubbleTriggersEnabled != true) return null;
        var ids = s.BubbleTriggerVariants;
        if (ids == null || ids.Count == 0) return null;
        if (_random.Next(100) >= Math.Clamp(s.BubbleTriggerChance, 0, 100)) return null;
        var id = ids[_random.Next(ids.Count)];
        try { return _effectPayloadFactory(id); } catch { return null; }
    }
}
