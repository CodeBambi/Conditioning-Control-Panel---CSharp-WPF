using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Companion;

/// <summary>Why a gate-passed bark surfaced without voice (typed — mute text-only is a SURFACED mode, never silent).</summary>
public enum BarkSuppression
{
    /// <summary>The Muted flag is set (WPF _isMuted — audio gated, bubble always renders, Speech.cs:483-505).</summary>
    Muted,

    /// <summary>MasterVolume == 0 (WPF every voice path early-returns, the text still shows, Speech.cs:2188-2189).</summary>
    VolumeZero,

    /// <summary>The variant carries no audio file (WPF substitutes a giggle sfx — no greenfield asset; consult binding 4b: never collapsed with a resolver miss).</summary>
    NoAudioAsset,

    /// <summary>The variant names an audio file the resolver could not find (typed, logged).</summary>
    AudioResolveFailed,

    /// <summary>Arbitration reported Unavailable/Failed at hand-off (WPF PlaySpokenAudio init failure → log + text remains, Speech.cs:1603-1607).</summary>
    AudioUnavailable,
}

/// <summary>
/// The bark payload — text, audio path, and emotion assembled as ONE immutable unit
/// (never a torn payload; WPF queue tuple (text, source, emotionLineId, mood),
/// Speech.cs:180,:253-255). EmotionLineId = the audio filename stem driving the portrait
/// emote downstream (:462-467); Mood = the rule's free-form manifest string, passed through
/// untouched. IsAi is always false for barks (WPF Speak aiGenerated:false) — the field
/// exists so the pacing formula's AI bonus arms when a chat row surfaces AI lines.
/// </summary>
public sealed record BarkPayload(
    string Text,
    string? AudioPath,
    string? EmotionLineId,
    string? Mood,
    string LineId,
    string RuleId,
    int Priority,
    BarkClass Class,
    bool IsAi);

/// <summary>Typed outcome of one <see cref="BarkPipeline.Raise"/> — nothing is silently dropped.</summary>
public abstract record BarkOutcome
{
    private BarkOutcome() { }

    /// <summary>Gate passed, payload surfaced, voice handed to arbitration (started or queued behind the pacing debt).</summary>
    public sealed record Surfaced(BarkPayload Payload, bool Priority, int QueueDepth) : BarkOutcome;

    /// <summary>Gate passed, payload surfaced TEXT-ONLY (typed reason — the mute/degrade mode is surfaced, never silent).</summary>
    public sealed record SurfacedTextOnly(BarkPayload Payload, BarkSuppression Reason) : BarkOutcome;

    /// <summary>A rule matched but the gate blocked it (typed reason, logged — WPF EvaluateGate reason strings).</summary>
    public sealed record Gated(string RuleId, string Reason) : BarkOutcome;

    /// <summary>No rule for the trigger, or no condition-passing rule.</summary>
    public sealed record NoRule(string Trigger, string Detail) : BarkOutcome;
}

/// <summary>Resolves a variant's audio filename to a playable file path (null = miss → typed AudioResolveFailed).</summary>
public interface IBarkAudioResolver
{
    /// <summary>The full path for <paramref name="audioFileName"/>, or null when absent.</summary>
    string? Resolve(string audioFileName);
}

