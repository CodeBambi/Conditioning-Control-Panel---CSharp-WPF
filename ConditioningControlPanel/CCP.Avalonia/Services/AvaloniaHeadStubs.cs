using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Compositor;
using ConditioningControlPanel.Avalonia.Compositor.Layers;
using ConditioningControlPanel.Avalonia.Services.Video;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.Flash;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Subliminal;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;
// S4: every tuning read in this file goes through the Core ChaosTuning now — the parallel
// Avalonia.Chaos.ChaosTuning alias is retired here (port-plan rule: one tuning source).
using CoreChaosTuning = ConditioningControlPanel.Core.Services.Chaos.ChaosTuning;
using ChaosNarrativeContext = ConditioningControlPanel.Core.Services.Chaos.ChaosNarrativeContext;

namespace ConditioningControlPanel.Avalonia.Services;

/// Avalonia Chaos engine service. Owns the run lifecycle: countdown, spawn loop, scoring,
/// combo/heat, boon draft between waves, and results. It wires into the ported overlay windows
/// and the cross-platform <see cref="IBubbleService"/> chaos hooks.
/// </summary>
public sealed class AvaloniaChaosService : IChaosService
{
    private readonly IBubbleService _bubbles;
    private readonly ISettingsService _settings;
    private readonly IProgressionService _progression;
    private readonly ILogger<AvaloniaChaosService>? _logger;
    private readonly IInputHook? _inputHook;
    private readonly IMouseHook? _mouseHook;
    private readonly IPointerState? _pointerState;
    private readonly IFlashService? _flash;
    private readonly ISubliminalService? _subliminal;
    private readonly IVideoService? _video;
    private readonly IOverlayService? _overlayService;
    private readonly IBouncingTextService? _bouncingText;
    private readonly IBrowserHost? _browserHost;
    private readonly IModService? _modService;
    private readonly ConditioningControlPanel.Core.Services.Chaos.IChaosTunnelService? _tunnel;
    private readonly global::ConditioningControlPanel.ISkillTreeService? _skillTree;
    private readonly global::ConditioningControlPanel.Core.Services.Progression.IAchievementService? _achievements;
    private readonly ChaosCrashSentinel? _crashSentinel;
    // WS2/WP3 template migration: the Rabbit Caller cursor-glow telegraph is a compositor
    // layer, not a window. The service owns the state and drives it (UCE rule 7).
    private readonly ChaosCursorGlowLayer _cursorGlowLayer;
    // WS2/WP3 Phase F #2: chaos pop text is a compositor layer, not a pooled window set.
    private readonly ChaosPopTextLayer _popTextLayer;
    // WS2/WP3 Phase F #3: the effect-banner strip is a compositor layer, not a keep-alive window.
    private readonly ChaosEffectBannerLayer _effectBannerLayer;
    // WS2/WP3 Phase F #4: the announcer subtitle line is a compositor layer; the priority
    // QUEUE lives here in the service, byte-equivalent to WPF's static queue (services own
    // state, layers render the current line — UCE rule 7).
    private readonly ChaosAnnouncerLayer _announcerLayer;
    // WS2/WP3 Phase F #5: the chaos "braindrain" full-screen image wash is a compositor layer.
    private readonly ChaosFlashWashLayer _flashWashLayer;
    // WS2/WP3 Phase F #6: the bouncing DVD logos are compositor render items; their side
    // effects (bubble pops, darter queries, sfx) stay HERE via the layer's delegates.
    private readonly ChaosDvdLayer _dvdLayer;
    // WS2/WP3 Phase F #7: the falling gif cascade is a compositor layer (decode-once).
    private readonly ChaosGifCascadeLayer _gifCascadeLayer;
    // WS2/WP3 Phase F #8: the ambient field FX (rings/residue/trails/tethers) are a
    // compositor layer. NO production caller exists yet (the WPF call sites live in
    // unported BubbleService paths); the seams below are live for that port + the harness.
    private readonly ChaosFieldFxLayer _fieldFxLayer;
    /// <summary>Orphans a superseded in-flight wash decode (WPF ChaosFlashOverlay._displayGen).</summary>
    private int _washGen;
    private readonly object _announceSync = new();
    private readonly List<(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs, int priority)> _announceQueue = new();
    private bool _announceShowing;
    private readonly CompositorEngine? _compositor;
    private readonly IScreenProvider? _screenProvider;

    /// <summary>WPF ChaosAnnouncerOverlay.TEACH_HOLD_MS — onboarding/help lines linger so
    /// they can actually be read.</summary>
    public const int TeachHoldMs = 3000;
    /// <summary>WPF ChaosAnnouncerOverlay.HOLD_MS — default dwell for event beats.</summary>
    private const int AnnounceDefaultHoldMs = 650;
    private readonly Random _rng = new();

    private bool _active;
    private bool _spawning;
    private bool _paused;
    private bool _manualPaused;
    private bool _ending;
    private ChaosRunState? _state;
    private ChaosOverlayWindow? _overlay;
    private ChaosHudWindow? _hud;

    private DispatcherTimer? _runTimer;
    private DispatcherTimer? _spawnTimer;
    private int _chromeRaiseTick;
    private int _waveIndex;
    private int _waveCount;
    private bool _scriptedDraftPending;

    // ---- hold-to-defuse focus economy state ----
    private double _focusLowAccumSec;
    private bool _focusLowBarkFired;
    // Snap Chain: triggers bounce off inside this window (WPF ChaosModeService.cs:1923).
    private DateTime _invulnUntilUtc = DateTime.MinValue;
    // Pendulum swing activation lands in S6/S7 (plan: chaos-run-engine-port-plan.md); false until then.
    private bool _pendulumSlowActive = false;

    // ---- spawn-director run transients (S4; WPF ChaosModeService.cs SpawnTick/RunTick state) ----
    // Heavy Drop: every Nth ordinary spawn swaps for the giant treat (WPF :1138-1141; reset :401).
    private int _spawnSerial;
    // Side-drift grace counter (WPF :1147-1148 / SIDE_DRIFT_GRACE_SPAWNS; reset :401).
    private int _ordinarySpawns;
    // Heavy-payload gate: expected end of a running heavy effect. The payload paths arm it in
    // S6 (chaos-run-engine-port-plan.md); until then it stays MinValue and only the live
    // video/cascade checks gate (WPF ChaosModeService.cs:2156 _heavyUntilUtc; reset :402).
    private DateTime _heavyUntilUtc = DateTime.MinValue;
    // Darter slow-mo runs on the real clock, like the freeze (WPF ChaosModeService.cs:2325).
    private double _slowMoRemainingSec;
    private bool _slowMoCueOn;   // an in-cue played; the matching out-cue is owed on end (WPF :2965)
    // Pop-up Notification heart: once-per-loop arm + random fire beat (WPF ChaosModeService.cs:2899-2913).
    private int _heartRolledWave;
    private bool _heartArmedThisWave;
    private double _heartFireAtProgress;
    // The Tease denial streak (WPF ChaosModeService.cs:1331-1332).
    private int _teaseDeniedThisRun;
    private bool _teaseDeniedStreakBarked;

    // Freeze power-up windows (WPF ChaosModeService.cs:2957-2959 service consts).
    private const double FREEZE_DURATION_SEC = 3.5;
    private const int FREEZE_VIBRATE_MS = 200;

    // ---- active toys ----
    private readonly List<ChaosToyButtonWindow> _toyButtons = new();
    private double _vibeRemainingSec;
    private double _freezeRemainingSec;
    private double _snapFlashRemainingSec;
    private int _rabbitCallPending;
    private bool _rabbitCallMaxed;
    private DispatcherTimer? _rabbitAimTimer;
    private bool _rabbitAimPrevDown;
    private bool _dvdBannerOn;
    private double _rippleCooldownSec;

    public AvaloniaChaosService(
        IBubbleService bubbles,
        ISettingsService settings,
        IProgressionService progression,
        ILogger<AvaloniaChaosService>? logger = null,
        IInputHook? inputHook = null,
        IMouseHook? mouseHook = null,
        IPointerState? pointerState = null,
        IFlashService? flash = null,
        ISubliminalService? subliminal = null,
        IVideoService? video = null,
        IOverlayService? overlayService = null,
        IBouncingTextService? bouncingText = null,
        IBrowserHost? browserHost = null,
        IModService? modService = null,
        ConditioningControlPanel.Core.Services.Chaos.IChaosTunnelService? tunnel = null,
        global::ConditioningControlPanel.ISkillTreeService? skillTree = null,
        global::ConditioningControlPanel.Core.Services.Progression.IAchievementService? achievements = null,
        ChaosCrashSentinel? crashSentinel = null,
        CompositorEngine? compositor = null,
        IScreenProvider? screenProvider = null)
    {
        _bubbles = bubbles;
        _settings = settings;
        _progression = progression;
        _logger = logger;
        _inputHook = inputHook;
        _mouseHook = mouseHook;
        _pointerState = pointerState;
        _flash = flash;
        _subliminal = subliminal;
        _video = video;
        _overlayService = overlayService;
        _bouncingText = bouncingText;
        _browserHost = browserHost;
        _modService = modService;
        _tunnel = tunnel;
        _skillTree = skillTree;
        _achievements = achievements;
        _crashSentinel = crashSentinel;
        // Register once for the service lifetime (AvaloniaOverlayService pattern): IsActive is
        // content-driven, so the idle layer renders nothing and costs nothing. Without a
        // DI-provided engine the layer is never registered and the telegraph renders nothing
        // (UCE rule 7 nullable-engine caveat).
        _compositor = compositor;
        _cursorGlowLayer = new ChaosCursorGlowLayer();
        compositor?.RegisterLayer(_cursorGlowLayer);
        _popTextLayer = new ChaosPopTextLayer();
        compositor?.RegisterLayer(_popTextLayer);
        _effectBannerLayer = new ChaosEffectBannerLayer();
        compositor?.RegisterLayer(_effectBannerLayer);
        _announcerLayer = new ChaosAnnouncerLayer();
        // Queue-advance hook: fires on the engine tick (UI thread) when a line's fade-out
        // completes — the WPF fade.Completed → ShowNext() chain.
        _announcerLayer.LineCompleted = () => { lock (_announceSync) { ShowNextAnnouncementLocked(); } };
        compositor?.RegisterLayer(_announcerLayer);
        _flashWashLayer = new ChaosFlashWashLayer();
        compositor?.RegisterLayer(_flashWashLayer);
        _dvdLayer = new ChaosDvdLayer
        {
            // Policy hooks (services own side effects; the layer owns flight physics).
            // The bubble seams take PHYSICAL px rects (IAvaloniaLayer coordinate contract).
            PopBubblesInRect = rect => { try { _bubbles.PopBubblesInRect(rect); } catch { } },
            DarterIntersects = rect => { try { return _bubbles.AnyDarterIntersects(rect); } catch { return false; } },
            PlaySfx = (name, vol) => { try { AvaloniaChaosSfx.Play(name, vol); } catch { } },
        };
        compositor?.RegisterLayer(_dvdLayer);
        _gifCascadeLayer = new ChaosGifCascadeLayer();
        compositor?.RegisterLayer(_gifCascadeLayer);
        _fieldFxLayer = new ChaosFieldFxLayer();
        compositor?.RegisterLayer(_fieldFxLayer);
        _screenProvider = screenProvider;
        AvaloniaChaosCatalogs.EnsureInitialized();
    }

    public bool IsRunning => _active;
    public bool IsManuallyPaused => _manualPaused;
    public double LastRunScore { get; private set; }

    public void ShowLoadoutSidebar() { }
    public void CloseLoadoutSidebar() { }
    public void NotifyLoadoutChanged() { }

