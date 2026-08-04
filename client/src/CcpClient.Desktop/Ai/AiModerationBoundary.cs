using System.Collections.ObjectModel;

namespace CcpClient.Desktop.Ai;

// Moderation boundary (admission §8 slice c3; contract §7; ai-companion-admission.md §3).
// The guard is code OUTSIDE the model: it evaluates an INJECTED policy document whose
// default is the SP-019 "verdict-rejected shape only" posture (Empty — no category,
// wordlist, soft-hit value, or threshold invented; §9.2 #1 owner-pending). User-authored
// prompt sections can never widen or bypass it (contract §7 rule 4): request text is only
// ever the subject under evaluation, never a policy source. WPF sentinel-string refusal
// channel (ModerationRefusal.cs:53-54) REJECTED — refusals ride the c1 typed
// AiReply.Refused carrier. WPF threshold values (3-hit warning / 5-hit cooldown,
// ModerationCounter.cs:84-87) are recorded as baseline, never as decision.

/// <summary>Policy action for one category (contract §7 rule 3; soft-hit handling policy pending-owner — the SHAPE only).</summary>
public enum AiModerationAction
{
    /// <summary>Content must not be sent, shown, persisted, or executed.</summary>
    Block,

    /// <summary>Log-worthy but not blocking (first-attempt soft-hit shape, e.g. ProfessionalAdvice).</summary>
    SoftHit,
}

/// <summary>
/// One policy rule: a stable machine category token, an action, and the tokens that trip
/// it. Category codes are stable tokens — never user text, never logged (content-free
/// diagnostics carry side codes only). Matching is ordinal case-insensitive containment —
/// the placeholder evaluation SHAPE; wordlist semantics (word boundaries, regex) are
/// policy VALUES pending-owner with the wordlist itself (§9.2 #1).
/// </summary>
public sealed record AiModerationRule(string CategoryCode, AiModerationAction Action, IReadOnlyList<string> Tokens);

/// <summary>
/// The injected moderation policy document (contract §7 rule 5). Shape-validated at
/// construction; the DEFAULT is <see cref="Empty"/> — the SP-019 "verdict-rejected shape
/// only" posture: the document shape exists and is validated, but no category, wordlist,
/// or soft-hit value is invented (admission §3 rule 5; §9.2 #1 owner-pending). A consumer
/// (owner-supplied settings row, future slice) injects a real document; the guard
/// evaluates whatever document it is given, never a hardcoded list.
/// </summary>
public sealed record AiModerationPolicy
{
    public AiModerationPolicy(IReadOnlyList<AiModerationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var categories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (string.IsNullOrWhiteSpace(rule.CategoryCode))
                throw new ArgumentException("policy rule category code must be a non-empty stable token", nameof(rules));
            if (!categories.Add(rule.CategoryCode))
                throw new ArgumentException($"duplicate policy category code '{rule.CategoryCode}'", nameof(rules));
            ArgumentNullException.ThrowIfNull(rule.Tokens);
            if (rule.Tokens.Count == 0)
                throw new ArgumentException($"policy rule '{rule.CategoryCode}' carries no tokens", nameof(rules));
            foreach (var token in rule.Tokens)
            {
                if (string.IsNullOrEmpty(token))
                    throw new ArgumentException($"policy rule '{rule.CategoryCode}' carries an empty token", nameof(rules));
            }
        }

        Rules = new ReadOnlyCollection<AiModerationRule>([.. rules]);
    }

    public IReadOnlyList<AiModerationRule> Rules { get; }

    /// <summary>The default posture: shape only, zero values (SP-019 limit 3; admission §3 rule 5). Every evaluation passes.</summary>
    public static AiModerationPolicy Empty { get; } = new([]);
}

/// <summary>Whether a §3 boundary surface exists and is wired today, or is reserved for a named future surface (coverage honesty — never a blanket claim).</summary>
public enum AiModerationSurfaceDisposition
{
    /// <summary>The surface EXISTS in the client and the boundary moderates it (proven by the coverage tests).</summary>
    Wired,

    /// <summary>The §3 surface does NOT exist yet; <see cref="AiModerationSurface.ReservedFor"/> names the slice/seam that will carry it. Never a coverage claim.</summary>
    Reserved,
}

/// <summary>
/// One §3 moderation surface as a typed inventory row (admission §8 c3 coverage honesty).
/// Wired input surfaces name the pipeline entry point they moderate so the completeness
/// tripwire can fail when a new entry point appears unregistered.
/// </summary>
public sealed record AiModerationSurface(
    string Id,
    AiModerationSource Side,
    AiModerationSurfaceDisposition Disposition,
    string? ReservedFor,
    string? OperationEntryPoint);

