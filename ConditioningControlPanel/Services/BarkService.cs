using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        // --- reused gate primitives (cooldown dict / global min-gap / one-fire latch / variant rotation) ---
        private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<int>> _usedVariants = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _globalLastFireUtc = DateTime.MinValue;

        /// <summary>Global minimum gap between any two barks. (Speak-path PR will surface this as a setting.)</summary>
        private const int GlobalMinGapMs = 4000;

        private static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);

        /// <summary>PR1 has no speak path; the matcher only logs. Always true for now.</summary>
        public bool DryRun { get; set; } = true;

        public BarkState State => _state;

        // ===================== lifecycle =====================

        public void Start()
        {
            if (_started) return;
            _started = true;

            try
            {
                _rules = BarkRuleLoader.Load();
                _state.CaptureLaunchRecency(App.Settings?.Current?.LastSeenUtc);

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
        /// Reload rules from disk (e.g. after a mod switch so the new mod's manifest applies).
        /// </summary>
        public void ReloadRules()
        {
            try
            {
                _rules = BarkRuleLoader.Load();
                App.Logger?.Information("BarkService: reloaded {Count} rules", _rules.Count);
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
                Wire<Action>(h => App.Webcam.OnFaceFound += h, h => App.Webcam.OnFaceFound -= h,
                    () => { _state.FaceFound(); Raise("FaceFound"); });
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
                    () => { if (App.Video?.IsPlaying == true) Raise("AttentionCheckFail", c => c.Set("video_playing", true)); });
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
                Wire<EventHandler>(h => App.BouncingText.OnBounce += h, h => App.BouncingText.OnBounce -= h,
                    (_, __) => Raise("BouncingTextBounce"));

            // ---- Lock card (single subscriber; Fork-D coin flip lands with the speak path) ----
            if (App.LockCard != null)
                Wire<EventHandler<LockCardCompletedEventArgs>>(
                    h => App.LockCard.LockCardCompleted += h, h => App.LockCard.LockCardCompleted -= h,
                    (_, e) => Raise("LockCardCompleted", c => c
                        .Set("phrase", e.Phrase ?? "")
                        .Set("mistakes", e.Mistakes)
                        .Set("repeats", e.Repeats)));

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
                    (_, kt) => Raise("KeywordTriggerFired", c => c.Set("keyword", kt?.Keyword ?? "")));
            if (App.WindowAwareness != null)
            {
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.ActivityChanged += h, h => App.WindowAwareness.ActivityChanged -= h,
                    (_, a) => Raise("ActivityChanged", c => c.Set("activity", a?.ToString() ?? "")));
                Wire<EventHandler<ActivityChangedEventArgs>>(h => App.WindowAwareness.StillOnActivity += h, h => App.WindowAwareness.StillOnActivity -= h,
                    (_, a) => Raise("StillOnActivity", c => c.Set("activity", a?.ToString() ?? "")));
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
                    (_, __) => Raise("UserMessageSent"));
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
                    (_, mod) => { _state.RegisterModSwitch(); Raise("ModChanged", c => c
                        .Set("mod", mod?.Id ?? "")
                        .Set("mod_switches_60s", _state.ModSwitchesWithin(RapidModSwitchWindow))); });
            if (App.ActivityTracker != null)
                Wire<EventHandler<bool>>(h => App.ActivityTracker.IdleStateChanged += h, h => App.ActivityTracker.IdleStateChanged -= h,
                    (_, idle) => Raise("IdleStateChanged", c => c.Set("idle", idle)));
            if (App.Update != null)
                Wire<EventHandler<UpdateInfo>>(h => App.Update.UpdateAvailable += h, h => App.Update.UpdateAvailable -= h,
                    (_, __) => Raise("UpdateAvailable"));
            if (App.Patreon != null)
                Wire<EventHandler<PatreonTier>>(h => App.Patreon.TierChanged += h, h => App.Patreon.TierChanged -= h,
                    (_, tier) => Raise("PatreonTierChanged", c => c.Set("tier", tier.ToString())));
            if (App.Discord != null)
                Wire<EventHandler<bool>>(h => App.Discord.AuthenticationChanged += h, h => App.Discord.AuthenticationChanged -= h,
                    (_, ok) => Raise("DiscordAuthChanged", c => c.Set("authenticated", ok)));

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
                    Raise("SessionPhaseChanged", c => c.Set("phase_index", e?.PhaseIndex ?? -1));

                engine.SessionStarted += started;
                engine.SessionStopped += stopped;
                engine.SessionCompleted += completed;
                engine.PhaseChanged += phase;

                _engineUnsubscribe.Add(() => engine.SessionStarted -= started);
                _engineUnsubscribe.Add(() => engine.SessionStopped -= stopped);
                _engineUnsubscribe.Add(() => engine.SessionCompleted -= completed);
                _engineUnsubscribe.Add(() => engine.PhaseChanged -= phase);

                App.Logger?.Debug("BarkService: attached to SessionEngine");
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "BarkService: AttachSessionEngine failed"); }
        }

        /// <summary>Wire the MainWindow-owned tray icon (created in MainWindow's constructor).</summary>
        public void AttachTray(TrayIconService tray)
        {
            if (tray == null) return;
            try
            {
                Action wake = () => Raise("WakeBambiRequested");
                tray.OnWakeBambiRequested += wake;
                _unsubscribe.Add(() => tray.OnWakeBambiRequested -= wake);
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

        // ===================== matcher (dry-run) =====================

        private void Raise(string trigger, Action<BarkContext>? fill = null)
        {
            try
            {
                var rules = _rules.ForTrigger(trigger);
                if (rules.Count == 0) return;

                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);

                lock (_gate)
                {
                    // Highest-priority rule whose conditions pass.
                    BarkRule? winner = null;
                    foreach (var rule in rules) // already priority-descending
                    {
                        if (ConditionsPass(rule, ctx)) { winner = rule; break; }
                    }

                    if (winner == null)
                    {
                        App.Logger?.Debug("[BARK dry-run] trigger={Trigger} no rule matched conditions", trigger);
                        return;
                    }

                    var pool = ResolvePool(winner);
                    var decision = EvaluateGate(winner, pool);
                    LogDecision(trigger, winner, decision, ctx);

                    if (decision.WouldFire)
                        CommitFire(winner, decision.VariantIndex);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "BarkService: Raise('{Trigger}') failed", trigger);
            }
        }

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

        private List<string> ResolvePool(BarkRule rule)
        {
            if (rule.VariantPool != null && rule.VariantPool.Count > 0) return rule.VariantPool;
            if (!string.IsNullOrWhiteSpace(rule.PoolRef))
            {
                var phrases = App.Mods?.GetPhrases(rule.PoolRef!);
                if (phrases != null && phrases.Length > 0) return phrases.ToList();
            }
            return new List<string>();
        }

        private readonly struct GateDecision
        {
            public bool WouldFire { get; init; }
            public int VariantIndex { get; init; }
            public string Reason { get; init; }
        }

        private GateDecision EvaluateGate(BarkRule rule, List<string> pool)
        {
            if (pool.Count == 0)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "empty-pool" };

            // Safety bypasses cooldown / min-gap / one-fire entirely.
            bool isSafety = rule.Class == BarkClass.Safety;

            // One-shot (repeatable=false): fire once per scope. PR1 enforces session scope in memory;
            // tier/lifetime persistence comes with the speak path.
            if (!isSafety && !rule.Repeatable && _firedOnceSession.Contains(rule.Id))
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"already-fired ({rule.Scope})" };

            if (!isSafety)
            {
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

            // Variant selection: repeatable rotates unused (no repeat in-session); one-shot uses index 0.
            int idx = 0;
            if (rule.Repeatable && pool.Count > 1)
            {
                var used = _usedVariants.TryGetValue(rule.Id, out var set) ? set : null;
                idx = -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (used == null || !used.Contains(i)) { idx = i; break; }
                }
                if (idx < 0)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "variants-exhausted (session)" };
            }

            // NOTE: chat-suppression + self-echo mute are speak-path concerns (next PR); not evaluated here.
            return new GateDecision { WouldFire = true, VariantIndex = idx, Reason = "OK" };
        }

        private void CommitFire(BarkRule rule, int variantIndex)
        {
            var now = DateTime.UtcNow;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;
            if (!rule.Repeatable) _firedOnceSession.Add(rule.Id);
            if (variantIndex >= 0)
            {
                if (!_usedVariants.TryGetValue(rule.Id, out var set))
                {
                    set = new HashSet<int>();
                    _usedVariants[rule.Id] = set;
                }
                set.Add(variantIndex);
            }
        }

        private void LogDecision(string trigger, BarkRule rule, GateDecision decision, BarkContext ctx)
        {
            var pool = ResolvePool(rule);
            string preview = decision.VariantIndex >= 0 && decision.VariantIndex < pool.Count
                ? Truncate(pool[decision.VariantIndex], 48)
                : "(n/a)";

            if (decision.WouldFire)
            {
                App.Logger?.Information(
                    "[BARK dry-run] WOULD FIRE trigger={Trigger} rule={Rule} class={Class} mood={Mood} priority={Priority} variant#={Idx} line=\"{Preview}\" gate=OK",
                    trigger, rule.Id, rule.Class, rule.Mood, rule.Priority, decision.VariantIndex, preview);
            }
            else
            {
                App.Logger?.Information(
                    "[BARK dry-run] blocked trigger={Trigger} rule={Rule} class={Class} priority={Priority} reason={Reason}",
                    trigger, rule.Id, rule.Class, rule.Priority, decision.Reason);
            }
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }
}
