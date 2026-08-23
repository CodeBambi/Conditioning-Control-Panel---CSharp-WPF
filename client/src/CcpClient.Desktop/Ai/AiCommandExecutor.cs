namespace CcpClient.Desktop.Ai;

// Command execution (admission §8 slice c6; contract §8 rule 6, §9): validated envelope →
// execution plan → per-effect dispatch behind master + per-effect consent gates
// (post-validation), with generation supersession checked per command at dispatch
// (spike limit 7 — the verdict the validator could never emit, landed here).
//
// WPF archaeology (READ-ONLY): master gate Services/Commands/AiCommandService.cs:42,
// per-effect IsEffectAllowed map AiCommandService.cs:182-200 (defaults
// CCP.Core/Models/CompanionPromptSettings.cs:99-112). WPF/first-attempt gated commands are
// SILENTLY DROPPED (log line only) and handler faults are swallowed (async void
// ExecuteCommand top-level catch) — both REJECTED: gates here are typed verdicts, and a
// faulting handler faults the dispatch call, never a silent swallow.
//
// EFFECT BACKENDS EXIST, and this comment used to say they did not. The rack under
// Session/SessionParticipant is live and AiEffectBridge maps the kinds this build can honour
// onto it (spiral, pink, bubbles); the other eight kinds have a NAMED absence there rather
// than a handler, because a command that half-applies is worse than one that says this build
// has no such effect. An admitted kind with no registered handler is still
// NotExecuted(EffectUnavailable), never a fake effect, and tests still inject canary handlers
// so zero-execution proofs stay falsifiable.
//
// WHAT IS STILL NOT WIRED, so nobody reads the above as more than it is: nothing calls
// Execute. A model reply never reaches AiEnvelopeValidator — AiOperationPipeline.cs:340-346
// refuses envelope-shaped replies with MalformedOutput and
// client/src/CcpClient.Desktop/Ai/AiTextHygiene.cs:24-30 records that as a decision — so no
// companion reply can reach this executor in this build.

/// <summary>
/// The dispatch target for one admitted effect command (c6). Product implementations live in
/// <see cref="AiEffectBridge"/>, over the landed effect rack. Implementations must be
/// self-safing: <see cref="AiCommandExecutor"/> deliberately does NOT catch (a fault
/// propagates — honest, never the WPF/first-attempt swallow-and-log).
/// </summary>
public interface IAiEffectHandler
{
    /// <summary>Applies one admitted command. The command data is in-process only — implementations never log command-field contents (content-free rule, contract §12).</summary>
    void Execute(AiCommand command);
}

/// <summary>
/// The execution-time gate state (contract §8 rule 6: gating AFTER validation, BEFORE
/// execution). Consent captured at validation can change before dispatch, so the executor
/// re-evaluates the CURRENT gate state per command; a command validated under master-ON
/// and dispatched under master-OFF is ConsentGated("master") at execution — typed, never
/// partially applied.
/// </summary>
/// <param name="MasterEffectsEnabled">The master consent toggle (WPF AllowAiToControlEffects shape).</param>
/// <param name="IsEffectAllowed">The per-effect consent map (WPF IsEffectAllowed shape).</param>
/// <param name="Generation">The generation the plan was produced under.</param>
/// <param name="IsGenerationLive">Point-of-dispatch liveness (AsyncOperationOwner.IsLive shape): a stale generation supersedes every remaining command — never a late apply.</param>
public sealed record AiExecutionGates(
    bool MasterEffectsEnabled,
    Func<AiCommandKind, bool> IsEffectAllowed,
    int Generation,
    Func<int, bool> IsGenerationLive)
{
    /// <summary>
    /// The pending-owner default (admission §9.2 #5): master OFF, ZERO per-effect
    /// allowances — nothing admitted. DIVERGENCE (deliberate, recorded, never silent): the
    /// WPF baseline (contract §8 rule 6; CompanionPromptSettings.cs:106-110) has master OFF
    /// but bubbles/subliminal/bounce ON; the greenfield pipeline executes only the
    /// owner-admitted subset, default NONE (conservative pending-owner posture —
    /// admission §8 c6 row; the row's verbatim text is recorded in the packet record §2.1).
    /// </summary>
    public static AiExecutionGates NoneAdmitted(int generation, Func<int, bool> isGenerationLive) =>
        new(false, _ => false, generation, isGenerationLive);

    /// <summary>
    /// Derives the execution gates from the SAME policy instance the validator consumed
    /// (single consent source — pre-approach consult, the packet record §3.1.1): the two
    /// gate evaluations cannot drift apart unless consent genuinely changed between
    /// validation and dispatch (which the dispatch re-gate then catches, typed).
    /// </summary>
    public static AiExecutionGates FromPolicy(AiEnvelopePolicy policy, int generation, Func<int, bool> isGenerationLive)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new AiExecutionGates(policy.MasterEffectsEnabled, policy.IsEffectAllowed, generation, isGenerationLive);
    }
}