/// <summary>
/// The typed §3 surface inventory (record.md §2 is the prose mirror). Wired = exists and
/// moderated by the boundary today; Reserved = the surface does not exist — the named
/// seam carries it when its slice lands. NEVER claim coverage of a nonexistent surface.
/// </summary>
public static class AiModerationSurfaces
{
    public static readonly AiModerationSurface InteractiveChatInput =
        new("interactive-chat-input", AiModerationSource.Input, AiModerationSurfaceDisposition.Wired, null, "RunInteractiveAsync");

    public static readonly AiModerationSurface AwarenessOperationInput =
        new("awareness-operation-input", AiModerationSource.Input, AiModerationSurfaceDisposition.Wired, null, "RunAwarenessAsync");

    public static readonly AiModerationSurface AwarenessContextFields =
        new("awareness-context-fields", AiModerationSource.Input, AiModerationSurfaceDisposition.Wired, null, "AiAwarenessContextPackaging.TryPackage");

    public static readonly AiModerationSurface InteractiveReplyOutput =
        new("interactive-reply-output", AiModerationSource.Output, AiModerationSurfaceDisposition.Wired, null, "RunInteractiveAsync");

    public static readonly AiModerationSurface AwarenessReplyOutput =
        new("awareness-reply-output", AiModerationSource.Output, AiModerationSurfaceDisposition.Wired, null, "RunAwarenessAsync");

    public static readonly AiModerationSurface MemoryPersistOutput =
        new("memory-persist-output", AiModerationSource.Output, AiModerationSurfaceDisposition.Reserved, "c4 memory store (admission §4 rule 5: moderation-gated persist with rollback)", null);

    public static readonly AiModerationSurface ReplySpeechOutput =
        new("reply-speech-output", AiModerationSource.Output, AiModerationSurfaceDisposition.Reserved, "future AI-reply speech consumer (no path exists)", null);

    public static readonly AiModerationSurface CommandFreeText =
        new("command-free-text", AiModerationSource.Output, AiModerationSurfaceDisposition.Wired, null, "AiEnvelopeValidator.FreeTextFields");

    public static readonly AiModerationSurface PromptTemplateSections =
        new("prompt-template-sections", AiModerationSource.Input, AiModerationSurfaceDisposition.Reserved, "c7 prompt assembly (user-authored preset sections through EvaluateInput)", null);

    public static readonly AiModerationSurface CommunityPrompts =
        new("community-prompts", AiModerationSource.Input, AiModerationSurfaceDisposition.Reserved, "future community-prompt surface (none exists)", null);

    public static readonly AiModerationSurface QuizTemplates =
        new("quiz-templates", AiModerationSource.Input, AiModerationSurfaceDisposition.Reserved, "future quiz consumer (interactive class; WPF gap — quiz templates unmoderated, QuizService.cs:561 is output-only)", null);

    /// <summary>The closed inventory. Adding a Wired row without a coverage assertion fails the tripwire test.</summary>
    public static IReadOnlyList<AiModerationSurface> All { get; } =
    [
        InteractiveChatInput,
        AwarenessOperationInput,
        AwarenessContextFields,
        InteractiveReplyOutput,
        AwarenessReplyOutput,
        MemoryPersistOutput,
        ReplySpeechOutput,
        CommandFreeText,
        PromptTemplateSections,
        CommunityPrompts,
        QuizTemplates,
    ];
}

/// <summary>
/// Escalation thresholds (admission §3 rule 4). VALUES owner-pending (§9.2 #1):
/// <see cref="WpfBaselinePlaceholder"/> records the WPF baseline (3-hit one-shot warning /
/// 5-hit cooldown, 10-min rolling window, 5-min cooldown — ModerationCounter.cs:84-87) as
/// a BASELINE FACT, never a decision.
/// </summary>
public sealed record AiEscalationThresholds(int WarningAt, int CooldownAt, TimeSpan Window, TimeSpan Cooldown)
{
    /// <summary>WPF baseline values, recorded not decided (admission §3 rule 4).</summary>
    public static AiEscalationThresholds WpfBaselinePlaceholder { get; } =
        new(3, 5, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5));
}

/// <summary>Typed escalation state (the WPF ModerationCounterState shape, ported). Consumed at interactive operation admission and (c7) by the user-facing warning surface.</summary>
public sealed record AiEscalationState(int HitsInWindow, bool WarningShown, bool CooldownActive, DateTimeOffset? CooldownEndsAt);

