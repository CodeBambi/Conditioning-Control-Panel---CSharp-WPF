using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Bark;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Reactive companion-dialogue ("bark") system. Modeled on <see cref="GamificationBridge"/>:
    /// a single seam that SUBSCRIBES directly to the events feature services already raise and
    /// decides which bark — if any — should be spoken in response.
    ///
    /// PR1 scope: rule LOADER + full subscription block + counter layer + DRY-RUN matcher.
    /// There is no speak path yet — every decision is logged ("[BARK dry-run] …"), nothing is
    /// rendered to the avatar. The speak path (GigglePriority/Giggle, self-echo mute,
    /// chat-suppression, easter-egg clip) lands in a follow-up PR.
    /// </summary>
    public class BarkService : IDisposable
    {
        private bool _started;
        private readonly object _gate = new();

        private BarkRuleSet _rules = BarkRuleSet.Empty;
        private readonly BarkState _state = new();

        // Subscription teardown (named delegates captured so -= matches +=).
        private readonly List<Action> _unsubscribe = new();
        private readonly List<Action> _engineUnsubscribe = new();
        private readonly List<Action> _trayUnsubscribe = new();

        // --- reused gate primitives (cooldown dict / global min-gap / one-fire latch / variant rotation) ---
        private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
        // Per-rule variant rotation, keyed by the variant's stable LINE ID (not index) so it survives
        // content edits AND can be PERSISTED across sessions (AppSettings.BarkVariantRotation). This is
        // the hard no-repeat-until-exhausted guarantee: a rule never replays a line until every line in
        // its pool has been spoken this cycle. Loaded from settings on Start().
        private readonly Dictionary<string, HashSet<string>> _usedVariantKeys = new(StringComparer.OrdinalIgnoreCase);
        // Last variant line id fired per rule — used to avoid an immediate repeat when a repeatable
        // pool is exhausted and recycled (see EvaluateGate).
        private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _globalLastFireUtc = DateTime.MinValue;

        // Audio filenames of the last few barks spoken across ALL rules — a soft, global de-dupe.
        // Per-rule variant rotation (_usedVariantKeys) only stops a rule repeating ITS OWN lines; it
        // does nothing for the felt repetition of a high-frequency rule (flash/subliminal) cycling
        // its small pool over a long session, nor for two rules echoing the same line back-to-back.
        // Variant selection prefers a line whose audio isn't in this window — a preference, not a
        // hard gate (if every candidate is recent we still speak rather than go silent).
        private readonly Queue<string> _recentlySpoken = new();
        private readonly HashSet<string> _recentlySpokenSet = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>How many distinct just-spoken lines to avoid replaying across all rules.</summary>
        private const int RecentlySpokenMemory = 8;

        /// <summary>
        /// Hard global cooldown between any two reactive/idle barks (temporary — a user-facing slider
        /// is the planned follow-up). Users reported she "runs her mouth constantly" and that reactive
        /// lines (esp. the webcam FaceFound "back into frame" line) ignore the interval sliders; a 60s
        /// floor keeps her from firing more than once a minute. Safety (Panic) and `guaranteed` barks
        /// bypass this (see the `!bypass` block in EvaluateGate), so this never mutes a panic response
        /// or a one-shot milestone.
        /// </summary>
        private const int GlobalMinGapMs = 60000;

        /// <summary>
        /// Triggers that skip ONLY the hard global min-gap floor above (they keep their own per-rule
        /// cooldown_ms and every other gate). These are FUNCTIONAL corrections that must keep their
        /// authored cadence rather than flavor chatter — currently the mandatory-video attention-check
        /// fail ("eyes on the screen, pay attention"), which authors a 20s cooldown. Trigger keys are
        /// app-raised constants, so a mod's rule under this trigger inherits the exemption.
        /// </summary>
        private static readonly HashSet<string> GlobalGapExemptTriggers =
            new(StringComparer.OrdinalIgnoreCase) { "AttentionCheckFail" };

        /// <summary>Barks at/above this priority (or any non-Normal class) speak via GigglePriority; lower ones queue via Giggle.</summary>
        private const int PriorityBarkThreshold = 100;

        /// <summary>After rendering a bark, mute its line this long so it can't re-trigger awareness/OCR.</summary>
        private const int SelfEchoMuteMs = 8000;

        /// <summary>How long a safety bark holds the floor (no non-safety bark may fire). Approximate — we have no speech-duration callback.</summary>
        private const int SafetyHoldMs = 6000;

        private static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan RapidClickWindow = TimeSpan.FromSeconds(60);

        // Coin flip is varied per call; not security-sensitive.
        private readonly Random _rng = new();
        private DateTime _lastUserMessageUtc = DateTime.MinValue;
        private DateTime _safetyHoldUntilUtc = DateTime.MinValue;
        private PatreonTier _lastTier = PatreonTier.None; // to detect tier-up for the upgrade egg

        // --- idle chatter (item F): pool-wide no-repeat across all eligible Idle rules.
        // Cadence is OWNED by AvatarTubeWindow's idle timer (companion-tab slider,
        // IdleGiggleIntervalSeconds) which calls DispatchIdle() — barks ARE the idle chatter now.
        private readonly HashSet<string> _usedIdleRules = new(StringComparer.OrdinalIgnoreCase);
        private string? _lastIdleRuleId;
        /// <summary>Chance an idle tick prefers an eligible band-gated idle rule over the ungated majority.</summary>
        private const double GatedIdleBias = 0.35;

        // --- anticipatory SessionSetupReady detector (item E): debounced, fires when the user goes quiet ---
        private System.Windows.Threading.DispatcherTimer? _setupReadyTimer;
        private bool _setupReadyStallPending; // true between the first ready-fire and the stall follow-up
        private const int SetupReadyDebounceMinSec = 8;
        private const int SetupReadyDebounceMaxSec = 12;
        private const int SetupReadyStallSec = 45; // gives setup_ready_stall (setup_idle_sec_gte:45) its window

        /// <summary>
        /// When true the matcher evaluates and logs ("[BARK dry-run] …") but never renders to the
        /// avatar and never writes persisted latches. Speak path is otherwise fully exercised.
        /// </summary>
        public bool DryRun { get; set; } = false;

        public BarkState State => _state;

        // ===================== lifecycle =====================

        public void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                // Opt-in log-only mode for validation: run with CCP_BARK_DRYRUN=1 to exercise the
                // full matcher (logs "[BARK dry-run] …") without rendering to the avatar.
                if (string.Equals(Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN"), "1", StringComparison.Ordinal))
                    DryRun = true;

                _rules = BarkRuleLoader.Load();
                LoadRotationFromSettings(); // restore persisted no-repeat rotation so pools don't reset each launch
                // Instant-relaunch egg threshold: relaunched within 60s of last seen.
                _state.CaptureLaunchRecency(App.Settings?.Current?.LastSeenUtc, instantThresholdSeconds: 60);
                _lastTier = App.Patreon?.CurrentTier ?? PatreonTier.None;

                WireSubscriptions();

                App.Logger?.Information(
                    "BarkService started — {Count} rules, {Triggers} trigger keys, dry-run={DryRun}",
                    _rules.Count, _rules.Triggers.Count(), DryRun);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "BarkService failed to start");
            }
        }

        public void Stop()
        {
            if (!_started) return;
            _started = false;

            try { _setupReadyTimer?.Stop(); } catch { }
            _setupReadyTimer = null;

            RunUnsubscribers(_unsubscribe);
            RunUnsubscribers(_engineUnsubscribe);
            RunUnsubscribers(_trayUnsubscribe);
        }

        private static void RunUnsubscribers(List<Action> list)
        {
            foreach (var u in list)
            {
                try { u(); } catch (Exception ex) { App.Logger?.Debug(ex, "BarkService: unsubscribe failed"); }
            }
            list.Clear();
        }

        public void Dispose() => Stop();

        /// <summary>
        /// External entry point for the panic/abort key (MainWindow.HandlePanicKeyPress). Raises the
        /// safety-class "Panic" bark, which bypasses the gate and holds the floor.
        /// </summary>
        public void NotifyPanic() => Raise("Panic");

        /// <summary>
        /// The app was just opened — fires the voiced, time-aware welcome greeting. The bucket
        /// (first/soon/back/while/long) reflects how long the user has been away so rules can
        /// select an appropriately-warm line. Returns whether a greeting bark actually spoke so
        /// the caller can fall back to the legacy text-only greeting when the bark system is
        /// unavailable.
        /// </summary>
        public bool NotifyAppOpened(string awayBucket)
            => Raise("AppOpened", c => c.Set("away_bucket", awayBucket ?? "first"));

        /// <summary>
        /// A daily-login-streak milestone (7/14/30/60/100/365 days) was reached for the first
        /// time. Caller owns the once-per-milestone latch (AppSettings.LastAnnouncedStreakMilestone);
        /// guaranteed so it isn't dropped by the min-gap behind the welcome greeting it queues after.
        /// </summary>
        public void NotifyStreakMilestone(int days)
            => Raise("StreakMilestone", c => c.Set("streak_days", (double)days), guaranteed: true);

        /// <summary>A dashboard feature popup was opened (feature = control type name w/o "FeatureControl", e.g. "Flash").</summary>
        public void NotifyFeatureOpened(string? feature)
        {
            if (string.IsNullOrEmpty(feature)) return;
            Raise("FeatureOpened", c => c.Set("feature", feature!));
        }

        /// <summary>The user navigated to a tab (tab = ShowTab key, e.g. "leaderboard").</summary>
        public void NotifyTabNavigated(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return;
            Raise("TabNavigated", c => c.Set("tab", tab!));
        }

        /// <summary>A numeric/bool setting changed. Non-numeric props are ignored; value is bool→0/1, enum→int.</summary>
        public void NotifySettingChanged(string? setting)
        {
            if (string.IsNullOrEmpty(setting)) return;
            // Setup-screen tracking for the anticipatory SessionSetupReady bark — ANY setting counts as
            // a "setup action" (even non-numeric ones that the SettingChanged rules below can't match).
            _state.MarkSettingChanged();
            ArmSetupReadyDebounce();
            if (!TryGetSettingNumber(setting!, out var value)) return;
            Raise("SettingChanged", c => c.Set("setting", setting!).Set("value", value));
        }

        /// <summary>The user changed the avatar appearance/skin (avatar-set selector). Bambi reacts ~1-in-10.</summary>
        public void NotifyAvatarChanged()
        {
            Raise("AvatarChanged");
        }

        /// <summary>The avatar was clicked. Stamps a rolling 60s click count for the escalation eggs.</summary>
        public void NotifyAvatarClicked()
        {
            _state.RegisterAvatarClick();
            Raise("AvatarClicked", c => c.Set("clicks_60s", (double)_state.AvatarClicksWithin(RapidClickWindow)));
        }

        /// <summary>An enhancement was applied to the current browser page (id = enhancement name — no slug field exists).</summary>
        public void NotifyEnhancementApplied(string? enhancementId)
        {
            if (string.IsNullOrEmpty(enhancementId)) return;
            Raise("EnhancementApplied", c => c.Set("enhancement_id", enhancementId!));
        }

        /// <summary>A generic UI action the user took (action = stable key, e.g. "test_video", "reroll_daily", "minimize").</summary>
        public void NotifyUiAction(string? action)
        {
            if (string.IsNullOrEmpty(action)) return;
            Raise("UiAction", c => c.Set("action", action!));
        }

        /// <summary>The user opened/refreshed the leaderboard. rank/total let rules react to standing (rank 0 = unranked).</summary>
        public void NotifyLeaderboardViewed(int rank, int total)
        {
            Raise("LeaderboardViewed", c => c.Set("rank", (double)rank).Set("total", (double)total));
        }

        // ===================== Chaos Mode (Lab effect-bubbles roguelite) =====================
        /// <summary>A Chaos run started. ctx: difficulty.</summary>
        public void NotifyChaosRunStarted(string difficulty) => Raise("ChaosRunStarted", c => c.Set("difficulty", difficulty));
        /// <summary>The run escalated into a new wave. ctx: wave (number).</summary>
        public void NotifyChaosWaveEscalated(int wave) => Raise("ChaosWaveEscalated", c => c.Set("wave", (double)wave));
        /// <summary>A live bubble was defused in time. ctx: combo, payload (variant id), difficulty.</summary>
        public void NotifyChaosBubbleDefused(int combo, string payload, string difficulty) =>
            Raise("ChaosBubbleDefused", c => c.Set("combo", (double)combo).Set("payload", payload).Set("difficulty", difficulty));
        /// <summary>An UNSHIELDED live bubble detonated and fired its payload (real hit). ctx: payload (variant id),
        /// strength, shield_absorbed(false), run_detonations, combo (captured before the break), difficulty.</summary>
        public void NotifyChaosBubbleDetonated(string payload, double strength, double runDetonations, int combo, string difficulty) =>
            Raise("ChaosBubbleDetonated", c => c
                .Set("payload", payload).Set("strength", strength).Set("shield_absorbed", false)
                .Set("run_detonations", runDetonations).Set("combo", (double)combo).Set("difficulty", difficulty));
        /// <summary>A clutch shield-save: a live bubble's detonation was absorbed by a shield. ctx: payload (variant id),
        /// strength, shield_absorbed(true), run_detonations, combo, difficulty, shields_left.</summary>
        public void NotifyChaosBubbleDetonatedAbsorbed(string payload, double strength, double runDetonations, int combo, string difficulty, int shieldsLeft) =>
            Raise("ChaosBubbleDetonatedAbsorbed", c => c
                .Set("payload", payload).Set("strength", strength).Set("shield_absorbed", true)
                .Set("run_detonations", runDetonations).Set("combo", (double)combo).Set("difficulty", difficulty)
                .Set("shields_left", (double)shieldsLeft));
        /// <summary>A benign treat bubble was popped. ctx: variant_id, payload (display name), combo.</summary>
        public void NotifyChaosBenignPopped(string variantId, string payload, int combo) =>
            Raise("ChaosBenignPopped", c => c.Set("variant_id", variantId).Set("payload", payload).Set("combo", (double)combo));
        /// <summary>A boon was drafted. ctx: boon (name).</summary>
        public void NotifyChaosBoonPicked(string boon) => Raise("ChaosBoonPicked", c => c.Set("boon", boon));
        /// <summary>A curse was drafted (fired instead of ChaosBoonPicked for curses). ctx: boon (name), rarity, run_mult_bonus.</summary>
        public void NotifyChaosCursePicked(string boon, string rarity, double runMultBonus) =>
            Raise("ChaosCursePicked", c => c.Set("boon", boon).Set("rarity", rarity).Set("run_mult_bonus", runMultBonus));
        /// <summary>The boon draft was skipped (null pick, +1 shield). ctx: shields_now.</summary>
        public void NotifyChaosBoonSkipped(int shieldsNow) => Raise("ChaosBoonSkipped", c => c.Set("shields_now", (double)shieldsNow));
        /// <summary>A darter was caught. ctx: points, combo, quick (true = fast-catch bonus).</summary>
        public void NotifyChaosDarterCaught(double points, int combo, bool quick) =>
            Raise("ChaosDarterCaught", c => c.Set("points", points).Set("combo", (double)combo).Set("quick", quick));
        /// <summary>A freeze bubble was caught. ctx: points, combo.</summary>
        public void NotifyChaosFreezeCaught(double points, int combo) =>
            Raise("ChaosFreezeCaught", c => c.Set("points", points).Set("combo", (double)combo));
        /// <summary>A combo milestone (every 10). ctx: combo, difficulty.</summary>
        public void NotifyChaosComboMilestone(int combo, string difficulty) =>
            Raise("ChaosComboMilestone", c => c.Set("combo", (double)combo).Set("difficulty", difficulty));
        /// <summary>A high combo threshold was crossed (edge-detected). ctx: combo, threshold.</summary>
        public void NotifyChaosComboBig(int combo, double threshold) =>
            Raise("ChaosComboBig", c => c.Set("combo", (double)combo).Set("threshold", threshold));
        /// <summary>The act advanced (edge-detected). ctx: act, wave.</summary>
        public void NotifyChaosActChanged(int act, int wave) =>
            Raise("ChaosActChanged", c => c.Set("act", (double)act).Set("wave", (double)wave));
        /// <summary>The field was cleared at a wave boundary. ctx: wave (the wave just cleared).</summary>
        public void NotifyChaosWaveCleared(int wave) => Raise("ChaosWaveCleared", c => c.Set("wave", (double)wave));
        /// <summary>T-minus ~10s of a chaos run: the hole is closing (once per run, service-gated).</summary>
        public void NotifyChaosEndingSoon() => Raise("ChaosEndingSoon");
        /// <summary>The run finished. ctx: xp (final payout), difficulty, runs_completed, rank.</summary>
        public void NotifyChaosRunCompleted(int xp, string difficulty) =>
            Raise("ChaosRunCompleted", c => c.Set("xp", (double)xp).Set("difficulty", difficulty)
                .Set("runs_completed", (double)(Services.Chaos.ChaosMeta.State?.RunsCompleted ?? 0))
                .Set("rank", Services.Chaos.ChaosRanks.NameLower(Services.Chaos.ChaosMeta.RankIndex)));
        // ---- hold-to-defuse verb rework (2026-06-11). First-time gating lives in
        // ChaosModeService (chaos_meta.json flags); rh_focus_low is once per run there too. ----
        /// <summary>First ever completed defuse channel.</summary>
        public void NotifyChaosDefuseFirst() => Raise("ChaosDefuseFirst");
        /// <summary>First time a live triggered because focus couldn't cover the channel.</summary>
        public void NotifyChaosDefuseNoFocus() => Raise("ChaosDefuseNoFocus");
        /// <summary>First time an early release (mid-channel let-go) detonated a live.</summary>
        public void NotifyChaosDefuseRelease() => Raise("ChaosDefuseRelease");
        /// <summary>First time a plain click detonated a live bubble.</summary>
        public void NotifyChaosClickDetonate() => Raise("ChaosClickDetonate");
        /// <summary>Focus sat below a defuse's cost for 8s+ while lives were on screen.</summary>
        public void NotifyChaosFocusLow() => Raise("ChaosFocusLow");
        /// <summary>The Tease's first-ever appearance (debut spawn).</summary>
        public void NotifyChaosTeaseDebut() => Raise("ChaosTeaseDebut");
        /// <summary>A Tease expired untouched — the DENIED bonus paid. ctx: denied_count (this run).</summary>
        public void NotifyChaosTeaseDenied(int deniedCount) =>
            Raise("ChaosTeaseDenied", c => c.Set("denied_count", (double)deniedCount));
        /// <summary>The player touched a Tease — payload + streak halve. </summary>
        public void NotifyChaosTeaseClicked() => Raise("ChaosTeaseClicked");
        /// <summary>5+ Teases denied in a single run. ctx: denied_count.</summary>
        public void NotifyChaosTeaseDeniedStreak(int deniedCount) =>
            Raise("ChaosTeaseDeniedStreak", c => c.Set("denied_count", (double)deniedCount));
        /// <summary>The results screen was shown. ctx: score, best_score, pb_delta, is_pb, defused, detonated, best_combo, difficulty.</summary>
        public void NotifyChaosResultsShown(double score, double bestScore, double pbDelta, bool isPb,
                                            double defused, double detonated, int bestCombo, string difficulty) =>
            Raise("ChaosResultsShown", c => c
                .Set("score", score).Set("best_score", bestScore).Set("pb_delta", pbDelta).Set("is_pb", isPb)
                .Set("defused", defused).Set("detonated", detonated).Set("best_combo", (double)bestCombo)
                .Set("difficulty", difficulty)
                .Set("runs_completed", (double)(Services.Chaos.ChaosMeta.State?.RunsCompleted ?? 0))
                .Set("rank", Services.Chaos.ChaosRanks.NameLower(Services.Chaos.ChaosMeta.RankIndex)));

        // ---- Down the Rabbit Hole progression triggers (2026-06-11) ----
        /// <summary>Rank increased on run completion. ctx: rank (lowercase word).</summary>
        public void NotifyChaosRankUp(string rank) => Raise("ChaosRankUp", c => c.Set("rank", rank));
        /// <summary>A pending reveal flashed on dollhouse open (once per batch). ctx: element (reveal id).</summary>
        public void NotifyChaosRevealFlash(string element) => Raise("ChaosRevealFlash", c => c.Set("element", element));
        /// <summary>A lesson completed. ctx: lesson_id.</summary>
        public void NotifyChaosLessonComplete(string lessonId) => Raise("ChaosLessonComplete", c => c.Set("lesson_id", lessonId));
        /// <summary>She auto-covered a short balance on the first toy pocket attempt (one-time).</summary>
        public void NotifyChaosGiftGiven() => Raise("ChaosGiftGiven");
        /// <summary>A one-time first-times bonus awarded. ctx: bonus_id.</summary>
        public void NotifyChaosFirstTime(string bonusId) => Raise("ChaosFirstTime", c => c.Set("bonus_id", bonusId));
        /// <summary>First run with both e_stim + the_spanker: the scripted duo draft fired.</summary>
        public void NotifyChaosDuoDemo() => Raise("ChaosDuoDemo");
        /// <summary>First gold income ever (gold explained).</summary>
        public void NotifyChaosGoldFirst() => Raise("ChaosGoldFirst");
        /// <summary>The dollhouse opened for the very first time.</summary>
        public void NotifyChaosDollhouseFirstOpen() => Raise("ChaosDollhouseFirstOpen");
        /// <summary>An untouched draft timed out before the skip is revealed: it autopicked a card.</summary>
        public void NotifyChaosDraftAutopick() => Raise("ChaosDraftAutopick");

        // Reflection cache so a numeric-setting read on every PropertyChanged stays cheap.
        private static readonly Dictionary<string, System.Reflection.PropertyInfo?> _settingPropCache = new(StringComparer.Ordinal);
        /// <summary>Read a setting's current value as a double (bool→0/1, enum→int). False for non-numeric/missing props.</summary>
        private bool TryGetSettingNumber(string name, out double value)
        {
            value = 0;
            var cur = App.Settings?.Current;
            if (cur == null) return false;
            System.Reflection.PropertyInfo? pi;
            lock (_settingPropCache)
            {
                if (!_settingPropCache.TryGetValue(name, out pi))
                {
                    pi = cur.GetType().GetProperty(name);
                    _settingPropCache[name] = pi;
                }
            }
            if (pi == null) return false;
            object? raw;
            try { raw = pi.GetValue(cur); } catch { return false; }
            switch (raw)
            {
                case bool b: value = b ? 1 : 0; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case double d: value = d; return true;
                case float f: value = f; return true;
                case Enum en: value = Convert.ToDouble(en); return true;
                default: return false;
            }
        }

        /// <summary>
        /// Reload rules from disk (e.g. after a mod switch so the new mod's manifest applies).
        /// Rotation / cooldown / session one-shot state is keyed by rule id, and ids are SHARED
        /// across mods (same id → different content), so it is cleared here: otherwise a line
        /// fired under the old mod would suppress the same-id line under the new one. Persisted
        /// lifetime/tier latches (AppSettings) are intentionally NOT cleared — those are once-ever.
        /// </summary>
        public void ReloadRules()
        {
            try
            {
                var fresh = BarkRuleLoader.Load();
                lock (_gate)
                {
                    _rules = fresh;
                    _firedOnceSession.Clear();
                    _lastFiredUtc.Clear();
                    // Variant/idle rotation is intentionally NOT cleared — it's persistent no-repeat state
                    // (line ids filter against whatever the new mod's pool contains), so a mod switch must
                    // not restart every pool. Session one-shots above are still re-armed.
                }
                App.Logger?.Information("BarkService: reloaded {Count} rules (rotation preserved)", fresh.Count);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: rule reload failed"); }
        }

        // ===================== subscription block =====================

        private void WireSubscriptions()
        {
            // ---- Webcam / gaze ----
            if (App.Webcam != null)
            {
                Wire<Action>(h => App.Webcam.OnBlink += h, h => App.Webcam.OnBlink -= h,
                    () => { _state.RegisterBlink(); Raise("Blink", c => c.Set("blink_count", _state.BlinkCount)); });
                Wire<Action>(h => App.Webcam.OnMouthOpen += h, h => App.Webcam.OnMouthOpen -= h,
                    () => Raise("MouthOpen"));
                Wire<Action>(h => App.Webcam.OnTongueOut += h, h => App.Webcam.OnTongueOut -= h,
                    () => Raise("TongueOut"));
                Wire<Action<System.Windows.Point>>(h => App.Webcam.OnLongStare += h, h => App.Webcam.OnLongStare -= h,
                    _ => Raise("LongStare"));
                Wire<Action>(h => App.Webcam.OnFaceLost += h, h => App.Webcam.OnFaceLost -= h,
                    () => { _state.FaceLost(); Raise("FaceLost"); });
                // Raise BEFORE clearing so face_lost_sec still reads the elapsed lost-duration
                // (enables the prolonged-FaceLost egg's distinct very-long threshold).
                Wire<Action>(h => App.Webcam.OnFaceFound += h, h => App.Webcam.OnFaceFound -= h,
                    () => { Raise("FaceFound"); _state.FaceFound(); });
                Wire<Action<WebcamTrackingState>>(h => App.Webcam.OnTrackingStateChanged += h, h => App.Webcam.OnTrackingStateChanged -= h,
                    s => Raise("TrackingStateChanged", c => c.Set("state", s.ToString())));
            }

            // ---- Gaze focus ----
            if (App.GazeFocus != null)
            {
                Wire<Action>(h => App.GazeFocus.GazePopped += h, h => App.GazeFocus.GazePopped -= h,
                    () => Raise("GazePopped"));
                Wire<Action<bool>>(h => App.GazeFocus.OnActiveChanged += h, h => App.GazeFocus.OnActiveChanged -= h,
                    active => Raise("GazeActiveChanged", c => c.Set("active", active)));
            }

            // ---- Blink trainer ----
            if (App.BlinkTrainer != null)
                Wire<Action>(h => App.BlinkTrainer.StateChanged += h, h => App.BlinkTrainer.StateChanged -= h,
                    () => Raise("BlinkTrainerStateChanged", c => c.Set("running", App.BlinkTrainer?.IsRunning ?? false)));

            // ---- Video ----
            if (App.Video != null)
            {
                Wire<EventHandler>(h => App.Video.VideoAboutToStart += h, h => App.Video.VideoAboutToStart -= h,
                    (_, __) => Raise("VideoAboutToStart"));
                Wire<EventHandler>(h => App.Video.VideoStarted += h, h => App.Video.VideoStarted -= h,
                    (_, __) => Raise("VideoStarted"));
                Wire<EventHandler>(h => App.Video.VideoEnded += h, h => App.Video.VideoEnded -= h,
                    (_, __) => Raise("VideoEnded"));
            }

            // ---- Attention checks (GATED on a video actually playing) ----
            if (App.AttentionCheck != null)
            {
                Wire<Action>(h => App.AttentionCheck.OnPass += h, h => App.AttentionCheck.OnPass -= h,
                    () => { if (App.Video?.IsPlaying == true) Raise("AttentionCheckPass", c => c.Set("video_playing", true)); });
                Wire<Action>(h => App.AttentionCheck.OnFail += h, h => App.AttentionCheck.OnFail -= h,
                    () => { if (App.Video?.IsPlaying == true) Raise("AttentionCheckFail", c => c
                        .Set("video_playing", true)
                        .Set("fail_count", App.Video?.PlaythroughFailCount ?? 0)); });
            }

            // ---- Bubble minigames ----
            if (App.Bubbles != null)
            {
                Wire<Action>(h => App.Bubbles.OnBubblePopped += h, h => App.Bubbles.OnBubblePopped -= h,
                    () => Raise("BubblePopped"));
                Wire<Action>(h => App.Bubbles.OnBubbleMissed += h, h => App.Bubbles.OnBubbleMissed -= h,
                    () => Raise("BubbleMissed"));
            }
            if (App.BubbleCount != null)
            {
                Wire<EventHandler>(h => App.BubbleCount.GameCompleted += h, h => App.BubbleCount.GameCompleted -= h,
                    (_, __) => Raise("BubbleCountCompleted"));
                Wire<EventHandler>(h => App.BubbleCount.GameFailed += h, h => App.BubbleCount.GameFailed -= h,
                    (_, __) => Raise("BubbleCountFailed"));
            }
            if (App.BouncingText != null)
            {
                Wire<EventHandler>(h => App.BouncingText.OnBounce += h, h => App.BouncingText.OnBounce -= h,
                    (_, __) => Raise("BouncingTextBounce"));
                // DVD corner hit (true/near corner) — distinct from a plain wall bounce.
                Wire<EventHandler>(h => App.BouncingText.OnCornerHit += h, h => App.BouncingText.OnCornerHit -= h,
                    (_, __) => Raise("BouncingTextCorner"));
            }

            // ---- Lock card (sole subscriber; Fork-D 50/50 coin flip in HandleLockCardCompleted) ----
            if (App.LockCard != null)
                Wire<EventHandler<LockCardCompletedEventArgs>>(
                    h => App.LockCard.LockCardCompleted += h, h => App.LockCard.LockCardCompleted -= h,
                    (_, e) => HandleLockCardCompleted(e));

            // ---- Visual fx ----
            if (App.Flash != null)
                Wire<EventHandler>(h => App.Flash.FlashDisplayed += h, h => App.Flash.FlashDisplayed -= h,
                    (_, __) => Raise("FlashDisplayed"));
            if (App.Subliminal != null)
                Wire<EventHandler>(h => App.Subliminal.SubliminalDisplayed += h, h => App.Subliminal.SubliminalDisplayed -= h,
                    (_, __) => Raise("SubliminalDisplayed"));
            if (App.BrainDrain != null)
                Wire<EventHandler>(h => App.BrainDrain.BrainDrainTriggered += h, h => App.BrainDrain.BrainDrainTriggered -= h,
                    (_, __) => Raise("BrainDrainTriggered"));
            if (App.MindWipe != null)
                Wire<EventHandler>(h => App.MindWipe.MindWipeTriggered += h, h => App.MindWipe.MindWipeTriggered -= h,
                    (_, __) => Raise("MindWipeTriggered"));

            // ---- Awareness / keywords ----
            if (App.KeywordTriggers != null)
                Wire<EventHandler<KeywordTrigger>>(h => App.KeywordTriggers.TriggerFired += h, h => App.KeywordTriggers.TriggerFired -= h,
                    (_, kt) => Raise("KeywordTriggerFired", c => c
                        .Set("keyword", kt?.Keyword ?? "")
                        .Set("kw_effect", kt?.VisualEffect.ToString() ?? "")));
            if (App.WindowAwareness != null)
            {
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.ActivityChanged += h, h => App.WindowAwareness.ActivityChanged -= h,
                    (_, a) => Raise("ActivityChanged", c => c
                        .Set("activity", a?.ServiceName ?? "")
                        .Set("category", a?.Category.ToString() ?? "")
                        // Fine-grained awareness (item G) — populated only when AppClusterMap matched the
                        // title; empty otherwise so app_cluster_eq/app_eq rules simply don't match.
                        .Set("app_cluster", a?.AppCluster ?? "")
                        .Set("app", a?.AppId ?? "")));
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.StillOnActivity += h, h => App.WindowAwareness.StillOnActivity -= h,
                    (_, a) => Raise("StillOnActivity", c => c
                        .Set("activity", a?.ServiceName ?? "")
                        .Set("still_minutes", App.WindowAwareness?.CurrentActivityDuration.TotalMinutes ?? 0)));
            }

            // ---- Progression / companion / skills ----
            if (App.Progression != null)
            {
                Wire<EventHandler<int>>(h => App.Progression.LevelUp += h, h => App.Progression.LevelUp -= h,
                    (_, lvl) => Raise("LevelUp", c => c.Set("level", lvl)));
                Wire<EventHandler<double>>(h => App.Progression.XPChanged += h, h => App.Progression.XPChanged -= h,
                    (_, xp) => Raise("XPChanged", c => c.Set("xp", xp)));
            }
            if (App.Companion != null)
            {
                Wire<EventHandler<(CompanionId Companion, int NewLevel)>>(
                    h => App.Companion.CompanionLevelUp += h, h => App.Companion.CompanionLevelUp -= h,
                    (_, e) => Raise("CompanionLevelUp", c => c.Set("level", e.NewLevel)));
                Wire<EventHandler>(h => App.Companion.UserMessageSent += h, h => App.Companion.UserMessageSent -= h,
                    (_, __) => { _lastUserMessageUtc = DateTime.UtcNow; Raise("UserMessageSent"); });
            }
            if (App.SkillTree != null)
            {
                Wire<EventHandler<string>>(h => App.SkillTree.SkillUnlocked += h, h => App.SkillTree.SkillUnlocked -= h,
                    (_, id) => Raise("SkillUnlocked", c => c.Set("skill", id ?? "")));
                Wire<EventHandler<LuckyProcEventArgs>>(h => App.SkillTree.LuckyProc += h, h => App.SkillTree.LuckyProc -= h,
                    (_, __) => Raise("LuckyProc"));
                Wire<EventHandler>(h => App.SkillTree.PinkRushStarted += h, h => App.SkillTree.PinkRushStarted -= h,
                    (_, __) => Raise("PinkRushStarted"));
                Wire<EventHandler>(h => App.SkillTree.PinkRushEnded += h, h => App.SkillTree.PinkRushEnded -= h,
                    (_, __) => Raise("PinkRushEnded"));
            }

            // ---- Achievements / roadmap / quests / quiz / mantra ----
            if (App.Achievements != null)
                Wire<EventHandler<Achievement>>(h => App.Achievements.AchievementUnlocked += h, h => App.Achievements.AchievementUnlocked -= h,
                    (_, a) => Raise("AchievementUnlocked", c => c.Set("achievement", a?.Id ?? "")));
            if (App.Roadmap != null)
            {
                Wire<EventHandler<RoadmapStepCompletedEventArgs>>(h => App.Roadmap.StepCompleted += h, h => App.Roadmap.StepCompleted -= h,
                    (_, __) => Raise("RoadmapStepCompleted"));
                Wire<EventHandler<RoadmapTrack>>(h => App.Roadmap.TrackUnlocked += h, h => App.Roadmap.TrackUnlocked -= h,
                    (_, __) => Raise("RoadmapTrackUnlocked"));
                Wire<EventHandler>(h => App.Roadmap.BadgeEarned += h, h => App.Roadmap.BadgeEarned -= h,
                    (_, __) => Raise("RoadmapBadgeEarned"));
            }
            if (App.Quests != null)
            {
                Wire<EventHandler<QuestCompletedEventArgs>>(h => App.Quests.QuestCompleted += h, h => App.Quests.QuestCompleted -= h,
                    (_, __) => Raise("QuestCompleted"));
                Wire<EventHandler<QuestProgressEventArgs>>(h => App.Quests.QuestProgressChanged += h, h => App.Quests.QuestProgressChanged -= h,
                    (_, __) => Raise("QuestProgressChanged"));
                Wire<EventHandler>(h => App.Quests.QuestsRefreshed += h, h => App.Quests.QuestsRefreshed -= h,
                    (_, __) => Raise("QuestsRefreshed"));
            }
            // QuizCompleted is a STATIC event — must unsubscribe in Stop() to avoid a dangling handler.
            {
                EventHandler<QuizCompletedEventArgs> h = (_, e) => Raise("QuizCompleted", c => c.Set("passed", e.Passed).Set("perfect", e.Perfect));
                QuizService.QuizCompleted += h;
                _unsubscribe.Add(() => QuizService.QuizCompleted -= h);
            }
            if (App.Mantra != null)
            {
                Wire<Action>(h => App.Mantra.MantraCompleted += h, h => App.Mantra.MantraCompleted -= h,
                    () => Raise("MantraCompleted"));
                Wire<Action<int>>(h => App.Mantra.StreakChanged += h, h => App.Mantra.StreakChanged -= h,
                    streak => Raise("MantraStreakChanged", c => c.Set("streak", streak)));
                Wire<Action>(h => App.Mantra.StreakBroken += h, h => App.Mantra.StreakBroken -= h,
                    () => Raise("MantraStreakBroken"));
            }

            // ---- Control / system ----
            if (App.Lockdown != null)
            {
                Wire<Action>(h => App.Lockdown.LockdownActivated += h, h => App.Lockdown.LockdownActivated -= h,
                    () => Raise("LockdownActivated"));
                Wire<Action>(h => App.Lockdown.LockdownDeactivated += h, h => App.Lockdown.LockdownDeactivated -= h,
                    () => Raise("LockdownDeactivated"));
                Wire<Action<TimeSpan>>(h => App.Lockdown.CountdownTick += h, h => App.Lockdown.CountdownTick -= h,
                    ts => Raise("LockdownCountdownTick", c => c.Set("remaining_sec", ts.TotalSeconds)));
            }
            if (App.RemoteControl != null)
            {
                Wire<EventHandler<string>>(h => App.RemoteControl.CommandReceived += h, h => App.RemoteControl.CommandReceived -= h,
                    (_, action) => Raise("RemoteCommandReceived", c => c.Set("command", action ?? "")));
                Wire<EventHandler>(h => App.RemoteControl.ControllerConnectedChanged += h, h => App.RemoteControl.ControllerConnectedChanged -= h,
                    (_, __) => Raise("ControllerConnectedChanged"));
            }
            if (App.Mods != null)
                Wire<EventHandler<ModPackage>>(h => App.Mods.ModChanged += h, h => App.Mods.ModChanged -= h,
                    (_, mod) =>
                    {
                        _state.RegisterModSwitch();
                        // The new mod ships its own bark manifest — reload so its lines/voicelines
                        // apply. ModChanged fires after ModService sets the active mod, so the
                        // loader reads the new ActiveModId. (Raise the switch bark off the OLD set
                        // first, since the new set may not define a ModChanged rule.)
                        Raise("ModChanged", c => c
                            .Set("mod", mod?.Id ?? "")
                            .Set("mod_switches_60s", _state.ModSwitchesWithin(RapidModSwitchWindow)));
                        ReloadRules();
                    });
            if (App.ActivityTracker != null)
                Wire<EventHandler<bool>>(h => App.ActivityTracker.IdleStateChanged += h, h => App.ActivityTracker.IdleStateChanged -= h,
                    (_, idle) => Raise("IdleStateChanged", c => c.Set("idle", idle)));
            if (App.Update != null)
                Wire<EventHandler<UpdateInfo>>(h => App.Update.UpdateAvailable += h, h => App.Update.UpdateAvailable -= h,
                    (_, __) => Raise("UpdateAvailable"));
            if (App.Patreon != null)
                Wire<EventHandler<PatreonTier>>(h => App.Patreon.TierChanged += h, h => App.Patreon.TierChanged -= h,
                    (_, tier) =>
                    {
                        bool up = tier > _lastTier; // enum is ordinal (None < Level1 < …)
                        _lastTier = tier;
                        Raise("PatreonTierChanged", c => c.Set("tier", tier.ToString()).Set("tier_up", up));
                    });
            if (App.Discord != null)
                Wire<EventHandler<bool>>(h => App.Discord.AuthenticationChanged += h, h => App.Discord.AuthenticationChanged -= h,
                    (_, ok) => Raise("DiscordAuthChanged", c => c.Set("authenticated", ok)));
            if (App.Tutorial != null)
                Wire<EventHandler>(h => App.Tutorial.TutorialCompleted += h, h => App.Tutorial.TutorialCompleted -= h,
                    (_, __) => Raise("TutorialCompleted"));

            // LocalAiService.PersistentMemoryRecalled is a STATIC event — unsubscribe in Stop().
            {
                EventHandler h = (_, __) => Raise("PersistentMemoryRecalled");
                LocalAiService.PersistentMemoryRecalled += h;
                _unsubscribe.Add(() => LocalAiService.PersistentMemoryRecalled -= h);
            }
        }

        /// <summary>
        /// Wire SessionEngine events. SessionEngine is MainWindow-owned and created lazily on
        /// the first session, so MainWindow calls this once it constructs the engine. Re-attach
        /// safely detaches any previous engine wiring.
        /// </summary>
        public void AttachSessionEngine(SessionEngine engine)
        {
            if (engine == null) return;
            try
            {
                RunUnsubscribers(_engineUnsubscribe);
                _state.AttachSessionEngine(engine);

                EventHandler started = (_, __) =>
                {
                    // A session started — the setup screen is gone, so cancel any pending setup-ready nudge.
                    try { _setupReadyTimer?.Stop(); } catch { }
                    _setupReadyStallPending = false;
                    _state.ResetSessionScoped();
                    Raise("SessionStarted");
                };
                EventHandler stopped = (_, __) => Raise("SessionStopped");
                EventHandler<SessionCompletedEventArgs> completed = (_, __) => Raise("SessionCompleted");
                EventHandler<SessionPhaseChangedEventArgs> phase = (_, e) =>
                {
                    _state.SetPhase(e?.Phase?.Name);
                    Raise("SessionPhaseChanged", c => c
                        .Set("phase_index", e?.PhaseIndex ?? -1)
                        .Set("phase_name", e?.Phase?.Name ?? "")
                        .Set("phase_is_deepener", _state.CurrentPhaseIsDeepener));
                };
                // Periodic in-session tick — drives time-threshold eggs (e.g. marathon: session_elapsed_sec >= 3h).
                EventHandler<SessionProgressEventArgs> progress = (_, __) =>
                    Raise("SessionProgress", c => c.Set("session_elapsed_sec", _state.SessionElapsedSeconds));

                engine.SessionStarted += started;
                engine.SessionStopped += stopped;
                engine.SessionCompleted += completed;
                engine.PhaseChanged += phase;
                engine.ProgressUpdated += progress;

                _engineUnsubscribe.Add(() => engine.SessionStarted -= started);
                _engineUnsubscribe.Add(() => engine.SessionStopped -= stopped);
                _engineUnsubscribe.Add(() => engine.SessionCompleted -= completed);
                _engineUnsubscribe.Add(() => engine.PhaseChanged -= phase);
                _engineUnsubscribe.Add(() => engine.ProgressUpdated -= progress);

                App.Logger?.Debug("BarkService: attached to SessionEngine");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AttachSessionEngine failed"); }
        }

        /// <summary>
        /// Wire the MainWindow-owned tray icon (created in MainWindow's constructor).
        /// Re-attach safe: detaches any previous tray wiring before subscribing, so a
        /// repeat call can't double-subscribe (no double-fire, no leak).
        /// </summary>
        public void AttachTray(TrayIconService tray)
        {
            if (tray == null) return;
            try
            {
                RunUnsubscribers(_trayUnsubscribe);

                Action wake = () => Raise("WakeBambiRequested");
                tray.OnWakeBambiRequested += wake;
                _trayUnsubscribe.Add(() => tray.OnWakeBambiRequested -= wake);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AttachTray failed"); }
        }

        // ===================== subscription helper =====================

        private void Wire<TDelegate>(Action<TDelegate> subscribe, Action<TDelegate> unsubscribe, TDelegate handler)
            where TDelegate : Delegate
        {
            try
            {
                subscribe(handler);
                _unsubscribe.Add(() => unsubscribe(handler));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: failed to wire a subscription");
            }
        }

        // ===================== matcher =====================

        /// <param name="guaranteed">
        /// When true the gate is bypassed (cooldown / min-gap / one-fire / chat-suppression /
        /// safety-hold) and a variant is always selected — used for direct reactions that must
        /// fire exactly once (e.g. the lock-card pool bark on a tails coin flip).
        /// </param>
        /// <returns>True if a bark was actually spoken (rule matched + gate passed + not dry-run).</returns>
        private bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            try
            {
                var rules = _rules.ForTrigger(trigger);
                if (rules.Count == 0) return false;

                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);

                // Decide under the lock; render after releasing it (GigglePriority/Giggle marshal to UI).
                BarkRule? toSpeak = null;
                int variantIndex = -1;
                List<BarkVariant>? pool = null;

                lock (_gate)
                {
                    BarkRule? winner = null;
                    foreach (var rule in rules) // already priority-descending
                    {
                        if (ConditionsPass(rule, ctx)) { winner = rule; break; }
                    }

                    if (winner == null)
                    {
                        App.Logger?.Debug("[BARK] trigger={Trigger} no rule matched conditions", trigger);
                        return false;
                    }

                    (toSpeak, variantIndex, pool) = DecideLocked(trigger, winner, guaranteed);
                }

                if (toSpeak != null && pool != null)
                {
                    Speak(toSpeak, variantIndex, ctx, pool);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: Raise('{Trigger}') failed", trigger);
                return false;
            }
        }

        /// <summary>
        /// Shared gate→commit→speak decision for an already-chosen winner. Caller MUST hold
        /// <see cref="_gate"/>. Returns the rule/variant/pool to speak, or (null,-1,null) when the
        /// gate blocks the fire or we're in dry-run. Reused by <see cref="Raise"/> (priority-walk
        /// winner) and <see cref="DispatchIdle"/> (no-repeat idle winner).
        /// </summary>
        private (BarkRule? toSpeak, int variantIndex, List<BarkVariant>? pool) DecideLocked(
            string trigger, BarkRule winner, bool guaranteed)
        {
            var resolved = ResolvePool(winner);
            var decision = EvaluateGate(winner, resolved, guaranteed);
            LogDecision(trigger, winner, decision);

            if (!decision.WouldFire) return (null, -1, null);

            CommitFire(winner, decision.VariantIndex, resolved);
            return DryRun ? (null, -1, null) : (winner, decision.VariantIndex, resolved);
        }

        // ===================== idle chatter (item F) =====================

        /// <summary>
        /// Raise an idle ("Idle") bark, chosen with a pool-wide no-repeat across ALL eligible idle
        /// rules. Called by AvatarTubeWindow's idle timer (companion-tab slider cadence). Skipped
        /// while muted or while she's already speaking so idle never talks over a real bark; the
        /// normal gate (min-gap / chat-suppression / whisper / safety-hold) still applies on top,
        /// so a recent real bark also preempts idle.
        /// </summary>
        public void DispatchIdle()
        {
            try
            {
                // Pause idle while muted or already speaking — real barks always win.
                if ((App.Settings?.Current?.MasterVolume ?? 0) == 0) return;
                if (App.AvatarWindow?.IsSpeaking ?? false) return;

                BarkRule? toSpeak = null; int variantIndex = -1; List<BarkVariant>? pool = null;
                var ctx = new BarkContext("Idle");
                lock (_gate)
                {
                    var idleRules = _rules.ForTrigger("Idle");
                    if (idleRules.Count == 0) return;
                    var eligible = idleRules.Where(r => ConditionsPass(r, ctx)).ToList();
                    if (eligible.Count == 0) return;
                    var winner = PickIdleRuleLocked(eligible);
                    if (winner == null) return;
                    (toSpeak, variantIndex, pool) = DecideLocked("Idle", winner, guaranteed: false);
                }
                if (toSpeak != null && pool != null) Speak(toSpeak, variantIndex, ctx, pool);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: DispatchIdle failed"); }
        }

        /// <summary>
        /// Pick an idle rule with pool-wide no-repeat: don't replay a rule until every eligible idle
        /// rule has fired, then recycle (reseeding with the last one so it can't immediately repeat).
        /// Biased (<see cref="GatedIdleBias"/>) to occasionally surface an eligible band-gated rule
        /// (idle_gated_*) so the ~3 gated lines aren't drowned by the ~32 ungated ones. Caller holds
        /// <see cref="_gate"/>.
        /// </summary>
        private BarkRule? PickIdleRuleLocked(List<BarkRule> eligible)
        {
            var unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
            if (unused.Count == 0)
            {
                _usedIdleRules.Clear();
                if (_lastIdleRuleId != null) _usedIdleRules.Add(_lastIdleRuleId);
                PersistIdleRotation(); // exhausted → recycle; mirror the reset so it persists
                unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                if (unused.Count == 0) unused = eligible; // single eligible rule — unavoidable repeat
            }

            var gated = unused.Where(r => r.Conditions != null && r.Conditions.Count > 0).ToList();
            if (gated.Count > 0 && _rng.NextDouble() < GatedIdleBias)
                return gated[_rng.Next(gated.Count)];
            return unused[_rng.Next(unused.Count)];
        }

        // ===================== anticipatory setup-ready detector (item E) =====================

        /// <summary>
        /// (Re)start the SessionSetupReady debounce after a setup action (setting change). Fires when the
        /// user goes quiet (~8–12s after the LAST action), not on every click. No-op while a session is
        /// running. Marshals to the UI thread since the settings setter may call us off-thread.
        /// </summary>
        private void ArmSetupReadyDebounce()
        {
            if (_state.SessionRunning) return;
            DispatcherHelper.RunOnUI(() =>
            {
                if (!_started) return;
                _setupReadyStallPending = false;
                if (_setupReadyTimer == null)
                {
                    _setupReadyTimer = new System.Windows.Threading.DispatcherTimer();
                    _setupReadyTimer.Tick += OnSetupReadyTick;
                }
                _setupReadyTimer.Stop();
                _setupReadyTimer.Interval = TimeSpan.FromSeconds(
                    _rng.Next(SetupReadyDebounceMinSec, SetupReadyDebounceMaxSec + 1));
                _setupReadyTimer.Start();
            });
        }

        private void OnSetupReadyTick(object? sender, EventArgs e)
        {
            _setupReadyTimer?.Stop();
            try
            {
                if (!SetupConditionsMet()) { _setupReadyStallPending = false; return; }

                Raise("SessionSetupReady");

                // setup_ready_stall needs ~45s of idle, but this first fire lands at ~10s. If the user is
                // still settling, schedule ONE more raise once the stall window elapses (the per-rule
                // scope:session latches keep each line one-shot, so the earlier ready line won't repeat).
                if (!_setupReadyStallPending && _state.SetupIdleSeconds < SetupReadyStallSec && _setupReadyTimer != null)
                {
                    _setupReadyStallPending = true;
                    double wait = Math.Max(5, SetupReadyStallSec - _state.SetupIdleSeconds + 2);
                    _setupReadyTimer.Interval = TimeSpan.FromSeconds(wait);
                    _setupReadyTimer.Start();
                }
                else
                {
                    _setupReadyStallPending = false;
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: setup-ready tick failed"); }
        }

        /// <summary>
        /// SessionSetupReady gate: engine not running + ≥2 setting changes this app-open + at least one
        /// content feature enabled + media present on disk. (MediaAdded was cut this pass; this covers
        /// the "everything's ready, press start" moment.)
        /// </summary>
        private bool SetupConditionsMet()
        {
            var s = App.Settings?.Current;
            if (s == null || _state.SessionRunning) return false;
            if (_state.SettingsChangedThisSession < 2) return false;
            if (!(s.FlashEnabled || s.MandatoryVideosEnabled)) return false; // at least one asset enabled
            return HasAnyMedia();
        }

        /// <summary>Cheap probe: does the assets folder hold at least one image or video file?</summary>
        private static bool HasAnyMedia()
        {
            try
            {
                var root = App.EffectiveAssetsPath;
                if (string.IsNullOrEmpty(root)) return false;
                foreach (var sub in new[] { "images", "videos" })
                {
                    var dir = System.IO.Path.Combine(root, sub);
                    if (System.IO.Directory.Exists(dir) && System.IO.Directory.EnumerateFiles(dir).Any())
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        // ===================== lock card (Fork D: single coin flip, exactly one fires) =====================

        private void HandleLockCardCompleted(LockCardCompletedEventArgs e)
        {
            try
            {
                bool heads = _rng.Next(2) == 0;
                bool aiAvailable = App.AvatarWindow != null
                    && App.Settings?.Current?.AiChatEnabled == true
                    && App.Ai?.IsAvailable == true;

                if (heads && aiAvailable)
                {
                    if (DryRun)
                    {
                        App.Logger?.Information("[BARK dry-run] LockCard coin=heads -> WOULD play AI lock reaction");
                        return;
                    }
                    App.Logger?.Information("[BARK] LockCard coin=heads -> AI lock reaction");
                    _ = PlayLockReactionThenMaybePool(e);
                }
                else
                {
                    // tails, or heads-but-AI-unavailable -> guaranteed pool bark (never neither).
                    var why = heads ? "heads(ai-unavailable)" : "tails";
                    App.Logger?.Information("[BARK] LockCard coin={Why} -> pool bark", why);
                    FireLockCardPoolBark(e);
                }
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: lock-card handler failed"); }
        }

        private async System.Threading.Tasks.Task PlayLockReactionThenMaybePool(LockCardCompletedEventArgs e)
        {
            bool spoke = false;
            try
            {
                var avatar = App.AvatarWindow;
                if (avatar != null) spoke = await avatar.PlayLockCardAiReactionAsync(e);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AI lock reaction threw"); }

            if (!spoke)
            {
                // AI produced nothing — fall through so exactly one reaction still fires.
                App.Logger?.Information("[BARK] LockCard heads but AI silent -> pool bark fallback");
                DispatcherHelper.RunOnUI(() => FireLockCardPoolBark(e));
            }
        }

        private void FireLockCardPoolBark(LockCardCompletedEventArgs e) =>
            Raise("LockCardCompleted", c => c
                .Set("phrase", e.Phrase ?? "")
                .Set("mistakes", e.Mistakes)
                .Set("repeats", e.Repeats), guaranteed: true);

        private bool ConditionsPass(BarkRule rule, BarkContext ctx)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0) return true;
            foreach (var kvp in rule.Conditions)
            {
                if (!ConditionPass(kvp.Key, kvp.Value, ctx)) return false;
            }
            return true;
        }

        private bool ConditionPass(string key, object? expected, BarkContext ctx)
        {
            string field = key;
            string op = "eq";
            foreach (var suffix in new[] { "_gte", "_lte", "_gt", "_lt", "_eq" })
            {
                if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    field = key.Substring(0, key.Length - suffix.Length);
                    op = suffix.Substring(1);
                    break;
                }
            }

            var actual = ResolveField(field, ctx);
            if (actual == null) return false;

            if (op == "eq")
            {
                // bool/string equality first, numeric fallback.
                if (expected is bool eb && TryBool(actual, out var ab)) return ab == eb;
                if (TryDouble(expected, out var en) && TryDouble(actual, out var an))
                    return Math.Abs(an - en) < 0.0001;
                return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
            }

            if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e)) return false;
            return op switch
            {
                "gte" => a >= e,
                "lte" => a <= e,
                "gt" => a > e,
                "lt" => a < e,
                _ => false
            };
        }

        /// <summary>Resolve a condition field: well-known live reads first, else the per-fire context.</summary>
        private object? ResolveField(string field, BarkContext ctx)
        {
            switch (field.ToLowerInvariant())
            {
                case "video_playing": return App.Video?.IsPlaying ?? false;
                // True only while webcam tracking is actually live (Tracking/FaceLost) — the same
                // source the OnFaceFound/OnBlink/OnLongStare events fire from. Lets watching/camera-
                // themed lines on non-webcam triggers (Idle, etc.) gate on the cam actually being on.
                case "webcam_running": return App.Webcam?.IsRunning ?? false;
                case "session_running": return _state.SessionRunning;
                case "setup_idle_sec": return _state.SetupIdleSeconds;
                case "session_elapsed_sec": return _state.SessionElapsedSeconds;
                case "session_phase_index": return _state.SessionPhaseIndex;
                case "blink_count": return (double)_state.BlinkCount;
                case "face_lost_sec": return _state.FaceLostSeconds;
                case "mod_switches_60s": return _state.ModSwitchesWithin(RapidModSwitchWindow);
                case "clicks_60s": return _state.AvatarClicksWithin(RapidClickWindow);
                case "days_away": return _state.DaysAwayAtLaunch;
                case "instant_relaunch": return _state.InstantRelaunch;
                case "master_volume": return (double)(App.Settings?.Current?.MasterVolume ?? 0);
                case "mute": return (App.Settings?.Current?.MasterVolume ?? 0) == 0;
                case "player_level": return (double)(App.Settings?.Current?.PlayerLevel ?? 0);
                case "total_sessions": return (double)(App.Settings?.Current?.TotalSessions ?? 0);
                case "daily_quest_streak": return (double)(App.Settings?.Current?.DailyQuestStreak ?? 0);
                case "current_streak": return (double)(App.Settings?.Current?.CurrentStreak ?? 0);

                // --- lifetime-completion egg conditions ---
                case "achievements_all_unlocked":
                {
                    var total = App.Achievements?.GetTotalCount() ?? 0;
                    return total > 0 && (App.Achievements?.GetUnlockedCount() ?? 0) >= total;
                }
                case "all_skills_unlocked":
                {
                    var total = SkillDefinition.All?.Count ?? 0;
                    return total > 0 && (App.SkillTree?.GetUnlockedSkills().Count ?? 0) >= total;
                }
                // (max-level egg uses the existing "player_level_gte" condition — there is no level cap.)

                // --- date / time-of-day ---
                case "is_nye":
                {
                    var now = DateTime.Now; // Dec 31 or Jan 1 (local)
                    return (now.Month == 12 && now.Day == 31) || (now.Month == 1 && now.Day == 1);
                }
                case "local_hour": return (double)DateTime.Now.Hour;

                // --- session phase (for deepener conditions on non-phase events) ---
                case "phase_name": return _state.CurrentPhaseName;
                case "phase_is_deepener": return _state.CurrentPhaseIsDeepener;

                default:
                    return ctx.Values.TryGetValue(field, out var v) ? v : null;
            }
        }

        private static bool TryDouble(object? raw, out double value)
        {
            value = 0;
            switch (raw)
            {
                case null: return false;
                case double d: value = d; return true;
                case int i: value = i; return true;
                case long l: value = l; return true;
                case float f: value = f; return true;
                case bool b: value = b ? 1 : 0; return true;
                case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p):
                    value = p; return true;
                default: return false;
            }
        }

        private static bool TryBool(object? raw, out bool value)
        {
            value = false;
            switch (raw)
            {
                case bool b: value = b; return true;
                case int i: value = i != 0; return true;
                case double d: value = Math.Abs(d) > double.Epsilon; return true;
                case string s when bool.TryParse(s, out var p): value = p; return true;
                default: return false;
            }
        }

        private List<BarkVariant> ResolvePool(BarkRule rule)
        {
            // Inline pool: drop any lines the user disabled/hid in the Phrase Manager (shares
            // DisabledPhraseIds / RemovedPhraseIds with built-in phrases via the "Bark:" id prefix).
            // A filtered copy — never mutate the rule's live VariantPool. If everything's disabled the
            // pool comes back empty and EvaluateGate short-circuits on "empty-pool" (rule stays silent).
            if (rule.VariantPool != null && rule.VariantPool.Count > 0)
                return rule.VariantPool.Where(v => IsBarkLineEnabled(rule.Id, v)).ToList();
            if (!string.IsNullOrWhiteSpace(rule.PoolRef))
            {
                var phrases = App.Mods?.GetPhrases(rule.PoolRef!);
                if (phrases != null && phrases.Length > 0)
                    return phrases.Select(p => new BarkVariant(p)).ToList(); // pool-ref lines are text-only
            }
            return new List<BarkVariant>();
        }

        /// <summary>
        /// Stable identifier for a single bark line, used to toggle it in the Phrase Manager and to
        /// gate it at speak time. Keyed off the audio filename (e.g. <c>flash_12</c>) so it survives
        /// reordering/content edits — an index-based id would silence the wrong line after a content
        /// update. Text-only variants fall back to a slug of their text. The <c>Bark:</c> prefix never
        /// collides with the manager's <c>Category:index</c> / GUID ids, so it shares
        /// <see cref="AppSettings.DisabledPhraseIds"/> / <see cref="AppSettings.RemovedPhraseIds"/>.
        /// </summary>
        public static string BarkLineId(string ruleId, BarkVariant v)
        {
            var key = string.IsNullOrWhiteSpace(v.Audio)
                ? "t_" + CompanionPhraseService.Slugify(v.Text)
                : System.IO.Path.GetFileNameWithoutExtension(v.Audio);
            return "Bark:" + ruleId + ":" + key;
        }

        /// <summary>True unless the user disabled or hid this bark line in the Phrase Manager.</summary>
        private static bool IsBarkLineEnabled(string ruleId, BarkVariant v)
        {
            var s = App.Settings?.Current;
            if (s == null) return true;
            var id = BarkLineId(ruleId, v);
            return !s.DisabledPhraseIds.Contains(id) && !s.RemovedPhraseIds.Contains(id);
        }

        /// <summary>One enumerable bark line, surfaced to the Phrase Manager.</summary>
        public readonly record struct BarkLineInfo(
            string LineId, string RuleId, string Trigger, string Text, string? AudioFileName, string? AudioFolder);

        /// <summary>
        /// All inline bark lines for the ACTIVE mod's loaded rule set, for display in the Phrase
        /// Manager. Skips <c>pool_ref</c> rules — those reuse existing phrase categories already shown
        /// in the manager, so surfacing them here would double them up.
        /// </summary>
        public IReadOnlyList<BarkLineInfo> GetAllBarkLines()
        {
            var result = new List<BarkLineInfo>();
            BarkRuleSet rules;
            lock (_gate) rules = _rules;

            foreach (var rule in rules.AllRules)
            {
                if (rule.VariantPool == null || rule.VariantPool.Count == 0) continue; // skip pool_ref
                foreach (var v in rule.VariantPool)
                {
                    if (!v.HasText) continue;
                    string? folder = null;
                    var path = ResolveBarkAudio(v.Audio);
                    if (path != null) folder = System.IO.Path.GetDirectoryName(path);
                    result.Add(new BarkLineInfo(
                        BarkLineId(rule.Id, v), rule.Id, rule.Trigger, v.Text, v.Audio, folder));
                }
            }
            return result;
        }

        /// <summary>
        /// Public access to the per-mod voiceline path resolver, for inline voiced bubbles
        /// (e.g. the "Hey Bambi" wake ack + voice-command confirmations) that render via
        /// GigglePriority directly rather than through the bark engine.
        /// </summary>
        public static string? ResolveModAudio(string? file) => ResolveBarkAudio(file);

        // Last variant index served per voice-line rule, so PickVoiceLine avoids an immediate repeat.
        private readonly Dictionary<string, int> _lastVoiceIdx = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Pick a variant (display text + resolved per-mod audio path) for the given rule id from the
        /// ACTIVE mod's loaded ruleset, avoiding an immediate repeat. Lets inline voiced bubbles (the
        /// "Hey Bambi" wake ack + voice-command confirmations) keep their text single-sourced in the
        /// bark manifests so the spoken clip always matches the on-screen line. Null when the rule or
        /// its pool is absent (caller falls back to its own text, unvoiced).
        /// </summary>
        public (string Text, string? Audio)? PickVoiceLine(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) return null;
            BarkVariant? v;
            lock (_gate)
            {
                var rule = _rules.AllRules.FirstOrDefault(
                    r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
                var pool = rule?.VariantPool;
                if (pool == null || pool.Count == 0) return null;

                var idx = _rng.Next(pool.Count);
                if (pool.Count > 1 && _lastVoiceIdx.TryGetValue(ruleId, out var last) && idx == last)
                    idx = (idx + 1) % pool.Count;
                _lastVoiceIdx[ruleId] = idx;
                v = pool[idx];
            }
            return (v.Text, ResolveBarkAudio(v.Audio));
        }

        /// <summary>
        /// Resolve a variant's voiceline filename to a full path: active mod's
        /// companion_audio folder first, embedded fallback second. Null if absent/missing.
        /// </summary>
        private static string? ResolveBarkAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;

            // 1) packaged mod (InstalledPath)
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (!string.IsNullOrEmpty(modPath))
            {
                var p = System.IO.Path.Combine(modPath, "resources", "sounds", "companion_audio", file);
                if (System.IO.File.Exists(p)) return p;
            }
            // 2) embedded per-mod folder (Bambi/Sissy and, uniformly, the others)
            var modId = App.Mods?.ActiveModId;
            if (!string.IsNullOrEmpty(modId))
            {
                var pm = System.IO.Path.Combine(CompanionPhraseService.CompanionAudioFolder, "mods", modId, file);
                if (System.IO.File.Exists(pm)) return pm;
            }
            // 3) embedded shared fallback
            var embedded = System.IO.Path.Combine(CompanionPhraseService.CompanionAudioFolder, file);
            return System.IO.File.Exists(embedded) ? embedded : null;
        }

        private readonly struct GateDecision
        {
            public bool WouldFire { get; init; }
            public int VariantIndex { get; init; }
            public string Reason { get; init; }
        }

        private GateDecision EvaluateGate(BarkRule rule, List<BarkVariant> pool, bool guaranteed)
        {
            if (pool.Count == 0)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "empty-pool" };

            // Safety and guaranteed reactions bypass the timing/floor gates entirely.
            bool isSafety = rule.Class == BarkClass.Safety;
            bool bypass = isSafety || guaranteed;

            // One-shot (repeatable=false): fire once per scope. Session is in-memory; tier/lifetime
            // consult the persisted latch (AppSettings.BarkLifetimeFired). This dedup is NOT bypassed
            // by `guaranteed` — guaranteed means "ignore timing/floor", not "fire again". Otherwise a
            // guaranteed lifetime one-shot (e.g. a streak milestone) would replay on every launch if
            // the caller's own latch ever got reset. Safety barks (Panic) stay exempt — they may
            // intentionally fire repeatedly and are typically repeatable anyway.
            if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule))
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"already-fired ({rule.Scope})" };

            if (!bypass)
            {
                // A safety bark holds the floor: nothing fires over it for SafetyHoldMs.
                if (DateTime.UtcNow < _safetyHoldUntilUtc)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "safety-active" };

                // Don't talk over a subliminal/flash whisper that's still audible — two voices at once is
                // jarring. Safety/guaranteed barks bypass this (handled above) so panic still speaks.
                if (App.Audio?.IsWhisperAudioPlaying == true)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "whisper-active" };

                // Don't talk over the Chaos narrator (the Madam). She holds the floor; the next
                // eligible event speaks once she's done (anti-stale drops stale queued barks naturally).
                if (Services.Chaos.ChaosNarrator.IsPlaying)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "narrator-active" };

                // Chat-suppression: don't talk over an active conversation.
                int window = App.Settings?.Current?.BarkChatSuppressionMs ?? 10000;
                if (CompanionBusy(window))
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"chat-suppressed ({window}ms)" };

                // Anti-stale: ordinary (queue-class) barks would pile up behind a bubble that's already
                // on screen and, by the time the queue drained, comment on something that happened 10s
                // ago. Drop them while she's mid-bubble — the next eligible event gets a FRESH reaction
                // instead. Preempting barks (non-Normal class or priority >= threshold) are exempt: they
                // route through GigglePriority, which clears the queue and shows the latest immediately.
                bool willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
                if (!willPreempt && (App.AvatarWindow?.IsSpeaking ?? false))
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "speaking" };

                // Global min-gap. Functional triggers (e.g. the video attention-check correction) skip
                // ONLY this floor and keep their own per-rule cooldown below, so a needed correction
                // still speaks even when recent chatter would otherwise wall it off for a full minute.
                if (!GlobalGapExemptTriggers.Contains(rule.Trigger))
                {
                    var sinceGlobal = (DateTime.UtcNow - _globalLastFireUtc).TotalMilliseconds;
                    if (sinceGlobal < GlobalMinGapMs)
                        return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"min-gap ({sinceGlobal:F0}/{GlobalMinGapMs}ms)" };
                }

                // Per-bark cooldown.
                if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last))
                {
                    var sinceRule = (DateTime.UtcNow - last).TotalMilliseconds;
                    if (sinceRule < rule.CooldownMs)
                        return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"cooldown ({sinceRule:F0}/{rule.CooldownMs}ms)" };
                }

                // Probability roll — last gate before variant selection. Lets a high-frequency
                // reactive rule fire only ~Chance of the time (e.g. 0.2 = "~1 in 5").
                if (rule.Chance < 1.0 && _rng.NextDouble() >= rule.Chance)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"chance ({rule.Chance:0.##})" };
            }

            // Variant selection:
            //  • repeatable, pool>1 → pick a RANDOM unused variant; when ALL are used, RECYCLE the pool
            //    (clear the used-set) so the bark keeps talking instead of going silent forever,
            //    reseeding with the last index so we don't immediately repeat the line just spoken.
            //    Random-among-unused (rather than first-unused-in-order) means the play order is
            //    reshuffled every cycle — no-repeat-until-exhausted AND no learnable rhythm.
            //  • one-shot, pool>1 → pick a random line (it only fires once, so use the whole pool
            //    rather than always line 0; otherwise the extra authored variants are dead content).
            int idx = 0;
            if (pool.Count > 1)
            {
                if (rule.Repeatable)
                {
                    var usedKeys = _usedVariantKeys.TryGetValue(rule.Id, out var set) ? set : null;
                    idx = PickVariant(pool, rule.Id, usedKeys);
                    if (idx < 0)
                    {
                        // Exhausted → recycle. Reseed the used-set with the last-fired line so the
                        // next pick avoids repeating the line we just played.
                        var reseed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (_lastVariantKey.TryGetValue(rule.Id, out var lastKey))
                            reseed.Add(lastKey);
                        _usedVariantKeys[rule.Id] = reseed;
                        PersistVariantRotation(rule.Id);
                        idx = PickVariant(pool, rule.Id, reseed);
                        if (idx < 0) idx = 0; // safety (pool>1 means reseed has ≤1 entry, so unreachable)
                    }
                }
                else
                {
                    idx = _rng.Next(pool.Count); // one-shot: random line from the pool
                }
            }

            return new GateDecision { WouldFire = true, VariantIndex = idx, Reason = "OK" };
        }

        private bool AlreadyFiredOnce(BarkRule rule)
        {
            if (_firedOnceSession.Contains(rule.Id)) return true;
            if (rule.Scope != BarkScope.Session)
                return App.Settings?.Current?.IsBarkFired(LatchKey(rule)) == true;
            return false;
        }

        /// <summary>Persisted-latch key: lifetime = id; tier = "id@Tier" so a tier change re-arms it.</summary>
        private static string LatchKey(BarkRule rule) =>
            rule.Scope == BarkScope.Tier
                ? rule.Id + "@" + (App.Patreon?.CurrentTier.ToString() ?? "None")
                : rule.Id;

        private bool CompanionBusy(int windowMs)
        {
            if ((App.AvatarWindow?.IsCompanionBusy(windowMs) ?? false)) return true;
            return windowMs > 0 && (DateTime.UtcNow - _lastUserMessageUtc).TotalMilliseconds < windowMs;
        }

        private void CommitFire(BarkRule rule, int variantIndex, List<BarkVariant> pool)
        {
            var now = DateTime.UtcNow;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;

            // Feed the global recency window so the NEXT pick (any rule) avoids this exact line.
            if (variantIndex >= 0 && variantIndex < pool.Count)
                RememberSpoken(pool[variantIndex].Audio);

            // A fired safety bark holds the floor.
            if (rule.Class == BarkClass.Safety)
                _safetyHoldUntilUtc = now.AddMilliseconds(SafetyHoldMs);

            if (!rule.Repeatable)
            {
                _firedOnceSession.Add(rule.Id);
                // Persist lifetime/tier latches — but never in dry-run (it must not mutate settings).
                if (rule.Scope != BarkScope.Session && !DryRun)
                    App.Settings?.Current?.MarkBarkFired(LatchKey(rule));
            }

            if (variantIndex >= 0 && variantIndex < pool.Count)
            {
                var key = BarkLineId(rule.Id, pool[variantIndex]);
                if (!_usedVariantKeys.TryGetValue(rule.Id, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _usedVariantKeys[rule.Id] = set;
                }
                set.Add(key);
                _lastVariantKey[rule.Id] = key; // for no-immediate-repeat on pool recycle
                PersistVariantRotation(rule.Id);
            }

            // Pool-wide no-repeat for the idle class (each idle line is its own single-variant rule,
            // so per-rule variant rotation above can't help — track at the rule-id level instead).
            if (string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                _usedIdleRules.Add(rule.Id);
                _lastIdleRuleId = rule.Id;
                PersistIdleRotation();
            }
        }

        /// <summary>
        /// Choose a variant index for a repeatable pool. Two layers, both no-repeat:
        ///  1. per-rule — only lines whose LINE ID is NOT in <paramref name="usedKeys"/> (the rule's own
        ///     rotation set); this is the HARD guarantee — a line never replays until the pool's exhausted;
        ///  2. global — among those, PREFER lines whose audio isn't in <see cref="_recentlySpokenSet"/>
        ///     (the last few lines spoken across ALL rules), so a small high-frequency pool stops
        ///     dominating a session and two rules can't echo the same line back-to-back.
        /// Layer 2 is a soft preference: if every per-rule-unused line is also globally-recent we fall
        /// back to the unused set rather than going silent. Random (not first-in-order) within the
        /// chosen set so there's no learnable rhythm. Returns -1 only when every line is in
        /// <paramref name="usedKeys"/> (caller recycles).
        /// </summary>
        private int PickVariant(List<BarkVariant> pool, string ruleId, HashSet<string>? usedKeys)
        {
            // Layer 1: per-rule no-repeat — gather indices whose line id isn't yet used this cycle.
            List<int>? unused = null;
            for (int i = 0; i < pool.Count; i++)
                if (usedKeys == null || !usedKeys.Contains(BarkLineId(ruleId, pool[i])))
                    (unused ??= new List<int>()).Add(i);
            if (unused == null || unused.Count == 0) return -1;

            // Layer 2: global recency — prefer lines not spoken in the last RecentlySpokenMemory barks.
            List<int>? fresh = null;
            if (_recentlySpokenSet.Count > 0)
                foreach (var i in unused)
                    if (!_recentlySpokenSet.Contains(pool[i].Audio ?? string.Empty))
                        (fresh ??= new List<int>()).Add(i);

            var pick = fresh ?? unused;
            return pick[_rng.Next(pick.Count)];
        }

        /// <summary>Record a just-spoken line's audio in the bounded global recency window.</summary>
        private void RememberSpoken(string? audio)
        {
            if (string.IsNullOrEmpty(audio)) return;
            if (!_recentlySpokenSet.Add(audio)) return; // already in-window
            _recentlySpoken.Enqueue(audio);
            while (_recentlySpoken.Count > RecentlySpokenMemory)
                _recentlySpokenSet.Remove(_recentlySpoken.Dequeue());
        }

        // ===================== persistent rotation =====================
        // The no-repeat rotation is mirrored to AppSettings so it survives restarts — otherwise every
        // launch restarts each pool and the "same few" lines get heard again. Save() is debounced (500ms),
        // so persisting on each bark coalesces to ~one write per bark and flushes on shutdown.

        /// <summary>Restore persisted variant/idle rotation into the in-memory sets on startup.</summary>
        private void LoadRotationFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            try
            {
                _usedVariantKeys.Clear();
                foreach (var kv in s.BarkVariantRotation)
                    _usedVariantKeys[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

                _usedIdleRules.Clear();
                foreach (var id in s.BarkIdleRotation)
                    _usedIdleRules.Add(id);
            }
            catch (Exception ex) { App.Logger?.Debug("BarkService: rotation restore failed: {Error}", ex.Message); }
        }

        /// <summary>Mirror one rule's variant rotation into settings and request a (debounced) save.</summary>
        private void PersistVariantRotation(string ruleId)
        {
            if (DryRun) return; // dry-run must not mutate settings
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BarkVariantRotation[ruleId] = _usedVariantKeys.TryGetValue(ruleId, out var set)
                ? new List<string>(set) : new List<string>();
            App.Settings?.Save(suppressCloudBackup: true); // local-only; low-value, rides next real change to cloud
        }

        /// <summary>Mirror the idle rotation into settings and request a (debounced) save.</summary>
        private void PersistIdleRotation()
        {
            if (DryRun) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.BarkIdleRotation = new List<string>(_usedIdleRules);
            App.Settings?.Save(suppressCloudBackup: true);
        }

        // ===================== speak =====================

        private void Speak(BarkRule rule, int variantIndex, BarkContext ctx, List<BarkVariant> pool)
        {
            if (variantIndex < 0 || variantIndex >= pool.Count) return;
            var avatar = App.AvatarWindow;
            if (avatar == null)
            {
                App.Logger?.Debug("[BARK] no avatar window — dropping rule={Rule}", rule.Id);
                return;
            }

            var variant = pool[variantIndex];
            string line = ApplySubstitutions(variant.Text, ctx);
            if (string.IsNullOrWhiteSpace(line)) return;

            // Mute egg (Fork F): an easter_egg bark while audio is fully muted shows a silent, text-only
            // bubble via Giggle (the PlayGiggleSound MasterVolume==0 guard keeps it from making sound).
            bool muted = (App.Settings?.Current?.MasterVolume ?? 0) == 0;
            if (rule.Class == BarkClass.EasterEgg && muted)
            {
                avatar.Giggle(line, mood: rule.Mood);
                App.KeywordTriggers?.MuteKeywordEcho(line, SelfEchoMuteMs);
                return;
            }

            // Resolve the variant's voiceline (if any) to a real path under the active mod.
            string? audioPath = ResolveBarkAudio(variant.Audio);

            // Route by class/priority: non-Normal or high-priority barks preempt (GigglePriority);
            // ordinary barks queue (Giggle). Safety is non-Normal, so it preempts and clears the queue.
            // When a voiceline exists it plays as the bubble's audio (no giggle sound on top).
            bool priority = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
            if (priority)
                avatar.GigglePriority(line, playSound: audioPath == null, aiGenerated: false,
                    phraseAudioPath: audioPath, barkVoice: audioPath != null, mood: rule.Mood);
            else
                avatar.Giggle(line, phraseAudioPath: audioPath, barkVoice: audioPath != null, mood: rule.Mood);

            // Self-echo guard so the bubble text can't trip awareness/OCR off its own output.
            App.KeywordTriggers?.MuteKeywordEcho(line, SelfEchoMuteMs);
        }

        /// <summary>Substitute {0} (focused app) and {key} tokens (from the per-fire context).</summary>
        private static string ApplySubstitutions(string text, BarkContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;

            if (text.Contains("{0}"))
            {
                var app = App.WindowAwareness?.CurrentServiceName;
                if (string.IsNullOrWhiteSpace(app)) app = App.WindowAwareness?.CurrentDetectedName;
                if (string.IsNullOrWhiteSpace(app)) app = "that";
                text = text.Replace("{0}", app);
            }

            foreach (var kvp in ctx.Values)
            {
                var token = "{" + kvp.Key + "}";
                if (text.Contains(token))
                    text = text.Replace(token, kvp.Value?.ToString() ?? "");
            }
            return text;
        }

        private void LogDecision(string trigger, BarkRule rule, GateDecision decision)
        {
            var pool = ResolvePool(rule);
            string preview = decision.VariantIndex >= 0 && decision.VariantIndex < pool.Count
                ? Truncate(pool[decision.VariantIndex].Text, 48)
                : "(n/a)";
            string tag = DryRun ? "[BARK dry-run]" : "[BARK]";

            if (decision.WouldFire)
            {
                string verb = DryRun ? "WOULD FIRE" : "FIRE";
                App.Logger?.Information(
                    "{Tag} {Verb} trigger={Trigger} rule={Rule} class={Class} mood={Mood} priority={Priority} variant#={Idx} line=\"{Preview}\"",
                    tag, verb, trigger, rule.Id, rule.Class, rule.Mood, rule.Priority, decision.VariantIndex, preview);
            }
            else
            {
                App.Logger?.Information(
                    "{Tag} blocked trigger={Trigger} rule={Rule} class={Class} priority={Priority} reason={Reason}",
                    tag, trigger, rule.Id, rule.Class, rule.Priority, decision.Reason);
            }
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }
}
