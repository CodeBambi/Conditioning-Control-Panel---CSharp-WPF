using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Owns the Chaos Mode run lifecycle over the LIVE desktop: a 3·2·1·GO countdown,
/// then waves of effect bubbles (rendered by <see cref="BubbleService"/>'s chaos
/// API) that the player pops/defuses/lets-detonate, the combo/heat/multiplier
/// stack + boon draft + shields, and the capped XP payout. The HUD is a thin
/// collapsible strip (<see cref="ChaosHudWindow"/>); the countdown/boon-draft/
/// results are a centered overlay (<see cref="ChaosOverlayWindow"/>).
///
/// Parallel to <c>BubbleService</c>'s ambient pop game — it pauses/resumes that
/// game around the run but never modifies it.
/// </summary>
public sealed class ChaosModeService
{
    /// <summary>Which mode the live (or most recent) run is. Defaults to Story so hub-side narrative
    /// behaves as before. Set at <see cref="StartRun"/>, reset to Story at <see cref="CleanupAfterRun"/>.
    /// The backdrop, narrative director, avatar and z-order helpers read this instead of the user's
    /// saved <c>NarrativeModeEnabled</c> so a Free Desktop run never clobbers their Story settings.</summary>
    public static ChaosPlayMode ActiveMode { get; private set; } = ChaosPlayMode.Story;

    /// <summary>Master switch for the Madam's Story descent. While <c>false</c> the Lab-card Story
    /// option is disabled, every run is forced to <see cref="ChaosPlayMode.FreeDesktop"/>, and the
    /// narrative/backdrop/avatar-hide never engage — i.e. the game plays exactly as it did before
    /// story support was added. Flip to <c>true</c> once real story content exists to bring Story
    /// mode + the Madam back. (Reversible single switch — no other code needs to change.)
    /// static readonly (not const) so the guards don't fold to unreachable-code warnings.</summary>
    public static readonly bool StoryModeEnabled = false;

    /// <summary>The play mode chosen on the Lab card. The pick moved here from the in-hub picker, so
    /// this is the single source of truth that <see cref="StartRun"/> reads. Defaults to Free Desktop
    /// (the pre-Story behaviour); <see cref="StartRun"/> forces Free Desktop anyway while
    /// <see cref="StoryModeEnabled"/> is false.</summary>
    public static ChaosPlayMode SelectedPlayMode { get; set; } = ChaosPlayMode.FreeDesktop;

    /// <summary>True only in a Free Desktop run: the run must NOT pin itself above other apps, so the
    /// per-tick topmost re-assertion stands down and chaos windows are born non-topmost.</summary>
    public static bool IsDesktopMode => ActiveMode == ChaosPlayMode.FreeDesktop;

    /// <summary>In-run Madam narrative + story cards fire only in Story mode AND when the user setting is
    /// on. Free Desktop runs are silent regardless of the setting. (Hub-side greetings still honor the
    /// raw setting — there is no run mode chosen yet at the hub.)</summary>
    public static bool NarrativeActive =>
        App.Settings?.Current?.NarrativeModeEnabled == true && ActiveMode == ChaosPlayMode.Story;

    /// <summary>True while a Rabbit Hole run is on screen (countdown → results dismissed). Other
    /// services gate run-hostile behavior on this — e.g. subliminals suppress their focus-steal so a
    /// flash doesn't yank foreground off the bubble-click interaction mid-run.</summary>
    public bool IsRunActive => _active;

    private ChaosRunState? _state;
    private ChaosHudWindow? _hud;
    private ChaosOverlayWindow? _overlay;
    private ChaosFxWindow? _fx;
    private DispatcherTimer? _runTimer;
    private DispatcherTimer? _spawnTimer;
    private int _chromeRaiseTick;   // throttles the per-tick chrome topmost re-assert (see RunTick)
    private int _memSampleTick;     // throttles the [CHAOSMEM] telemetry sample (~every 15s, see RunTick)
    private long _peakNativeMb;     // high-water native (~workingSet-managed) MB this run — fed to the crash sentinel
    private TimeSpan _lastRenderTs = TimeSpan.MinValue;   // [CHAOSHITCH] frame-gap detector (see OnChaosRendering)
    private int _hitchCount;
    // Perf governor: hitch frames feed a decaying pressure score; when it climbs, the spawn
    // cadence stretches (fewer NEW bubbles is the one lever that sheds load everywhere —
    // motion, collision, FX, decode all scale with the field). Eases back once frames are clean.
    private double _hitchScore;          // decayed 4x/s in RunTick (see UpdatePerfGovernor)
    private double _perfBackoff = 1.0;   // >1 = spawn interval multiplied by this
    private bool _active;        // a run session exists (countdown → results dismissed)
    private bool _spawning;      // GO fired, bubbles spawning, not yet ended
    private bool _paused;        // boon draft on screen (clock + spawns held)
    private bool _manualPaused;  // user hit pause
    private int _pendingWave;
    private bool _endingSoonFired;     // T-10s "the hole is closing" beat, once per run
    private bool _finalLoopAnnounced;  // FINAL LOOP banner, once per run (Relapse can't re-fire it)
    private bool _rippleTeachOffered;  // ready-cue teach line, at most once per run until first cast
    private int _runDetonations;  // per-run count of detonations (absorbed + unshielded)
    private int _waveDetonations; // per-loop count — a zero-detonation loop doubles its gold tip
    private int _lastComboBigFired;   // highest ComboBig threshold already fired this combo streak
    private int _lastActFired = 1;    // last ActIndex an ActChanged bark fired for
    private DateTime _lastNoFocusAnnounceUtc = DateTime.MinValue;   // throttles the big "NO FOCUS" banner so mashed grabs don't stack a queue

    // ---- "start small": named tunables (conservative defaults; no magic numbers in logic) ----
    /// <summary>High-combo thresholds that fire ChaosComboBig once per crossing (edge-detected).</summary>
    private static readonly int[] COMBO_BIG_THRESHOLDS = { 25, 50, 100 };
    /// <summary>Countdown length (ms) used on RunAgain (full-start countdown is unchanged at 3s).</summary>
    public const int ChaosRestartCountdownMs = 1000;
    /// <summary>Seconds an untouched boon draft waits before auto-skipping (+1 shield). 0 disables.</summary>
    public const int DraftAutoResumeSecDefault = 15;

    // ---- benign-pop juice ----
    private static readonly Color BENIGN_POP_COLOR = Color.FromRgb(255, 200, 235);  // soft pink/white
    private const double BENIGN_POP_PULSE = 0.10;                                    // low-strength micro-pulse
    // ---- combo micro-build juice ----
    private const double COMBO_MICRO_PULSE_MIN = 0.04;   // tiny pulse just after a milestone
    private const double COMBO_MICRO_PULSE_MAX = 0.12;   // grows toward the next milestone
    private const double COMBO_BIG_PULSE = 0.55;         // distinct big-combo beat
    private static readonly Color COMBO_MICRO_COLOR = Color.FromRgb(255, 170, 80);
    private static readonly Color COMBO_BIG_COLOR   = Color.FromRgb(255, 230, 120);
    // ---- shield gain/loss cue ----
    private static readonly Color SHIELD_GAIN_COLOR = Color.FromRgb(120, 220, 160);  // green +shield
    private const double SHIELD_GAIN_PULSE = 0.22;
    // ---- wave-clear cue ----
    private static readonly Color WAVE_CLEAR_COLOR = Color.FromRgb(150, 200, 255);
    private const double WAVE_CLEAR_PULSE = 0.30;
    // Near-miss danger telegraph is now visual-only and per-bubble (the about-to-blow bubble
    // flashes + breathes faster in BubbleService) — no screen flash, no audio tick.
    // ---- heat temperature tint (additive overlay only; rises with HeatMult) ----
    private static readonly Color HEAT_TINT_COLOR = Color.FromRgb(255, 90, 40);
    private const double HEAT_TINT_MIN = 0.30;       // heat must exceed this before the tint shows
    private const double HEAT_TINT_MAX_OPACITY = 0.30;  // peak held-edge opacity at full heat
    private double _lastHeatTint = -1;               // last applied heat-tint level (avoid churn)

    public ChaosModeService()
    {
        // Unlock toast: when something reveals mid-run, one quiet line in the event feed.
        RevealService.Pending += OnRevealPending;
        // First-times toast: "+N ✦ {label}" floats at the pop point + one feed line.
        ChaosFirstTimes.Awarded += OnFirstTimeAwarded;
        // Lesson complete: pause the fall and show the unlock card (each fires exactly once).
        ChaosLessons.LessonCompleted += OnLessonCompleted;
    }

    // ---- lesson-complete unlock card (pauses the field while the player reads) ----

    private bool _lessonCardPaused;       // a card holds the field frozen (vs draft/manual pause)
    private bool _lessonCardsAfterDraft;  // the card pause took the baton from a draft → it owes the GO! beat
    private readonly List<ChaosUnlockCardData> _pendingLessonCards = new();   // completed AT the draft table

