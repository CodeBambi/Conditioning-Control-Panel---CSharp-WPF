using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Core.Services.Bark;

/// <summary>
/// Portable bark DECISION engine (BARK-1 slice 1) — the rule matcher + gate + commit pipeline of
/// the WPF reactive companion-dialogue system, ported from the frozen WPF head
/// (Services/Companion/BarkService.cs, 1678 lines; contract: docs/bark-engine-contract.md).
///
/// This slice decides; it does not deliver. Delivery goes through the injected
/// <see cref="IBarkSpeaker"/> seam OUTSIDE the decision lock (WPF calls Speak after DecideLocked
/// the same way, BarkService.cs:783/805). Nothing is wired to real triggers yet — slice 3 maps
/// the Notify*/event surface onto <see cref="Raise"/>; the existing AvaloniaBarkService
/// NotifyChaos*→BarkRequested bare-string path stays the live consumer until then.
///
/// State model mirrors WPF exactly (BarkService.cs:36-104): per-rule cooldown dictionary, global
/// min-gap, session one-shot set, persisted lifetime/tier latches (AppSettings.BarkLifetimeFired),
/// per-rule variant rotation keyed by stable line ids (persisted in AppSettings.BarkVariantRotation),
/// bounded global recently-spoken window, idle pool-wide no-repeat (AppSettings.BarkIdleRotation),
/// safety hold, and chat suppression.
/// </summary>
public sealed class BarkEngine
{
    private readonly object _gate = new();

    private BarkRuleSet _rules = BarkRuleSet.Empty;
    private bool _started;

    // --- reused gate primitives (WPF BarkService.cs:36-47) ---
    private readonly Dictionary<string, DateTime> _lastFiredUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _firedOnceSession = new(StringComparer.OrdinalIgnoreCase);
    // Per-rule variant rotation, keyed by the variant's stable LINE ID (not index) so it survives
    // content edits AND can be persisted across sessions (WPF BarkService.cs:39-43).
    private readonly Dictionary<string, HashSet<string>> _usedVariantKeys = new(StringComparer.OrdinalIgnoreCase);
    // Last variant line id fired per rule — avoids an immediate repeat on pool recycle (WPF :44-46).
    private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _globalLastFireUtc = DateTime.MinValue;

    // Bounded global recency window over just-spoken audio filenames (WPF BarkService.cs:49-58).
    private readonly Queue<string> _recentlySpoken = new();
    private readonly HashSet<string> _recentlySpokenSet = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>How many distinct just-spoken lines to avoid replaying across all rules (WPF :58).</summary>
    private const int RecentlySpokenMemory = 8;

    /// <summary>Hard global cooldown between any two reactive/idle barks (WPF BarkService.cs:68).</summary>
    private const int GlobalMinGapMs = 60000;

    /// <summary>Triggers that skip ONLY the global min-gap floor (functional corrections that keep
    /// their authored cadence; matched against rule.Trigger — WPF BarkService.cs:77-78, :1367).</summary>
    private static readonly HashSet<string> GlobalGapExemptTriggers =
        new(StringComparer.OrdinalIgnoreCase) { "AttentionCheckFail" };

    /// <summary>Barks at/above this priority (or any non-Normal class) preempt (WPF BarkService.cs:81).</summary>
    private const int PriorityBarkThreshold = 100;

    /// <summary>How long a safety bark holds the floor (WPF BarkService.cs:87).</summary>
    private const int SafetyHoldMs = 6000;

    // --- idle chatter: pool-wide no-repeat across all eligible Idle rules (WPF BarkService.cs:98-104) ---
    private readonly HashSet<string> _usedIdleRules = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastIdleRuleId;
    /// <summary>Chance an idle tick prefers an eligible band-gated idle rule (WPF BarkService.cs:104).</summary>
    private const double GatedIdleBias = 0.35;

    private readonly Random _rng;
    private readonly Func<DateTime> _utcNow;
    private DateTime _lastUserMessageUtc = DateTime.MinValue;
    private DateTime _safetyHoldUntilUtc = DateTime.MinValue;