/// <summary>
/// The escalation counter MECHANISM (admission §3 rule 4): rolling-window hit list,
/// one-shot warning at threshold, non-stacking cooldown at threshold, expired cooldown
/// resets window + warning (WPF ModerationCounter.cs:108-200, ported as typed mechanism;
/// values injected). Session-scoped by design (record.md §3.2 rule 4): WPF persists
/// across restarts against DECIDED thresholds; persisting placeholder-valued state would
/// encode undecided values into an SP-005 schema. The state stays serializable-shaped
/// (hits + cooldown-end, the WPF ModerationCounterPersistedState shape) so persistence
/// lands additively when thresholds are owner-decided. Divergence from WPF restart
/// survival is recorded, never claimed as parity.
/// </summary>
public sealed class AiModerationEscalation
{
    private readonly AiEscalationThresholds _thresholds;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly List<DateTimeOffset> _hits = [];
    private bool _warningShown;
    private DateTimeOffset? _cooldownEndsAt;

    public AiModerationEscalation(AiEscalationThresholds? thresholds = null, Func<DateTimeOffset>? clock = null)
    {
        _thresholds = thresholds ?? AiEscalationThresholds.WpfBaselinePlaceholder;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Records one interactive-input moderation hit and returns the new state (WPF
    /// RecordHit shape): prune expired → append → cooldown at threshold (non-stacking —
    /// hits during an active cooldown never extend it) → else one-shot warning.
    /// </summary>
    public AiEscalationState RecordHit()
    {
        lock (_gate)
        {
            var now = _clock();
            PruneExpiredNoLock(now);
            _hits.Add(now);
            if (_hits.Count >= _thresholds.CooldownAt && _cooldownEndsAt is null)
            {
                _cooldownEndsAt = now.Add(_thresholds.Cooldown);
            }
            else if (_hits.Count >= _thresholds.WarningAt && !_warningShown)
            {
                _warningShown = true;
            }

            return StateNoLock(now);
        }
    }

    /// <summary>Current state. An expired cooldown clears window + warning (fresh start, WPF GetState shape).</summary>
    public AiEscalationState GetState()
    {
        lock (_gate)
        {
            var now = _clock();
            PruneExpiredNoLock(now);
            if (_cooldownEndsAt is { } ends && now >= ends)
            {
                _cooldownEndsAt = null;
                _hits.Clear();
                _warningShown = false;
            }

            return StateNoLock(now);
        }
    }

    private AiEscalationState StateNoLock(DateTimeOffset now) =>
        new(_hits.Count, _warningShown, _cooldownEndsAt is { } e && now < e, _cooldownEndsAt is { } e2 && now < e2 ? e2 : null);

    private void PruneExpiredNoLock(DateTimeOffset now) =>
        _hits.RemoveAll(h => now - h >= _thresholds.Window);
}

/// <summary>
/// The moderation boundary (contract §7). Pure local evaluation — NO network, NO log
/// lines, NO diagnostic text (outcomes ride typed verdicts and the pipeline's content-free
/// diagnostic emission). Placement (c1 seam consumed; record.md §3.2 rule 2): input at
/// operation admission (post provider-admission, pre-send); output inside the owned
/// operation after the stale check, before application; every free-text command field
/// pre-execution via <see cref="ModerateCommandField"/> composed into
/// <see cref="AiEnvelopePolicy.ModerateText"/>.
/// </summary>
public sealed class AiModerationBoundary
{
    private readonly AiModerationPolicy _policy;

    public AiModerationBoundary(AiModerationPolicy? policy = null, AiModerationEscalation? escalation = null)
    {
        _policy = policy ?? AiModerationPolicy.Empty;
        Escalation = escalation ?? new AiModerationEscalation();
    }

    /// <summary>The escalation counter consulted at interactive operation admission.</summary>
    public AiModerationEscalation Escalation { get; }

    /// <summary>Input side (contract §7 rule 1): user-authored/observed text entering an AI request.</summary>
    public AiModerationVerdict EvaluateInput(string text, AiModerationSurface surface) =>
        Evaluate(text, surface);

    /// <summary>Output side (contract §7 rule 1): model-produced text shown, spoken, persisted, or executed.</summary>
    public AiModerationVerdict EvaluateOutput(string text, AiModerationSurface surface) =>
        Evaluate(text, surface);

    /// <summary>Every-command-field rule (contract §7 rule 2): free-text command fields pre-execution.</summary>
    public AiModerationVerdict ModerateCommandField(string text) =>
        Evaluate(text, AiModerationSurfaces.CommandFreeText);

    private AiModerationVerdict Evaluate(string text, AiModerationSurface surface)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(surface);
        foreach (var rule in _policy.Rules)
        {
            foreach (var token in rule.Tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    return rule.Action == AiModerationAction.Block
                        ? new AiModerationVerdict.Block(rule.CategoryCode) { SurfaceId = surface.Id }
                        : new AiModerationVerdict.SoftHit(rule.CategoryCode) { SurfaceId = surface.Id };
                }
            }
        }

        return AiModerationVerdict.Pass.Instance;
    }
}
