using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Services.Bark
{
    /// <summary>What the engine hands the mouth: the final line, its resolved clip (or null), whether it
    /// preempts (WPF GigglePriority) or queues (WPF Giggle), the mood, and the silent mute-egg path.</summary>
    public readonly record struct BarkSpeech(string Text, string? AudioPath, bool Priority, string? Mood, bool SilentEgg);

    /// <summary>
    /// The matcher, gate and rotation of WPF 7.1.5 <c>Services/Companion/BarkService.cs</c> (ai#1), in Core
    /// so every head runs the same rules: priority walk over the rules of a trigger, conditions with the
    /// _gte/_lte/_gt/_lt/_eq suffixes, the gate (companion off, one-shot latches, safety hold, whisper,
    /// chat suppression, anti-stale, the 60 s global floor with its exemptions, per-rule cooldown, chance),
    /// no-repeat variant rotation persisted in AppSettings, the pool-wide idle rotation with the 0.35
    /// gated bias, and {0}/{key} substitutions. The head owns the event subscriptions and the mouth.
    /// Same numbers as WPF.
    /// </summary>
    public sealed class BarkEngine
    {
        public const int GlobalMinGapMs = 60000;
        public const int PriorityBarkThreshold = 100;
        public const int SafetyHoldMs = 6000;
        public const int RecentlySpokenMemory = 8;
        public const double GatedIdleBias = 0.35;
        public static readonly TimeSpan RapidModSwitchWindow = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan RapidClickWindow = TimeSpan.FromSeconds(60);

        /// <summary>WPF GlobalGapExemptTriggers: functional corrections keep their authored cadence.</summary>
        public static readonly HashSet<string> GlobalGapExemptTriggers = new(StringComparer.OrdinalIgnoreCase)
        {
            "AttentionCheckFail",
            "PossessionEffect", "PossessionTripwire", "PossessionWarden", "PossessionRungChanged",
            "PossessionTimerRestarted", "PossessionRemember",
            "EmergencyExitOpened", "EmergencyExitVerdict",
            "LockdownConscript",
        };

        private static readonly HashSet<string> TextOnlyAttribution = new(StringComparer.OrdinalIgnoreCase)
        {
            "PossessionEffect", "PossessionTripwire", "PossessionWarden", "PossessionRungChanged",
            "PossessionTimerRestarted", "PossessionRemember", "LockdownConscript",
            "EmergencyExitOpened", "EmergencyExitVerdict",
        };

        private readonly object _gate = new();
        private BarkRuleSet _rules = BarkRuleSet.Empty;
        private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _usedVariantKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _globalLastFireUtc = DateTime.MinValue;
        private DateTime _safetyHoldUntilUtc = DateTime.MinValue;
        private DateTime _lastUserMessageUtc = DateTime.MinValue;
        private readonly Queue<string> _recentlySpoken = new();
        private readonly HashSet<string> _recentlySpokenSet = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _usedIdleRules = new(StringComparer.OrdinalIgnoreCase);
        private string? _lastIdleRuleId;
        private readonly Dictionary<string, int> _lastVoiceIdx = new(StringComparer.OrdinalIgnoreCase);
        private readonly Random _rng;

        public BarkEngineState State { get; } = new();
        public bool DryRun { get; set; }

        // ---------------- head seams (unseeded = the quiet answer) ----------------
        /// <summary>The mouth. Unseeded = nothing is said (and nothing is committed).</summary>
        public Action<BarkSpeech>? Speak;
        /// <summary>WPF App.AvatarWindow.IsSpeaking.</summary>
        public Func<bool>? IsSpeaking;
        /// <summary>WPF App.AvatarWindow.IsCompanionBusy(windowMs).</summary>
        public Func<int, bool>? IsCompanionBusy;
        /// <summary>WPF App.Audio.IsWhisperAudioPlaying.</summary>
        public Func<bool>? IsWhisperPlaying;
        /// <summary>WPF ChaosNarrator.IsPlaying.</summary>
        public Func<bool>? IsNarratorPlaying;
        /// <summary>WPF App.EmiDesk.AvatarMuted.</summary>
        public Func<bool>? EmiDeskAvatarMuted;
        /// <summary>WPF App.WindowAwareness CurrentServiceName ?? CurrentDetectedName, for {0}.</summary>
        public Func<string?>? FocusedAppName;
        /// <summary>The Patreon tier name for tier-scoped latches ("None" when unseeded).</summary>
        public Func<string?>? TierName;
        /// <summary>Head-only live reads (video_playing, webcam_running, achievements_all_unlocked...).
        /// Return null for "not mine".</summary>
        public Func<string, object?>? LiveField;
        /// <summary>Voiceline file -> full path (WPF ResolveBarkAudio). Default: the active mod's ladder.</summary>
        public Func<string, string?> ResolveAudio = file =>
        {
            var pick = ModCompanionContent.ResolveActive(CompanionChannel.BarkAudio, file);
            return pick.Found ? pick.Path : null;
        };
        /// <summary>pool_ref phrase categories (WPF App.Mods.GetPhrases).</summary>
        public Func<string, string[]?> PoolRefPhrases = cat => CoreMods.GetPhrases(cat);
        /// <summary>Rule loader (tests swap it).</summary>
        public Func<BarkRuleSet> LoadRules = () => BarkRuleManifest.Load();

        /// <summary>WPF BarkSpoken: after a line reached the mouth, with the final text (brain echo).</summary>
        public event Action<BarkRule, string>? BarkSpoken;

        public BarkEngine(Random? rng = null) { _rng = rng ?? new Random(); }

        public int RuleCount { get { lock (_gate) return _rules.Count; } }

        public void Start()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN"), "1", StringComparison.Ordinal)) DryRun = true;
            var fresh = SafeLoad();
            lock (_gate) _rules = fresh;
            LoadRotationFromSettings();
            State.CaptureLaunchRecency(CoreSettings.Current.LastSeenUtc, instantThresholdSeconds: 60);
            Log.Information("BarkEngine started - {Count} rules, dry-run={DryRun}", fresh.Count, DryRun);
        }

        /// <summary>WPF ReloadRules: after a mod switch. Session one-shots and cooldowns re-arm; rotation stays.</summary>
        public void ReloadRules()
        {
            var fresh = SafeLoad();
            lock (_gate)
            {
                _rules = fresh;
                _firedOnceSession.Clear();
                _lastFiredUtc.Clear();
            }
        }

        private BarkRuleSet SafeLoad()
        {
            try { return LoadRules() ?? BarkRuleSet.Empty; }
            catch (Exception ex) { Log.Warning(ex, "BarkEngine: rule load failed"); return BarkRuleSet.Empty; }
        }

        public void NoteUserMessage() { lock (_gate) _lastUserMessageUtc = DateTime.UtcNow; }
        public void NotifyExternalLineSpoken() { lock (_gate) _globalLastFireUtc = DateTime.UtcNow; }

        /// <summary>
        /// WPF BarkService.RaiseAwarenessBark (:289). The arbiter decided the moment is worth a canned
        /// line: a Milestone frame raises StillOnActivity, anything else ActivityChanged. The arbiter
        /// records this delivery itself, so <see cref="CommitFire"/> must not also report it.
        /// </summary>
        public bool RaiseAwarenessBark(Awareness.ContextFrame frame)
        {
            if (frame == null) return false;
            try { AwarenessReactionStarting?.Invoke(); } catch { }

            _inAwarenessBark = true;
            try
            {
                if (frame.Transition == Awareness.TransitionKind.Milestone)
                {
                    return Raise("StillOnActivity", c => c
                        .Set("activity", frame.ServiceName ?? "")
                        .Set("still_minutes", Math.Floor(Math.Max(0, frame.DwellSeconds) / 60.0))
                        .Set("app_cluster", frame.AppCluster ?? "")
                        .Set("app", frame.AppId ?? ""));
                }

                return Raise("ActivityChanged", c => c
                    .Set("activity", frame.ServiceName ?? "")
                    .Set("category", frame.Category.ToString())
                    .Set("app_cluster", frame.AppCluster ?? "")
                    .Set("app", frame.AppId ?? ""));
            }
            finally { _inAwarenessBark = false; }
        }

        private bool _inAwarenessBark;

        /// <summary>WPF App.EmiDesk.NoteAwarenessReaction(): the tube is about to talk about the window.</summary>
        public Action? AwarenessReactionStarting;

        // ---------------- matcher ----------------

        /// <summary>WPF Raise. True when a line went to the mouth.</summary>
        public bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
        {
            EmiDesk.EmiBarkBridge.Mirror(trigger, fill);   // WPF BarkService.cs:1106: EMI hears the same funnel (no-op with no desk)
            try
            {
                IReadOnlyList<BarkRule> rules;
                lock (_gate) rules = _rules.ForTrigger(trigger);
                if (rules.Count == 0) return false;
                var ctx = new BarkContext(trigger);
                fill?.Invoke(ctx);

                BarkRule? toSpeak = null; int idx = -1; List<BarkVariant>? pool = null;
                lock (_gate)
                {
                    BarkRule? winner = null;
                    foreach (var rule in rules) if (ConditionsPass(rule, ctx)) { winner = rule; break; }
                    if (winner == null) return false;
                    (toSpeak, idx, pool) = DecideLocked(trigger, winner, guaranteed);
                }
                if (toSpeak != null && pool != null) { SpeakLine(toSpeak, idx, ctx, pool); return true; }
                return false;
            }
            catch (Exception ex) { Log.Warning(ex, "BarkEngine: Raise('{Trigger}') failed", trigger); return false; }
        }

        /// <summary>WPF DispatchIdle: one Idle bark, pool-wide no-repeat. True when one spoke.</summary>
        public bool DispatchIdle()
        {
            try
            {
                if (CoreSettings.Current.MasterVolume == 0) return false;
                if (Safe(IsSpeaking)) return false;
                BarkRule? toSpeak = null; int idx = -1; List<BarkVariant>? pool = null;
                var ctx = new BarkContext("Idle");
                lock (_gate)
                {
                    var eligible = _rules.ForTrigger("Idle").Where(r => ConditionsPass(r, ctx)).ToList();
                    if (eligible.Count == 0) return false;
                    var winner = PickIdleRuleLocked(eligible);
                    if (winner == null) return false;
                    (toSpeak, idx, pool) = DecideLocked("Idle", winner, guaranteed: false);
                }
                if (toSpeak != null && pool != null) { SpeakLine(toSpeak, idx, ctx, pool); return true; }
                return false;
            }
            catch (Exception ex) { Log.Warning(ex, "BarkEngine: DispatchIdle failed"); return false; }
        }

        private BarkRule? PickIdleRuleLocked(List<BarkRule> eligible)
        {
            var unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
            if (unused.Count == 0)
            {
                _usedIdleRules.Clear();
                if (_lastIdleRuleId != null) _usedIdleRules.Add(_lastIdleRuleId);
                PersistIdleRotation();
                unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
                if (unused.Count == 0) unused = eligible;
            }
            var gated = unused.Where(r => r.Conditions != null && r.Conditions.Count > 0).ToList();
            if (gated.Count > 0 && _rng.NextDouble() < GatedIdleBias) return gated[_rng.Next(gated.Count)];
            return unused[_rng.Next(unused.Count)];
        }

        private (BarkRule?, int, List<BarkVariant>?) DecideLocked(string trigger, BarkRule winner, bool guaranteed)
        {
            var resolved = ResolvePool(winner);
            var (fire, idx, reason) = EvaluateGate(winner, resolved, guaranteed);
            if (!fire)
            {
                Log.Debug("[BARK] blocked trigger={Trigger} rule={Rule} reason={Reason}", trigger, winner.Id, reason);
                return (null, -1, null);
            }
            Log.Information("[BARK] {Verb} trigger={Trigger} rule={Rule} class={Class} variant#={Idx}",
                DryRun ? "WOULD FIRE" : "FIRE", trigger, winner.Id, winner.Class, idx);
            if (Speak == null && !DryRun) return (null, -1, null);   // no mouth: never spend a latch on silence
            CommitFire(winner, idx, resolved);
            return DryRun ? (null, -1, null) : (winner, idx, resolved);
        }

        public bool ConditionsPass(BarkRule rule, BarkContext ctx)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0) return true;
            foreach (var kvp in rule.Conditions)
                if (!ConditionPass(kvp.Key, kvp.Value, ctx)) return false;
            return true;
        }

        private bool ConditionPass(string key, object? expected, BarkContext ctx)
        {
            string field = key, op = "eq";
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
                if (expected is bool eb && TryBool(actual, out var ab)) return ab == eb;
                if (TryDouble(expected, out var en) && TryDouble(actual, out var an)) return Math.Abs(an - en) < 0.0001;
                return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
            }
            if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e)) return false;
            return op switch { "gte" => a >= e, "lte" => a <= e, "gt" => a > e, "lt" => a < e, _ => false };
        }

        private object? ResolveField(string field, BarkContext ctx)
        {
            var s = CoreSettings.Current;
            switch (field.ToLowerInvariant())
            {
                case "session_running": return State.SessionRunning;
                case "setup_idle_sec": return State.SetupIdleSeconds;
                case "session_elapsed_sec": return State.SessionElapsedSeconds;
                case "session_phase_index": return State.SessionPhaseIndex;
                case "blink_count": return (double)State.BlinkCount;
                case "face_lost_sec": return State.FaceLostSeconds;
                case "mod_switches_60s": return State.ModSwitchesWithin(RapidModSwitchWindow);
                case "clicks_60s": return State.AvatarClicksWithin(RapidClickWindow);
                case "days_away": return State.DaysAwayAtLaunch;
                case "instant_relaunch": return State.InstantRelaunch;
                case "master_volume": return (double)s.MasterVolume;
                case "mute": return s.MasterVolume == 0;
                case "player_level": return (double)s.PlayerLevel;
                case "total_sessions": return (double)s.TotalSessions;
                case "daily_quest_streak": return (double)s.DailyQuestStreak;
                case "current_streak": return (double)s.CurrentStreak;
                case "is_nye": { var now = DateTime.Now; return (now.Month == 12 && now.Day == 31) || (now.Month == 1 && now.Day == 1); }
                case "local_hour": return (double)DateTime.Now.Hour;
                case "phase_name": return State.CurrentPhaseName;
                case "phase_is_deepener": return State.CurrentPhaseIsDeepener;
                case "sessions_7d": return (double)s.SessionsWithinDays(7);
                case "late_sessions_7d": return (double)s.LateSessionsWithinDays(7);
                case "sessions_today": return (double)s.SessionsToday();
                case "same_mod_run": return (double)s.SameModRun;
                case "pauses_this_session": return (double)State.PauseCount;
                case "refocus_this_session": return (double)State.RefocusCount;
                case "session_planned_min": return State.SessionPlannedMinutes;
                case "is_late_hour": { int h = DateTime.Now.Hour; return h >= 23 || h < 4; }
            }
            // Head reads (video_playing, webcam_running, achievements_all_unlocked, all_skills_unlocked).
            // A head without the service answers the WPF default for a missing service (false).
            switch (field.ToLowerInvariant())
            {
                case "video_playing":
                case "webcam_running":
                case "achievements_all_unlocked":
                case "all_skills_unlocked":
                    if (ctx.Values.TryGetValue(field, out var cv)) return cv;
                    try { return LiveField?.Invoke(field) ?? false; } catch { return false; }
            }
            return ctx.Values.TryGetValue(field, out var v) ? v : null;
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
                case string str when double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var p): value = p; return true;
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
                case long l: value = l != 0; return true;
                case double d: value = Math.Abs(d) > double.Epsilon; return true;
                case string s when bool.TryParse(s, out var p): value = p; return true;
                default: return false;
            }
        }

        private List<BarkVariant> ResolvePool(BarkRule rule)
        {
            if (rule.VariantPool != null && rule.VariantPool.Count > 0)
                return rule.VariantPool.Where(v => IsBarkLineEnabled(rule.Id, v)).ToList();
            if (!string.IsNullOrWhiteSpace(rule.PoolRef))
            {
                string[]? phrases = null;
                try { phrases = PoolRefPhrases(rule.PoolRef!); } catch { }
                if (phrases != null && phrases.Length > 0) return phrases.Select(p => new BarkVariant(p)).ToList();
            }
            return new List<BarkVariant>();
        }

        /// <summary>WPF BarkService.BarkLineId: "Bark:rule:clip" or "Bark:rule:t_slug".</summary>
        public static string BarkLineId(string ruleId, BarkVariant v) => CompanionPhraseIds.BarkLineId(ruleId, v.Text, v.Audio);

        private static bool IsBarkLineEnabled(string ruleId, BarkVariant v)
        {
            var s = CoreSettings.Current;
            var id = BarkLineId(ruleId, v);
            return !s.DisabledPhraseIds.Contains(id) && !s.RemovedPhraseIds.Contains(id);
        }

        /// <summary>WPF GetAllBarkLines: every inline line of the active rule set, for the Phrase Manager.</summary>
        public IReadOnlyList<BarkLineInfo> GetAllBarkLines()
        {
            BarkRuleSet rules;
            lock (_gate) rules = _rules;
            var result = new List<BarkLineInfo>();
            foreach (var rule in rules.AllRules)
            {
                if (rule.VariantPool == null || rule.VariantPool.Count == 0) continue;
                foreach (var v in rule.VariantPool)
                {
                    if (!v.HasText) continue;
                    string? folder = null;
                    var path = SafeResolveAudio(v.Audio);
                    if (path != null) folder = System.IO.Path.GetDirectoryName(path);
                    result.Add(new BarkLineInfo(BarkLineId(rule.Id, v), rule.Id, rule.Trigger, v.Text, v.Audio, folder));
                }
            }
            return result;
        }

        /// <summary>WPF PickVoiceLine: a variant of one rule, avoiding an immediate repeat.</summary>
        public (string Text, string? Audio)? PickVoiceLine(string ruleId)
        {
            if (string.IsNullOrWhiteSpace(ruleId)) return null;
            BarkVariant v;
            lock (_gate)
            {
                var rule = _rules.AllRules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
                var pool = rule?.VariantPool;
                if (pool == null || pool.Count == 0) return null;
                var idx = _rng.Next(pool.Count);
                if (pool.Count > 1 && _lastVoiceIdx.TryGetValue(ruleId, out var last) && idx == last) idx = (idx + 1) % pool.Count;
                _lastVoiceIdx[ruleId] = idx;
                v = pool[idx];
            }
            return (v.Text, SafeResolveAudio(v.Audio));
        }

        private string? SafeResolveAudio(string? file)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try { return ResolveAudio(file!); } catch { return null; }
        }

        private static bool Safe(Func<bool>? f) { try { return f?.Invoke() == true; } catch { return false; } }

        private (bool Fire, int Index, string Reason) EvaluateGate(BarkRule rule, List<BarkVariant> pool, bool guaranteed)
        {
            if (pool.Count == 0) return (false, -1, "empty-pool");
            bool isSafety = rule.Class == BarkClass.Safety;
            bool bypass = isSafety || guaranteed;
            var s = CoreSettings.Current;

            if (!isSafety && Safe(EmiDeskAvatarMuted)) return (false, -1, "emi-desk-mute");
            if (!isSafety && !s.AvatarEnabled) return (false, -1, "companion-off");
            if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule)) return (false, -1, $"already-fired ({rule.Scope})");

            if (!bypass)
            {
                if (DateTime.UtcNow < _safetyHoldUntilUtc) return (false, -1, "safety-active");
                if (Safe(IsWhisperPlaying) && !TextOnlyAttribution.Contains(rule.Trigger)) return (false, -1, "whisper-active");
                if (Safe(IsNarratorPlaying)) return (false, -1, "narrator-active");
                int window = s.BarkChatSuppressionMs;
                if (CompanionBusy(window)) return (false, -1, $"chat-suppressed ({window}ms)");
                bool willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
                if (!willPreempt && Safe(IsSpeaking)) return (false, -1, "speaking");
                if (!GlobalGapExemptTriggers.Contains(rule.Trigger))
                {
                    var since = (DateTime.UtcNow - _globalLastFireUtc).TotalMilliseconds;
                    if (since < GlobalMinGapMs) return (false, -1, $"min-gap ({since:F0}/{GlobalMinGapMs}ms)");
                }
                if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last))
                {
                    var since = (DateTime.UtcNow - last).TotalMilliseconds;
                    if (since < rule.CooldownMs) return (false, -1, $"cooldown ({since:F0}/{rule.CooldownMs}ms)");
                }
                if (rule.Chance < 1.0 && _rng.NextDouble() >= rule.Chance) return (false, -1, $"chance ({rule.Chance:0.##})");
            }

            int idx = 0;
            if (pool.Count > 1)
            {
                if (rule.Repeatable)
                {
                    var used = _usedVariantKeys.TryGetValue(rule.Id, out var set) ? set : null;
                    idx = PickVariant(pool, rule.Id, used);
                    if (idx < 0)
                    {
                        var reseed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (_lastVariantKey.TryGetValue(rule.Id, out var lastKey)) reseed.Add(lastKey);
                        _usedVariantKeys[rule.Id] = reseed;
                        PersistVariantRotation(rule.Id);
                        idx = PickVariant(pool, rule.Id, reseed);
                        if (idx < 0) idx = 0;
                    }
                }
                else idx = _rng.Next(pool.Count);
            }
            return (true, idx, "OK");
        }

        private bool AlreadyFiredOnce(BarkRule rule)
        {
            if (_firedOnceSession.Contains(rule.Id)) return true;
            return rule.Scope != BarkScope.Session && CoreSettings.Current.IsBarkFired(LatchKey(rule));
        }

        private string LatchKey(BarkRule rule)
        {
            if (rule.Scope != BarkScope.Tier) return rule.Id;
            string? tier = null;
            try { tier = TierName?.Invoke(); } catch { }
            return rule.Id + "@" + (tier ?? "None");
        }

        private bool CompanionBusy(int windowMs)
        {
            try { if (IsCompanionBusy?.Invoke(windowMs) == true) return true; } catch { }
            return windowMs > 0 && (DateTime.UtcNow - _lastUserMessageUtc).TotalMilliseconds < windowMs;
        }

        private void CommitFire(BarkRule rule, int variantIndex, List<BarkVariant> pool)
        {
            var now = DateTime.UtcNow;
            _lastFiredUtc[rule.Id] = now;
            _globalLastFireUtc = now;

            // One mouth, in both directions (WPF BarkService.cs:1808). Every non-safety bark this
            // engine fires on its own lands in the arbiter's cooldown ledger; the awareness bark is
            // excluded because the arbiter records that one itself.
            if (!_inAwarenessBark && !DryRun && rule.Class != BarkClass.Safety &&
                Awareness.AwarenessV2Routing.IsActive)
            {
                try { Awareness.AwarenessV2Routing.Arbiter?.RecordExternalLine(Awareness.ReactionSource.Bark); }
                catch (Exception ex) { Log.Debug("BarkEngine: arbiter report failed: {Error}", ex.Message); }
            }
            if (variantIndex >= 0 && variantIndex < pool.Count) RememberSpoken(pool[variantIndex].Audio);
            if (rule.Class == BarkClass.Safety) _safetyHoldUntilUtc = now.AddMilliseconds(SafetyHoldMs);
            if (!rule.Repeatable)
            {
                _firedOnceSession.Add(rule.Id);
                if (rule.Scope != BarkScope.Session && !DryRun) CoreSettings.Current.MarkBarkFired(LatchKey(rule));
            }
            if (variantIndex >= 0 && variantIndex < pool.Count)
            {
                var key = BarkLineId(rule.Id, pool[variantIndex]);
                if (!_usedVariantKeys.TryGetValue(rule.Id, out var set))
                    _usedVariantKeys[rule.Id] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(key);
                _lastVariantKey[rule.Id] = key;
                PersistVariantRotation(rule.Id);
            }
            if (string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase))
            {
                _usedIdleRules.Add(rule.Id);
                _lastIdleRuleId = rule.Id;
                PersistIdleRotation();
            }
        }

        private int PickVariant(List<BarkVariant> pool, string ruleId, HashSet<string>? usedKeys)
        {
            List<int>? unused = null;
            for (int i = 0; i < pool.Count; i++)
                if (usedKeys == null || !usedKeys.Contains(BarkLineId(ruleId, pool[i]))) (unused ??= new()).Add(i);
            if (unused == null || unused.Count == 0) return -1;
            List<int>? fresh = null;
            if (_recentlySpokenSet.Count > 0)
                foreach (var i in unused)
                    if (!_recentlySpokenSet.Contains(pool[i].Audio ?? string.Empty)) (fresh ??= new()).Add(i);
            var pick = fresh ?? unused;
            return pick[_rng.Next(pick.Count)];
        }

        private void RememberSpoken(string? audio)
        {
            if (string.IsNullOrEmpty(audio)) return;
            if (!_recentlySpokenSet.Add(audio)) return;
            _recentlySpoken.Enqueue(audio);
            while (_recentlySpoken.Count > RecentlySpokenMemory) _recentlySpokenSet.Remove(_recentlySpoken.Dequeue());
        }

        private void LoadRotationFromSettings()
        {
            try
            {
                var s = CoreSettings.Current;
                lock (_gate)
                {
                    _usedVariantKeys.Clear();
                    foreach (var kv in s.BarkVariantRotation)
                        _usedVariantKeys[kv.Key] = new HashSet<string>(kv.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                    _usedIdleRules.Clear();
                    foreach (var id in s.BarkIdleRotation) _usedIdleRules.Add(id);
                }
            }
            catch (Exception ex) { Log.Debug("BarkEngine: rotation restore failed: {Error}", ex.Message); }
        }

        private void PersistVariantRotation(string ruleId)
        {
            if (DryRun) return;
            var s = CoreSettings.Current;
            s.BarkVariantRotation[ruleId] = _usedVariantKeys.TryGetValue(ruleId, out var set) ? new List<string>(set) : new List<string>();
            CoreSettings.Save(suppressCloudBackup: true);
        }

        private void PersistIdleRotation()
        {
            if (DryRun) return;
            CoreSettings.Current.BarkIdleRotation = new List<string>(_usedIdleRules);
            CoreSettings.Save(suppressCloudBackup: true);
        }

        // ---------------- speak ----------------

        private void SpeakLine(BarkRule rule, int variantIndex, BarkContext ctx, List<BarkVariant> pool)
        {
            if (variantIndex < 0 || variantIndex >= pool.Count) return;
            var mouth = Speak;
            if (mouth == null) return;
            var variant = pool[variantIndex];
            var line = ApplySubstitutions(variant.Text, ctx);
            if (string.IsNullOrWhiteSpace(line)) return;

            bool muted = CoreSettings.Current.MasterVolume == 0;
            string? mood = string.IsNullOrEmpty(rule.Mood) ? null : rule.Mood;
            if (rule.Class == BarkClass.EasterEgg && muted)
            {
                mouth(new BarkSpeech(line, null, false, mood, SilentEgg: true));
                RaiseBarkSpoken(rule, line);
                return;
            }
            var audio = SafeResolveAudio(variant.Audio);
            bool priority = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
            mouth(new BarkSpeech(line, audio, priority, mood, SilentEgg: false));
            RaiseBarkSpoken(rule, line);
        }

        private void RaiseBarkSpoken(BarkRule rule, string line)
        {
            var h = BarkSpoken;
            if (h == null) return;
            try { h(rule, line); } catch (Exception ex) { Log.Debug("[BARK] BarkSpoken subscriber error: {Error}", ex.Message); }
        }

        /// <summary>WPF ApplySubstitutions: {0} = the focused app ("that" when unknown), {key} = context values.</summary>
        public string ApplySubstitutions(string text, BarkContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.Contains("{0}"))
            {
                string? app = null;
                try { app = FocusedAppName?.Invoke(); } catch { }
                if (string.IsNullOrWhiteSpace(app)) app = "that";
                text = text.Replace("{0}", app);
            }
            foreach (var kvp in ctx.Values)
            {
                var token = "{" + kvp.Key + "}";
                if (text.Contains(token)) text = text.Replace(token, kvp.Value?.ToString() ?? "");
            }
            return text;
        }

        /// <summary>Test seam: load a fixed rule set.</summary>
        internal void UseRules(BarkRuleSet rules) { lock (_gate) _rules = rules; }
    }
}
