using System.Runtime.InteropServices;
using System.Text;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Ai;

// Awareness slice (admission §8 slice c5; contract §4; ai-companion-admission.md §5).
// Code-enforced consent at operation admission (typed state, never a prompt convention);
// typed cooldown machinery (per-trigger / global / per-keyword / loop-protection,
// extend-not-shrink) with observable Suppressed(cooldown) outcomes; context packaging
// under consent with EVERY field through the c3 input boundary; keyword-trigger routing
// as SP-004 OWNED awareness operations (the WPF fire-and-forget Task.Run,
// KeywordTriggerService.cs:1650, is REJECTED); drop-by-type at the awareness operation
// class (no out-of-context refusal bubble); typed Fallback/Unavailable visibility (the
// badge always reflects the true source).
//
// Owner-pending VALUES (§9.2 #4 — recorded as WPF baseline FACTS, never decisions):
// consent granularity/defaults (placeholder default = NOT GIVEN; WPF baseline both-false,
// WindowAwarenessService.cs:337-338 + AppSettings.cs:2982,2993); cooldown values
// (reaction effective default 10s, AppSettings.cs:3000-3008 clamp 10-600 — with the
// recorded discrepancy that the service's `?? 90` fallback, WindowAwarenessService.cs:374-388,
// is dead code against the non-nullable 10s default. OWNER QUESTION: 10 or 90?;
// keyword global 10s :4294; per-keyword 15s :4314; loop protection 5s :4438,4450;
// still-on milestones 1/5/10 min :436-438; rolled randomization AwarenessCooldownMaxSeconds
// NOT ported — nondeterminism has no place in the mechanism; owner-pending value).

/// <summary>
/// Typed awareness-consent state (contract §4 rule 1; admission §5 rule 1). Code-enforced
/// at operation admission — never a prompt convention. Placeholder default =
/// <see cref="NotGiven"/> (conservative posture: awareness performs NO operations until an
/// owner-decided consent exists; WPF baseline FACT: AwarenessModeEnabled AND
/// AwarenessConsentGiven both default false, AppSettings.cs:2982,2993). Consent
/// granularity (per-context-field? per-source?) and defaults are owner-pending (§9.2 #4):
/// the record is shaped so scope fields land ADDITIVELY without contract change.
/// </summary>
public sealed record AiAwarenessConsent(bool Granted)
{
    /// <summary>The placeholder default: consent NOT given. No awareness operation runs under this state.</summary>
    public static AiAwarenessConsent NotGiven { get; } = new(false);

    /// <summary>Consent given (global scope — granularity tightening lands additively, §9.2 #4).</summary>
    public static AiAwarenessConsent Given { get; } = new(true);
}

/// <summary>The four typed cooldown classes (contract §4 rule 2; admission §5 rule 3).</summary>
public enum AiCooldownKind
{
    /// <summary>Per-trigger cooldown (WPF KeywordTrigger.IsOnCooldown, KeywordTrigger.cs:230 — duration arrives with each trigger). The window-reaction path uses this class with a fixed reaction slot (WPF CanReact/MarkReaction, WindowAwarenessService.cs:375-433).</summary>
    PerTrigger,

    /// <summary>Global cooldown between ANY two awareness operations (WPF _lastGlobalTriggerTime gate, KeywordTriggerService.cs:923,1108,1274).</summary>
    Global,

    /// <summary>Hard per-keyword cooldown (WPF KeywordPerKeywordCooldownSeconds arm of the merged mute dict, KeywordTriggerService.cs:165-181).</summary>
    PerKeyword,

    /// <summary>Loop-protection mute so a trigger's own output cannot re-arm it (WPF AwarenessLoopProtection arm, KeywordTriggerService.cs:165-181). Equivalence: WPF merges this and PerKeyword into ONE dict entry with Math.Max(durations); the union of typed classes (suppressed if EITHER is live) is behaviorally identical — recorded, not a new mechanism.</summary>
    LoopProtection,
}

