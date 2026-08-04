using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// c6 command-execution tests (SP-044; admission §8 c6; contract §8 rule 6, §9): envelope →
/// plan → gated dispatch. Proves: consent gates post-validation and typed; the none-admitted
/// default (deliberate WPF divergence); canary zero-execution on every rejected/gated/stale
/// class; moderation pre-execution through the shipped ForBoundary factory; and
/// NotExecuted(SupersededGeneration) at EXECUTION level (SP-019 limit 7) against the REAL
/// AsyncOperationOwner generation machinery.
/// </summary>
public class AiCommandExecutorTests
{
    /// <summary>The canary handler (test-side): records every command it is handed. Silent on every rejected/gated/stale class — the falsifiable zero-execution instrument.</summary>
    private sealed class Canary : IAiEffectHandler
    {
        private readonly List<AiCommandKind> _invocations = [];

        public IReadOnlyList<AiCommandKind> Invocations => _invocations;

        public Action? OnExecute { get; set; }

        public void Execute(AiCommand command)
        {
            _invocations.Add(command.Kind);
            OnExecute?.Invoke();
        }
    }

    private static readonly OperationRegistry Registry = new();

    private static (AsyncOperationOwner Owner, int Generation) LiveGeneration()
    {
        var owner = Registry.OwnerFor($"AiExecutorTests.{Guid.NewGuid():N}");
        var generation = owner.Begin();
        return (owner, generation);
    }

    private static AiEnvelopePolicy Policy(bool master, Func<AiCommandKind, bool>? perEffect = null, Func<string, AiModerationVerdict>? moderate = null) =>
        new(master, perEffect ?? (_ => true), moderate ?? (_ => AiModerationVerdict.Pass.Instance));

    private static AiEnvelopeResult Validate(string json, AiEnvelopePolicy policy) =>
        AiEnvelopeValidator.Validate(json, policy);

    private const string BubblesJson = """{"commands":[{"command":"bubbles","data":{"on":true,"frequency":5}}]}""";

    private const string TwoCommandJson =
        """{"commands":[{"command":"bubbles","data":{"on":true,"frequency":5}},{"command":"subliminal","data":{"text":"ok","opacity":10}}]}""";

    // ---- none-admitted default + consent gates (post-validation, typed) ----

