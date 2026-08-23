using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The bridge from the c6 executor to the LANDED effect rack, and the permission state the grid
/// writes. Every fact below runs against REAL product effect objects — a real
/// <see cref="SpiralOverlayEffect"/>, <see cref="PinkFilterEffect"/>, <see cref="BubblePopEffect"/>
/// and <see cref="BouncingTextEffect"/> over real persisted stores — with NO surface composed, so
/// what is proved is the dial, the arm and the typed refusal, never a pixel.
/// </summary>
public class AiEffectBridgeTests
{
    // ---- the default is closed, and it stays closed ----

    /// <summary>
    /// The default-closed fact. <see cref="AiEffectPermissions.NoneAdmitted"/> admits NOTHING:
    /// master off and every kind refused, enumerated off the enum so a kind added later cannot
    /// arrive pre-admitted. Upstream ships bubbles/subliminal/bounce pre-ticked
    /// (<c>Models/CompanionPromptSettings.cs:103-110</c>); this port does not, and that divergence
    /// is the one this fact defends.
    /// </summary>
    [Fact]
    public void TheDefaultAdmitsNothing_MasterOff_AndEveryKindRefused()
    {
        var permissions = AiEffectPermissions.NoneAdmitted;

        Assert.False(permissions.MasterEnabled);
        foreach (var kind in Enum.GetValues<AiCommandKind>())
        {
            Assert.False(permissions.IsAllowed(kind));
        }

        foreach (var row in AiEffectPermissions.Rows)
        {
            Assert.False(permissions.IsRowAllowed(row));
        }
    }

    /// <summary>A fresh participant starts closed too — the default is the participant's, not just the type's.</summary>
    [Fact]
    public void AFreshCompanionParticipantStartsClosed()
    {
        using var lab = new Rack();
        var participant = lab.Companion();

        Assert.Same(AiEffectPermissions.NoneAdmitted, participant.Permissions);
        Assert.False(participant.Permissions.MasterEnabled);
        Assert.All(Enum.GetValues<AiCommandKind>(), kind => Assert.False(participant.Permissions.IsAllowed(kind)));
    }

    /// <summary>
    /// The whole point of the default, proved through the real chain: an envelope asking for a
    /// spiral is validated, reaches the executor with the real bridge behind it, and the REAL
    /// spiral module never moves. Not a canary — the module's own dial, its enable flag and its
    /// generation.
    /// </summary>
    [Fact]
    public void UnderTheDefault_ASpiralEnvelopeMovesNothingOnTheRealModule()
    {
        using var lab = new Rack();
        var permissions = AiEffectPermissions.NoneAdmitted;

        // Arrangement: the user has her spiral switched off, so the module's enable flag is a
        // real discriminator here (its product default is ON — SpiralPresetDocument.cs:54,
        // upstream's own default).
        lab.Spiral.SetEnabled(false);

        // The validator gates it first, on the same state. The envelope PARSES — accepted is a
        // schema verdict, not a consent one — and zero commands reach the plan.
        var gated = AiEnvelopeValidator.Validate(
            SpiralJson, new AiEnvelopePolicy(permissions.MasterEnabled, permissions.IsAllowed, _ => AiModerationVerdict.Pass.Instance));
        Assert.True(gated.Accepted);
        Assert.Empty(gated.Plan!.Commands);
        Assert.Equal("master", Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(gated.Verdicts)).Toggle);

        // And so does the dispatch re-gate, which is the one that matters if consent changed
        // between validation and dispatch (contract §8 rule 6).
        var plan = lab.Plan(SpiralJson);
        var execution = lab.Executor.Execute(plan, lab.Gates(permissions));