/// <summary>
/// Cooldown VALUES — placeholders; owner-pending (§9.2 #4). <see cref="WpfBaselinePlaceholder"/>
/// records the WPF baselines as FACTS, never decisions: reaction effective default 10s
/// (AppSettings.cs:3000-3008, clamp 10-600) **with the recorded discrepancy that the
/// service's `?? 90` fallback (WindowAwarenessService.cs:374-388) is dead code against the
/// non-nullable 10s default — OWNER QUESTION: 10 or 90?**; keyword global 10s
/// (AppSettings.cs:4294); per-keyword 15s (:4314); loop protection 5s (:4438,4450).
/// Per-trigger durations arrive with each trigger (WPF KeywordTrigger.CooldownSeconds is
/// per-trigger config, floored by the per-keyword class via the union — :4314 doc); no
/// single baseline exists. Still-on milestones (1/5/10 min, WindowAwarenessService.cs:436-438)
/// and the rolled max (AwarenessCooldownMaxSeconds) are scheduling/value matters, not mechanism.
/// </summary>
public sealed record AiCooldownValues(TimeSpan Reaction, TimeSpan Global, TimeSpan PerKeyword, TimeSpan LoopProtection)
{
    /// <summary>WPF baseline values, recorded not decided (admission §5 rule 3; the 10-vs-90 owner question stands verbatim).</summary>
    public static AiCooldownValues WpfBaselinePlaceholder { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5));
}

/// <summary>Typed cooldown admission verdict. A suppression is OBSERVABLE (kind + expiry), never a silent no-op (contract §4 rule 2).</summary>
public abstract record AiCooldownVerdict
{
    private AiCooldownVerdict() { }

    /// <summary>No live cooldown for the (kind, key) pair.</summary>
    public sealed record Admitted : AiCooldownVerdict
    {
        public static readonly Admitted Instance = new();
    }

    /// <summary>A live cooldown suppresses the operation until <paramref name="Until"/>.</summary>
    public sealed record Suppressed(AiCooldownKind Kind, DateTimeOffset Until) : AiCooldownVerdict;
}

/// <summary>
/// The cooldown REGISTRY (contract §4 rule 2; admission §5 rule 3): typed per-trigger /
/// global / per-keyword / loop-protection classes with **extend-not-shrink** semantics
/// (WPF mechanism, KeywordTriggerService.cs:178-181 — a cooldown operation may extend a
/// live cooldown, never shorten it). Live iff `now &lt; expiry` — admitted at exact
/// equality (WPF boundary, `DateTime.UtcNow &lt; expiresAt` :94 and
/// `TotalSeconds &gt;= cooldownSeconds` WindowAwarenessService.cs:380). Expired entries are
/// pruned on access (WPF IsKeywordMuted shape, :82-99). Injectable clock (the c3
/// AiModerationEscalation precedent). Keys are caller-normalized (WPF uses the configured
/// keyword string as-is); keys NEVER enter diagnostics.
/// </summary>
public sealed class AiCooldownRegistry
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<(AiCooldownKind Kind, string Key), DateTimeOffset> _expiries = [];

    public AiCooldownRegistry(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Admission check for one (kind, key) pair. Expired entries are pruned on access.</summary>
    public AiCooldownVerdict Check(AiCooldownKind kind, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        lock (_gate)
        {
            var now = _clock();
            if (_expiries.TryGetValue((kind, key), out var expiry))
            {
                if (now < expiry)
                {
                    return new AiCooldownVerdict.Suppressed(kind, expiry);
                }

                _expiries.Remove((kind, key));
            }

            return AiCooldownVerdict.Admitted.Instance;
        }
    }

    /// <summary>
    /// Extends (never shrinks) the cooldown for a (kind, key) pair and returns the
    /// effective expiry: `max(existing live expiry, now + duration)` (WPF
    /// `if (!TryGetValue || existing &lt; expiresAt) write` mechanism, KeywordTriggerService.cs:178-181).
    /// </summary>
    public DateTimeOffset Extend(AiCooldownKind kind, string key, TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "cooldown duration must be non-negative");
        }

        lock (_gate)
        {
            var now = _clock();
            var requested = now.Add(duration);
            if (_expiries.TryGetValue((kind, key), out var existing) && existing > now && existing >= requested)
            {
                return existing; // a live, longer cooldown is never shortened
            }

            _expiries[(kind, key)] = requested;
            return requested;
        }
    }
}