    public void StartRun(object cfg)
    {
        if (_active) return;

        try
        {
            var config = cfg as ChaosRunConfig ?? ChaosRunConfig.FromSettings();
            if (ChaosMeta.State.RunsCompleted == 0)
                config = ChaosHappyPath.BuildFirstRunConfig();
            AvaloniaChaosMode.ActiveMode = config.PlayMode;
            // WPF parity (ChaosModeService.cs:320): snapshot the user's Pin-on-top setting
            // BEFORE any chaos window is constructed, "regardless of mode" — a default Free
            // Desktop run stays pinned topmost; only unchecking the hub's Pin-on-top box
            // yields the sink-behind behavior. This also makes the hub checkbox functional
            // (it writes ChaosPinOnTop, which previously had no consumer).
            AvaloniaChaosMode.PinTopmost = _settings.Current?.ChaosPinOnTop ?? true;

            _bubbles.PauseAndClear();
            _state = new ChaosRunState()
            {
                Config = config,
                RunDurationSec = config.RunDurationSec,
                ElapsedSec = 0,
                WaveIndex = 1,
                ActIndex = 1,
                Shields = Math.Max(0, config.StartingShields),
                Focus = Math.Clamp(config.StartingFocus, 0, 100),
                FocusMax = 100,
                Combo = 0,
                Heat = 0,
                BoonMult = 1.0,
                // ComboMult/HeatMult/DifficultyMult are computed on the state now (WPF ChaosModels.cs:524-527).
                // Seed the upgrade-shaped knobs from the config like the WPF state ctor (WPF ChaosModels.cs:417-421).
                FuseTimeMult = config.FuseTimeMult,
                MagnetEnabled = config.MagnetEnabled,
                Score = 0,
                Defused = 0,
                Detonated = 0,
                EffectsFired = 0,
            };
            _waveCount = Math.Max(1, config.WaveCount);
            _waveIndex = 1;
            _state.WaveCount = _waveCount;
            _active = true;
            _spawning = false;
            _paused = false;
            _manualPaused = false;
            _ending = false;
            LastRunScore = 0;
            _chromeRaiseTick = 0;

            RunOnUi(() =>
            {
                try
                {
                    // Close any orphaned overlay windows (e.g., hub greeting conversations)
                    // so the run overlay is the only interactive surface and gameplay clicks
                    // are not blocked by a stale conversation.
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    foreach (var w in lifetime?.Windows.OfType<ChaosOverlayWindow>().ToList() ?? new List<ChaosOverlayWindow>())
                    {
                        try { w.Close(); } catch { }
                    }

                    _hud = new ChaosHudWindow(_state, this);
                    _hud.Show();

                    _overlay = new ChaosOverlayWindow();
                    _overlay.OnRunAgain = () =>
                    {
                        var previous = _state?.Config;
                        // Close the old overlay/hud and clear run state before restarting,
                        // otherwise the previous results surface stays on screen.
                        CleanupAfterRun();
                        if (previous != null) StartRun(previous);
                    };
                    _overlay.OnDismissed = OnOverlayClosed;
                    _overlay.Show();
                    _tunnel?.Preload(); // warm the WebView2/three.js init under the countdown
                    _overlay.ShowCountdown(BeginRun);

                    // Effect banner and field FX are compositor layers now: registration is
                    // app-lifetime, so the WPF-hang-motivated EnsureCreated pre-warms have no
                    // equivalent (the WPF ChaosFieldFxOverlay.EnsureCreated call dies here too).
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "AvaloniaChaosService StartRun UI init failed");
                    CleanupAfterRun();
                }
            });

            _logger?.LogInformation("AvaloniaChaosService run started ({Difficulty}, {Duration}s, {Waves} waves)",
                config.Difficulty, config.RunDurationSec, _waveCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "AvaloniaChaosService StartRun failed");
            CleanupAfterRun();
        }
    }

    public void StartRunFromSidebar() => StartRun(ChaosRunConfig.FromSettings());

    public void ToggleManualPause()
    {
        if (!_spawning || _ending) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused)
        {
            _paused = true;
            _bubbles.SetChaosFrozen(true);
            _bubbles.SetChaosInputLocked(true);
            _state?.PushEvent("⏸ held. the hole waits.");
        }
        else
        {
            _paused = false;
            _bubbles.SetChaosInputLocked(false);
            // A freeze power-up that was live when pause hit didn't tick down — let it finish.
            if (_freezeRemainingSec <= 0) _bubbles.SetChaosFrozen(false);
            _state?.PushEvent("▶ sinking again");
        }
        RunOnUi(() => _hud?.SetPausedUi(_manualPaused));
    }

    public void RequestStop()
    {
        if (!_active || _ending) return;
        EndRun();
    }

    public void CloseWarrenPhase() => RequestStop();
    public void OpenWarrenAt(string tag) { }
    public void UnequipFromSidebar(string id) { }

    private void BeginRun()
    {
        if (!_active || _state == null) return;

        ChaosLessonHooks.OnRunStarted();

        // Arm the dirty-shutdown sentinel (WPF ChaosModeService.cs:860): a native vanish mid-run
        // self-reports at the next launch. Cleared on every teardown path (EndRun/CleanupAfterRun).
        try { _crashSentinel?.Mark(BuildSentinelContext()); } catch { }

        var runStartCtx = BuildNarrativeContext(depth: 1);
        ChaosNarrativeHooks.OnRunStarted(runStartCtx);
        var runStartConvo = ChaosNarrativeDirector.Pick(runStartCtx, "run_start");
        if (runStartConvo != null)
            RunOnUi(() => _overlay?.ShowConversation(runStartConvo, null, () => { }));

        // Full behavioral callback set — the WPF BeginChaosMode wiring (WPF ChaosModeService.cs:361-381)
        // mapped onto the Core BubbleEngine surface. The WPF live-lambda knobs (chainReach,
        // hitboxScale, bubbleOpacity, cursorPull, rabbitHoming, spankerOn/spankGrow, liveMagnet,
        // rabbitTrailSec, electrifiedRabbits) live on the engine's ChaosRunKnobs, pushed by
        // SyncKnobsFromState below (S4b-4). wandShimmer is retired (WPF :370 hard-codes false);
        // onEStimArc has no engine seam until the charged E-Stim chain is ported — follow-up row.
        // onChaperoneShieldBroken: WPF has NO service-side handler — the escort pop is a normal
        // treat pop and the shield release is engine-internal (WPF BubbleService.cs:1148-1184).
        // onDarterSpanked: fired by the engine on a darter's first SMACK (S4b-3) — only when the
        // Spanker is on (ChaosKnobs.SpankerOn, WPF BubbleService.cs:3706-3708; spank REPLACES the
        // catch) or for born-spanked sweepers (never fires — latch pre-set).
        _bubbles.BeginChaosMode(
            OnBenignPopped,
            OnDefused,
            OnDetonated,
            onDarterCaught: OnDarterCaught,
            onFreezeCaught: OnFreezeCaught,
            onBoundEnraged: OnBoundEnraged,
            onTeaseTouched: OnTeaseTouched,
            onTeaseDenied: OnTeaseDenied,
            onBrittleShattered: OnBrittleShattered,
            onTreatExpired: OnTreatExpired,
            onDarterSpanked: OnDarterSpanked,
            canChannel: CanChannelDefuse,
            onChannelStarted: OnChannelStarted,
            onChannelBroken: OnChannelBroken);

        // Seed the live knobs immediately after BeginChaosMode (which Reset() them) so the
        // config-shaped values — silk_touch HitboxScale/MagnetEnabled — hold from the first
        // spawn, exactly like WPF's lambdas being live from wiring (WPF ChaosModeService.cs:363-386).
        SyncKnobsFromState();

        // Fresh run-transient spawn-director state (WPF ChaosModeService.cs:395-403, 421-422).
        EndSlowMo(); EndFreeze();   // clean power-up state for the new run (no leak across runs) (WPF :399)
        _spawnSerial = 0; _ordinarySpawns = 0; _pendulumSlowActive = false;   // WPF :401
        _heavyUntilUtc = DateTime.MinValue;                                    // WPF :402
        _heartRolledWave = 0; _heartArmedThisWave = false;                     // WPF :400
        _teaseDeniedThisRun = 0; _teaseDeniedStreakBarked = false;             // WPF :421-422

        _spawning = true;
        _state.PushEvent("🐇 the descent begins");
        AvaloniaChaosSfx.Play("fall_in", 0.28f);   // the falling whoosh as the descent opens (WPF ChaosModeService.cs:342)

        try { ChaosBackdropService.Show(_state.ActIndex); } catch (Exception ex) { _logger?.LogDebug("ChaosBackdropService.Show failed: {E}", ex.Message); }

        try { _tunnel?.Show(); _tunnel?.SendZoneHint(_state.ActIndex, Math.Min(1.0, (_state.ActIndex - 1) / 5.0)); _tunnel?.SetStreak(_state.Combo, _state.ComboMult); } catch (Exception ex) { _logger?.LogDebug("ChaosTunnel wire failed: {E}", ex.Message); }

        ChaosHappyPath.OnRunStarted(_state, this);
        if (ChaosMeta.State.RunsCompleted == 0)
            ChaosHappyPath.OnFirstDescentStarted(_state);
        try { AvaloniaChaosApp.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty); } catch { }

        // The run-pick ribbon along the top: shows ONLY mantras/sins drafted during THIS descent,
        // in pick order, beside the clock (WPF ChaosModeService.cs:414-417). RunPickTiles is a plain
        // List (no CollectionChanged), so SetPicks is re-called after each pick instead of subscribing.
        ChaosBoonBarOverlay.EnsureCreated();

        // Apply equipped start boon, if any.
        var equipped = ChaosMeta.State.EquippedStartBoon;
        if (!string.IsNullOrEmpty(equipped))
        {
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null)
            {
                // ApplyBoon pushes the pick tile + "◈ {Name}" feed line itself now (WPF ChaosModels.cs:604-620).
                _state.ApplyBoon(boon);
            }
        }
        ChaosBoonBarOverlay.SetPicks(_state.RunPickTiles);

        // Welcome Shower equipped as the start boon: the very first GO! gets its treat dump too
        // (WPF ChaosModeService.cs:434-435).
        if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();

        // Apply lifetime boons (passive values + active toy power) and build HUD state.
        ChaosMeta.ApplyLifetimeBoons(_state);
        foreach (var lifetimeId in ChaosMeta.State.ActiveLifetimeBoons)
        {
            var lb = ChaosLifetimeBoons.ById(lifetimeId);
            _logger?.LogInformation("AvaloniaChaosService lifetime boon active: {Id}", lifetimeId);
            _state.PushEvent($"👝 loadout: {lb?.Name ?? lifetimeId}");
        }

        // Active skills: build state, listen for keybinds, spawn one hero button per toy.
        BuildActiveToys();
        _state.RaiseChanged(nameof(ChaosRunState.ActiveToys));

        // Re-push the knobs now that the equipped start boon, lifetime boons and toys have
        // mutated the run state (each was a live-lambda read in WPF, ChaosModeService.cs:363-386).
        SyncKnobsFromState();
        StartKeyHook();
        StartRippleHook();
        RunOnUi(() =>
        {
            _hud?.SetClockVisible(true);
            _hud?.SetHeroMode(preRun: false);
            _hud?.SetPreRunExpanded(false);

            for (int i = 0; i < _state.ActiveToys.Count; i++)
            {
                try
                {
                    var btn = new ChaosToyButtonWindow(_state.ActiveToys[i], this, i);
                    btn.Show();
                    _toyButtons.Add(btn);
                }
                catch (Exception ex) { _logger?.LogWarning(ex, "Chaos toy button init failed"); }
            }
        });

        StartTimers();
    }

    private void StartTimers()
    {
        StopTimers();
        _runTimer = StartPeriodicTimer(TimeSpan.FromMilliseconds(250), RunTick);
        // 800ms initial beat; SpawnTick re-arms its own interval every tick
        // (WPF ChaosModeService.cs:507-509 / :1219-1228).
        _spawnTimer = StartPeriodicTimer(TimeSpan.FromMilliseconds(800), SpawnTick);
    }

    private void StopTimers()
    {
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        _runTimer = null;
        _spawnTimer = null;
    }

    private void RunTick()
    {
        if (!_spawning || _state == null || _paused || _manualPaused || _ending) return;

        if (++_chromeRaiseTick >= 4)
        {
            _chromeRaiseTick = 0;
            KeepChromeTopmost();
            if (AvaloniaChaosApp.Video?.IsPlaying == true)
                RaiseGameLayerAboveVideo();
        }

        double dt = 0.25;
        _state.ElapsedSec += dt;
        _state.Heat = Math.Max(0, _state.Heat - 0.0015);

        // Power-ups run on the real clock (so they don't extend the run length)
        // (WPF ChaosModeService.cs:963-975).
        if (_slowMoRemainingSec > 0)
        {
            _slowMoRemainingSec -= dt;
            if (_slowMoRemainingSec <= 0) EndSlowMo();
        }
        if (_freezeRemainingSec > 0)
        {
            _freezeRemainingSec -= dt;
            if (_freezeRemainingSec <= 0) EndFreeze();
        }
        if (_snapFlashRemainingSec > 0) _snapFlashRemainingSec -= dt;

        // Empty-field rescue: a fast clear shouldn't leave dead air until the spawn timer's
        // next beat (gaps run ~1.3s early and slow-mo stretches them ~8x). The moment the
        // field is bare while spawning is live, pull the next spawn forward — SpawnTick
        // re-arms its own interval, so the cadence resumes cleanly from here
        // (WPF ChaosModeService.cs:974-980).
        if (_spawning && _freezeRemainingSec <= 0 && _bubbles.ActiveBubbles == 0)
            SpawnTick();

        // The Ripple recharges.
        if (_rippleCooldownSec > 0)
        {
            _rippleCooldownSec -= dt;
            if (_rippleCooldownSec <= 0)
                AvaloniaChaosSfx.Play("toy_ready", 0.3f);   // the ripple gathered (WPF ChaosModeService.cs:987)
        }
        _state.RippleReady = _rippleCooldownSec <= 0;
        _state.RippleText = _state.RippleReady ? "READY" : $"{Math.Ceiling(_rippleCooldownSec):0}s";

        TickActiveToys(dt);

        // WPF parity (ChaosTuning.cs:11-13): focus is EARNED by pops/rabbits only — no passive regen.

        // Advance active channel bookkeeping for the HUD.
        if (_state.IsChanneling)
        {
            _state.ChannelHeldSec = (DateTime.UtcNow - _state.ChannelStartTime).TotalSeconds;
        }

        ChaosHappyPath.Tick(dt);

        // rh_focus_low: warn once per run if focus sits below a defuse's price while lives remain.
        if (!_focusLowBarkFired && _state.FocusLow && _bubbles.ActiveBubbles > 0)
        {
            _focusLowAccumSec += dt;
            if (_focusLowAccumSec >= CoreChaosTuning.FOCUS_LOW_BARK_SEC)
            {
                _focusLowBarkFired = true;
                _state.PushEvent("◌ low focus. pop treats before you grab a live one.");
                try { AvaloniaChaosApp.Bark?.NotifyChaosFocusLow(); } catch { }
            }
        }
        else _focusLowAccumSec = 0;

        // Pop-up Notification habit: once per loop, sometimes (60%), a little heart drifts
        // down at a random beat. Catch = +1 resistance; a miss just exits the bottom
        // (WPF ChaosModeService.cs:2899-2913).
        double heartWaveLen = _state.RunDurationSec / Math.Max(1, _waveCount);
        double waveProgress = (_state.ElapsedSec % heartWaveLen) / heartWaveLen;   // WPF :1095
        if (_state.Config.PopupHeartEnabled && _spawning && _waveIndex != _heartRolledWave)
        {
            _heartRolledWave = _waveIndex;
            _heartArmedThisWave = _rng.NextDouble() < 0.60;
            _heartFireAtProgress = 0.20 + _rng.NextDouble() * 0.60;
        }
        if (_heartArmedThisWave && _spawning && waveProgress >= _heartFireAtProgress)
        {
            _heartArmedThisWave = false;
            _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildHeart(_rng));
            _bubbles.PlayChime(0.22f);   // the soft ping — a notification, after all (WPF :2911)
        }

        UpdateStateText();

        double waveDuration = _state.RunDurationSec / Math.Max(1, _waveCount);
        if (_state.ElapsedSec >= waveDuration * _waveIndex)
        {
            if (_waveIndex < _waveCount && _state.Config.BoonDraftEnabled)
            {
                ChaosLessonHooks.OnLoopCompleted();
                ShowDraft();
            }
            else if (_waveIndex >= _waveCount)
            {
                EndRun(ranFullCourse: true);
            }
            else
            {
                ChaosLessonHooks.OnLoopCompleted();
                _waveIndex++;
                _state.WaveIndex = _waveIndex;
                ShowWaveConversation(_waveIndex);
            }
        }
    }

    /// <summary>True while a mandatory video or gif cascade is (still) running. The timer
    /// floor (<see cref="_heavyUntilUtc"/>) covers window-open latency and the post-video
    /// teardown quarantine once the S6 payload paths arm it
    /// (WPF ChaosModeService.cs:2158-2162 HeavyEffectActive).</summary>
    private bool HeavyEffectActive =>
        AvaloniaChaosApp.Video?.IsPlaying == true || IsGifCascadeRaining || DateTime.UtcNow < _heavyUntilUtc;

    /// <summary>The faithful WPF spawn director (WPF ChaosModeService.cs:1103-1230 SpawnTick):
    /// behavioral roll first (replacing the ordinary slot), then the ordinary weighted pick with
    /// the end-of-loop video strip / Heavy Drop swap / freeze cap re-pick, then the golden/prism/
    /// brittle riders, then the cap-independent darter roll, then the self-retuning interval.</summary>
    private void SpawnTick()
    {
        if (!_spawning || _state == null || _paused || _manualPaused || _ending) return;   // WPF :1106
        if (_freezeRemainingSec > 0) return;   // time is frozen: hold the field, spawn nothing new (WPF :1107)

        var cfg = _state.Config;
        double intensity = _state.RunIntensity;                                                  // WPF :1110 (raw: density + cadence)
        double effIntensity = ChaosSpawnDirector.EffIntensity(intensity, cfg.DifficultyMult);    // WPF :1111 (picks + behavioral)
        double diffFactor = cfg.DifficultyMult;

        // Field density: 6 early → 16 late (×√difficulty) (WPF :1113-1117).
        int maxConcurrent = ChaosSpawnDirector.MaxConcurrent(intensity, diffFactor);
        // Behavioral bubbles (Echo/Chaperone/Tease/Bound): each rolls to REPLACE this ordinary
        // spawn slot, so the field density stays the same. Darters still roll below either way
        // (WPF :1118-1121).
        bool behavioralSpawned = _bubbles.ActiveBubbles < maxConcurrent
                                 && TrySpawnBehavioralBubble(cfg, effIntensity);
        if (!behavioralSpawned && _bubbles.ActiveBubbles < maxConcurrent)                        // WPF :1122
        {
            // Be gentle with the tape: no video bubble while a heavy effect (video/cascade) is
            // running, and none when the loop or run is too close to its end for the bubble's
            // fuse plus the 15s video slice to fit (WPF :1124-1135).
            IReadOnlyList<string>? enabled = cfg.EnabledVariants;
            double waveLen = _state.RunDurationSec / Math.Max(1, _state.WaveCount);
            double waveLeft = waveLen - (_state.ElapsedSec % waveLen);
            double runLeft = _state.RunDurationSec - _state.ElapsedSec;
            if (ChaosSpawnDirector.ShouldStripVideo(enabled, HeavyEffectActive, waveLeft, runLeft))
                enabled = enabled!.Where(id => id != "video").ToList();

            // Heavy Drop: every Nth ordinary spawn swaps for a giant, slow, triple-pay treat
            // (WPF :1137-1141).
            ChaosBubbleSpec spec;
            if (_state.HeavyDropEvery > 0 && ++_spawnSerial % _state.HeavyDropEvery == 0)
            {
                spec = ChaosSpawnCatalog.BuildHeavy(effIntensity, cfg.EffectIntensity, _state.BubbleScale, _rng);
            }
            else
            {
                // Side entries arm only after the first few spawns (WPF :1145-1148).
                double sideDrift = ChaosSpawnDirector.SideDriftChance(_ordinarySpawns);
                spec = ChaosSpawnCatalog.Pick(effIntensity, _state.FuseTimeMult,
                    cfg.MotionOverride, enabled, cfg.EffectIntensity, _state.BubbleScale, sideDrift, _rng);

                // Freeze cap: at most FREEZE_MAX_ON_SCREEN freeze pickups live at once — re-pick
                // with freeze excluded so the slot still spawns something (WPF :1151-1162).
                if (spec.IsFreeze
                    && _bubbles.ActiveFreezeBubbles >= CoreChaosTuning.FREEZE_MAX_ON_SCREEN)
                {
                    var noFreeze = (enabled ?? ChaosSpawnCatalog.AllIds())
                        .Where(id => id != "bambifreeze").ToList();
                    spec = ChaosSpawnCatalog.Pick(effIntensity, _state.FuseTimeMult,
                        cfg.MotionOverride, noFreeze, cfg.EffectIntensity, _state.BubbleScale, sideDrift, _rng);
                }
            }
            _ordinarySpawns++;
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            _bubbles.SpawnChaosBubble(spec);

            // Lucky golden bubble: a rare bonus roll riding every ordinary spawn (base 0.5%;
            // Rabbit's Foot raises it) (WPF :1168-1175).
            if (_rng.NextDouble() < _state.GoldenChance)
            {
                ChaosMeta.MarkDiscovered("bubble:golden");
                _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildGolden(_rng));
                _bubbles.PlayChime(0.30f);   // a soft chime so a sharp ear catches the chance (WPF :1174)
            }

            // "Look at the bright colors..." sin: sometimes a mimic prism drifts in (WPF :1177-1183).
            if (_state.PrismChance > 0 && _rng.NextDouble() < _state.PrismChance)
            {
                ChaosMeta.MarkDiscovered("bubble:prism");
                _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildPrism(effIntensity, cfg.EffectIntensity,
                    treatOnly: _state.PrismTreatOnly, _rng));
            }

            // The Brittle (Tempted+, half odds on Gentle — rank-not-difficulty rule): a glass
            // mine rides in alongside the field (WPF :1185-1201).
            if (ChaosMeta.AtLeast(ChaosRank.Tempted)
                && _rng.NextDouble() < ChaosSpawnDirector.BehavioralChance(
                    CoreChaosTuning.BRITTLE_SPAWN_CHANCE, cfg.Difficulty == "Easy"))
            {
                if (!ChaosMeta.State.SeenBrittle)
                {
                    ChaosMeta.State.SeenBrittle = true; ChaosMeta.Save();
                    AnnounceChaos("◇ THE BRITTLE — don't even hover", ChaosAnnounceKind.Temptation,
                        artKey: "brittle", subText: "don't even hover");
                    _state.PushEvent("◇ thin glass drifts in. steer around it.");
                }
                ChaosMeta.MarkDiscovered("bubble:brittle");
                _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildBrittle(effIntensity,
                    cfg.EffectIntensity, _state.BubbleScale, _rng));
            }
        }

        // Darters spawn on their own intensity-scaled roll, independent of the bubble cap
        // (WPF :1204-1214 — NOTE: WPF passes effIntensity here, not raw intensity; the
        // contract's §5.7 "intensity" is WPF's effIntensity argument). WPF keeps no darter
        // Seen*/debut flag — only MarkDiscovered.
        if (cfg.DartersEnabled)
        {
            var darter = ChaosSpawnCatalog.RollDarter(effIntensity, _state.RabbitRateMult, _rng, spotlight: false);
            if (darter != null)
            {
                ChaosMeta.MarkDiscovered("bubble:darter");
                _bubbles.SpawnChaosBubble(darter);
            }
        }

        // Refill cadence: 1000ms early → 320ms late (÷difficulty, ÷SpawnRateMult, ÷slow-mo,
        // floor 280ms), re-armed every tick (WPF :1217-1228). The WPF ×_perfBackoff factor is
        // its frame-hitch governor — this head has no governor field, so none is invented.
        double interval = ChaosSpawnDirector.SpawnIntervalMs(intensity, diffFactor,
            cfg.SpawnRateMult, _slowMoRemainingSec > 0);
        if (_spawnTimer != null) _spawnTimer.Interval = TimeSpan.FromMilliseconds(interval);
    }

    /// <summary>
    /// Roll the behavioral bubbles for this spawn slot (WPF ChaosModeService.cs:1244-1329).
    /// A hit REPLACES the ordinary spawn (density stays sane; a debut also consumes the tick →
    /// it spawns alone). Gating is by RANK, not difficulty: Echo + Chaperone from Tempted,
    /// Tease from Slipping, Bound from Entranced (or any Hard+ descent). Gentle halves every
    /// roll instead of forbidding the menagerie. Debuts get a gentler trance and announce.
    /// </summary>
    private bool TrySpawnBehavioralBubble(ChaosRunConfig cfg, double effIntensity)
    {
        if (_state == null) return false;
        if (cfg.ScriptedFirstRun) return false;   // run 1 is scripted: no behavioral bubbles at all (WPF :1246)
        bool easy = cfg.Difficulty == "Easy";     // gentleMult (WPF :1247)

        // The Echo (Tempted+): trigger it and it multiplies; only the held defuse is clean (WPF :1249-1265).
        if (ChaosMeta.AtLeast(ChaosRank.Tempted)
            && _rng.NextDouble() < ChaosSpawnDirector.BehavioralChance(CoreChaosTuning.ECHO_SPAWN_CHANCE, easy))
        {
            bool debut = !ChaosMeta.State.SeenEcho;
            if (debut)
            {
                ChaosMeta.State.SeenEcho = true; ChaosMeta.Save();
                AnnounceChaos("◌ THE ECHO — hold it down, or it multiplies", ChaosAnnounceKind.Item,
                    artKey: "echo", subText: "hold it down, or it multiplies");
                _state.PushEvent("◌ something doubled stirs below");
            }
            ChaosMeta.MarkDiscovered("bubble:echo");
            _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildEcho(effIntensity, _state.FuseTimeMult,
                _state.BubbleScale, debut ? CoreChaosTuning.DEBUT_FUSE_MULT : 1.0, _rng,
                effectIntensity: cfg.EffectIntensity));
            return true;
        }

        // The Chaperone (Tempted+): shielded while its escort circles — pop the escort first (WPF :1267-1284).
        if (ChaosMeta.AtLeast(ChaosRank.Tempted)
            && _rng.NextDouble() < ChaosSpawnDirector.BehavioralChance(CoreChaosTuning.CHAPERONE_SPAWN_CHANCE, easy))
        {
            bool debut = !ChaosMeta.State.SeenChaperone;
            if (debut)
            {
                ChaosMeta.State.SeenChaperone = true; ChaosMeta.Save();
                AnnounceChaos("💞 THE CHAPERONE — its little escort first", ChaosAnnounceKind.Item,
                    artKey: "chaperone", subText: "its little escort first");
                _state.PushEvent("💞 it brought company");
            }
            var (live, escort) = ChaosSpawnCatalog.BuildChaperonePair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? CoreChaosTuning.DEBUT_FUSE_MULT : 1.0, _rng);
            ChaosMeta.MarkDiscovered("bubble:chaperone");
            _bubbles.SpawnChaosChaperone(live, escort);
            return true;
        }

        // The Bound (Hard+ descents, or the Entranced rank on any difficulty):
        // two lives, one thread — both must come down quickly (WPF :1286-1303).
        if ((cfg.Difficulty is "Hard" or "Extreme" || ChaosMeta.AtLeast(ChaosRank.Entranced))
            && _rng.NextDouble() < ChaosSpawnDirector.BehavioralChance(CoreChaosTuning.BOUND_SPAWN_CHANCE, easy))
        {
            bool debut = !ChaosMeta.State.SeenBound;
            if (debut)
            {
                ChaosMeta.State.SeenBound = true; ChaosMeta.Save();
                AnnounceChaos("⛓ THE BOUND — both, and quickly", ChaosAnnounceKind.Item,
                    artKey: "bound", subText: "both, and quickly");
                _state.PushEvent("⛓ two of them, one thread");
            }
            var (a, b) = ChaosSpawnCatalog.BuildBoundPair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? CoreChaosTuning.DEBUT_FUSE_MULT : 1.0, _rng);
            ChaosMeta.MarkDiscovered("bubble:bound");
            _bubbles.SpawnChaosBoundPair(a, b);
            return true;
        }

        // The Tease (Slipping rank): the one you beat by NOT touching it (WPF :1305-1326).
        if (ChaosMeta.AtLeast(ChaosRank.Slipping)
            && _rng.NextDouble() < ChaosSpawnDirector.BehavioralChance(CoreChaosTuning.TEASE_SPAWN_CHANCE, easy))
        {
            bool debut = !ChaosMeta.State.SeenTease;
            if (debut)
            {
                ChaosMeta.State.SeenTease = true; ChaosMeta.Save();
                AnnounceChaos("✖ THE TEASE — whatever you do, don't", ChaosAnnounceKind.Temptation,
                    artKey: "tease", subText: "whatever you do, don't");
                _state.PushEvent("✖ it wants your hand. don't.");
                // WPF :1318 App.Bark?.NotifyChaosTeaseDebut() — IBarkService has no such member
                // in this head yet (follow-up row; not faked).
            }
            ChaosMeta.MarkDiscovered("bubble:tease");
            _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildTease(effIntensity,
                cfg.EffectIntensity, _state.BubbleScale, _rng));
            return true;
        }

        return false;
    }

    /// <summary>Welcome Shower: dump a handful of treats (flash/subliminal) raining from the
    /// top — at run start and each loop GO when the boon holds (WPF ChaosModeService.cs:1649-1665).</summary>
    private void SpawnWelcomeShower()
    {
        if (_state == null) return;
        try
        {
            const int count = 6;
            for (int i = 0; i < count; i++)
                _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildWelcomeShowerTreat(
                    _state.RunIntensity, _state.FuseTimeMult, _state.Config.EffectIntensity,
                    _state.BubbleScale, _rng));
            _bubbles.PlayChime(0.25f);   // WPF :1663
            _state.PushEvent("🚿 welcome shower — treats from above");
        }
        catch (Exception ex) { _logger?.LogDebug("SpawnWelcomeShower: {E}", ex.Message); }
    }

    private void OnBenignPopped(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        ChaosNarrativeHooks.OnFirstPop(BuildNarrativeContext(depth: _waveIndex));

        // ---- pickup early-returns: OUTSIDE the score/combo economy — they bank gold or
        //      resistance; never touch Score/Combo, no mults/flips (WPF ChaosModeService.cs:1780-1828) ----

        // Pop-up Notification heart: pure kindness — +1 resistance + focus; no points, no
        // streak, no payload (WPF ChaosModeService.cs:1780-1791).
        if (spec.IsHeart)
        {
            _state.Shields++;
            _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_HEART);
            _state.PushEvent("💖 pop-up notification! +1 resistance");
            AvaloniaChaosSfx.Play("resist_absorb", 0.55f);   // WPF ChaosModeService.cs:1790
            UpdateStateText();
            return;
        }

        // Gold Digger droplet: a little gold per bead, outside the score economy like its
        // parent (WPF ChaosModeService.cs:1794-1802). Gold doubles in the Relapse loop.
        if (spec.IsDroplet)
        {
            _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_DROPLET);
            int dGold = GoldScaled(_rng.Next(3, 8));
            ChaosMeta.AddGold(dGold);
            ChaosHappyPath.OnGoldFirstSeen();   // WPF BankGold's first-gold beat (ChaosModeService.cs:1713-1726)
            _state.PushEvent($"{ChaosGlyphs.Gold} droplet +{dGold} gold");
            AvaloniaChaosSfx.Play("golden_pop", 0.35f);   // WPF ChaosModeService.cs:1801
            UpdateStateText();
            return;
        }

        // Lucky golden bubble: pure treasure — real gold banked instantly, outside the
        // score/combo economy entirely (WPF ChaosModeService.cs:1804-1828). Rabbit's Foot
        // scales the gold per level (10-20 unworn … 20-40 at the capstone).
        if (spec.IsGolden)
        {
            _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_GOLDEN);
            int lvl = ChaosMeta.IsBoonActive("rabbits_foot") ? ChaosMeta.BoonLevel("rabbits_foot") : 0;
            var (gMin, gMax) = ChaosLifetimeBoons.GoldenPayRange(lvl);
            int gold = GoldScaled(_rng.Next(gMin, gMax + 1));
            ChaosMeta.AddGold(gold);
            ChaosHappyPath.OnGoldFirstSeen();
            _state.PushEvent($"{ChaosGlyphs.Gold} lucky bubble! +{gold} gold");
            AvaloniaChaosSfx.Play("golden_pop", 0.6f);   // coins spill (WPF ChaosModeService.cs:1814)
            UpdateStateText();
            return;
        }

        // Freeze pickup: the engine's onFreezeCaught callback carries it now (S4); the
        // delegation keeps any residual pop path faithful (WPF ChaosModeService.cs:2300-2318).
        if (spec.IsFreeze)
        {
            OnFreezeCaught(spec);
            return;
        }

        // Mimic prism ("Look at the bright colors..."): 10x pay — and the copied effect fires
        // (WPF ChaosModeService.cs:1829-1852). Side effects BEFORE scoring so the new combo
        // step and heat feed the multiplier stack, exactly like WPF.
        if (spec.IsPrism)
        {
            ChaosLessonHooks.OnPrismPopped();   // taking_chances lesson (WPF :1831)
            _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_PRISM);
            FirePayload(spec);                  // the mimicked payload fires (WPF FireScaledPayload :1833)
            _state.EffectsFired++;
            _state.Combo++;
            _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
            double prismPts = ChaosScoring.PrismScore(spec.Strength, _state.TotalMult, _state.BlindfoldPayMult);
            _state.Score += prismPts;
            try { _achievements?.TrackBubblePopped(); } catch { }
            _state.PushEvent($"🔮 prism! 10x · {(string.IsNullOrEmpty(spec.MimicVariantId) ? spec.PayloadKind : spec.MimicVariantId)} fires");
            UpdateStateText();
            return;
        }

        // ---- treat pop (WPF ChaosModeService.cs:1854-1900): the payload fires FIRST, then
        //      the exact WPF side-effect order — EffectsFired, Combo, Focus, Heat — THEN the
        //      score, THEN the Drip Feed trickle. ----
        FirePayload(spec);   // benign pop = a treat (WPF spec.Payload.Fire() :1854)
        ChaosLessonHooks.OnTreatPopped(spec.VariantId);   // vibe_popping / chain_reaction / … (WPF :1856)
        _state.EffectsFired++;
        _state.Combo++;
        // Focus economy: every treat-class pop refuels the hand; heavies refuel a little extra
        // (WPF :1858-1861).
        double focusGain = ChaosScoring.FocusForTreatPop(spec.PayMult);
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + focusGain);
        _state.Heat = Math.Min(1.0, _state.Heat + 0.04);
        double pts = ChaosScoring.TreatPopScore(spec.Strength, _state.BenignBaseline, spec.PayMult,
            ChaosScoring.PendulumFactor(_pendulumSlowActive, _state.PendulumPayMult),
            ChaosScoring.ChanceFlip(_state.ChanceDoubleOdds, _rng),
            _state.TotalMult, _state.BlindfoldPayMult);
        _state.Score += pts;
        BankDripFeed();   // Drip Feed (x2 in the relapse loop, clamped to the per-descent cap) (WPF :1870)
        // Achievement bubble-pop tracker: every benign/prism pop counts (WPF ChaosModeService.cs:1843,1869).
        try { _achievements?.TrackBubblePopped(); } catch { }
        if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
        // GG make more GG: sometimes a popped treat bursts into 3 wild sweeper rabbits at the
        // pop point (WPF ChaosModeService.cs:1892-1900).
        if (_state.GgRabbitChance > 0 && _rng.NextDouble() < _state.GgRabbitChance)
        {
            for (int i = 0; i < 3; i++)
                _bubbles.SpawnChaosBubble(ChaosSpawnCatalog.BuildDarter(_state.RunIntensity, spotlight: false,
                    sweeper: true, _rng,
                    _bubbles.ChaosLastPopX + _rng.Next(-40, 41),
                    _bubbles.ChaosLastPopY + _rng.Next(-40, 41)));
            AvaloniaChaosSfx.Play("rabbit_spawn", 0.5f);   // WPF :1899
            _state.PushEvent("🐇 GG! they multiply");
        }
        _state.PushEvent($"○ popped {spec.VariantId}");
        UpdateStateText();
    }

    private void OnDefused(ChaosBubbleSpec spec, double fuseSecLeft, bool viaChannel)
    {
        if (_state == null || _paused || _manualPaused) return;

        // The player's hand pays for its defuses; toys, chains and zones never do. A channel
        // completed during a freeze is FREE — that's the freeze's reward
        // (WPF ChaosModeService.cs:2003-2005).
        if (viaChannel)
        {
            if (_freezeRemainingSec <= 0)
                _state.Focus = Math.Max(0, _state.Focus - ChaosScoring.DefuseCostFor(spec.IsBoundHalf));
            ChaosMeta.State.TotalChannelSeconds += _state.ChannelHeldSec;
            _state.IsChanneling = false;
            _state.ChannelHeldSec = 0;
            _state.ChannelTargetBubbleId = null;
        }

        // Snap Chain mantra: every completed defuse opens a brief invulnerability window
        // (WPF ChaosModeService.cs:2012-2013).
        if (_state.DefuseInvulnMs > 0)
            _invulnUntilUtc = DateTime.UtcNow.AddMilliseconds(_state.DefuseInvulnMs);

        ChaosLessonHooks.OnDefuseCompleted(fuseSecLeft, viaChannel);
        ChaosNarrativeHooks.OnFirstDefuse(BuildNarrativeContext(depth: _waveIndex));
        ChaosHappyPath.OnDefuseCompleted();

        // Side effects BEFORE scoring — Defused/Combo/Heat feed the multiplier stack for THIS
        // snap, exactly like WPF (ChaosModeService.cs:2009-2011). BestCombo tracks in
        // UpdateStateText (WPF: the Combo setter, ChaosModels.cs:490).
        _state.Defused++;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.07);
        double pts = ChaosScoring.DefuseScore(spec.Strength, fuseSecLeft,
            _state.LastBreathWindowSec, _state.LastBreathPayMult,
            _state.MaxedBoons.Contains("slowburner"),
            ChaosScoring.PendulumFactor(_pendulumSlowActive, _state.PendulumPayMult),
            ChaosScoring.ChanceFlip(_state.ChanceDoubleOdds, _rng),
            _state.TotalMult, _state.BlindfoldPayMult);
        _state.Score += pts;
        BankDripFeed();   // Drip Feed (x2 in the relapse loop, clamped to the per-descent cap) (WPF :2023)
        try { _achievements?.TrackBubblePopped(); } catch { }
        // Last Breath / Slowburner feed lines so score spikes explain themselves (WPF :2044-2052).
        if (_state.LastBreathWindowSec > 0 && fuseSecLeft <= _state.LastBreathWindowSec && _state.LastBreathPayMult > 1)
            _state.PushEvent($"⏱ last breath! x{_state.LastBreathPayMult:0}");
        if (fuseSecLeft <= 1.5 && _state.MaxedBoons.Contains("slowburner"))
            _state.PushEvent("🐌 slow burn! x3");
        _state.PushEvent($"✔ snapped {spec.VariantId}");
        UpdateStateText();
    }

    private void OnDetonated(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;

        // The Tease and the Brittle carry their FULL consequences in their own handlers
        // (WPF OnTeaseTouched :1330-1360 does Detonated++/payload/shield itself; the Brittle
        // "never counts as a missed trance" :1365-1368). The Core engine fires the generic
        // detonate callback alongside those handlers — swallow it here so nothing double-fires.
        if (spec.IsTease || spec.IsBrittle) return;

        // The Echo fires NO conditioning payload — it SPLITS (WPF ChaosModeService.cs:2077-2084).
        // The engine raises EchoSplitRequested for the child spawns (AvaloniaBubbleService wires
        // it); the detonation consequences below still apply to the parent.
        if (!spec.IsEcho)
        {
            FirePayload(spec);   // the threat goes off (WPF FirePayloadForDetonation :2082)
            _state.EffectsFired++;
        }
        _state.Detonated++;
        ChaosLessonHooks.OnDetonation();
        ChaosNarrativeHooks.OnFirstDetonation(BuildNarrativeContext(depth: _waveIndex));

        // Snap Chain mantra: inside the post-snap invulnerability window a trigger can't take
        // anything — the payload already fired above, but streak, heat and resistance all hold,
        // and no shield is spent (WPF ChaosModeService.cs:2094-2101).
        if (DateTime.UtcNow < _invulnUntilUtc)
        {
            _state.PushEvent($"⛓ snap chain holds ({spec.VariantId})");
            UpdateStateText();
            return;
        }

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            // Resistance absorbs the hit: combo PRESERVED, heat cools by 0.2
            // (WPF ChaosModeService.cs:2103-2115).
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            _state.PushEvent($"♥ resistance crumbles ({spec.VariantId})");
            // The last point of resistance going has its own, sadder cue (WPF :2109).
            AvaloniaChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
        }
        else if (_state.CollarSaves > 0)
        {
            // Collar: out of resistance, but the streak is held — combo, heat and lust survive
            // the hit. The payload still fired above; the collar protects the chain, not the
            // screen (WPF ChaosModeService.cs:2117-2136).
            _state.CollarSaves--;
            _state.PushEvent($"📿 the collar holds ({_state.CollarSaves} left)");
            AvaloniaChaosSfx.Play("collar_save", 0.6f);
            // Unleashed: the save ITSELF strikes back — a golden shockwave snaps every live bubble.
            if (_state.UnleashedEnabled)
            {
                _bubbles.DefuseAllLive();
                _state.PushEvent("📿 unleashed — the field lets go");
            }
        }
        else
        {
            // Bare hit: streak breaks and the heat GUTTERS to zero — WPF zeroes Heat here, it
            // does NOT nibble it by 0.15 (WPF ChaosModeService.cs:2138-2151).
            _state.Combo = 0;
            _state.Heat = 0;
            _state.PushEvent($"💥 {spec.VariantId} triggered!");
            AvaloniaChaosSfx.Play("trigger", 0.55f);   // the muffled boom under the payload stinger (WPF :2145)
        }
        UpdateStateText();
    }

    /// <summary>Build an effect payload from a bubble spec's PayloadKind.</summary>
    private static EffectPayload? BuildPayload(ChaosBubbleSpec spec)
    {
        try
        {
            EffectPayload payload;
            switch (spec.PayloadKind)
            {
                case "flash": payload = new FlashPayload(); break;
                case "subliminal": payload = new SubliminalPayload(); break;
                case "pink": payload = new OverlayPayload("pink_filter"); break;
                case "spiral": payload = new OverlayPayload("spiral"); break;
                case "braindrain": payload = new OverlayPayload("braindrain"); break;
                case "bambifreeze": payload = new BambiFreezePayload(); break;
                case "video": payload = new VideoPayload(); break;
                case "htlink": payload = new GifCascadePayload(); break;
                default: payload = new FlashPayload(); break;
            }

            double size = spec.SizePx;
            const double sizeMin = 150;
            const double sizeMax = 320;
            int strength = (int)Math.Round(Math.Clamp((size - sizeMin) / (sizeMax - sizeMin), 0, 1) * 100);
            payload.Strength = strength;
            return payload;
        }
        catch { return null; }
    }

    private static void FirePayload(ChaosBubbleSpec spec)
    {
        var payload = BuildPayload(spec);
        payload?.Fire();
    }

    /// <summary>Focus cost for one channel (Bound halves pay half each) — the formula lives in
    /// <see cref="ChaosScoring.DefuseCostFor"/> (WPF ChaosModeService.cs:1927-1929).</summary>
    private double DefuseCostFor(ChaosBubbleSpec spec) => ChaosScoring.DefuseCostFor(spec.IsBoundHalf);

    /// <summary>May the player's press start a defuse channel? Frozen fields channel for FREE —
    /// otherwise the focus must cover the bubble's cost (deducted on COMPLETION, not here)
    /// (WPF ChaosModeService.cs:1933-1938).</summary>
    private bool CanChannelDefuse(ChaosBubbleSpec spec)
    {
        if (_state == null) return false;
        return _freezeRemainingSec > 0 || _state.Focus >= DefuseCostFor(spec);
    }

    /// <summary>Relapse's bonus loop pays double gold — every gold bank routes through here
    /// (WPF ChaosModeService.cs:1705).</summary>
    private int GoldScaled(int gold) => _state?.RelapseLoopActive == true ? gold * 2 : gold;

    /// <summary>Drip Feed drops per pop, doubled during the Relapse bonus loop
    /// (WPF ChaosModeService.cs:1744).</summary>
    private int DropsPerPopNow() => (_state?.DropPerPop ?? 0) * (_state?.RelapseLoopActive == true ? 2 : 1);

    /// <summary>Drip Feed: bank the per-pop trickle, doubled during the Relapse bonus loop,
    /// clamped to the level's per-descent ceiling (the cap bounds the doubling too)
    /// (WPF ChaosModeService.cs:1746-1752; cap WPF ChaosLifetimeBoons.cs:412).</summary>
    private void BankDripFeed()
    {
        if (_state == null || _state.DropPerPop <= 0) return;
        long cap = ChaosLifetimeBoons.DripFeedCap(_state.DropPerPop);
        _state.TrickleDrops = Math.Min(cap, _state.TrickleDrops + DropsPerPopNow());
    }

    private void OnChannelStarted(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnChannelStarted();
        _state.IsChanneling = true;
        _state.ChannelStartTime = DateTime.UtcNow;
        _state.ChannelHeldSec = 0;
        _state.ChannelTargetBubbleId = spec.Id.ToString();
        UpdateStateText();
    }

    private void OnChannelBroken(ChaosBubbleSpec spec, string reason)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnChannelBroken();
        _state.IsChanneling = false;
        _state.ChannelHeldSec = 0;
        _state.ChannelTargetBubbleId = null;

        switch (reason)
        {
            case "nofocus":
                _state.PushEvent("✋ no focus — it triggers in your grip");
                break;
            case "click":
                _state.PushEvent("💥 a tap isn't a hold");
                break;
            default: // "release"
                _state.PushEvent("💥 you let go");
                break;
        }
        UpdateStateText();
    }

    // ============================ behavioral / pickup callbacks (S4) ============================

    /// <summary>White-rabbit darter caught: score + focus + streak, then the slow-mo window —
    /// a rabbit clicked while time is ALREADY slow tops the window up by +0.8s instead of
    /// re-arming the full duration (WPF ChaosModeService.cs:2270-2295).</summary>
    private void OnDarterCaught(ChaosBubbleSpec spec, bool quick)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Score += ChaosScoring.DarterScore(quick, _state.TotalMult);   // 120 + 90 quick, x TotalMult (WPF :2273-2274)
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_RABBIT);   // WPF :2275
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        try { _achievements?.TrackBubblePopped(); } catch { }
        ChaosLessonHooks.OnRabbitCaught();   // rabbit_caller lesson (WPF :2280)
        // The darter is a utility pickup: catching it slows time (no conditioning jolt) (WPF :2285-2291).
        bool extended = _slowMoRemainingSec > 0;
        if (extended)
        {
            _slowMoRemainingSec += 0.8;
            _pendulumSlowActive = false;
        }
        else ActivateSlowMo();
        // WPF :2292 Pulse(120,200,255) — no full-screen pulse seam in this head yet (follow-up).
        // WPF :2293 App.Bark?.NotifyChaosDarterCaught(...) — IBarkService lacks the member (follow-up).
        _state.PushEvent(extended ? "🐇 caught in the slow! +0.8s"
            : quick ? "⚡ quick catch! time slows" : "🐇 white rabbit caught! time slows");
        UpdateStateText();
    }

    /// <summary>The Spanker: a rabbit took its first smack. With the Spanker equipped rabbits
    /// can't be caught at all, so the first smack is what counts toward the rabbit_caller lesson
    /// (WPF ChaosLessonHooks.OnRabbitSpanked, fired from BubbleService.cs:3789 on the first smack).
    /// The scoring/slow-mo for a catch lives in <see cref="OnDarterCaught"/>; this is the
    /// lesson-only hook the engine fires on a darter's first pointer-down.</summary>
    private void OnDarterSpanked(ChaosBubbleSpec spec, bool quick)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnRabbitSpanked();
    }

    /// <summary>Freeze pickup: a GOOD catch — pays 140 x TotalMult (NO BoonPayMult), feeds the
    /// streak and heat, then holds the whole field (WPF ChaosModeService.cs:2300-2318).</summary>
    private void OnFreezeCaught(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Score += ChaosScoring.FreezeScore(_state.TotalMult);   // WPF :2311
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        try { _achievements?.TrackBubblePopped(); } catch { }
        ChaosLessonHooks.OnFreezeCaught();   // freeze_trigger lesson (pickups only) (WPF :2315)
        ActivateFreeze();
        // WPF :2317 App.Bark?.NotifyChaosFreezeCaught(...) — IBarkService lacks the member (follow-up).
        _state.PushEvent("❄ frozen. the field holds");
        UpdateStateText();
    }

    /// <summary>A mouse-down landed on a Tease: its payload fires (resistance can absorb THAT,
    /// nothing else) and the streak HALVES no matter what — that's the price of touching
    /// (WPF ChaosModeService.cs:1330-1360).</summary>
    private void OnTeaseTouched(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Detonated++;   // WPF :1333
        ChaosLessonHooks.OnDetonation();   // silk_touch: a touched Tease dirties the loop too (WPF :1336)

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            // Resistance prevents only the payload — the streak still pays below (WPF :1340-1346).
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            AvaloniaChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the sting ({spec.PayloadKind})");
        }
        else
        {
            FirePayload(spec);   // WPF FirePayloadForDetonation :1349
            _state.EffectsFired++;
            AvaloniaChaosSfx.Play("trigger", 0.55f);
            // WPF :1352 Shake(0.3+s*0.4, 320) — no screen-shake seam in this head yet (follow-up).
        }

        _state.Combo = _state.Combo > 1 ? _state.Combo / 2 : 0;   // ALWAYS — the price of touching (WPF :1355)
        _state.PushEvent($"✖ you touched it. it laughs — streak halves to x{_state.Combo}");
        // WPF :1358 Pulse(FF3D5A, 0.38) — no pulse seam (follow-up).
        // WPF :1359 App.Bark?.NotifyChaosTeaseClicked() — IBarkService lacks the member (follow-up).
        UpdateStateText();
    }

    /// <summary>The Tease expired untouched: restraint pays — gold, score AND focus
    /// (WPF ChaosModeService.cs:1405-1425).</summary>
    private void OnTeaseDenied(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        int gold = GoldScaled(_rng.Next(CoreChaosTuning.TEASE_GOLD_MIN, CoreChaosTuning.TEASE_GOLD_MAX + 1));   // WPF :1408
        ChaosMeta.AddGold(gold);   // WPF BankGold :1409
        ChaosHappyPath.OnGoldFirstSeen();
        _state.Score += ChaosScoring.TeaseDeniedScore(_state.TotalMult, _state.BlindfoldPayMult);   // 120 x mults (WPF :1410-1412)
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + CoreChaosTuning.FOCUS_PER_DENIED);  // restraint feeds focus (WPF :1413)
        _state.PushEvent($"{ChaosGlyphs.Gold} denied. it pays +{gold} gold");
        AnnounceChaos($"DENIED. +{gold} {ChaosGlyphs.Gold} gold", ChaosAnnounceKind.PowerUp,
            artKey: "denied", subText: $"+{gold} {ChaosGlyphs.Gold} gold");   // WPF :1416-1417
        // WPF :1418 Pulse(FFD700, 0.25) — no pulse seam (follow-up).
        // WPF :1420-1424 NotifyChaosTeaseDenied / the 5-streak NotifyChaosTeaseDeniedStreak —
        // IBarkService lacks both members (follow-up); the counters stay live for that port.
        _teaseDeniedThisRun++;
        if (_teaseDeniedThisRun >= CoreChaosTuning.TEASE_DENIED_STREAK_COUNT && !_teaseDeniedStreakBarked)
            _teaseDeniedStreakBarked = true;
        UpdateStateText();
    }

    /// <summary>A Bound survivor's tether snapped — it enraged. The juice lives here; in WPF
    /// the trance-halving/speed-up is engine-side (WPF ChaosModeService.cs:1395-1404). The Core
    /// BubbleEngine now ENRAGES the survivor in place (S4b-1: halves the fuse, scales drift by
    /// BOUND_ENRAGE_SPEED_MULT, keeps it alive) instead of detonating it.</summary>
    private void OnBoundEnraged(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        AvaloniaChaosSfx.Play("toy_denied", 0.5f);   // a sharp denial sting until a dedicated cue ships (WPF :1399)
        // WPF :1400 Pulse(FF4A4A, 0.30) — no pulse seam (follow-up).
        _state.PushEvent("⛓ the tether snaps — it enrages");
    }

    /// <summary>The Brittle shattered — the cursor brushed (or pressed) the glass. The mimic's
    /// live effect fires; resistance can absorb the payload but unlike the Tease the streak is
    /// spared — the effect itself is the whole price. Never counts as a missed trance
    /// (WPF ChaosModeService.cs:1365-1392).</summary>
    private void OnBrittleShattered(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        AvaloniaChaosSfx.Play(AvaloniaChaosSfx.ResolvePath("glass_shatter").Length > 0 ? "glass_shatter" : "trigger", 0.55f);   // WPF :1368

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            AvaloniaChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the shards ({spec.PayloadKind})");
        }
        else
        {
            FirePayload(spec);   // WPF FirePayloadForDetonation :1381
            _state.EffectsFired++;
            // WPF :1384 Shake(0.25+s*0.35, 300) — no screen-shake seam (follow-up).
            _state.PushEvent($"◇ it shatters — {spec.PayloadKind} was inside");
        }
        // WPF :1389 Pulse(BFE6FF, 0.32) — no pulse seam (follow-up).
        UpdateStateText();
    }

    /// <summary>A treat (flash/subliminal/golden) sat unpopped past its screen life: it
    /// dissolved — no pop, no payload — and the streak HALVES (WPF ChaosModeService.cs:1901-1920).
    /// The heart and gold droplets are exempt: a missed heart "just exits the bottom"
    /// (WPF ChaosModeService.cs:2897-2898) — the Core engine fires this callback for them
    /// too, so they are swallowed here.</summary>
    private void OnTreatExpired(ChaosBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        if (spec.IsHeart || spec.IsDroplet) return;   // kindness pickups never punish a miss
        string name = spec.IsGolden ? "lucky bubble" : spec.VariantId;
        if (_state.Combo > 1)
        {
            _state.Combo /= 2;
            _state.PushEvent($"💨 {name} faded… streak halved to x{_state.Combo}");
        }
        else
        {
            _state.Combo = 0;
            _state.PushEvent($"💨 {name} faded away");
        }
        UpdateStateText();
    }

    // ---- darter slow-mo power-up (WPF ChaosModeService.cs:2323-2394) ----

    /// <summary>Catching a darter slows the whole field: bubbles drift slower, live fuses last
    /// longer, spawns stretch out, and payloads linger. Refreshes on each catch
    /// (WPF ChaosModeService.cs ActivateSlowMo :2966+).</summary>
    private void ActivateSlowMo(double? durationSec = null, string bannerLabel = "Time Slow")
    {
        _slowMoRemainingSec = durationSec
            ?? (ChaosSpawnDirector.SLOWMO_DURATION_SEC + (_state?.SlowMoBonusSec ?? 0));
        // "Focus here...": triple pay rides ONLY the pendulum's own swing (WPF :2336-2338).
        _pendulumSlowActive = bannerLabel == "Pendulum";
        _bubbles.SetChaosTimeScale(ChaosSpawnDirector.SLOWMO_FACTOR);
        ShowEffectBanner("slowmo", bannerLabel, global::Avalonia.Media.Color.FromRgb(0x7A, 0xE0, 0xFF),
            artKey: bannerLabel == "Pendulum" ? "pendulum" : "slowmo");   // WPF :2340-2341
        EffectPayload.GlobalDurationMult = 1.0 / ChaosSpawnDirector.SLOWMO_FACTOR;   // payloads linger (WPF :2342)
        if (!_slowMoCueOn) AvaloniaChaosSfx.Play("time_slow_in", 0.5f);   // refreshes shouldn't re-warp (WPF :2343)
        _slowMoCueOn = true;
    }

    private void EndSlowMo()
    {
        _slowMoRemainingSec = 0;   // WPF :2386-2394
        _pendulumSlowActive = false;
        _bubbles.SetChaosTimeScale(1.0);
        EndEffectBanner("slowmo");
        if (_slowMoCueOn) AvaloniaChaosSfx.Play("time_slow_out", 0.45f);
        _slowMoCueOn = false;
        if (_freezeRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active freeze
    }

    private void UpdateStateText()
    {
        if (_state == null) return;
        _state.BestCombo = Math.Max(_state.BestCombo, _state.Combo);
        // ComboMult/HeatMult are computed on the state now (WPF ChaosModels.cs:525-527);
        // the HUD strip shows the full WPF stack (WPF ChaosModels.cs:531 TotalMultText).
        _state.TotalMultText = $"x{_state.TotalMult:0.0}";
        _state.ScoreText = ((int)_state.Score).ToString("N0");
        _state.ShieldText = $"{_state.Shields} ♥";
        _state.FocusText = _state.IsChanneling
            ? $"HOLD {_state.ChannelHeldSec:0.0}s / {(int)_state.Focus} / {(int)_state.FocusMax}"
            : $"{(int)_state.Focus} / {(int)_state.FocusMax}";
        _state.ChannelText = _state.IsChanneling
            ? $"channeling… {_state.ChannelHeldSec:0.0}s"
            : "";
        // Focus reads low under one snap's price (WPF: FocusLow judges Focus against
        // ChaosTuning.DEFUSE_COST — both 30).
        _state.FocusLow = _state.Focus < CoreChaosTuning.DEFUSE_COST;
        _state.RaiseChanged(nameof(ChaosRunState.FocusText));
        _state.RaiseChanged(nameof(ChaosRunState.ChannelText));
        _state.RaiseChanged(nameof(ChaosRunState.FocusLow));

        var remaining = Math.Max(0, _state.RunDurationSec - _state.ElapsedSec);
        _state.ClockText = $"{(int)remaining / 60}:{(int)remaining % 60:00}";
        _state.RunTimeText = $"{(int)_state.ElapsedSec / 60}:{(int)_state.ElapsedSec % 60:00}";
        _state.ActWaveText = $"I · {_waveIndex}";
        // RunProgress is computed from ElapsedSec/RunDurationSec on the state now (WPF ChaosModels.cs:485).

        // Fall speed tracks the pop streak (mirrors WPF ChaosModeService mid-run SetStreak).
        try { _tunnel?.SetStreak(_state.Combo, _state.ComboMult); } catch (Exception ex) { _logger?.LogDebug("ChaosTunnel SetStreak failed: {E}", ex.Message); }
    }

    /// <summary>Writes every live-knob value from the run state into the engine's
    /// <see cref="ChaosRunKnobs"/> — the port's equivalent of the live lambdas WPF passes into
    /// BeginChaosMode (WPF ChaosModeService.cs:363-386, formulas verbatim). Called at run start
    /// and after every state mutation any lambda read (boon picks, lifetime boons, toys), so
    /// owned upgrades and drafted boons take effect mid-run.</summary>
    private void SyncKnobsFromState()
    {
        var knobs = _bubbles.ChaosKnobs;
        var s = _state;
        if (knobs == null || s == null) return;

        // chainReach: () => _state?.ChainReactionReach ?? 0 (WPF :367) — a box-MULTIPLE, <=1 = off
        // (WPF BubbleService.cs:1610-1611 `if (reachMult <= 1.0) return`). The engine's chain knob
        // is a centre-distance DIP radius, so the multiple maps onto the engine's 120-DIP base
        // reach; <=1 turns chaining OFF, matching WPF's no-boon default.
        // (plan: chaos-run-engine-port-plan.md S4b-4) the engine's ChainPop still differs from
        // WPF ChainPopNeighbors (pickup-only targets, no 80ms hop stagger, benign-pop trigger
        // only) — reported as a follow-up row; this sync only makes the reach LIVE.
        knobs.ChainReachDip = s.ChainReactionReach <= 1.0 ? 0.0 : 120.0 * s.ChainReactionReach;
        knobs.HitboxScale = s.Config?.HitboxScale ?? 1.0;                     // WPF :368
        knobs.BubbleOpacity = s.BlindfoldActive ? s.BlindfoldOpacity : 1.0;   // WPF :369
        // wandShimmer: () => false — magic_wand retired 2026-06-10, not ported (WPF :370).
        knobs.CursorPull = s.CursorPullStrength - s.CamGirlFlee;              // WPF :371 (Cam Girl flees; The Pull fights back)
        knobs.RabbitHoming = s.CursorPullStrength > 0;                        // WPF :372
        knobs.SpankerOn = s.SpankerActive;                                    // WPF :373
        knobs.SpankGrow = Math.Max(1.0, s.SpankGrowFactor);                   // WPF :374, per-tick clamp WPF BubbleService.cs:489
        knobs.LiveMagnet = s.MagnetEnabled;                                   // WPF :375
        knobs.RabbitTrailSec = Math.Max(0.0, s.RabbitTrailSec);               // WPF :378, per-tick clamp WPF BubbleService.cs:490
        knobs.ElectrifiedRabbits = s.ElectrifiedRabbits;                      // WPF :379
    }

    private void ShowDraft()
    {
        if (_overlay == null || _state == null || _ending) return;
        _paused = true;
        _bubbles.SetChaosFrozen(true);
        _bubbles.SetChaosInputLocked(true);
        AvaloniaChaosSfx.PlayWaveClear();   // the field pops as the draft table comes out (WPF ChaosModeService.cs:1469)

        ChaosNarrativeHooks.OnBoonDraft(_waveIndex, BuildNarrativeContext(depth: _waveIndex));

        var options = PickDraftOptions(_state.Config.AllowCurses);
        ChaosHappyPath.RigDraft(options, _state);
        RunOnUi(() => _overlay?.ShowBoonDraft(_waveIndex, options, OnBoonPicked, autoResumeSec: _state.Config.DraftAutoResumeSec));
    }

    /// <summary>Internal hook for ChaosHappyPath's scripted mid-run draft (run 1).</summary>
    internal bool TriggerScriptedDraft(List<ChaosBoon> options)
    {
        if (_overlay == null || _state == null || !_spawning || _paused || _manualPaused || _ending) return false;
        if (options.Count == 0) return false;
        _paused = true;
        _bubbles.SetChaosFrozen(true);
        _bubbles.SetChaosInputLocked(true);
        AvaloniaChaosSfx.PlayWaveClear();   // WPF ChaosModeService.cs:1503
        _scriptedDraftPending = true;
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        RunOnUi(() => _overlay?.ShowBoonDraft(_waveIndex, options, OnBoonPicked, autoResumeSec: _state.Config.DraftAutoResumeSec));
        return true;
    }

    private List<ChaosBoon> PickDraftOptions(bool allowCurses)
    {
        var pool = ChaosBoonPool.All.ToList();
        if (!allowCurses) pool = pool.Where(b => !b.IsCurse).ToList();
        if (pool.Count == 0) pool = ChaosBoonPool.All.ToList();
        var picked = new List<ChaosBoon>();
        for (int i = 0; i < 3 && pool.Count > 0; i++)
        {
            int idx = _rng.Next(pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return picked;
    }

    private void OnBoonPicked(ChaosBoon? boon)
    {
        if (_state == null || !_active) return;
        bool scripted = _scriptedDraftPending;
        _scriptedDraftPending = false;

        if (boon != null)
        {
            bool shielded = ChaosHappyPath.ShouldShieldSin(boon.Id);
            // ApplyBoon pushes the pick tile + "☠/◈ {Name}" feed line itself now (WPF ChaosModels.cs:604-620).
            _state.ApplyBoon(boon, shielded);
            // Push the pick's state mutations into the engine's live knobs — the WPF lambdas saw
            // them on the next frame automatically (WPF ChaosModeService.cs:363-386); the port
            // syncs explicitly after every mutation (S4b-4).
            SyncKnobsFromState();
            ChaosBoonBarOverlay.SetPicks(_state.RunPickTiles);   // ribbon reflects the new pick (WPF ChaosModeService.cs:417)
            ChaosLessonHooks.OnDraftCardTaken(boon.IsCurse);
            if (boon.IsCurse)
            {
                AvaloniaChaosSfx.Play("sin_accept", 0.6f);   // WPF ChaosModeService.cs:1569
                ChaosNarrativeHooks.OnSinAccepted(boon.Id, BuildNarrativeContext(depth: _waveIndex));
                if (shielded) ChaosHappyPath.OnSinAccepted();
            }
        }
        ChaosHappyPath.OnDraftResolved();

        if (scripted)
        {
            _paused = false;
            _bubbles.SetChaosInputLocked(false);
            _bubbles.SetChaosFrozen(false);
            // Welcome Shower: every resume-GO dumps a quick rain of treats from the top
            // (WPF ResumeAfterDraft, ChaosModeService.cs:1621-1623 — the scripted draft
            // resumes through the same path in WPF).
            if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();
            UpdateStateText();
            return;
        }

        _waveIndex++;
        _state.WaveIndex = _waveIndex;
        ShowWaveConversation(_waveIndex);

        _paused = false;
        _bubbles.SetChaosInputLocked(false);
        _bubbles.SetChaosFrozen(false);
        // Welcome Shower: every loop's GO! dumps a quick rain of treats from the top
        // (WPF ResumeAfterDraft, ChaosModeService.cs:1621-1623).
        if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();
        UpdateStateText();
    }

    private void EndRun(bool ranFullCourse = false)
    {
        if (!_active || _ending) return;
        _ending = true;
        _spawning = false;
        try { _crashSentinel?.Clear(); } catch { }               // the field is coming down (WPF ChaosModeService.cs:3126)
        try { ChaosBoonBarOverlay.CloseActive(); } catch { }     // WPF ChaosModeService.cs:3153
        StopTimers();
        StopKeyHook();
        StopRippleHook();
        DisarmRabbitCall();
        CloseToyButtons();
        _bubbles.EndChaosMode();

        var state = _state;
        if (state != null)
        {
            LastRunScore = state.Score;
            // Her gold tip for the final loop — full-course descents only (WPF EndRun AwardLoopTip).
            if (ranFullCourse) AwardLoopTip(state);
            ChaosLessonHooks.OnRunCompleted(state.Shields, ranFullCourse, state.Config.Difficulty);
            ChaosNarrativeHooks.OnRunEnded(BuildNarrativeContext(depth: _waveIndex), state.Score, ranFullCourse);

            double baseXp = Math.Sqrt(Math.Max(0, state.Score)) * 1.5 + state.RunDurationSec / 60.0 * 35.0 * state.DifficultyMult;
            // Skill-tree XP multiplier for the recap (WPF ChaosModeService.cs:3166 / ChaosModels.cs:535).
            // Sparks stay on baseXp: IProgressionService.AddXP re-applies the multiplier internally,
            // so the recap's finalXp reflects the skill tree without double-counting the payout.
            double skillMult = _skillTree?.GetTotalXpMultiplier() ?? 1.0;
            double finalXp = baseXp * skillMult;
            int sparks = (int)Math.Round(baseXp);
            long previousBest = (long)ChaosMeta.State.BestScore;

            try { _progression.AddXP(sparks, XPSource.Chaos); }
            catch (Exception ex) { _logger?.LogDebug("Chaos payout AddXP: {E}", ex.Message); }

            ChaosMeta.State.Sparks += Math.Max(0, sparks);
            ChaosMeta.State.RunsCompleted++;
            ChaosMeta.State.BestScore = Math.Max(ChaosMeta.State.BestScore, (long)state.Score);
            ChaosMeta.State.BestCombo = Math.Max(ChaosMeta.State.BestCombo, state.BestCombo);
            ChaosMeta.State.TotalDefused += state.Defused;
            ChaosMeta.State.TotalRunSeconds += state.ElapsedSec;
            ChaosMeta.Save();
            RevealService.Sync("run_complete");
            ChaosHappyPath.OnRunResultsShown(state, baseXp, skillMult, finalXp, previousBest, sparks);

            RunOnUi(() =>
            {
                _hud?.Close();
                _hud = null;
                _overlay?.ShowResults(state, baseXp, skillMult, finalXp, previousBest, sparks);
            });
        }

        // The run lifecycle is complete; the results overlay may stay visible until the
        // user dismisses it. Report IsRunning=false so callers (and the smoke test) don't
        // wait forever for a window that needs user input to close.
        _active = false;

        _logger?.LogInformation("AvaloniaChaosService run ended");
    }

    private void CleanupAfterRun()
    {
        _logger?.LogDebug("CleanupAfterRun called");
        try { _crashSentinel?.Clear(); } catch { }   // every run teardown funnels here (WPF ChaosModeService.cs:3229)
        try { _tunnel?.CloseActive(); } catch { }
        ChaosHappyPath.OnRunEnded();
        _ending = false;
        _active = false;
        _spawning = false;
        _paused = false;
        _manualPaused = false;
        StopTimers();
        StopKeyHook();
        StopRippleHook();
        DisarmRabbitCall();
        CloseToyButtons();
        try { _bubbles.EndChaosMode(); } catch { }
        RunOnUi(() =>
        {
            try { _hud?.Close(); } catch { }
            _hud = null;
            try { _overlay?.Close(); } catch { }
            _overlay = null;
            try { ChaosBoonBarOverlay.CloseActive(); } catch { }   // WPF ChaosModeService.cs:3239
            try { ChaosBackdropService.CloseActive(); } catch { }
        });
        // WPF parity: run teardown closes the effect-banner strip instantly (the WPF
        // CloseActive path — the legacy Avalonia head never called it only because banner
        // Show was never wired). Pop text likewise dies with the run (WPF ShutdownPool),
        // and the announcer drops its queue + visible line (WPF ChaosModeService.cs:3099/
        // 3148 CloseActive — the legacy head skipped this teardown entirely, a drift).
        CloseEffectBanners();
        ClearChaosPopText();
        ClearAnnouncements();
        // WPF parity (ChaosModeService.cs:3097/3146): the braindrain wash dies with the run.
        // The legacy Avalonia head never tore it down (its window just outlived the run) — a drift.
        ClearChaosFlashWash();
        // WPF parity (ChaosModeService.cs:3101/3150/3236): every DVD logo dies with the run.
        // The legacy Avalonia head never called the DVD CloseActive either — same drift class.
        ClearChaosDvdLogos();
        // WPF parity (ChaosModeService.cs:3098/3147): the gif cascade dies with the run too
        // (the legacy head also skipped this CloseActive — same drift class).
        ClearChaosGifCascade();
        // WPF parity (ChaosModeService.cs:3108/3157/3243): field FX die with the run.
        ClearChaosFieldFx();
        _state = null;
        _vibeRemainingSec = 0;
        _freezeRemainingSec = 0;
        _snapFlashRemainingSec = 0;
        _rippleCooldownSec = 0;
        // Spawn-director transients die with the run (WPF ChaosModeService.cs:399-402 resets
        // these at the NEXT BeginRun; clearing here too keeps a torn-down service inert).
        _slowMoRemainingSec = 0;
        _slowMoCueOn = false;
        _pendulumSlowActive = false;
        _heavyUntilUtc = DateTime.MinValue;
        _heartArmedThisWave = false;
        EffectPayload.GlobalDurationMult = 1.0;   // a mid-run teardown can't leave payloads stretched (WPF EndSlowMo :2393)
        AvaloniaChaosMode.ActiveMode = ChaosPlayMode.Story;
        // WPF parity (ChaosModeService.cs:3266-3270): reset the pin after the run so
        // dashboard trigger overlays never inherit a stale demote.
        AvaloniaChaosMode.PinTopmost = true;
    }

    private void OnOverlayClosed()
    {
        CleanupAfterRun();
        AvaloniaChaosApp.Avatar?.SetChaosRunActive(false);
    }

    private void KeepChromeTopmost()
    {
        if (!_spawning) return;
        RunOnUi(() =>
        {
            try { _hud?.RaiseToTopmost(); } catch { }
            try { ChaosBoonBarOverlay.RaiseActive(); } catch { }
            // Effect banner is a compositor layer: z comes from CompositorLayers (UCE rule 9).
        });
    }

    private void RaiseGameLayerAboveVideo()
    {
        if (!_spawning) return;
        RunOnUi(() =>
        {
            // Field FX, pop text, effect banner and announcer are compositor layers: z comes
            // from CompositorLayers (UCE rule 9), so no RaiseActive churn is needed for them.
            try { _hud?.RaiseToTopmost(); } catch { }
        });
    }

    // ============================ active toys ============================

    private void BuildActiveToys()
    {
        if (_state == null) return;
        _state.ActiveToys.Clear();
        int pockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
        if (pockets <= 0) return;
        var settings = _settings.Current;
        string[] keys =
        {
            settings?.ChaosAccessoryKey1 ?? "Q",
            settings?.ChaosAccessoryKey2 ?? "E",
            "R",
            "F"
        };
        int slot = 0;
        foreach (var b in ChaosLifetimeBoons.All)
        {
            if (slot >= pockets) break;
            if (!b.IsActiveUse || !ChaosMeta.IsBoonActive(b.Id)) continue;
            if (!_state.ToyPower.TryGetValue(b.Id, out var power)) continue;
            var toy = new ChaosToyState
            {
                Id = b.Id,
                Name = b.Name,
                Glyph = b.Glyph,
                Desc = b.Desc,
                Flavor = b.Flavor,
                CapstoneDesc = b.CapstoneDesc,
                KeyLabel = slot < keys.Length ? keys[slot] : "",
                CooldownSec = b.UseCooldownSec,
            };
            if (b.UseCooldownSec <= 0) toy.ChargesLeft = (int)power; // charge-based (Freeze Trigger)
            _state.ActiveToys.Add(toy);
            slot++;
        }
    }

    private void CloseToyButtons()
    {
        RunOnUi(() =>
        {
            foreach (var b in _toyButtons.ToArray())
                try { b.Close(); } catch { }
            _toyButtons.Clear();
        });
    }

    private void StartKeyHook()
    {
        if (_inputHook == null) return;
        try
        {
            _inputHook.KeyPressed += OnToyKey;
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Chaos toy key hook failed"); }
    }

    private void StopKeyHook()
    {
        try
        {
            if (_inputHook != null) _inputHook.KeyPressed -= OnToyKey;
        }
        catch { }
    }

    private void OnToyKey(object? sender, KeyboardHookEventArgs e)
    {
        RunOnUi(() =>
        {
            var settings = _settings.Current;
            string name = VirtualKeyToName(e.VirtualKeyCode);

            // Panic key outranks toys.
            if (settings?.PanicKeyEnabled == true &&
                name.Equals(settings.PanicKey, StringComparison.OrdinalIgnoreCase))
            {
                OnPanicKeyDuringRun();
                return;
            }

            if (!_spawning || _state == null || _paused || _manualPaused) return;
            foreach (var toy in _state.ActiveToys)
            {
                if (!string.IsNullOrEmpty(toy.KeyLabel) &&
                    toy.KeyLabel.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    UseToyById(toy.Id);
                    break;
                }
            }
        });
    }

    private static string VirtualKeyToName(int vkCode)
    {
        if (vkCode is >= 0x30 and <= 0x39) return ((char)('0' + (vkCode - 0x30))).ToString();
        if (vkCode is >= 0x41 and <= 0x5A) return ((char)vkCode).ToString();
        return vkCode switch
        {
            0x20 => "Space",
            0x1B => "Escape",
            0x0D => "Return",
            0x09 => "Tab",
            0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
            0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
            0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            _ => $"VK{vkCode}",
        };
    }

    private void OnPanicKeyDuringRun()
    {
        if (!_spawning || _paused) return;
        if (!_manualPaused) ToggleManualPause();
        else RequestStop();
    }

    // Strict Install/Uninstall pairing: the shared IMouseHook is reference-counted across
    // consumers (flash clicks, bubble pops, this ripple hook). EndRun and CleanupAfterRun
    // both call StopRippleHook on a normal teardown, so without this flag the second call
    // would release a hook share owned by another consumer (uninstall-collision class).
    private bool _rippleHookInstalled;

    private void StartRippleHook()
    {
        if (_mouseHook == null || _rippleHookInstalled) return;
        try
        {
            _mouseHook.RightButtonUp += OnRippleRightUp;
            _mouseHook.Install();
            _rippleHookInstalled = true;
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "Chaos ripple hook failed"); }
    }

    private void StopRippleHook()
    {
        if (!_rippleHookInstalled) return;
        _rippleHookInstalled = false;
        try
        {
            if (_mouseHook != null) _mouseHook.RightButtonUp -= OnRippleRightUp;
        }
        catch { }
        try { _mouseHook?.Uninstall(); } catch { }
    }

    private void OnRippleRightUp(object? sender, Core.Platform.HookPoint e)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;
        // Without bubble-center access we fire whenever any chaos bubble is alive,
        // letting right-clicks pass through to the desktop when the field is empty.
        if (_bubbles.ActiveBubbles == 0) return;
        RunOnUi(() => FireRipple(new Core.Platform.Point(e.X, e.Y)));
    }

    private void FireRipple(Core.Platform.Point px)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;
        if (_freezeRemainingSec > 0) return;
        ChaosLessonHooks.OnRippleCast();
        if (!_state.RippleReady)
        {
            _state.PushEvent($"🌊 still water... gathering {_state.RippleText}");
            return;
        }
        _rippleCooldownSec = _state.RippleRechargeSec;
        bool skips = _state.MaxedBoons.Contains("skipping_stone");
        CastRippleWave(px);
        if (skips)
        {
            for (int i = 1; i <= 2; i++)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(i * CoreChaosTuning.RIPPLE_WAVE_GAP_MS) };
                int local = i;
                t.Tick += (_, _) => { t.Stop(); CastRippleWave(px); };
                t.Start();
            }
        }
        _state.PushEvent(skips ? "🌊 the stone skips — three waves" : "🌊 ripple");
    }

    private void CastRippleWave(Core.Platform.Point px)
    {
        if (!_spawning || _state == null) return;
        _bubbles.TriggerPlayerRipple(px, _state.RippleRadiusPx, _state.RippleLifeMs);
    }

    public void UseToyById(string id)
    {
        if (_state == null) return;
        foreach (var t in _state.ActiveToys)
            if (t.Id == id) { UseToy(t); return; }
    }

    private void UseToy(ChaosToyState toy)
    {
        if (_state == null || !_spawning || _paused || _manualPaused) return;
        if (_state.ActivesDisabled)
        {
            _state.PushEvent("🫦 the urge holds your hands — no toys");
            AvaloniaChaosSfx.Play("toy_denied", 0.45f);   // WPF ChaosModeService.cs:2522
            return;
        }
        if (!toy.IsReady) { AvaloniaChaosSfx.Play("toy_denied", 0.45f); return; }
        ChaosLessonHooks.OnToyUsed(toy.Id);
        double power = _state.ToyPower.TryGetValue(toy.Id, out var p) ? p : 0;
        bool maxed = _state.MaxedBoons.Contains(toy.Id);

        switch (toy.Id)
        {
            case "vibe_popping":
                _bubbles.SetVibePop(true, hoverPops: maxed);
                _vibeRemainingSec = Math.Max(1, power);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                _state.PushEvent("🔸 it buzzes. hold and sweep");
                break;

            case "freeze_trigger":
                if (toy.ChargesLeft <= 0) return;
                toy.ChargesLeft--;
                ActivateFreeze();
                if (maxed) _bubbles.DefuseAllLive();
                toy.CooldownRemainingSec = 3; // anti-doubletap between charges
                toy.IsEffectActive = true;
                _state.PushEvent("❄ everything holds still");
                break;

            case "porn_dvd":
                int lvl = ChaosMeta.BoonLevel(toy.Id);
                double speed = lvl switch { 1 => 0.7, 2 => 0.85, _ => 1.0 };
                double scale = lvl switch { 1 => 0.8, 2 => 0.9, _ => 1.0 };
                LaunchChaosDvd(Math.Max(5, power), speed, scale, count: maxed ? 2 : 1,
                    splitBounces: _state.DvdSplitBounces);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                // WPF parity (ChaosModeService.cs:2657): the port kept the _dvdBannerOn flag
                // but dropped the banner call — restored through the compositor seam.
                ShowEffectBanner("dvd", "Porn DVD", global::Avalonia.Media.Color.FromRgb(0xFF, 0x69, 0xB4));
                _dvdBannerOn = true;
                _state.PushEvent("📀 now loading");
                break;

            case "snap_field":
                if (maxed) _bubbles.PopAllChaosPaid();
                else _bubbles.DefuseAllLive();
                toy.CooldownRemainingSec = Math.Max(5, power);
                _snapFlashRemainingSec = 1.0;
                toy.IsEffectActive = true;
                _state.PushEvent(maxed ? "✋ snapped. all of it." : "✋ snapped — every live one let go");
                break;

            case "rabbit_caller":
                ArmRabbitCall(Math.Max(1, (int)power), maxed);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                _state.PushEvent("🐇 the whistle hangs — your next click calls them");
                break;

            case "e_stim":
                int charges = Math.Max(1, (int)power) * Math.Max(1, _state.EStimChargeMult);
                _bubbles.ArmEStim(charges, maxed);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                _state.PushEvent(maxed
                    ? $"⚡ charged — your next {charges} pops chain-react"
                    : $"⚡ charged — your next {charges} pops conduct");
                break;
        }
    }

    private void ActivateFreeze()
    {
        _freezeRemainingSec = FREEZE_DURATION_SEC;
        _bubbles.SetChaosFrozen(true);
        _bubbles.VibrateAllForFreeze(FREEZE_VIBRATE_MS);
    }

    private void EndFreeze()
    {
        _freezeRemainingSec = 0;
        _bubbles.SetChaosFrozen(false);
    }

    private void ArmRabbitCall(int rabbits, bool maxed)
    {
        _rabbitCallPending = rabbits;
        _rabbitCallMaxed = maxed;
        _rabbitAimPrevDown = true; // swallow the press that armed the toy
        ArmCursorGlow();
        if (_rabbitAimTimer == null)
        {
            _rabbitAimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _rabbitAimTimer.Tick += RabbitAimTick;
        }
        _rabbitAimTimer.Start();
    }

    private void DisarmRabbitCall()
    {
        _rabbitCallPending = 0;
        _rabbitAimTimer?.Stop();
        DisarmCursorGlow();
    }

    private void RabbitAimTick(object? sender, EventArgs e)
    {
        try
        {
            if (_rabbitCallPending <= 0 || _state == null || !_active)
            {
                DisarmRabbitCall();
                return;
            }
            var cur = _pointerState?.GetCursorPosition();
            if (!cur.HasValue) return;
            MoveCursorGlow(cur.Value.X, cur.Value.Y);

            bool down = _pointerState?.IsMouseButtonPressed(Core.Platform.MouseButton.Left) ?? false;
            bool pressed = down && !_rabbitAimPrevDown;
            _rabbitAimPrevDown = down;
            if (!pressed || _paused || _manualPaused || !_spawning) return;

            int rabbits = _rabbitCallPending;
            bool maxed = _rabbitCallMaxed;
            DisarmRabbitCall();
            for (int i = 0; i < rabbits; i++)
            {
                double jx = cur.Value.X + _rng.Next(-60, 61);
                double jy = cur.Value.Y + _rng.Next(-60, 61);
                SpawnDarter(jx, jy);
            }
            _state.PushEvent(maxed ? $"🐇 {rabbits} at your fingertip… and the burrow is emptying"
                                   : $"🐇 {rabbits} answered at your fingertip");
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "RabbitAimTick failed"); }
    }

    /// <summary>Cursor-glow telegraph mutators (WS2: chaos on the compositor). Public because
    /// the --verify-layers harness drives the layer through its OWNING service (services own
    /// state; layers only render) — the same calls the Rabbit Caller aim loop makes. The WPF
    /// Arm-time RaiseAboveVideo has no layer equivalent: z-order comes from CompositorLayers
    /// only (UCE rule 9; the chaos band sits above the video layers by constant).</summary>
    public void ArmCursorGlow() => _cursorGlowLayer.Arm();

    /// <summary>Hide the cursor-glow telegraph.</summary>
    public void DisarmCursorGlow() => _cursorGlowLayer.Disarm();

    /// <summary>Center the telegraph on raw PHYSICAL virtual-desktop px (IPointerState space).</summary>
    public void MoveCursorGlow(double pxX, double pxY) => _cursorGlowLayer.MoveTo(pxX, pxY);

    /// <summary>Pop a floating chaos word at a PHYSICAL virtual-desktop px anchor (WS2:
    /// chaos on the compositor). Master-gated on ChaosAnnouncerEnabled exactly like WPF
    /// ChaosPopText.Show (one toggle for all on-screen Chaos text). Public because the
    /// --verify-layers harness drives the layer through its OWNING service; production
    /// callers arrive with the run-engine bubble-effect port (the WPF call sites live in
    /// ChaosModeService/BubbleService paths not yet ported). Wakes the engine same-tick
    /// (documented CompositorEngine.Start contract) so the 490ms one-shot never loses its
    /// first frames to the idle watchdog.</summary>
    public void ShowChaosPopText(double pxX, double pxY, string text, global::Avalonia.Media.Color tint)
    {
        if (_settings.Current?.ChaosAnnouncerEnabled != true) return;
        _compositor?.Start();
        _popTextLayer.Spawn(pxX, pxY, text, tint);
    }

    /// <summary>Drop every live pop-text floater (WPF ShutdownPool at chaos teardown).</summary>
    public void ClearChaosPopText() => _popTextLayer.Clear();

    /// <summary>Show the chaos "braindrain" wash: one random flash-pool image held over the
    /// stage at a low opacity, fading in/out (WS2: chaos on the compositor; WPF
    /// ChaosFlashOverlay.Show contract — defaults ~10%/10s, silent no-op on an empty pool,
    /// NO settings gate). The service owns policy: image pick, stage rect (effect-screen
    /// union — the WPF dual-aware StageBounds; the legacy Avalonia window forced primary,
    /// a drift), and the off-thread decode-once (SkiaImageDecoder budgets in the layer doc);
    /// the layer renders final inputs. Public because the --verify-layers harness drives the
    /// layer through its OWNING service — the same call the braindrain effect payload makes.
    /// The WPF Show-time RaiseAboveVideo/ForceTopmost churn has no layer equivalent (UCE rule 9).</summary>
    public void ShowChaosFlashWash(int durationMs = 10000, double opacity = 0.10)
    {
        try
        {
            var files = ChaosImagePool.GetFiles(AvaloniaChaosEnv.EffectiveAssetsPath ?? "");
            if (files.Count == 0) return;   // silent no-op (WPF PickImage)
            var path = files[Random.Shared.Next(files.Count)];

            var stage = ComputeEffectStagePx();
            if (stage.IsEmpty) return;

            _compositor?.Start();   // wake same-tick so the 500ms fade-in loses no frames
            int gen = Interlocked.Increment(ref _washGen);
            Task.Run(() =>
            {
                try
                {
                    // Budgets: see ChaosFlashWashLayer class doc (WPF still cap 2560; the WPF
                    // animated-webp wash budget 1280/40 for all animated content + spiral 96MB cap).
                    bool animated = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                                 || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
                    var set = animated
                        ? SkiaImageDecoder.Decode(path, maxFrames: 40, decodeMaxDim: 1280, maxMemoryMb: 96.0, defaultFrameDelayMs: 100, maxFrameDelayMs: 0)
                        : SkiaImageDecoder.Decode(path, maxFrames: 1, decodeMaxDim: 2560, maxMemoryMb: 0, defaultFrameDelayMs: 100, maxFrameDelayMs: 0);
                    if (set == null) return;
                    // A newer wash or a clear/teardown superseded this decode (WPF _displayGen).
                    if (gen != Volatile.Read(ref _washGen)) { set.Release(); return; }
                    _flashWashLayer.ShowWash(set, stage, durationMs, opacity);
                }
                catch (Exception ex) { _logger?.LogDebug("ChaosFlashWash decode: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosFlashWash.Show: {E}", ex.Message); }
    }

    /// <summary>The chaos stage in PHYSICAL px: the union of the same screen set the engine
    /// composites (primary unless dual — the WPF dual-aware ChaosWindowZ.StageBounds).</summary>
    private ConditioningControlPanel.Core.Platform.PixelRect ComputeEffectStagePx()
    {
        var dual = _settings.Current?.DualMonitorEnabled != false;
        var screens = _screenProvider?.GetEffectScreens(dual);
        if (screens == null || screens.Count == 0) return ConditioningControlPanel.Core.Platform.PixelRect.Empty;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var s in screens)
        {
            x0 = Math.Min(x0, s.Bounds.X);
            y0 = Math.Min(y0, s.Bounds.Y);
            x1 = Math.Max(x1, s.Bounds.Right);
            y1 = Math.Max(y1, s.Bounds.Bottom);
        }
        return new ConditioningControlPanel.Core.Platform.PixelRect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Instant wash teardown (run end — WPF ChaosFlashOverlay.CloseActive).</summary>
    public void ClearChaosFlashWash()
    {
        Interlocked.Increment(ref _washGen);   // orphan any in-flight decode
        _flashWashLayer.Clear();
    }

    /// <summary>Launch bouncing DVD logos (WS2: chaos on the compositor; WPF
    /// ChaosDvdOverlay.Launch contract — the porn_dvd toy and the Intrusive Thoughts
    /// accessory share it). The service owns policy: the primary work-area anchor + DPI
    /// scale are captured per launch (the flight is confined to the primary work area,
    /// WPF SystemParameters.WorkArea); physics/splits live in the layer's Update, side
    /// effects come back through the delegates wired in the ctor. Public because the
    /// --verify-layers harness drives the layer through its OWNING service. NO settings
    /// gate (WPF Launch has none). The WPF Spanker smack-to-turn path is dead in this head
    /// (SpankerRedirect never assigned) — the layer is purely passive until that port.</summary>
    public void LaunchChaosDvd(double durationSec, double speedMult, double scale, int count = 1,
        string? text = null, bool splitOnRabbit = false, int splitBounces = 0)
    {
        try
        {
            var primary = _screenProvider?.GetPrimaryScreen();
            if (primary == null) return;   // no screen info — nowhere to fly
            _compositor?.Start();   // wake same-tick so the 180ms fade-in loses no frames
            _dvdLayer.Launch(durationSec, speedMult, scale, count, text, splitOnRabbit, splitBounces,
                primary.WorkingArea.IsEmpty ? primary.Bounds : primary.WorkingArea, primary.Scaling);
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosDvd.Launch: {E}", ex.Message); }
    }

    /// <summary>True while a TOY-launched logo flies (WPF ChaosDvdOverlay.AnyToyActive —
    /// the porn_dvd banner interplay and the toy active flag read this).</summary>
    public bool ChaosDvdToyActive => _dvdLayer.AnyToyActive;

    /// <summary>Instant DVD teardown (run end — WPF ChaosDvdOverlay.CloseActive).</summary>
    public void ClearChaosDvdLogos() => _dvdLayer.Clear();

    /// <summary>Start (or restart) a falling gif cascade (WS2: chaos on the compositor; WPF
    /// ChaosGifCascadeOverlay.Show contract — all knobs come from the payload's consts; a
    /// re-Show replaces the in-flight clips; empty pool = silent no-op; NO settings gate).
    /// The service owns policy: pool pick, dual-aware stage union (the legacy window forced
    /// primary — a drift vs WPF StageBounds) and the primary DPI scale; the layer owns
    /// spawn cadence, decode-once and fall physics. Public because the --verify-layers
    /// harness drives the layer through its OWNING service — the same call the gif-cascade
    /// effect payload makes.</summary>
    public void ShowChaosGifCascade(double spawnRatePerSec, double durationSec, double gifSize,
        double fallSpeed, double opacity, double startScale = 1.0)
    {
        try
        {
            var files = ChaosImagePool.GetFiles(AvaloniaChaosEnv.EffectiveAssetsPath ?? "");
            if (files.Count == 0) return;   // silent no-op (WPF PickFiles)
            var stage = ComputeEffectStagePx();
            var primary = _screenProvider?.GetPrimaryScreen();
            if (stage.IsEmpty || primary == null) return;
            _compositor?.Start();   // wake same-tick so the first clip loses no frames
            _gifCascadeLayer.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed,
                opacity, startScale, stage, primary.Scaling);
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosGifCascade.Show: {E}", ex.Message); }
    }

    /// <summary>True while a cascade is in flight (WPF IsRaining — the WPF chaos heavy gate
    /// and VideoService read it; no Avalonia consumer exists yet, exposed for that port).</summary>
    public bool IsGifCascadeRaining => _gifCascadeLayer.IsActive;

    /// <summary>Instant cascade teardown (run end — WPF ChaosGifCascadeOverlay.CloseActive).</summary>
    public void ClearChaosGifCascade() => _gifCascadeLayer.Clear();

    // ---- Field FX seams (WS2: chaos on the compositor; WPF ChaosFieldFxOverlay statics).
    // All coordinates/radii in PHYSICAL virtual-desktop px (the layer's native space — the
    // WPF seams were px too; its window converted px→DIP internally). Public because the
    // --verify-layers harness drives the layer through its OWNING service; production
    // callers arrive with the bubble-engine FX port (WPF BubbleService shockwaves / field
    // hazards / rabbit trails / The Bound — none of those paths exist in this head yet).

    /// <summary>Size Queen: one expanding pop-ring (WPF ChaosFieldFxOverlay.Ripple).</summary>
    public void ChaosFieldRipple(double cxPx, double cyPx, double radiusPx, double lifeMs)
    { _compositor?.Start(); _fieldFxLayer.Ripple(cxPx, cyPx, radiusPx, lifeMs); }

    /// <summary>The Ripple cast: linear kill-front + echo + shards (WPF SnapRipple).</summary>
    public void ChaosFieldSnapRipple(double cxPx, double cyPx, double radiusPx, double lifeMs)
    { _compositor?.Start(); _fieldFxLayer.SnapRipple(cxPx, cyPx, radiusPx, lifeMs); }

    /// <summary>Aftermath: one crackling residue zone (WPF Residue).</summary>
    public void ChaosFieldResidue(double cxPx, double cyPx, double radiusPx, double lifeMs)
    { _compositor?.Start(); _fieldFxLayer.Residue(cxPx, cyPx, radiusPx, lifeMs); }

    /// <summary>Rabbit sparkle trail dot; warm = the amber GG-sweeper variant (WPF TrailDot).</summary>
    public void ChaosFieldTrailDot(double cxPx, double cyPx, double lifeSec, bool warm = false)
    { _compositor?.Start(); _fieldFxLayer.TrailDot(cxPx, cyPx, lifeSec, warm); }

    /// <summary>The Bound: create/update a pair's elastic thread (WPF SetTether).</summary>
    public void ChaosFieldSetTether(int key, double axPx, double ayPx, double bxPx, double byPx)
    { _compositor?.Start(); _fieldFxLayer.SetTether(key, axPx, ayPx, bxPx, byPx); }

    /// <summary>The Bound: drop a pair's thread (WPF ClearTether).</summary>
    public void ChaosFieldClearTether(int key) => _fieldFxLayer.ClearTether(key);

    /// <summary>Instant field-FX teardown (run end — WPF ChaosFieldFxOverlay.CloseActive).</summary>
    public void ClearChaosFieldFx() => _fieldFxLayer.Clear();

    /// <summary>Show (or keep) the effect-banner strip entry for an effect (WS2: chaos on
    /// the compositor; WPF ChaosEffectBannerOverlay.Show contract). Applies the
    /// ChaosBoonColors payload color language and resolves the announce word-art here —
    /// the service owns policy, the layer renders final inputs (UCE rule 7). NOT gated on
    /// ChaosAnnouncerEnabled (WPF banner Show has no settings gate). Public because the
    /// --verify-layers harness drives the layer through its OWNING service; production
    /// callers arrive with the run-engine effect port (the WPF call sites live in
    /// ChaosModeService paths not yet ported).</summary>
    public void ShowEffectBanner(string id, string text, global::Avalonia.Media.Color accent, string? artKey = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        accent = ChaosBoonColors.ForOrDefault(id, accent);   // payload-based color language
        var artPath = AvaloniaChaosArt.PathFor("announce", artKey ?? id);
        var primary = _screenProvider?.GetPrimaryScreen();
        if (primary == null) return; // no screen info — nowhere to anchor the strip
        _compositor?.Start();
        _effectBannerLayer.Show(id, text, accent, artPath,
            primary.WorkingArea.IsEmpty ? primary.Bounds : primary.WorkingArea, primary.Scaling);
    }

    /// <summary>Fade out + remove the banner entry for an effect (WPF End).</summary>
    public void EndEffectBanner(string id) => _effectBannerLayer.End(id);

    /// <summary>Instant strip teardown (run end — WPF CloseActive).</summary>
    public void CloseEffectBanners() => _effectBannerLayer.Clear();

    /// <summary>Queue a bordered fading announcement (WS2: chaos on the compositor; WPF
    /// ChaosAnnouncerOverlay.Announce contract). Master-gated on ChaosAnnouncerEnabled.
    /// Queue semantics are byte-equivalent to WPF: gameplay lines enqueue at priority 0,
    /// stable max-priority dequeue, per-line dwell (default 650ms, teach 3000ms), one line
    /// on screen at a time. Palette (WPF constant colors) and announce-art resolution are
    /// applied here — the layer renders final inputs.</summary>
    public void AnnounceChaos(string text, ChaosAnnounceKind kind,
                              string? artKey = null, string? subText = null, int? holdMs = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_settings.Current?.ChaosAnnouncerEnabled != true) return;
            lock (_announceSync)
            {
                _announceQueue.Add((text, kind, artKey, subText, holdMs ?? AnnounceDefaultHoldMs, 0));
                if (!_announceShowing) ShowNextAnnouncementLocked();
            }
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosAnnouncer.Announce: {E}", ex.Message); }
    }

    /// <summary>Queue a narrator (Madam) line (WPF AnnounceNarrator contract): gated on
    /// NarrativeActive (NOT the gameplay announcer toggle), priority 100 + band so she sits
    /// above gameplay announces with STORY &gt; REACTIVE; STORY passes interrupt=true, which
    /// cuts the current line short so she lands next.</summary>
    public void AnnounceChaosNarrator(string text, int bandPriority, bool interrupt, int holdMs)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!AvaloniaChaosMode.NarrativeActive) return;
            lock (_announceSync)
            {
                _announceQueue.Add((text, ChaosAnnounceKind.Narrator, null, null, holdMs, 100 + bandPriority));
                if (!_announceShowing) ShowNextAnnouncementLocked();
                else if (interrupt) _announcerLayer.CutShort();   // STORY: end the current line now
            }
        }
        catch (Exception ex) { _logger?.LogDebug("ChaosAnnouncer.AnnounceNarrator: {E}", ex.Message); }
    }

    /// <summary>Drop any queued/visible announcement (run teardown — WPF CloseActive).</summary>
    public void ClearAnnouncements()
    {
        lock (_announceSync)
        {
            _announceQueue.Clear();
            _announceShowing = false;
        }
        _announcerLayer.HideNow();   // never fires LineCompleted (WPF CloseActive parity)
    }

    /// <summary>Dequeue-and-display under _announceSync. WPF ShowNext: stable max-priority
    /// pick (first of the max wins), _showing false when the queue is empty; a failed
    /// display drops the showing flag so the chain can restart (WPF catch parity).</summary>
    private void ShowNextAnnouncementLocked()
    {
        if (!TryDequeueAnnouncement(out var item)) { _announceShowing = false; return; }
        _announceShowing = true;
        try
        {
            var artPath = item.artKey != null ? AvaloniaChaosArt.PathFor("announce", item.artKey) : null;
            var primary = _screenProvider?.GetPrimaryScreen();
            var wa = primary == null
                ? ConditioningControlPanel.Core.Platform.PixelRect.Empty
                : (primary.WorkingArea.IsEmpty ? primary.Bounds : primary.WorkingArea);
            _compositor?.Start();
            _announcerLayer.ShowLine(item.text, AnnouncePalette(item.kind), artPath, item.subText,
                item.holdMs, wa, primary?.Scaling ?? 1.0);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("ChaosAnnouncer.ShowNext: {E}", ex.Message);
            _announceShowing = false;
        }
    }

    private bool TryDequeueAnnouncement(out (string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs, int priority) item)
    {
        item = default;
        if (_announceQueue.Count == 0) return false;
        int best = 0;
        for (int i = 1; i < _announceQueue.Count; i++)
            if (_announceQueue[i].priority > _announceQueue[best].priority) best = i;   // stable: first of the max wins
        item = _announceQueue[best];
        _announceQueue.RemoveAt(best);
        return true;
    }

    /// <summary>Accent per announcement kind — the WPF ChaosAnnouncerOverlay.Palette
    /// CONSTANTS. The legacy Avalonia window substituted theme brushes for Mantra/
    /// Temptation/Depth (PinkButtonHoveredBrush/DangerBrush/TextLightBrush) — a palette
    /// drift; the WPF colors are the contract. Stroke stays near-black in the layer.</summary>
    private static global::Avalonia.Media.Color AnnouncePalette(ChaosAnnounceKind kind) => kind switch
    {
        ChaosAnnounceKind.Mantra => global::Avalonia.Media.Color.FromRgb(0xFF, 0xD2, 0x7A), // warm gold
        ChaosAnnounceKind.Temptation => global::Avalonia.Media.Color.FromRgb(0xFF, 0x6B, 0x6B), // risky red
        ChaosAnnounceKind.Willpower => global::Avalonia.Media.Color.FromRgb(0x7A, 0xE0, 0xFF), // cyan
        ChaosAnnounceKind.Depth => global::Avalonia.Media.Color.FromRgb(0xFF, 0xFF, 0xFF), // white
        ChaosAnnounceKind.Streak => global::Avalonia.Media.Color.FromRgb(0xFF, 0xC8, 0x3C), // bright gold
        ChaosAnnounceKind.Item => global::Avalonia.Media.Color.FromRgb(0x7A, 0xFF, 0xD2), // mint
        ChaosAnnounceKind.PowerUp => global::Avalonia.Media.Color.FromRgb(0x9C, 0xE8, 0xA0), // green
        ChaosAnnounceKind.Narrator => global::Avalonia.Media.Color.FromRgb(0xE6, 0x9A, 0xFF), // the Madam — soft violet
        _ => global::Avalonia.Media.Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    /// <summary>Force-spawn one white rabbit right now (Rabbit Caller; storm ticks reuse it).
    /// Optional point pins the spawn there — the summon-at-click
    /// (WPF ChaosModeService.cs:2762-2769; the stand-in passed DifficultyMult where WPF
    /// scales by RunIntensity — a drift, fixed with the S4 catalog switch).</summary>
    private void SpawnDarter(double? atPxX = null, double? atPxY = null)
    {
        if (_state == null) return;
        var spec = ChaosSpawnCatalog.BuildDarter(_state.RunIntensity, spotlight: false, sweeper: false, _rng, atPxX, atPxY);
        ChaosMeta.MarkDiscovered("bubble:darter");   // WPF :2767
        _bubbles.SpawnChaosBubble(spec);
    }

    private void TickActiveToys(double dt)
    {
        if (_state == null) return;
        if (_vibeRemainingSec > 0)
        {
            _vibeRemainingSec -= dt;
            if (_vibeRemainingSec <= 0)
            {
                _bubbles.SetVibePop(false);
            }
        }
        if (_dvdBannerOn && !ChaosDvdToyActive) { EndEffectBanner("dvd"); _dvdBannerOn = false; }   // WPF ChaosModeService.cs:2953
        foreach (var t in _state.ActiveToys)
        {
            if (t.CooldownRemainingSec > 0)
            {
                t.CooldownRemainingSec -= dt;
            }
            t.IsEffectActive = t.Id switch
            {
                "vibe_popping" => _vibeRemainingSec > 0,
                "freeze_trigger" => _freezeRemainingSec > 0,
                "porn_dvd" => ChaosDvdToyActive,
                "snap_field" => _snapFlashRemainingSec > 0,
                "rabbit_caller" => _rabbitCallPending > 0,
                "e_stim" => _bubbles.EStimChargesLeft > 0,
                _ => false,
            };
        }
    }

    /// <summary>Context line stamped into the crash sentinel so a native mid-run vanish
    /// self-reports its run parameters at the next launch (WPF BuildSentinelContext).</summary>
    private string BuildSentinelContext() =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "mode={0} difficulty={1} waves={2} elapsed={3:F0}s",
            AvaloniaChaosMode.ActiveMode, _state?.Config?.Difficulty, _waveCount, _state?.ElapsedSec ?? 0);

    /// <summary>Her gold tip for a finished loop (WPF AwardLoopTip). A clean loop pays double.
    /// Note: the simplified engine has no per-loop detonation counter, so "clean" here means
    /// zero detonations across the whole descent (a stricter proxy than WPF's final-loop test).</summary>
    private void AwardLoopTip(ChaosRunState state)
    {
        bool clean = state.Detonated == 0;
        int tip = (int)Math.Round(_rng.Next(3, 7) * state.DifficultyMult);
        if (clean) tip *= 2;
        tip = Math.Max(1, tip);
        ChaosMeta.AddGold(tip);
        state.PushEvent(clean
            ? $"{ChaosGlyphs.Gold} clean loop — she tips +{tip} gold"
            : $"{ChaosGlyphs.Gold} loop done — she tips +{tip} gold");
    }

    private ChaosNarrativeContext BuildNarrativeContext(int depth)
    {
        var ctx = new ChaosNarrativeContext
        {
            RankIndex = ChaosMeta.RankIndex,
            Depth = depth,
            OwnedItemIds = _state?.ActiveBoons.Select(b => b.Id)
                .Concat(_state?.ActiveCurses.Select(c => c.Id) ?? Enumerable.Empty<string>())
                .ToList(),
        };
        if (_state != null)
        {
            ctx.RunStats = new Dictionary<string, double>
            {
                ["streak"] = _state.Combo,
                ["bestStreak"] = _state.BestCombo,
                ["defused"] = _state.Defused,
                ["detonated"] = _state.Detonated,
                ["score"] = _state.Score,
            };
        }
        return ctx;
    }

    private void ShowWaveConversation(int depth)
    {
        var ctx = BuildNarrativeContext(depth);
        ChaosNarrativeHooks.OnWaveStart(depth, ctx);
        var convo = ChaosNarrativeDirector.Pick(ctx, "zone_border")
            ?? (depth >= 5 ? ChaosNarrativeDirector.Pick(ctx, "depthV_enter") : null);
        if (convo != null)
            RunOnUi(() => _overlay?.ShowConversation(convo, null, () => { }));
    }

    private void RunOnUi(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    private static DispatcherTimer StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return timer;
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _action;
        public DisposableAction(Action action) => _action = action;
        public void Dispose() => _action();
    }
}