        Assert.Equal("master", Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(execution.Verdicts)).Toggle);
        Assert.False(lab.Spiral.Enabled);
        Assert.Equal(SpiralPresetDocument.DefaultOpacityPercent, lab.Spiral.Presentation.OpacityPercent);
        Assert.Null(lab.Spiral.Completion); // never armed: no generation was ever begun
        Assert.Equal(EffectDotState.Off, lab.Spiral.Dot);
    }

    /// <summary>Master ON but the overlay row unticked is still nothing, named by the effect rather than by "master".</summary>
    [Fact]
    public void MasterOnAloneIsNotConsent_TheOverlayRowStillRefusesByName()
    {
        using var lab = new Rack();
        var permissions = AiEffectPermissions.NoneAdmitted.WithMaster(true);
        lab.Spiral.SetEnabled(false); // as above: the enable flag's product default is ON

        var execution = lab.Executor.Execute(lab.Plan(SpiralJson), lab.Gates(permissions));

        Assert.Equal(
            nameof(AiCommandKind.Spiral),
            Assert.IsType<AiCommandVerdict.ConsentGated>(Assert.Single(execution.Verdicts)).Toggle);
        Assert.False(lab.Spiral.Enabled);
        Assert.Null(lab.Spiral.Completion);
    }

    /// <summary>Unticking the master REMEMBERS the ticks, which is upstream's behaviour (<c>MainWindow/MainWindow.Patreon.cs:1476-1478</c> writes only the master flag and hides the panel).</summary>
    [Fact]
    public void TheMasterSwitchHidesTheTicksItDoesNotClearThem()
    {
        var overlay = AiEffectPermissions.Rows.Single(r => r.Id == "Overlay");
        var permissions = AiEffectPermissions.NoneAdmitted.WithMaster(true).WithRow(overlay, true);

        var off = permissions.WithMaster(false);
        Assert.False(off.MasterEnabled);
        Assert.True(off.IsRowAllowed(overlay)); // remembered

        var backOn = off.WithMaster(true);
        Assert.True(backOn.IsRowAllowed(overlay));
    }

    // ---- the grid's own coverage ----

    /// <summary>
    /// Every command kind is on exactly one row, so there is no kind the user cannot see or
    /// change. Ten rows for eleven kinds because upstream's OVERLAY switch governs both overlays
    /// (<c>Services/Commands/AiCommandService.cs:186-187</c>).
    /// </summary>
    [Fact]
    public void EveryCommandKindSitsOnExactlyOneRow()
    {
        var seen = AiEffectPermissions.Rows.SelectMany(r => r.Kinds).ToList();

        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(Enum.GetValues<AiCommandKind>().OrderBy(k => k), seen.OrderBy(k => k));
        Assert.Equal(10, AiEffectPermissions.Rows.Count);
        Assert.Equal(
            [AiCommandKind.Spiral, AiCommandKind.Pink],
            AiEffectPermissions.Rows.Single(r => r.Id == "Overlay").Kinds);
    }

    /// <summary>
    /// The honesty rule for the whole bridge: over a real rack, every kind either has a handler
    /// or a NAMED absence, and never both and never neither. A kind quietly dropped from both
    /// tables is the "nothing happens and nobody says why" defect this slice exists to remove.
    /// </summary>
    [Fact]
    public void EveryKindEitherHasAHandlerOrANamedAbsence_NeverBothNeverNeither()
    {
        using var lab = new Rack();
        var handlers = AiEffectBridge.HandlersFor(lab.Effects);

        foreach (var kind in Enum.GetValues<AiCommandKind>())
        {
            var handled = handlers.ContainsKey(kind);
            var named = AiEffectBridge.Absences.ContainsKey(kind);
            Assert.True(handled != named, $"{kind} is {(handled ? "both handled and named absent" : "neither handled nor named absent")}");
            if (named)
            {
                Assert.NotEmpty(AiEffectBridge.Absences[kind].Code);
                Assert.NotEmpty(AiEffectBridge.Absences[kind].Detail);
            }
        }

        Assert.Equal(
            [AiCommandKind.Bubbles, AiCommandKind.Spiral, AiCommandKind.Pink],
            handlers.Keys.OrderBy(k => k));
    }

    // ---- admitted commands really drive the real modules ----

    /// <summary>
    /// Admitted, and the REAL spiral module moves: the opacity the envelope carried is on the
    /// module's own dial, its enable flag is set and it took a generation. Upstream's
    /// <c>SpiralCommand.cs:26-32</c> writes exactly those two settings and starts the overlay.
    /// </summary>
    [Fact]
    public void AnAdmittedSpiralPutsTheRealModuleUpAtTheOpacityItAskedFor()
    {
        using var lab = new Rack();
        lab.Spiral.SetEnabled(false); // she is turning it ON from off, not finding it already on

        var execution = lab.Executor.Execute(lab.Plan(SpiralJson), lab.Gates(lab.Admitting("Overlay")));

        Assert.IsType<AiCommandVerdict.Valid>(Assert.Single(execution.Verdicts));
        Assert.True(lab.Spiral.Enabled);
        Assert.Equal(20, lab.Spiral.Presentation.OpacityPercent);
        Assert.NotNull(lab.Spiral.Completion);
        Assert.False(lab.Spiral.Completion!.IsCompleted); // the generation is live
        Assert.Equal(EffectDotState.Armed, lab.Spiral.Dot); // no surface composed, so never Live
    }

    /// <summary>
    /// Off takes the layer down and LEAVES THE GENERATION ALIVE, because that is what upstream
    /// does: <c>PinkCommand.cs:78</c> clears the flag and <c>RefreshOverlays()</c> drops the layer
    /// while the overlay service keeps running (<c>OverlayService.cs:421-437</c>). Disarming here
    /// would cancel the module's generation, which is what STOP means in this port — a different
    /// user-visible thing.
    /// </summary>
    [Fact]
    public void AnAdmittedPinkOffDropsTheLayerWithoutCancellingTheGeneration()
    {
        using var lab = new Rack();
        var permissions = lab.Admitting("Overlay");

        lab.Executor.Execute(lab.Plan(PinkOnJson), lab.Gates(permissions));
        Assert.True(lab.Pink.Enabled);
        var completion = lab.Pink.Completion;
        Assert.NotNull(completion);

        lab.Executor.Execute(lab.Plan(PinkOffJson), lab.Gates(permissions));

        Assert.False(lab.Pink.Enabled);
        Assert.Equal(EffectDotState.Off, lab.Pink.Dot);
        Assert.Same(completion, lab.Pink.Completion);
        Assert.False(completion!.IsCompleted); // still the same live generation, not a stop
    }

    /// <summary>
    /// Upstream's tolerant intent reading, kept (<c>Services/Commands/BubbleCommand.cs:20-23</c>):
    /// a frequency above zero MEANS start even when the model forgot <c>on</c>.
    /// </summary>
    [Fact]
    public void BubblesWithAFrequencyStartEvenWhenTheModelForgotTheOnFlag()
    {
        using var lab = new Rack();

        var execution = lab.Executor.Execute(lab.Plan(BubblesOffButFrequentJson), lab.Gates(lab.Admitting("Bubbles")));

        Assert.IsType<AiCommandVerdict.Valid>(Assert.Single(execution.Verdicts));
        Assert.True(lab.Bubbles.Enabled);
        Assert.Equal(7, lab.Bubbles.Settings.PerMinute);
        Assert.NotNull(lab.Bubbles.Completion);
    }

    /// <summary>
    /// The other half of that reading: a zero frequency leaves the user's own spawn rate alone.
    /// Upstream passes <c>null</c> rather than zero (<c>BubbleCommand.cs:32</c>), and this build's
    /// rate dial floors at 1/min (<c>Effects/BubblePopField.cs:123</c>), so writing the zero would
    /// invent a rate the user never chose.
    /// </summary>
    [Fact]
    public void BubblesWithNoFrequencyLeaveTheUsersOwnSpawnRateAlone()
    {
        using var lab = new Rack();
        lab.Bubbles.SetPerMinute(4);

        lab.Executor.Execute(lab.Plan(BubblesOnNoFrequencyJson), lab.Gates(lab.Admitting("Bubbles")));

        Assert.True(lab.Bubbles.Enabled);
        Assert.Equal(4, lab.Bubbles.Settings.PerMinute);
    }

    // ---- and the refusals are typed, not silent ----

    /// <summary>
    /// The refusal that cost the most to decide. Bounce on/off IS expressible here, but the
    /// command may carry WORDS (<c>BounceCommand.cs:122</c> passes them to <c>Start</c>) and this
    /// build's module draws the user's own configured phrases with no phrase setter. Half-applying
    /// — bouncing the user's words while she asked for hers — is the silent defect this slice
    /// exists to remove, so the kind is refused whole, by name, and the real module never arms.
    /// </summary>
    [Fact]
    public void BounceIsRefusedByNameRatherThanHalfApplied()
    {
        using var lab = new Rack();
        var permissions = lab.Admitting("Bounce");

        var execution = lab.Executor.Execute(lab.Plan(BounceJson), lab.Gates(permissions));

        var notExecuted = Assert.IsType<AiCommandVerdict.NotExecuted>(Assert.Single(execution.Verdicts));
        Assert.Equal(AiNotExecutedReason.EffectUnavailable, notExecuted.Reason);
        Assert.False(lab.BouncingText.Enabled);
        Assert.Null(lab.BouncingText.Completion);
        Assert.Equal("ai-effect-no-caller-supplied-phrase", AiEffectBridge.Absences[AiCommandKind.Bounce].Code);
    }

    /// <summary>A participant given no rack keeps exactly the behaviour it had before the bridge existed: every admitted kind is EffectUnavailable, nothing pretends.</summary>
    [Fact]
    public void WithNoRack_EveryAdmittedKindIsStillEffectUnavailable()
    {
        using var lab = new Rack();
        var bare = lab.Companion(withRack: false);

        foreach (var kind in Enum.GetValues<AiCommandKind>())
        {
            Assert.False(bare.Executor.Handles(kind));
        }

        var execution = bare.Executor.Execute(lab.Plan(SpiralJson), lab.Gates(lab.Admitting("Overlay")));
        Assert.Equal(
            AiNotExecutedReason.EffectUnavailable,
            Assert.IsType<AiCommandVerdict.NotExecuted>(Assert.Single(execution.Verdicts)).Reason);
    }

    /// <summary>A participant given the rack reports the three backed kinds through the same member the grid reads.</summary>
    [Fact]
    public void AParticipantGivenTheRackReportsExactlyTheThreeBackedKinds()
    {
        using var lab = new Rack();
        var participant = lab.Companion();

        Assert.True(participant.Executor.Handles(AiCommandKind.Spiral));
        Assert.True(participant.Executor.Handles(AiCommandKind.Pink));
        Assert.True(participant.Executor.Handles(AiCommandKind.Bubbles));
        foreach (var kind in AiEffectBridge.Absences.Keys)
        {
            Assert.False(participant.Executor.Handles(kind));
        }
    }

    // =====================================================================================

    private const string SpiralJson = """{"commands":[{"command":"spiral","data":{"on":true,"intensity":20}}]}""";
    private const string PinkOnJson = """{"commands":[{"command":"pink","data":{"on":true,"intensity":12}}]}""";
    private const string PinkOffJson = """{"commands":[{"command":"pink","data":{"on":false,"intensity":12}}]}""";
    private const string BubblesOffButFrequentJson = """{"commands":[{"command":"bubbles","data":{"on":false,"frequency":7}}]}""";
    private const string BubblesOnNoFrequencyJson = """{"commands":[{"command":"bubbles","data":{"on":true,"frequency":0}}]}""";
    private const string BounceJson = """{"commands":[{"command":"bounce","data":{"on":true,"words":"good girl"}}]}""";

    /// <summary>
    /// Four REAL rack modules over real persisted stores, with no surface composed anywhere, plus
    /// the real executor over the real bridge and a real operation generation.
    /// </summary>
    private sealed class Rack : IDisposable
    {
        private readonly string _dir;
        private readonly OperationRegistry _registry = new();
        private readonly AsyncOperationOwner _owner;
        private readonly int _generation;

        public Rack()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ccp-ai-bridge-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var boundary = new UiDispatchBoundary();
            var signal = new EffectSignal(boundary, static () => true);

            Spiral = new SpiralOverlayEffect(
                _registry.OwnerFor("BridgeSpiral"), signal,
                Store<SpiralPresetDocument>(SpiralPresetDocument.FileName, SpiralPresetDocument.CurrentSchemaVersion),
                () => @"C:\spirals\classic.gif");
            Pink = new PinkFilterEffect(
                _registry.OwnerFor("BridgePink"), signal,
                Store<PinkFilterPresetDocument>(PinkFilterPresetDocument.FileName, PinkFilterPresetDocument.CurrentSchemaVersion));
            Bubbles = new BubblePopEffect(
                _registry.OwnerFor("BridgeBubbles"), signal,
                Store<BubblePopPresetDocument>(BubblePopPresetDocument.FileName, BubblePopPresetDocument.CurrentSchemaVersion));
            BouncingText = new BouncingTextEffect(
                _registry.OwnerFor("BridgeBounce"), signal,
                Store<BouncingTextPresetDocument>(BouncingTextPresetDocument.FileName, BouncingTextPresetDocument.CurrentSchemaVersion));

            Effects = [Spiral, Pink, Bubbles, BouncingText];
            Executor = new AiCommandExecutor(AiEffectBridge.HandlersFor(Effects));
            _owner = _registry.OwnerFor("BridgeDispatch");
            _generation = _owner.Begin();
        }

        public SpiralOverlayEffect Spiral { get; }

        public PinkFilterEffect Pink { get; }

        public BubblePopEffect Bubbles { get; }

        public BouncingTextEffect BouncingText { get; }

        public IReadOnlyList<ISessionEffect> Effects { get; }

        public AiCommandExecutor Executor { get; }

        /// <summary>A real participant, with or without the rack the composition root would hand it.</summary>
        public CcpClient.Desktop.Features.Companion.CompanionParticipant Companion(bool withRack = true) =>
            new(new ParticipantInfrastructure(_registry, new UiDispatchBoundary(), new NullSink()),
                new CcpClient.Desktop.Capabilities.CapabilityRegistry(),
                _dir,
                effects: withRack ? Effects : null);

        /// <summary>Validation is not the subject here: every plan is built under PermitAll so the EXECUTION gate is the only thing under test.</summary>
        public AiExecutionPlan Plan(string json)
        {
            var result = AiEnvelopeValidator.Validate(json, AiEnvelopePolicy.PermitAll);
            Assert.True(result.Accepted);
            return result.Plan!;
        }

        /// <summary>The execution gates built from a permission state — the SAME two members the grid writes.</summary>
        public AiExecutionGates Gates(AiEffectPermissions permissions) =>
            new(permissions.MasterEnabled, permissions.IsAllowed, _generation, _owner.IsLive);

        public AiEffectPermissions Admitting(string rowId) =>
            AiEffectPermissions.NoneAdmitted
                .WithMaster(true)
                .WithRow(AiEffectPermissions.Rows.Single(r => r.Id == rowId), true);

        public void Dispose()
        {
            Spiral.Disarm();
            Pink.Disarm();
            Bubbles.Disarm();
            BouncingText.Disarm();
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a test failure.
            }
        }

        private PersistenceStore<T> Store<T>(string fileName, int schemaVersion)
            where T : class, new() =>
            new(_registry.OwnerFor("BridgeStore." + fileName), new NullSink(), Path.Combine(_dir, fileName), schemaVersion);
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }
}
