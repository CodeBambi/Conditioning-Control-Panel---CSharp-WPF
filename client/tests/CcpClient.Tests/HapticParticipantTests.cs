using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Entitlement;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The haptic sink's APP-LIFETIME owner: what phase 3 does, what it deliberately does not
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
        // auto-connect the same way (App.xaml.cs:2176, predicate at :3580-3589) and says why at
        // :2173-2174.
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
        var participant = scope.Build(sink, Entitled(EntitlementTier.Supporter));

        await participant.StartAsync(TestContext.Current.CancellationToken);

        // A route is admitted, and STILL nothing is contacted: the setting is off. Upstream guards
        // its auto-connect the same way, because knocking on a provider nobody has "would silently
        // bring up three virtual toys and a stream of pink toasts at each launch".
        Assert.Equal(0, participant.ConnectAttempts);

        participant.RequestEnable(true);
        await TestWait.Until(participant.PendingConnect!, "the enable-path connect to land");

        Assert.Equal(1, participant.ConnectAttempts);
        Assert.IsType<CapabilityState.Available>(participant.LastConnectOutcome);
        Assert.True(participant.LastObservation!.Confirmed);
        Assert.Equal(1, sink.Connects);

        // NOT ASSERTED HERE, and named rather than left as a silent hole: "a relaunch with the
        // setting already on connects at START". Settings Mutate only marks dirty — persistence is
        // debounced — so a relaunch inside this fact would be racing a save rather than proving a
        // gate, and a fact that sometimes reads the old file is worse than no fact. The start gate
        // itself IS covered: the assertion above proves it refuses while the setting is off, and
        // both paths run the same ConnectAndRecordAsync.
        await participant.StopAsync();
    }

    /// <summary>
    /// The panel's line and the System page's line are the SAME expression, with the feature off and
    /// with it on.
    ///
    /// <para><b>The probe is now <c>ProbeSinkAsync</c> rather than a bare
    /// <c>Sink.ObserveAsync().Classify()</c></b>, because it carries the gate. This fact compares
    /// <see cref="HapticParticipant.SinkState"/> against that real member on both sides of the gate,
    /// so the two surfaces cannot tell different stories about one sink in either state.</para>
    /// </summary>
    [Fact]
    public async Task THESINKSTATEIsTheSAMEExpressionTheCapabilityProbeEvaluates()
    {
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 2);
        var participant = scope.Build(sink, Entitled(EntitlementTier.Supporter));

        await participant.StartAsync(TestContext.Current.CancellationToken);

        // WITH THE FEATURE OFF. Both surfaces say not-probed, and the sink was NEVER ASKED - which
        // is the fact that would fail if the probe opened a socket for a switched-off feature.
        var offState = Assert.IsType<CapabilityState.Unavailable>(participant.SinkState);
        var offProbe = Assert.IsType<CapabilityState.Unavailable>(
            await participant.ProbeSinkAsync(TestContext.Current.CancellationToken));
        Assert.Equal(CapabilityReasonCodes.NotProbed, offState.Reason.Code);
        Assert.Equal(offState.Reason.Code, offProbe.Reason.Code);
        Assert.Equal(offState.Reason.Detail, offProbe.Reason.Detail);
        Assert.Equal(0, sink.Observes);

        // WITH THE FEATURE ON. Both surfaces now report what the SERVER said, and they still agree.
        participant.RequestEnable(true);
        await TestWait.Until(participant.PendingConnect!, "the enable-path connect to land");

        var onState = participant.SinkState;
        var onProbe = await participant.ProbeSinkAsync(TestContext.Current.CancellationToken);
        Assert.IsType<CapabilityState.Available>(onState);
        Assert.IsType<CapabilityState.Available>(onProbe);
        Assert.True(sink.Observes > 0);

        await participant.StopAsync();
    }

    [Fact]
    public async Task SINKSTATEReportsWhatTheSERVERSaid_NotAFixedSentence()
    {
        // The sweep's M-av: SinkState classifying a hard-coded NotAsked instead of the observation
        // survived, because every fact about it drove a build with nothing to observe. A capability
        // line that cannot change is a capability line that is decoration.
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Lovense, devices: 3);
        var participant = scope.Build(sink, Entitled(EntitlementTier.Supporter));

        Assert.IsType<CapabilityState.Unavailable>(participant.SinkState);

        await participant.StartAsync(TestContext.Current.CancellationToken);

        // Nothing was asked yet, because the setting is off and this build no longer contacts a
        // provider a user has not switched on.
        Assert.IsType<CapabilityState.Unavailable>(participant.SinkState);

        participant.RequestEnable(true);
        await TestWait.Until(participant.PendingConnect!, "the enable-path connect to land");

        var available = Assert.IsType<CapabilityState.Available>(participant.SinkState);
        Assert.Contains("3 device(s)", available.Detail, StringComparison.Ordinal);
        Assert.Contains("Lovense", available.Detail, StringComparison.Ordinal);

        await participant.StopAsync();
    }

    // =====================================================================================
    //  The premium gate, and what a refused tick writes
    // =====================================================================================

    [Fact]
    public async Task AREFUSEDTickWritesNOTHING_WhichIsUpstreamsOwnStatementOrder()
    {
        // MainWindow/MainWindow.Haptics.cs tests the gate at :489, reverts the box at :491 and
        // RETURNS at :497 — so HapticCfg.Enabled = isEnabled at :500 is reached only when the gate
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
        // Upstream's condition is `isEnabled && …` (MainWindow.Haptics.cs:489): the gate only
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

        // WHAT THIS FACT DOES NOT COVER, said here because the sweep proved it (M-as, the packet's
        // one survivor). It pins the two things that are OBSERVABLE — the gate's message and the log
        // — and neither renders the EntitlementReason.Detail this participant builds from the
        // exception. Swapping ex.GetType().Name for ex.Message there survives this fact and every
        // other one in the suite, because in this build that detail has NO READER at all:
        // HapticGate.Decide takes reason.Code and EntitlementOutcome.Describe takes reason.Code.
        // Dispositioned UNCOVERED rather than equivalent — whichever packet first RENDERS an
        // entitlement detail inherits the obligation not to render this one.

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
        // the checkbox (the D180 discipline, the dot's fifth meaning).
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
        await TestWait.Until(participant.PendingConnect!, "the enable-path connect to land");

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
                // HAPTICS FIRST, deliberately, and it is what makes this fact bite. Participant stop
                // is REVERSE order, so registering it first means its own StopAsync runs LAST — and
                // then the ONLY thing that can put the all-stop before the other participant's stop
                // is the reserved pre-drain head slot. Register it last and the fact passes with the
                // head slot deleted, which is the mutation this is aimed at.
                return [haptics, new OrderedParticipant("Other", order, () => Interlocked.Increment(ref ticks))];
            },
        };
        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());
        Assert.IsType<StartupOutcome.Success>(
            await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

        await host.ShutdownAsync();

        Assert.NotNull(haptics);
        Assert.Equal(1, haptics!.AllStops);
        var other = Assert.IsType<OrderedParticipant>(host.Participants[1]);
        Assert.True(other.StopSequence > 0, "the other participant never stopped");
        Assert.True(
            haptics.AllStopSequence < other.StopSequence,
            $"the haptic all-stop ran at {haptics.AllStopSequence} and the participant stop at "
            + $"{other.StopSequence}: the all-stop must come FIRST");
    }

    /// <summary>
    /// THE STOP REACHES A REAL PROVIDER ON THE WIRE, before teardown completes.
    ///
    /// <para>Every other teardown fact in this file counts calls on a recording double, which
    /// proves the ORDER and nothing about delivery: a sink that accepted an all-stop and sent
    /// nothing would satisfy all of them. This one drives the REAL ApplicationHost.ShutdownAsync
    /// through the REAL composition root into a REAL LovenseHapticSink pointed at
    /// an HTTP server, and then asks the SERVER what arrived.</para>
    ///
    /// <para><b>What this proves is DELIVERY, and not ordering.</b> I wrote it claiming both and
    /// checked: with the composition root's pre-drain head slot DELETED this fact still passes,
    /// because <c>StopAsync</c> also all-stops before releasing the sink, so a zero still reaches
    /// the wire by the slower path. Ordering is <see cref="THEALLSTOPRunsBEFOREEveryParticipantStop_WhichIsUpstreamsOrdering"/>,
    /// which does fail against that mutation. The two are complementary and neither is redundant:
    /// the ordering fact counts calls on a double and would pass against a sink that sent nothing,
    /// and this one would pass against a broken order. Naming the boundary here so the next reader
    /// does not delete one believing the other covers it.</para>
    ///
    /// <para>What this still does NOT claim: that a motor stopped spinning. That is a device gate a
    /// human reports, and the row says so.</para>
    /// </summary>
    [Fact]
    public async Task THEALLSTOPPutsAZeroOnTHEWIRE_AgainstARealProviderBeforeTeardownCompletes()
    {
        using var scope = new Scope();
        using var server = new HapticToyServer();
        HapticParticipant? haptics = null;

        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(scope.Directory, "settings.json"),
            ParticipantsFactory = infra =>
            {
                haptics = new HapticParticipant(
                    infra, scope.Directory,
                    new LovenseHapticSink(_ => { }, server.BaseUrl, LovenseHapticSink.LovenseMode.Lan),
                    Entitled(EntitlementTier.Supporter));
                return [haptics];
            },
        };

        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());
        Assert.IsType<StartupOutcome.Success>(
            await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

        // Switch it on and let the connect land, so the sink really has a device to countermand.
        haptics!.RequestEnable(true);
        await TestWait.Until(haptics.PendingConnect!, "the enable-path connect to land");

        // Drive one real level onto the wire. Without this the stop would have nothing recorded to
        // stop, and StopAllAsync would answer Degraded rather than sending anything.
        await haptics.Sink.SetOutputsAsync(
            server.ToyKey, [new HapticOutput(0, HapticLevel.Of(0.8))], TestContext.Current.CancellationToken);
        // Derived, not restated: the level this maps to is the quantizer's business, and a
        // hand-written number here would be a second implementation of it that can disagree.
        var expected = LovenseHapticSink.QuantizeLevel(HapticLevel.Of(0.8));
        Assert.Contains(server.Commands, c => c.Contains($"Vibrate:{expected}", StringComparison.Ordinal));
        var beforeShutdown = server.Commands.Count;

        await host.ShutdownAsync();

        // THE FACT: a zero arrived at the server during shutdown.
        var duringShutdown = server.Commands.Skip(beforeShutdown).ToList();
        Assert.Contains(duringShutdown, c => c.Contains("Vibrate:0", StringComparison.Ordinal));

        // And it was the all-stop that sent it, once.
        Assert.Equal(1, haptics.AllStops);
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
    public async Task ANUNSAVEDSettingStillReachesDiskThroughTheReservedPreDrainSlot()
    {
        // The sweep's M-ax: deleting the haptic flush from the pre-drain slot survived, because
        // every other fact about the setting either saved it explicitly or never restarted. A
        // switch the user flipped on the way out is a persisted setting like any other
        // (persistence contract §11), and this is the ONE place the port guarantees it reaches disk.
        using var scope = new Scope();
        HapticParticipant? haptics = null;
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(scope.Directory, "settings.json"),
            ParticipantsFactory = infra =>
            {
                haptics = new HapticParticipant(
                    infra, scope.Directory, new RecordingSink(HapticProviderRoute.Buttplug, 1),
                    Entitled(EntitlementTier.Supporter));
                return [haptics];
            },
        };
        Assert.True(root.Validate(out _));
        var host = root.Build(new StartupTrace());
        Assert.IsType<StartupOutcome.Success>(
            await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

        // Dirty, and NEVER saved: no Save() call anywhere on this path.
        Assert.NotNull(haptics);
        Assert.IsType<HapticGateDecision.Allow>(haptics!.RequestEnable(true));
        Assert.True(haptics.Preset.IsDirty);

        await host.ShutdownAsync();

        var json = await File.ReadAllTextAsync(
            Path.Combine(scope.Directory, HapticSettingsDocument.FileName),
            TestContext.Current.CancellationToken);
        Assert.Contains("\"enabled\": true", json, StringComparison.Ordinal);
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
    public async Task STOPPINGANeverStartedParticipantStillCOUNTERMANDSAndStillRELEASESTheSink()
    {
        // It is NOT a no-op, and the difference is reachable rather than theoretical: this
        // participant is registered LAST, so any earlier participant's phase-3 failure leaves it
        // constructed and un-started while ApplicationHost.ShutdownAsync still stops everyone. A
        // sink released only on the happy path is a WebSocket or an HttpClient held open by a
        // process that has already given up.
        using var scope = new Scope();
        var sink = new RecordingSink(HapticProviderRoute.Buttplug, devices: 1);
        var participant = scope.Build(sink);

        await participant.StopAsync();

        Assert.False(participant.Running);
        Assert.Equal(1, participant.AllStops);
        Assert.Equal(1, sink.StopAlls);
        Assert.True(sink.Disposed);

        // And the ORDER is upstream's: the all-stop reached the sink BEFORE the sink was torn down
        // (App.xaml.cs:4401-4407; HapticService.cs:961-962 all-stops and only then disposes).
        Assert.Equal(1, sink.StopAllsBeforeDispose);

        // Idempotent, and still one all-stop: the latch and Dispose both hold on a second pass.
        await participant.StopAsync();
        Assert.Equal(1, participant.AllStops);
        Assert.Equal(1, sink.StopAlls);
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
                Directory, sink ?? Unadmitted(), entitlement);

        /// <summary>The refusing sink, named explicitly. Before a route was admitted this was what
        /// the product factory returned, so a fact could get it by default; now the default is a
        /// composite over the routes the user ticked, and an EMPTY list means "ticked nothing"
        /// rather than "nothing is admitted". A fact about the unadmitted case has to ask for the
        /// unadmitted case, and this is where it asks.</summary>
        public static IHapticSink Unadmitted() =>
            HapticSinkFactory.CreateFor(HapticProviderRoute.None);

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

        /// <summary>How many times anything asked this sink about a server. <b>The witness that a
        /// gated probe did not open a socket</b>: a real sink's ObserveAsync IS the wire.</summary>
        public int Observes { get; private set; }

        public int OutputCalls { get; private set; }

        public int StopAlls { get; private set; }

        public bool Disposed { get; private set; }

        /// <summary>How many all-stops had run at the moment Dispose was first called. The order
        /// witness: an all-stop that arrives after teardown reaches a provider that is already
        /// gone, which is the defect upstream's own comment describes (App.xaml.cs:4401-4404).</summary>
        public int StopAllsBeforeDispose { get; private set; } = -1;

        public Task<HapticServerObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            Observes++;
            return Task.FromResult(new HapticServerObservation(true, Route, true, true, _devices));
        }

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

        public void Dispose()
        {
            if (!Disposed)
            {
                StopAllsBeforeDispose = StopAlls;
            }

            Disposed = true;
        }
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
