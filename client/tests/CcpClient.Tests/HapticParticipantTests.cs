using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-119 — the haptic sink's APP-LIFETIME owner: what phase 3 does, what it deliberately does not
/// do, where the all-stop runs relative to everything else, and what the premium gate writes.
/// </summary>
public class HapticParticipantTests
{
    // =====================================================================================
    //  Phase 3: what it does, and the connection it refuses to attempt
    // =====================================================================================

    [Fact]
    public async Task PHASETHREEConnectsToNOTHING_BecauseNoProviderClientIsAdmitted()
    {
        using var scope = new Scope();
        var participant = scope.Build();

        await participant.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(participant.Running);
        // The product does not knock on a door it has no key for. Upstream guards its own
        // auto-connect the same way (App.xaml.cs:2103, predicate at :3487-3495) and says why at
        // :2098-2102.
        Assert.Equal(0, participant.ConnectAttempts);
        Assert.Null(participant.LastConnectOutcome);
        Assert.Null(participant.LastObservation);
        // And the sink was never asked ANYTHING by the participant.
        Assert.Equal(0, ((UnadmittedHapticSink)participant.Sink).RefusedCalls);

        await participant.StopAsync();
    }

    [Fact]
    public async Task WITHAROUTEAdmitted_PhaseThreeDOESConnectAndDoesRecordWhatItFound()
    {
        // The other half of the predicate, executed: the refusal above is a consequence of the
        // admitted-route list rather than of a hard-coded "never".
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 2);
        var participant = scope.Build(sink);

