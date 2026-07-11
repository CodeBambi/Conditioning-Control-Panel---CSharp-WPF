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
/// <summary>
/// Avalonia Chaos service — AMBIENT compositor surfaces only. The native/Skia DTRH chaos-run
/// game (countdown, spawn loop, scoring, boon draft, hub/HUD/boon-bar/overlay windows) was
/// STRIPPED 2026-07-11 (owner ruling, task board row #6): the WebView DTRH game is the sole
/// run path in this head. What remains is the ambient/shared surface driven by the web game's
/// native-effects bridge, the ambient effect payloads and the --verify-layers harness: the 12
/// chaos compositor layers (registered for the service lifetime), the announcer queue, flash
/// wash, DVD logos, gif cascade, field FX, pop text, effect banners and the cursor glow.
/// The Core <see cref="IChaosService"/> run members are documented no-ops here — the interface
/// is WPF-shared and unchanged (WPF keeps its native run, ChaosModeService.cs).
/// </summary>
public sealed class AvaloniaChaosService : IChaosService
{
    private readonly IBubbleService _bubbles;
    private readonly ISettingsService _settings;
    private readonly ILogger<AvaloniaChaosService>? _logger;
    private readonly ChaosCrashSentinel? _crashSentinel;
    // WS2/WP3 template migration: the Rabbit Caller cursor-glow telegraph is a compositor
    // layer, not a window. The service owns the state and drives it (UCE rule 7).
    private readonly ChaosCursorGlowLayer _cursorGlowLayer;
    // E-Stim arc bolts are a compositor layer (replaces the unwired ChaosEStimOverlay). The
    // native-run driver is gone; the layer stays registered for the harness + ambient ports.
    private readonly ChaosEStimArcLayer _eStimArcLayer;
    // Vibe-pop cursor trail layer (same registration-only status post-strip).
    private readonly ChaosVibeTrailLayer _vibeTrailLayer;
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
    // compositor layer. The seams below are live for the bubble-engine FX port + the harness.
    private readonly ChaosFieldFxLayer _fieldFxLayer;
    // Full-screen vignette pulse — a compositor layer (migrated from ChaosFxWindow, WS2/WP3).
    // Registered once for the service lifetime; IsActive is content-driven so idle costs nothing.
    private readonly ChaosFxLayer _fxLayer;
    // Wave-timer pill (top-right) — a compositor layer (migrated from ChaosWaveTimerOverlay, WS2/WP3).
    private readonly ChaosWaveTimerLayer _waveTimerLayer;
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
        IScreenProvider? screenProvider = null,
        IScreenShakeService? screenShake = null)
    {
        _bubbles = bubbles;
        _settings = settings;
        _logger = logger;
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
        _fxLayer = new ChaosFxLayer();
        compositor?.RegisterLayer(_fxLayer);
        _waveTimerLayer = new ChaosWaveTimerLayer();
        compositor?.RegisterLayer(_waveTimerLayer);
        _eStimArcLayer = new ChaosEStimArcLayer();
        compositor?.RegisterLayer(_eStimArcLayer);
        _vibeTrailLayer = new ChaosVibeTrailLayer();
        compositor?.RegisterLayer(_vibeTrailLayer);
        _screenProvider = screenProvider;
        AvaloniaChaosCatalogs.EnsureInitialized();
    }

    // ---- IChaosService run surface: documented no-ops since the native-run strip ----
    // (owner ruling 2026-07-11, task board row #6: the WebView DTRH game is the sole run
    // path; WPF keeps its native run — ChaosModeService.cs — so the Core interface stays).

    public bool IsRunning => false;
    public bool IsManuallyPaused => false;
    public double LastRunScore => 0;

    public void ShowLoadoutSidebar() { }
    public void CloseLoadoutSidebar() { }
    public void NotifyLoadoutChanged() { }

    /// <summary>No-op: the native run was stripped; DTRH runs live in the web game window
    /// (DtrhGameHostService → Resources/web/dtrh). WPF StartRun: ChaosModeService.cs:317.</summary>
    public void StartRun(object cfg)
        => _logger?.LogInformation("AvaloniaChaosService.StartRun ignored — native DTRH run removed; the web game is the sole path");

    public void StartRunFromSidebar() { }
    public void ToggleManualPause() { }
    public void RequestStop() { }
    public void CloseWarrenPhase() { }
    public void OpenWarrenAt(string tag) { }
    public void UnequipFromSidebar(string id) { }
    public void UseToyById(string id) { }

    /// <summary>App-exit funnel (WPF ChaosModeService.cs:3085 ForceShutdown). The native run is
    /// gone, but the sentinel CLEAR must survive (P0-5, WPF :3120-3126): a stale sentinel file
    /// from a pre-strip mid-run exit must never read as a native crash at the next launch.
    /// Ambient layers are cleared best-effort so nothing lingers past app teardown.</summary>
    public void ForceShutdown()
    {
        try { _crashSentinel?.Clear(); } catch { }
        try
        {
            ClearAnnouncements();
            ClearChaosPopText();
            ClearChaosFlashWash();
            ClearChaosDvdLogos();
            ClearChaosGifCascade();
            ClearChaosFieldFx();
            CloseEffectBanners();
            DisarmCursorGlow();
            _vibeTrailLayer.Stop();
            _fxLayer.Clear();
            _waveTimerLayer.Clear();
        }
        catch (Exception ex) { _logger?.LogDebug("Chaos ambient-layer teardown: {E}", ex.Message); }
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

    private void RunOnUi(Action action)
    {
        Dispatcher.UIThread.Post(action);
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

    // World-freeze pause/resume of the spoken voice line — forwards to the tube's Lane-B seam
    // (position-preserving; does NOT hard-stop the line). Overrides the IAvatarWindowService DIM no-ops.
    public void PauseSpokenAudio() => _window?.PauseSpokenAudio();
    public void ResumeSpokenAudio() => _window?.ResumeSpokenAudio();

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

        // SINGLE OWNER: MainWindow creates and owns the avatar tube; this service only
        // borrows it. Creating our own window here while MainWindow later created its own
        // was the duplicate-avatar bug (visible after theme changes, when both instances'
        // ActiveModChanged handlers repainted side by side). GetOrCreateAvatarTube builds
        // the window on demand and respects AvatarEnabled.
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is global::ConditioningControlPanel.Avalonia.Views.MainWindow main)
        {
            _window = main.GetOrCreateAvatarTube();
            if (_window != null)
            {
                var adopted = _window;
                adopted.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_window, adopted))
                        _window = null;
                };
            }
            return;
        }

        // Fallback for heads whose MainWindow is not the desktop MainWindow type.
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
    // S5 draft/wave choreography barks (WPF Services/Companion/BarkService.cs:247-290).
    public void NotifyChaosWaveEscalated(int wave) => RaiseBark("chaos.waveescalated");
    public void NotifyChaosWaveCleared(int wave) => RaiseBark("chaos.wavecleared");
    public void NotifyChaosBoonPicked(string boon) => RaiseBark("chaos.boonpicked");
    public void NotifyChaosCursePicked(string boon, string rarity, double runMultBonus) => RaiseBark("chaos.cursepicked");
    public void NotifyChaosBoonSkipped(int shieldsNow) => RaiseBark("chaos.boonskipped");
    public void NotifyChaosActChanged(int act, int wave) => RaiseBark("chaos.actchanged");
    // Q11 chaos gameplay barks (WPF Services/Companion/BarkService.cs:275-318).
    public void NotifyChaosEndingSoon() => RaiseBark("chaos.endingsoon");
    public void NotifyChaosDarterCaught(double points, int combo, bool quick) => RaiseBark("chaos.dartercaught");
    public void NotifyChaosFreezeCaught(double points, int combo) => RaiseBark("chaos.freezecaught");
    public void NotifyChaosComboMilestone(int combo, string difficulty) => RaiseBark("chaos.combomilestone");
    public void NotifyChaosComboBig(int combo, double threshold) => RaiseBark("chaos.combobig");
    public void NotifyChaosTeaseDebut() => RaiseBark("chaos.teasedebut");
    public void NotifyChaosTeaseDenied(int deniedCount) => RaiseBark("chaos.teasedenied");
    public void NotifyChaosTeaseClicked() => RaiseBark("chaos.teaseclicked");
    public void NotifyChaosTeaseDeniedStreak(int deniedCount) => RaiseBark("chaos.teasedeniedstreak");
    // S2c-2a bubble-economy / lesson / run-end barks (WPF Services/Companion/BarkService.cs).
    public void NotifyChaosBenignPopped(string variant, string payload, int combo) => RaiseBark("chaos.benignpopped");
    public void NotifyChaosBubbleDefused(int combo, string variant, string difficulty) => RaiseBark("chaos.defused");
    public void NotifyChaosBubbleDetonated(string variant, double strength, double runDetonations, int combo, string difficulty) => RaiseBark("chaos.detonated");
    public void NotifyChaosBubbleDetonatedAbsorbed(string variant, double strength, double runDetonations, int combo, string difficulty, int shields) => RaiseBark("chaos.detonatedabsorbed");
    public void NotifyChaosDefuseFirst() => RaiseBark("chaos.defusefirst");
    public void NotifyChaosDefuseNoFocus() => RaiseBark("chaos.defusenofocus");
    public void NotifyChaosDefuseRelease() => RaiseBark("chaos.defuserelease");
    public void NotifyChaosClickDetonate() => RaiseBark("chaos.clickdetonate");
    public void NotifyChaosLessonComplete(string id) => RaiseBark("chaos.lessoncomplete");
    public void NotifyChaosRunCompleted(int finalXp, string difficulty) => RaiseBark("chaos.runcompleted");

    private void RaiseBark(string kind)
    {
        try { BarkRequested?.Invoke(kind); }
        catch { /* never break game flow for a bark */ }
    }
}

/// <summary>Video state for the Avalonia head, backed by the compositor-only video service.</summary>
public sealed class AvaloniaVideoInfo : IVideoInfo
{
    private readonly IVideoService? _videoService;

    public AvaloniaVideoInfo(IVideoService? videoService = null)
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