    [Fact]
    public void NoneAdmitted_Default_MasterGatesEverything_CanarySilent()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(BubblesJson, Policy(master: true));
        Assert.True(result.Accepted);
        Assert.NotNull(result.Plan);

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
            { [AiCommandKind.Bubbles] = canary });

        // The pending-owner default (admission §9.2 #5): NOTHING admitted — master OFF,
        // zero per-effect allowances. Deliberate divergence from the WPF baseline
        // (bubbles/subliminal/bounce ON), recorded verbatim in the executor doc.
        var execution = executor.Execute(result.Plan!, AiExecutionGates.NoneAdmitted(generation, owner.IsLive));

        var gated = Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(execution.Verdicts));
        Assert.Equal("master", gated.Toggle);
        Assert.Empty(canary.Invocations);
    }

    [Fact]
    public void MasterOn_NoPerEffectAllowance_ConsentGatedPerKind_CanarySilent()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(TwoCommandJson, Policy(master: true));
        Assert.True(result.Accepted);

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
        {
            [AiCommandKind.Bubbles] = canary,
            [AiCommandKind.Subliminal] = canary,
        });

        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(true, _ => false, generation, owner.IsLive));

        Assert.Equal(2, execution.Verdicts.Count);
        Assert.Equal("Bubbles", Assert.IsType<AiCommandVerdict.ConsentGated>(execution.Verdicts[0]).Toggle);
        Assert.Equal("Subliminal", Assert.IsType<AiCommandVerdict.ConsentGated>(execution.Verdicts[1]).Toggle);
        Assert.Empty(canary.Invocations);
    }

    [Fact]
    public void Subset_Admitted_ExecutesInOrder_OthersGated_VerdictOrderMatchesPlan()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(TwoCommandJson, Policy(master: true));
        Assert.True(result.Accepted);

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
            { [AiCommandKind.Bubbles] = canary });

        var gates = new AiExecutionGates(true, k => k == AiCommandKind.Bubbles, generation, owner.IsLive);
        var execution = executor.Execute(result.Plan!, gates);

        Assert.Equal(2, execution.Verdicts.Count);
        // Valid in an EXECUTION result means dispatched.
        Assert.IsType<AiCommandVerdict.Valid>(execution.Verdicts[0]);
        Assert.Equal("Subliminal", Assert.IsType<AiCommandVerdict.ConsentGated>(execution.Verdicts[1]).Toggle);
        Assert.Equal([AiCommandKind.Bubbles], canary.Invocations);
    }

    [Fact]
    public void Admitted_KindWithoutHandler_NotExecutedEffectUnavailable()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(BubblesJson, Policy(master: true));
        Assert.True(result.Accepted);

        // No handlers registered: the typed placeholder — no effect backends exist.
        var executor = new AiCommandExecutor();
        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(true, _ => true, generation, owner.IsLive));

        var notExecuted = Assert.IsType<AiCommandVerdict.NotExecuted>(Assert.Single(execution.Verdicts));
        Assert.Equal(AiNotExecutedReason.EffectUnavailable, notExecuted.Reason);
    }

    [Fact]
    public void ConsentFlip_BetweenValidationAndDispatch_GatedAtExecution_CanarySilent()
    {
        var (owner, generation) = LiveGeneration();
        // Validated under master-ON (validator's own Phase-2 gate passed; plan carries the command).
        var result = Validate(BubblesJson, Policy(master: true));
        Assert.True(result.Accepted);
        Assert.Single(result.Plan!.Commands);

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
            { [AiCommandKind.Bubbles] = canary });

        // Consent changed before dispatch: gates are re-evaluated at execution (contract §8
        // rule 6 — post-validation), typed, never partially applied.
        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(false, _ => true, generation, owner.IsLive));

        Assert.Equal("master", Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(execution.Verdicts)).Toggle);
        Assert.Empty(canary.Invocations);
    }

    [Fact]
    public void FromPolicy_DerivesGatesFromTheSamePolicyInstance()
    {
        var (owner, generation) = LiveGeneration();
        var policy = Policy(master: true, perEffect: k => k == AiCommandKind.Bubbles);
        var gates = AiExecutionGates.FromPolicy(policy, generation, owner.IsLive);

        Assert.True(gates.MasterEffectsEnabled);
        Assert.True(gates.IsEffectAllowed(AiCommandKind.Bubbles));
        Assert.False(gates.IsEffectAllowed(AiCommandKind.Subliminal));
        Assert.Equal(generation, gates.Generation);
    }

    // ---- superseded generation at execution level (SP-019 limit 7) ----

    [Fact]
    public void StaleGeneration_BeforeDispatch_EveryCommandSuperseded_CanarySilent()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(TwoCommandJson, Policy(master: true));
        Assert.True(result.Accepted);
        Assert.Equal(2, result.Plan!.Commands.Count);

        // Provider switch between plan and dispatch: generation invalidation (contract §3).
        owner.Begin();

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
        {
            [AiCommandKind.Bubbles] = canary,
            [AiCommandKind.Subliminal] = canary,
        });

        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(true, _ => true, generation, owner.IsLive));

        Assert.Equal(2, execution.Verdicts.Count);
        Assert.All(execution.Verdicts, v =>
            Assert.Equal(AiNotExecutedReason.SupersededGeneration,
                Assert.IsType<AiCommandVerdict.NotExecuted>(v).Reason));
        Assert.Empty(canary.Invocations);
    }

    [Fact]
    public void MidDispatch_GenerationFlip_RemainingCommandsSuperseded_NeverALateApply()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(TwoCommandJson, Policy(master: true));
        Assert.True(result.Accepted);

        var canary = new Canary();
        // The first command's dispatch flips the generation (provider switch mid-dispatch).
        canary.OnExecute = () => owner.Begin();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
        {
            [AiCommandKind.Bubbles] = canary,
            [AiCommandKind.Subliminal] = canary,
        });

        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(true, _ => true, generation, owner.IsLive));

        Assert.Equal(2, execution.Verdicts.Count);
        Assert.IsType<AiCommandVerdict.Valid>(execution.Verdicts[0]);
        Assert.Equal(AiNotExecutedReason.SupersededGeneration,
            Assert.IsType<AiCommandVerdict.NotExecuted>(execution.Verdicts[1]).Reason);
        Assert.Equal([AiCommandKind.Bubbles], canary.Invocations);
    }

    // ---- zero-execution on every rejected class + valid-sibling verdicts ----

    [Theory]
    [InlineData("""not json at all""")]
    [InlineData("""{"commands":[{"command":"nope","data":{}}]}""")]
    [InlineData("""{"commands":[{"command":"bubbles","data":{"on":true,"frequency":99}}]}""")]
    [InlineData("""{"commands":[{"command":"bubbles","data":{"on":true,"frequency":5}},{"command":"subliminal","data":{"text":"ok","opacity":999}}]}""")]
    public void RejectedClasses_NoPlan_ExecutorUnreachable(string json)
    {
        var result = Validate(json, Policy(master: true));

        Assert.False(result.Accepted);
        // Atomic envelope semantics (SP-016/SP-019): an invalid envelope has NO executable
        // representation — the executor cannot be invoked on it (type-enforced, internal ctor).
        Assert.Null(result.Plan);
    }

    [Fact]
    public void MixedEnvelope_ValidSiblings_NotExecutedEnvelopeRejected()
    {
        var result = Validate(
            """{"commands":[{"command":"bubbles","data":{"on":true,"frequency":5}},{"command":"subliminal","data":{"text":"ok","opacity":999}}]}""",
            Policy(master: true));

        Assert.False(result.Accepted);
        Assert.Equal("command-invalid", result.EnvelopeRejectionCode);
        Assert.Equal(2, result.Verdicts.Count);
        // The schema-valid sibling is typed, never silently dropped and never executed.
        Assert.Equal(AiNotExecutedReason.EnvelopeRejected,
            Assert.IsType<AiCommandVerdict.NotExecuted>(result.Verdicts[0]).Reason);
        Assert.IsType<AiCommandVerdict.OutOfRange>(result.Verdicts[1]);
    }

    // ---- moderation pre-execution through the shipped ForBoundary factory ----

    [Fact]
    public void ModeratedField_ThroughForBoundary_TypedRefusalAtEnvelope_ZeroDispatch()
    {
        var policy = new AiModerationPolicy(
            [new AiModerationRule("test-block", AiModerationAction.Block, ["forbidden-token"])]);
        var boundary = new AiModerationBoundary(policy);

        // The PRODUCT composition point (c3): the boundary wired into validation, consumed
        // never re-implemented.
        var envelopePolicy = AiEnvelopePolicy.ForBoundary(boundary, masterEffectsEnabled: true, _ => true);
        var result = Validate(
            """{"commands":[{"command":"subliminal","data":{"text":"forbidden-token","opacity":10}}]}""",
            envelopePolicy);

        Assert.True(result.Accepted); // envelope shape valid; the COMMAND is moderation-blocked
        var blocked = Assert.IsType<AiCommandVerdict.ModerationBlocked>(Assert.Single(result.Verdicts));
        Assert.Equal("test-block", blocked.CategoryCode);
        // Zero dispatch: a moderation-blocked command never enters the plan.
        Assert.Empty(result.Plan!.Commands);

        var (owner, generation) = LiveGeneration();
        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
            { [AiCommandKind.Subliminal] = canary });
        var execution = executor.Execute(result.Plan!,
            AiExecutionGates.FromPolicy(envelopePolicy, generation, owner.IsLive));
        Assert.Empty(execution.Verdicts);
        Assert.Empty(canary.Invocations);
    }

    // ---- cap overflow round-trip ----

    [Fact]
    public void CapOverflow_FourthCommandNotExecutedCapExceeded_DispatchExecutesExactlyThree()
    {
        var (owner, generation) = LiveGeneration();
        const string json =
            """{"commands":[{"command":"bubbles","data":{"on":true,"frequency":1}},{"command":"bubbles","data":{"on":true,"frequency":2}},{"command":"bubbles","data":{"on":true,"frequency":3}},{"command":"bubbles","data":{"on":true,"frequency":4}}]}""";
        var result = Validate(json, Policy(master: true));

        Assert.True(result.Accepted);
        Assert.Equal(4, result.Verdicts.Count);
        Assert.Equal(AiNotExecutedReason.CapExceeded,
            Assert.IsType<AiCommandVerdict.NotExecuted>(result.Verdicts[3]).Reason);
        Assert.Equal(3, result.Plan!.Commands.Count);

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
            { [AiCommandKind.Bubbles] = canary });
        var execution = executor.Execute(result.Plan!,
            new AiExecutionGates(true, _ => true, generation, owner.IsLive));

        Assert.Equal(3, execution.Verdicts.Count);
        Assert.All(execution.Verdicts, v => Assert.IsType<AiCommandVerdict.Valid>(v));
        Assert.Equal(3, canary.Invocations.Count);
    }

    // ---- verdict round-trip: envelope JSON → validation verdicts → execution verdicts ----

    [Fact]
    public void VerdictRoundTrip_EndToEnd_FullPerCommandSequence()
    {
        var (owner, generation) = LiveGeneration();
        var result = Validate(TwoCommandJson, Policy(master: true));
        Assert.True(result.Accepted);
        Assert.All(result.Verdicts, v => Assert.IsType<AiCommandVerdict.Valid>(v));

        var canary = new Canary();
        var executor = new AiCommandExecutor(new Dictionary<AiCommandKind, IAiEffectHandler>
        {
            [AiCommandKind.Bubbles] = canary,
            [AiCommandKind.Subliminal] = canary,
        });

        // Dispatch through gates derived from the SAME policy (single consent source).
        var policy = Policy(master: true);
        var execution = executor.Execute(result.Plan!, AiExecutionGates.FromPolicy(policy, generation, owner.IsLive));

        Assert.Equal(result.Plan!.Commands.Count, execution.Verdicts.Count);
        Assert.All(execution.Verdicts, v => Assert.IsType<AiCommandVerdict.Valid>(v));
        Assert.Equal([AiCommandKind.Bubbles, AiCommandKind.Subliminal], canary.Invocations);
    }
}