/// <summary>Avalonia avatar-window service. Lazily creates the avatar tube window
/// and exposes chat/hotkey integration.</summary>
public sealed class AvaloniaAvatarWindowService : IAvatarWindowService
{
    private readonly ILogger<AvaloniaAvatarWindowService>? _logger;
    private readonly Window? _parentWindow;
    private AvatarTube.AvatarTubeWindow? _window;
    private bool _isMuted;
    private bool _chaosRunActive;
    private bool _detached;

    public AvaloniaAvatarWindowService()
    {
        _logger = global::ConditioningControlPanel.Avalonia.App.Services?.GetRequiredService<ILogger<AvaloniaAvatarWindowService>>();

        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _parentWindow = desktop.MainWindow;
        }
    }

    public bool IsMuted => _isMuted;

    public bool IsVisible => _window?.IsVisible ?? false;

    // lot-8 C1 (#463): proxy the tube's speaking flags so the keyword-trigger busy-retry can
    // hold an awareness comment until she stops talking instead of cutting her off. These
    // OVERRIDE the default-false members on IAvatarWindowService.
    public bool IsSpeaking => _window?.IsSpeaking ?? false;
    public bool IsSpeakingAudio => _window?.IsSpeakingAudio ?? false;

    public void ShowTube()
    {
        try
        {
            EnsureWindow();
            _window?.ShowTube();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show avatar tube");
        }
    }

    public void HideTube()
    {
        try
        {
            _window?.HideTube();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to hide avatar tube");
        }
    }

    public void SetMuteAvatar(bool muted)
    {
        _isMuted = muted;
        if (_window != null)
        {
            _window.SetMuted(muted);
        }
    }

    public void SetChaosRunActive(bool active)
    {
        _chaosRunActive = active;
        if (_window != null)
        {
            _window.SetChaosRunActive(active);
        }
    }

    public void SetDetached(bool detached)
    {
        _detached = detached;
        if (_window != null)
        {
            _window.SetDetached(detached);
        }
    }

    public void SetPose(int poseNumber)
    {
        try
        {
            EnsureWindow();
            _window?.SetPose(poseNumber);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to set avatar pose");
        }
    }

    public void OpenChatWindow()
    {
        try
        {
            EnsureWindow();
            _window?.ShowTube();
            _window?.OpenChatInput();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open avatar chat window");
        }
    }

    public void Giggle(string? text = null)
    {
        try
        {
            EnsureWindow();
            if (_window == null) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                _window.ShowGiggle("*giggles*");
            }
            else
            {
                _window.ShowGiggle(text);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to trigger avatar giggle");
        }
    }

    public void GigglePriority(string text, bool playSound = true, bool aiGenerated = false,
        string? phraseAudioPath = null, bool barkVoice = false)
    {
        try
        {
            EnsureWindow();
            _window?.GigglePriority(text, playSound, aiGenerated, phraseAudioPath, barkVoice);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to trigger priority avatar giggle");
        }
    }

    private void EnsureWindow()
    {
        if (_window != null) return;

        // Reuse the avatar tube window that MainWindow already created so we never
        // show two tubes side-by-side.
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is global::ConditioningControlPanel.Avalonia.Views.MainWindow main)
        {
            _window = main.AvatarTube;
        }

        if (_window == null)
        {
            _window = new AvatarTube.AvatarTubeWindow(_parentWindow);
            _window.Closed += (_, _) => _window = null;
            _window.SetMuted(_isMuted);
            _window.SetChaosRunActive(_chaosRunActive);
            _window.SetDetached(_detached);
        }
    }
}

