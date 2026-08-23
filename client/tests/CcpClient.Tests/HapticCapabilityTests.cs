using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The haptic SEAM and its refusal.
///
/// <para>Every fact here is about a capability with no provider behind it, which is exactly why the
/// classification is tested as a TRUTH TABLE over directly-constructed observations rather than
/// through a driver: the arms that a real provider would reach must be executed too, or the
/// refusal this build does produce would be the only thing anybody ever ran.</para>
/// </summary>
public class HapticCapabilityTests
{
    // =====================================================================================
    //  The refusal, and the gap it names
    // =====================================================================================

    /// <summary>
    /// THIS build owns BOTH clients, and a FRESH INSTALL still reaches neither.
    ///
    /// <para><b>Admission and consent are different, and this fact holds both ends.</b>
    /// <see cref="HapticSinkFactory.AdmittedRoutes"/> carries both routes, so "admitted" cannot
    /// quietly become "admitted and doing nothing"; and both per-route flags default FALSE, which is
    /// upstream's own stored default (<c>Models/HapticSettings.cs:769</c> has no initializer), so a
    /// user who has never opened the panel has consented to no route and no socket can be opened on
    /// their behalf.</para>
    /// </summary>
    [Fact]
    public void ThisBuildOwnsBOTHClients_AndAFreshInstallHasConsentedToNEITHER()
    {
        Assert.Equal(
            [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug],
            HapticSinkFactory.AdmittedRoutes);

        // The product sink over a document nobody has edited.
        var document = new HapticSettingsDocument();
        Assert.False(document.LovenseEnabled);
        Assert.False(document.ButtplugEnabled);
        Assert.Empty(document.EnabledRoutes());

        using var sink = HapticSinkFactory.Create(document.EnabledRoutes);
        var composite = Assert.IsType<CompositeHapticSink>(sink);
        Assert.Equal(HapticProviderRoute.None, composite.Route);

        // Nothing was asked of a server by CONSTRUCTING it, and no route client was even built: the
        // composite builds them on demand, so a user who ticks nothing pays for nothing.
        Assert.Null(composite.LastOutcome);
        Assert.Empty(composite.LiveRoutes);

        // Tick one and the SAME sink reaches it, without being rebuilt - the flags are read per
        // operation, which is upstream's own re-read at every connect
        // (HapticDeviceManager.cs:102, :91-98).
        document.ButtplugEnabled = true;
        Assert.Equal(HapticProviderRoute.Buttplug, composite.Route);
        document.LovenseEnabled = true;
        Assert.Equal(HapticProviderRoute.Lovense, composite.Route);
    }

    /// <summary>A route claimed as admitted with no client behind it still THROWS. Admitting the two
    /// real routes must not have relaxed the check that catches a fake-available sink - it stops
    /// firing for them because they now HAVE clients, and for no other reason.</summary>
    [Fact]
    public void ARouteWithNoClientStillThrows_BecauseAdmissionDidNotRelaxTheCheck()
    {
        // Executable only because the admitted list is a parameter: with both real routes now
        // carrying clients, the only way to reach this guard is to claim a route is admitted when no
        // sink can be constructed for it - which is exactly the build mistake it exists to catch.
        var ex = Assert.Throws<InvalidOperationException>(
            () => HapticSinkFactory.CreateFrom(
                [HapticProviderRoute.None], admittedRoutes: [HapticProviderRoute.None]));
        Assert.Contains("admit the route AND", ex.Message, StringComparison.Ordinal);

        // And the two real routes do NOT reach it, because each one constructs its own client.
        Assert.IsType<ButtplugHapticSink>(HapticSinkFactory.CreateFor(HapticProviderRoute.Buttplug));
        Assert.IsType<LovenseHapticSink>(HapticSinkFactory.CreateFor(HapticProviderRoute.Lovense));
    }