/// <summary>
/// The WPF-observed awareness context shape (`[Category | App | Title | Duration]`,
/// Services/AiService.cs:160-163,182-188). Privacy-sensitive: transmitted ONLY under
/// consent, EVERY field through the c3 input boundary before assembly, NEVER into
/// diagnostics or memory (contract §12; admission §5 rule 2).
/// </summary>
public sealed record AiAwarenessContext(string Category, string App, string Title, string DurationText)
{
    /// <summary>The WPF still-on duration format (`AiService.cs:182-188`): seconds under a minute, minutes under an hour, else hours.</summary>
    public static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes < 1
            ? $"{(int)duration.TotalSeconds}s"
            : duration.TotalMinutes < 60
                ? $"{(int)duration.TotalMinutes}m"
                : $"{(int)duration.TotalHours}h";
}

/// <summary>
/// Context packaging (admission §5 rule 2). Assembles the WPF-observed
/// `[Category: X | App: Y | Title: Z | Duration: N]` shape — ONLY after EVERY field passes
/// the c3 input boundary (<see cref="AiModerationSurfaces.AwarenessContextFields"/>). A
/// blocking verdict on ANY field prevents transmission entirely (zero network follows).
/// Fields never enter diagnostics or memory: this packager emits nothing, and the
/// pipeline persists memory for the interactive class only (c4).
/// </summary>
public static class AiAwarenessContextPackaging
{
    /// <summary>Moderates every field, then assembles. False + typed refusal when any field blocks; <paramref name="request"/> is null in that case (nothing transmittable exists).</summary>
    public static bool TryPackage(
        AiAwarenessContext context,
        AiModerationBoundary boundary,
        out AiRequest? request,
        out AiModerationRefusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(boundary);
        foreach (var field in new[] { context.Category, context.App, context.Title, context.DurationText })
        {
            ArgumentNullException.ThrowIfNull(field);
            if (boundary.EvaluateInput(field, AiModerationSurfaces.AwarenessContextFields) is AiModerationVerdict.Block block)
            {
                request = null;
                refusal = new AiModerationRefusal(block.CategoryCode, AiModerationSource.Input);
                return false;
            }
        }

        // The WPF-observed shape verbatim (AiService.cs:160-163).
        request = new AiRequest(
            $"[Category: {context.Category} | App: {context.App} | Title: {context.Title} | Duration: {context.DurationText}]");
        refusal = null;
        return true;
    }
}

/// <summary>Why an awareness-class routing produced no visible reply (drop-by-type, contract §4 rule 3 — typed and observable at the machinery level, never a user-facing bubble).</summary>
public enum AiAwarenessDropKind
{
    /// <summary>Awareness consent not given (typed Suppressed at admission).</summary>
    ConsentDenied,

    /// <summary>A cooldown suppressed the operation (typed Suppressed at admission).</summary>
    CooldownSuppressed,

    /// <summary>Moderation refused input or output. Drops BY TYPE — never a refusal bubble, and (recorded divergence) NEVER the canned fallback: WPF showed canned on refusal only because its string channel collapses refusal → null (KeywordTriggerService.cs:1661-1664); the typed pipeline distinguishes by type deliberately.</summary>
    RefusedByModeration,

    /// <summary>The provider could not produce a reply and no app-authored canned fallback was supplied (window-reaction path — WPF has no canned fallback there).</summary>
    ProviderUnavailable,

    /// <summary>The owned operation was cancelled (panic / provider switch / teardown).</summary>
    Cancelled,
}

/// <summary>
/// The routing-layer result for awareness operations (contract §4 rules 3-4; admission §5
/// rule 4). <see cref="Visible"/> carries the typed reply — `Generated` or `Fallback`,
/// distinguishable BY TYPE so the badge always reflects the true source (the WPF
/// `aiGenerated`-reflects-actual-source behavior retained, the string channel rejected).
/// <see cref="Dropped"/> is the awareness-class drop-by-type: no out-of-context refusal
/// bubble, but typed and observable at the machinery level (admission detail attached when
/// the drop was a typed suppression).
/// </summary>
public abstract record AiAwarenessRoutingResult
{
    private AiAwarenessRoutingResult() { }

