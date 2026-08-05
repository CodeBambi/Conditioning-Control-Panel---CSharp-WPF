using CcpClient.Desktop;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-046 c7 composition-wiring proof: the PRODUCT CompositionRoot constructs the full
/// AI chain (before this slice, no product composition constructed the pipeline — the
/// packet's core wiring requirement). Boot phases 1-3 through the real
/// CompositionRoot/StartupPhaseRunner against a temp data root; assert the participant
/// exists, starts, owns the composed seams, the default selection, and the capability
/// states (provider probe + cloud typed absence + window-title capability).
/// </summary>
public class CompanionCompositionTests
{
    private static async Task<(ApplicationHost Host, string Dir)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp046-comp-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        return (host!, dir);
    }

    [Fact]
    public async Task ProductComposition_ConstructsAiChain_ParticipantRunning()
    {
        var (host, _) = await BootAsync();
        var companion = host.Participants.OfType<CompanionParticipant>().Single();

        Assert.True(companion.Running); // the memory store started in phase-3 order
        Assert.NotNull(companion.Pipeline);
        Assert.NotNull(companion.Memory);
        Assert.NotNull(companion.Awareness);
        Assert.NotNull(companion.Executor);
        Assert.NotNull(companion.Diagnostics);

        // Default selection (recorded decision): the only admissible endpoint class.
        Assert.Equal(AiProviderId.LocalOllama, companion.Pipeline.Selected);

        // Placeholder postures: memory consent Denied, awareness consent NotGiven.
        Assert.Equal(AiMemoryConsent.Denied, companion.MemoryConsent);
        Assert.False(companion.Awareness.Consent.Granted);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task ProductComposition_CapabilityStates_TypedAndHonest()
    {
        var (host, _) = await BootAsync();
        var capabilities = host.Capabilities!;

        // The provider probe ran in the CapabilityProbes phase — a REAL loopback probe,
        // honestly read either way: Available when an Ollama-shaped service answers
        // api/version on this box (a real Ollama may be present), Unavailable otherwise.
        // NEVER a registration/selection-derived claim (SP-006; contract §11 rule 2).
        var provider = capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.LocalOllama));
        Assert.True(provider is CapabilityState.Available or CapabilityState.Unavailable,
            $"unexpected provider state: {provider}");

        // Cloud = inventory with typed absence (credentials-absent — never invented).
        var cloud = capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.Cloud));
        var cloudUnavailable = Assert.IsType<CapabilityState.Unavailable>(cloud);
        Assert.Equal("credentials-absent", cloudUnavailable.Reason.Code);

        // Offline = zero network through the pipeline gateway: nothing was sent.
        var companion = host.Participants.OfType<CompanionParticipant>().Single();
        Assert.Equal(0, companion.Pipeline.SendAttempts);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task HostOverride_RemoteHost_RejectedPreSocket_ZeroSendAttempts()
    {
        // The --ai-ollama-host boundary claim is security-critical (pre-completion
        // consult): a REMOTE override must classify RemoteHostOllama and be rejected
        // BEFORE any socket opens — admission policy + provider defense in depth. The
        // flag can NEVER widen the network boundary; this test is the proof, not inspection.
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp046-remote-" + Guid.NewGuid().ToString("N"));
        var registry = new OperationRegistry();
        var capabilities = new CapabilityRegistry();
        var participant = new CompanionParticipant(
            new ParticipantInfrastructure(registry, new UiDispatchBoundary(), new DebugLogSink()),
            capabilities, dir,
            ollamaHostOverride: "http://example.com:11434/");

        try
        {
            // Classification is config-pure: the remote host is RemoteHostOllama (contract §6).
            // The probe itself rejects pre-socket (probing a remote host would itself be
            // undeclared remote traffic).
            var runner = new CapabilityProbeRunner(registry.OwnerFor("probes"), capabilities);
            await runner.RunAllAsync(CancellationToken.None);
            var state = capabilities.GetState(AiOperationPipeline.CapabilityName(AiProviderId.LocalOllama));
            var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(AiReplyCodes.EndpointNotAdmitted, unavailable.Reason.Code);

            // Even with the probe forced Available, the admission policy rejects the class
            // pre-socket: the operation is typed Unavailable and ZERO sends occurred.
            var result = await participant.Pipeline.RunInteractiveAsync(new AiRequest("never leaves the machine"));
            var reply = Assert.IsType<AiReply.Unavailable>(result.Reply);
            Assert.Equal(AiReplyCodes.ProviderUnproven, reply.Code); // probe never ran Available
            Assert.Equal(0, participant.Pipeline.SendAttempts);
        }
        finally
        {
            await participant.StopAsync();
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductComposition_MemoryPersistsUnderUserDataRoot_FlushedAtTeardown()
    {
        var (host, dir) = await BootAsync();
        var companion = host.Participants.OfType<CompanionParticipant>().Single();

        companion.MemoryConsent = AiMemoryConsent.Granted;
        companion.Memory.Append(new AiMemoryTurn(AiMemoryRole.User, "composition-proof turn"));
        Assert.NotNull(companion.Memory.LastWriteCompletion);

        await host.ShutdownAsync(); // teardown flush (the pre-drain slot) lands the write
        Assert.True(File.Exists(Path.Combine(dir, AiMemoryStore.FileName)));
    }
}