    /// <summary>
    /// A lesson just completed (fires once per lesson, ever): freeze the field — same
    /// choreography as the manual pause — and put the unlock card up so the moment lands.
    /// The pause lifts when the LAST queued card is dismissed. Lessons judged AT the draft
    /// table (silk_touch on the loop boundary, draft4/surrender on the pick) defer to the
    /// draft's GO — a scrim over the table would cover it while its auto-pick countdown
    /// kept ticking underneath. During a manual pause / the run-end judgments the field is
    /// already held (or gone), so the card shows over whatever is up, auto-dismissing.
    /// </summary>
    private void OnLessonCompleted(string id)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!_active) return;   // hub-side completions can't happen; belt-and-suspenders
                var card = ChaosUnlockCards.ForLesson(id);
                if (card == null) return;

                _state?.PushEvent($"📖 lesson learned — {card.Title}");

                // The draft (or its ready-go beat) owns the field → hold the card for the GO.
                if (_spawning && _paused && !_manualPaused && !_lessonCardPaused)
                {
                    _pendingLessonCards.Add(card);
                    return;
                }

                // No cue here — the card itself plays the unlock sting when it lands.
                bool claimPause = _spawning && !_paused && !_manualPaused;
                if (claimPause)
                {
                    _lessonCardPaused = true;
                    _paused = true;             // holds RunTick (clock) + spawns
                    _spawnTimer?.Stop();
                    App.Bubbles?.SetChaosFrozen(true);
                    App.Bubbles?.SetChaosInputLocked(true);
                }
                // While a pause is held (just claimed, or by an earlier same-click lesson),
                // only the click may dismiss — a timeout would resume the run unattended.
                ChaosUnlockCardOverlay.Show(card, onDismissed: ResumeAfterLessonCard,
                    autoDismiss: !claimPause && !_lessonCardPaused);
            }
            catch (Exception ex) { App.Logger?.Debug("Chaos.OnLessonCompleted: {E}", ex.Message); }
        }));
    }

    /// <summary>Lift the lesson-card pause once the card queue is empty (no-op when the
    /// pause wasn't ours, or when more cards are still up).</summary>
    private void ResumeAfterLessonCard()
    {
        if (!_lessonCardPaused) return;
        if (ChaosUnlockCardOverlay.IsShowing) return;   // another card queued behind this one
        _lessonCardPaused = false;
        _paused = false;
        App.Bubbles?.SetChaosInputLocked(false);
        // A freeze power-up live when the card went up is still owed its remaining window
        // (the held clock didn't tick it down) — don't thaw the field out from under it.
        // Guaranteed overlap: the freeze_trigger lesson completes ON a freeze catch.
        if (_freezeRemainingSec <= 0) App.Bubbles?.SetChaosFrozen(false);
        if (_spawning) _spawnTimer?.Start();
        if (_lessonCardsAfterDraft)
        {
            // This pause stood in for the draft's GO! beat — deliver what it deferred.
            _lessonCardsAfterDraft = false;
            if (_state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
            AnnounceFinalLoopIfEntering();
        }
    }

    /// <summary>A first-times bonus just banked: float it where the hand was, plus a feed line.</summary>
    private void OnFirstTimeAwarded(string bonusId, int amount)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_state == null) return;
                string label = ChaosFirstTimes.Labels.TryGetValue(bonusId, out var l) ? l : bonusId;
                _state.PushEvent($"+{amount} {ChaosGlyphs.Drops} {label}");
                ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip - 30,
                    $"+{amount} {ChaosGlyphs.Drops} {label}", Color.FromRgb(0xC8, 0xA8, 0xFF));
            }
            catch { }
        }));
    }

    private DateTime _lastRevealToastUtc = DateTime.MinValue;

    /// <summary>One event-feed line per reveal batch (Sync raises Pending per id; the
    /// debounce window collapses a batch to a single line). Run-active only.</summary>
    private void OnRevealPending(string id)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!_spawning || _state == null) return;
                if ((DateTime.UtcNow - _lastRevealToastUtc).TotalSeconds < 8) return;
                _lastRevealToastUtc = DateTime.UtcNow;
                _state.PushEvent("something new in the dollhouse.");
            }
            catch { }
        }));
    }

    public bool IsRunning => _active;

    /// <summary>The descent itself is live (GO fired, not yet ended) — manual pause included.
    /// MainWindow's panic flow defers to the chaos key hook while this is true.</summary>
    public bool IsDescending => _spawning;

    public bool IsManuallyPaused => _manualPaused;

    public void ToggleManualPause()
    {
        if (!_spawning || _paused) return;
        _manualPaused = !_manualPaused;
        if (_manualPaused)
        {
            // Freeze the whole field: stop spawning, hold the clock (RunTick early-returns),
            // freeze every bubble's motion + fuse, and swallow clicks — a paused field
            // can't be farmed for pops.
            _spawnTimer?.Stop();
            App.Bubbles?.SetChaosFrozen(true);
            App.Bubbles?.SetChaosInputLocked(true);
            _state?.PushEvent("⏸ held. the hole waits.");
        }
        else
        {
            App.Bubbles?.SetChaosInputLocked(false);
            // A freeze power-up that was live when the pause hit didn't tick down (the held
            // clock stops _freezeRemainingSec) — let it finish instead of thawing it early.
            if (_freezeRemainingSec <= 0) App.Bubbles?.SetChaosFrozen(false);
            _spawnTimer?.Start();
            _state?.PushEvent("▶ sinking again");
        }
        // The HUD mirrors the pause from every entry point (its own buttons OR the panic key):
        // paused pins the panel open on the continue-or-wake-up choice; resuming folds it away.
        _hud?.SetPausedUi(_manualPaused);
    }

    /// <summary>The user's panic key, mid-descent: first press HOLDS the field (manual pause),
    /// a second press while held SURFACES the run (recap still pays out). The countdown and
    /// the draft table ignore it — their own affordances already own those moments.</summary>
    private void OnPanicKeyDuringRun()
    {
        if (!_spawning || _paused) return;
        if (!_manualPaused) ToggleManualPause();
        else RequestStop();
    }

    // ============================ start / countdown ============================

    public void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
    {
        if (_active) return;

        // A chaos run takes over the screen with its own overlays/HUD; stop any running
        // conditioning engine or AI session first so the two don't fight over the display.
        // (Self-guards — a no-op when nothing is running.)
        if (App.IsEngineRunning || App.IsSessionRunning)
            App.MainWindowRef?.StopEngineAndSession("Chaos");

        CloseLoadoutSidebar();   // the Warren-phase sidebar hands off to the real run HUD
        var cfg = config ?? ChaosRunConfig.FromSettings();

        // Lock in the play mode BEFORE any run window is built — the chaos windows read
        // ChaosWindowZ.BornTopmost in their constructors, so this must be set first.
        // The mode is picked on the Lab card (SelectedPlayMode), not per-config — the in-hub
        // picker was retired. While Story is disabled (no story content yet) we force Free
        // Desktop, so the Madam narrative/backdrop/avatar-hide never engage.
        var resolvedMode = StoryModeEnabled ? SelectedPlayMode : ChaosPlayMode.FreeDesktop;
        cfg.PlayMode = resolvedMode;
        ActiveMode = resolvedMode;
        ChaosWindowZ.DesktopMode = resolvedMode == ChaosPlayMode.FreeDesktop;
        // Pin the whole layer topmost (default) regardless of mode — Free Desktop keeps its other
        // traits but no longer sinks behind whatever window you click. Set BEFORE any chaos window
        // is created (they read ChaosWindowZ.BornTopmost in their constructors).
        ChaosWindowZ.PinTopmost = App.Settings?.Current?.ChaosPinOnTop ?? true;
        // Free Desktop is meant for keeping your PC usable, so soften intrusive payloads (no fullscreen
        // video yanking you out of what you're doing). Story keeps whatever the run config said.
        if (resolvedMode == ChaosPlayMode.FreeDesktop) cfg.AmbientMode = true;

        try
        {
            App.Bubbles?.PauseAndClear();
            _state = new ChaosRunState(cfg);
            _active = true;

            _hud = new ChaosHudWindow(_state, this);
            _hud.Show();

            // In-run sidebar stays collapsed unless hovered — the pre-run glance already happened
            // beside the Warren, so the descent starts with a clean screen.
            RefreshSidebarLoadout();

            _overlay = new ChaosOverlayWindow();
            _overlay.OnRunAgain = RunAgain;
            _overlay.OnDismissed = OnOverlayClosed;
            _overlay.Show();
            if (!isRestart) ChaosSfx.Play("fall_in", 0.28f);   // the falling whoosh under the countdown (kept soft)
            // Warm the tunnel's WebView2 + three.js under the countdown (no-op when the toggle is
            // off) so BeginRun's fade-in is instant instead of seconds of black.
            ChaosTunnelService.Preload();
            _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain

            // The run's topmost windows (overlay/FX/payload washes/bubbles) would otherwise bury an
            // ATTACHED avatar's speech bubble in the non-topmost band — keep it topmost for the run.
            App.AvatarWindow?.SetChaosRunActive(true);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "ChaosModeService.StartRun failed");
            CleanupAfterRun();
            App.Bubbles?.Resume();
            MessageBox.Show("Couldn't open the Rabbit Hole:\n\n" + ex, "The Rabbit Hole",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BeginRun()
    {
        if (!_active || _state == null) return;

        App.Bubbles?.BeginChaosMode(OnBenignPopped, OnDefused, OnDetonated, OnDarterCaught, OnFreezeCaught,
            chainReach: () => _state?.ChainReactionReach ?? 0,
            hitboxScale: () => _state?.Config?.HitboxScale ?? 1.0,
            bubbleOpacity: () => _state?.BlindfoldActive == true ? _state.BlindfoldOpacity : 1.0,
            wandShimmer: () => false,   // magic_wand retired 2026-06-10 (BubbleService hook kept for reuse)
            cursorPull: () => (_state?.CursorPullStrength ?? 0) - (_state?.CamGirlFlee ?? 0),   // Cam Girl flees; The Pull fights back
            rabbitHoming: () => (_state?.CursorPullStrength ?? 0) > 0,
            spankerOn: () => _state?.SpankerActive == true,
            spankGrow: () => _state?.SpankGrowFactor ?? 1.0,
            liveMagnet: () => _state?.MagnetEnabled == true,   // Silk Touch: near-miss on a live still touches
            onTreatExpired: OnTreatExpired,
            onEStimArc: OnEStimArc,
            rabbitTrailSec: () => _state?.RabbitTrailSec ?? 0,
            electrifiedRabbits: () => _state?.ElectrifiedRabbits == true,
            canChannelDefuse: CanChannelDefuse,
            onChannelBroken: OnChannelBroken,
            onTeaseTouched: OnTeaseTouched,
            onTeaseDenied: OnTeaseDenied,
            onBoundEnraged: OnBoundEnraged,
            onBrittleShattered: OnBrittleShattered);
        App.Bark?.NotifyChaosRunStarted(_state.Config.Difficulty.ToString());
        _state.PushEvent("🐇 the descent begins");
        _runDetonations = 0;
        _waveDetonations = 0;
        _endingSoonFired = false;
        _finalLoopAnnounced = false;
        _rippleTeachOffered = false;
        _lastComboBigFired = 0;
        _lastActFired = 1;
        _lastHeatTint = -1;
        ChaosLessonHooks.OnRunStarted();   // lessons: fresh per-run trackers
        EndSlowMo(); EndFreeze();   // clean power-up state for the new run (no leak across runs)
        _snapFlashRemainingSec = 0; _rabbitStormRemainingSec = 0; _rabbitStormAccumSec = 0; _thoughtAccumSec = 0;
        _pendulumRolledWave = 0;   // pendulum event re-rolls its beat for wave 1
        _heartRolledWave = 0; _heartArmedThisWave = false;   // pop-up heart re-rolls too
        _spawnSerial = 0; _ordinarySpawns = 0; _pendulumSlowActive = false; _afterglowApplied = false;   // run-boon transient state
        _heavyUntilUtc = DateTime.MinValue; _chaosVideoCapUtc = DateTime.MinValue;   // heavy gate never leaks across runs
        // The Spanker capstone gate — each DVD/thought logo samples this when it spawns.
        ChaosDvdOverlay.SpankerRedirect = () => _state?.SpankerActive == true
                                             && _state?.MaxedBoons.Contains("the_spanker") == true;
        App.Overlay?.WarmSpiralCache();   // pre-decode the spiral off-thread so its first show doesn't hitch
        ChaosEffectBannerOverlay.EnsureCreated();   // birth the banner window NOW, not mid-chaos
        ChaosFieldFxOverlay.EnsureCreated();        // ripples/residue/trails are drafted mid-run — pre-create always
        ChaosSkiaFxOverlay.EnsureCreated();         // PROTOTYPE Skia FX layer (rabbit trail + caller glow); self-gates on AppSettings flag

        // The run-pick ribbon along the top: shows ONLY mantras/sins drafted during THIS descent,
        // in pick order, beside the clock. Bind to this run's collection so each drafted card lands
        // on it live (the start-boon pick below re-fires it too).
        ChaosBoonBarOverlay.EnsureCreated();
        var runPicks = _state.RunPickTiles;
        ChaosBoonBarOverlay.SetPicks(runPicks);
        runPicks.CollectionChanged += (_, _) => ChaosBoonBarOverlay.SetPicks(runPicks);

        // Loadout: a pre-equipped start boon enters the run already active (before wave 1).
        // The scripted first descent falls in bare — no start boon, whatever a stale save says.
        var equipped = _state.Config.ScriptedFirstRun ? null : ChaosMeta.State.EquippedStartBoon;
        if (!string.IsNullOrEmpty(equipped))
        {
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null)
            {
                _state.ApplyBoon(boon);
                ChaosMeta.MarkDiscovered("boon:" + boon.Id);
                // Same top-center treatment as a drafted pick — the sidebar feed line alone
                // read as "text off at the side of the screen".
                ChaosAnnouncerOverlay.Announce($"◈ {boon.Name}", ChaosAnnounceKind.Mantra, artKey: boon.Id);
            }
        }
        // Welcome Shower equipped as the start boon: the very first GO! gets its treat dump too.
        if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();

        // Lifetime boons (Skills/Accessories/Utility): apply active ones at their level, then
        // mirror them into the HUD strip as icons. The pre-run glance is over: the loadout
        // locks in and the pinned-open panel folds away.
        ChaosMeta.ApplyLifetimeBoons(_state);
        RefreshSidebarLoadout();
        _hud?.SetPreRunExpanded(false);
        // Pocket Watch gates ALL timekeeping: the sidebar run clock + fill bar only show with the charm.
        _hud?.SetClockVisible(_state.ShowWaveTimer);

        // Pocket Watch: birth the wave-countdown window NOW (keep-alive contract — never mid-run).
        if (_state.ShowWaveTimer) ChaosWaveTimerOverlay.EnsureCreated();
        // Rabbit Caller equipped: pre-create the cursor-glow halo for the summon-at-click.
        if (ChaosMeta.IsBoonActive("rabbit_caller") && !ChaosSkiaFxOverlay.Enabled) ChaosCursorGlowOverlay.EnsureCreated();
        if (ChaosMeta.IsBoonActive("e_stim")) ChaosEStimOverlay.EnsureCreated();
        // VibePopping equipped: pre-create the pointer glow + trail for the buzz window.
        if (ChaosMeta.IsBoonActive("vibe_popping")) ChaosVibeTrailOverlay.EnsureCreated();

        // Muscle Memory regen caps at the resistance you descended with (capstone: +1 above it).
        _state.StartShields = _state.Shields;
        _regenPopCount = 0;
        _heartbeatCooldownSec = 0;

        // Hold-to-defuse: fresh focus warning + Snap Chain invuln state for the new descent.
        _focusLowAccumSec = 0;
        _focusLowBarkFired = false;
        _invulnUntilUtc = DateTime.MinValue;
        _teaseDeniedThisRun = 0;
        _teaseDeniedStreakBarked = false;
        // Happy path: the scripted-run director resets its beat trackers with the run.
        ChaosHappyPath.OnRunStarted(_state, this);

        // First descent since the verb changed: one quiet line so the hold isn't a mystery.
        // The scripted run 1 holds this back — ChaosHappyPath fires it at the lone-threat beat.
        if (!ChaosMeta.State.SeenDefuseTutorial && !_state.Config.ScriptedFirstRun)
        {
            ChaosMeta.State.SeenDefuseTutorial = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("press and HOLD a live one to snap it", ChaosAnnounceKind.Willpower, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            _state.PushEvent("✋ hold to snap. let go and it triggers.");
        }

        // Active skills (toys): build their state, listen for keybinds, and park one big
        // hero button per toy at the bottom-left of the screen (clickable at a glance).
        _vibeRemainingSec = 0;
        BuildActiveToys();
        StartKeyHook();
        StartRippleHook();   // the Ripple: right-click verb, live for the whole descent
        for (int i = 0; i < _state.ActiveToys.Count; i++)
        {
            try
            {
                var btn = new ChaosToyButtonWindow(_state.ActiveToys[i], this, i);
                btn.Show();
                _toyButtons.Add(btn);
            }
            catch (Exception ex) { App.Logger?.Debug("Toy button: {E}", ex.Message); }
        }

        _spawning = true;

        try { _fx = new ChaosFxWindow(); _fx.Show(); } catch (Exception ex) { App.Logger?.Debug("Chaos FX init: {E}", ex.Message); }

        // A mandatory-video payload can fire mid-run; keep the gameplay layer above it (see handler).
        if (App.Video != null) App.Video.VideoStarted += OnVideoStartedDuringRun;
        if (App.Video != null) App.Video.VideoEnded += OnVideoEndedDuringRun;

        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _runTimer.Tick += RunTick;
        _runTimer.Start();

        _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _spawnTimer.Tick += SpawnTick;
        _spawnTimer.Start();

        // Narrative layer (the Madam) + per-zone backdrop. Both gated on their settings internally;
        // the backdrop spawns no window when off, so classic Chaos keeps its desktop click-through.
        ChaosNarrativeHooks.OnRunStarted();
        _pendingDepthVCard = false;
        ChaosBackdropService.Show(_state.ActIndex);
        // Endless 3D tunnel background (opt-in, gated on ChaosTunnelEnabled internally; spawns no
        // window when off). Non-topmost like the backdrop, so it needs no z-order handling.
        ChaosTunnelService.Show();
        ChaosTunnelService.SendZoneHint(_state.ActIndex, Math.Min(1.0, (_state.ActIndex - 1) / 5.0));
        // Fall speed follows the streak: seed it (super-slow start) and track every Combo change
        // (gain, halve, break) through the state's own setter — no per-call-site wiring.
        ChaosTunnelService.SetStreak(_state.Combo, _state.ComboMult);
        _state.PropertyChanged += OnStateChangedForTunnel;
        ChaosNarrativeHooks.OnMoment("run_start", BuildNarrativeCtx());

        // Arm crash diagnostics: fresh peak, baseline sample, and the sentinel goes live for THIS run.
        _peakNativeMb = 0; _memSampleTick = 0;
        LogMemSample("run-start");
        // Arm the frame-hitch detector + perf governor for this run.
        _lastRenderTs = TimeSpan.MinValue; _hitchCount = 0;
        _hitchScore = 0; _perfBackoff = 1.0;
        System.Windows.Media.CompositionTarget.Rendering += OnChaosRendering;
    }

    /// <summary>
    /// Open a STORY conversation card in the run overlay (routed here by <see cref="ChaosStoryCards"/>
    /// when a descent is live). Reuses the lesson-card pause — freeze clock + spawns + bubble motion +
    /// input — and resumes the field when the card closes. The card paints over the live zone plate.
    /// </summary>
    public void PlayStoryCard(ChaosConversation convo)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_overlay == null || !_active) return;
                // Claim a pause only if the field is live and unheld; otherwise let the existing owner
                // (draft/manual/lesson) keep ownership and just show the card without resuming on close.
                bool claimPause = _spawning && !_paused && !_manualPaused && !_lessonCardPaused;
                if (claimPause)
                {
                    _lessonCardPaused = true;
                    _paused = true;
                    _spawnTimer?.Stop();
                    App.Bubbles?.SetChaosFrozen(true);
                    App.Bubbles?.SetChaosInputLocked(true);
                }
                var bg = ChaosBackdropService.CurrentSource
                         ?? ChaosArt.Resolve("backdrops", "depth" + (_state?.ActIndex ?? 1));
                _overlay.ShowConversation(convo, bg,
                    onComplete: claimPause ? ResumeAfterLessonCard : (Action?)null);
            }
            catch (Exception ex) { App.Logger?.Debug("Chaos.PlayStoryCard: {E}", ex.Message); }
        }));
    }

    /// <summary>Snapshot the live run into a narrative context (depth/rank/owned/run-stats) for the director.</summary>
    private ChaosNarrativeContext BuildNarrativeCtx()
    {
        var stats = new System.Collections.Generic.Dictionary<string, double>();
        if (_state != null) stats["streak"] = _state.Combo;
        return new ChaosNarrativeContext
        {
            Depth = _state?.ActIndex ?? 1,
            RankIndex = (int)ChaosMeta.RankIndex,
            OwnedItemIds = TakenBoonIds(),
            RunStats = stats,
        };
    }

    // ---- pre-run loadout sidebar (shown beside the Warren hub, before any run exists) ----
    private ChaosHudWindow? _preHud;
    private ChaosRunState? _preState;

    /// <summary>The loadout is editable while the Warren sidebar is up or from FALL IN until
    /// SINK fires (boons apply at BeginRun).</summary>
    public bool CanEditLoadout => !_spawning && (_active || _preHud != null);

    /// <summary>Open the loadout sidebar next to the Warren: the run HUD, pinned open, pockets
    /// editable. Lives until the hub closes (a started run swaps in the real HUD).</summary>
    public void ShowLoadoutSidebar()
    {
        if (_active || _preHud != null) return;
        try
        {
            _preState = new ChaosRunState(ChaosRunConfig.FromSettings());
            _preHud = new ChaosHudWindow(_preState, this);
            _preHud.Show();
            RefreshSidebarLoadout();
            _preHud.SetHeroMode(preRun: true);   // hero button reads FALL IN until the run takes over
            _preHud.SetPreRunExpanded(true);
        }
        catch (Exception ex) { App.Logger?.Debug("ShowLoadoutSidebar: {E}", ex.Message); }
    }

    /// <summary>Close the Warren-phase sidebar (hub closed or a run is taking over).</summary>
    public void CloseLoadoutSidebar()
    {
        try { _preHud?.Close(); } catch { }
        _preHud = null;
        _preState = null;
    }

    /// <summary>A pocket tile was clicked in the HUD during the pre-run glance: take the boon off.</summary>
    public void UnequipFromSidebar(string id)
    {
        if (!CanEditLoadout || string.IsNullOrEmpty(id)) return;
        if (!ChaosMeta.IsBoonActive(id)) return;
        ChaosMeta.SetBoonActive(id, false);
        RefreshSidebarLoadout();
        (_state ?? _preState)?.PushEvent("👝 left behind: " + (ChaosLifetimeBoons.ById(id)?.Name ?? id));
        ChaosHubWindow.Current?.RefreshAfterExternalLoadoutChange();   // keep the open Warren in sync
    }

    /// <summary>The Warren changed the loadout while the pre-run glance is up — mirror it.</summary>
    public void NotifyLoadoutChanged()
    {
        if (CanEditLoadout) RefreshSidebarLoadout();
    }

    /// <summary>An empty "+" pocket tile was clicked in the sidebar: take the player shopping —
    /// bring the open Warren forward on the right tab (no-op mid-run, when no Warren exists).</summary>
    public void OpenWarrenAt(string tab) => ChaosHubWindow.Current?.NavigateTo(tab);

    /// <summary>The sidebar's pre-run ✖: leave the rabbit hole — close the Warren, which folds
    /// the sidebar away with it (no-op once a run is live; the pause flow owns exits then).</summary>
    public void CloseWarrenPhase()
    {
        if (_active) return;
        if (ChaosHubWindow.Current != null) ChaosHubWindow.Current.Close();
        else CloseLoadoutSidebar();
    }

    /// <summary>The sidebar's FALL IN hero button: start the descent. Goes through the open
    /// Warren when there is one (so the run setup saves), else starts directly.</summary>
    public void StartRunFromSidebar()
    {
        if (_active) return;
        if (ChaosHubWindow.Current != null) ChaosHubWindow.Current.FallIn();
        else StartRun();
    }

    /// <summary>Populate the HUD's pocket loadout — equipped lifetime boons in slot order, with dim
    /// empty-slot tiles for unfilled pockets (the pre-run glance) — plus the passive modifier list
    /// (owned always-on upgrades shaping this run).</summary>
    private void RefreshSidebarLoadout()
    {
        var st = _state ?? _preState;   // live run, or the Warren-phase preview state
        if (st == null) return;
        st.ActiveSidebarToys.Clear();
        st.ActiveSidebarAccessories.Clear();
        foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory })
        {
            var group = cat == ChaosBoonCategory.Skill ? st.ActiveSidebarToys : st.ActiveSidebarAccessories;
            foreach (var b in ChaosLifetimeBoons.InCategory(cat))
            {
                if (!ChaosMeta.IsBoonActive(b.Id)) continue;
                int lvl = ChaosMeta.BoonLevel(b.Id);
                group.Add(new ChaosSidebarBoon
                {
                    Id = b.Id,
                    Icon = ChaosArt.Resolve("boons", b.Id),
                    Glyph = b.Glyph,
                    Name = b.Name,
                    Level = lvl,
                    Desc = b.Desc,
                    Flavor = b.Flavor,
                    Extra = lvl >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc) ? "max: " + b.CapstoneDesc : "",
                });
            }
            // Empty "+" slots only exist during the Warren-phase glance (clicking one jumps the
            // Warren to Enhancements). Mid-run the loadout is locked — don't render dead tiles.
            if (_preHud != null)
                for (int i = group.Count; i < ChaosMeta.SlotsFor(cat); i++)
                    group.Add(new ChaosSidebarBoon
                    {
                        IsEmptySlot = true,
                        Glyph = "+",
                        Name = cat == ChaosBoonCategory.Skill ? "empty toy pocket" : "empty accessory pocket",
                        Desc = "click to go shopping in the dollhouse.",
                    });
        }
        st.RunModifiers.Clear();
        foreach (var id in ChaosMeta.State.PurchasedUpgrades)
        {
            if (!ChaosMeta.IsUpgradeActive(id)) continue;   // switched-off habits sit out the run
            var u = ChaosUpgrades.ById(id);
            if (u == null) continue;
            st.RunModifiers.Add(new ChaosSidebarBoon
            {
                Icon = u.IconPath != null ? ChaosArt.TryLoad(u.IconPath) : ChaosArt.Resolve("upgrades", u.Id),
                Glyph = u.Glyph,
                Name = u.Name,
                Desc = u.Desc,
                Flavor = u.Flavor,
                IsModifier = true,
            });
        }
        // Worn leveled habits (Utility lifetime boons — Rabbit's Foot etc.) are modifiers
        // too, not pocket gear: they list here beside the other always-on passives.
        foreach (var b in ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility))
        {
            if (!ChaosMeta.IsBoonActive(b.Id)) continue;
            int lvl = ChaosMeta.BoonLevel(b.Id);
            st.RunModifiers.Add(new ChaosSidebarBoon
            {
                Id = b.Id,
                Icon = ChaosArt.Resolve("boons", b.Id),
                Glyph = b.Glyph,
                Name = $"{b.Name} · L{lvl}",
                Level = lvl,
                Desc = b.Desc,
                Flavor = b.Flavor,
                Extra = lvl >= b.MaxLevel && !string.IsNullOrEmpty(b.CapstoneDesc) ? "max: " + b.CapstoneDesc : "",
                IsModifier = true,
            });
        }
    }

    /// <summary>
    /// Lift the whole gameplay layer back to the top of the topmost band (focus-free, so a
    /// playing video keeps rolling): live bubbles, every keep-alive overlay, then HUD + toy
    /// buttons on top. The keep-alive overlays must be listed explicitly — they only
    /// hide/unhide mid-run, and un-hiding doesn't re-stack a window, so without a kick they
    /// stay buried under the video. Called when a mandatory video starts mid-run AND on every
    /// click that lands on the video (VideoService.BringTargetsToFront): a click activates
    /// the video window at the Win32 level, which yanks it back above everything previously
    /// raised. No-op outside a run. UI thread only.
    /// </summary>
    public void RaiseGameLayerAboveVideo()
    {
        if (!_spawning) return;
        // Only re-raise while the layer is pinned. If the player opted out of pinning, re-raising
        // would fight them bringing a browser/work window forward.
        if (!ChaosWindowZ.PinTopmost) return;
        // Bottom of the gameplay band: ambient FX that read fine UNDER the bubbles.
        try { ChaosFieldFxOverlay.RaiseActive(); } catch { }
        try { ChaosSkiaFxOverlay.RaiseActive(); } catch { }
        // The shared bubble host (when enabled) sits just above the ambient FX, below the chrome.
        try { ChaosBubbleHostOverlay.RaiseActive(); } catch { }
        try { ChaosPopText.RaiseActive(); } catch { }
        try { ChaosDvdOverlay.RaiseActive(); } catch { }
        try { ChaosEffectBannerOverlay.RaiseActive(); } catch { }
        try { ChaosAnnouncerOverlay.RaiseActive(); } catch { }
        try { ChaosCursorGlowOverlay.RaiseActive(); } catch { }
        try { ChaosEStimOverlay.RaiseActive(); } catch { }
        try { ChaosVibeTrailOverlay.RaiseActive(); } catch { }
        // Run chrome sits BELOW the bubbles so the player can always pop what's drifting over the
        // sidebar / boon ribbon / active-skill buttons instead of the chrome stealing the click.
        try { _fx?.RaiseToTopmost(); } catch { }
        try { ChaosWaveTimerOverlay.RaiseActive(); } catch { }
        try { ChaosBoonBarOverlay.RaiseActive(); } catch { }
        try { _hud?.RaiseToTopmost(); } catch { }
        foreach (var b in _toyButtons) { try { b.RaiseToTopmost(); } catch { } }
        // Bubbles ride ABOVE the chrome...
        App.Bubbles?.BringAllToFront();
        // ...and the big attention assets (gif cascades + flashes) ride ABOVE the bubbles.
        try { ChaosGifCascadeOverlay.RaiseActive(); } catch { }
        try { ChaosFlashOverlay.RaiseActive(); } catch { }
        try { App.Flash?.RaiseAllToFront(); } catch { }
    }

    /// <summary>
    /// Re-assert HWND_TOPMOST on the persistent run chrome (HUD/sidebar, wave clock, boon bar,
    /// active-skill toy buttons) so it never sinks behind a foreground or fullscreen window
    /// mid-run. Then re-stack the bubbles ABOVE the chrome and the attention assets (gif cascades +
    /// flashes) above THEM — the chrome raise would otherwise bury the bubbles every tick, the
    /// "bubbles spawn behind the sidebar/boons" report. The re-stack is a handful of focus-free
    /// SetWindowPos calls (no layered-window churn), so it's cheap to run every ~1s. Called
    /// throttled (~1s) from <see cref="RunTick"/>. UI thread only; each call no-ops if a window
    /// isn't up.
    /// </summary>
    private void KeepChromeTopmost()
    {
        if (!_spawning) return;
        // If the player opted out of pinning, skip the topmost re-assertion entirely (this also
        // avoids BringAllToFront yanking the bubbles back over the foreground app).
        if (!ChaosWindowZ.PinTopmost) return;
        // Chrome first (lowest of the pinned set).
        try { _hud?.RaiseToTopmost(); } catch { }
        try { _fx?.RaiseToTopmost(); } catch { }
        try { ChaosWaveTimerOverlay.RaiseActive(); } catch { }
        try { ChaosBoonBarOverlay.RaiseActive(); } catch { }
        foreach (var b in _toyButtons) { try { b.RaiseToTopmost(); } catch { } }
        // Bubbles above the chrome so they stay poppable over the sidebar / boons / active buttons.
        App.Bubbles?.BringAllToFront();
        // Attention assets above the bubbles.
        try { ChaosGifCascadeOverlay.RaiseActive(); } catch { }
        try { ChaosFlashOverlay.RaiseActive(); } catch { }
        try { App.Flash?.RaiseAllToFront(); } catch { }
    }

    /// <summary>
    /// A chaos payload can fire a mandatory video mid-run. The video is a freshly-raised
    /// fullscreen topmost window that lands on top of the run's bubbles + HUD + FX. Lift the
    /// gameplay layer back ABOVE it (focus-free, so the video keeps playing) — the video ends up
    /// UNDER the assets but over everything else, and the player keeps popping. Re-kick once after
    /// the video's maximize/activate settles (mirrors VideoService's 50ms attention-target kick).
    /// New bubbles spawned during the video already land on top naturally (fresh topmost windows).
    /// </summary>
    private void OnVideoStartedDuringRun(object? sender, EventArgs e)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        // A fullscreen video fully covers the tunnel — pause its render loop (0% tunnel GPU).
        try { disp.BeginInvoke((Action)(() => ChaosTunnelService.SetVideoPlaying(true))); } catch { }
        try { disp.BeginInvoke((Action)RaiseGameLayerAboveVideo); } catch { }
        System.Threading.Tasks.Task.Delay(60).ContinueWith(_ =>
        {
            if (Application.Current?.Dispatcher == null) return;
            try { Application.Current.Dispatcher.BeginInvoke((Action)RaiseGameLayerAboveVideo); }
            catch (Exception ex) { App.Logger?.Debug("Chaos raise-above-video kick: {E}", ex.Message); }
        });
    }

    // ============================ run loop ============================

    /// <summary>
    /// One memory snapshot to the app log (gated on <c>ChaosMemTelemetry</c>) AND a refresh of the
    /// crash sentinel. Native~ = workingSet - managed; if it climbs run-over-run while managed stays
    /// flat, the random mid-play crash is the residual native OOM. If native stays flat, the vanish is
    /// an access violation (prime suspect: the Skia FX raster layer) — flip Enhanced FX off to confirm.
    /// </summary>
    private void LogMemSample(string phase)
    {
        try
        {
            var proc = System.Diagnostics.Process.GetCurrentProcess();
            long wsMb = proc.WorkingSet64 / (1024 * 1024);
            long privMb = proc.PrivateMemorySize64 / (1024 * 1024);
            long managedMb = GC.GetTotalMemory(false) / (1024 * 1024);
            long nativeMb = Math.Max(0, wsMb - managedMb);
            if (nativeMb > _peakNativeMb) _peakNativeMb = nativeMb;
            int bubbles = App.Bubbles?.ActiveBubbles ?? 0;
            double elapsed = _state?.ElapsedSec ?? 0;

            long gifDecodes = App.Flash?.GifDecodes ?? 0;
            long staticDecodes = App.Flash?.StaticDecodes ?? 0;

            if (App.Settings?.Current?.ChaosMemTelemetry == true)
                App.Logger?.Information(
                    "[CHAOSMEM] {Phase} t={Elapsed:F0}s ws={Ws}MB priv={Priv}MB managed={Managed}MB native~={Native}MB peakNative={Peak}MB bubbles={Bubbles} gifDecodes={Gif} staticDecodes={Static} skiaFx={Skia}",
                    phase, elapsed, wsMb, privMb, managedMb, nativeMb, _peakNativeMb, bubbles,
                    gifDecodes, staticDecodes, App.Settings?.Current?.ChaosSkiaFxEnabled);

            // Refresh the dirty-shutdown sentinel so a native vanish self-reports this run's peak
            // native memory + how far it got + which FX path was live.
            ChaosCrashSentinel.Mark(BuildSentinelContext(elapsed, bubbles));
        }
        catch { }
    }

    private string BuildSentinelContext(double elapsed, int bubbles)
    {
        int monitors = 1;
        try { monitors = System.Windows.Forms.Screen.AllScreens.Length; } catch { }
        var s = App.Settings?.Current;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "v{0} mode={1} skiaFx={2} pinTop={3} difficulty={4} monitors={5} elapsed={6:F0}s peakNative={7}MB bubbles={8}",
            Services.UpdateService.AppVersion, ActiveMode, s?.ChaosSkiaFxEnabled, s?.ChaosPinOnTop,
            _state?.Config?.Difficulty, monitors, elapsed, _peakNativeMb, bubbles);
    }

    /// <summary>Per-frame render-gap detector (gated on ChaosMemTelemetry). CompositionTarget.Rendering
    /// fires once per composed frame; a gap well over the ~16ms budget means the single WPF render thread
    /// stalled — a dropped frame felt by EVERY window (avatar, cursor glow, bouncing text) at once. Logs
    /// the gap + live bubble count so we can correlate hitches with spawns/density before optimizing.</summary>
    private void OnChaosRendering(object? sender, EventArgs e)
    {
        if (e is not System.Windows.Media.RenderingEventArgs re) return;
        var ts = re.RenderingTime;
        if (_lastRenderTs != TimeSpan.MinValue)
        {
            double gapMs = (ts - _lastRenderTs).TotalMilliseconds;
            if (gapMs > 40.0)
            {
                // Governor input runs regardless of the telemetry flag (the flag only gates the
                // log line). A real stall (>120ms) weighs triple — one of those is a felt freeze.
                _hitchScore += gapMs >= 120.0 ? 3.0 : 1.0;
                if (App.Settings?.Current?.ChaosMemTelemetry == true)
                {
                    _hitchCount++;
                    App.Logger?.Information("[CHAOSHITCH] frame gap {Gap:F0}ms bubbles={Bubbles} (#{N} this run)",
                        gapMs, App.Bubbles?.ActiveBubbles ?? 0, _hitchCount);
                }
            }
        }
        _lastRenderTs = ts;
    }

    /// <summary>Perf governor (4x/s from RunTick): decay the hitch pressure, then set the spawn
    /// backoff — degrade INSTANTLY when hitches pile up, ease back to full over ~5s of clean
    /// frames. The backoff multiplies the refill interval in <c>SpawnTick</c>'s cadence math, so
    /// an overwhelmed render thread gets a thinner field instead of a frozen one. Sized so a
    /// single stray hitch (score 1-3) changes nothing.</summary>
    private void UpdatePerfGovernor()
    {
        _hitchScore = Math.Max(0, _hitchScore * 0.93);   // ~halves every 2.3s
        double target = _hitchScore >= 12 ? 2.0 : _hitchScore >= 5 ? 1.5 : 1.0;
        if (target > _perfBackoff)
        {
            _perfBackoff = target;
            App.Logger?.Information("[CHAOSPERF] render thread behind (hitch score {S:F1}) — spawn cadence x{B:F1}, bubbles={Bubbles}",
                _hitchScore, _perfBackoff, App.Bubbles?.ActiveBubbles ?? 0);
        }
        else if (target < _perfBackoff)
        {
            _perfBackoff = Math.Max(target, _perfBackoff - 0.05);
            if (_perfBackoff <= 1.0)
                App.Logger?.Information("[CHAOSPERF] recovered — spawn cadence back to full");
        }
    }

    private void RunTick(object? sender, EventArgs e)
    {
        // Governor housekeeping runs even while paused, so the backoff relaxes during a draft/pause.
        UpdatePerfGovernor();
        if (!_spawning || _state == null || _paused || _manualPaused) return;

        // Keep the run chrome (HUD/sidebar, clock, boon bar, active-skill buttons) pinned above
        // whatever grabs the foreground mid-run — other apps AND fullscreen surfaces, not just the
        // mandatory-video case RaiseGameLayerAboveVideo already covers. A Topmost window still sinks
        // under another process's topmost/fullscreen window, so we re-kick HWND_TOPMOST (focus-free,
        // cheap) ~once a second. Throttled so it never churns z-order or fights clicks every frame.
        if (++_chromeRaiseTick >= 4)
        {
            _chromeRaiseTick = 0;
            KeepChromeTopmost();
            // While a mandatory video is on screen, re-lift the WHOLE game layer above it ~once a
            // second — not just the chrome. The game layer is raised above the video once at video
            // start, but anything that disturbs the topmost band afterwards (a full-screen SUBLIMINAL
            // re-asserting HWND_TOPMOST on each show, a video attention-kick, a stray click) can leave
            // the video sitting over the bubbles, and nothing re-lifts them until new bubbles spawn —
            // the "video pops over the bubbles, then they come back" report. Gated to video-playing so
            // a normal run never pays this z-order churn.
            if (App.Video?.IsPlaying == true) RaiseGameLayerAboveVideo();
        }

        double dt = 0.25;
        double elapsed = _state.ElapsedSec + dt;
        _state.ElapsedSec = elapsed;
        _state.Heat = Math.Max(0, _state.Heat - 0.0015);
        UpdateHeatTint();

        // Crash telemetry + sentinel refresh: ~every 15s (RunTick fires 4x/s → 60 ticks).
        if (++_memSampleTick >= 60) { _memSampleTick = 0; LogMemSample("tick"); }

        // Focus-bar hover cue: while the hand rests on a live bubble, the bar brightens —
        // "check your fuel first" lands at the exact moment of the decision. 4x/s poll.
        _hud?.SetCursorOnLive(App.Bubbles?.IsCursorOverLiveChaosBubble() == true);

        // Power-ups run on the real clock (so they don't extend the run length).
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

        // Empty-field rescue: a fast clear shouldn't leave dead air until the spawn timer's
        // next beat (gaps run ~1.3s early and slow-mo stretches them ~8x). The moment the
        // field is bare while spawning is live, pull the next spawn forward — SpawnTick
        // re-arms its own interval, so the cadence resumes cleanly from here.
        if (_spawning && _freezeRemainingSec <= 0 && (App.Bubbles?.ActiveBubbles ?? 1) == 0)
            SpawnTick(null, EventArgs.Empty);

        // the Ripple: gather the next charge (a soft ready cue the moment it lands).
        if (_state.RippleCooldown > 0)
        {
            _state.RippleCooldown -= dt;
            if (_state.RippleReady) ChaosSfx.Play("toy_ready", 0.3f);
        }
        // The verb has no button to find — until the FIRST cast ever lands, the ready charge
        // offers it once per (non-scripted) run. FireRipple sets the flag for good. The few
        // seconds of air keep it clear of the GO/start-mantra announces.
        if (!ChaosMeta.State.SeenRippleTeach && !_rippleTeachOffered && elapsed > 6
            && _state.RippleReady && !_state.Config.ScriptedFirstRun)
        {
            _rippleTeachOffered = true;
            ChaosAnnouncerOverlay.Announce("🌊 THE RIPPLE — right-click near the bubbles", ChaosAnnounceKind.PowerUp,
                artKey: "ripple_teach", subText: "right-click near the bubbles", holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            _state.PushEvent("🌊 a wave waits in your hand. right-click.");
        }

        TickBlindfoldHeartbeat(dt);   // Blindfold capstone: the closest fuse gets a pulse
        TickActiveToys(dt);           // toy cooldowns + the VibePopping buzz window
        ChaosLessonHooks.SampleCursor();   // the_pull lesson: cheap, self-disabling once learned
        ChaosHappyPath.Tick(dt);           // happy path: scripted teach beats (no-op past run 2)

        // Once-ever gentle teach the FIRST time focus dips under a snap's price, before the
        // harsh NO FOCUS lesson ever gets the chance: how focus refills, what it buys.
        if (!ChaosMeta.State.SeenFocusTip && _spawning && _state.Focus < ChaosTuning.DEFUSE_COST)
        {
            ChaosMeta.State.SeenFocusTip = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("focus runs low. treats refill it. snaps spend it.", ChaosAnnounceKind.Willpower, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            _state.PushEvent("◌ low focus. pop treats before you grab a live one.");
        }

        // Once-ever heat teach: the first time the burn visibly climbs, name the orange bar —
        // otherwise "lust" only ever explains itself in a hover tooltip.
        if (!ChaosMeta.State.SeenHeatTeach && _state.Heat >= 0.15)
        {
            ChaosMeta.State.SeenHeatTeach = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("lust climbs while you perform. it pays up to x2", ChaosAnnounceKind.Depth, holdMs: ChaosAnnouncerOverlay.TEACH_HOLD_MS);
            _state.PushEvent("🔥 the orange bar is lust. a trigger cools it to zero.");
        }

        // rh_focus_low: focus has sat below a defuse's price while live threats hang on screen.
        // Once per run — a nudge toward farming treats, not a nag.
        if (!_focusLowBarkFired)
        {
            if (_state.FocusLow && App.Bubbles?.MinChaosFuseSec() != null)
            {
                _focusLowAccumSec += dt;
                if (_focusLowAccumSec >= ChaosTuning.FOCUS_LOW_BARK_SEC)
                {
                    _focusLowBarkFired = true;
                    App.Bark?.NotifyChaosFocusLow();
                }
            }
            else _focusLowAccumSec = 0;
        }

        // Mandatory-video hard caps: a chaos-fired video never runs past its 15s slice, and
        // never into the run's final 3s (the recap should never land on top of a playing tape).
        if (_chaosVideoCapUtc != DateTime.MinValue)
        {
            bool capHit = DateTime.UtcNow >= _chaosVideoCapUtc;
            bool runClosing = _state.RunDurationSec - elapsed <= 3;
            if (App.Video?.IsPlaying == true && (capHit || runClosing))
            {
                _chaosVideoCapUtc = DateTime.MinValue;
                try { App.Video?.ForceCleanup(); } catch (Exception ex) { App.Logger?.Debug("Chaos video cap: {E}", ex.Message); }
                ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);   // ForceCleanup may not raise VideoEnded
                _state.PushEvent("▶ the tape snaps off");
                // porn_dvd lesson: the full slice ran (the 15s cap IS the slice length);
                // a run-closing cut before the cap is an abort and doesn't count.
                if (capHit) ChaosLessonHooks.OnVideoEndured();
            }
            else if (App.Video?.IsPlaying != true && capHit)
            {
                _chaosVideoCapUtc = DateTime.MinValue;   // ended on its own — clear the cap
                ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);
                ChaosLessonHooks.OnVideoEndured();   // porn_dvd lesson: endured to its natural end
            }
        }

        // T-10s: the hole is closing — one varied voiced bark + a quiet announce, so the run
        // gets an ending instead of an interruption. Once per run; a Relapse extension that
        // pushes the clock back out doesn't re-arm it (Relapse announces itself).
        if (!_endingSoonFired && _state.RunDurationSec - elapsed <= 10)
        {
            _endingSoonFired = true;
            ChaosAnnouncerOverlay.Announce("the hole is closing…", ChaosAnnounceKind.Depth,
                artKey: "ending_soon", subText: "ten seconds");
            _state.PushEvent("⏳ ten seconds. make them count.");
            App.Bark?.NotifyChaosEndingSoon();
        }

        if (elapsed >= _state.RunDurationSec)
        {
            // Relapse sin: the hole isn't done with you — one more loop, paying double drops + gold.
            if (_state.RelapseLoopArmed && !_state.RelapseLoopActive)
            {
                _state.ExtendOneLoop();
                ChaosAnnouncerOverlay.Announce("☠ RELAPSE — one more loop", ChaosAnnounceKind.Temptation,
                    artKey: "relapse", subText: "one more loop");
                ChaosSfx.Play("sin_accept", 0.6f);
                _state.PushEvent("☠ relapse. one more loop — everything drips double");
                App.Bark?.NotifyChaosWaveEscalated(_state.WaveIndex + 1);
            }
            else { EndRun(); return; }
        }

        double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
        int newWave = Math.Min(_state.WaveCount, 1 + (int)(elapsed / waveLen));
        _state.WaveProgress = (elapsed % waveLen) / waveLen;

        // Pocket Watch: the wave countdown at the top of the screen (+ a live score line).
        if (_state.ShowWaveTimer)
            ChaosWaveTimerOverlay.Update(newWave, _state.WaveCount, waveLen - (elapsed % waveLen), _state.Score);

        if (newWave > _state.WaveIndex) BeginWaveTransition(newWave);
    }

    private void SpawnTick(object? sender, EventArgs e)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;
        if (_freezeRemainingSec > 0) return;   // time is frozen: hold the field, spawn nothing new

        var cfg = _state.Config;
        double intensity = _state.RunIntensity;
        double effIntensity = Math.Clamp(intensity + (cfg.DifficultyMult - 1.0) * 0.15, 0, 1);
        double diffFactor = cfg.DifficultyMult;

        // Field density: power-ups (ripple/freeze/chains/sweeps) clear the screen in bulk, so the
        // cap has to be high enough that the field refills instead of sitting empty. 6 early → 16
        // late (×√difficulty). Bumped 2026-06-13 from 4→11 — runs played "always cleared".
        int maxConcurrent = (int)Math.Round((6 + intensity * 10) * Math.Sqrt(diffFactor));
        // Behavioral bubbles (Echo/Chaperone/Tease/Bound): each rolls to REPLACE this ordinary
        // spawn slot, so the field density stays the same. Darters still roll below either way.
        bool behavioralSpawned = (App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent
                                 && TrySpawnBehavioralBubble(cfg, effIntensity);
        if (!behavioralSpawned && (App.Bubbles?.ActiveBubbles ?? 0) < maxConcurrent)
        {
            // Be gentle with the tape: no video bubble while a heavy effect (video/cascade) is
            // running, and none when the loop or run is too close to its end for the bubble's
            // fuse plus the 15s video slice to fit.
            IReadOnlyCollection<string>? enabled = cfg.EnabledVariants;
            double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
            double waveLeft = waveLen - (_state.ElapsedSec % waveLen);
            double runLeft = _state.RunDurationSec - _state.ElapsedSec;
            if (enabled != null && enabled.Contains("video")
                && (HeavyEffectActive || waveLeft < 14 || runLeft < 18))
            {
                enabled = enabled.Where(id => id != "video").ToList();
            }

            // Heavy Drop: every Nth ordinary spawn swaps for a giant, slow, triple-pay treat.
            EffectBubbleSpec spec;
            if (_state.HeavyDropEvery > 0 && ++_spawnSerial % _state.HeavyDropEvery == 0)
            {
                spec = ChaosBubbleVariants.BuildHeavy(effIntensity, cfg.EffectIntensity, _state.BubbleScale);
            }
            else
            {
                // Side entries arm only after the first few spawns — the classic bottom rise
                // opens the run, then the field starts coming at you sideways too.
                double sideDrift = _ordinarySpawns < ChaosTuning.SIDE_DRIFT_GRACE_SPAWNS
                    ? 0 : ChaosTuning.SIDE_DRIFT_CHANCE;
                spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult,
                    cfg.MotionOverride, enabled, cfg.EffectIntensity, _state.BubbleScale, sideDrift);

                // Freeze cap: at most FREEZE_MAX_ON_SCREEN freeze pickups live at once — if the
                // roll landed on a freeze while the field is already at the cap, re-pick from the
                // pool with freeze excluded so the slot still spawns something.
                if (spec.IsFreeze
                    && (App.Bubbles?.ActiveFreezeBubbles ?? 0) >= ChaosTuning.FREEZE_MAX_ON_SCREEN)
                {
                    var noFreeze = (enabled ?? ChaosBubbleVariants.AllIds())
                        .Where(id => id != "bambifreeze").ToList();
                    spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult,
                        cfg.MotionOverride, noFreeze, cfg.EffectIntensity, _state.BubbleScale, sideDrift);
                }
            }
            _ordinarySpawns++;
            ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
            App.Bubbles?.SpawnChaosBubble(spec);

            // Lucky golden bubble: a rare bonus roll riding every ordinary spawn (base 0.5%;
            // Rabbit's Foot raises it). Pays real gold — Sparks — the moment it's popped.
            if (Random.Shared.NextDouble() < _state.GoldenChance)
            {
                ChaosMeta.MarkDiscovered("bubble:golden");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGolden());
                App.Bubbles?.PlayChime(0.30f);   // a soft chime so a sharp ear catches the chance
            }

            // "Look at the bright colors..." sin: sometimes a mimic prism drifts in.
            if (_state.PrismChance > 0 && Random.Shared.NextDouble() < _state.PrismChance)
            {
                ChaosMeta.MarkDiscovered("bubble:prism");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildPrism(effIntensity, cfg.EffectIntensity,
                    treatOnly: _state.PrismTreatOnly));
            }

            // The Brittle (Tempted+, half odds on Gentle — same rank-not-difficulty rule as
            // the menagerie): a glass mine rides in alongside the field — the cursor merely
            // brushing it shatters it and a random live effect fires.
            if (ChaosMeta.AtLeast(ChaosRank.Tempted)
                && Random.Shared.NextDouble() < ChaosTuning.BRITTLE_SPAWN_CHANCE
                    * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0))
            {
                if (!ChaosMeta.State.SeenBrittle)
                {
                    ChaosMeta.State.SeenBrittle = true; ChaosMeta.Save();
                    ChaosAnnouncerOverlay.Announce("◇ THE BRITTLE — don't even hover", ChaosAnnounceKind.Temptation,
                        artKey: "brittle", subText: "don't even hover");
                    _state.PushEvent("◇ thin glass drifts in. steer around it.");
                }
                ChaosMeta.MarkDiscovered("bubble:brittle");
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildBrittle(effIntensity,
                    cfg.EffectIntensity, _state.BubbleScale));
            }
        }

        // Darters spawn on their own intensity-scaled roll, independent of the bubble cap.
        // (Tunnel Vision habit cut 2026-06-10 — the spotlight viz lives on in the variant builder.)
        if (cfg.DartersEnabled)
        {
            var darter = ChaosBubbleVariants.RollDarter(effIntensity, _state.RabbitRateMult, spotlight: false);
            if (darter != null)
            {
                ChaosMeta.MarkDiscovered("bubble:darter");
                App.Bubbles?.SpawnChaosBubble(darter);
            }
        }

        // Refill cadence: 1000ms early → 320ms late (÷difficulty), steeper late ramp so the field
        // gets "progressively faster" as the run deepens. Quickened 2026-06-13 from 1300→450 to keep
        // the bigger cap actually filled against bulk-clearing power-ups.
        double interval = (1000 - intensity * 680) / diffFactor;
        // SpawnRateMult scales SPAWNS, so it divides the interval: 0.6 rate = fewer
        // spawns = a LONGER gap between ticks (the scripted run 1 breathes at ~0.6).
        interval /= Math.Clamp(cfg.SpawnRateMult, 0.1, 10.0);
        if (_slowMoRemainingSec > 0) interval /= SLOWMO_FACTOR;   // slow-mo stretches the spawn cadence
        // Perf governor: while the render thread is dropping frames, stretch the refill cadence.
        // Applied AFTER the floor-feeding math so a hitching late run can breathe above 280ms.
        interval = Math.Max(280, interval) * _perfBackoff;
        _spawnTimer!.Interval = TimeSpan.FromMilliseconds(interval);
    }

    // ============================ behavioral bubbles (Echo / Chaperone / Tease / Bound) ============================

    /// <summary>
    /// Roll the behavioral bubbles for this spawn slot. A hit REPLACES the ordinary spawn
    /// (density stays sane; a debut also consumes the tick → it spawns alone). Gating is by
    /// RANK, not difficulty (2026-06-12 — the old hard Gentle return meant default-settings
    /// players never met five diary entries): Echo + Chaperone from Tempted, Tease from
    /// Slipping, Bound from Entranced (or any Relentless+ descent, where it always belonged).
    /// Gentle stays gentle by halving every roll instead of forbidding the menagerie outright.
    /// Debuts get a gentler trance and announce themselves.
    /// </summary>
    private bool TrySpawnBehavioralBubble(ChaosRunConfig cfg, double effIntensity)
    {
        if (_state == null) return false;
        if (cfg.ScriptedFirstRun) return false;   // run 1 is scripted: no behavioral bubbles at all
        double gentleMult = cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0;

        // The Echo (Tempted+): trigger it and it multiplies; only the held defuse is clean.
        if (ChaosMeta.AtLeast(ChaosRank.Tempted)
            && Random.Shared.NextDouble() < ChaosTuning.ECHO_SPAWN_CHANCE * gentleMult)
        {
            bool debut = !ChaosMeta.State.SeenEcho;
            if (debut)
            {
                ChaosMeta.State.SeenEcho = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("◌ THE ECHO — hold it down, or it multiplies", ChaosAnnounceKind.Item,
                    artKey: "echo", subText: "hold it down, or it multiplies");
                _state.PushEvent("◌ something doubled stirs below");
            }
            ChaosMeta.MarkDiscovered("bubble:echo");
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildEcho(effIntensity, _state.FuseTimeMult,
                _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0));
            return true;
        }

        // The Chaperone (Tempted+): shielded while its escort circles — pop the escort first.
        if (ChaosMeta.AtLeast(ChaosRank.Tempted)
            && Random.Shared.NextDouble() < ChaosTuning.CHAPERONE_SPAWN_CHANCE * gentleMult)
        {
            bool debut = !ChaosMeta.State.SeenChaperone;
            if (debut)
            {
                ChaosMeta.State.SeenChaperone = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("💞 THE CHAPERONE — its little escort first", ChaosAnnounceKind.Item,
                    artKey: "chaperone", subText: "its little escort first");
                _state.PushEvent("💞 it brought company");
            }
            var (live, escort) = ChaosBubbleVariants.BuildChaperonePair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
            ChaosMeta.MarkDiscovered("bubble:chaperone");
            App.Bubbles?.SpawnChaosChaperone(live, escort);
            return true;
        }

        // The Bound (Relentless+ descents, or the Entranced rank on any difficulty):
        // two lives, one thread — both must come down quickly.
        if ((cfg.Difficulty >= ChaosDifficulty.Hard || ChaosMeta.AtLeast(ChaosRank.Entranced))
            && Random.Shared.NextDouble() < ChaosTuning.BOUND_SPAWN_CHANCE * gentleMult)
        {
            bool debut = !ChaosMeta.State.SeenBound;
            if (debut)
            {
                ChaosMeta.State.SeenBound = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("⛓ THE BOUND — both, and quickly", ChaosAnnounceKind.Item,
                    artKey: "bound", subText: "both, and quickly");
                _state.PushEvent("⛓ two of them, one thread");
            }
            var (a, b) = ChaosBubbleVariants.BuildBoundPair(effIntensity, _state.FuseTimeMult,
                cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
            ChaosMeta.MarkDiscovered("bubble:bound");
            App.Bubbles?.SpawnChaosBoundPair(a, b);
            return true;
        }

        // The Tease (Slipping rank): the one you beat by NOT touching it.
        if (ChaosMeta.AtLeast(ChaosRank.Slipping)
            && Random.Shared.NextDouble() < ChaosTuning.TEASE_SPAWN_CHANCE * gentleMult)
        {
            bool debut = !ChaosMeta.State.SeenTease;
            if (debut)
            {
                ChaosMeta.State.SeenTease = true; ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce("✖ THE TEASE — whatever you do, don't", ChaosAnnounceKind.Temptation,
                    artKey: "tease", subText: "whatever you do, don't");
                _state.PushEvent("✖ it wants your hand. don't.");
                App.Bark?.NotifyChaosTeaseDebut();
            }
            ChaosMeta.MarkDiscovered("bubble:tease");
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildTease(effIntensity,
                cfg.EffectIntensity, _state.BubbleScale));
            return true;
        }

        return false;
    }

    // ---- The Tease: touch + denial outcomes ----
    private int _teaseDeniedThisRun;
    private bool _teaseDeniedStreakBarked;

    /// <summary>A mouse-down landed on a Tease: its payload fires (resistance can absorb THAT,
    /// nothing else) and the streak HALVES no matter what — that's the price of touching.</summary>
    private void OnTeaseTouched(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Detonated++;
        _runDetonations++;
        _waveDetonations++;
        ChaosLessonHooks.OnDetonation();   // silk_touch: a touched Tease dirties the loop too
        double s = spec.Strength / 100.0;

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            // Resistance prevents only the payload — the streak still pays below.
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the sting ({spec.Payload.DisplayName})");
        }
        else
        {
            FirePayloadForDetonation(spec);
            _state.EffectsFired++;
            ChaosSfx.Play("trigger", 0.55f);
            Shake(0.3 + s * 0.4, 320);
            _hud?.FlashShields(false);   // no resistance to soak the touch
        }

        _state.Combo = _state.Combo > 1 ? _state.Combo / 2 : 0;
        _lastComboBigFired = 0;
        _state.PushEvent($"✖ you touched it. it laughs — streak halves to x{_state.Combo}");
        Pulse(Color.FromRgb(0xFF, 0x3D, 0x5A), 0.38);
        App.Bark?.NotifyChaosTeaseClicked();
    }

    /// <summary>The Brittle shattered — the cursor brushed (or pressed) the glass. The mimic's
    /// live effect fires; resistance can absorb the payload (a strayed hand is an accident,
    /// like touching the Tease) but unlike the Tease the streak is spared — the effect itself
    /// is the whole price. Never counts as a missed trance.</summary>
    private void OnBrittleShattered(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        ChaosSfx.Play(ChaosSfx.ResolvePath("glass_shatter").Length > 0 ? "glass_shatter" : "trigger", 0.55f);

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            _state.PushEvent($"♥ resistance takes the shards ({spec.Payload.DisplayName})");
        }
        else
        {
            FirePayloadForDetonation(spec);
            _state.EffectsFired++;
            double s = spec.Strength / 100.0;
            Shake(0.25 + s * 0.35, 300);
            _hud?.FlashShields(false);   // no resistance to soak the shards
            _state.PushEvent($"◇ it shatters — {spec.Payload.DisplayName} was inside");
        }
        Pulse(Color.FromRgb(0xBF, 0xE6, 0xFF), 0.32);
    }

    /// <summary>A Bound survivor's tether snapped (window lapsed or its partner triggered) —
    /// it enraged: half the trance left, half again the speed. The juice lives here.</summary>
    private void OnBoundEnraged(EffectBubbleSpec spec)
    {
        if (_state == null) return;
        ChaosSfx.Play("toy_denied", 0.5f);   // a sharp denial sting until a dedicated cue ships
        Pulse(Color.FromRgb(0xFF, 0x4A, 0x4A), 0.30);
        _state.PushEvent("⛓ the tether snaps — it enrages");
    }

    /// <summary>The Tease expired untouched: restraint pays — gold, score AND focus.</summary>
    private void OnTeaseDenied(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        int gold = GoldScaled(Random.Shared.Next(ChaosTuning.TEASE_GOLD_MIN, ChaosTuning.TEASE_GOLD_MAX + 1));
        BankGold(gold);
        double pts = ChaosTuning.TEASE_DENIED_SCORE * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        _state.Focus += ChaosTuning.FOCUS_PER_DENIED;   // restraint feeds focus like a treat would
        ShowPopScore(pts);
        _state.PushEvent($"{ChaosGlyphs.Gold} denied. it pays +{gold} gold");
        ChaosAnnouncerOverlay.Announce($"DENIED. +{gold} {ChaosGlyphs.Gold} gold", ChaosAnnounceKind.PowerUp,
            artKey: "denied", subText: $"+{gold} {ChaosGlyphs.Gold} gold");
        Pulse(Color.FromRgb(0xFF, 0xD7, 0x00), 0.25);
        App.Bark?.NotifyChaosTeaseDenied(++_teaseDeniedThisRun);
        if (_teaseDeniedThisRun >= ChaosTuning.TEASE_DENIED_STREAK_COUNT && !_teaseDeniedStreakBarked)
        {
            _teaseDeniedStreakBarked = true;
            App.Bark?.NotifyChaosTeaseDeniedStreak(_teaseDeniedThisRun);
        }
    }

    /// <summary>The Echo triggered: NO payload — two smaller, faster lives burst out at its
    /// spot instead. Children are ordinary bubbles (light live trio) and never re-split.</summary>
    private void SpawnEchoChildren(EffectBubbleSpec parent)
    {
        if (_state == null) return;
        for (int i = 0; i < 2; i++)
        {
            var child = ChaosBubbleVariants.BuildEchoChild(parent.SizePx,
                BubbleService.ChaosLastPopXPx + Random.Shared.Next(-70, 71),
                BubbleService.ChaosLastPopYPx + Random.Shared.Next(-50, 51),
                _state.Config.EffectIntensity);
            App.Bubbles?.SpawnChaosBubble(child);
        }
        _state.PushEvent("◌ it splits — two more");
        Pulse(Color.FromRgb(0xC9, 0xC4, 0xE8), 0.30);
    }

    private void BeginWaveTransition(int newWave)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnLoopCompleted();   // lessons: judge silk_touch, reset per-loop tallies
        AwardLoopTip();                       // her gold tip for the loop just finished

        // Drafts disabled → roll straight into the next wave with no pause.
        if (!_state.Config.BoonDraftEnabled)
        {
            _state.AllLiveNextWave = false;
            _state.WaveIndex = newWave;
            _state.ActIndex = 1 + (newWave - 1) / 5;
            App.Bark?.NotifyChaosWaveEscalated(newWave);
            FireActChangedIfCrossed();
            AnnounceFinalLoopIfEntering();
            return;
        }

        _paused = true;
        _spawnTimer?.Stop();
        ChaosWaveTimerOverlay.Clear();   // the watch blanks while the draft table is out
        // Clear the field so live fuses don't tick (and detonate) behind the draft overlay,
        // with a single burst cue as everything pops.
        App.Bubbles?.PopAllBubbles();
        ChaosSfx.PlayWaveClear();
        // Wave-clear juice (additive only): a soft screen pulse + a clear bark on the boundary.
        Pulse(WAVE_CLEAR_COLOR, WAVE_CLEAR_PULSE);
        App.Bark?.NotifyChaosWaveCleared(_state.WaveIndex);

        // Clear the finished wave's transient (next-wave) flags before drafting.
        _state.AllLiveNextWave = false;

        _pendingWave = newWave;
        App.Bark?.NotifyChaosWaveEscalated(newWave);

        // Surrender capstone: every draft carries a sin (only while the user allows sins at all).
        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices,
            guaranteeCurse: _state.MaxedBoons.Contains("surrender"), takenIds: TakenBoonIds(),
            sinChance: _state.Config.SinChance);
        ChaosHappyPath.RigDraft(options, _state);   // run-4 first sin / duo demo (first draft only)
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        _overlay?.ShowBoonDraft(_state.WaveIndex, options, OnBoonChosen, _state.Config.DraftAutoResumeSec,
            rerollsLeft: _state.RerollsLeft, onReroll: RerollDraft);
    }

    /// <summary>
    /// Happy path (run 1): the ONE scripted draft, fired mid-run by <see cref="ChaosHappyPath"/>
    /// while the config's draft flag is off. Same pause/clear/resume choreography as a wave
    /// draft, but the wave doesn't advance — the table simply interrupts the fall.
    /// </summary>
    internal bool TriggerScriptedDraft(System.Collections.Generic.List<ChaosBoon> options)
    {
        if (_state == null || !_spawning || _paused || _manualPaused) return false;
        if (options == null || options.Count == 0) return false;
        _paused = true;
        _spawnTimer?.Stop();
        ChaosWaveTimerOverlay.Clear();
        App.Bubbles?.PopAllBubbles();
        ChaosSfx.PlayWaveClear();
        _pendingWave = _state.WaveIndex;   // OnBoonChosen re-applies the SAME wave
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        _overlay?.ShowBoonDraft(_state.WaveIndex, options, OnBoonChosen, _state.Config.DraftAutoResumeSec);
        return true;
    }

    /// <summary>Run-boon ids already drafted this descent (unique cards sit the rest out).</summary>
    private System.Collections.Generic.HashSet<string> TakenBoonIds()
    {
        var taken = new System.Collections.Generic.HashSet<string>();
        if (_state == null) return taken;
        foreach (var b in _state.ActiveBoons) taken.Add(b.Id);
        foreach (var b in _state.ActiveCurses) taken.Add(b.Id);
        return taken;
    }

    /// <summary>Taking Chances: spend one reroll for a fresh deal at the draft table. Null = none left.</summary>
    private (System.Collections.Generic.List<ChaosBoon> options, int rerollsLeft)? RerollDraft()
    {
        if (_state == null || _state.RerollsLeft <= 0) return null;
        _state.RerollsLeft--;
        _state.PushEvent("🎲 tempted fate again");
        var options = ChaosBoonPool.Draft(_state.Config.AllowCurses, _state.Config.DraftChoices,
            guaranteeCurse: _state.MaxedBoons.Contains("surrender"), takenIds: TakenBoonIds(),
            sinChance: _state.Config.SinChance);
        foreach (var o in options) ChaosMeta.MarkDiscovered("boon:" + o.Id);
        return (options, _state.RerollsLeft);
    }

    private void OnBoonChosen(ChaosBoon? boon)
    {
        if (_state == null) return;
        ChaosHappyPath.OnDraftResolved();   // the run-4 first-sin beat is spent either way

        if (boon != null)
        {
            // Surrender capstone: the FIRST sin of the descent keeps its sweetness, loses its sting.
            bool sinShielded = boon.IsCurse && _state.MaxedBoons.Contains("surrender") && !_state.SurrenderShieldUsed;
            if (sinShielded) _state.SurrenderShieldUsed = true;
            // Happy path: the run-4 demo sin is rigged shielded once — its downside cannot fire.
            if (!sinShielded && boon.IsCurse && ChaosHappyPath.ShouldShieldSin(boon.Id)) sinShielded = true;

            // First-times: the first card ever taken, and the first sin ever accepted.
            if (!ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper);
            if (boon.IsCurse && !ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Yes);

            ChaosLessonHooks.OnDraftCardTaken(boon.IsCurse);   // draft4 (any card) + surrender (sins)
            _state.ApplyBoon(boon, shieldDrawback: sinShielded);
            if (boon.Id == "extra_shield") Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);   // +2 shields cue
            if (boon.IsCurse)
            {
                // Surrender: each accepted sin sweetens the multiplier; at max, saying yes gives back.
                if (_state.SinExtraMult > 0)
                {
                    _state.BoonMult += _state.SinExtraMult;
                    _state.PushEvent($"🕯 sin embraced (+{_state.SinExtraMult:0.00}x)");
                }
                if (sinShielded) _state.PushEvent("🕯 the candle took the sting (no drawback)");
                if (_state.MaxedBoons.Contains("surrender"))
                {
                    _state.Shields += 1;
                    _state.PushEvent("🕯 you said yes. it gave back (+1 resistance)");
                    Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);
                }
                App.Bark?.NotifyChaosCursePicked(boon.Name, boon.Rarity.ToString(), boon.RunMultBonus);
                ChaosSfx.Play("sin_accept", 0.6f);
                ChaosAnnouncerOverlay.Announce($"☠ {boon.Name}", ChaosAnnounceKind.Temptation, artKey: boon.Id);
                // Narrative: a sin accepted at the draft (suppressed during scripted descents incl. the run-4 rig).
                var sinCtx = BuildNarrativeCtx(); sinCtx.SinId = boon.Id;
                ChaosNarrativeHooks.OnMoment("sin_accepted", sinCtx);
            }
            else
            {
                App.Bark?.NotifyChaosBoonPicked(boon.Name);
                ChaosAnnouncerOverlay.Announce($"◈ {boon.Name}", ChaosAnnounceKind.Mantra, artKey: boon.Id);   // ◈ mantra mark (✦ is drops only)
            }
        }
        else
        {
            _state.Shields += 1;
            _state.PushEvent("♥ resisted → +1 resistance");
            Pulse(SHIELD_GAIN_COLOR, SHIELD_GAIN_PULSE);
            App.Bark?.NotifyChaosBoonSkipped(_state.Shields);
            ChaosAnnouncerOverlay.Announce("+1 RESISTANCE", ChaosAnnounceKind.Willpower, artKey: "resistance");
        }

        _state.WaveIndex = _pendingWave;
        _state.ActIndex = 1 + (_state.WaveIndex - 1) / 5;
        FireActChangedIfCrossed();

        // A brief "Ready? :3" → "GO!" beat (same flashing display as run start) before the next
        // loop resumes, so the pick lands with a moment to settle. Stays paused until GO.
        if (_overlay != null) _overlay.ShowReadyGo(ResumeAfterDraft);
        else ResumeAfterDraft();
    }

    /// <summary>Un-pause the field and restart spawns after the post-pick "Ready? → GO!" beat.</summary>
    private void ResumeAfterDraft()
    {
        // Lessons judged at the draft table deferred their cards to here: the card pause
        // takes the baton from the draft pause (_paused stays true, field stays held) and
        // the real resume — including the GO! beat's welcome shower — runs after the last
        // card is dismissed (see ResumeAfterLessonCard).
        if (_pendingLessonCards.Count > 0 && _spawning)
        {
            _lessonCardPaused = true;
            _lessonCardsAfterDraft = true;
            App.Bubbles?.SetChaosFrozen(true);
            App.Bubbles?.SetChaosInputLocked(true);
            ChaosSfx.Play("ui_unlock", 0.55f);
            foreach (var card in _pendingLessonCards)
                ChaosUnlockCardOverlay.Show(card, onDismissed: ResumeAfterLessonCard, autoDismiss: false);
            _pendingLessonCards.Clear();
            return;
        }

        _paused = false;
        if (_spawning) _spawnTimer?.Start();
        // Welcome Shower: every loop's GO! dumps a quick rain of treats from the top.
        if (_state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
        AnnounceFinalLoopIfEntering();
        // The depth-V story card, deferred from the act-cross so it didn't fight the draft/ReadyGo.
        // The field is live now, so the card claims its own (lesson-card-style) pause cleanly.
        if (_pendingDepthVCard)
        {
            _pendingDepthVCard = false;
            ChaosNarrativeHooks.OnMoment("depthV_enter", BuildNarrativeCtx());
        }
    }

    /// <summary>FINAL LOOP banner the moment the run's last loop actually begins — for the
    /// draft path that's after the post-pick GO! (a banner under the table would be wasted),
    /// for draftless runs it's the wave commit. Once per run; the Relapse bonus loop keeps
    /// its own "☠ RELAPSE" announce instead of a second finale.</summary>
    private void AnnounceFinalLoopIfEntering()
    {
        if (_state == null || _finalLoopAnnounced) return;
        if (_state.WaveCount <= 1 || _state.WaveIndex < _state.WaveCount) return;
        _finalLoopAnnounced = true;
        ChaosAnnouncerOverlay.Announce("THE LAST LOOP", ChaosAnnounceKind.Depth,
            artKey: "final_loop", subText: "nothing after this one");
        _state.PushEvent("🕳 the last loop.");
    }

    /// <summary>Welcome Shower: dump a handful of treats (flash/subliminal) raining from the top.</summary>
    private void SpawnWelcomeShower()
    {
        if (_state == null) return;
        try
        {
            int count = 6;
            for (int i = 0; i < count; i++)
            {
                var variant = ChaosBubbleVariants.All[Random.Shared.Next(2)];   // rows 0/1 = the treats
                var spec = ChaosBubbleVariants.Build(variant, _state.RunIntensity, _state.FuseTimeMult,
                    ChaosMotion.RainDown, _state.Config.EffectIntensity, _state.BubbleScale);
                App.Bubbles?.SpawnChaosBubble(spec);
            }
            App.Bubbles?.PlayChime(0.25f);
            _state.PushEvent("🚿 welcome shower — treats from above");
        }
        catch (Exception ex) { App.Logger?.Debug("SpawnWelcomeShower: {E}", ex.Message); }
    }

    // ============================ bubble callbacks ============================

    private double BasePoints(int strength) => 40 + strength * 1.6; // 40..200

    /// <summary>Lifetime-boon payout layer on bubble pops: Blindfold's pay multiplier (1.0 unworn).</summary>
    private double BoonPayMult => _state?.BlindfoldPayMult ?? 1.0;

    // Config-gated juice helpers (no-op when the user disabled that feedback).
    private void Pulse(Color color, double strength)
    {
        if (_state?.Config?.ColorFlashesEnabled == true) _fx?.Pulse(color, strength);
    }
    private void Shake(double baseIntensity, int durMs)
    {
        var cfg = _state?.Config;
        if (cfg?.ScreenShakeEnabled == true)
            App.ScreenShake?.Shake(Math.Clamp(baseIntensity * cfg.ShakeIntensity, 0, 1), durMs);
    }

    /// <summary>Blank Eyes: float the pop's real payout (mults, flips and all) just under the
    /// pop word, anchored at the popped bubble's centre (written by Bubble.Pop a moment ago).</summary>
    private void ShowPopScore(double pts)
    {
        if (_state?.ShowPopScores != true || pts <= 0) return;
        ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip + 30,
            "+" + ((long)pts).ToString("N0"), Color.FromRgb(0xFF, 0xE9, 0xA0));
    }

    /// <summary>Taking Chances: every pop is a coin flip — x2 with the level's odds, else x0.5. 1.0 when unworn.</summary>
    private double ChanceFlip() =>
        _state?.ChanceDoubleOdds > 0 ? (Random.Shared.NextDouble() < _state.ChanceDoubleOdds ? 2.0 : 0.5) : 1.0;

    /// <summary>"Focus here...": x3 while the pendulum's slow swing holds (1.0 otherwise).</summary>
    private double PendulumFactor() =>
        _pendulumSlowActive && _state?.PendulumPayMult > 1 ? _state.PendulumPayMult : 1.0;

    /// <summary>Relapse's bonus loop pays double gold — every gold bank routes through here.</summary>
    private int GoldScaled(int gold) => _state?.RelapseLoopActive == true ? gold * 2 : gold;

    /// <summary>
    /// Bank an instant in-run payout as GOLD (ChaosGlyphs.Gold, her bench's coin; never drops/✦).
    /// Persists immediately via <see cref="ChaosMeta.AddGold"/>; the very first gold ever
    /// also gets its quiet debut beat (flag + bark + one feed line). When
    /// <paramref name="floatAtPop"/> is set, a small gold figure floats at the last pop point.
    /// </summary>
    private void BankGold(int gold, bool floatAtPop = false)
    {
        if (gold <= 0) return;
        bool first = !ChaosMeta.State.SeenGoldFirst;
        if (first) ChaosMeta.State.SeenGoldFirst = true;
        ChaosMeta.AddGold(gold);   // persists the balance (and the first-gold flag with it)
        if (floatAtPop)
            ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip + 30,
                $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(0xFF, 0xD7, 0x00));
        if (first)
        {
            _state?.PushEvent($"{ChaosGlyphs.Gold} gold. she takes it at her bench.");
            try { App.Bark?.NotifyChaosGoldFirst(); } catch { }
        }
    }

    /// <summary>Drip Feed drops per pop, doubled during the Relapse bonus loop.</summary>
    private int DropsPerPopNow() => (_state?.DropPerPop ?? 0) * (_state?.RelapseLoopActive == true ? 2 : 1);

    /// <summary>Drip Feed: bank the per-pop trickle, doubled during the Relapse bonus loop,
    /// clamped to the level's per-descent ceiling (the cap bounds the doubling too).</summary>
    private void BankDripFeed()
    {
        if (_state == null || _state.DropPerPop <= 0) return;
        long cap = ChaosLifetimeBoons.DripFeedCap(_state.DropPerPop);
        _state.TrickleDrops = Math.Min(cap, _state.TrickleDrops + DropsPerPopNow());
    }

    /// <summary>
    /// Loop-clear tip (2026-06-12 economy rework): every loop boundary reached banks a
    /// little gold so her bench is never luck-gated — 3-6 · difficulty, doubled when the
    /// loop was clean (zero detonations). A skill-based baseline; goldens, tease denials
    /// and cam-girl tips stack on top.
    /// </summary>
    private void AwardLoopTip()
    {
        if (_state == null) return;
        bool clean = _waveDetonations == 0;
        _waveDetonations = 0;
        int tip = (int)Math.Round(Random.Shared.Next(3, 7) * _state.Config.DifficultyMult);
        if (clean) tip *= 2;
        tip = GoldScaled(tip);
        BankGold(tip);
        _state.PushEvent(clean
            ? $"{ChaosGlyphs.Gold} clean loop — she tips +{tip} gold"
            : $"{ChaosGlyphs.Gold} loop done — she tips +{tip} gold");
    }

    /// <summary>Cam Girl: any pop can tip gold (banked instantly at her bench's balance).</summary>
    private void RollCamGirlTip()
    {
        if (_state == null || _state.CamGirlTipChance <= 0) return;
        if (Random.Shared.NextDouble() >= _state.CamGirlTipChance) return;
        int tip = GoldScaled(Random.Shared.Next(2, 5));
        BankGold(tip, floatAtPop: true);
        _state.PushEvent($"{ChaosGlyphs.Gold} tipped +{tip} gold");
    }

    private void OnBenignPopped(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        CountRegenPop();   // Slow Recovery: every pop feeds the regen counter

        // Lucky golden bubble: pure treasure — real gold (Sparks) banked instantly, outside
        // the score/combo economy entirely (so coin flips, mults and streaks never touch it).
        // Pop-up Notification heart: pure kindness — +1 resistance, outside the score/combo
        // economy entirely (no points, no streak, no payload).
        if (spec.IsHeart)
        {
            _state.Shields += 1;
            _state.Focus += ChaosTuning.FOCUS_PER_HEART;
            _state.PushEvent("💖 pop-up notification! +1 resistance");
            ChaosAnnouncerOverlay.Announce("💖 +1 resistance", ChaosAnnounceKind.PowerUp, artKey: "resistance");
            Pulse(SHIELD_GAIN_COLOR, 0.25);
            ChaosSfx.Play("resist_absorb", 0.55f);
            return;
        }

        // Gold Digger droplet: a little gold per bead, outside the score economy like its parent.
        if (spec.IsDroplet)
        {
            _state.Focus += ChaosTuning.FOCUS_PER_DROPLET;
            int dGold = GoldScaled(Random.Shared.Next(3, 8));
            BankGold(dGold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} droplet +{dGold} gold");
            Pulse(Color.FromRgb(255, 215, 0), 0.12);
            ChaosSfx.Play("golden_pop", 0.35f);
            return;
        }

        if (spec.IsGolden)
        {
            _state.Focus += ChaosTuning.FOCUS_PER_GOLDEN;
            // Rabbit's Foot scales the gold per level (10–20 unworn … 20–40 at the capstone).
            int lvl = ChaosMeta.IsBoonActive("rabbits_foot") ? ChaosMeta.BoonLevel("rabbits_foot") : 0;
            var (gMin, gMax) = ChaosLifetimeBoons.GoldenPayRange(lvl);
            int gold = GoldScaled(Random.Shared.Next(gMin, gMax + 1));
            BankGold(gold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} lucky bubble! +{gold} gold");
            ChaosAnnouncerOverlay.Announce($"{ChaosGlyphs.Gold} +{gold} gold", ChaosAnnounceKind.PowerUp);
            Pulse(Color.FromRgb(255, 215, 0), 0.35);
            ChaosSfx.Play("golden_pop", 0.6f);   // coins spill — real gold just landed
            // Gold Digger: the lucky bubble bursts into 3 falling droplets at the pop point.
            if (_state.GoldDiggerEnabled)
            {
                for (int i = 0; i < 3; i++)
                    App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildGoldDroplet(
                        BubbleService.ChaosLastPopXPx + Random.Shared.Next(-50, 51),
                        BubbleService.ChaosLastPopYPx + Random.Shared.Next(-20, 21)));
                _state.PushEvent("⛏ gold digger — it spills");
            }
            return;
        }

        // Mimic prism ("Look at the bright colors..."): 10x pay — and the copied effect fires.
        if (spec.IsPrism)
        {
            ChaosLessonHooks.OnPrismPopped();   // taking_chances lesson
            _state.Focus += ChaosTuning.FOCUS_PER_PRISM;
            FireScaledPayload(spec.Payload);
            _state.EffectsFired++;
            _state.Combo++;
            _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
            double prismPts = BasePoints(spec.Strength) * 10.0 * _state.TotalMult * BoonPayMult;
            _state.Score += prismPts;
            ShowPopScore(prismPts);
            ChaosAnnouncerOverlay.Announce($"🔮 the colors! 10x — it was {spec.Payload.DisplayName}", ChaosAnnounceKind.Temptation,
                artKey: "bright_colors", subText: $"10x — it was {spec.Payload.DisplayName}");
            _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
            Pulse(Color.FromRgb(0xC8, 0xA8, 0xFF), 0.40);
            App.Achievements?.TrackBubblePopped();
            RollCamGirlTip();
            CheckComboMilestone();
            return;
        }

        spec.Payload.Fire();                 // benign pop = a treat
        if (!ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Taste)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Taste);
        ChaosLessonHooks.OnTreatPopped(spec);   // vibe_popping / chain_reaction / the_pull / intrusive_thoughts
        _state.EffectsFired++;
        _state.Combo++;
        // Focus economy: every treat-class pop refuels the hand, REGARDLESS of source (a
        // rabbit mowing treats still feeds the player). Heavies refuel a little extra.
        bool focusWasLow = _state.FocusLow;
        double focusGain = spec.PayMult > 1 ? ChaosTuning.FOCUS_PER_HEAVY : ChaosTuning.FOCUS_PER_POP;
        _state.Focus += focusGain;
        // While the tank is under a snap's price, each pop names its refill at the pop point —
        // the recovery loop teaches itself, and the float stops the moment focus is healthy.
        if (focusWasLow)
            ChaosPopText.Show(BubbleService.ChaosLastPopXDip, BubbleService.ChaosLastPopYDip + 34,
                $"+{(int)focusGain} FOCUS", Color.FromRgb(0x7A, 0xE0, 0xFF));
        _state.Heat = Math.Min(1.0, _state.Heat + 0.04);
        // Golden Touch charm raises the calm-pop baseline (0.4 unworn → 0.45–0.60 by level).
        double benignMult = _state.BenignBaseline;
        double pts = BasePoints(spec.Strength) * benignMult * spec.PayMult * PendulumFactor()
                     * ChanceFlip() * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        BankDripFeed();   // Drip Feed (x2 in the relapse loop, clamped to the per-descent cap)
        ShowPopScore(pts);                                                     // Blank Eyes
        App.Achievements?.TrackBubblePopped();
        Pulse(BENIGN_POP_COLOR, BENIGN_POP_PULSE);   // the most-frequent action now has a tiny pop pulse
        if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
        RollCamGirlTip();
        // Body Buzz: one pop in eight detonates an electric shockwave — the ring strikes every
        // bubble it covers. Shockwave pops re-enter here and re-roll, but 1/8 damps the cascade.
        if (_state.EStimShockwaveChance > 0 && Random.Shared.NextDouble() < _state.EStimShockwaveChance)
        {
            App.Bubbles?.TriggerEStimShockwave(new System.Windows.Point(
                BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
            Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.25);
            _state.PushEvent("⚡ body buzz — the current spreads");
        }
        // GG make more GG: sometimes a popped treat bursts into 3 wild sweeper rabbits.
        if (_state.GgRabbitChance > 0 && Random.Shared.NextDouble() < _state.GgRabbitChance)
        {
            for (int i = 0; i < 3; i++)
                App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildDarter(_state.RunIntensity, spotlight: false,
                    BubbleService.ChaosLastPopXPx + Random.Shared.Next(-40, 41),
                    BubbleService.ChaosLastPopYPx + Random.Shared.Next(-40, 41),
                    sweeper: true));
            ChaosSfx.Play("rabbit_spawn", 0.5f);
            _state.PushEvent("🐇 GG! they multiply");
        }
        App.Bark?.NotifyChaosBenignPopped(spec.VariantId, spec.Payload.DisplayName, _state.Combo);
        _state.PushEvent($"○ popped {spec.Payload.DisplayName}");
        CheckComboMilestone();
    }

    /// <summary>A treat (flash/subliminal/golden) sat unpopped past its 5s screen life: it
    /// dissolved — no pop, no payload — and the streak HALVES. Letting rewards rot really
    /// hurts now, which keeps the player chasing the good bubbles too.</summary>
    private void OnTreatExpired(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        string name = spec.IsGolden ? "lucky bubble" : spec.Payload.DisplayName;
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
        Pulse(Color.FromRgb(150, 150, 175), 0.10);   // faint grey sigh — a reward slipped by
    }

    // ---- hold-to-defuse: the focus gate + channel feedback (BubbleService calls these) ----
    private DateTime _invulnUntilUtc = DateTime.MinValue;   // Snap Chain: triggers bounce off inside this window
    private double _focusLowAccumSec;                        // rh_focus_low: dry-spell stopwatch
    private bool _focusLowBarkFired;                         // once per run max

    /// <summary>Defuse cost for one channel on this bubble (Bound halves pay half each,
    /// so the pair totals one normal defuse).</summary>
    private double DefuseCostFor(EffectBubbleSpec spec) =>
        spec.IsBoundHalf ? ChaosTuning.DEFUSE_COST_BOUND : ChaosTuning.DEFUSE_COST;

    /// <summary>May the player's press start a defuse channel? Frozen fields channel for free —
    /// otherwise the focus must cover the bubble's cost (deducted on COMPLETION, not here).</summary>
    private bool CanChannelDefuse(EffectBubbleSpec spec)
    {
        if (_state == null) return false;
        bool ok = _freezeRemainingSec > 0 || _state.Focus >= DefuseCostFor(spec);
        if (ok) ChaosLessonHooks.OnChannelStarted();   // slow_fuses: the hold's clock starts now
        return ok;
    }

    /// <summary>A channel never completed — the bubble is already triggering (onDetonate carries
    /// the consequences); this is purely the "WHY it blew" feedback + first-time barks.</summary>
    private void OnChannelBroken(EffectBubbleSpec spec, string reason)
    {
        if (_state == null) return;
        ChaosLessonHooks.OnChannelBroken();   // slow_fuses: bank the partial hold ("nofocus" never started one)
        switch (reason)
        {
            case "nofocus":
                // Distinct cue + red flash so the lesson lands: you grabbed what you couldn't pay for.
                ChaosSfx.Play("focus_empty", 0.55f);
                Pulse(Color.FromRgb(255, 80, 80), 0.22);
                _hud?.FlashFocusBar();   // point the eye at the empty bar itself
                // The small bubble-side "NO FOCUS" label reads as "popped for no reason" to a new
                // player — say WHY loudly, center-screen, so the lesson lands. Throttled: a flurry
                // of mis-grabs shouldn't stack a queue of identical banners.
                if ((DateTime.UtcNow - _lastNoFocusAnnounceUtc).TotalSeconds >= 1.5)
                {
                    _lastNoFocusAnnounceUtc = DateTime.UtcNow;
                    // No artKey → the announcer renders only this single 60px line (subText is
                    // dropped without banner art), so the whole message lives in the headline.
                    ChaosAnnouncerOverlay.Announce("✋ NO FOCUS TO DEFUSE", ChaosAnnounceKind.Temptation, holdMs: 1300);
                }
                _state.PushEvent("✋ no focus — it triggers in your grip");
                if (!ChaosMeta.State.SeenBarkDefuseNoFocus)
                {
                    ChaosMeta.State.SeenBarkDefuseNoFocus = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosDefuseNoFocus();
                }
                break;
            case "click":
                _state.PushEvent("💥 a tap isn't a hold");
                if (!ChaosMeta.State.SeenBarkClickDetonate)
                {
                    ChaosMeta.State.SeenBarkClickDetonate = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosClickDetonate();
                }
                break;
            default:   // "release" (early let-go, or the cursor strayed off the bubble)
                _state.PushEvent("💥 you let go");
                if (!ChaosMeta.State.SeenBarkDefuseRelease)
                {
                    ChaosMeta.State.SeenBarkDefuseRelease = true; ChaosMeta.Save();
                    App.Bark?.NotifyChaosDefuseRelease();
                }
                break;
        }
    }

    private void OnDefused(EffectBubbleSpec spec, double fuseSecLeft, bool viaChannel)
    {
        if (_state == null || _paused || _manualPaused) return;
        // The player's hand pays for its defuses; toys, chains and zones never do. A channel
        // completed during a freeze is free (that's the freeze's reward).
        if (viaChannel)
        {
            if (_freezeRemainingSec <= 0) _state.Focus -= DefuseCostFor(spec);
            if (!ChaosMeta.State.SeenBarkDefuseFirst)
            {
                ChaosMeta.State.SeenBarkDefuseFirst = true; ChaosMeta.Save();
                App.Bark?.NotifyChaosDefuseFirst();
            }
        }
        // Snap Chain mantra: every completed defuse opens a brief invulnerability window.
        if (_state.DefuseInvulnMs > 0)
            _invulnUntilUtc = DateTime.UtcNow.AddMilliseconds(_state.DefuseInvulnMs);
        CountRegenPop();   // Slow Recovery: snaps count toward the regen threshold too
        if (!ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Snap)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Snap);
        ChaosHappyPath.OnDefuseCompleted();   // run 1: the lone threat's defuse opens the live whitelist
        ChaosLessonHooks.OnDefuseCompleted(fuseSecLeft, viaChannel);   // snap_field / blindfold / last_breath / slow_fuses
        _state.Defused++;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.07);
        // Last Breath: snapping at the very brink pays fortunes (window + payout scale by level).
        double lastBreath = _state.LastBreathWindowSec > 0 && fuseSecLeft <= _state.LastBreathWindowSec
            ? _state.LastBreathPayMult : 1.0;
        // Slowburner capstone: a snap inside the trance's final 1.5 seconds pays triple.
        double slowburn = fuseSecLeft <= 1.5 && _state.MaxedBoons.Contains("slowburner") ? 3.0 : 1.0;
        double pts = BasePoints(spec.Strength) * 1.0 * lastBreath * slowburn * PendulumFactor()
                     * ChanceFlip() * _state.TotalMult * BoonPayMult;
        _state.Score += pts;
        BankDripFeed();   // Drip Feed (x2 in the relapse loop, clamped to the per-descent cap)
        ShowPopScore(pts);                                                     // Blank Eyes
        App.Achievements?.TrackBubblePopped();
        RollCamGirlTip();
        // Playing with fire: a snap inside the final second pays gold on the spot.
        if (_state.LastSecondGoldEnabled && fuseSecLeft <= 1.0)
        {
            int fGold = GoldScaled(Random.Shared.Next(5, 10));
            BankGold(fGold, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} playing with fire +{fGold} gold");
            Pulse(Color.FromRgb(255, 140, 60), 0.30);
        }
        // Aftermath: a brink-snap leaves 2s of crackling residue at the pop point.
        if (_state.AftermathEnabled && fuseSecLeft <= 1.5)
        {
            App.Bubbles?.AddChaosResidue(new Point(BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
            _state.PushEvent("⚡ aftermath — the air still crackles");
        }
        // Size Queen: every snap rings outward and pops the treats it touches.
        if (_state.RippleEnabled)
            App.Bubbles?.TriggerChaosRipple(new Point(BubbleService.ChaosLastPopXPx, BubbleService.ChaosLastPopYPx));
        App.Bark?.NotifyChaosBubbleDefused(_state.Combo, spec.VariantId, _state.Config.Difficulty.ToString());
        if (lastBreath > 1)
        {
            _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
            ChaosAnnouncerOverlay.Announce($"⏱ last breath x{lastBreath:0}", ChaosAnnounceKind.PowerUp);
            Pulse(Color.FromRgb(255, 215, 0), 0.40);   // gold flash — you earned it
        }
        else
        {
            Pulse(Color.FromRgb(90, 255, 150), 0.16);  // soft green confirm
        }
        if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
        _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
        // Narrative: a brink defuse — snapped with under 0.8s of fuse left.
        if (fuseSecLeft <= 0.8) ChaosNarrativeHooks.OnMoment("brink_defuse", BuildNarrativeCtx());
        CheckComboMilestone();
    }

    private void OnDetonated(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        if (spec.IsEcho)
        {
            SpawnEchoChildren(spec);         // The Echo fires NO conditioning payload — it SPLITS
        }
        else
        {
            FirePayloadForDetonation(spec);  // the threat goes off (ambient mode may soften it)
            _state.EffectsFired++;
        }
        _state.Detonated++;
        _runDetonations++;
        _waveDetonations++;
        ChaosLessonHooks.OnDetonation();   // silk_touch: the loop is no longer clean

        string variant = spec.VariantId;     // payload ctx = variant id (e.g. "braindrain","video")
        string diff = _state.Config.Difficulty.ToString();
        double s = spec.Strength / 100.0;

        // Snap Chain mantra: inside the post-snap invulnerability window a trigger can't take
        // anything from you — the payload already fired above, but streak, lust and resistance
        // all hold (and no shield is spent).
        if (DateTime.UtcNow < _invulnUntilUtc)
        {
            _state.PushEvent($"⛓ snap chain holds ({spec.Payload.DisplayName})");
            Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.22);
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
            return;
        }

        const int shieldCost = 1;
        if (_state.Shields >= shieldCost)
        {
            _state.Shields -= shieldCost;
            _state.Heat = Math.Max(0, _state.Heat - 0.2);
            _state.PushEvent($"♥ resistance crumbles ({spec.Payload.DisplayName})");
            // The last point of resistance going has its own, sadder cue.
            ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            Pulse(Color.FromRgb(80, 160, 255), 0.28);    // blue shield-save
            Shake(0.25 + s * 0.3, 280);
            // Clutch shield-save branch — distinct trigger so the matcher can praise it.
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
        }
        else if (_state.CollarSaves > 0)
        {
            // Collar: out of resistance, but the streak is held — combo and lust survive the hit.
            // The payload still fired above; the collar protects the chain, not the screen.
            _state.CollarSaves--;
            _hud?.FlashShields(false);   // resistance still came up short — the collar only held the streak
            _state.PushEvent($"📿 the collar holds ({_state.CollarSaves} left)");
            ChaosSfx.Play("collar_save", 0.6f);
            Pulse(Color.FromRgb(255, 215, 0), 0.32);             // gold save flash
            Shake(0.25 + s * 0.3, 280);
            // Unleashed: the save ITSELF strikes back — a golden shockwave snaps every live bubble.
            if (_state.UnleashedEnabled)
            {
                App.Bubbles?.DefuseAllLive();
                ChaosAnnouncerOverlay.Announce("📿 UNLEASHED", ChaosAnnounceKind.PowerUp, artKey: "unleashed");
                _state.PushEvent("📿 unleashed — the field lets go");
                Pulse(Color.FromRgb(255, 215, 0), 0.50);
            }
            App.Bark?.NotifyChaosBubbleDetonatedAbsorbed(variant, spec.Strength, _runDetonations, _state.Combo, diff, _state.Shields);
        }
        else
        {
            int comboBeforeBreak = _state.Combo;   // capture BEFORE zeroing (frozen contract)
            _state.Combo = 0;
            _lastComboBigFired = 0;                // combo broke → reset ComboBig crossing tracking
            _state.Heat = 0;
            _hud?.FlashShields(false);             // the hearts flash red: nothing left to pay with
            _state.PushEvent($"💥 {spec.Payload.DisplayName} triggered!");
            ChaosSfx.Play("trigger", 0.55f);                     // the muffled boom under the payload stinger
            Pulse(Color.FromRgb(255, 50, 50), 0.4 + s * 0.35);   // red malus
            Shake(0.4 + s * 0.5, 380);                           // the malus jolt
            // Real-hit branch (unshielded only now).
            App.Bark?.NotifyChaosBubbleDetonated(variant, spec.Strength, _runDetonations, comboBeforeBreak, diff);
            // Narrative: the first bare detonation of the run.
            if (ChaosNarrativeHooks.TryFirstBareDeto())
                ChaosNarrativeHooks.OnMoment("first_bare_deto", BuildNarrativeCtx());
        }
    }

    /// <summary>
    /// Fire a detonating bubble's payload. In opt-in Ambient mode, an intrusive payload
    /// (video / HT link) is remapped to one of the two lighter ambient payloads so a
    /// background run never yanks a fullscreen video/browser over the user's work.
    /// Outside ambient mode the payload fires exactly as built (neutral invariant).
    /// </summary>
    // ---- heavy-payload gate (video + gif cascade): ONE at a time, never queued ----
    // Stacked LibVLC players + cascade windows is what crashed the app (2026-06-10): while a
    // heavy effect runs, any further heavy detonation is simply dropped — no queue, no stack.
    private DateTime _heavyUntilUtc = DateTime.MinValue;     // expected end of the running heavy effect
    private DateTime _chaosVideoCapUtc = DateTime.MinValue;  // hard stop for a chaos-fired video
    private const double VIDEO_HARD_CAP_SEC = 15;            // = VideoPayload.SEGMENT_SEC (random 15s slice)

    /// <summary>True while a mandatory video or gif cascade is (still) running. The cascade is
    /// checked for REAL (slow clips can outlive the time estimate); the timer remains as a floor
    /// covering window-open latency and the post-video teardown quarantine.</summary>
    private bool HeavyEffectActive =>
        App.Video?.IsPlaying == true || ChaosGifCascadeOverlay.IsRaining || DateTime.UtcNow < _heavyUntilUtc;

    /// <summary>Seconds the heavy gate stays shut AFTER a video ends. Closing the fullscreen
    /// video windows + disposing LibVLC players runs async for several seconds; raising the
    /// cascade's layered window into that churn wedged the render thread twice (22:41, 22:50
    /// freezes — both were "cascade ~4s after video teardown").</summary>
    private const double VIDEO_TEARDOWN_QUARANTINE_SEC = 3;   // 2026-06-13: loosened 6→3 — cascades may now follow a video much sooner (tail-overlap with teardown), the keep-alive cascade window no longer churns layered HWNDs into LibVLC disposal

    private void ExtendHeavyQuarantine(double sec)
    {
        var until = DateTime.UtcNow.AddSeconds(sec);
        if (until > _heavyUntilUtc) _heavyUntilUtc = until;
    }

    /// <summary>Mirrors the pop streak into the tunnel background so the fall accelerates with
    /// the combo and brakes when it halves/breaks. Combo only ever changes on the UI thread
    /// (timers + click handlers), so this posts straight through.</summary>
    private void OnStateChangedForTunnel(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChaosRunState.Combo)) return;
        var st = _state;
        if (st == null) return;
        try { ChaosTunnelService.SetStreak(st.Combo, st.ComboMult); } catch { }
    }

    /// <summary>Any video ending during a run (natural end, attention-check retry, cap) starts
    /// the teardown quarantine so no cascade rises into the LibVLC disposal churn.</summary>
    private void OnVideoEndedDuringRun(object? sender, EventArgs e)
    {
        try { Application.Current?.Dispatcher?.BeginInvoke((Action)(() => ChaosTunnelService.SetVideoPlaying(false))); } catch { }
        ExtendHeavyQuarantine(VIDEO_TEARDOWN_QUARANTINE_SEC);
    }

    /// <summary>Fire a payload under "Playing with fire": detonation durations scale by the sin's
    /// knob for exactly this call (GlobalDurationMult is shared with slow-mo/freeze — scope it).</summary>
    private void FireScaledPayload(EffectPayload payload)
    {
        ChaosLessonHooks.OnPayloadFired(payload.Kind);   // blindfold: the screen turns busy
        double m = _state?.DetonationDurationMult ?? 1.0;
        if (m == 1.0) { payload.Fire(); return; }
        EffectPayload.GlobalDurationMult *= m;
        try { payload.Fire(); }
        finally { EffectPayload.GlobalDurationMult /= m; }
    }

    private void FirePayloadForDetonation(EffectBubbleSpec spec)
    {
        try
        {
            if (_state?.Config?.AmbientMode == true && IsIntrusivePayload(spec.Payload.Kind))
            {
                // Soft remap. While a heavy effect runs the coin always lands on text — never a
                // second cascade on top of a running one.
                bool cascade = !HeavyEffectActive && _ambientRng.NextDouble() < 0.5;
                EffectPayload soft = cascade ? new GifCascadePayload() : new BouncingTextPayload();
                soft.Strength = spec.Payload.Strength;
                if (cascade)
                    _heavyUntilUtc = DateTime.UtcNow.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
                PlayPayloadStinger(cascade ? "fx_rain_start" : "fx_text");
                FireScaledPayload(soft);
                return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos ambient remap: {E}", ex.Message); }

        // ONE heavy effect at a time: a video/cascade detonating while another heavy effect is
        // up gets dropped on the floor (the shield/streak consequences upstream still applied).
        var kind = spec.Payload.Kind;
        bool heavy = kind == EffectBubblePayloadKind.Video || kind == EffectBubblePayloadKind.GifCascade;
        if (heavy && HeavyEffectActive)
        {
            App.Logger?.Information("Chaos: dropped {Kind} detonation — a heavy effect is already running", kind);
            _state?.PushEvent($"▶ the deep is busy — {spec.Payload.DisplayName} fizzles");
            return;
        }
        if (kind == EffectBubblePayloadKind.Video)
        {
            _chaosVideoCapUtc = DateTime.UtcNow.AddSeconds(VIDEO_HARD_CAP_SEC);
            _heavyUntilUtc = DateTime.UtcNow.AddSeconds(VIDEO_HARD_CAP_SEC + 3);   // cap + open/close slack
        }
        else if (kind == EffectBubblePayloadKind.GifCascade)
        {
            // Spawn window + the last clips' ride to the bottom of the screen.
            _heavyUntilUtc = DateTime.UtcNow.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
        }
        PlayPayloadStinger(StingerForVariant(spec.VariantId));
        FireScaledPayload(spec.Payload);   // Playing with fire: detonations linger 50% longer
    }

    /// <summary>Flavor stinger over the detonation boom — one per payload family. Video, flash,
    /// and subliminal payloads carry their own audio, so they get no stinger.</summary>
    private static string StingerForVariant(string variantId) => variantId switch
    {
        "braindrain" => "fx_drain",
        "bambifreeze" => "fx_freeze",
        "htlink" => "fx_rain_start",
        _ => "",
    };

    private static void PlayPayloadStinger(string name)
    {
        if (name.Length > 0) ChaosSfx.Play(name, 0.45f);
    }

    // What the desktop-friendly (ambient) mode soft-remaps to a cascade/text instead of firing.
    // NOTE: Video is intentionally NOT here. Story mode is disabled, so every run is forced to
    // FreeDesktop (AmbientMode=true) — if Video were remapped, video bubbles could NEVER play a
    // video (they'd always fizzle to a cascade/text). Players who don't want videos just disable
    // the "video" bubble in the pool toggles. HT links stay softened: they hijack the embedded
    // browser fullscreen and have no equivalent opt-out.
    private static bool IsIntrusivePayload(EffectBubblePayloadKind k) =>
        k == EffectBubblePayloadKind.HtLink;

    private static readonly Random _ambientRng = new();

    private void OnDarterCaught(EffectBubbleSpec spec, bool quick)
    {
        if (_state == null || _paused || _manualPaused) return;
        int pts = ChaosBubbleVariants.DARTER_BASE_POINTS + (quick ? ChaosBubbleVariants.DARTER_QUICK_BONUS : 0);
        _state.Score += pts * _state.TotalMult;
        _state.Focus += ChaosTuning.FOCUS_PER_RABBIT;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        ChaosLessonHooks.OnRabbitCaught();   // rabbit_caller lesson
        // The darter is a utility pickup: catching it slows time (no conditioning jolt).
        // Chained catches: a rabbit clicked while time is ALREADY slow tops the window up
        // by +0.8s instead of re-arming the full duration. A pendulum swing extended this
        // way still hands "Focus here..." scoring back to normal (same rule as a refresh).
        bool extended = _slowMoRemainingSec > 0;
        if (extended)
        {
            _slowMoRemainingSec += 0.8;
            _pendulumSlowActive = false;
        }
        else ActivateSlowMo();
        Pulse(Color.FromRgb(120, 200, 255), quick ? 0.32 : 0.24);   // icy slow-mo flash
        App.Bark?.NotifyChaosDarterCaught(pts, _state.Combo, quick);
        _state.PushEvent(extended ? "🐇 caught in the slow! +0.8s"
            : quick ? "⚡ quick catch! time slows" : "🐇 white rabbit caught! time slows");
        CheckComboMilestone();
    }

    /// <summary>Catching a Freeze bubble is a GOOD pickup: it freezes the whole field — every bubble
    /// shudders then holds in place, live fuses pause, spawns halt — and plays an icy "freeze" burst
    /// with a held white-blue edge glow and per-bubble blue auras. Refreshes on each catch.</summary>
    private void OnFreezeCaught(EffectBubbleSpec spec)
    {
        if (_state == null || _paused || _manualPaused) return;
        _state.Score += FREEZE_BASE_POINTS * _state.TotalMult;
        _state.Combo++;
        _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
        App.Achievements?.TrackBubblePopped();
        ChaosLessonHooks.OnFreezeCaught();   // freeze_trigger lesson (pickups only, not the toy)
        ActivateFreeze();
        App.Bark?.NotifyChaosFreezeCaught(FREEZE_BASE_POINTS * _state.TotalMult, _state.Combo);
        _state.PushEvent("❄ frozen. the field holds");
        CheckComboMilestone();
    }

    // ---- darter slow-mo power-up ----
    private const double SLOWMO_FACTOR = 0.12;        // chaos motion/fuse speed while active (lower = stronger slow)
    private const double SLOWMO_DURATION_SEC = 6.0;   // real-time length of the slow-mo
    private double _slowMoRemainingSec;

    /// <summary>Catching a darter slows the whole field: bubbles drift slower, live fuses last
    /// longer, spawns stretch out, and flash/overlay payloads linger. Refreshes on each catch.
    /// Default duration is base + the Pendulum boon's bonus; pass an explicit duration to
    /// override (the Collar capstone's short recovery beat).</summary>
    private bool _slowMoCueOn;   // an in-cue played; the matching out-cue is owed on end

    private void ActivateSlowMo(double? durationSec = null, string bannerLabel = "Time Slow")
    {
        _slowMoRemainingSec = durationSec ?? (SLOWMO_DURATION_SEC + (_state?.SlowMoBonusSec ?? 0));
        // "Focus here...": triple pay rides ONLY the pendulum's own swing — a darter slow-mo
        // refreshing over it hands the window back to normal scoring.
        _pendulumSlowActive = bannerLabel == "Pendulum";
        App.Bubbles?.SetChaosTimeScale(SLOWMO_FACTOR);
        ChaosEffectBannerOverlay.Show("slowmo", bannerLabel, Color.FromRgb(0x7A, 0xE0, 0xFF),
            artKey: bannerLabel == "Pendulum" ? "pendulum" : "slowmo");
        EffectPayload.GlobalDurationMult = 1.0 / SLOWMO_FACTOR;
        if (!_slowMoCueOn) ChaosSfx.Play("time_slow_in", 0.5f);   // refreshes shouldn't re-warp
        _slowMoCueOn = true;
    }

    // ---- Slow Recovery charm: resistance regen EARNED by pops (time-based habit retired 2026-06-10) ----
    private int _regenPopCount;

    /// <summary>Every pop (treat, golden, or snap) feeds Slow Recovery: once
    /// <see cref="ChaosRunState.ShieldRegenPops"/> pops accumulate while you're below the
    /// resistance you descended with, one point knits back and the count restarts.</summary>
    private void CountRegenPop()
    {
        if (_state == null || _state.ShieldRegenPops <= 0) return;
        if (_state.Shields >= _state.StartShields) { _regenPopCount = 0; return; }
        if (++_regenPopCount < _state.ShieldRegenPops) return;
        _regenPopCount = 0;
        _state.Shields += 1;
        _state.PushEvent("♥ resistance regrows");
        Pulse(SHIELD_GAIN_COLOR, 0.18);
    }

    // ---- Blindfold capstone: one global heartbeat tracking the field's closest fuse ----
    private double _heartbeatCooldownSec;

    private void TickBlindfoldHeartbeat(double dt)
    {
        if (_state == null || !_state.MaxedBoons.Contains("blindfold")) return;
        _heartbeatCooldownSec -= dt;
        if (_heartbeatCooldownSec > 0) return;
        var fuse = App.Bubbles?.MinChaosFuseSec();
        if (fuse == null || fuse > 4.0) { _heartbeatCooldownSec = 0; return; }
        // Quickens as the nearest detonation closes in: ~1/s at 4s out, capped at ~3/s.
        _heartbeatCooldownSec = Math.Clamp(fuse.Value / 4.0, 0.33, 1.0);
        try
        {
            var path = ModResourceResolver.ResolveAudioPath("chaos/heartbeat.mp3");
            App.Bubbles?.PlayCue(path, 0.5f);   // File.Exists-guarded: silent until the asset ships
        }
        catch (Exception ex) { App.Logger?.Debug("Blindfold heartbeat: {E}", ex.Message); }
    }

    private void EndSlowMo()
    {
        _slowMoRemainingSec = 0;
        _pendulumSlowActive = false;
        App.Bubbles?.SetChaosTimeScale(1.0);
        ChaosEffectBannerOverlay.End("slowmo");
        if (_slowMoCueOn) ChaosSfx.Play("time_slow_out", 0.45f);
        _slowMoCueOn = false;
        if (_freezeRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active freeze
    }

    // ============================ active skills (toys you press) ============================
    // Equipped active-use Skills fire on their keybind (pass-through global hook, run-scoped)
    // or their HUD button. Cooldowns/charges live per run on ChaosToyState (HUD binds them).

    private Services.GlobalKeyboardHook? _keyHook;
    private Services.GlobalMouseHook? _rippleHook;
    private double _vibeRemainingSec;   // VibePopping buzz window (real clock, like the power-ups)
    private bool _dvdBannerOn;          // the "PORN DVD" banner is up; drop it when the last logo lands
    private readonly System.Collections.Generic.List<ChaosToyButtonWindow> _toyButtons = new();

    private void CloseToyButtons()
    {
        foreach (var b in _toyButtons.ToArray())
            try { b.Close(); } catch { }
        _toyButtons.Clear();
    }

    /// <summary>Build the HUD button/keybind state for equipped active-use skills. Slot order =
    /// catalogue order; slot 1 fires on ChaosAccessoryKey1 (slot 2 arrives with the second pocket).</summary>
    private void BuildActiveToys()
    {
        if (_state == null) return;
        _state.ActiveToys.Clear();
        // Pockets are sewn at her bench: zero pockets = no toy buttons, no keybinds —
        // even if a stale save still has an active toy flagged.
        int pockets = ChaosMeta.SlotsFor(ChaosBoonCategory.Skill);
        if (pockets <= 0) return;
        var s = App.Settings?.Current;
        string[] keys = { s?.ChaosAccessoryKey1 ?? "Q", s?.ChaosAccessoryKey2 ?? "E" };
        int slot = 0;
        foreach (var b in ChaosLifetimeBoons.All)
        {
            if (slot >= pockets) break;
            if (!b.IsActiveUse || !ChaosMeta.IsBoonActive(b.Id)) continue;
            if (!_state.ToyPower.TryGetValue(b.Id, out var power)) continue;
            var toy = new ChaosToyState
            {
                Id = b.Id, Name = b.Name, Glyph = b.Glyph, Desc = b.Desc, Flavor = b.Flavor, CapstoneDesc = b.CapstoneDesc,
                KeyLabel = slot < keys.Length ? keys[slot] : "",
                CooldownSec = b.UseCooldownSec,
            };
            if (b.UseCooldownSec <= 0) toy.ChargesLeft = (int)power;   // charge-based (Freeze Trigger)
            _state.ActiveToys.Add(toy);
            slot++;
        }
    }

    private void StartKeyHook()
    {
        // Always hooked, toys or not — the panic key pauses/surfaces the descent through here.
        if (_state == null) return;
        try
        {
            _keyHook = new Services.GlobalKeyboardHook();
            _keyHook.KeyPressed += OnToyKey;
            _keyHook.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos toy key hook: {E}", ex.Message); }
    }

    private void StopKeyHook()
    {
        try
        {
            if (_keyHook != null)
            {
                _keyHook.KeyPressed -= OnToyKey;
                _keyHook.Dispose();
            }
        }
        catch { }
        _keyHook = null;
    }

    // ---- the Ripple (right-click verb): one charge, slow recharge, expanding wave ----

    private void StartRippleHook()
    {
        try
        {
            _rippleHook = new Services.GlobalMouseHook();
            _rippleHook.RightDown = OnRippleRightDown;
            // Shared-host bubble pops also ride this hook (self-gates on ChaosBubbleSharedHost): on a
            // left-click over a live bubble it pops + swallows; everywhere else it passes through.
            _rippleHook.LeftDown = px => App.Bubbles?.OnSharedHostLeftDown(px) ?? false;
            _rippleHook.Start();
        }
        catch (Exception ex) { App.Logger?.Debug("Chaos ripple hook: {E}", ex.Message); }
    }

    private void StopRippleHook()
    {
        try { _rippleHook?.Dispose(); } catch { }
        _rippleHook = null;
    }

    /// <summary>HOOK THREAD: swallow the right-click only when it lands within the wave's
    /// reach (+grace) of a chaos bubble — everywhere else it passes through, so desktop
    /// right-clicks stay usable mid-run. Touches ONLY the immutable centre snapshot; the
    /// actual cast (or the not-ready denial) is marshalled to the dispatcher.</summary>
    private bool OnRippleRightDown(Point px)
    {
        if (!_spawning || _paused || _manualPaused) return false;
        var st = _state;
        if (st == null) return false;
        double reach = st.RippleRadiusPx + ChaosTuning.RIPPLE_TRIGGER_GRACE_PX;
        bool near = false;
        foreach (var c in BubbleService.ChaosBubbleCentersSnapshot)
        {
            double dx = c.X - px.X, dy = c.Y - px.Y;
            if (dx * dx + dy * dy <= reach * reach) { near = true; break; }
        }
        if (!near) return false;
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return false;
        disp.BeginInvoke(new Action(() => FireRipple(px)));
        return true;
    }

    /// <summary>Cast the ripple at the click point (UI thread): consume the charge and send
    /// the wave — three of them a second apart on the Skipping Stone capstone.</summary>
    private void FireRipple(Point px)
    {
        if (!_spawning || _state == null || _paused || _manualPaused) return;
        if (_freezeRemainingSec > 0) return;   // a frozen field is already a free-pop window
        if (!_state.RippleReady)
        {
            ChaosSfx.Play("toy_denied", 0.45f);
            _state.PushEvent($"🌊 still water... gathering {_state.RippleText}");
            // The feed lives in the folded-away panel; answer the hand where it clicked.
            var dip = PxToDip(px);
            ChaosPopText.Show(dip.X, dip.Y - 30, $"gathering… {_state.RippleText}",
                Color.FromRgb(0x7A, 0xE0, 0xFF));
            return;
        }
        _state.RippleCooldown = _state.RippleRechargeSec;
        // First cast EVER: the verb is learned — retire the ready-cue teach line for good.
        if (!ChaosMeta.State.SeenRippleTeach)
        {
            ChaosMeta.State.SeenRippleTeach = true;
            ChaosMeta.Save();
            ChaosAnnouncerOverlay.Announce("🌊 that's the ripple. it gathers back on its own",
                ChaosAnnounceKind.PowerUp);
        }
        bool skips = _state.MaxedBoons.Contains("skipping_stone");
        CastRippleWave(px);
        if (skips)
        {
            for (int i = 1; i <= 2; i++)
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(i * ChaosTuning.RIPPLE_WAVE_GAP_MS) };
                t.Tick += (_, _) => { t.Stop(); CastRippleWave(px); };
                t.Start();
            }
        }
        _state.PushEvent(skips ? "🌊 the stone skips — three waves" : "🌊 ripple");
    }

    private void CastRippleWave(Point px)
    {
        if (!_spawning || _state == null) return;
        ChaosSfx.PlayRippleCast();
        App.Bubbles?.TriggerPlayerRipple(px, _state.RippleRadiusPx, _state.RippleLifeMs);
    }

    /// <summary>Physical px (mouse hook) → DIPs (ChaosPopText anchors). The HUD window is
    /// alive for the whole run, so its presentation source carries the transform.</summary>
    private Point PxToDip(Point px)
    {
        try
        {
            var v = (Visual?)_hud ?? Application.Current?.MainWindow;
            var t = v == null ? null : PresentationSource.FromVisual(v)?.CompositionTarget?.TransformFromDevice;
            return t?.Transform(px) ?? px;
        }
        catch { return px; }
    }

    /// <summary>Hook callback → marshal to the dispatcher. Keys pass through to whatever has
    /// focus (the hook never swallows); cooldowns absorb accidental fires while typing.</summary>
    private void OnToyKey(System.Windows.Input.Key key)
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted) return;
        disp.BeginInvoke(new Action(() =>
        {
            // The panic key outranks everything — it must still work while manually paused
            // (that's how a held field surfaces). Same key string MainWindow's hook compares.
            var settings = App.Settings?.Current;
            if (settings?.PanicKeyEnabled == true &&
                key.ToString().Equals(settings.PanicKey, StringComparison.OrdinalIgnoreCase))
            { OnPanicKeyDuringRun(); return; }

            if (!_spawning || _state == null || _paused || _manualPaused) return;
            string name = key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9
                ? ((int)(key - System.Windows.Input.Key.D0)).ToString()
                : key.ToString();
            foreach (var toy in _state.ActiveToys)
                if (!string.IsNullOrEmpty(toy.KeyLabel) &&
                    toy.KeyLabel.Equals(name, StringComparison.OrdinalIgnoreCase))
                { UseToy(toy); break; }
        }));
    }

    /// <summary>HUD button entry point.</summary>
    public void UseToyById(string id)
    {
        if (_state == null) return;
        foreach (var t in _state.ActiveToys)
            if (t.Id == id) { UseToy(t); return; }
    }

    private void UseToy(ChaosToyState toy)
    {
        if (_state == null || !_spawning || _paused || _manualPaused) return;
        // The urge sin: your toys are off-limits for the rest of the run.
        if (_state.ActivesDisabled)
        {
            ChaosSfx.Play("toy_denied", 0.4f);
            _state.PushEvent("🫦 the urge holds your hands — no toys");
            return;
        }
        if (!toy.IsReady) { ChaosSfx.Play("toy_denied", 0.4f); return; }
        double power = _state.ToyPower.TryGetValue(toy.Id, out var p) ? p : 0;
        bool maxed = _state.MaxedBoons.Contains(toy.Id);

        switch (toy.Id)
        {
            case "vibe_popping":
                // Hold the button and sweep: everything the cursor passes over pops, live ones snap.
                // Capstone: the hold isn't needed — hovering alone pops.
                App.Bubbles?.SetVibePop(true, hoverPops: maxed);
                _vibeRemainingSec = Math.Max(1, power);
                _afterglowApplied = false;   // each buzz earns one fresh afterglow window
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosSfx.Play("vibe_buzz", 0.5f);   // the soft buzz (vibe_start's spin-up was rejected)
                ChaosVibeTrailOverlay.Start();   // pointer glow + trail while the buzz runs
                ChaosEffectBannerOverlay.Show("vibe", "VibePopping", Color.FromRgb(0xFF, 0xB0, 0x3A));
                _state.PushEvent("🔸 it buzzes. hold and sweep");
                break;

            case "freeze_trigger":
                if (toy.ChargesLeft <= 0) { ChaosSfx.Play("toy_denied", 0.4f); return; }
                toy.ChargesLeft--;
                ChaosSfx.Play("freeze_trigger", 0.6f);
                ActivateFreeze();   // identical to catching a freeze bubble (shows the FREEZE banner)
                if (maxed) App.Bubbles?.DefuseAllLive();
                toy.CooldownRemainingSec = 3;   // anti-doubletap between charges
                toy.IsEffectActive = true;
                _state.PushEvent("❄ everything holds still");
                break;

            case "porn_dvd":
                int lvl = ChaosMeta.BoonLevel(toy.Id);
                double speed = lvl switch { 1 => 0.7, 2 => 0.85, _ => 1.0 };
                double scale = lvl switch { 1 => 0.8, 2 => 0.9, _ => 1.0 };
                ChaosDvdOverlay.Launch(Math.Max(5, power), speed, scale, count: maxed ? 2 : 1,
                    splitBounces: _state.DvdSplitBounces);   // Casting Couch: 2, then 4
                ChaosSfx.Play("dvd_launch", 0.55f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosEffectBannerOverlay.Show("dvd", "Porn DVD", Color.FromRgb(0xFF, 0x69, 0xB4));
                _dvdBannerOn = true;
                _state.PushEvent("📀 now loading");
                break;

            case "snap_field":
                // One clean snap: every live bubble on screen lets go. Capstone clears EVERYTHING —
                // through the PAYING pop paths (PopAllBubbles is the silent wave-janitor wipe:
                // zero pay, zero callbacks, and it severed Snap Chain/Last Breath/Aftermath procs,
                // which made the 1300-Spark capstone a strict downgrade).
                if (maxed) App.Bubbles?.PopAllChaosPaid();
                else App.Bubbles?.DefuseAllLive();
                ChaosSfx.Play("freeze_trigger", 0.5f);
                toy.CooldownRemainingSec = Math.Max(5, power);   // level value IS the cooldown (60/45/30s)
                _snapFlashRemainingSec = 1.0;                    // brief glow on the hero button
                toy.IsEffectActive = true;
                Pulse(Color.FromRgb(150, 220, 255), 0.35);
                _state.PushEvent(maxed ? "✋ snapped. all of it." : "✋ snapped — every live one let go");
                break;

            case "rabbit_caller":
                // Whistle, then point: the cursor glows and the rabbits (plus the capstone
                // storm) arrive wherever the player clicks next — see RabbitAimTick.
                ArmRabbitCall(Math.Max(1, (int)power), maxed);
                ChaosSfx.Play("toy_ready", 0.5f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                _state.PushEvent("🐇 the whistle hangs — your next click calls them");
                break;

            case "e_stim":
                // Charge up: the next N clicks on good bubbles discharge lightning into the
                // bubbles around them. Capstone turns each charged pop into a chain reaction.
                // Overload mantra: the charge runs double.
                int charges = Math.Max(1, (int)power) * Math.Max(1, _state.EStimChargeMult);
                App.Bubbles?.ArmEStim(charges, maxed);
                ChaosEStimOverlay.Arm();   // violet halo rides the cursor while charges wait
                ChaosSfx.Play("toy_ready", 0.5f);
                toy.CooldownRemainingSec = toy.CooldownSec;
                toy.IsEffectActive = true;
                ChaosEffectBannerOverlay.Show("estim", "E-Stim", Color.FromRgb(0x9C, 0x5C, 0xFF));
                Pulse(Color.FromRgb(0x9C, 0x5C, 0xFF), 0.25);
                _state.PushEvent(maxed
                    ? $"⚡ charged — your next {charges} pops chain-react"
                    : $"⚡ charged — your next {charges} pops conduct");
                break;
        }

        // First-times: the first toy ever fired.
        if (!ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Play);
    }

    /// <summary>A charged E-Stim pop just discharged (per-arc juice lives here; the bolts and
    /// the staggered pops are BubbleService's). Drops the banner once the last charge is spent.</summary>
    private void OnEStimArc(int chargesLeft)
    {
        if (_state == null) return;
        ChaosSfx.Play("estim_zap", 0.6f);   // the lightning arc cracks
        Pulse(Color.FromRgb(0x9C, 0x5C, 0xFF), 0.25);
        if (chargesLeft > 0)
        {
            _state.PushEvent($"⚡ the current arcs ({chargesLeft} left)");
        }
        else
        {
            ChaosEffectBannerOverlay.End("estim");
            ChaosEStimOverlay.Disarm();
            _state.PushEvent("⚡ the charge is spent");
        }
    }

    // ---- Snap Field / Rabbit Caller / Intrusive Thoughts transient state ----
    private double _snapFlashRemainingSec;     // brief hero-button glow after a snap
    private int _spawnSerial;                  // Heavy Drop: ordinary-spawn counter (every Nth goes giant)
    private int _ordinarySpawns;               // run-wide spawn count: gates the side-drift entries past the opening
    private bool _pendulumSlowActive;          // the running slow-mo IS the pendulum ("Focus here..." pays now)
    private bool _afterglowApplied;            // Afterglow: one lingering window per buzz
    private int _pendulumRolledWave;           // Pendulum event: wave the swing was last rolled for
    private double _pendulumFireAtProgress;    // 0..1 wave-progress beat the swing fires at
    private bool _pendulumFiredThisWave;
    private int _heartRolledWave;              // Pop-up Notification: wave the heart was last rolled for
    private double _heartFireAtProgress;       // 0..1 wave-progress beat the heart drifts in at
    private bool _heartArmedThisWave;          // this wave's 60% roll landed and hasn't fired yet
    private double _rabbitStormRemainingSec;   // Rabbit Caller capstone: storm window
    private double _rabbitStormAccumSec;       // spawns one rabbit every 1.25s while the storm runs
    private double _thoughtAccumSec;           // Intrusive Thoughts: one bouncing text every 5s

    /// <summary>Intrusive Thoughts: a phrase from the user's bouncing-text pool (fallback included).</summary>
    private static string PickThoughtText()
    {
        try
        {
            var pool = App.Settings?.Current?.BouncingTextPool;
            if (pool != null)
            {
                var enabled = pool.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
                if (enabled.Count > 0) return enabled[Random.Shared.Next(enabled.Count)];
            }
        }
        catch { }
        return "GIVE IN";
    }

    /// <summary>Force-spawn one white rabbit right now (Rabbit Caller; storm ticks reuse it).
    /// Optional physical-px point pins the spawn there (the summon-at-click).</summary>
    private void SpawnDarter(double? atPxX = null, double? atPxY = null)
    {
        if (_state == null) return;
        var spec = ChaosBubbleVariants.BuildDarter(_state.RunIntensity, spotlight: false, atPxX, atPxY);
        ChaosMeta.MarkDiscovered("bubble:darter");
        App.Bubbles?.SpawnChaosBubble(spec);
    }

    // ---- Rabbit Caller: whistle, then point — the rabbits arrive at your next click ----
    private System.Windows.Threading.DispatcherTimer? _rabbitAimTimer;
    private int _rabbitCallPending;          // rabbits waiting on the click (0 = not armed)
    private bool _rabbitCallMaxed;           // capstone storm rides the summon
    private bool _rabbitAimPrevDown;         // press-edge detection (starts true: the arming click)

    /// <summary>Arm the whistle: the cursor glows, and the next click summons the rabbits there.</summary>
    private void ArmRabbitCall(int rabbits, bool maxed)
    {
        _rabbitCallPending = rabbits;
        _rabbitCallMaxed = maxed;
        _rabbitAimPrevDown = true;   // swallow the press that armed us (HUD/toy-button click)
        if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.ArmCursorGlow(); else ChaosCursorGlowOverlay.Arm();
        if (_rabbitAimTimer == null)
        {
            // Render priority (not the DispatcherTimer default of Background, which starves under load
            // and made the cursor glow "stick"/skip frames whenever a chaos event spiked the UI thread).
            _rabbitAimTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render)
            { Interval = TimeSpan.FromMilliseconds(16) };
            _rabbitAimTimer.Tick += RabbitAimTick;
        }
        _rabbitAimTimer.Start();
    }

    /// <summary>Stand down without summoning (run end / teardown).</summary>
    private void DisarmRabbitCall()
    {
        _rabbitCallPending = 0;
        _rabbitAimTimer?.Stop();
        if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.DisarmCursorGlow(); else ChaosCursorGlowOverlay.Disarm();
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
            if (!GetCursorPos(out var cur)) return;
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.MoveCursorGlowToPx(cur.X, cur.Y); else ChaosCursorGlowOverlay.MoveToPx(cur.X, cur.Y);

            bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool pressed = down && !_rabbitAimPrevDown;
            _rabbitAimPrevDown = down;
            // The field must be live for the summon (drafts/pauses hold the whistle).
            if (!pressed || _paused || _manualPaused || !_spawning) return;

            int rabbits = _rabbitCallPending;
            bool maxed = _rabbitCallMaxed;
            DisarmRabbitCall();
            for (int i = 0; i < rabbits; i++)
            {
                // A small scatter so a triple summon doesn't stack into one spot.
                double jx = cur.X + Random.Shared.Next(-60, 61);
                double jy = cur.Y + Random.Shared.Next(-60, 61);
                SpawnDarter(jx, jy);
            }
            if (maxed)
            {
                _rabbitStormRemainingSec = 10.0;             // ~8 more over the next 10s
                _rabbitStormAccumSec = 0;
                ChaosEffectBannerOverlay.Show("rabbits", "Rabbit Storm", Color.FromRgb(0xFF, 0x4D, 0xC4));
            }
            ChaosSfx.Play("rabbit_spawn", 0.5f);
            _state.PushEvent(maxed ? $"🐇 {rabbits} at your fingertip… and the burrow is emptying"
                                   : $"🐇 {rabbits} answered at your fingertip");
        }
        catch (Exception ex) { App.Logger?.Debug("RabbitAimTick: {E}", ex.Message); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    private const int VK_LBUTTON = 0x01;

    private void TickActiveToys(double dt)
    {
        if (_state == null) return;
        if (_vibeRemainingSec > 0)
        {
            _vibeRemainingSec -= dt;
            if (_vibeRemainingSec <= 0)
            {
                // Afterglow mantra: the buzz doesn't quite stop — hovering keeps popping and the
                // trail keeps drawing for the lingering window, then it truly ends.
                if (_state.AfterglowSec > 0 && !_afterglowApplied)
                {
                    _afterglowApplied = true;
                    _vibeRemainingSec = _state.AfterglowSec;
                    App.Bubbles?.SetVibePop(true, hoverPops: true);
                    _state.PushEvent("🔸 afterglow — it lingers");
                }
                else
                {
                    App.Bubbles?.SetVibePop(false);
                    ChaosVibeTrailOverlay.Stop();
                    ChaosEffectBannerOverlay.End("vibe");
                }
            }
        }
        if (_snapFlashRemainingSec > 0) _snapFlashRemainingSec -= dt;
        // Pendulum: a trained habit again (re-gated 2026-06-11 after a day as a free event) —
        // at a random beat of every loop the world dips into slow-mo on its own, announced on top.
        if (_spawning && _state.Config.PendulumSwing && _state.WaveIndex != _pendulumRolledWave)
        {
            _pendulumRolledWave = _state.WaveIndex;
            _pendulumFireAtProgress = 0.15 + Random.Shared.NextDouble() * 0.65;   // somewhere mid-loop
            _pendulumFiredThisWave = false;
        }
        if (_spawning && _state.Config.PendulumSwing
            && !_pendulumFiredThisWave && _state.WaveProgress >= _pendulumFireAtProgress
            && _freezeRemainingSec <= 0 && _slowMoRemainingSec <= 0)
        {
            _pendulumFiredThisWave = true;
            ActivateSlowMo(2.5, bannerLabel: "Pendulum");   // so the swing reads as ITS OWN event, not a darter
            ChaosSfx.PlayTickTock();   // tick-tock underlay while time hangs (silent until the asset ships)
            if (_state.PendulumPayMult > 1)   // the mantra turns the swing into a scoring window
                ChaosAnnouncerOverlay.Announce("🕰 FOCUS HERE — everything pays x3", ChaosAnnounceKind.PowerUp,
                    artKey: "focus_here", subText: "everything pays x3");
            else
                ChaosAnnouncerOverlay.Announce("🕰 the pendulum swings", ChaosAnnounceKind.PowerUp,
                    artKey: "pendulum");
            _state.PushEvent("🕰 the pendulum swings");
        }
        // Pop-up Notification habit: once per loop, sometimes (60%), a little heart drifts
        // down at a random beat. Catch = +1 resistance; a miss just exits the bottom.
        if (_state.Config.PopupHeartEnabled && _spawning && _state.WaveIndex != _heartRolledWave)
        {
            _heartRolledWave = _state.WaveIndex;
            _heartArmedThisWave = Random.Shared.NextDouble() < 0.60;
            _heartFireAtProgress = 0.20 + Random.Shared.NextDouble() * 0.60;
        }
        if (_heartArmedThisWave && _spawning && _state.WaveProgress >= _heartFireAtProgress)
        {
            _heartArmedThisWave = false;
            App.Bubbles?.SpawnChaosBubble(ChaosBubbleVariants.BuildHeart());
            App.Bubbles?.PlayChime(0.22f);   // the soft ping — a notification, after all
        }
        // Rabbit Caller capstone storm: one more rabbit every 1.25s for the window's length.
        if (_rabbitStormRemainingSec > 0)
        {
            _rabbitStormRemainingSec -= dt;
            _rabbitStormAccumSec += dt;
            if (_rabbitStormAccumSec >= 1.25) { _rabbitStormAccumSec = 0; SpawnDarter(); }
            if (_rabbitStormRemainingSec <= 0) ChaosEffectBannerOverlay.End("rabbits");
        }
        // Intrusive Thoughts: every 5s a bouncing text races across on its own (30% faster than
        // the DVD's; level sets its 3/4/5s life). Holds its tongue while time is frozen.
        if (_state.IntrusiveThoughtsSec > 0 && _freezeRemainingSec <= 0)
        {
            _thoughtAccumSec += dt;
            if (_thoughtAccumSec >= 5.0)
            {
                _thoughtAccumSec = 0;
                ChaosDvdOverlay.Launch(_state.IntrusiveThoughtsSec, 1.3, 0.8, count: 1,
                    text: PickThoughtText(),
                    splitOnRabbit: _state.MaxedBoons.Contains("intrusive_thoughts"));
            }
        }
        foreach (var t in _state.ActiveToys)
        {
            if (t.CooldownRemainingSec > 0)
            {
                t.CooldownRemainingSec -= dt;
                // Ready-again ding the instant the cooldown lapses (spent charge-toys stay quiet).
                if (t.CooldownRemainingSec <= 0 && t.ChargesLeft != 0) ChaosSfx.Play("toy_ready", 0.45f);
            }
            // Glow state: each toy knows whether its own temporary effect is still running.
            t.IsEffectActive = t.Id switch
            {
                "vibe_popping" => _vibeRemainingSec > 0,
                "freeze_trigger" => _freezeRemainingSec > 0,
                "porn_dvd" => ChaosDvdOverlay.AnyToyActive,
                "snap_field" => _snapFlashRemainingSec > 0,
                "rabbit_caller" => _rabbitCallPending > 0 || _rabbitStormRemainingSec > 0,
                "e_stim" => (App.Bubbles?.EStimChargesLeft ?? 0) > 0,
                _ => false,
            };
        }
        if (_dvdBannerOn && !ChaosDvdOverlay.AnyToyActive) { ChaosEffectBannerOverlay.End("dvd"); _dvdBannerOn = false; }
    }

    // ---- freeze-bubble power-up ----
    private const double FREEZE_DURATION_SEC = 3.5;   // real-time length of the freeze
    private const double FREEZE_DURATION_MULT = 2.5;  // flash/overlay payloads linger this much longer while frozen
    private const int    FREEZE_VIBRATE_MS    = 200;  // whole-field shudder as the freeze lands
    private const int    FREEZE_BASE_POINTS   = 140;
    private static readonly Color FREEZE_EDGE = Color.FromRgb(150, 210, 255);   // icy white-blue
    private double _freezeRemainingSec;

    private void ActivateFreeze()
    {
        _freezeRemainingSec = FREEZE_DURATION_SEC;
        _freezeCueOn = true;   // the catch/toy cue covers the way in; the shatter covers the way out
        App.Bubbles?.SetChaosFrozen(true);
        App.Bubbles?.VibrateAllForFreeze(FREEZE_VIBRATE_MS);
        ChaosEffectBannerOverlay.Show("freeze", "Freeze", Color.FromRgb(0x96, 0xD2, 0xFF));
        EffectPayload.GlobalDurationMult = FREEZE_DURATION_MULT;
        if (_state?.Config?.ColorFlashesEnabled == true)
        {
            _fx?.FreezeBurst(FREEZE_EDGE);        // icy "ice hit" flash
            _fx?.BeginEdgeHold(FREEZE_EDGE, 0.5); // sustained white-blue edges for the frozen window
        }
        Shake(0.3, FREEZE_VIBRATE_MS);
    }

    private bool _freezeCueOn;   // the field is audibly frozen; the shatter is owed on release

    private void EndFreeze()
    {
        _freezeRemainingSec = 0;
        App.Bubbles?.SetChaosFrozen(false);
        ChaosEffectBannerOverlay.End("freeze");
        if (_freezeCueOn) ChaosSfx.Play("freeze_shatter", 0.5f);
        _freezeCueOn = false;
        if (_slowMoRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // don't clobber an active slow-mo
        _fx?.EndEdgeHold();
    }

    private void CheckComboMilestone()
    {
        if (_state == null) return;
        int combo = _state.Combo;
        if (combo <= 0) return;

        // Big-combo crossing: edge-detected, fires ONCE per threshold per streak.
        foreach (int t in COMBO_BIG_THRESHOLDS)
        {
            if (combo >= t && _lastComboBigFired < t)
            {
                _lastComboBigFired = t;
                _state.PushEvent($"🔥🔥 STREAK x{combo}!");
                App.Bark?.NotifyChaosComboBig(combo, t);
                ChaosSfx.Play("streak_milestone", 0.5f);
                ChaosAnnouncerOverlay.Announce($"STREAK ×{t}", ChaosAnnounceKind.Streak,
                    artKey: "streak", subText: $"×{t}");
                Pulse(COMBO_BIG_COLOR, COMBO_BIG_PULSE);   // distinct bigger beat
            }
        }

        if (combo % 10 == 0)
        {
            _state.PushEvent($"🔥 streak x{combo}!");
            App.Bark?.NotifyChaosComboMilestone(combo, _state.Config.Difficulty.ToString());
            Pulse(Color.FromRgb(255, 200, 60), 0.4);   // gold combo flash
        }
        else
        {
            // Micro-build between milestones: a tiny pulse that grows toward the next ×10.
            double frac = (combo % 10) / 10.0;
            double strength = COMBO_MICRO_PULSE_MIN + (COMBO_MICRO_PULSE_MAX - COMBO_MICRO_PULSE_MIN) * frac;
            Pulse(COMBO_MICRO_COLOR, strength);
        }
    }

    /// <summary>Fire ChaosActChanged once when ActIndex advances (edge-detected, not per tick).</summary>
    private void FireActChangedIfCrossed()
    {
        if (_state == null) return;
        if (_state.ActIndex > _lastActFired)
        {
            _lastActFired = _state.ActIndex;
            App.Bark?.NotifyChaosActChanged(_state.ActIndex, _state.WaveIndex);
            ChaosSfx.Play("depth_change", 0.55f);
            ChaosAnnouncerOverlay.Announce($"DEPTH {_state.ActIndex}", ChaosAnnounceKind.Depth,
                artKey: "depth", subText: $"{_state.ActIndex}");
            // Zone border: swap the backdrop plate and let the Madam mark the descent.
            ChaosBackdropService.SwapTo(_state.ActIndex);
            ChaosTunnelService.SendZoneHint(_state.ActIndex, Math.Min(1.0, (_state.ActIndex - 1) / 5.0));
            // Depth V is a STORY card moment, not a reactive line — defer it past this transition's
            // draft/ReadyGo (which would overwrite the card) and open it once the field resumes.
            if (_state.ActIndex >= 5) _pendingDepthVCard = true;
            else ChaosNarrativeHooks.OnMoment("zone_border", BuildNarrativeCtx());
        }
    }

    private bool _pendingDepthVCard;   // entered depth V this transition → open the card after resume

    /// <summary>Make heat visible: a subtle rising temperature tint as Heat climbs (additive
    /// held-edge overlay only — never touches scoring/multiplier math). Honors the color-flashes toggle.</summary>
    private void UpdateHeatTint()
    {
        if (_state?.Config?.ColorFlashesEnabled != true || _fx == null) return;
        double heat = _state.Heat;
        double level = heat <= HEAT_TINT_MIN
            ? 0
            : (heat - HEAT_TINT_MIN) / (1.0 - HEAT_TINT_MIN);   // 0..1 above the floor
        // Quantize so we don't re-issue an animation every 250ms tick.
        double q = Math.Round(level, 1);
        if (Math.Abs(q - _lastHeatTint) < 0.05) return;
        _lastHeatTint = q;
        if (q <= 0) _fx.EndHeatTint();
        else _fx.SetHeatTint(HEAT_TINT_COLOR, q * HEAT_TINT_MAX_OPACITY);
    }

    // ============================ end / teardown ============================

    public void RequestStop()
    {
        // Surfacing from a held field: drop the pause first so its freeze/input-lock state
        // never outlives the run (EndChaosMode clears the bubble side; this clears ours).
        if (_manualPaused) { _manualPaused = false; _hud?.SetPausedUi(false); }
        if (_spawning) EndRun();
    }

    /// <summary>
    /// Hard teardown for app exit / main-window close: stop everything, clear all
    /// chaos bubbles, close the HUD + overlay, resume the ambient game. No results,
    /// no payout. Safe to call when no run is active.
    /// </summary>
    public void ForceShutdown()
    {
        if (!_active && _hud == null && _overlay == null) return;
        _spawning = false;
        _active = false;
        _paused = false;
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        try { App.Bubbles?.EndChaosMode(); } catch { }
        try { App.Bubbles?.Resume(); } catch { }
        StopKeyHook();
        CloseToyButtons();
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
        try { ChaosAnnouncerOverlay.CloseActive(); } catch { }
        try { ChaosUnlockCardOverlay.CloseActive(); } catch { }
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { ChaosBoonBarOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosSkiaFxOverlay.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        try { _fx?.Close(); } catch { }
        if (_overlay != null)
        {
            _overlay.OnDismissed = null;   // avoid re-entrant cleanup
            _overlay.OnRunAgain = null;
            try { _overlay.Close(); } catch { }
        }
        try { _hud?.Close(); } catch { }
        CleanupAfterRun();
    }

    private void EndRun()
    {
        if (!_spawning || _state == null) return;
        LogMemSample("run-end");
        ChaosCrashSentinel.Clear();   // the field is coming down — a vanish after here isn't a run crash
        try { System.Windows.Media.CompositionTarget.Rendering -= OnChaosRendering; } catch { }
        bool ranFullCourse = _state.ElapsedSec >= _state.RunDurationSec;
        // The final loop ends at the run clock, not a wave boundary — its tip lands here
        // (full-course descents only; a quit mid-fall forfeits the loop's tip).
        if (ranFullCourse) AwardLoopTip();
        // Lessons: final-loop + end-of-descent judgments (popup_notification / extreme_tier /
        // silk_touch). A quit mid-fall (RequestStop) didn't run the full course.
        ChaosLessonHooks.OnRunCompleted(_state.Shields, _state.Config.Difficulty,
            ranFullCourse: ranFullCourse);
        _spawning = false;
        if (App.Video != null) App.Video.VideoStarted -= OnVideoStartedDuringRun;
        if (App.Video != null) App.Video.VideoEnded -= OnVideoEndedDuringRun;
        _runTimer?.Stop();
        _spawnTimer?.Stop();
        StopKeyHook();
        StopRippleHook();
        CloseToyButtons();
        App.Bubbles?.EndChaosMode();
        EndSlowMo(); EndFreeze();
        try { ChaosFlashOverlay.CloseActive(); } catch { }
        try { ChaosGifCascadeOverlay.CloseActive(); } catch { }
        try { ChaosAnnouncerOverlay.CloseActive(); } catch { }
        try { ChaosUnlockCardOverlay.CloseActive(); } catch { }
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { ChaosBoonBarOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosSkiaFxOverlay.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        try { _fx?.Close(); } catch { }
        _fx = null;

        double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;
        double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
        double baseXp = Math.Min(_state.Score, capBase);
        double skillMult = _state.SkillMult;
        double finalXp = baseXp * skillMult;

        try { App.Progression?.AddXP(baseXp, XPSource.Chaos); }
        catch (Exception ex) { App.Logger?.Debug("Chaos payout AddXP: {E}", ex.Message); }

        // Capture the previous best BEFORE the meta award updates it — for the PB delta line/bark.
        long previousBest = ChaosMeta.State.BestScore;

        // Meta-progression: bank Sparks + update lifetime stats (separate from XP payout).
        int sparksEarned = 0;
        try { sparksEarned = ChaosMeta.AwardRunRewards(_state); }
        catch (Exception ex) { App.Logger?.Debug("Chaos meta award: {E}", ex.Message); }

        // RunsCompleted just moved — queue any freshly crossed reveals for the next dollhouse open.
        try { RevealService.Sync("run_end"); } catch { }

        // Rank spine: did this descent push the rank past the last card shown? Only the
        // HIGHEST new rank gets a card (a debug fast-forward skips the ones in between).
        ChaosRank? rankUp = null;
        try
        {
            var nowRank = ChaosRanks.For(ChaosMeta.State.RunsCompleted);
            if ((int)nowRank > ChaosMeta.State.LastRankSeen) rankUp = nowRank;
        }
        catch { }

        string diff = _state.Config.Difficulty.ToString();
        App.Bark?.NotifyChaosRunCompleted((int)finalXp, diff);

        _hud?.Close();
        _hud = null;
        _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned, rankUp);

        App.Bubbles?.Resume();
        App.Logger?.Information("Chaos run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP (defused {D}, detonated {Det})",
            baseXp, skillMult, finalXp, _state.Defused, _state.Detonated);
    }

    private void RunAgain()
    {
        // Tear down the current run windows, then start a fresh one (short 1s GO flash on restart).
        _overlay?.Close();   // triggers OnOverlayClosed → CleanupAfterRun
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => StartRun(isRestart: true)));
    }

    private void OnOverlayClosed()
    {
        // Results dismissed (or window closed mid-run). Ensure everything is torn down.
        if (_spawning)
        {
            _spawning = false;
            _runTimer?.Stop();
            _spawnTimer?.Stop();
            App.Bubbles?.EndChaosMode();
            App.Bubbles?.Resume();
        }
        try { _hud?.Close(); } catch { }
        CleanupAfterRun();
    }

    private void CleanupAfterRun()
    {
        ChaosCrashSentinel.Clear();   // every run teardown path funnels through here — disarm the sentinel
        try { System.Windows.Media.CompositionTarget.Rendering -= OnChaosRendering; } catch { }
        if (App.Video != null) App.Video.VideoStarted -= OnVideoStartedDuringRun;   // belt-and-suspenders (mid-run close)
        if (App.Video != null) App.Video.VideoEnded -= OnVideoEndedDuringRun;
        StopKeyHook();   // idempotent; covers the overlay-closed-mid-run path
        StopRippleHook();
        CloseToyButtons();
        try { ChaosDvdOverlay.CloseActive(); } catch { }
        try { ChaosEffectBannerOverlay.CloseActive(); } catch { }
        try { ChaosWaveTimerOverlay.CloseActive(); } catch { }
        try { ChaosBoonBarOverlay.CloseActive(); } catch { }
        try { DisarmRabbitCall(); ChaosCursorGlowOverlay.CloseActive(); } catch { }
        try { ChaosVibeTrailOverlay.CloseActive(); } catch { }
        try { ChaosEStimOverlay.CloseActive(); } catch { }
        try { ChaosFieldFxOverlay.CloseActive(); } catch { }
        try { ChaosSkiaFxOverlay.CloseActive(); } catch { }
        try { ChaosBackdropService.CloseActive(); } catch { }
        try { ChaosTunnelService.CloseActive(); } catch { }
        try { ChaosPopText.ShutdownPool(); } catch { }
        App.AvatarWindow?.SetChaosRunActive(false);   // restore the avatar's normal attached z-order
        ChaosHappyPath.OnRunEnded();   // the script never outlives its run (idempotent)
        ChaosNarrativeHooks.OnRunEnded();   // drop the Madam's run-scoped state + any duck
        _runTimer = null;
        _spawnTimer = null;
        _hud = null;
        _overlay = null;
        _fx = null;
        if (_state != null) _state.PropertyChanged -= OnStateChangedForTunnel;
        _state = null;
        _active = false;
        _spawning = false;
        _paused = false;
        _manualPaused = false;
        // Back to the immersive default so hub-side narrative + a fresh run behave as Story until a
        // run explicitly opts into Free Desktop again.
        ActiveMode = ChaosPlayMode.Story;
        ChaosWindowZ.DesktopMode = false;
        // Reset the topmost-pin too. It's set from ChaosPinOnTop at StartRun and was leaking
        // across runs: a Free Desktop run with pin-off left PinTopmost=false, so a later
        // DASHBOARD glitch/cascade bubble built its keep-alive overlay non-topmost and it
        // rendered behind the main window (invisible). Dashboard effects must default to pinned.
        ChaosWindowZ.PinTopmost = true;
        _lessonCardPaused = false;
        _lessonCardsAfterDraft = false;
        _pendingLessonCards.Clear();
    }
}
