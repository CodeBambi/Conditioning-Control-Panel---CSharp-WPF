using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Services.Overlays;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.Services;

/// <summary>
/// Avalonia host for the ambient and chaos bubble engine.
/// Implements <see cref="IBubbleService"/> and the Chaos overlay bridge <see cref="IAvaloniaBubbleService"/>.
/// </summary>
public sealed class AvaloniaBubbleService : IBubbleService, IAvaloniaBubbleService, IBubbleRenderer
{
    private readonly ISettingsService _settings;
    private readonly IScreenProvider _screens;
    private readonly IAssetLoader _assets;
    private readonly ISfxPlayer _sfx;
    private readonly IPointerState _pointerState;
    private readonly IMouseHook _mouseHook;
    private readonly ILogger<AvaloniaBubbleService>? _logger;
    private readonly ILogger<BubbleEngine>? _bubbleEngineLogger;
    private readonly BubbleEngine _ambientEngine;

    private readonly CompositorEngine? _compositor;
    private readonly BubbleLayer? _bubbleLayer;

    private BubbleEngine? _chaosEngine;
    private SharedHostBubbleRenderer? _sharedHostRenderer;
    private bool _sharedHost;
    private Bitmap? _bubbleBitmap;
    private int _mouseHookRefCount;

    // ---- active-toy state (Avalonia parity stubs) ----
    private bool _vibePopActive;
    private bool _vibePopHoverPops;
    private DateTime _vibePopEndUtc;
    private int _eStimCharges;
    private bool _eStimChainReaction;

    public AvaloniaBubbleService(
        ISettingsService settings,
        IScreenProvider screens,
        IAssetLoader assets,
        ISfxPlayer sfx,
        IPointerState pointerState,
        IMouseHook mouseHook,
        ILogger<AvaloniaBubbleService>? logger = null,
        ILogger<BubbleEngine>? bubbleEngineLogger = null,
        CompositorEngine? compositor = null)
    {
        _settings = settings;
        _screens = screens;
        _assets = assets;
        _sfx = sfx;
        _pointerState = pointerState;
        _mouseHook = mouseHook;
        _logger = logger;
        _bubbleEngineLogger = bubbleEngineLogger;
        _ambientEngine = new BubbleEngine(screens, settings, this, pointerState, bubbleEngineLogger,
            effectPayloadFactory: ConditioningControlPanel.Avalonia.Chaos.AvaloniaEffectPayloadFactory.ForVariant);
        _ambientEngine.OnBubblePopped += OnEngineBubblePopped;
        _ambientEngine.EchoSplitRequested += OnEchoSplitRequested;
        _compositor = compositor;
        _bubbleLayer = compositor != null ? new BubbleLayer() : null;
        if (_bubbleLayer != null)
            compositor?.RegisterLayer(_bubbleLayer);
        // The Skia bubble image is decoded and owned by BubbleLayer itself (write-once, immutable,
        // never disposed) to avoid a cross-thread SKImage use-after-free. The service does NOT
        // create or hand off an SKImage — see AvaloniaUI/Avalonia#13521.
    }

    public bool IsRunning => _ambientEngine.IsRunning;
    public bool IsPaused => _ambientEngine.IsPaused;
    public int ActiveBubbles => _ambientEngine.ActiveBubbles + (_chaosEngine?.ActiveBubbles ?? 0);

    /// <summary>Freeze pickups currently alive on the chaos field — the spawn director's
    /// FREEZE_MAX_ON_SCREEN re-pick reads this (WPF ChaosModeService.cs:1155-1162).</summary>
    public int ActiveFreezeBubbles => _chaosEngine?.ActiveFreezeBubbles ?? 0;

    /// <summary>Live run knobs of the ACTIVE chaos engine (null when no chaos run is up).
    /// The chaos service mutates these mid-run; the engine reads them at use sites.</summary>
    public ChaosRunKnobs? ChaosKnobs => _chaosEngine?.Knobs;

    /// <summary>Engine-logical X of the last chaos pop (WPF BubbleService.cs:120-122
    /// <c>ChaosLastPopXPx</c> equivalent — gg-rabbit sweepers/droplets pin here).</summary>
    public double ChaosLastPopX => _chaosEngine?.ChaosLastPopX ?? 0;

    /// <summary>Engine-logical Y of the last chaos pop (WPF BubbleService.cs:120-122).</summary>
    public double ChaosLastPopY => _chaosEngine?.ChaosLastPopY ?? 0;

