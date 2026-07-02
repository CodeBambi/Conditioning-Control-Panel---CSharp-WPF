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
    private double _chainReachDip = DefaultChainReachDip;
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
    private Action<ChaosBubbleSpec, bool>? _onDarterSpanked;

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
    private double _rabbitTrailSec;

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

    /// <summary>Seconds of rabbit/darter trail currently active (Tail-Plug boon).</summary>
    public double ChaosRabbitTrailSecNow => _rabbitTrailSec;

    /// <summary>Active Size Queen ripples for optional overlay rendering.</summary>
    public IReadOnlyList<(Point CenterPx, double AgeMs, double LifeMs)> ActiveRipples =>
        _ripples.Select(r => (r.CenterPx, r.AgeMs, RIPPLE_LIFE_MS)).ToList();

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
        _rabbitTrailSec = 0.0;
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

                if (!spec.IsDarter && !spec.IsFreeze && _chainReachDip > 0)
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
        Action<ChaosBubbleSpec, string>? onChannelBroken = null)
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
        _chainReachDip = chainReachDip > 0 ? chainReachDip : DefaultChainReachDip;
        _canChannel = canChannel;
        _onChannelStarted = onChannelStarted;
        _onChannelBroken = onChannelBroken;
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
        _channelBubbleId = null;
        _chaosSpawnQueue.Clear();
        _pendingChaperoneEscorts.Clear();
        _pendingChaperoneLives.Clear();
        _pendingBoundPairIds.Clear();
        _ripples.Clear();
        _residues.Clear();
        _playerRipples.Clear();
        _rabbitTrailSec = 0.0;

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

        if (_canChannel?.Invoke(spec) != true)
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
        _onDetonate?.Invoke(spec);
        _renderer.Pop(bubble, () => _bubbles.Remove(bubble));
    }

    /// <summary>Sets the Tail-Plug rabbit/darter trail duration in seconds.</summary>
    public void SetRabbitTrailSec(double seconds) => _rabbitTrailSec = Math.Max(0.0, seconds);

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
            var r = (bubble.Size / 2.0) * bubble.Scaling;
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

    /// <summary>Returns ids of chaos bubbles whose bounds intersect the given DIP rectangle.</summary>
    public IReadOnlyList<Guid> GetChaosBubblesInRect(PixelRect rectDips)
    {
        var result = new List<Guid>();
        if (!_chaosActive) return result;

        foreach (var bubble in _bubbles)
        {
            if (bubble.Spec == null || bubble.IsPopping) continue;
            var r = new PixelRect(bubble.X, bubble.Y, bubble.Size, bubble.Size);
            if (r.X < rectDips.Right && r.Right > rectDips.X && r.Y < rectDips.Bottom && r.Bottom > rectDips.Y)
                result.Add(bubble.Id);
        }

        return result;
    }

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

        var missed = new List<BubbleState>();
        var moved = new List<BubbleState>();

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

        TickFieldHazards(dt);

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
        var screens = _screenProvider.GetAllScreens();
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
            AgeMs = 0.0
        };

        if (spec.IsDarter)
        {
            var angle = _random.NextDouble() * Math.PI * 2.0;
            var speed = spec.DarterSpeed > 0 ? spec.DarterSpeed : DefaultDarterSpeed;
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

        if (bubble.BoundHalfResolved && bubble.BoundResolveTimeRemainingMs > 0 && !bubble.BoundEnraged)
        {
            bubble.BoundResolveTimeRemainingMs -= dt * 1000.0;
            if (bubble.BoundResolveTimeRemainingMs <= 0)
            {
                bubble.BoundEnraged = true;
                bubble.IsPopping = true;
                _onBoundEnraged?.Invoke(spec);
                _onDetonate?.Invoke(spec);
                missed.Add(bubble);
                return;
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
        else
        {
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
        }

        // Tail-Plug: record trail points for darter/rabbit bubbles while the boon holds.
        if (_rabbitTrailSec > 0 && spec.IsDarter && bubble.TelegraphComplete && !bubble.IsPopping)
        {
            var nowPx = CenterPx(bubble);
            var tdx = nowPx.X - bubble.LastTrailEmitPx.X;
            var tdy = nowPx.Y - bubble.LastTrailEmitPx.Y;
            if (tdx * tdx + tdy * tdy >= TRAIL_GAP_PX * TRAIL_GAP_PX || bubble.TrailPoints.Count == 0)
            {
                bubble.LastTrailEmitPx = nowPx;
                bubble.TrailPoints.Add((nowPx, DateTime.UtcNow));
            }
            var cutoff = DateTime.UtcNow.AddSeconds(-_rabbitTrailSec);
            while (bubble.TrailPoints.Count > 0 && bubble.TrailPoints[0].T < cutoff)
                bubble.TrailPoints.RemoveAt(0);
            if (bubble.TrailPoints.Count > TRAIL_MAX_POINTS)
                bubble.TrailPoints.RemoveAt(0);
        }
        else if (!spec.IsDarter || _rabbitTrailSec <= 0)
        {
            bubble.TrailPoints.Clear();
        }

        if (bubble.LifeRemainingSec <= 0)
        {
            if (spec.IsTease)
                _onTeaseDenied?.Invoke(spec);
            else if (spec.IsGolden || spec.IsHeart || spec.IsDroplet)
                _onTreatExpired?.Invoke(spec);

            missed.Add(bubble);
            return;
        }

        bubble.LifeRemainingSec -= dt;

        if (spec.IsLive && !bubble.IsDefused && !bubble.IsDetonated && !bubble.IsShielded)
        {
            if (bubble.IsChanneling)
            {
                // Hold-to-defuse: the fuse pauses while the channel is held.
                var elapsedMs = (DateTime.UtcNow - _channelStartUtc).TotalMilliseconds;
                double t = Math.Clamp(elapsedMs / ChaosTuning.DEFUSE_HOLD_MS, 0.0, 1.0);
                bubble.Scale = 1.0 - (1.0 - ChaosTuning.CHANNEL_MIN_SCALE) * t;

                if (elapsedMs >= ChaosTuning.DEFUSE_HOLD_MS)
                {
                    bubble.IsChanneling = false;
                    _channelBubbleId = null;
                    bubble.IsDefused = true;
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

    private void ChainPop(BubbleState source)
    {
        if (source.Spec == null || _chainReachDip <= 0) return;

        var cx = source.X + source.Size / 2.0;
        var cy = source.Y + source.Size / 2.0;
        var reachSq = _chainReachDip * _chainReachDip;

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

    private void TickFieldHazards(double dt)
    {
        if (!_chaosActive || _chaosFrozen) return;
        if (_ripples.Count == 0 && _residues.Count == 0 && _playerRipples.Count == 0
            && _rabbitTrailSec <= 0) return;

        static double DistSq(Point a, Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        var snapshot = _bubbles.ToArray();
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
        if (_rabbitTrailSec > 0)
        {
            foreach (var darter in snapshot)
            {
                if (darter.Spec?.IsDarter != true || darter.TrailPoints.Count == 0) continue;
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

        var speed = (darter.Spec.DarterSpeed > 0 ? darter.Spec.DarterSpeed : DefaultDarterSpeed) * 2.0;
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
        var screens = _screenProvider.GetAllScreens();
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