    /// <summary>A reply to surface. Fallback text is app-authored canned text (c3's recorded moderation non-claim applies ONLY to genuinely app-authored phrases — a mod/community phrase source would NOT inherit it; WPF sources phrases from CompanionPhrases/mods, KeywordTriggerService.cs:1676-1685).</summary>
    public sealed record Visible(AiReply Reply) : AiAwarenessRoutingResult;

    /// <summary>No user-visible result. Typed, never a silent no-op: the admission detail rides along when the drop was a suppression.</summary>
    public sealed record Dropped(AiAwarenessDropKind Kind, AiAdmission.Suppressed? Admission = null) : AiAwarenessRoutingResult;
}

/// <summary>
/// Window-title observation capability (admission §5 rule 2; SP-006 probe discipline).
/// Capability-probed typed state, honestly scoped: Windows = real foreground-title capture
/// (GetForegroundWindow + GetWindowTextW — session facts on the evidence box); Linux =
/// typed Unavailable (WSL2 zero-distro named limit on the evidence box — X11 session facts
/// owner-gated, NEVER faked; NO Wayland claim anywhere). The probe's Available detail is
/// content-free (title LENGTH only — the WPF discipline "only categorizes, never logs
/// titles", AppSettings.cs:2983-2986). Titles never enter diagnostics, logs, or memory.
/// </summary>
public static class AiWindowTitleCapability
{
    /// <summary>The SP-006 capability name.</summary>
    public const string Name = "ai.awareness.window-title";

    /// <summary>Linux typed-Unavailable code (WSL zero-distro named limit; X11 facts owner-gated; no Wayland claim).</summary>
    public const string LinuxUnprobedCode = "title-observation-linux-unprobed";

    /// <summary>Transient Windows state: no foreground window (lock screen, secure desktop, mid-switch — WPF WindowAwarenessService.cs:596-604 discipline).</summary>
    public const string NoForegroundWindowCode = "no-foreground-window";

    /// <summary>Declares the capability probe (registration never yields Available — SP-006; the probe must run).</summary>
    public static void Register(CapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        capabilities.Register(Name, Probe);
    }

    /// <summary>The probe: Windows = real capture attempt; Linux = typed Unavailable (never faked).</summary>
    public static Task<CapabilityState> Probe(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryCaptureForegroundTitle(out var title)
                ? Task.FromResult<CapabilityState>(new CapabilityState.Available(
                    $"windows foreground window title observation confirmed (title length {title.Length}; content never logged)"))
                : Task.FromResult<CapabilityState>(new CapabilityState.Unavailable(new CapabilityReason(
                    NoForegroundWindowCode, "no foreground window (lock screen, secure desktop, or mid-switch) — transient, re-probe later")));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Task.FromResult<CapabilityState>(new CapabilityState.Unavailable(new CapabilityReason(
                LinuxUnprobedCode,
                "linux window-title observation not probed on the evidence box (WSL2 zero distros — owner-gated); " +
                "X11 session facts pending; no Wayland claim")));
        }

        return Task.FromResult<CapabilityState>(new CapabilityState.Unavailable(new CapabilityReason(
            CapabilityReasonCodes.UnsupportedPlatform, "platform is neither Windows nor Linux")));
    }

    /// <summary>Real foreground-title capture (Windows only; false when there is no capturable foreground window). The title leaves ONLY to the caller — never logged.</summary>
    public static bool TryCaptureForegroundTitle(out string title)
    {
        title = string.Empty;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return true; // a foreground window with an empty title: the observation mechanism works
        }

        var buffer = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Capacity);
        title = buffer.ToString();
        return true;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }
}

/// <summary>Typed title-observation result. Observation happens ONLY under consent and an Available capability (admission §5 rules 1-2).</summary>
public abstract record AiTitleObservation
{
    private AiTitleObservation() { }