    public event Action? OnBubblePopped
    {
        add => _ambientEngine.OnBubblePopped += value;
        remove => _ambientEngine.OnBubblePopped -= value;
    }

    public event Action? OnBubbleMissed
    {
        add => _ambientEngine.OnBubbleMissed += value;
        remove => _ambientEngine.OnBubbleMissed -= value;
    }

    /// <summary>Raised when an echo bubble splits and the caller should spawn child bubbles.</summary>
    public event Action<ChaosBubbleSpec, double, double>? EchoSplitRequested;

    public double ChaosRabbitTrailSecNow => _chaosEngine?.ChaosRabbitTrailSecNow ?? 0.0;

    public void SetRabbitTrailSec(double seconds) => _chaosEngine?.SetRabbitTrailSec(seconds);

    public void Start()
    {
        LoadBubbleImage();
        // Wake the compositor before the first bubble spawns: Start() is idempotent, and the
        // dashboard Bubble Pop toggle can arrive with the engine never started (no session).
        RunOnUi(() => _compositor?.Start());
        _ambientEngine.Start();
        InstallMouseHook();
    }

    public void Stop()
    {
        _ambientEngine.Stop();
        _chaosEngine?.Stop();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        ReleaseMouseHook();
        _bubbleLayer?.Clear();
        _bubbleBitmap?.Dispose();
        _bubbleBitmap = null;
    }

    public void PauseAndClear() => _ambientEngine.PauseAndClear();
    public void Resume() => _ambientEngine.Resume();
    public void RefreshFrequency() => _ambientEngine.RefreshFrequency();
    public void SpawnOnce() => _ambientEngine.SpawnOnce();
    public void PopAllBubbles()
    {
        _ambientEngine.PopAllBubbles();
        _chaosEngine?.PopAllChaosPaid();
    }

    /// <summary>
    /// Pops every clickable bubble intersecting <paramref name="rectPx"/>, given in
    /// PHYSICAL virtual-desktop pixels (raw hook / gaze space; IAvaloniaLayer contract).
    /// </summary>
    public int PopBubblesInRect(ConditioningControlPanel.Core.Platform.PixelRect rectPx)
    {
        int popped = 0;

        // Ambient bubbles — UCE single-surface path: hit-test the Skia BubbleLayer.
        if (_ambientEngine != null && _bubbleLayer != null)
        {
            foreach (var id in _bubbleLayer.HitTestInRect(rectPx))
            {
                _logger?.LogDebug("PopBubblesInRect popping ambient bubble {Id}", id);
                _ambientEngine.PopBubble(id);
                popped++;
            }
        }

        if (_chaosEngine != null)
        {
            var hits = _chaosEngine.GetChaosBubblesInRect(rectPx);
            if (hits.Count > 0)
                _logger?.LogDebug("PopBubblesInRect chaos hits={Hits}", hits.Count);
            foreach (var id in hits)
            {
                var bubble = _chaosEngine.GetChaosBubble(id);
                if (bubble?.Spec is not { } spec) continue;
                if (spec.IsDarter || spec.IsFreeze || spec.IsTease || spec.IsBrittle) continue;
                _logger?.LogDebug("PopBubblesInRect popping chaos bubble {Id} live={Live}", id, spec.IsLive);
                _chaosEngine.PopBubble(id);
                popped++;
            }
        }

        return popped;
    }

    public bool AnyDarterIntersects(ConditioningControlPanel.Core.Platform.PixelRect rectPx)
    {
        if (_chaosEngine == null) return false;
        var hits = _chaosEngine.GetChaosBubblesInRect(rectPx);
        foreach (var id in hits)
        {
            var bubble = _chaosEngine.GetChaosBubble(id);
            if (bubble?.Spec is { IsDarter: true })
                return true;
        }
        return false;
    }

    // ---- Stage 2a/2b chaos mode ----

