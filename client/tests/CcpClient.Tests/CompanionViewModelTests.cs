using System.Collections.Concurrent;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Companion surface logic tests (SP-046 slice c7). The view-model runs against the REAL
/// pipeline/participant composition with a controllable fake provider at the provider
/// seam (the established test discipline — no mocks in the machinery). Proves: badge
/// truth from provenance BY TYPE (incl. the falsifiable identical-text pair), status
/// from the SP-006 capability state only, refusal bubble class discipline, the
/// memory-clear flow (default-No confirm, re-entrancy, file-content proof, honest
/// failure path), consent/cooldown typed-state driving, and panic-quiet (typed
/// Cancelled, thinking bubble removed, nothing partial, re-armed surface works).
/// </summary>
public class CompanionViewModelTests
{
    private sealed class FakeProvider : IAiProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeProvider(AiReply reply, bool block = false)
        {
            Reply = reply;
            Block = block;
        }

        public AiProviderDescriptor Descriptor { get; } =
            new(AiProviderId.LocalOllama, AiEndpointClass.Loopback);

        public AiReply Reply { get; }

        public bool Block { get; }

        public int Calls;

        public Func<CancellationToken, Task<CapabilityState>>? Probe { get; } =
            _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("fake-probe"));

        public async Task<AiReply> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Block)
            {
                // Cooperative: honors the token (panic cancels the wait).
                await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return Reply;
        }

        public void Release() => _gate.TrySetResult();
    }

    private sealed class QueuedDispatch : IUiDispatch
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public int Pending => _queue.Count;

        public void Post(Action action) => _queue.Enqueue(action);

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-sp046-vm-" + Guid.NewGuid().ToString("N"));

        public OperationRegistry Registry { get; } = new();
        public CapabilityRegistry Capabilities { get; } = new();
        public UiDispatchBoundary DispatchBoundary { get; } = new();
        public QueuedDispatch Dispatch { get; } = new();
        public CompanionParticipant Participant { get; }
        public CompanionViewModel Vm { get; }

        public Harness(AiReply? reply = null, bool block = false)
        {
            DispatchBoundary.Bind(Dispatch);
            var provider = new FakeProvider(reply ?? new AiReply.Generated("lab-reply", AiEndpointClass.Loopback), block);
            Provider = provider;
            Directory.CreateDirectory(_dir);
            Participant = new CompanionParticipant(
                new ParticipantInfrastructure(Registry, DispatchBoundary, new DebugLogSink()),
                Capabilities, _dir, providerOverride: provider);
            Vm = new CompanionViewModel(Participant, DispatchBoundary);
        }

        public FakeProvider Provider { get; }

        public string MemoryFile => Path.Combine(_dir, AiMemoryStore.FileName);

        public async Task StartMemoryAsync()
        {
            await Participant.StartAsync(CancellationToken.None);
            var probes = new CapabilityProbeRunner(Registry.OwnerFor("probes"), Capabilities);
            await probes.RunAllAsync(CancellationToken.None);
        }

        /// <summary>Waits (bounded) for a dispatch post to arrive, then pumps it.</summary>
        public void PumpEventually(int timeoutMs = 5000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Dispatch.Pending == 0 && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(10);
            }

            Assert.True(Dispatch.Pending > 0, "no dispatch post arrived within the bound");
            Dispatch.Pump();
        }

        public void Dispose()
        {
            Participant.StopAsync().GetAwaiter().GetResult();
            try { Directory.Delete(_dir, recursive: true); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    // ---- badge truth (contract §1; admission §2 rule 4 — LOAD-BEARING) ----

    [Fact]
    public void Badge_GeneratedBubble_IsBadged_WithProvenanceClass()
    {
        var bubble = CompanionBubbleModel.ForReply(new AiReply.Generated("model text", AiEndpointClass.Loopback));
        Assert.True(bubble.IsAiBadged);
        Assert.Equal("Loopback", bubble.ProvenanceClass);
        Assert.False(bubble.IsRefusal);
        Assert.False(bubble.IsSubdued);
    }

    [Fact]
    public void Badge_FallbackWithIdenticalText_NeverBadged_FalsifiablePair()
    {
        // The falsifiable pair (pre-approach consult #8): identical text, different TYPE —
        // the badge follows the type, never the text.
        const string identical = "identical reply text";
        var generated = CompanionBubbleModel.ForReply(new AiReply.Generated(identical, AiEndpointClass.Loopback));
        var fallback = CompanionBubbleModel.ForReply(new AiReply.Fallback(identical, "canned"));
        Assert.True(generated.IsAiBadged);
        Assert.False(fallback.IsAiBadged);
        Assert.Null(fallback.ProvenanceClass);
        Assert.True(fallback.IsSubdued);
        Assert.Equal(identical, fallback.Text); // text shows; the badge does not
    }

    [Fact]
    public void Badge_UnavailableAndRefused_NeverBadged()
    {
        var unavailable = CompanionBubbleModel.ForReply(new AiReply.Unavailable(AiReplyCodes.Offline));
        var refused = CompanionBubbleModel.ForReply(new AiReply.Refused(new AiModerationRefusal("cat", AiModerationSource.Input)));
        Assert.False(unavailable.IsAiBadged);
        Assert.True(unavailable.IsSubdued);
        Assert.False(refused.IsAiBadged);
        Assert.True(refused.IsRefusal);
        Assert.False(refused.IsSubdued);
    }

    [Fact]
    public void Bubble_UserAndThinking_NeverBadged()
    {
        Assert.False(CompanionBubbleModel.User("hi").IsAiBadged);
        Assert.False(CompanionBubbleModel.Thinking().IsAiBadged);
        Assert.True(CompanionBubbleModel.Thinking().IsThinking);
    }

    // ---- status from capability state ONLY ----

    [Fact]
    public async Task Status_ReflectsCapabilityState_NotSelection()
    {
        using var h = new Harness();
        // Before probes: registered-but-unprobed → Unavailable(not-probed) even though
        // a provider IS selected (selection ≠ availability, contract §3 rule 3).
        Assert.NotNull(h.Participant.Pipeline.Selected);
        Assert.False(h.Vm.StatusAvailable);
        Assert.Contains("unavailable", h.Vm.StatusText);

        await h.StartMemoryAsync(); // runs the probes
        h.Vm.RefreshStatus();
        Assert.True(h.Vm.StatusAvailable);
        Assert.Contains("available", h.Vm.StatusText);
    }

    // ---- send flow + badge through the REAL pipeline ----

    [Fact]
    public async Task Send_GeneratedReply_BadgedBubble_ThinkingRemoved()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();

        h.Vm.InputText = "hello companion";
        Assert.True(h.Vm.CanSend);
        h.Vm.SendCommand.Execute(null);

        Assert.Equal(string.Empty, h.Vm.InputText); // box clears BEFORE the reply (WPF order)
        Assert.True(h.Vm.InFlight);
        Assert.Equal(2, h.Vm.Bubbles.Count); // user + thinking
        Assert.True(h.Vm.Bubbles[0].IsUser);
        Assert.True(h.Vm.Bubbles[1].IsThinking);
        Assert.False(h.Vm.CanSend);

        h.PumpEventually();
        Assert.False(h.Vm.InFlight);
        Assert.Equal(2, h.Vm.Bubbles.Count); // user + reply; thinking REMOVED whole
        Assert.DoesNotContain(h.Vm.Bubbles, b => b.IsThinking);
        var reply = h.Vm.Bubbles[1];
        Assert.True(reply.IsAiBadged);
        Assert.Equal("lab-reply", reply.Text);
    }

    [Fact]
    public async Task Send_RefusedReply_RefusalBubbleClass_NeverBadged()
    {
        using var h = new Harness(new AiReply.Refused(new AiModerationRefusal("cat", AiModerationSource.Output)));
        await h.StartMemoryAsync();

        h.Vm.InputText = "say something";
        h.Vm.SendCommand.Execute(null);
        h.PumpEventually();

        var bubble = Assert.Single(h.Vm.Bubbles, b => !b.IsUser);
        Assert.True(bubble.IsRefusal);
        Assert.False(bubble.IsAiBadged);
        Assert.Contains("declined", bubble.Text); // output-side refusal copy
    }

    [Fact]
    public async Task Send_EmptyInput_NoOps()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();

        h.Vm.InputText = "   ";
        Assert.False(h.Vm.CanSend);
        h.Vm.SendCommand.Execute(null);
        Assert.Empty(h.Vm.Bubbles);
        Assert.Equal(0, h.Provider.Calls);
    }

    // ---- panic-quiet (contract §2 rule 3; pre-approach consult #7) ----

    [Fact]
    public async Task Panic_MidOperation_QuietSurface_TypedCancelled_NothingPartial_RearmWorks()
    {
        using var h = new Harness(block: true);
        await h.StartMemoryAsync();

        h.Vm.InputText = "long operation";
        h.Vm.SendCommand.Execute(null);
        var deadline = Environment.TickCount64 + 5000;
        while (h.Provider.Calls == 0 && Environment.TickCount64 < deadline)
        {
            Thread.Sleep(10);
        }

        Assert.Equal(1, h.Provider.Calls); // genuinely in flight

        h.Vm.StopCommand.Execute(null);
        h.PumpEventually(); // the cancelled operation's resolution post

        // Panic-quiet: the thinking bubble is gone, NO partial/final bubble surfaced,
        // the input is re-enabled — the surface is calm.
        Assert.False(h.Vm.InFlight);
        var userOnly = Assert.Single(h.Vm.Bubbles);
        Assert.True(userOnly.IsUser);

        // The calm state is a WORKING state (re-arm via SelectProvider): a post-panic
        // send through the same pipeline succeeds.
        h.Provider.Release();
        h.Vm.InputText = "after panic";
        Assert.True(h.Vm.CanSend);
        h.Vm.SendCommand.Execute(null);
        h.PumpEventually();
        Assert.Equal(3, h.Vm.Bubbles.Count); // user1 + user2 + reply
        Assert.True(h.Vm.Bubbles[1].IsUser);
        Assert.True(h.Vm.Bubbles[2].IsAiBadged);
    }

    // ---- memory-clear control flow (WPF default-No confirm; file-content proof) ----

    [Fact]
    public async Task ClearFlow_ConfirmDefaultsVisible_CancelClearsNothing()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        await SeedMemoryAsync(h, "kept turn");

        h.Vm.RequestClearCommand.Execute(null);
        Assert.True(h.Vm.ConfirmVisible);
        Assert.False(h.Vm.CanSend); // modal within the window

        h.Vm.CancelClearCommand.Execute(null); // the default-NO path
        Assert.False(h.Vm.ConfirmVisible);
        Assert.True(File.Exists(h.MemoryFile));
        Assert.Contains(h.Participant.Memory.ReadRecent(10), t => t.Text == "kept turn");
    }

    [Fact]
    public async Task ClearFlow_Confirm_EmptiesBubbles_DeletesFile_HonestOutcomeText()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        await SeedMemoryAsync(h, "doomed turn");
        Assert.True(File.Exists(h.MemoryFile));

        h.Vm.Bubbles.Add(CompanionBubbleModel.User("on-screen log entry"));
        h.Vm.RequestClearCommand.Execute(null);
        h.Vm.ConfirmClearCommand.Execute(null);
        Assert.False(h.Vm.ConfirmVisible);

        h.PumpEventually();
        Assert.False(File.Exists(h.MemoryFile)); // file-content proof
        Assert.Empty(h.Vm.Bubbles); // WPF: the on-screen chat log clears too
        Assert.Contains("cleared", h.Vm.ClearOutcomeText);
        Assert.DoesNotContain("failed", h.Vm.ClearOutcomeText);
    }

    [Fact]
    public async Task ClearFlow_DeleteFails_HonestFailureText_NeverReportsCleared()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();
        await SeedMemoryAsync(h, "locked turn");

        // Hold the document open exclusively so the delete fails (AV-scanner/lock class).
        using (var lockStream = new FileStream(h.MemoryFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            h.Vm.RequestClearCommand.Execute(null);
            h.Vm.ConfirmClearCommand.Execute(null);
            h.PumpEventually();
        }

        Assert.Contains("could not be deleted", h.Vm.ClearOutcomeText);
        Assert.True(File.Exists(h.MemoryFile)); // the document survived — the text told the truth
    }

    private static async Task SeedMemoryAsync(Harness h, string text)
    {
        h.Participant.MemoryConsent = AiMemoryConsent.Granted;
        h.Participant.Memory.Append(new AiMemoryTurn(AiMemoryRole.User, text));
        var outcome = await h.Participant.Memory.LastWriteCompletion!;
        Assert.IsType<OperationOutcome.Completed>(outcome);
    }

    // ---- consent + cooldown typed-state driving ----

    [Fact]
    public async Task ConsentToggles_DriveTypedStates_MemoryWriteAdmission()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();

        Assert.False(h.Vm.AwarenessConsentGiven); // placeholder default: NOT given
        Assert.False(h.Vm.MemoryConsentGranted); // placeholder default: Denied

        h.Vm.AwarenessConsentGiven = true;
        Assert.True(h.Participant.Awareness.Consent.Granted);

        h.Vm.MemoryConsentGranted = true;
        Assert.Equal(AiMemoryConsent.Granted, h.Participant.MemoryConsent);

        // The typed state reaches write admission: a send persists the pair.
        h.Vm.InputText = "remember this";
        h.Vm.SendCommand.Execute(null);
        h.PumpEventually();
        var outcome = await h.Participant.Memory.LastWriteCompletion!;
        Assert.IsType<OperationOutcome.Completed>(outcome);
        Assert.Equal(AiMemoryWriteAdmission.Admitted, h.Participant.Memory.LastWriteAdmission);
        Assert.Contains(h.Participant.Memory.ReadRecent(10), t => t.Text == "remember this");
    }

    [Fact]
    public async Task ConsentDenied_SendPersistsNothing_TypedAdmission()
    {
        using var h = new Harness();
        await h.StartMemoryAsync();

        h.Vm.InputText = "forget this";
        h.Vm.SendCommand.Execute(null);
        h.PumpEventually();

        Assert.Equal(AiMemoryWriteAdmission.ConsentDenied, h.Participant.Memory.LastWriteAdmission);
        Assert.Empty(h.Participant.Memory.ReadRecent(10));
        Assert.False(File.Exists(h.MemoryFile));
    }

    [Fact]
    public void CooldownBoxes_DriveTypedValues_NeverShrinkLiveCooldown()
    {
        using var h = new Harness();
        var service = h.Participant.Awareness;

        Assert.Equal(10, (int)service.Values.Global.TotalSeconds); // placeholder baseline

        h.Vm.GlobalSeconds = 42;
        Assert.Equal(42, (int)service.Values.Global.TotalSeconds);

        // A live cooldown is NEVER shortened by a value edit (extend-not-shrink).
        var live = service.Cooldowns.Extend(AiCooldownKind.Global, "(global)", TimeSpan.FromSeconds(60));
        h.Vm.GlobalSeconds = 5;
        var verdict = service.Cooldowns.Check(AiCooldownKind.Global, "(global)");
        var suppressed = Assert.IsType<AiCooldownVerdict.Suppressed>(verdict);
        Assert.Equal(live, suppressed.Until); // the live expiry stands
    }
}
