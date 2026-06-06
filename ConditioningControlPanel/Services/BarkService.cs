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
        private readonly Dictionary<string, HashSet<int>> _usedVariants = new(StringComparer.OrdinalIgnoreCase);
        // Last variant index fired per rule — used to avoid an immediate repeat when a repeatable
        // pool is exhausted and recycled (see EvaluateGate).
        private readonly Dictionary<string, int> _lastVariantIndex = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _globalLastFireUtc = DateTime.MinValue;

        /// <summary>Global minimum gap between any two barks.</summary>
        private const int GlobalMinGapMs = 4000;

        /// <summary>Barks at/above this priority (or any non-Normal class) speak via GigglePriority; lower ones queue via Giggle.</summary>
        private const int PriorityBarkThreshold = 100;

        /// <summary>After rendering a bark, mute its line this long so it can't re-trigger awareness/OCR.</summary>
        private const int SelfEchoMuteMs = 8000;

        /// <summary>How long a safety bark holds the floor (no non-safety bark may fire). Approximate — we have no speech-duration callback.</summary>
        private const int SafetyHoldMs = 6000;

        private static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);

        // Coin flip is varied per call; not security-sensitive.
        private readonly Random _rng = new();
        private DateTime _lastUserMessageUtc = DateTime.MinValue;
        private DateTime _safetyHoldUntilUtc = DateTime.MinValue;
        private PatreonTier _lastTier = PatreonTier.None; // to detect tier-up for the upgrade egg

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
                    _usedVariants.Clear();
                    _firedOnceSession.Clear();
                    _lastFiredUtc.Clear();
                    _lastVariantIndex.Clear();
                }
                App.Logger?.Information("BarkService: reloaded {Count} rules (per-rule rotation state reset)", fresh.Count);
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
                        .Set("category", a?.Category.ToString() ?? "")));
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

                EventHandler started = (_, __) => { _state.ResetSessionScoped(); Raise("SessionStarted"); };
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
        private void Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            try
            {
                var rules = _rules.ForTrigger(trigger);
                if (rules.Count == 0) return;

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
                        return;
                    }

                    var resolved = ResolvePool(winner);
                    var decision = EvaluateGate(winner, resolved, guaranteed);
                    LogDecision(trigger, winner, decision);

                    if (decision.WouldFire)
                    {
                        CommitFire(winner, decision.VariantIndex);
                        if (!DryRun)
                        {
                            toSpeak = winner;
                            variantIndex = decision.VariantIndex;
                            pool = resolved;
                        }
                    }
                }

                if (toSpeak != null && pool != null)
                    Speak(toSpeak, variantIndex, ctx, pool);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: Raise('{Trigger}') failed", trigger);
            }
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
                case "session_running": return _state.SessionRunning;
                case "session_elapsed_sec": return _state.SessionElapsedSeconds;
                case "session_phase_index": return _state.SessionPhaseIndex;
                case "blink_count": return (double)_state.BlinkCount;
                case "face_lost_sec": return _state.FaceLostSeconds;
                case "mod_switches_60s": return _state.ModSwitchesWithin(RapidModSwitchWindow);
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
            if (rule.VariantPool != null && rule.VariantPool.Count > 0) return rule.VariantPool;
            if (!string.IsNullOrWhiteSpace(rule.PoolRef))
            {
                var phrases = App.Mods?.GetPhrases(rule.PoolRef!);
                if (phrases != null && phrases.Length > 0)
                    return phrases.Select(p => new BarkVariant(p)).ToList(); // pool-ref lines are text-only
            }
            return new List<BarkVariant>();
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

            // Safety and guaranteed reactions bypass the gate entirely.
            bool isSafety = rule.Class == BarkClass.Safety;
            bool bypass = isSafety || guaranteed;

            if (!bypass)
            {
                // A safety bark holds the floor: nothing fires over it for SafetyHoldMs.
                if (DateTime.UtcNow < _safetyHoldUntilUtc)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "safety-active" };

                // Don't talk over a subliminal/flash whisper that's still audible — two voices at once is
                // jarring. Safety/guaranteed barks bypass this (handled above) so panic still speaks.
                if (App.Audio?.IsWhisperAudioPlaying == true)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "whisper-active" };

                // Chat-suppression: don't talk over an active conversation.
                int window = App.Settings?.Current?.BarkChatSuppressionMs ?? 10000;
                if (CompanionBusy(window))
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"chat-suppressed ({window}ms)" };

                // One-shot (repeatable=false): fire once per scope. Session is in-memory;
                // tier/lifetime consult the persisted latch (AppSettings.BarkLifetimeFired).
                if (!rule.Repeatable && AlreadyFiredOnce(rule))
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"already-fired ({rule.Scope})" };

                // Global min-gap.
                var sinceGlobal = (DateTime.UtcNow - _globalLastFireUtc).TotalMilliseconds;
                if (sinceGlobal < GlobalMinGapMs)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"min-gap ({sinceGlobal:F0}/{GlobalMinGapMs}ms)" };

                // Per-bark cooldown.
                if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last))
                {
                    var sinceRule = (DateTime.UtcNow - last).TotalMilliseconds;
                    if (sinceRule < rule.CooldownMs)
                        return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"cooldown ({sinceRule:F0}/{rule.CooldownMs}ms)" };
                }
            }

            // Variant selection:
            //  • repeatable, pool>1 → rotate through unused; when ALL are used, RECYCLE the pool
            //    (clear the used-set) so the bark keeps talking instead of going silent forever,
            //    reseeding with the last index so we don't immediately repeat the line just spoken.
            //  • one-shot, pool>1 → pick a random line (it only fires once, so use the whole pool
            //    rather than always line 0; otherwise the extra authored variants are dead content).
            int idx = 0;
            if (pool.Count > 1)
            {
                if (rule.Repeatable)
                {
                    var used = _usedVariants.TryGetValue(rule.Id, out var set) ? set : null;
                    idx = FirstUnused(pool.Count, used);
                    if (idx < 0)
                    {
                        // Exhausted → recycle. Reseed the used-set with the last-fired index so the
                        // next pick avoids repeating the line we just played.
                        var reseed = new HashSet<int>();
                        if (_lastVariantIndex.TryGetValue(rule.Id, out var lastIdx))
                            reseed.Add(lastIdx);
                        _usedVariants[rule.Id] = reseed;
                        idx = FirstUnused(pool.Count, reseed);
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

        private void CommitFire(BarkRule rule, int variantIndex)
        {
            var now = DateTime.UtcNow;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;

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

            if (variantIndex >= 0)
            {
                if (!_usedVariants.TryGetValue(rule.Id, out var set))
                {
                    set = new HashSet<int>();
                    _usedVariants[rule.Id] = set;
                }
                set.Add(variantIndex);
                _lastVariantIndex[rule.Id] = variantIndex; // for no-immediate-repeat on pool recycle
            }
        }

        /// <summary>First index in [0,count) not present in <paramref name="used"/>, or -1 if all are used.</summary>
        private static int FirstUnused(int count, HashSet<int>? used)
        {
            for (int i = 0; i < count; i++)
                if (used == null || !used.Contains(i)) return i;
            return -1;
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
                avatar.Giggle(line);
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
                    phraseAudioPath: audioPath, barkVoice: audioPath != null);
            else
                avatar.Giggle(line, phraseAudioPath: audioPath, barkVoice: audioPath != null);

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