/// <summary>Bark/notification service for the Avalonia head.</summary>
public sealed class AvaloniaBarkService : IBarkService
{
    /// <summary>Raised when the avatar is clicked; subscribers (e.g. the active AvatarTubeWindow) can react with speech/emote.</summary>
    public event Action? AvatarClicked;

    /// <summary>
    /// Raised when a Chaos (or other) notification wants the avatar to speak.
    /// The kind string lets subscribers pick an appropriate phrase/style.
    /// </summary>
    public event Action<string>? BarkRequested;

    public void NotifyAvatarClicked()
    {
        try { AvatarClicked?.Invoke(); }
        catch { /* never break click handling for a bark */ }
    }

    public void NotifyChaosDollhouseFirstOpen() => RaiseBark("chaos.dollhouse");
    public void NotifyChaosRevealFlash(string id) => RaiseBark("chaos.reveal");
    public void NotifyChaosResultsShown(double score, double best, double delta, bool pb,
                                        int defused, int detonated, int bestCombo, string difficulty)
        => RaiseBark("chaos.results");
    public void NotifyChaosRankUp(string rankName) => RaiseBark("chaos.rankup");
    public void NotifyChaosGiftGiven() => RaiseBark("chaos.gift");
    public void NotifyChaosDraftAutopick() => RaiseBark("chaos.autopick");
    public void NotifyChaosRunStarted(string difficulty) => RaiseBark("chaos.runstarted");
    public void NotifyChaosFocusLow() => RaiseBark("chaos.focuslow");
    public void NotifyChaosGoldFirst() => RaiseBark("chaos.goldfirst");
    public void NotifyChaosDuoDemo() => RaiseBark("chaos.duodemo");

    private void RaiseBark(string kind)
    {
        try { BarkRequested?.Invoke(kind); }
        catch { /* never break game flow for a bark */ }
    }
}

/// <summary>Video state for the Avalonia head, backed by the multi-monitor video service.</summary>
public sealed class AvaloniaVideoInfo : IVideoInfo
{
    private readonly AvaloniaMultiMonitorVideoService? _videoService;

    public AvaloniaVideoInfo(AvaloniaMultiMonitorVideoService? videoService = null)
    {
        _videoService = videoService;
    }

    public bool IsPlaying => _videoService?.IsPlaying ?? false;
}

/// <summary>Exposes the Avalonia desktop main window without coupling Core to Avalonia.</summary>
public sealed class AvaloniaMainWindowService : IMainWindowService
{
    public object? MainWindow =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}