    [Fact]
    public async Task AnUNADMITTEDSinkRefusesEVERYTHING_AndNamesTheADMITTEDPROVIDERGap()
    {
        using var sink = HapticSinkFactory.CreateFor(HapticProviderRoute.None);

        Assert.IsType<UnadmittedHapticSink>(sink);
        Assert.Equal(HapticProviderRoute.None, sink.Route);
        Assert.Null(sink.LastOutcome);

        var connect = Assert.IsType<CapabilityState.Unavailable>(
            await sink.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, connect.Reason.Code);
        Assert.Same(connect, sink.LastOutcome);

        var outputs = Assert.IsType<CapabilityState.Unavailable>(
            await sink.SetOutputsAsync("buttplug:0", [new HapticOutput(0, HapticLevel.Of(0.5))],
                TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, outputs.Reason.Code);

        // The all-stop refuses TOO, and that is the sharpest of the four. A sink that reported
        // Available for having stopped nothing would let a teardown pin read green on exactly the
        // build where the guarantee is worthless (Pointer/UnsupportedPointerSurface.Close's rule,
        // and upstream's reason at App.xaml.cs:4401-4404: an uncountermanded level outlives the app).
        var stop = Assert.IsType<CapabilityState.Unavailable>(await sink.StopAllAsync());
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, stop.Reason.Code);

        // And its OBSERVATION still says ClientAdmitted: false, which is the one thing this sink
        // exists to be able to say. It builds that observation itself rather than borrowing
        // HapticServerObservation.NotAsked, whose ClientAdmitted flipped to true when the second
        // route was admitted - borrowing it would have made this sink claim a client it has not got.
        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);
        Assert.False(observation.ClientAdmitted);
        Assert.Equal(
            HapticReasonCodes.HapticNoAdmittedProvider,
            Assert.IsType<CapabilityState.Unavailable>(observation.Classify()).Reason.Code);
    }

    [Fact]
    public void TheRefusalNEVERSaysNoDeviceFound_BecauseThereIsNoClientHereWithWhichToLook()
    {
        var detail = HapticSinkFactory.AdmissionGap;

        // It says so in as many words...
        Assert.Contains("THIS IS NOT \"no device found\"", detail, StringComparison.Ordinal);
        // ...and it does NOT carry the wording a build WITH a client and no toy would use
        // (ButtplugProvider.cs:135, LovenseProvider.cs:116).
        Assert.DoesNotContain("Connect your device in Intiface first", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Connect toy in Lovense app first", detail, StringComparison.OrdinalIgnoreCase);

        // And it names BOTH routes with their transports, because a seam justified against one
        // provider is the failure this capability was built to avoid.
        Assert.Contains("ws://127.0.0.1:12345", detail, StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:20010", detail, StringComparison.Ordinal);
        Assert.Contains("SEPARATE SERVER PROCESS", detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two routes are a SET and each gets its OWN client, which is what replaced
    /// <c>DescribeRoute</c>.
    ///
    /// <para>That method priced what each unadmitted route WOULD need — a NuGet package for
    /// Buttplug, a hold strategy for Lovense. Both were paid, so a fact pinning the price would be
    /// pinning a quotation for work that is finished. What has to hold instead is the shape the
    /// pricing was FOR: two independent clients, neither substituted for the other, in upstream's own
    /// preference order (<c>HapticDeviceManager.cs:21</c>).</para>
    /// </summary>
    [Fact]
    public void THETWOROUTESAreSEPARATEClients_NeitherSubstitutedForTheOther()
    {
        using var buttplug = HapticSinkFactory.CreateFor(HapticProviderRoute.Buttplug);
        using var lovense = HapticSinkFactory.CreateFor(HapticProviderRoute.Lovense);

        Assert.IsType<ButtplugHapticSink>(buttplug);
        Assert.IsType<LovenseHapticSink>(lovense);
        Assert.Equal(HapticProviderRoute.Buttplug, buttplug.Route);
        Assert.Equal(HapticProviderRoute.Lovense, lovense.Route);

        // Preference order is Lovense FIRST, which is upstream's and is held for upstream's reason:
        // "we keep the one with the richer API" (HapticDeviceManager.cs:19-21).
        Assert.Equal(
            [HapticProviderRoute.Lovense, HapticProviderRoute.Buttplug],
            HapticSinkFactory.AdmittedRoutes);
        Assert.Equal(HapticSinkFactory.AdmittedRoutes, CompositeHapticSink.Preference);

        // Neither client was constructed by asking for the OTHER route: a factory that fell back
        // would hand a Lovense user an Intiface socket, silently.
        Assert.IsNotType<LovenseHapticSink>(buttplug);
        Assert.IsNotType<ButtplugHapticSink>(lovense);
    }

    /// <summary>
    /// The manual gate is ATTEMPTABLE and still UNDISCHARGED, and it must say both.
    ///
    /// <para>It used to say the opposite of the first — <i>"CANNOT be attempted until a provider
    /// client is admitted"</i> — which was true then and is not now. The rung that must survive
    /// admission is the second: no automated step on any platform substitutes for a person reporting
    /// that a device moved and then stopped.</para>
    /// </summary>
    [Fact]
    public void THEDEVICEGateIsATTEMPTABLEAndStillUNDISCHARGED_WhichAreDifferentFacts()
    {
        var gate = HapticSinkFactory.DeviceManualGate;

        // The stale rung is GONE rather than reworded around.
        Assert.DoesNotContain("CANNOT be attempted until a provider client is admitted",
            gate, StringComparison.Ordinal);
        Assert.Contains("undischarged", gate, StringComparison.Ordinal);
        // The last step is the one nothing on any platform discharges, and it is stated as such.
        Assert.Contains("HUMAN", gate, StringComparison.Ordinal);
        Assert.Contains("STOPPED", gate, StringComparison.Ordinal);
    }

    /// <summary>
    /// An EMPTY route list is the user having ticked nothing — a typed refusal, never an exception
    /// and never the admission gap.
    ///
    /// <para><b>This fact changed meaning and the change is the point.</b> <c>CreateFrom([])</c> used
    /// to return <see cref="UnadmittedHapticSink"/>, because an empty list and an empty admitted list
    /// were the same thing. They are different questions now — "which routes does this build have a
    /// client for" versus "which did the user tick" — with different repairs, and a fact that still
    /// expected the old type would have been asking the product to tell a user this build has no
    /// client when it has two.</para>
    /// </summary>
    [Fact]
    public async Task ANEMPTYRouteListIsTheUserHavingTickedNOTHING_NotTheAdmissionGap()
    {
        using var nothingTicked = HapticSinkFactory.CreateFrom([]);

        Assert.IsType<CompositeHapticSink>(nothingTicked);
        Assert.Equal(HapticProviderRoute.None, nothingTicked.Route);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            await nothingTicked.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoProviderEnabled, refusal.Reason.Code);
        Assert.NotEqual(HapticReasonCodes.HapticNoAdmittedProvider, refusal.Reason.Code);

        // The refusal names a checkbox that EXISTS, which is the whole reason the two codes are
        // separate: "this build has no client" sends a user to wait for a release.
        Assert.Contains("Tick at least one route", refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("both routes have a client here", refusal.Reason.Detail, StringComparison.Ordinal);

        // And the product list yields real clients on the same expression, so the outcome is READ
        // off the route list rather than stipulated.
        using var product = HapticSinkFactory.CreateFrom(HapticSinkFactory.AdmittedRoutes);
        Assert.Equal(HapticProviderRoute.Lovense, product.Route);
    }

    /// <summary>
    /// Naming a route STILL never admits one. The refusal that used to fire for Buttplug fires for a
    /// route with no client, and it is the same refusal for the same reason.
    /// </summary>
    [Fact]
    public async Task ASinkNamedForANUNADMITTEDRouteRefuses_AndNamesTheADMISSIONGapAndNothingElse()
    {
        using var none = HapticSinkFactory.CreateFor(HapticProviderRoute.None);

        Assert.IsType<UnadmittedHapticSink>(none);
        Assert.Equal(HapticProviderRoute.None, none.Route);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            await none.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, refusal.Reason.Code);

        // It says what this build DOES have, so it can never be read as "the product cannot do
        // haptics" - the sentence this packet had to delete from a user-facing panel. (The detail
        // does contain the string "no device found", inside the clause that DENIES it; the denial
        // itself is pinned by TheRefusalNEVERSaysNoDeviceFound... above.)
        Assert.Contains("THIS BUILD HAS A CLIENT FOR BOTH OF THEM",
            refusal.Reason.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Connect your device in Intiface first",
            refusal.Reason.Detail, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A disposed sink answers with the DISPOSAL on every verb, INCLUDING the observation.
    ///
    /// <para><b>The observation arm is the one this packet had to repair.</b> Every sink's disposed
    /// <c>ObserveAsync</c> returned <c>HapticServerObservation.NotAsked</c>, which classified as the
    /// admission gap only because that value's <c>ClientAdmitted</c> was false. Flipping it to true
    /// made a RELEASED sink classify as <c>not-probed</c> — "nothing is known yet" about an object
    /// that will never know anything again, which is the one wording that would send somebody to
    /// wait. <see cref="HapticServerObservation.SinkDisposed"/> exists for exactly this state and all
    /// four sinks answer with it.</para>
    ///
    /// <para><b>The stop verb is asserted only where a disposed guard exists</b>, and the two that
    /// have none are named rather than quietly excluded: <c>LovenseHapticSink.StopAllAsync</c> and
    /// <c>ButtplugHapticSink.StopAllAsync</c> answer from their driven-device bookkeeping instead, so
    /// a released one that drove nothing reports <c>Degraded(haptic-no-device)</c>. That is
    /// unreachable on the product path — the composite refuses at its own disposed guard before it
    /// reaches a route sink — and widening it is not this packet's.</para>
    ///
    /// <para>No socket is opened by any row: every disposed sink returns before it reaches a
    /// wire.</para>
    /// </summary>
    [Theory]
    [InlineData("composite", true)]
    [InlineData("unadmitted", true)]
    [InlineData("lovense", false)]
    [InlineData("buttplug", false)]
    public async Task ADisposedSinkAnswersWithTheDISPOSAL_OnEVERYVerbIncludingTheObservation(
        string which, bool stopReportsTheDisposal)
    {
        var sink = which switch
        {
            "composite" => HapticSinkFactory.CreateFrom([]),
            "unadmitted" => HapticSinkFactory.CreateFor(HapticProviderRoute.None),
            "lovense" => HapticSinkFactory.CreateFor(HapticProviderRoute.Lovense),
            _ => HapticSinkFactory.CreateFor(HapticProviderRoute.Buttplug),
        };
        sink.Dispose();

        if (stopReportsTheDisposal)
        {
            var state = Assert.IsType<CapabilityState.Unavailable>(await sink.StopAllAsync());
            Assert.Equal(HapticReasonCodes.HapticSinkDisposed, state.Reason.Code);
        }

        // THE ARM THIS PACKET REPAIRED, and it holds on all four. Before it, every one of these
        // returned HapticServerObservation.NotAsked - which classified as the admission gap only
        // while that value's ClientAdmitted was false, and became "nothing is known yet" the moment
        // it was not.
        var observed = await sink.ObserveAsync(TestContext.Current.CancellationToken);
        Assert.False(observed.Asked);
        var classified = Assert.IsType<CapabilityState.Unavailable>(observed.Classify());
        Assert.Equal(HapticReasonCodes.HapticSinkDisposed, classified.Reason.Code);
        Assert.NotEqual(CapabilityReasonCodes.NotProbed, classified.Reason.Code);
        Assert.NotEqual(HapticReasonCodes.HapticNoAdmittedProvider, classified.Reason.Code);
    }

    [Fact]
    public async Task TheRefusingSinkValidatesItsArgumentsANYWAY()
    {
        using var sink = HapticSinkFactory.CreateFor(HapticProviderRoute.None);

        // A caller whose bad argument is swallowed by a refusing build discovers it on the day the
        // refusal stops, which is the day a real device is attached to it. Each guard is exercised
        // with the OTHER arguments valid, so none of them is only ever observed through another's
        // throw — the first draft of this fact passed an empty list with a whitespace key and saw
        // only the key's exception.
        await Assert.ThrowsAsync<ArgumentException>(
            () => sink.SetOutputsAsync("   ", [new HapticOutput(0, HapticLevel.Silent)],
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sink.SetOutputsAsync("buttplug:0", null!, TestContext.Current.CancellationToken));

        // IHapticSink documents that an empty list is a caller error and not a silent no-op, and a
        // contract only a comment believes in is not a contract.
        var empty = await Assert.ThrowsAsync<ArgumentException>(
            () => sink.SetOutputsAsync("buttplug:0", [], TestContext.Current.CancellationToken));
        Assert.Equal("outputs", empty.ParamName);
    }

    // =====================================================================================
    //  The observation and its classification — the truth table
    // =====================================================================================

    /// <summary>
    /// The arm order, row by row.
    ///
    /// <para><b>The <c>refused</c> column is the SECOND arm, and it was inserted without a row.</b>
    /// A truth table whose purpose is to pin arm ORDER is worth exactly the arms it enumerates, so
    /// the three rows that exercise it are the ones that say where it sits: BELOW the admission
    /// question (a build with no client must not be told about a checkbox), and ABOVE everything
    /// else — including a row whose other four fields would otherwise earn
    /// <see cref="CapabilityState.Available"/>.</para>
    /// </summary>
    [Theory]
    // asked, admitted, answered, devices, refused  ->  expected reason code (null = Available)
    [InlineData(false, false, false, 0, false, HapticReasonCodes.HapticNoAdmittedProvider)]
    [InlineData(true, false, true, 3, false, HapticReasonCodes.HapticNoAdmittedProvider)]
    [InlineData(false, true, false, 0, false, CapabilityReasonCodes.NotProbed)]
    [InlineData(false, true, true, 5, false, CapabilityReasonCodes.NotProbed)]
    [InlineData(true, true, false, 0, false, HapticReasonCodes.HapticServerUnreachable)]
    [InlineData(true, true, false, 4, false, HapticReasonCodes.HapticServerUnreachable)]
    [InlineData(true, true, true, 0, false, HapticReasonCodes.HapticNoDevice)]
    [InlineData(true, true, true, 1, false, null)]
    [InlineData(true, true, true, 9, false, null)]
    // The refusal arm. FIRST row: admission still outranks it, so a build with no client is never
    // told to tick a box that would not help. The other two: it outranks not-probed AND a
    // fully-answered observation, because a refusal decided before the wire is the only thing that
    // could have produced those fields and they cannot be trusted over it.
    [InlineData(false, false, false, 0, true, HapticReasonCodes.HapticNoAdmittedProvider)]
    [InlineData(false, true, false, 0, true, HapticReasonCodes.HapticNoProviderEnabled)]
    [InlineData(true, true, true, 3, true, HapticReasonCodes.HapticNoProviderEnabled)]
    public void THECLASSIFICATIONSArmOrderIsTheWholeDesign(
        bool asked, bool admitted, bool answered, int devices, bool refused, string? expectedCode)
    {
        var observation = Observation(asked, admitted, answered, devices, refused);

        var state = observation.Classify();

        // Unconditional, and load-bearing rather than a formality: Available and Confirmed are the
        // same claim written two ways, so every row of this table checks that they agree before it
        // checks which refusal the row earns.
        Assert.Equal(expectedCode is null, observation.Confirmed);
        Assert.Equal(expectedCode is null, state is CapabilityState.Available);

        if (expectedCode is null)
        {
            var available = Assert.IsType<CapabilityState.Available>(state);
            Assert.Contains(devices.ToString(System.Globalization.CultureInfo.InvariantCulture),
                available.Detail, StringComparison.Ordinal);
        }
        else if (expectedCode == HapticReasonCodes.HapticNoDevice)
        {
            // The ONE arm that is allowed to talk about a missing device, and it is a
            // DependencyMissing rather than an Unavailable because the thing that is missing is a
            // named external dependency the user can go and attach.
            var missing = Assert.IsType<CapabilityState.DependencyMissing>(state);
            Assert.Equal(expectedCode, missing.Reason.Code);
            Assert.Contains("haptic device", missing.Dependency, StringComparison.Ordinal);
        }
        else
        {
            var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(expectedCode, unavailable.Reason.Code);
        }
    }

    [Fact]
    public void ADMISSIONIsAskedFIRST_SoNoOtherFieldCanProduceAnAnswerAboutAServerWeCannotReach()
    {
        // The mutation this closes: move the admission arm below the others and a build with no
        // client reports "no device found" the moment somebody hands it an empty device list. That
        // refusal would send a user to plug in a toy that was never the problem.
        var everythingElsePerfect = Observation(asked: true, admitted: false, answered: true, devices: 0);

        var state = Assert.IsType<CapabilityState.Unavailable>(everythingElsePerfect.Classify());

        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, state.Reason.Code);
        Assert.NotEqual(HapticReasonCodes.HapticNoDevice, state.Reason.Code);
    }

    [Fact]
    public void CONFIRMEDAndAVAILABLEAgreeOnEVERYONEOfTheSixteenCombinations()
    {
        // Not an equivalence CLAIM: the whole input space of the four booleans (with the device
        // count in both of its meaningful states) is enumerated, so the two expressions are shown
        // to agree rather than argued to. The refusal field is null throughout; the theory above
        // carries the row where it is set and Confirmed must therefore be false.
        var rows = new List<(HapticServerObservation Observation, bool Expected)>();
        foreach (var asked in new[] { false, true })
        {
            foreach (var admitted in new[] { false, true })
            {
                foreach (var answered in new[] { false, true })
                {
                    foreach (var devices in new[] { 0, 2 })
                    {
                        var observation = Observation(asked, admitted, answered, devices);
                        rows.Add((observation, asked && admitted && answered && devices >= 1));
                    }
                }
            }
        }

        Assert.Equal(16, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(row.Expected, row.Observation.Confirmed);
            Assert.Equal(row.Expected, row.Observation.Classify() is CapabilityState.Available);
        });
        // Exactly ONE of the sixteen is Available — asked AND admitted AND answered AND a device —
        // which is the point: fifteen ways to be honest about not being able to do this, one way to
        // claim you can.
        Assert.Equal(1, rows.Count(r => r.Expected));
    }

    /// <summary>
    /// <c>NotAsked</c> claims NOTHING about a server, and exactly one thing about this build.
    ///
    /// <para>Every field was false while no route had a client, because "nothing was asked" and
    /// "there is nothing to ask with" were then the same fact. They are not, and
    /// <c>ClientAdmitted</c> is the field that separates them: a <c>false</c> here would make every
    /// un-probed moment classify as the admission gap and tell a user this build cannot talk to a
    /// haptic server at all.</para>
    /// </summary>
    [Fact]
    public void NOTASKEDClaimsNOTHINGAboutAServer_AndExactlyOneThingAboutThisBuild()
    {
        var notAsked = HapticServerObservation.NotAsked;

        Assert.False(notAsked.Asked);
        Assert.False(notAsked.ServerAnswered);
        Assert.Equal(HapticProviderRoute.None, notAsked.Route);
        Assert.Empty(notAsked.DeviceKeys);
        Assert.Equal(0, notAsked.DeviceCount);
        Assert.False(notAsked.Confirmed);
        Assert.Null(notAsked.Refused);

        // The one thing it asserts, and it is true of this build.
        Assert.True(notAsked.ClientAdmitted);
        Assert.NotEmpty(HapticSinkFactory.AdmittedRoutes);

        // So it classifies as NOT PROBED - never the admission gap, never a missing device, and
        // never a server that failed to answer.
        var state = Assert.IsType<CapabilityState.Unavailable>(notAsked.Classify());
        Assert.Equal(CapabilityReasonCodes.NotProbed, state.Reason.Code);
    }

    // =====================================================================================
    //  The level and the output line — the two-provider shape
    // =====================================================================================

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(4.2, 1.0)]
    public void ALEVELIsClampedIntoZeroToOne_WhichIsBOTHProvidersOwnBoundary(double raw, double expected)
    {
        // ButtplugProvider.cs:300 is Math.Clamp(intensity, 0.0, 1.0); LovenseProvider.cs:204 is
        // Math.Clamp(i, 0, 20) on its own quantized scale. Clamping here cannot make either behave
        // differently and it stops an out-of-range value ever reaching a wire.
        Assert.Equal(expected, HapticLevel.Of(raw).Value);
    }

    [Fact]
    public void ANANIsSILENCE_NeverAnUnhandledFaultOnThePathThatDrivesHardware()
    {
        Assert.True(HapticLevel.Of(double.NaN).IsSilent);
        Assert.Equal(0.0, HapticLevel.Of(double.NaN).Value);
        Assert.True(HapticLevel.Silent.IsSilent);
        Assert.False(HapticLevel.Of(0.01).IsSilent);
    }

    [Fact]
    public void ANOutputAddressesANONNEGATIVEActuatorIndex()
    {
        // The index is what makes this seam fit BOTH providers: upstream's legacy pair cannot drive
        // a two-motor device's motors apart, and upstream repaired exactly that in its v2 contract
        // ("Index disambiguates same-type motors (Edge=2 vibes, Lapis=3)", HapticContracts.cs:31-39).
        var output = new HapticOutput(2, HapticLevel.Of(0.25));
        Assert.Equal(2, output.ActuatorIndex);
        Assert.Equal(0.25, output.Level.Value);

        Assert.Throws<ArgumentOutOfRangeException>(() => new HapticOutput(-1, HapticLevel.Silent));
    }

    [Fact]
    public void THESEAMHasNoIsConnectedAndNoPing_WhichIsWhereUpstreamsOWNContractSplitInTwo()
    {
        // IHapticProvider.cs:23-28 requires PingAsync to touch the wire because "IsConnected can
        // lie"; LovenseProvider.cs:163-186 obeys and ButtplugProvider.cs:215-221 answers from the
        // cached field instead. This seam removes the field the shim answered from, so the divergence
        // is structurally impossible rather than forbidden by a comment.
        var members = typeof(IHapticSink).GetMembers().Select(m => m.Name).ToList();

        Assert.NotEmpty(members);
        Assert.DoesNotContain("IsConnected", members);
        Assert.DoesNotContain("get_IsConnected", members);
        Assert.DoesNotContain("PingAsync", members);
        // And there is no duration on the output verb: upstream's two implementations of
        // Vibrate(intensity, durationMs) disagree about who ends it (ButtplugProvider.cs:309-331 vs
        // LovenseProvider.cs:232-233 and :242-243), so the seam takes a LEVEL and the stop is its
        // own verb.
        Assert.DoesNotContain("VibrateAsync", members);
        Assert.Contains("SetOutputsAsync", members);
        Assert.Contains("StopAllAsync", members);
        Assert.Contains("ObserveAsync", members);
    }

    private static HapticServerObservation Observation(
        bool asked, bool admitted, bool answered, int devices, bool refused = false) =>
        new(asked,
            admitted ? HapticProviderRoute.Buttplug : HapticProviderRoute.None,
            admitted,
            answered,
            Enumerable.Range(0, devices)
                .Select(i => "buttplug:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToArray())
        {
            Refused = refused
                ? new CapabilityReason(
                    HapticReasonCodes.HapticNoProviderEnabled, CompositeHapticSink.NoProviderEnabledDetail)
                : null,
        };
}