    void IBubbleService.BeginChaosMode(
        Action<ChaosBubbleSpec> onBenignPop,
        Action<ChaosBubbleSpec, double, bool> onDefuse,
        Action<ChaosBubbleSpec> onDetonate,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null)
    {
        BeginChaosMode(onBenignPop, onDefuse, onDetonate,
            canChannel: canChannel,
            onChannelStarted: onChannelStarted,
            onChannelBroken: onChannelBroken);
    }

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
        double chainReachDip = 120.0,
        Func<ChaosBubbleSpec, bool>? canChannel = null,
        Action<ChaosBubbleSpec>? onChannelStarted = null,
        Action<ChaosBubbleSpec, string>? onChannelBroken = null)
    {
        LoadBubbleImage();

        // Ensure a previous chaos engine is torn down before starting a new run.
        if (_chaosEngine != null)
        {
            _chaosEngine.EndChaosMode();
            _chaosEngine = null;
        }

        _sharedHost = _settings.Current.ChaosBubbleSharedHost;
        IBubbleRenderer chaosRenderer;
        if (_sharedHost)
        {
            ChaosBubbleHostOverlay.EnsureCreated();
            _sharedHostRenderer = new SharedHostBubbleRenderer(_bubbleBitmap);
            chaosRenderer = _sharedHostRenderer;
        }
        else
        {
            chaosRenderer = this;
        }

        _chaosEngine = new BubbleEngine(_screens, _settings, chaosRenderer, _pointerState, _bubbleEngineLogger);
        _chaosEngine.OnBubblePopped += OnEngineBubblePopped;
        _chaosEngine.EchoSplitRequested += OnEchoSplitRequested;

        Action<ChaosBubbleSpec> wrappedBenignPop = spec =>
        {
            onBenignPop(spec);
            if (spec.IsPrism)
                ShowPrismGhost(spec);
        };

        _chaosEngine.BeginChaosMode(
            wrappedBenignPop,
            onDefuse,
            onDetonate,
            onDarterCaught,
            onFreezeCaught,
            onChaperoneShieldBroken,
            onBoundEnraged,
            onTeaseTouched,
            onTeaseDenied,
            onBrittleShattered,
            onTreatExpired,
            onDarterSpanked,
            chainReachDip,
            canChannel,
            onChannelStarted,
            onChannelBroken);

        InstallMouseHook();
    }

    public void EndChaosMode()
    {
        _chaosEngine?.EndChaosMode();
        _chaosEngine = null;
        _sharedHostRenderer = null;
        ReleaseMouseHook();
    }

    public void SpawnChaosBubble(ChaosBubbleSpec spec) => _chaosEngine?.SpawnChaosBubble(spec);

    public void SpawnChaosChaperone(ChaosBubbleSpec live, ChaosBubbleSpec escort) => _chaosEngine?.SpawnChaosChaperone(live, escort);

    public void SpawnChaosBoundPair(ChaosBubbleSpec a, ChaosBubbleSpec b) => _chaosEngine?.SpawnChaosBoundPair(a, b);

    public void PopNearestBenign() => _chaosEngine?.PopNearestBenign();

    public void DefuseAllLive() => _chaosEngine?.DefuseAllLive();

    public void PopAllChaosPaid() => _chaosEngine?.PopAllChaosPaid();

    public void SetChaosFrozen(bool frozen) => _chaosEngine?.SetChaosFrozen(frozen);

    public void SetChaosTimeScale(double scale) => _chaosEngine?.SetChaosTimeScale(scale);

    public void SetChaosInputLocked(bool locked) => _chaosEngine?.SetChaosInputLocked(locked);

    public void SetVibePop(bool active, bool hoverPops = false)
    {
        _vibePopActive = active;
        _vibePopHoverPops = hoverPops;
        // Duration is driven by AvaloniaChaosService; keep sweeping until it calls SetVibePop(false).
        _vibePopEndUtc = active ? DateTime.MaxValue : DateTime.UtcNow;
    }

    public void VibrateAllForFreeze(int durationMs)
    {
        // Stage 2c: window vibration stub; harmless no-op until visual FX layer is wired.
    }

    public void ArmEStim(int charges, bool chainReaction = false)
    {
        _eStimCharges = Math.Max(0, charges);
        _eStimChainReaction = chainReaction;
    }

    public int EStimChargesLeft => _eStimCharges;

    public void TriggerPlayerRipple(Point centerPx, double radiusPx, double lifeMs) =>
        _chaosEngine?.TriggerPlayerRipple(centerPx, radiusPx, lifeMs);

    public void PlayChime(float volumeScale = 0.3f)
    {
        try { _sfx.Play("chime", volumeScale * 0.6f); }
        catch (Exception ex) { _logger?.LogWarning(ex, "PlayChime failed"); }
    }

    private void OnMouseHookLeftDown(object? sender, HookPoint e)
    {
        // The low-level hook runs off the UI thread. Marshal the decision to the UI thread so
        // we can safely inspect engine/window state and call PopBubble/BeginChaosChannel.
        Dispatcher.UIThread.Post(() => HandleMouseLeftDown(e));
    }

    private void HandleMouseLeftDown(HookPoint e)
    {
        try
        {
            // VibePopping: while armed, every left-down sweeps the immediate area.
            if (_vibePopActive && _chaosEngine != null)
            {
                if (DateTime.UtcNow >= _vibePopEndUtc) _vibePopActive = false;
                else
                {
                    const int sweep = 120;
                    PopBubblesInRect(new PixelRect(e.X - sweep, e.Y - sweep, sweep * 2, sweep * 2));
                }
            }

            // E-Stim: discharge one charge on each click while armed.
            if (_eStimCharges > 0 && _chaosEngine != null)
            {
                _eStimCharges--;
                int radius = _eStimChainReaction ? 800 : 500;
                PopBubblesInRect(new PixelRect(e.X - radius, e.Y - radius, radius * 2, radius * 2));
            }

            // Chaos bubbles (shared-host or per-window) take priority.
            if (_chaosEngine != null)
            {
                bool swallowed = _chaosEngine.OnSharedHostLeftDown(new Point(e.X, e.Y));
                if (swallowed) return;
            }

            // Ambient session bubbles: pop the window under the cursor.
            TryPopAmbientBubbleAt(e.X, e.Y);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Mouse left-down handler failed");
        }
    }

    private void OnMouseHookRightDown(object? sender, HookPoint e)
    {
        // The Ripple verb is driven by AvaloniaChaosService so it can honor recharge/cooldown.
        // The bubble engine still exposes TriggerPlayerRipple for consumers that call it directly.
    }

    private void OnMouseHookLeftUp(object? sender, HookPoint e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try { _chaosEngine?.EndActiveChaosChannel(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Mouse left-up handler failed"); }
        });
    }

    private void TryPopAmbientBubbleAt(double x, double y)
    {
        if (_ambientEngine == null || !_ambientEngine.IsRunning || _ambientEngine.IsPaused) return;

        // UCE single-surface path: hit-test against the Skia BubbleLayer directly.
        var hit = _bubbleLayer?.HitTest(x, y) ?? Guid.Empty;
        if (hit != Guid.Empty)
        {
            try { _ambientEngine.PopBubble(hit); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to pop ambient bubble {StateId}", hit); }
        }
    }

    private void InstallMouseHook()
    {
        if (_mouseHookRefCount++ == 0)
        {
            _mouseHook.LeftButtonDown += OnMouseHookLeftDown;
            _mouseHook.RightButtonDown += OnMouseHookRightDown;
            _mouseHook.LeftButtonUp += OnMouseHookLeftUp;
            _mouseHook.Install();
        }
    }

    private void ReleaseMouseHook()
    {
        // Strict pairing with InstallMouseHook: the shared IMouseHook is reference-counted
        // across consumers, so an unpaired release (e.g. Stop() without a prior Start())
        // must NOT reach Uninstall — it would release a share owned by the flash or chaos
        // ripple consumer.
        if (_mouseHookRefCount <= 0)
        {
            _mouseHookRefCount = 0;
            return;
        }
        if (--_mouseHookRefCount > 0) return;

        try
        {
            _mouseHook.LeftButtonDown -= OnMouseHookLeftDown;
            _mouseHook.RightButtonDown -= OnMouseHookRightDown;
            _mouseHook.LeftButtonUp -= OnMouseHookLeftUp;
        }
        catch { }
        _mouseHook.Uninstall();
    }

    private void OnEchoSplitRequested(ChaosBubbleSpec spec, double x, double y)
    {
        EchoSplitRequested?.Invoke(spec, x, y);
        SpawnEchoChildren(spec, x, y);
    }

    private void SpawnEchoChildren(ChaosBubbleSpec parent, double centerPxX, double centerPxY)
    {
        if (_chaosEngine == null) return;

        // WPF SpawnEchoChildren (ChaosModeService.cs:1430-1443): 2 children via the faithful
        // catalog builder, pinned at the pop point ±70/±50. The run's EffectIntensity rides on
        // the parent spec (stamped by ChaosSpawnCatalog.BuildEcho) — WPF reads Config.EffectIntensity
        // directly in the service; this engine's split event lands here instead.
        for (int i = 0; i < 2; i++)
        {
            try
            {
                var child = ChaosSpawnCatalog.BuildEchoChild(
                    parent.SizePx,
                    centerPxX + Random.Shared.Next(-70, 71),
                    centerPxY + Random.Shared.Next(-50, 51),
                    parent.EffectIntensity,
                    Random.Shared);
                _chaosEngine.SpawnChaosBubble(child);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to spawn echo child");
            }
        }
    }

    // ---- IAvaloniaBubbleService explicit implementation ----

    int IAvaloniaBubbleService.PopBubblesInRect(global::Avalonia.Rect rectDips)
    {
        return PopBubblesInRect(new PixelRect(rectDips.X, rectDips.Y, rectDips.Width, rectDips.Height));
    }

    bool IAvaloniaBubbleService.AnyDarterIntersects(global::Avalonia.Rect rectDips) =>
        AnyDarterIntersects(new PixelRect(rectDips.X, rectDips.Y, rectDips.Width, rectDips.Height));

    // ---- IBubbleRenderer implementation ----

    /// <summary>
    /// Coordinate seam (IAvaloniaLayer contract): the Core BubbleEngine works in per-screen
    /// logical units (physical / that bubble's screen scaling — see BubbleEngine spawn
    /// bounds), while BubbleLayer stores PHYSICAL virtual-desktop pixels so raw hook
    /// coordinates hit-test directly. Multiplying X/Y/Size by the bubble's own screen
    /// scaling is the exact inverse of the engine's spawn mapping, per-monitor correct on
    /// mixed-DPI setups (same math as the engine's own OnSharedHostLeftDown physical discs).
    /// </summary>
    void IBubbleRenderer.Create(BubbleState state)
    {
        RunOnUi(() =>
        {
            // Single-surface UCE render path: every bubble lives in the Skia BubbleLayer.
            // No per-window bubble windows (eliminates the dual-render race + z-fighting).
            // Wake the compositor so the bubble actually renders (idempotent, cheap).
            _compositor?.Start();

            var isChaos = state.Spec != null;
            var tint = isChaos ? ((byte)state.Spec!.TintR, (byte)state.Spec.TintG, (byte)state.Spec.TintB) : ((byte, byte, byte)?)null;
            var fuseFraction = state.Spec is { IsLive: true, FuseMs: > 0 }
                ? Math.Clamp(state.FuseRemainingMs / state.Spec.FuseMs, 0.0, 1.0)
                : 1.0;

            var s = state.Scaling > 0 ? state.Scaling : 1.0;
            _bubbleLayer?.AddBubble(
                state.Id, state.X * s, state.Y * s, state.Size * s, state.Opacity, state.Scale,
                state.Spec?.Label, tint, isChaos, fuseFraction, state.Clickable);
        });
    }

    void IBubbleRenderer.Move(BubbleState state)
    {
        RunOnUi(() =>
        {
            var s = state.Scaling > 0 ? state.Scaling : 1.0;
            _bubbleLayer?.UpdateBubble(state.Id, state.X * s, state.Y * s, state.Opacity, state.Scale);
        });
    }

    void IBubbleRenderer.SetLabel(Guid id, string label)
    {
        RunOnUi(() => { _bubbleLayer?.SetLabel(id, label); });
    }

    void IBubbleRenderer.SetFuse(Guid id, double fraction)
    {
        RunOnUi(() => { _bubbleLayer?.SetFuse(id, fraction); });
    }

    void IBubbleRenderer.Pop(BubbleState state, Action onComplete)
    {
        RunOnUi(() =>
        {
            _bubbleLayer?.RemoveBubble(state.Id);
            onComplete();
        });
    }

    void IBubbleRenderer.Destroy(Guid id)
    {
        RunOnUi(() => { _bubbleLayer?.RemoveBubble(id); });
    }

    private void LoadBubbleImage()
    {
        // Only the Avalonia Bitmap (for the legacy shared-host path) is loaded here. The Skia
        // SKImage used by the compositor BubbleLayer is decoded and owned by the layer itself
        // (BubbleLayer.EnsureBubbleImage) to avoid a cross-thread use-after-free — sharing an
        // SKImage between this (UI) thread and the render thread corrupts the native heap
        // (AvaloniaUI/Avalonia#13521).
        try
        {
            if (_bubbleBitmap != null) return;
            _bubbleBitmap = AvaloniaBitmapHelper.LoadResource("bubble.png");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load bubble image");
        }
    }

    private void ShowPrismGhost(ChaosBubbleSpec spec)
    {
        // Transient visual flourish (a fading clone of a popped Prism chaos bubble).
        // On the single-surface UCE path there is no per-window ghost; the Skia BubbleLayer
        // does not animate transient ghosts, so this is a no-op. Gameplay is unaffected.
    }

    private void OnEngineBubblePopped()
    {
        var volume = _settings.Current.BubblesVolume / 100f * 0.6f;
        _sfx.Play("pop", volume);
    }

    private void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