/// <summary>Filesystem resolver over one sounds root (missing file → null, never throws).</summary>
public sealed class DirectoryBarkAudioResolver(string root) : IBarkAudioResolver
{
    /// <inheritdoc/>
    public string? Resolve(string audioFileName)
    {
        var path = Path.Combine(root, audioFileName);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>Tuning surface for <see cref="BarkPipeline"/> (every default WPF-cited).</summary>
public sealed class BarkPipelineOptions
{
    /// <summary>Global minimum gap between fires (WPF GlobalMinGapMs 60000, BarkService.cs:68).</summary>
    public TimeSpan GlobalMinGap { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Floor hold after a Safety-class fire (WPF SafetyHoldMs 6000, :87).</summary>
    public TimeSpan SafetyHold { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>Priority preemption threshold (WPF PriorityBarkThreshold 100, :81).</summary>
    public int PriorityThreshold { get; init; } = 100;

    /// <summary>Global recency window — soft preference against the last N spoken audios (WPF RecentlySpokenMemory 8, :58).</summary>
    public int RecencyMemory { get; init; } = 8;

    /// <summary>Triggers exempt from the global min-gap (WPF GlobalGapExemptTriggers, :78).</summary>
    public IReadOnlySet<string> GlobalMinGapExemptTriggers { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AttentionCheckFail" };

    /// <summary>Bark voice gain scale on top of Pow(master/100, 1.5) (WPF bark path scale 0.85, Speech.cs:2188 region).</summary>
    public double BarkVolumeScale { get; init; } = 0.85;

    // Pacing constants (WPF Speech.cs:112-115 — delay computed from the PREVIOUS line's properties, :148-168).
    /// <summary>Minimum gap between voice lines (WPF MinSpeechDelaySeconds 2.0).</summary>
    public double MinSpeechDelaySeconds { get; init; } = 2.0;

    /// <summary>Extra gap after an AI-sourced line (WPF AiSpeechBonusSeconds 5.0).</summary>
    public double AiSpeechBonusSeconds { get; init; } = 5.0;

    /// <summary>Length beyond which the per-char bonus applies (WPF LongTextThreshold 100).</summary>
    public int LongTextThreshold { get; init; } = 100;

    /// <summary>Per-char delay over the threshold (WPF PerCharDelaySeconds 0.02).</summary>
    public double PerCharDelaySeconds { get; init; } = 0.02;

    /// <summary>Injectable clock (gate timing: cooldowns, min-gap, safety-hold). Real = system; tests = manual.</summary>
    public ISoundClock Clock { get; init; } = new SystemSoundClock();

    /// <summary>Injectable uniform [0,1) source (chance rolls + variant selection). Default Random.Shared.</summary>
    public Func<double> Rng { get; init; } = static () => Random.Shared.NextDouble();

    /// <summary>
    /// Chat-busy signal for the chat-suppression gate (WPF CompanionBusy, :1438-1442). Null =
    /// not busy — NO chat UI exists in greenfield; the mechanism is present and the chat row
    /// wires the signal (honest seam, never a permanently-false gate body).
    /// </summary>
    public Func<bool>? ChatBusy { get; init; }

    /// <summary>
    /// Live condition fields (WPF ResolveField, :1099-1137 — live reads take precedence over
    /// per-fire fills). The pipeline itself supplies master_volume and mute; this seam covers
    /// the rest (focused_app for the {0} substitution, clicks_60s, …) as consumers land.
    /// </summary>
    public Func<string, object?>? LiveFields { get; init; }
}

/// <summary>
/// The bark content pipeline (SP-032 slice q2) — trigger → first-condition-passing rule →
/// WPF-order gate → variant selection → ONE payload → q1's arbitration. CONSUMES
/// Audio/SoundArbitration.cs (the q1/q2 boundary: per-item pacing is where TEXT-derived
/// timing enters; freshness stays caller-supplied — q2's WPF-cited policy is NO ms-age
/// expiry + gate-level anti-stale, SP-029 record).
///
/// WPF parity anchors (SP-032 record Step 1): gate order + bypass matrix
/// (BarkService.cs:1305-1422 — the one-shot latch is bypassed by Safety ONLY, never by
/// guaranteed; gates 4a–4h skip when bypass = isSafety || guaranteed); priority routing
/// (:1624, threshold 100); variant rotation/recycle/recency-8 (:1403-1534); mute text-only
/// (Speech.cs:483-505,:2188-2189 + egg :1590-1598); disabled-phrase filtering at every
/// selection (:1172-1180); pacing from the PREVIOUS line (Speech.cs:148-168).
///
/// Omitted gates (recorded, never invented): narrator-active (no narrator exists);
/// self-echo mute (keyword-echo concern, keyword row). Chat-suppressed rides the
/// <see cref="BarkPipelineOptions.ChatBusy"/> seam. Known tolerated race (consult binding
/// 2): voice activating between the anti-stale check and QueueVoice queues an ordinary bark
/// behind the speaking voice — WPF tolerates the identical race (Speech.cs:271-288).
/// </summary>
public sealed class BarkPipeline
{
    private readonly object _gate = new();
    private readonly SoundArbitration _arbitration;
    private readonly PersistenceStore<CompanionStateDocument> _store;
    private readonly IBarkAudioResolver _resolver;
    private readonly BarkPipelineOptions _options;
    private readonly Action<string> _log;

    private BarkRuleSet _ruleSet;
    private readonly Dictionary<string, DateTimeOffset> _lastFiredUtc = new(StringComparer.Ordinal);
    private readonly HashSet<string> _firedOnceSession = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _usedVariants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastVariantKey = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentlySpoken = new();
    private DateTimeOffset _globalLastFireUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _safetyHoldUntilUtc = DateTimeOffset.MinValue;
    private (int Length, bool IsAi)? _lastHandedToVoice;

    public BarkPipeline(
        SoundArbitration arbitration,
        PersistenceStore<CompanionStateDocument> store,
        IBarkAudioResolver resolver,
        IEnumerable<BarkRule> rules,
        BarkPipelineOptions? options,
        Action<string> log)
    {
        _arbitration = arbitration;
        _store = store;
        _resolver = resolver;
        _options = options ?? new BarkPipelineOptions();
        _log = log;
        _ruleSet = new BarkRuleSet(rules);
        // Variant rotation restores from the persisted document (WPF LoadRotationFromSettings, :1538-1553).
        foreach (var (ruleId, used) in _store.Current.VariantRotation)
        {
            _usedVariants[ruleId] = new HashSet<string>(used, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The payload surface — fires ONCE per gate-passed raise with the full unit, voice or
    /// text-only (the greenfield "bubble": mute degrades the audio channel, never the
    /// surface). Raised outside the pipeline gate.
    /// </summary>
    public event Action<BarkPayload>? BarkSurfaced;

    /// <summary>Avatar-level mute (WPF _isMuted): audio gated, text always surfaces.</summary>
    public bool Muted { get; set; }

    /// <summary>Master volume 0..100 (0 = the global text-only mode; WPF MasterVolume==0 surface).</summary>
    public int MasterVolume { get; set; } = 80;

    /// <summary>Number of rules currently loaded (session facts).</summary>
    public int RuleCount { get { lock (_gate) { return _ruleSet.Count; } } }

    /// <summary>Line enabled iff NOT in the persisted disabled set (no caching — WPF ResolvePool, :1172-1180).</summary>
    public bool IsPhraseEnabled(string lineId) => !_store.Current.DisabledPhraseIds.Contains(lineId);

    /// <summary>Disable (mute-but-list) one line id; persisted through the SP-005 store. Round-trips via <see cref="EnablePhrase"/>.</summary>
    public void DisablePhrase(string lineId)
    {
        _store.Mutate(d => d.DisabledPhraseIds.Add(lineId));
        _ = _store.Save();
        _log("bark: phrase disabled (presence only — id withheld from logs by content-free rule)");
    }

    /// <summary>Re-enable one line id; effective at the next selection (WPF re-enable = remove from the set).</summary>
    public void EnablePhrase(string lineId)
    {
        _store.Mutate(d => d.DisabledPhraseIds.Remove(lineId));
        _ = _store.Save();
        _log("bark: phrase re-enabled");
    }

    /// <summary>Flush pending companion-state writes (bound into the DTRH host Closing — consult binding 5).</summary>
    public Task<CcpClient.Desktop.Lifecycle.OperationOutcome> FlushAsync() => _store.SaveImmediate();

    /// <summary>
    /// Reload the rule set (WPF ReloadRules, :406-425): clears session one-shot latches and
    /// cooldowns; PRESERVES variant rotation and lifetime latches.
    /// </summary>
    public void LoadRules(IEnumerable<BarkRule> rules)
    {
        lock (_gate)
        {
            _ruleSet = new BarkRuleSet(rules);
            _firedOnceSession.Clear();
            _lastFiredUtc.Clear();
        }

        _log($"bark: rules reloaded ({_ruleSet.Count} rule(s)); session latches cleared, rotation + lifetime preserved");
    }

    /// <summary>
    /// Raise one trigger (WPF Raise, :773-817 + EvaluateGate :1305-1422 + Speak :1580-1635).
    /// Fills feed condition evaluation and {key} substitutions. guaranteed skips the floor
    /// gates but NEVER the one-shot latch (consult binding 3 — the WPF bypass matrix).
    /// </summary>
    public BarkOutcome Raise(string trigger, IReadOnlyDictionary<string, object?>? fills = null, bool guaranteed = false)
    {
        fills ??= new Dictionary<string, object?>();
        BarkPayload? surfaced = null;
        BarkOutcome outcome;
        lock (_gate)
        {
            var rules = _ruleSet.ForTrigger(trigger);
            if (rules.Count == 0)
            {
                _log($"bark: trigger '{trigger}' — no rule (typed)");
                return new BarkOutcome.NoRule(trigger, "no rule for trigger");
            }

            // First condition-passing rule wins; the gate evaluates that winner ONLY (WPF :790-794).
            BarkRule? winner = null;
            foreach (var rule in rules)
            {
                if (BarkConditions.Pass(rule, LiveField, fills))
                {
                    winner = rule;
                    break;
                }
            }

            if (winner is null)
            {
                _log($"bark: trigger '{trigger}' — no condition-passing rule ({rules.Count} candidate(s))");
                return new BarkOutcome.NoRule(trigger, "no condition-passing rule");
            }

            var gated = EvaluateGate(winner, guaranteed, out var reason);
            if (gated)
            {
                _log($"bark: rule '{winner.Id}' gated ({reason})");
                return new BarkOutcome.Gated(winner.Id, reason!);
            }

            // Enabled-pool filtering at EVERY selection (WPF :1172-1180); pool_ref resolves
            // empty until the phrase-category row (named limit) → the empty-pool gate.
            var pool = winner.VariantPool
                .Where(v => IsPhraseEnabled(BarkText.LineId(winner.Id, v)))
                .ToList();
            if (pool.Count == 0)
            {
                _log($"bark: rule '{winner.Id}' gated (empty-pool)");
                return new BarkOutcome.Gated(winner.Id, "empty-pool");
            }

            var variant = PickVariant(winner, pool);

            // Suppression decision BEFORE audio resolution (typed; consult binding 4b —
            // NoAudioAsset and AudioResolveFailed never collapse; the muted / volume-zero
            // paths skip audio resolution entirely, WPF mute-egg :1590-1598).
            BarkSuppression? suppression = Muted ? BarkSuppression.Muted
                : MasterVolume <= 0 ? BarkSuppression.VolumeZero
                : variant.Audio is null ? BarkSuppression.NoAudioAsset
                : null;
            string? audioPath = null;
            if (suppression is null)
            {
                audioPath = SafeResolve(variant.Audio!);
                if (audioPath is null)
                {
                    suppression = BarkSuppression.AudioResolveFailed;
                }
            }

            var payload = AssemblePayload(winner, variant, fills, audioPath);
            CommitFire(winner, payload);
            surfaced = payload;
            outcome = HandOff(winner, payload, suppression);
        }

        // The surface event fires exactly once per gate-passed raise, voice or text-only.
        if (surfaced is not null)
        {
            BarkSurfaced?.Invoke(surfaced);
        }

        return outcome;
    }

    // ---------- gate (WPF EvaluateGate :1305-1422 — order is load-bearing) ----------

    private bool EvaluateGate(BarkRule rule, bool guaranteed, out string? reason)
    {
        var isSafety = rule.Class == BarkClass.Safety;
        var bypass = isSafety || guaranteed;
        var now = _options.Clock.UtcNow;

        // One-shot latch: bypassed by Safety ONLY, never by guaranteed (consult binding 3, WPF :1326-1332).
        if (!isSafety && !rule.Repeatable && AlreadyFiredOnce(rule))
        {
            reason = $"already-fired ({rule.Scope})";
            return true;
        }

        if (!bypass)
        {
            if (now < _safetyHoldUntilUtc)
            {
                reason = "safety-active";
                return true;
            }

            if (_arbitration.WhisperBusy)
            {
                reason = "whisper-active";
                return true;
            }

            if (_options.ChatBusy?.Invoke() == true)
            {
                reason = "chat-suppressed";
                return true;
            }

            var willPreempt = rule.Class != BarkClass.Normal || rule.Priority >= _options.PriorityThreshold;
            if (!willPreempt && _arbitration.VoiceActive)
            {
                // Gate-level anti-stale = WPF's ONLY freshness mechanism (:1358-1365); typed + logged.
                reason = "speaking";
                return true;
            }

            if (!_options.GlobalMinGapExemptTriggers.Contains(rule.Trigger)
                && now - _globalLastFireUtc < _options.GlobalMinGap)
            {
                reason = "min-gap";
                return true;
            }

            if (_lastFiredUtc.TryGetValue(rule.Id, out var last)
                && now - last < TimeSpan.FromMilliseconds(rule.CooldownMs))
            {
                reason = "cooldown";
                return true;
            }

            // Chance rolls LAST (WPF :1386-1388).
            if (rule.Chance < 1.0 && _options.Rng() >= rule.Chance)
            {
                reason = "chance";
                return true;
            }
        }

        reason = null;
        return false;
    }

    private bool AlreadyFiredOnce(BarkRule rule) => rule.Scope == BarkScope.Session
        ? _firedOnceSession.Contains(rule.Id)
        : _store.Current.LifetimeFired.Contains(rule.Id);

    // ---------- variant selection (WPF :1403-1534) ----------

    private BarkVariant PickVariant(BarkRule rule, IReadOnlyList<BarkVariant> pool)
    {
        // One-shot: uniform random over the whole pool (WPF :1417-1419).
        if (!rule.Repeatable || pool.Count == 1)
        {
            return pool[RngInt(pool.Count)];
        }

        var used = UsedSet(rule.Id);
        var unused = pool.Where(v => !used.Contains(BarkText.LineId(rule.Id, v))).ToList();
        if (unused.Count == 0)
        {
            // Exhaustion → recycle: clear, reseed with the just-played line so it can't
            // immediately repeat (WPF :1403-1415); single-line pools fall back to the pool
            // (preference, never silence).
            used.Clear();
            if (_lastVariantKey.TryGetValue(rule.Id, out var last))
            {
                used.Add(last);
            }

            unused = pool.Where(v => !used.Contains(BarkText.LineId(rule.Id, v))).ToList();
            if (unused.Count == 0)
            {
                unused = [.. pool];
            }

            PersistRotation(rule.Id);
        }

        // Soft global recency (WPF :1501-1534): prefer audio outside the last-N spoken set;
        // all-recent → fall back to unused (never silence).
        var preferred = unused
            .Where(v => v.Audio is null || !_recentlySpoken.Contains(Stem(v.Audio)))
            .ToList();
        var candidates = preferred.Count > 0 ? preferred : unused;
        return candidates[RngInt(candidates.Count)];
    }

    private HashSet<string> UsedSet(string ruleId)
    {
        if (!_usedVariants.TryGetValue(ruleId, out var used))
        {
            used = new HashSet<string>(StringComparer.Ordinal);
            _usedVariants[ruleId] = used;
        }

        return used;
    }

    private int RngInt(int count) => Math.Clamp((int)(_options.Rng() * count), 0, count - 1);

    private static string Stem(string audio) => Path.GetFileNameWithoutExtension(audio);

    // ---------- payload assembly + hand-off ----------

    private BarkPayload AssemblePayload(BarkRule rule, BarkVariant variant, IReadOnlyDictionary<string, object?> fills, string? audioPath)
    {
        var text = BarkText.Substitute(variant.Text, fills, LiveField("focused_app"));
        return new BarkPayload(
            Text: text,
            AudioPath: audioPath,
            EmotionLineId: variant.Audio is { Length: > 0 } a ? Stem(a) : null,
            Mood: rule.Mood,
            LineId: BarkText.LineId(rule.Id, variant),
            RuleId: rule.Id,
            Priority: rule.Priority,
            Class: rule.Class,
            IsAi: false);
    }

    /// <summary>Resolver miss or resolver fault → null (typed AudioResolveFailed at the call site, logged — never throws).</summary>
    private string? SafeResolve(string audio)
    {
        try
        {
            return _resolver.Resolve(audio);
        }
        catch (Exception ex)
        {
            _log($"bark: audio resolver faulted ({ex.GetType().Name}: {ex.Message}) — typed resolve-failed");
            return null;
        }
    }

    /// <summary>
    /// WPF CommitFire (:1444-1487): runs UNCONDITIONALLY after a gate pass — a muted or
    /// volume-zero Safety fire still sets the 6 s safety-hold (consult binding 4a: mute
    /// degrades the audio channel, never the gate state).
    /// </summary>
    private void CommitFire(BarkRule rule, BarkPayload payload)
    {
        var now = _options.Clock.UtcNow;
        _lastFiredUtc[rule.Id] = now;
        _globalLastFireUtc = now;
        if (rule.Class == BarkClass.Safety)
        {
            _safetyHoldUntilUtc = now.Add(_options.SafetyHold);
        }

        if (!rule.Repeatable)
        {
            if (rule.Scope == BarkScope.Session)
            {
                _firedOnceSession.Add(rule.Id);
            }
            else
            {
                _store.Mutate(d => d.LifetimeFired.Add(rule.Id));
                _ = _store.Save();
            }
        }

        var used = UsedSet(rule.Id);
        used.Add(payload.LineId);
        _lastVariantKey[rule.Id] = payload.LineId;
        PersistRotation(rule.Id);

        if (payload.EmotionLineId is { Length: > 0 } stem)
        {
            _recentlySpoken.Enqueue(stem);
            while (_recentlySpoken.Count > _options.RecencyMemory)
            {
                _recentlySpoken.Dequeue();
            }
        }
    }

    private void PersistRotation(string ruleId)
    {
        var snapshot = UsedSet(ruleId).ToList();
        _store.Mutate(d => d.VariantRotation[ruleId] = snapshot);
        _ = _store.Save();
    }

    private BarkOutcome HandOff(BarkRule rule, BarkPayload payload, BarkSuppression? suppression)
    {
        var priority = rule.Class != BarkClass.Normal || rule.Priority >= _options.PriorityThreshold;

        if (suppression is { } textOnly)
        {
            _log($"bark: rule '{rule.Id}' surfaced text-only ({textOnly})");
            return new BarkOutcome.SurfacedTextOnly(payload, textOnly);
        }

        var gain = (float)(Math.Pow(Math.Clamp(MasterVolume, 0, 100) / 100.0, 1.5) * _options.BarkVolumeScale);
        // freshness: null — WPF VERIFIED has no ms-age queue expiry (SP-029 record); the
        // gate-level anti-stale drop above IS the WPF freshness policy (consult binding 2).
        var outcome = priority
            ? _arbitration.PlayVoicePriority(payload.AudioPath!, gain)
            : _arbitration.QueueVoice(payload.AudioPath!, gain, freshness: null, pacing: ComputePacing());

        switch (outcome)
        {
            case SoundOutcome.Started:
                _lastHandedToVoice = (payload.Text.Length, payload.IsAi);
                _log($"bark: rule '{rule.Id}' surfaced (priority={priority}, voice started)");
                return new BarkOutcome.Surfaced(payload, priority, 0);
            case SoundOutcome.Queued queued:
                _lastHandedToVoice = (payload.Text.Length, payload.IsAi);
                _log($"bark: rule '{rule.Id}' surfaced (queued, depth {queued.Depth})");
                return new BarkOutcome.Surfaced(payload, priority, queued.Depth);
            case SoundOutcome.Unavailable unavailable:
                // WPF PlaySpokenAudio init failure → log + the text remains (Speech.cs:1603-1607).
                _log($"bark: rule '{rule.Id}' voice unavailable ({unavailable.Reason}) — text-only surface");
                return new BarkOutcome.SurfacedTextOnly(payload, BarkSuppression.AudioUnavailable);
            case SoundOutcome.Failed failed:
                _log($"bark: rule '{rule.Id}' voice failed ({failed.Error}) — text-only surface");
                return new BarkOutcome.SurfacedTextOnly(payload, BarkSuppression.AudioUnavailable);
            default:
                _log($"bark: rule '{rule.Id}' voice outcome {outcome.GetType().Name} — text-only surface");
                return new BarkOutcome.SurfacedTextOnly(payload, BarkSuppression.AudioUnavailable);
        }
    }

    /// <summary>
    /// The WPF pacing formula as a pure function (Speech.cs:148-168):
    /// `2.0 + (prevIsAi ? 5.0 : 0) + max(0, prevLen-100)*0.02` — computed from the PREVIOUS
    /// line's properties. Static + pure so the WPF cases (floor / AI bonus / per-char) are
    /// directly testable; the AI bonus arms when a chat row hands AI lines through here.
    /// </summary>
    public static double ComputePacingSeconds(int previousLength, bool previousIsAi, BarkPipelineOptions? options = null)
    {
        var o = options ?? new BarkPipelineOptions();
        return o.MinSpeechDelaySeconds
            + (previousIsAi ? o.AiSpeechBonusSeconds : 0)
            + Math.Max(0, previousLength - o.LongTextThreshold) * o.PerCharDelaySeconds;
    }

    /// <summary>
    /// TEXT-derived pacing into q1's per-item seam (the recorded q1/q2 boundary). The
    /// previous line = the last one HANDED TO THE VOICE CHANNEL (queued OR played — consult
    /// binding 1: WPF computes at dequeue, q1 supplies at enqueue; a second bark queued
    /// inside the pacing window must derive from the line ahead of it, not the line before).
    /// </summary>
    private TimeSpan ComputePacing()
    {
        if (_lastHandedToVoice is not { } prev)
        {
            return TimeSpan.FromSeconds(_options.MinSpeechDelaySeconds);
        }

        return TimeSpan.FromSeconds(ComputePacingSeconds(prev.Length, prev.IsAi, _options));
    }

    private object? LiveField(string field)
    {
        // The pipeline owns master_volume + mute (WPF ResolveField live reads, :1099-1137).
        if (string.Equals(field, "master_volume", StringComparison.OrdinalIgnoreCase))
        {
            return MasterVolume;
        }

        if (string.Equals(field, "mute", StringComparison.OrdinalIgnoreCase))
        {
            return MasterVolume == 0;
        }

        return _options.LiveFields?.Invoke(field);
    }
}