    private readonly ISettingsService? _settings;
    private readonly IBarkSpeaker? _speaker;
    private readonly IBarkLiveFields? _liveFields;
    private readonly IBarkGateSignals? _gateSignals;
    private readonly IModService? _mods;
    private readonly IBarkManifestService? _audioResolver;
    private readonly Func<BarkRuleSet> _ruleLoader;
    private readonly ILogger<BarkEngine>? _logger;

    /// <summary>
    /// When true the matcher evaluates, logs, and ADVANCES in-memory state (CommitFire runs) but
    /// never calls the speaker and never writes persisted latches/rotation (WPF BarkService.cs:113-117,
    /// :835, :1463, :1558, :1569).
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>Loaded rule count (diagnostics/tests).</summary>
    public int RuleCount { get { lock (_gate) return _rules.Count; } }

    /// <summary>Embedded base folder: &lt;appBase&gt;/Resources/sounds/companion_audio (mirrors BarkManifestService).</summary>
    private static string CompanionAudioFolder =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "companion_audio");

    /// <param name="ruleLoader">Rule source override for tests; default loads the merged
    /// embedded+active-mod manifests via <see cref="BarkRuleLoader"/> (WPF Start(), BarkService.cs:135).</param>
    /// <param name="utcNow">Clock override for tests; default <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="rng">Randomness override for tests (chance roll + variant/idle picks).</param>
    public BarkEngine(
        ISettingsService? settings = null,
        IBarkSpeaker? speaker = null,
        IBarkLiveFields? liveFields = null,
        IBarkGateSignals? gateSignals = null,
        IModService? mods = null,
        IBarkManifestService? audioResolver = null,
        Func<BarkRuleSet>? ruleLoader = null,
        ILogger<BarkEngine>? logger = null,
        Func<DateTime>? utcNow = null,
        Random? rng = null)
    {
        _settings = settings;
        _speaker = speaker;
        _liveFields = liveFields;
        _gateSignals = gateSignals;
        _mods = mods;
        _audioResolver = audioResolver;
        _logger = logger;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _rng = rng ?? new Random();
        _ruleLoader = ruleLoader ?? (() => BarkRuleLoader.Load(
            CompanionAudioFolder, _mods?.ActiveMod?.InstalledPath, _mods?.ActiveMod?.Id,
            msg => _logger?.LogWarning("{Message}", msg)));
    }

    /// <summary>
    /// Load rules + restore persisted rotation. Ported from WPF Start() (BarkService.cs:123-151)
    /// minus the subscription block and launch-recency capture (slice 3 wires those).
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            // Opt-in log-only mode for validation (WPF BarkService.cs:132-133).
            if (string.Equals(Environment.GetEnvironmentVariable("CCP_BARK_DRYRUN"), "1", StringComparison.Ordinal))
                DryRun = true;

            var loaded = _ruleLoader();
            lock (_gate)
            {
                _rules = loaded;
                LoadRotationFromSettings(); // WPF BarkService.cs:136
            }

            _logger?.LogInformation(
                "BarkEngine started — {Count} rules, {Triggers} trigger keys, dry-run={DryRun}",
                loaded.Count, loaded.Triggers.Count(), DryRun);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BarkEngine failed to start");
        }
    }

    /// <summary>
    /// Stamp the last-user-chat-message time consumed by the chat-suppression gate. Slice 3 wires
    /// this to the UserMessageSent progression event (WPF WireSubscriptions; _lastUserMessageUtc
    /// read at BarkService.cs:1442).
    /// </summary>
    public void NotifyUserMessage() => _lastUserMessageUtc = _utcNow();

    /// <summary>
    /// Full raise pipeline, ported from WPF Raise (BarkService.cs:773-817): trigger lookup →
    /// per-fire context fill → first conditions-passing rule (priority-descending) under the lock →
    /// gate/commit decision → speak via seam OUTSIDE the lock.
    /// </summary>
    /// <param name="guaranteed">Bypasses the timing/floor gates (cooldown / min-gap / chat-suppression /
    /// safety-hold / chance) but NOT the one-shot dedup (WPF BarkService.cs:767-771, :1325-1332).</param>
    /// <returns>True if a bark was actually spoken (rule matched + gate passed + not dry-run).</returns>
    public bool Raise(string trigger, Action<BarkContext>? fill = null, bool guaranteed = false)
    {
        try
        {
            BarkRuleSet rules;
            lock (_gate) rules = _rules;

            var forTrigger = rules.ForTrigger(trigger); // priority-descending (WPF :777)
            if (forTrigger.Count == 0) return false;

            var ctx = new BarkContext(trigger);
            fill?.Invoke(ctx); // WPF :781

            // Decide under the lock; render after releasing it (WPF :783-803).
            BarkRule? toSpeak = null;
            int variantIndex = -1;
            List<BarkVariant>? pool = null;

            lock (_gate)
            {
                BarkRule? winner = null;
                foreach (var rule in forTrigger) // already priority-descending
                {
                    if (ConditionsPass(rule, ctx)) { winner = rule; break; } // WPF :791-794
                }

                if (winner == null)
                {
                    _logger?.LogDebug("[BARK] trigger={Trigger} no rule matched conditions", trigger);
                    return false;
                }

                (toSpeak, variantIndex, pool) = DecideLocked(trigger, winner, guaranteed); // WPF :802
            }

            if (toSpeak != null && pool != null)
            {
                SpeakViaSeam(toSpeak, variantIndex, ctx, pool); // WPF :805-807 (Speak)
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "BarkEngine: Raise('{Trigger}') failed", trigger);
            return false;
        }
    }

    /// <summary>
    /// Shared gate→commit decision for an already-chosen winner. Caller MUST hold <see cref="_gate"/>.
    /// Ported from WPF DecideLocked (BarkService.cs:825-836). CommitFire runs even in dry-run
    /// (state advances, speak suppressed — WPF :834-835).
    /// </summary>
    private (BarkRule? toSpeak, int variantIndex, List<BarkVariant>? pool) DecideLocked(
        string trigger, BarkRule winner, bool guaranteed)
    {
        var resolved = ResolvePool(winner);
        var decision = EvaluateGate(winner, resolved, guaranteed);
        LogDecision(trigger, winner, decision, resolved);

        if (!decision.WouldFire) return (null, -1, null);

        CommitFire(winner, decision.VariantIndex, resolved);
        return DryRun ? (null, -1, null) : (winner, decision.VariantIndex, resolved);
    }

    /// <summary>
    /// Raise an idle ("Idle") bark with pool-wide no-repeat across ALL eligible idle rules.
    /// Ported from WPF DispatchIdle (BarkService.cs:847-870). Skipped while muted or while the
    /// avatar is already speaking; the normal gate still applies on top.
    /// </summary>
    public void DispatchIdle()
    {
        try
        {
            // Pause idle while muted or already speaking — real barks always win (WPF :852-853).
            if ((_settings?.Current?.MasterVolume ?? 0) == 0) return;
            if (_gateSignals?.IsAvatarSpeaking ?? false) return;

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
            if (toSpeak != null && pool != null) SpeakViaSeam(toSpeak, variantIndex, ctx, pool);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkEngine: DispatchIdle failed"); }
    }

    /// <summary>
    /// Pick an idle rule with pool-wide no-repeat + recycle (reseeding with the last one) and the
    /// gated-idle bias. Ported from WPF PickIdleRuleLocked (BarkService.cs:879-895). Caller holds the lock.
    /// </summary>
    private BarkRule? PickIdleRuleLocked(List<BarkRule> eligible)
    {
        var unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
        if (unused.Count == 0)
        {
            _usedIdleRules.Clear();
            if (_lastIdleRuleId != null) _usedIdleRules.Add(_lastIdleRuleId);
            PersistIdleRotation(); // exhausted → recycle; mirror the reset so it persists (WPF :886)
            unused = eligible.Where(r => !_usedIdleRules.Contains(r.Id)).ToList();
            if (unused.Count == 0) unused = eligible; // single eligible rule — unavoidable repeat (WPF :888)
        }

        var gated = unused.Where(r => r.Conditions != null && r.Conditions.Count > 0).ToList();
        if (gated.Count > 0 && _rng.NextDouble() < GatedIdleBias)
            return gated[_rng.Next(gated.Count)];
        return unused[_rng.Next(unused.Count)];
    }

    /// <summary>
    /// Reload rules from disk (e.g. after a mod switch). Clears session one-shots + per-rule
    /// cooldowns (rule ids are SHARED across mods); PRESERVES variant/idle rotation and the
    /// persisted lifetime/tier latches. Ported from WPF ReloadRules (BarkService.cs:406-423).
    /// </summary>
    public void ReloadRules()
    {
        try
        {
            var fresh = _ruleLoader();
            lock (_gate)
            {
                _rules = fresh;
                _firedOnceSession.Clear();
                _lastFiredUtc.Clear();
                // Variant/idle rotation intentionally NOT cleared (WPF :416-418).
            }
            _logger?.LogInformation("BarkEngine: reloaded {Count} rules (rotation preserved)", fresh.Count);
        }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkEngine: rule reload failed"); }
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// True if at least one rule is registered for <paramref name="trigger"/> (WPF ForTrigger non-empty,
    /// BarkService.cs:777). Hosts use this to route a trigger through the rule engine when a rule
    /// exists, or fall back to a random-phrase bark otherwise, so an un-ruled trigger never goes
    /// silent (BARK-1 slice 3 regression guard). Safe before <see cref="Start"/>: the default empty
    /// rule set yields false. Takes the gate lock for a consistent snapshot of <see cref="_rules"/>.
    /// </summary>
    public bool HasTrigger(string trigger)
    {
        BarkRuleSet rules;
        lock (_gate) rules = _rules;
        return rules.ForTrigger(trigger).Count > 0;
    }

    // Condition matching (WPF BarkService.cs:1038-1170)
    // ------------------------------------------------------------------

    private bool ConditionsPass(BarkRule rule, BarkContext ctx)
    {
        if (rule.Conditions == null || rule.Conditions.Count == 0) return true; // WPF :1040
        foreach (var kvp in rule.Conditions)
        {
            if (!ConditionPass(kvp.Key, kvp.Value, ctx)) return false;
        }
        return true;
    }

    private bool ConditionPass(string key, object? expected, BarkContext ctx)
    {
        // Operator-suffix parse: _gte/_lte/_gt/_lt/_eq; bare key = eq (WPF :1050-1060).
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
        if (actual == null) return false; // WPF :1063

        if (op == "eq")
        {
            // bool/string equality first, numeric fallback (WPF :1065-1072).
            if (expected is bool eb && TryBool(actual, out var ab)) return ab == eb;
            if (TryDouble(expected, out var en) && TryDouble(actual, out var an))
                return Math.Abs(an - en) < 0.0001;
            return string.Equals(actual.ToString(), expected?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        if (!TryDouble(actual, out var a) || !TryDouble(expected, out var e)) return false; // WPF :1074
        return op switch
        {
            "gte" => a >= e,
            "lte" => a <= e,
            "gt" => a > e,
            "lt" => a < e,
            _ => false
        };
    }

    /// <summary>Resolve a condition field: well-known live reads first (they SHADOW ctx), else the
    /// per-fire context (WPF ResolveField, BarkService.cs:1086-1139).</summary>
    private object? ResolveField(string field, BarkContext ctx)
    {
        var lower = field.ToLowerInvariant();
        if (_liveFields != null && _liveFields.TryResolve(lower, out var live))
            return live;
        return ctx.Values.TryGetValue(field, out var v) ? v : null; // WPF :1138
    }

    private static bool TryDouble(object? raw, out double value)
    {
        // WPF BarkService.cs:1142-1157.
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
        // WPF BarkService.cs:1159-1170.
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

    // ------------------------------------------------------------------
    // Pool resolution + line ids (WPF BarkService.cs:1172-1212)
    // ------------------------------------------------------------------

    private List<BarkVariant> ResolvePool(BarkRule rule)
    {
        // Inline pool: drop lines the user disabled/hid in the Phrase Manager (WPF :1172-1187).
        if (rule.VariantPool != null && rule.VariantPool.Count > 0)
            return rule.VariantPool.Where(v => IsBarkLineEnabled(rule.Id, v)).ToList();
        if (!string.IsNullOrWhiteSpace(rule.PoolRef))
        {
            var phrases = _mods?.GetPhrases(rule.PoolRef!);
            if (phrases != null && phrases.Length > 0)
                return phrases.Select(p => new BarkVariant(p)).ToList(); // pool-ref lines are text-only
        }
        return new List<BarkVariant>();
    }

    /// <summary>
    /// Stable identifier for a single bark line — keyed off the audio filename, text-slug fallback.
    /// MUST stay byte-identical to WPF BarkLineId (BarkService.cs:1197-1203): these ids persist in
    /// AppSettings.BarkVariantRotation / DisabledPhraseIds and must match across heads.
    /// </summary>
    public static string BarkLineId(string ruleId, BarkVariant v)
    {
        var key = string.IsNullOrWhiteSpace(v.Audio)
            ? "t_" + Slugify(v.Text)
            : Path.GetFileNameWithoutExtension(v.Audio);
        return "Bark:" + ruleId + ":" + key;
    }

    /// <summary>
    /// Canonical slug for a spoken line. Byte-identical port of WPF
    /// CompanionPhraseService.Slugify (CompanionPhraseService.cs:84-95): drop {tokens}, lowercase,
    /// collapse every non-alphanumeric run to a single space, trim.
    /// </summary>
    public static string Slugify(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var noTokens = System.Text.RegularExpressions.Regex.Replace(text, @"\{[^}]*\}", " ");
        var sb = new System.Text.StringBuilder(noTokens.Length);
        foreach (var ch in noTokens.ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            else sb.Append(' ');
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>True unless the user disabled or hid this bark line in the Phrase Manager
    /// (shares DisabledPhraseIds/RemovedPhraseIds via the "Bark:" prefix — WPF :1206-1212).</summary>
    private bool IsBarkLineEnabled(string ruleId, BarkVariant v)
    {
        var s = _settings?.Current;
        if (s == null) return true;
        var id = BarkLineId(ruleId, v);
        return !s.DisabledPhraseIds.Contains(id) && !s.RemovedPhraseIds.Contains(id);
    }

    // ------------------------------------------------------------------
    // Gate (WPF BarkService.cs:1309-1443)
    // ------------------------------------------------------------------

    private readonly struct GateDecision
    {
        public bool WouldFire { get; init; }
        public int VariantIndex { get; init; }
        public string Reason { get; init; }
    }

    private GateDecision EvaluateGate(BarkRule rule, List<BarkVariant> pool, bool guaranteed)
    {
        // Gate order is the WPF contract (EvaluateGate, BarkService.cs:1316-1423) — do not reorder.
        if (pool.Count == 0)
            return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "empty-pool" };

        // Safety and guaranteed reactions bypass the timing/floor gates entirely (WPF :1322-1323).
        bool isSafety = rule.Class == BarkClass.Safety;
        bool bypass = isSafety || guaranteed;

        // One-shot dedup — NOT bypassed by `guaranteed`, only Safety is exempt (WPF :1325-1332).
        if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule))
            return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"already-fired ({rule.Scope})" };

        if (!bypass)
        {
            // A safety bark holds the floor (WPF :1337).
            if (_utcNow() < _safetyHoldUntilUtc)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "safety-active" };

            // Whisper audio holds the floor (WPF :1342).
            if (_gateSignals?.IsWhisperAudioPlaying == true)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "whisper-active" };

            // Chaos narrator holds the floor (WPF :1347).
            if (_gateSignals?.IsNarratorPlaying == true)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "narrator-active" };

            // Chat-suppression: don't talk over an active conversation (WPF :1350-1353).
            int window = _settings?.Current?.BarkChatSuppressionMs ?? 10000;
            if (CompanionBusy(window))
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"chat-suppressed ({window}ms)" };

            // Anti-stale: drop non-preempting barks while the avatar is mid-bubble (WPF :1355-1362).
            bool willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;
            if (!willPreempt && (_gateSignals?.IsAvatarSpeaking ?? false))
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = "speaking" };

            // Global min-gap — checked against rule.Trigger, not the raise argument (WPF :1364-1372).
            if (!GlobalGapExemptTriggers.Contains(rule.Trigger))
            {
                var sinceGlobal = (_utcNow() - _globalLastFireUtc).TotalMilliseconds;
                if (sinceGlobal < GlobalMinGapMs)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"min-gap ({sinceGlobal:F0}/{GlobalMinGapMs}ms)" };
            }

            // Per-bark cooldown (WPF :1374-1380).
            if (rule.CooldownMs > 0 && _lastFiredUtc.TryGetValue(rule.Id, out var last))
            {
                var sinceRule = (_utcNow() - last).TotalMilliseconds;
                if (sinceRule < rule.CooldownMs)
                    return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"cooldown ({sinceRule:F0}/{rule.CooldownMs}ms)" };
            }

            // Probability roll — last gate before variant selection, INSIDE the !bypass block
            // (Safety/guaranteed skip the roll too — WPF :1382-1386).
            if (rule.Chance < 1.0 && _rng.NextDouble() >= rule.Chance)
                return new GateDecision { WouldFire = false, VariantIndex = -1, Reason = $"chance ({rule.Chance:0.##})" };
        }

        // Variant selection (WPF :1388-1420):
        //  • repeatable, pool>1 → random unused variant; exhausted → recycle, reseeding with the
        //    last-fired line so it can't immediately repeat;
        //  • one-shot, pool>1 → random line from the whole pool.
        int idx = 0;
        if (pool.Count > 1)
        {
            if (rule.Repeatable)
            {
                var usedKeys = _usedVariantKeys.TryGetValue(rule.Id, out var set) ? set : null;
                idx = PickVariant(pool, rule.Id, usedKeys);
                if (idx < 0)
                {
                    var reseed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (_lastVariantKey.TryGetValue(rule.Id, out var lastKey))
                        reseed.Add(lastKey);
                    _usedVariantKeys[rule.Id] = reseed;
                    PersistVariantRotation(rule.Id); // WPF :1411
                    idx = PickVariant(pool, rule.Id, reseed);
                    if (idx < 0) idx = 0; // safety (WPF :1413)
                }
            }
            else
            {
                idx = _rng.Next(pool.Count); // one-shot: random line from the pool (WPF :1418)
            }
        }

        return new GateDecision { WouldFire = true, VariantIndex = idx, Reason = "OK" };
    }

    private bool AlreadyFiredOnce(BarkRule rule)
    {
        // WPF BarkService.cs:1425-1431.
        if (_firedOnceSession.Contains(rule.Id)) return true;
        if (rule.Scope != BarkScope.Session)
            return _settings?.Current?.IsBarkFired(LatchKey(rule)) == true;
        return false;
    }

    /// <summary>Persisted-latch key: lifetime = id; tier = "id@Tier" so a tier change re-arms it
    /// (WPF BarkService.cs:1433-1437). The port reads the cached settings tier int (same
    /// None/Level1/Level2 names as WPF's App.Patreon.CurrentTier.ToString()).</summary>
    private string LatchKey(BarkRule rule) =>
        rule.Scope == BarkScope.Tier
            ? rule.Id + "@" + ((PatreonTier)(_settings?.Current?.PatreonTier ?? 0)).ToString()
            : rule.Id;

    private bool CompanionBusy(int windowMs)
    {
        // WPF BarkService.cs:1439-1443.
        if (_gateSignals?.IsCompanionBusy(windowMs) ?? false) return true;
        return windowMs > 0 && (_utcNow() - _lastUserMessageUtc).TotalMilliseconds < windowMs;
    }

    // ------------------------------------------------------------------
    // Commit + rotation (WPF BarkService.cs:1445-1530)
    // ------------------------------------------------------------------

    private void CommitFire(BarkRule rule, int variantIndex, List<BarkVariant> pool)
    {
        // WPF BarkService.cs:1445-1488. Runs even in dry-run; only the PERSISTED writes are
        // dry-run-suppressed (MarkBarkFired :1463, PersistVariantRotation :1558, PersistIdleRotation :1569).
        var now = _utcNow();
        _lastFiredUtc[rule.Id] = now;
        _globalLastFireUtc = now;

        // Feed the global recency window so the NEXT pick (any rule) avoids this exact line (WPF :1452).
        if (variantIndex >= 0 && variantIndex < pool.Count)
            RememberSpoken(pool[variantIndex].Audio);

        // A fired safety bark holds the floor (WPF :1456-1457).
        if (rule.Class == BarkClass.Safety)
            _safetyHoldUntilUtc = now.AddMilliseconds(SafetyHoldMs);

        if (!rule.Repeatable)
        {
            _firedOnceSession.Add(rule.Id);
            // Persist lifetime/tier latches — never in dry-run (WPF :1462-1464).
            if (rule.Scope != BarkScope.Session && !DryRun)
                _settings?.Current?.MarkBarkFired(LatchKey(rule));
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
            _lastVariantKey[rule.Id] = key; // no-immediate-repeat on pool recycle (WPF :1476)
            PersistVariantRotation(rule.Id);
        }

        // Pool-wide no-repeat for the idle class (WPF :1480-1487).
        if (string.Equals(rule.Trigger, "Idle", StringComparison.OrdinalIgnoreCase))
        {
            _usedIdleRules.Add(rule.Id);
            _lastIdleRuleId = rule.Id;
            PersistIdleRotation();
        }
    }

    /// <summary>
    /// Choose a variant index for a repeatable pool: hard per-rule no-repeat (line id not in
    /// usedKeys) + soft global recency preference. Returns -1 when every line is used (caller
    /// recycles). Ported from WPF PickVariant (BarkService.cs:1502-1520).
    /// </summary>
    private int PickVariant(List<BarkVariant> pool, string ruleId, HashSet<string>? usedKeys)
    {
        List<int>? unused = null;
        for (int i = 0; i < pool.Count; i++)
            if (usedKeys == null || !usedKeys.Contains(BarkLineId(ruleId, pool[i])))
                (unused ??= new List<int>()).Add(i);
        if (unused == null || unused.Count == 0) return -1;

        // Soft global recency: prefer lines not spoken in the last RecentlySpokenMemory barks (WPF :1511-1516).
        List<int>? fresh = null;
        if (_recentlySpokenSet.Count > 0)
            foreach (var i in unused)
                if (!_recentlySpokenSet.Contains(pool[i].Audio ?? string.Empty))
                    (fresh ??= new List<int>()).Add(i);

        var pick = fresh ?? unused;
        return pick[_rng.Next(pick.Count)];
    }

    /// <summary>Record a just-spoken line's audio in the bounded global recency window (WPF :1523-1530).</summary>
    private void RememberSpoken(string? audio)
    {
        if (string.IsNullOrEmpty(audio)) return;
        if (!_recentlySpokenSet.Add(audio)) return; // already in-window
        _recentlySpoken.Enqueue(audio);
        while (_recentlySpoken.Count > RecentlySpokenMemory)
            _recentlySpokenSet.Remove(_recentlySpoken.Dequeue());
    }

    // ------------------------------------------------------------------
    // Rotation persistence (WPF BarkService.cs:1533-1574) — non-sensitive AppSettings lists only.
    // ------------------------------------------------------------------

    /// <summary>Restore persisted variant/idle rotation on startup (WPF LoadRotationFromSettings :1538-1553).</summary>
    private void LoadRotationFromSettings()
    {
        var s = _settings?.Current;
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
        catch (Exception ex) { _logger?.LogDebug("BarkEngine: rotation restore failed: {Error}", ex.Message); }
    }

    /// <summary>Mirror one rule's variant rotation into settings + debounced save (WPF :1556-1564).</summary>
    private void PersistVariantRotation(string ruleId)
    {
        if (DryRun) return; // dry-run must not mutate settings (WPF :1558)
        var s = _settings?.Current;
        if (s == null) return;
        s.BarkVariantRotation[ruleId] = _usedVariantKeys.TryGetValue(ruleId, out var set)
            ? new List<string>(set) : new List<string>();
        _settings?.Save(suppressCloudBackup: true); // local-only (WPF :1563)
    }

    /// <summary>Mirror the idle rotation into settings + debounced save (WPF :1567-1574).</summary>
    private void PersistIdleRotation()
    {
        if (DryRun) return;
        var s = _settings?.Current;
        if (s == null) return;
        s.BarkIdleRotation = new List<string>(_usedIdleRules);
        _settings?.Save(suppressCloudBackup: true);
    }

    // ------------------------------------------------------------------
    // Speak dispatch via seam (delivery itself is slice 2)
    // ------------------------------------------------------------------

    /// <summary>
    /// Compute the seam arguments the WPF Speak path computes (line substitution :1589, audio
    /// resolution :1603, priority routing flag :1619) and hand off to <see cref="IBarkSpeaker"/>.
    /// Mute egg, self-echo guard, and DtRH telemetry are delivery concerns → slice 2.
    /// </summary>
    private void SpeakViaSeam(BarkRule rule, int variantIndex, BarkContext ctx, List<BarkVariant> pool)
    {
        if (variantIndex < 0 || variantIndex >= pool.Count) return; // WPF :1580

        var variant = pool[variantIndex];
        var line = ApplySubstitutions(variant.Text, ctx); // WPF :1589
        if (string.IsNullOrWhiteSpace(line)) return;      // WPF :1590 (Raise still returned true)

        // 3-tier per-mod voiceline resolution via the existing manifest service (WPF ResolveBarkAudio :1286-1307).
        var audioPath = _audioResolver?.ResolveModAudio(variant.Audio);

        // Route by class/priority (WPF :1619).
        bool priority = rule.Class != BarkClass.Normal || rule.Priority >= PriorityBarkThreshold;

        // Expose the matched rule's class to the delivery seam so the slice-2 mute-egg special-case
        // (WPF BarkService.cs:1595) can detect EasterEgg without widening IBarkSpeaker.Speak. Stamped
        // AFTER ApplySubstitutions ran above so it can never leak as a {key} token.
        ctx.Set(BarkContext.RuleClassKey, rule.Class);

        try { _speaker?.Speak(line, audioPath, priority, rule.Mood, ctx); }
        catch (Exception ex) { _logger?.LogWarning(ex, "BarkEngine: speaker seam failed for rule {Rule}", rule.Id); }
    }

    /// <summary>
    /// Substitute {key} tokens from the per-fire context (WPF ApplySubstitutions :1643-1648).
    /// The {0} focused-app substitution (WPF :1635-1641) needs the window-awareness read and moves
    /// with the delivery slice; until then a {0} passes through untouched.
    /// </summary>
    private static string ApplySubstitutions(string text, BarkContext ctx)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var kvp in ctx.Values)
        {
            var token = "{" + kvp.Key + "}";
            if (text.Contains(token))
                text = text.Replace(token, kvp.Value?.ToString() ?? "");
        }
        return text;
    }

    // ------------------------------------------------------------------
    // Decision logging (WPF BarkService.cs:1652-1676)
    // ------------------------------------------------------------------

    private void LogDecision(string trigger, BarkRule rule, GateDecision decision, List<BarkVariant> pool)
    {
        if (_logger == null) return;
        string preview = decision.VariantIndex >= 0 && decision.VariantIndex < pool.Count
            ? Truncate(pool[decision.VariantIndex].Text, 48)
            : "(n/a)";
        string tag = DryRun ? "[BARK dry-run]" : "[BARK]";

        if (decision.WouldFire)
        {
            string verb = DryRun ? "WOULD FIRE" : "FIRE";
            _logger.LogInformation(
                "{Tag} {Verb} trigger={Trigger} rule={Rule} class={Class} mood={Mood} priority={Priority} variant#={Idx} line=\"{Preview}\"",
                tag, verb, trigger, rule.Id, rule.Class, rule.Mood, rule.Priority, decision.VariantIndex, preview);
        }
        else
        {
            _logger.LogInformation(
                "{Tag} blocked trigger={Trigger} rule={Rule} class={Class} priority={Priority} reason={Reason}",
                tag, trigger, rule.Id, rule.Class, rule.Priority, decision.Reason);
        }
    }

    private static string Truncate(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
}