/// <summary>
/// The typed outcome of dispatching one execution plan: exactly one verdict per plan
/// command, in plan order (contract §9 shape at execution level). <see cref="AiCommandVerdict.Valid"/>
/// in THIS result means DISPATCHED to a handler; every non-dispatch carries its typed
/// reason. Content-free: verdicts carry stable tokens only, never command-field contents.
/// </summary>
public sealed record AiCommandExecution(IReadOnlyList<AiCommandVerdict> Verdicts);

/// <summary>
/// The command executor (c6). Consumes a validator-constructed <see cref="AiExecutionPlan"/>
/// (invalid envelopes have NO plan — the executor is unreachable on every rejected class,
/// type-enforced) and dispatches each command in order:
/// generation-live → master gate → per-effect gate → handler resolution → dispatch.
/// SYNCHRONOUS by design, and the landed backends keep it that way: every module the bridge
/// drives applies its dials and engages synchronously (<c>OwnedSessionEffect.Arm</c>, which
/// upstream's services do too — <c>OverlayService.cs:394-428</c> puts the layers up before
/// <c>Start</c> returns). A real async backend lands with a signature change, recorded.
/// </summary>
public sealed class AiCommandExecutor
{
    private readonly IReadOnlyDictionary<AiCommandKind, IAiEffectHandler> _handlers;

    public AiCommandExecutor(IReadOnlyDictionary<AiCommandKind, IAiEffectHandler>? handlers = null) =>
        _handlers = handlers ?? new Dictionary<AiCommandKind, IAiEffectHandler>();

    /// <summary>
    /// Whether a registered handler exists for this kind — the answer to "can this build do
    /// that at all", which is a different question from "is she allowed to". The permissions
    /// grid reads BOTH so a ticked switch over an absent backend cannot read as a capability.
    /// </summary>
    public bool Handles(AiCommandKind kind) => _handlers.ContainsKey(kind);

    /// <summary>
    /// Dispatches a plan under the current gates. Per command, in order (first match wins):
    /// stale generation → NotExecuted(SupersededGeneration) (spike limit 7 — checked PER
    /// COMMAND, so a mid-dispatch flip supersedes the rest); master off → ConsentGated("master");
    /// effect not allowed → ConsentGated(kind); no handler → NotExecuted(EffectUnavailable);
    /// else dispatch → Valid. Dispatch is all-or-nothing at the execution layer
    /// (pre-completion consult, the packet record §3.2.3): a faulting handler faults
    /// <see cref="Execute"/> — commands after the fault are NEVER dispatched (no partial
    /// silent application), and the caller gets no execution result at all. Contract §9's
    /// per-command verdict guarantee lives at the ENVELOPE layer (validation already gave
    /// every submitted command exactly one verdict); a faulted execution forfeits its own
    /// result rather than issuing partial or invented verdicts. Handler exceptions
    /// propagate (never swallowed).
    /// </summary>
    public AiCommandExecution Execute(AiExecutionPlan plan, AiExecutionGates gates)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(gates);

        var verdicts = new AiCommandVerdict[plan.Commands.Count];
        for (var i = 0; i < plan.Commands.Count; i++)
        {
            var command = plan.Commands[i];
            if (!gates.IsGenerationLive(gates.Generation))
            {
                verdicts[i] = new AiCommandVerdict.NotExecuted(AiNotExecutedReason.SupersededGeneration);
            }
            else if (!gates.MasterEffectsEnabled)
            {
                verdicts[i] = new AiCommandVerdict.ConsentGated("master");
            }
            else if (!gates.IsEffectAllowed(command.Kind))
            {
                verdicts[i] = new AiCommandVerdict.ConsentGated(command.Kind.ToString());
            }
            else if (!_handlers.TryGetValue(command.Kind, out var handler))
            {
                verdicts[i] = new AiCommandVerdict.NotExecuted(AiNotExecutedReason.EffectUnavailable);
            }
            else
            {
                handler.Execute(command);
                verdicts[i] = AiCommandVerdict.Valid.Instance;
            }
        }

        return new AiCommandExecution(verdicts);
    }
}