        await participant.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, participant.ConnectAttempts);
        Assert.IsType<CapabilityState.Available>(participant.LastConnectOutcome);
        Assert.True(participant.LastObservation!.Confirmed);
        Assert.Equal(1, sink.Connects);

        await participant.StopAsync();
    }

    [Fact]
    public async Task THESINKSTATEIsTheSAMEExpressionTheCapabilityProbeEvaluates()
    {
        using var scope = new Scope();
        var participant = scope.Build();

        await participant.StartAsync(TestContext.Current.CancellationToken);

        var state = Assert.IsType<CapabilityState.Unavailable>(participant.SinkState);
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, state.Reason.Code);
        // The probe's expression, evaluated here on the same sink: the panel and the System page
        // cannot tell two different stories.
        var probed = Assert.IsType<CapabilityState.Unavailable>(
            (await participant.Sink.ObserveAsync(TestContext.Current.CancellationToken)).Classify());
        Assert.Equal(state.Reason.Code, probed.Reason.Code);
        Assert.Equal(state.Reason.Detail, probed.Reason.Detail);

        await participant.StopAsync();
    }

    // =====================================================================================
    //  The premium gate, and what a refused tick writes
    // =====================================================================================

    [Fact]
    public async Task AREFUSEDTickWritesNOTHING_WhichIsUpstreamsOwnStatementOrder()
    {
        // MainWindow/MainWindow.Haptics.cs tests the gate at :487, reverts the box at :489 and
        // RETURNS at :496 — so HapticCfg.Enabled = isEnabled at :499 is reached only when the gate
        // allowed. Reversing those two would leave the box visually off and the setting on, and the
        // setting is what survives a restart.
        using var scope = new Scope();
        var participant = scope.Build(entitlement: Fixed(Unknown()));

        await participant.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(participant.OutputAllowed);

        var decision = participant.RequestEnable(true);

        Assert.IsType<HapticGateDecision.RefusedUnverified>(decision);
        Assert.False(participant.Enabled);
        Assert.False(participant.Preset.Current.Enabled);

        await participant.StopAsync();
    }

    [Fact]
    public async Task ANALLOWEDTickREALLYWritesTheSetting()
    {
        using var scope = new Scope();
        var participant = scope.Build(entitlement: Entitled(EntitlementTier.Supporter));

        await participant.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(participant.OutputAllowed);

        Assert.IsType<HapticGateDecision.Allow>(participant.RequestEnable(true));

        Assert.True(participant.Enabled);
        Assert.True(participant.Preset.Current.Enabled);

        await participant.StopAsync();
    }

    [Fact]
    public async Task SWITCHINGOFFIsNEVERGated_SoALapsedPledgeCannotTrapARunningToy()
    {
        // Upstream's condition is `isEnabled && …` (MainWindow.Haptics.cs:487): the gate only
        // guards turning it ON.
        using var scope = new Scope();
        var participant = scope.Build(entitlement: Entitled(EntitlementTier.Lab));
        await participant.StartAsync(TestContext.Current.CancellationToken);
        participant.RequestEnable(true);
        Assert.True(participant.Enabled);

        // The pledge lapses, and the user reaches for the switch.
        await participant.ApplyGateAsync(HapticGate.Decide(Unknown()));
        participant.RequestEnable(false);

        Assert.False(participant.Enabled);

        await participant.StopAsync();
    }

    [Fact]
    public async Task THEGATEClosingSTOPSEVERYTHINGONCE_WhichIsUpstreamsOpenToClosedArm()
    {
        // HapticMixer.cs:253-262: the transition drops everything and stops the toys once. A level
        // already held on a device is the one piece of this port's state that outlives the process.
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Lovense, devices: 1);
        var participant = scope.Build(sink, Entitled(EntitlementTier.Supporter));
        await participant.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(participant.OutputAllowed);
        Assert.Equal(0, sink.StopAlls);

        await participant.ApplyGateAsync(HapticGate.Decide(Unknown()));

        Assert.False(participant.OutputAllowed);
        Assert.Equal(1, sink.StopAlls);
        Assert.Equal(1, participant.AllStops);

        // ONCE. Applying the same closed decision again stops nothing further — the transition is
        // the trigger, not the state.
        await participant.ApplyGateAsync(HapticGate.Decide(Unknown()));
        Assert.Equal(1, sink.StopAlls);

        await participant.StopAsync();
    }

    [Fact]
    public async Task AGATEThatWasNEVEROpenStopsNothingWhenItStaysClosed()
    {
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 1);
        var participant = scope.Build(sink, Fixed(Unknown()));

        await participant.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, sink.StopAlls);
        Assert.Equal(0, participant.AllStops);

        await participant.StopAsync();
    }

    [Fact]
    public async Task ANAUTHORITYThatTHROWSIsUNKNOWN_AndOnlyItsTYPENAMEIsCarried()
    {
        using var scope = new Scope();
        var participant = scope.Build(
            entitlement: _ => throw new InvalidOperationException("bearer=SECRET-VALUE https://host/x"));

        await participant.StartAsync(TestContext.Current.CancellationToken);

        var unverified = Assert.IsType<HapticGateDecision.RefusedUnverified>(participant.Gate);
        Assert.Equal(EntitlementReasonCodes.TierAuthorityFault, unverified.ReasonCode);
        Assert.DoesNotContain("SECRET-VALUE", string.Join("\n", scope.Log.Lines), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-VALUE", unverified.Message, StringComparison.Ordinal);

        await participant.StopAsync();
    }

    [Fact]
    public async Task BEFOREPhaseThreeTheGateIsALREADYCLOSED_NeverPermissiveByDefault()
    {
        using var scope = new Scope();
        var participant = scope.Build(entitlement: Entitled(EntitlementTier.Lab));

        // Not started: a gate that opened while uninitialised would open exactly once per launch,
        // in the window before anyone could see it close.
        Assert.False(participant.OutputAllowed);
        Assert.IsType<HapticGateDecision.RefusedUnverified>(participant.Gate);
        Assert.False(participant.Enabled);

        await participant.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(participant.OutputAllowed);
        await participant.StopAsync();
    }

    // =====================================================================================
    //  The dot — two reachable values, and the third is D179
    // =====================================================================================

    [Fact]
    public async Task THEDOTIsOFFOnThisBuildWhateverTheSwitchSays_BecauseNothingCanReachADevice()
    {
        using var scope = new Scope();
        var participant = scope.Build(entitlement: Entitled(EntitlementTier.Supporter));
        await participant.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EffectDotState.Off, participant.Dot);
        participant.RequestEnable(true);
        Assert.True(participant.Enabled);
        // Enabled and STILL dark: the second conjunct asks the sink, so the dot cannot be read off
        // the checkbox (SP-118's D180 discipline, SP-109's fifth meaning).
        Assert.Equal(EffectDotState.Off, participant.Dot);

        await participant.StopAsync();
    }

    [Fact]
    public async Task THEDOTReachesARMEDWhenASwitchIsOnAndADeviceIsReallyReachable_AndNEVERLive()
    {
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 1);
        var participant = scope.Build(sink, Entitled(EntitlementTier.Supporter));
        await participant.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EffectDotState.Off, participant.Dot);
        participant.RequestEnable(true);

        Assert.Equal(EffectDotState.Armed, participant.Dot);
        // D179 MADE VISIBLE. Live would have to mean something is being sent, and nothing is: the
        // thirteen ported effect modules are silent to this sink, where upstream drives it from
        // eight sites in three of them. Armed is the ceiling until one of them grows a limb.
        Assert.NotEqual(EffectDotState.Live, participant.Dot);
        Assert.Equal(0, sink.OutputCalls);

        await participant.StopAsync();
    }

    // =====================================================================================
    //  Teardown: the head slot, the order, and the one-shot latch
    // =====================================================================================

    [Fact]
    public async Task THEALLSTOPRunsBEFOREEveryParticipantStop_WhichIsUpstreamsOrdering()
    {
        // App.xaml.cs:4401-4407: "Haptics FIRST and synchronously (bounded ~2s) … This cannot be
        // left to Haptics.Dispose() further down". The port's analogue is the reserved pre-drain
        // head slot, which completes before generations are cancelled and before any participant
        // stops. This drives the REAL ApplicationHost.ShutdownAsync through the REAL composition
        // root's slot rather than calling the method directly.
        using var scope = new Scope();
        var ticks = 0L;
        var order = new List<string>();
        HapticParticipant? haptics = null;
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(scope.Directory, "settings.json"),
            ParticipantsFactory = infra =>
            {
                haptics = new HapticParticipant(
                    infra, scope.Directory, new RecordingSink(HapticProviderRoute.Buttplug, 1),
                    Entitled(EntitlementTier.Supporter), () => Interlocked.Increment(ref ticks));
                return [new OrderedParticipant("Other", order, () => Interlocked.Increment(ref ticks)), haptics];
            },
        };
        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());
        Assert.IsType<StartupOutcome.Success>(
            await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

        await host.ShutdownAsync();

        Assert.NotNull(haptics);
        Assert.Equal(1, haptics!.AllStops);
        var other = Assert.IsType<OrderedParticipant>(host.Participants[0]);
        Assert.True(other.StopSequence > 0, "the other participant never stopped");
        Assert.True(
            haptics.AllStopSequence < other.StopSequence,
            $"the haptic all-stop ran at {haptics.AllStopSequence} and the participant stop at "
            + $"{other.StopSequence}: the all-stop must come FIRST");
    }

    [Fact]
    public async Task THEALLSTOPIsONESHOT_SoTheHeadSlotAndTheParticipantStopCannotBothSpendIt()
    {
        // Upstream's own latch, and its own reason: App.OnExit calls ShutdownStop and then Dispose
        // does (HapticMixer.cs:172-174, :1122), and a stop that burned its ~2 s budget twice was a
        // real defect rather than a hypothetical.
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 1);
        var participant = scope.Build(sink);
        await participant.StartAsync(TestContext.Current.CancellationToken);

        await participant.ShutdownStopAsync();
        await participant.ShutdownStopAsync();
        await participant.StopAsync();

        Assert.Equal(1, sink.StopAlls);
        Assert.Equal(1, participant.AllStops);
        Assert.True(sink.Disposed);
    }

    [Fact]
    public async Task ARESTARTKeepsTheSettingTheGateAllowed()
    {
        using var scope = new Scope();
        var first = scope.Build(entitlement: Entitled(EntitlementTier.Supporter));
        await first.StartAsync(TestContext.Current.CancellationToken);
        first.RequestEnable(true);
        await first.Preset.Save();
        await first.StopAsync();

        var second = scope.Build(entitlement: Entitled(EntitlementTier.Supporter));
        await second.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(second.Enabled);
        await second.StopAsync();
    }

    [Fact]
    public async Task STOPPINGANeverStartedParticipantIsANoOp()
    {
        using var scope = new Scope();
        var participant = scope.Build();

        await participant.StopAsync();

        Assert.False(participant.Running);
        Assert.Equal(0, participant.AllStops);
    }

    // =====================================================================================
    //  Helpers
    // =====================================================================================

    private static Func<CancellationToken, Task<EntitlementOutcome>> Entitled(EntitlementTier tier) =>
        _ => Task.FromResult<EntitlementOutcome>(new EntitlementOutcome.Entitled(tier, "confirmed"));

    private static EntitlementOutcome Unknown() =>
        new EntitlementOutcome.Unavailable(new EntitlementReason(
            EntitlementReasonCodes.TierAuthorityAbsent, "no entitlement authority is configured in this build"));

    private static Func<CancellationToken, Task<EntitlementOutcome>> Fixed(EntitlementOutcome outcome) =>
        _ => Task.FromResult(outcome);

    private sealed class Scope : IDisposable
    {
        public Scope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "ccp-sp119-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public ListLog Log { get; } = new();

        public HapticParticipant Build(
            IHapticSink? sink = null,
            Func<CancellationToken, Task<EntitlementOutcome>>? entitlement = null) =>
            new(new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), Log),
                Directory, sink, entitlement);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not a test result.
            }
        }
    }

    private sealed class ListLog : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Log(string message) => Lines.Add(message);
    }

    /// <summary>
    /// A sink that RECORDS rather than refuses, so the ownership, ordering and gate facts are about
    /// real calls. It never claims a device this process could not address: its observation is built
    /// from the device count it was constructed with, and nothing in the product may build one.
    /// </summary>
    private sealed class RecordingSink(HapticProviderRoute route, int devices) : IHapticSink
    {
        private readonly IReadOnlyList<string> _devices = Enumerable.Range(0, devices)
            .Select(i => route.ToString().ToLowerInvariant() + ":"
                + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        public HapticProviderRoute Route { get; } = route;

        public CapabilityState? LastOutcome { get; private set; }

        public int Connects { get; private set; }

        public int OutputCalls { get; private set; }

        public int StopAlls { get; private set; }

        public bool Disposed { get; private set; }

        public Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HapticServerObservation(true, Route, true, true, _devices));

        public async Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
        {
            Connects++;
            return LastOutcome = (await ObserveAsync(cancellationToken)).Classify();
        }

        public Task<CapabilityState> SetOutputsAsync(
            string deviceKey, IReadOnlyList<HapticOutput> outputs, CancellationToken cancellationToken)
        {
            OutputCalls++;
            return Task.FromResult(LastOutcome = new CapabilityState.Available("recorded"));
        }

        public Task<CapabilityState> StopAllAsync()
        {
            StopAlls++;
            return Task.FromResult(LastOutcome = new CapabilityState.Available("recorded all-stop"));
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A participant that records WHEN it stopped, on the same monotonic tick the haptic
    /// participant reads. Order evidence, and nothing else.</summary>
    private sealed class OrderedParticipant(string name, List<string> order, Func<long> sequence)
        : IBackgroundParticipant
    {
        public string Name => name;

        public bool Running { get; private set; }

        public long StopSequence { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Running = false;
            StopSequence = sequence();
            order.Add(name);
            return Task.CompletedTask;
        }
    }
}
