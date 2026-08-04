using System.Reflection;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Coverage-honesty tests (SP-038 slice c3; admission §8 c3): the boundary-coverage tests
/// ENUMERATE the §3 surface/command-field inventory. Every Wired row has an executable
/// assertion through the real pipeline/validator (input side AND output side where the
/// surface exists); every Reserved row asserts the seam is named and typed, NEVER a
/// coverage claim. Completeness tripwires (SP-009-sweep-class): a new pipeline entry
/// point, a new string-carrying command-data variant, or a new Wired inventory row
/// without an assertion FAILS this suite. Also: session-scoped escalation state behavior,
/// offline zero-network re-verified, content-free diagnostics maintained.
/// </summary>
public class AiModerationCoverageTests
{
    private const string Forbidden = "forbidden-token";

    private static readonly AiModerationPolicy Policy = new(
        [new AiModerationRule("test-block-category", AiModerationAction.Block, [Forbidden])]);

    private sealed class StubProvider : IAiProvider
    {
        public AiReply Reply { get; set; } = new AiReply.Generated("clean reply", AiEndpointClass.Loopback);
        public int Calls;
        public AiProviderDescriptor Descriptor { get; } = new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);
        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("stub-probe"));
        public Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(Reply);
        }
    }

    private sealed class Harness
    {
        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public CollectingAiDiagnosticsSink Diagnostics { get; } = new();
        public AiModerationBoundary Boundary { get; }
        public AiOperationPipeline Pipeline { get; }
        public StubProvider Provider { get; } = new();

        public Harness(AiModerationPolicy policy)
        {
            Boundary = new AiModerationBoundary(policy);
            Pipeline = new AiOperationPipeline(Registry, Capabilities, LoopbackOnlyAdmissionPolicy.Instance, Diagnostics, Boundary);
        }

        public async Task AdmitProviderAsync()
        {
            Pipeline.RegisterProvider(Provider);
            Pipeline.SelectProvider(AiProviderId.LocalOllama);
            var runner = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await runner.RunAllAsync(CancellationToken.None);
        }
    }

    // ---- the inventory as executable assertions (one arm per Wired row) ----

    [Fact]
    public async Task Inventory_EveryWiredSurface_HasAnExecutableAssertion_EveryReservedSurface_NamesItsSeam()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        foreach (var surface in AiModerationSurfaces.All)
        {
            if (surface.Disposition == AiModerationSurfaceDisposition.Reserved)
            {
                // Reserved is TYPED, never implied: the seam that will carry the future
                // surface is named; no coverage is claimed.
                Assert.False(string.IsNullOrWhiteSpace(surface.ReservedFor), $"reserved surface '{surface.Id}' must name its seam");
                Assert.Null(surface.OperationEntryPoint);
                continue;
            }

            switch (surface.Id)
            {
                case "interactive-chat-input":
                {
                    var result = await h.Pipeline.RunInteractiveAsync(new AiRequest(Forbidden));
                    var refused = Assert.IsType<AiReply.Refused>(result.Reply);
                    Assert.Equal(AiModerationSource.Input, refused.Refusal.Source);
                    break;
                }
                case "awareness-operation-input":
                {
                    var result = await h.Pipeline.RunAwarenessAsync(new AiRequest(Forbidden), awarenessConsent: true);
                    var refused = Assert.IsType<AiReply.Refused>(result.Reply);
                    Assert.Equal(AiModerationSource.Input, refused.Refusal.Source);
                    break;
                }
                case "interactive-reply-output":
                {
                    h.Provider.Reply = new AiReply.Generated(Forbidden, AiEndpointClass.Loopback);
                    var result = await h.Pipeline.RunInteractiveAsync(new AiRequest("clean"));
                    var refused = Assert.IsType<AiReply.Refused>(result.Reply);
                    Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
                    break;
                }
                case "awareness-reply-output":
                {
                    h.Provider.Reply = new AiReply.Generated(Forbidden, AiEndpointClass.Loopback);
                    var result = await h.Pipeline.RunAwarenessAsync(new AiRequest("clean"), awarenessConsent: true);
                    var refused = Assert.IsType<AiReply.Refused>(result.Reply);
                    Assert.Equal(AiModerationSource.Output, refused.Refusal.Source);
                    break;
                }
                case "awareness-context-fields":
                {
                    // c5 packaging (SP-042): EVERY field through EvaluateInput on this
                    // surface pre-assembly; a blocking verdict on any field means nothing
                    // transmittable exists. The Wired flip landed in c6 (SP-044).
                    var blocked = AiAwarenessContextPackaging.TryPackage(
                        new AiAwarenessContext("cat", Forbidden, "title", "5s"),
                        h.Boundary, out var blockedRequest, out var refusal);
                    Assert.False(blocked);
                    Assert.Null(blockedRequest);
                    Assert.NotNull(refusal);
                    Assert.Equal(AiModerationSource.Input, refusal.Source);

                    var clean = AiAwarenessContextPackaging.TryPackage(
                        new AiAwarenessContext("cat", "app", "title", "5s"),
                        h.Boundary, out var cleanRequest, out _);
                    Assert.True(clean);
                    Assert.NotNull(cleanRequest);
                    break;
                }
                case "command-free-text":
                {
                    // Every free-text command field pre-execution (contract §7 rule 2),
                    // through the real validator composed with the boundary via the
                    // PRODUCT composition factory (ForBoundary — the shape c6 consumes).
                    var envelopePolicy = AiEnvelopePolicy.ForBoundary(h.Boundary, masterEffectsEnabled: true, _ => true);
                    string[] envelopes =
                    [
                        """{"commands":[{"command":"subliminal","data":{"text":"forbidden-token","opacity":10}}]}""",
                        """{"commands":[{"command":"mantra_lockscreen","data":{"mantra":"forbidden-token","amount":1}}]}""",
                        """{"commands":[{"command":"bounce","data":{"on":true,"words":"forbidden-token"}}]}""",
                        """{"commands":[{"command":"video","data":{"path":"forbidden-token.mp4","random":false}}]}""",
                        """{"commands":[{"command":"getbacktome","data":{"token":"tok","delay":5,"jsonOnly":false,"text":"forbidden-token"}}]}""",
                    ];
                    foreach (var json in envelopes)
                    {
                        var result = AiEnvelopeValidator.Validate(json, envelopePolicy);
                        var verdict = Assert.Single(result.Verdicts);
                        var blocked = Assert.IsType<AiCommandVerdict.ModerationBlocked>(verdict);
                        Assert.Equal("test-block-category", blocked.CategoryCode);
                    }

                    // Nothing hardcoded: the default (Empty) policy admits the same fields.
                    foreach (var json in envelopes)
                    {
                        var result = AiEnvelopeValidator.Validate(json, AiEnvelopePolicy.PermitAll);
                        Assert.IsType<AiCommandVerdict.Valid>(Assert.Single(result.Verdicts));
                    }

                    break;
                }
                default:
                    // TRIPWIRE: a new Wired inventory row without an assertion arm fails here.
                    Assert.Fail($"wired surface '{surface.Id}' has no executable assertion arm — coverage honesty forbids an untested wired claim");
                    break;
            }
        }
    }

    // ---- tripwire: a new pipeline operation entry point must register in the inventory ----

    [Fact]
    public void Tripwire_EveryPipelineEntryPoint_RegisteredAsWiredInputSurface()
    {
        var entryPoints = typeof(AiOperationPipeline).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("Run", StringComparison.Ordinal) && m.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToArray();

        Assert.NotEmpty(entryPoints);
        foreach (var entryPoint in entryPoints)
        {
            Assert.Contains(AiModerationSurfaces.All, s =>
                s.Disposition == AiModerationSurfaceDisposition.Wired &&
                s.Side == AiModerationSource.Input &&
                s.OperationEntryPoint == entryPoint);
        }
    }

    // ---- tripwire: a new string-carrying command-data variant must be enumerated ----

    [Fact]
    public void Tripwire_EveryStringCarryingCommandDataVariant_EnumeratedByFreeTextFields()
    {
        var variants = typeof(AiCommandData).GetNestedTypes()
            .Where(t => t.IsAssignableTo(typeof(AiCommandData)) && !t.IsAbstract)
            .ToArray();

        Assert.Equal(9, variants.Length); // the c1 vocabulary: a NEW variant fails here until reviewed
        foreach (var variant in variants)
        {
            var ctor = variant.GetConstructors().Single();
            var args = ctor.GetParameters()
                .Select(p => p.ParameterType == typeof(string) ? (object?)"sample"
                    : p.ParameterType == typeof(int) ? 1
                    : p.ParameterType == typeof(bool) ? true
                    : p.ParameterType == typeof(double) ? 0.5
                    : throw new InvalidOperationException($"unhandled parameter type {p.ParameterType} on {variant.Name} — extend the tripwire"))
                .ToArray();
            var instance = (AiCommandData)ctor.Invoke(args);

            var carriesString = variant.GetProperties().Any(p => p.PropertyType == typeof(string));
            var fields = AiEnvelopeValidator.FreeTextFields(instance).ToArray();
            if (carriesString)
            {
                Assert.NotEmpty(fields);
            }
            else
            {
                Assert.Empty(fields);
            }
        }
    }

    // ---- reserved dispositions are honest ----

    [Fact]
    public void Inventory_Dispositions_WiredAndReservedCountsMatchTheCoverageTable()
    {
        // record.md §2: 6 wired (chat input, awareness input, awareness context fields,
        // both reply outputs, command free-text), 5 reserved (memory persist, reply speech,
        // prompt templates, community prompts, quiz templates). A registry edit without a
        // record/table update trips here. (awareness-context-fields flipped Reserved→Wired
        // in c6/SP-044 after c5 landed the packaging wiring.)
        Assert.Equal(6, AiModerationSurfaces.All.Count(s => s.Disposition == AiModerationSurfaceDisposition.Wired));
        Assert.Equal(5, AiModerationSurfaces.All.Count(s => s.Disposition == AiModerationSurfaceDisposition.Reserved));
    }

    // ---- escalation state location per the Step-1 decision: session-scoped ----

    [Fact]
    public void Escalation_SessionScoped_NoSharedStateAcrossInstances()
    {
        var first = new AiModerationEscalation();
        for (var i = 0; i < AiEscalationThresholds.WpfBaselinePlaceholder.CooldownAt; i++)
        {
            first.RecordHit();
        }

        Assert.True(first.GetState().CooldownActive);

        // A new instance (a new session) starts clean — state is NOT persisted/shared
        // (recorded divergence from WPF restart survival; record.md §3.2 rule 4).
        var second = new AiModerationEscalation();
        var state = second.GetState();
        Assert.Equal(0, state.HitsInWindow);
        Assert.False(state.CooldownActive);
        Assert.False(state.WarningShown);
    }

    // ---- offline zero-network re-verified: the boundary adds no network path ----

    [Fact]
    public async Task Offline_BlockedAndUnprovenPaths_ZeroNetwork()
    {
        // (a) No proven provider: moderation never runs (offline-first ordering), zero
        // send attempts, zero escalation hits.
        var h = new Harness(Policy);
        h.Pipeline.RegisterProvider(h.Provider);
        h.Pipeline.SelectProvider(AiProviderId.LocalOllama);

        var interactive = await h.Pipeline.RunInteractiveAsync(new AiRequest(Forbidden));
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(interactive.Reply).Code);
        var awareness = await h.Pipeline.RunAwarenessAsync(new AiRequest(Forbidden), awarenessConsent: true);
        Assert.Equal(AiReplyCodes.ProviderUnproven, Assert.IsType<AiReply.Unavailable>(awareness.Reply).Code);
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Boundary.Escalation.GetState().HitsInWindow);

        // (b) Proven provider, blocked input: blocked BEFORE the gateway — zero network.
        await h.AdmitProviderAsync();
        await h.Pipeline.RunInteractiveAsync(new AiRequest(Forbidden));
        Assert.Equal(0, h.Pipeline.SendAttempts);
        Assert.Equal(0, h.Provider.Calls);
    }

    [Fact]
    public void Boundary_PureLocalEvaluation_NoNetworkSurface()
    {
        // Structural proof: the boundary mechanism (policy, escalation, boundary) holds no
        // network-capable dependency — only documents, counters, and clocks.
        foreach (var type in new[] { typeof(AiModerationBoundary), typeof(AiModerationEscalation), typeof(AiModerationPolicy) })
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    Assert.DoesNotContain("Http", param.ParameterType.Name, StringComparison.Ordinal);
                    Assert.DoesNotContain("Socket", param.ParameterType.Name, StringComparison.Ordinal);
                    Assert.DoesNotContain("Client", param.ParameterType.Name, StringComparison.Ordinal);
                }
            }
        }
    }

    // ---- content-free diagnostics: category/token text never enters records ----

    [Fact]
    public async Task Diagnostics_BlockedOperations_CarrySideCodesOnly_NeverPolicyContent()
    {
        var h = new Harness(Policy);
        await h.AdmitProviderAsync();

        await h.Pipeline.RunInteractiveAsync(new AiRequest(Forbidden));
        h.Provider.Reply = new AiReply.Generated(Forbidden, AiEndpointClass.Loopback);
        await h.Pipeline.RunInteractiveAsync(new AiRequest("clean"));

        Assert.NotEmpty(h.Diagnostics.Records);
        foreach (var record in h.Diagnostics.Records)
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(record);
            Assert.DoesNotContain("test-block-category", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(Forbidden, serialized, StringComparison.Ordinal);
        }

        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == "refused:input");
        Assert.Contains(h.Diagnostics.Records, r => r.StableCode == "refused:output");
    }
}
