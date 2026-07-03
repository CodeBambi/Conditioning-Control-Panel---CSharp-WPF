using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Chaos;
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
using AvaloniaChaosTuning = ConditioningControlPanel.Avalonia.Chaos.ChaosTuning;
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
        ConditioningControlPanel.Core.Services.Chaos.IChaosTunnelService? tunnel = null)
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
                ComboMult = 1.0,
                Heat = 0,
                HeatMult = 1.0,
                BoonMult = 1.0,
                DifficultyMult = config.DifficultyMult,
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

                    ChaosEffectBannerOverlay.EnsureCreated();
                    ChaosFieldFxOverlay.EnsureCreated();
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

        var runStartCtx = BuildNarrativeContext(depth: 1);
        ChaosNarrativeHooks.OnRunStarted(runStartCtx);
        var runStartConvo = ChaosNarrativeDirector.Pick(runStartCtx, "run_start");
        if (runStartConvo != null)
            RunOnUi(() => _overlay?.ShowConversation(runStartConvo, null, () => { }));

        _bubbles.BeginChaosMode(
            OnBenignPopped,
            OnDefused,
            OnDetonated,
            CanChannelDefuse,
            OnChannelStarted,
            OnChannelBroken);
        _spawning = true;
        _state.PushEvent("🐇 the descent begins");

        try { ChaosBackdropService.Show(_state.ActIndex); } catch (Exception ex) { _logger?.LogDebug("ChaosBackdropService.Show failed: {E}", ex.Message); }

        try { _tunnel?.Show(); _tunnel?.SendZoneHint(_state.ActIndex, Math.Min(1.0, (_state.ActIndex - 1) / 5.0)); _tunnel?.SetStreak(_state.Combo, _state.ComboMult); } catch (Exception ex) { _logger?.LogDebug("ChaosTunnel wire failed: {E}", ex.Message); }

        ChaosHappyPath.OnRunStarted(_state, this);
        if (ChaosMeta.State.RunsCompleted == 0)
            ChaosHappyPath.OnFirstDescentStarted(_state);
        try { AvaloniaChaosApp.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty); } catch { }

        // Apply equipped start boon, if any.
        var equipped = ChaosMeta.State.EquippedStartBoon;
        if (!string.IsNullOrEmpty(equipped))
        {
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null)
            {
                _state.ApplyBoon(boon);
                _state.RunPickTiles.Add(new ChaosSidebarBoon { Id = boon.Id, Name = boon.Name, Glyph = "◈" });
                _state.PushEvent($"◈ start: {boon.Name}");
            }
        }

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
        _spawnTimer = StartPeriodicTimer(TimeSpan.FromMilliseconds(900), SpawnTick);
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

        // Power-ups run on the real clock.
        if (_freezeRemainingSec > 0)
        {
            _freezeRemainingSec -= dt;
            if (_freezeRemainingSec <= 0) EndFreeze();
        }
        if (_snapFlashRemainingSec > 0) _snapFlashRemainingSec -= dt;

        // The Ripple recharges.
        if (_rippleCooldownSec > 0)
        {
            _rippleCooldownSec -= dt;
        }
        _state.RippleReady = _rippleCooldownSec <= 0;
        _state.RippleText = _state.RippleReady ? "READY" : $"{Math.Ceiling(_rippleCooldownSec):0}s";

        TickActiveToys(dt);

        // Passive focus regen while a run is active.
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + AvaloniaChaosTuning.FocusRegenPerSec * dt);

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
            if (_focusLowAccumSec >= AvaloniaChaosTuning.FocusLowBarkSec)
            {
                _focusLowBarkFired = true;
                _state.PushEvent("◌ low focus. pop treats before you grab a live one.");
                try { AvaloniaChaosApp.Bark?.NotifyChaosFocusLow(); } catch { }
            }
        }
        else _focusLowAccumSec = 0;

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

    private void SpawnTick()
    {
        if (!_spawning || _state == null || _paused || _manualPaused || _ending) return;
        SpawnRandomBubble();
    }

    private void SpawnRandomBubble()
    {
        try
        {
            var pool = _state?.Config.EnabledVariants?.Count > 0
                ? _state.Config.EnabledVariants
                : new List<string> { "flash", "pink", "subliminal" };
            string variant = pool[_rng.Next(pool.Count)];
            bool live = _rng.NextDouble() < 0.45;

            var motion = (_state?.Config.MotionMode ?? "Mixed") switch
            {
                "FloatUp" => ChaosMotion.FloatUp,
                "RainDown" => ChaosMotion.RainDown,
                "RoamBounce" => ChaosMotion.RoamBounce,
                _ => _rng.Next(3) switch { 0 => ChaosMotion.FloatUp, 1 => ChaosMotion.RainDown, _ => ChaosMotion.RoamBounce },
            };

            var spec = new ChaosBubbleSpec
            {
                VariantId = variant,
                PayloadKind = variant,
                SizePx = 80 + _rng.Next(80),
                IsLive = live,
                FuseMs = live ? (int)(4000 + _rng.NextDouble() * 4000) : 0,
                Motion = motion,
                SpeedMult = 1.0 + _rng.NextDouble(),
            };
            _bubbles.SpawnChaosBubble(spec);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AvaloniaChaosService spawn failed");
        }
    }

    private void OnBenignPopped(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnTreatPopped(spec.VariantId);
        ChaosNarrativeHooks.OnFirstPop(BuildNarrativeContext(depth: _waveIndex));

        // Special pickups
        if (spec.IsFreeze) { ActivateFreeze(); _state.PushEvent("❄ the field holds still"); }
        if (spec.IsHeart) { _state.Shields++; _state.PushEvent("💖 +1 resistance"); }
        if (spec.IsDroplet)
        {
            int gold = 3 + _rng.Next(5);
            ChaosMeta.AddGold(gold);
            _state.PushEvent($"✧ +{gold} gold");
        }

        if (spec.IsGolden)
        {
            int gold = 12 + _rng.Next(13);
            ChaosMeta.AddGold(gold);
            _state.PushEvent($"{ChaosGlyphs.Gold} +{gold} gold");
            ChaosHappyPath.OnGoldFirstSeen();
        }

        // Fire the bubble's payload (flash, subliminal, overlay, etc.)
        FirePayload(spec);

        double focusGain = spec.IsGolden ? AvaloniaChaosTuning.FocusPerGolden : AvaloniaChaosTuning.FocusPerPop;
        double basePay = 100 * (_state.DifficultyMult) * (1 + _state.Heat);
        double pay = basePay * _state.ComboMult * _state.BoonMult * _state.UrgeMult;
        _state.Score += pay;
        _state.Combo++;
        _state.EffectsFired++;
        _state.Focus = Math.Min(_state.FocusMax, _state.Focus + focusGain);
        _state.Heat = Math.Min(1.0, _state.Heat + 0.02);
        UpdateStateText();
    }

    private void OnDefused(ChaosBubbleSpec spec, double fuseSec, bool viaChannel)
    {
        if (_state == null) return;

        // The player's hand pays for completed channels; toys/chains/ripples defuse for free.
        if (viaChannel)
        {
            _state.Focus = Math.Max(0, _state.Focus - DefuseCostFor(spec));
            ChaosMeta.State.TotalChannelSeconds += _state.ChannelHeldSec;
            _state.IsChanneling = false;
            _state.ChannelHeldSec = 0;
            _state.ChannelTargetBubbleId = null;
        }

        ChaosLessonHooks.OnDefuseCompleted(fuseSec, viaChannel);
        ChaosNarrativeHooks.OnFirstDefuse(BuildNarrativeContext(depth: _waveIndex));
        ChaosHappyPath.OnDefuseCompleted();

        double basePay = 250 * _state.DifficultyMult * (1 + _state.Heat);
        double pay = basePay * _state.ComboMult * _state.BoonMult * _state.UrgeMult;
        _state.Score += pay;
        _state.Combo++;
        _state.Defused++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.03);
        UpdateStateText();
    }

    private void OnDetonated(ChaosBubbleSpec spec)
    {
        if (_state == null) return;
        _state.Detonated++;

        // Fire the bubble's payload (the punishment for letting the fuse expire)
        FirePayload(spec);

        ChaosLessonHooks.OnDetonation();
        ChaosNarrativeHooks.OnFirstDetonation(BuildNarrativeContext(depth: _waveIndex));

        // A shield absorbs the detonation and preserves the streak.
        bool shieldAbsorbed = _state.Shields > 0;
        if (shieldAbsorbed)
        {
            _state.Shields--;
            _state.PushEvent("♥ shield absorbed the snap");
        }
        else
        {
            _state.Combo = 0;
            _state.ComboMult = 1.0;
        }

        _state.Heat = Math.Max(0, _state.Heat - 0.15);
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

    /// <summary>Focus cost for one channel (Bound halves pay half each).</summary>
    private double DefuseCostFor(ChaosBubbleSpec spec) =>
        spec.IsBoundHalf ? AvaloniaChaosTuning.DefuseCostBound : AvaloniaChaosTuning.DefuseCost;

    /// <summary>May the player's press start a defuse channel?</summary>
    private bool CanChannelDefuse(ChaosBubbleSpec spec)
    {
        if (_state == null) return false;
        return _state.Focus >= DefuseCostFor(spec);
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

    private void UpdateStateText()
    {
        if (_state == null) return;
        _state.BestCombo = Math.Max(_state.BestCombo, _state.Combo);
        _state.ComboMult = 1.0 + Math.Min(2.0, _state.Combo * 0.02) + _state.ComboMultBonus;
        _state.HeatMult = 1.0 + _state.Heat;
        _state.TotalMultText = $"x{_state.ComboMult * _state.BoonMult * _state.HeatMult * _state.UrgeMult:0.0}";
        _state.ScoreText = ((int)_state.Score).ToString("N0");
        _state.ShieldText = $"{_state.Shields} ♥";
        _state.FocusText = _state.IsChanneling
            ? $"HOLD {_state.ChannelHeldSec:0.0}s / {(int)_state.Focus} / {(int)_state.FocusMax}"
            : $"{(int)_state.Focus} / {(int)_state.FocusMax}";
        _state.ChannelText = _state.IsChanneling
            ? $"channeling… {_state.ChannelHeldSec:0.0}s"
            : "";
        _state.FocusLow = _state.Focus < AvaloniaChaosTuning.FocusLowThreshold;
        _state.RaiseChanged(nameof(ChaosRunState.FocusText));
        _state.RaiseChanged(nameof(ChaosRunState.ChannelText));
        _state.RaiseChanged(nameof(ChaosRunState.FocusLow));

        var remaining = Math.Max(0, _state.RunDurationSec - _state.ElapsedSec);
        _state.ClockText = $"{(int)remaining / 60}:{(int)remaining % 60:00}";
        _state.RunTimeText = $"{(int)_state.ElapsedSec / 60}:{(int)_state.ElapsedSec % 60:00}";
        _state.ActWaveText = $"I · {_waveIndex}";
        _state.RunProgress = _state.ElapsedSec / _state.RunDurationSec;

        // Fall speed tracks the pop streak (mirrors WPF ChaosModeService mid-run SetStreak).
        try { _tunnel?.SetStreak(_state.Combo, _state.ComboMult); } catch (Exception ex) { _logger?.LogDebug("ChaosTunnel SetStreak failed: {E}", ex.Message); }
    }

    private void ShowDraft()
    {
        if (_overlay == null || _state == null || _ending) return;
        _paused = true;
        _bubbles.SetChaosFrozen(true);
        _bubbles.SetChaosInputLocked(true);

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
            _state.ApplyBoon(boon, shielded);
            _state.RunPickTiles.Add(new ChaosSidebarBoon { Id = boon.Id, Name = boon.Name, Glyph = boon.IsCurse ? "☠" : "◈" });
            _state.PushEvent($"{(boon.IsCurse ? (shielded ? "☠ shielded" : "☠ accepted") : "◈ chose")} {boon.Name}");
            ChaosLessonHooks.OnDraftCardTaken(boon.IsCurse);
            if (boon.IsCurse)
            {
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
            UpdateStateText();
            return;
        }

        _waveIndex++;
        _state.WaveIndex = _waveIndex;
        ShowWaveConversation(_waveIndex);

        _paused = false;
        _bubbles.SetChaosInputLocked(false);
        _bubbles.SetChaosFrozen(false);
        UpdateStateText();
    }

    private void EndRun(bool ranFullCourse = false)
    {
        if (!_active || _ending) return;
        _ending = true;
        _spawning = false;
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
            ChaosLessonHooks.OnRunCompleted(state.Shields, ranFullCourse, state.Config.Difficulty);
            ChaosNarrativeHooks.OnRunEnded(BuildNarrativeContext(depth: _waveIndex), state.Score, ranFullCourse);

            double baseXp = Math.Sqrt(Math.Max(0, state.Score)) * 1.5 + state.RunDurationSec / 60.0 * 35.0 * state.DifficultyMult;
            double skillMult = 1.0;
            double finalXp = baseXp * skillMult;
            int sparks = (int)Math.Round(finalXp);
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
            try { ChaosBackdropService.CloseActive(); } catch { }
        });
        _state = null;
        _vibeRemainingSec = 0;
        _freezeRemainingSec = 0;
        _snapFlashRemainingSec = 0;
        _rippleCooldownSec = 0;
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
            try { ChaosEffectBannerOverlay.RaiseActive(); } catch { }
        });
    }

    private void RaiseGameLayerAboveVideo()
    {
        if (!_spawning) return;
        RunOnUi(() =>
        {
            try { ChaosFieldFxOverlay.RaiseActive(); } catch { }
            try { ChaosPopText.RaiseActive(); } catch { }
            try { ChaosEffectBannerOverlay.RaiseActive(); } catch { }
            try { ChaosAnnouncerOverlay.RaiseActive(); } catch { }
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
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(i * AvaloniaChaosTuning.RIPPLE_WAVE_GAP_MS) };
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
            return;
        }
        if (!toy.IsReady) return;
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
                ChaosDvdOverlay.Launch(Math.Max(5, power), speed, scale, count: maxed ? 2 : 1,
                    splitBounces: _state.DvdSplitBounces);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
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
        _freezeRemainingSec = AvaloniaChaosTuning.FREEZE_DURATION_SEC;
        _bubbles.SetChaosFrozen(true);
        _bubbles.VibrateAllForFreeze(AvaloniaChaosTuning.FREEZE_VIBRATE_MS);
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
        try { ChaosCursorGlowOverlay.Arm(); } catch { }
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
        try { ChaosCursorGlowOverlay.Disarm(); } catch { }
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
            try { ChaosCursorGlowOverlay.MoveToPx(cur.Value.X, cur.Value.Y); } catch { }

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

    private void SpawnDarter(double? atPxX = null, double? atPxY = null)
    {
        if (_state == null) return;
        var spec = ChaosBubbleVariants.BuildDarter(_state.DifficultyMult, spotlight: false, atPxX, atPxY);
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
        if (_dvdBannerOn && !ChaosDvdOverlay.AnyToyActive) { _dvdBannerOn = false; }
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
                "porn_dvd" => ChaosDvdOverlay.AnyToyActive,
                "snap_field" => _snapFlashRemainingSec > 0,
                "rabbit_caller" => _rabbitCallPending > 0,
                "e_stim" => _bubbles.EStimChargesLeft > 0,
                _ => false,
            };
        }
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