    /// <summary>Consent not given — no observation performed (never a silent empty string).</summary>
    public sealed record ConsentNotGiven : AiTitleObservation
    {
        public static readonly ConsentNotGiven Instance = new();
    }

    /// <summary>The capability is not Available; carries the honest typed state (Linux Unavailable, no-foreground transient, not-probed).</summary>
    public sealed record Unavailable(CapabilityState State) : AiTitleObservation;

    /// <summary>The foreground title, observed under consent. The title leaves ONLY to the caller — never diagnostics, never logs, never memory.</summary>
    public sealed record Observed(string Title) : AiTitleObservation;
}

/// <summary>
/// The awareness service (admission §8 slice c5): the admission point for all awareness
/// operations. Consent is code-enforced HERE and again at the pipeline (the typed state
/// reaches the pipeline's admission via the typed overload — contract §4 rule 1).
/// Cooldown-suppressed and consent-denied admissions terminate Completed with typed
/// Suppressed outcomes — observable (typed result + content-free diagnostic), never
/// silent no-ops, and never touching SendAttempts (offline zero-network preserved).
/// Keyword routing runs as SP-004 OWNED awareness operations on the pipeline (panic-
/// cancellable; the WPF fire-and-forget Task.Run shape is rejected). Construction starts
/// nothing (SP-003).
/// </summary>
public sealed class AiAwarenessService
{
    // Fixed registry slots: the global class has exactly one entry; the window-reaction
    // path shares the per-trigger class under one fixed key (WPF CanReact/MarkReaction
    // mechanism, WindowAwarenessService.cs:375-433). These keys never leave the registry.
    private const string GlobalKey = "(global)";
    private const string ReactionKey = "(window-reaction)";

    private readonly AiOperationPipeline _pipeline;
    private readonly AiModerationBoundary _boundary;
    private readonly IAiDiagnosticsSink _diagnostics;
    private readonly CapabilityRegistry _capabilities;
    private readonly AiCooldownRegistry _cooldowns;
    private volatile AiAwarenessConsent _consent = AiAwarenessConsent.NotGiven;
    private volatile AiCooldownValues _values;

    public AiAwarenessService(
        AiOperationPipeline pipeline,
        AiModerationBoundary boundary,
        IAiDiagnosticsSink diagnostics,
        CapabilityRegistry capabilities,
        AiCooldownRegistry? cooldowns = null,
        AiCooldownValues? values = null)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _cooldowns = cooldowns ?? new AiCooldownRegistry();
        _values = values ?? AiCooldownValues.WpfBaselinePlaceholder;
    }

    /// <summary>The typed consent state. Default NOT GIVEN (placeholder; §9.2 #4 owner-pending): no awareness operation runs until an owner-decided consent exists.</summary>
    public AiAwarenessConsent Consent
    {
        get => _consent;
        set => _consent = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The cooldown registry (mechanism; values injected at construction).</summary>
    public AiCooldownRegistry Cooldowns => _cooldowns;

    /// <summary>
    /// The cooldown VALUES in effect (placeholder baselines; §9.2 #4 owner-pending).
    /// Settable (SP-046 c7 — the cooldown settings surface drives the typed state at
    /// runtime; session-scoped, never persisted). A value edit applies to the NEXT
    /// cooldown extension only: extend-not-shrink semantics (registry <see cref="AiCooldownRegistry.Extend"/>)
    /// mean a live cooldown is NEVER shortened by an edit.
    /// </summary>
    public AiCooldownValues Values
    {
        get => _values;
        set => _values = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Keyword-trigger routing (admission §5 rule 4): trigger → OWNED awareness operation
    /// (panic-cancellable; the WPF `_ = Task.Run(...)` fire-and-forget,
    /// KeywordTriggerService.cs:1650, is rejected). Gates in WPF order: consent → cooldowns
    /// (global, per-keyword, loop-protection, per-trigger — suppressed if ANY is live) →
    /// RecordFire stamped BEFORE dispatch (WPF `LastTriggeredAt = now` discipline,
    /// :1300-1308). Provider-Unavailable falls back to app-authored canned text as typed
    /// <see cref="AiReply.Fallback"/> (badge reflects the true source; §2 rule 4) — keyword
    /// path ONLY (WPF has no canned fallback on window reactions). Refusals drop by type,
    /// NEVER to canned (recorded divergence from WPF's string channel).
    /// </summary>
    public async Task<AiAwarenessRoutingResult> RunKeywordCommentAsync(
        string triggerId,
        string keyword,
        string? promptTemplate = null,
        string? fallbackText = null,
        TimeSpan? perTriggerCooldown = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(triggerId);
        ArgumentException.ThrowIfNullOrEmpty(keyword);

        if (!Consent.Granted)
        {
            return SuppressedDrop(AiSuppressionKind.ConsentDenied, AiAwarenessDropKind.ConsentDenied, "suppressed:consent-denied");
        }

        // Cooldown admission (extend-not-shrink classes; first live suppression wins).
        var verdict = FirstLive(
            _cooldowns.Check(AiCooldownKind.Global, GlobalKey),
            _cooldowns.Check(AiCooldownKind.PerKeyword, keyword),
            _cooldowns.Check(AiCooldownKind.LoopProtection, keyword),
            _cooldowns.Check(AiCooldownKind.PerTrigger, triggerId));
        if (verdict is AiCooldownVerdict.Suppressed)
        {
            return SuppressedDrop(AiSuppressionKind.Cooldown, AiAwarenessDropKind.CooldownSuppressed, "suppressed:cooldown");
        }

        // RecordFire (extend-not-shrink) BEFORE dispatch — a failed operation still gates
        // the next one (WPF stamps LastTriggeredAt before DispatchMergedAsync).
        _cooldowns.Extend(AiCooldownKind.Global, GlobalKey, _values.Global);
        _cooldowns.Extend(AiCooldownKind.PerKeyword, keyword, _values.PerKeyword);
        _cooldowns.Extend(AiCooldownKind.LoopProtection, keyword, _values.LoopProtection);
        if (perTriggerCooldown is { } triggerCooldown)
        {
            _cooldowns.Extend(AiCooldownKind.PerTrigger, triggerId, triggerCooldown);
        }

        // WPF prompt shape verbatim (AiService.cs:200-205).
        var prompt = string.IsNullOrEmpty(promptTemplate)
            ? $"You just caught the user on the word '{keyword}'. React in character, one short line."
            : promptTemplate.Replace("{keyword}", keyword, StringComparison.Ordinal);

        var result = await _pipeline.RunAwarenessAsync(new AiRequest(prompt), Consent).ConfigureAwait(false);
        return Route(result, fallbackText);
    }

    /// <summary>
    /// Window-reaction operation (admission §5 rules 1-3): consent → reaction cooldown →
    /// context packaging (assembled ONLY under consent; every field through the c3 input
    /// boundary — a blocking verdict means ZERO transmission) → owned awareness operation.
    /// No canned fallback on this path (WPF shape): Unavailable/Refused drop by type.
    /// </summary>
    public async Task<AiAwarenessRoutingResult> RunReactionAsync(
        AiAwarenessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!Consent.Granted)
        {
            return SuppressedDrop(AiSuppressionKind.ConsentDenied, AiAwarenessDropKind.ConsentDenied, "suppressed:consent-denied");
        }

        if (_cooldowns.Check(AiCooldownKind.PerTrigger, ReactionKey) is AiCooldownVerdict.Suppressed)
        {
            return SuppressedDrop(AiSuppressionKind.Cooldown, AiAwarenessDropKind.CooldownSuppressed, "suppressed:cooldown");
        }

        if (!AiAwarenessContextPackaging.TryPackage(context, _boundary, out var request, out _))
        {
            // A blocking policy prevents transmission — zero network follows. Content-free
            // diagnostic: the SIDE code only, never the category, never the field.
            _diagnostics.Emit(new AiDiagnosticRecord(
                AiOperationClass.Awareness, AiEndpointClass.Unspecified, AiDiagnosticOutcome.Refused, "refused:input", -1, 0, 0, []));
            return new AiAwarenessRoutingResult.Dropped(AiAwarenessDropKind.RefusedByModeration);
        }

        _cooldowns.Extend(AiCooldownKind.PerTrigger, ReactionKey, _values.Reaction);
        var result = await _pipeline.RunAwarenessAsync(request!, Consent).ConfigureAwait(false);
        return Route(result, fallbackText: null);
    }

    /// <summary>
    /// Foreground window-title observation: ONLY under consent and an Available capability
    /// (admission §5 rules 1-2). The title leaves ONLY to the caller — never diagnostics,
    /// never logs, never memory.
    /// </summary>
    public AiTitleObservation ObserveForegroundTitle()
    {
        if (!Consent.Granted)
        {
            return AiTitleObservation.ConsentNotGiven.Instance;
        }

        var state = _capabilities.GetState(AiWindowTitleCapability.Name);
        if (state is not CapabilityState.Available)
        {
            return new AiTitleObservation.Unavailable(state);
        }

        return AiWindowTitleCapability.TryCaptureForegroundTitle(out var title)
            ? new AiTitleObservation.Observed(title)
            : new AiTitleObservation.Unavailable(new CapabilityState.Unavailable(new CapabilityReason(
                AiWindowTitleCapability.NoForegroundWindowCode, "no capturable foreground window at observation time")));
    }

    private AiAwarenessRoutingResult Route(AiOperationResult result, string? fallbackText)
    {
        if (result.Outcome is Lifecycle.OperationOutcome.Cancelled)
        {
            return new AiAwarenessRoutingResult.Dropped(AiAwarenessDropKind.Cancelled);
        }

        if (result.Admission is AiAdmission.Suppressed suppressed)
        {
            return new AiAwarenessRoutingResult.Dropped(
                suppressed.Kind == AiSuppressionKind.ConsentDenied
                    ? AiAwarenessDropKind.ConsentDenied
                    : AiAwarenessDropKind.CooldownSuppressed,
                suppressed);
        }

        return result.Reply switch
        {
            AiReply.Generated generated => new AiAwarenessRoutingResult.Visible(generated),
            AiReply.Fallback fallback => new AiAwarenessRoutingResult.Visible(fallback),
            // Drop-by-type (contract §4 rule 3): no refusal bubble on the awareness class,
            // and NEVER the canned fallback (recorded divergence from the WPF string
            // channel, which collapsed refusal → null → canned).
            AiReply.Refused => new AiAwarenessRoutingResult.Dropped(AiAwarenessDropKind.RefusedByModeration),
            // §2 rule 4 typed visibility: app-authored canned text as typed Fallback
            // (keyword path only — the window-reaction path passes fallbackText: null).
            AiReply.Unavailable => !string.IsNullOrEmpty(fallbackText)
                ? new AiAwarenessRoutingResult.Visible(new AiReply.Fallback(fallbackText!, "keyword-fallback"))
                : new AiAwarenessRoutingResult.Dropped(AiAwarenessDropKind.ProviderUnavailable),
            _ => new AiAwarenessRoutingResult.Dropped(AiAwarenessDropKind.ProviderUnavailable),
        };
    }

    private AiAwarenessRoutingResult SuppressedDrop(AiSuppressionKind kind, AiAwarenessDropKind dropKind, string stableCode)
    {
        // Observable, never a silent no-op (contract §4 rules 1-2): typed result + a
        // content-free diagnostic. Generation -1 = no operation began; duration 0 = no
        // work performed; Unspecified endpoint = no provider was involved.
        _diagnostics.Emit(new AiDiagnosticRecord(
            AiOperationClass.Awareness, AiEndpointClass.Unspecified, AiDiagnosticOutcome.Completed, stableCode, -1, 0, 0, []));
        return new AiAwarenessRoutingResult.Dropped(dropKind, new AiAdmission.Suppressed(kind));
    }

    private static AiCooldownVerdict FirstLive(params AiCooldownVerdict[] verdicts)
    {
        foreach (var verdict in verdicts)
        {
            if (verdict is AiCooldownVerdict.Suppressed)
            {
                return verdict;
            }
        }

        return AiCooldownVerdict.Admitted.Instance;
    }
}
