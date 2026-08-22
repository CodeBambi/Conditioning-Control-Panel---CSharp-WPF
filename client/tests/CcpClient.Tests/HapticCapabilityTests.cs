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
    /// THIS build owns a Lovense client, and the refusal above is now a statement about the
    /// unadmitted CASE rather than about this product.
    ///
    /// <para>The pair matters. Every refusal fact in this file used to reach the product factory
    /// directly, which made them true by accident of what was admitted; they now ask for the empty
    /// route list explicitly, and this fact holds the other end so "admitted" cannot quietly become
    /// "admitted and doing nothing".</para>
    /// </summary>
    [Fact]
    public void ThisBuildOwnsALovenseClient_SoTheAdmittedListAndTheSinkAgree()
    {
        Assert.Contains(HapticProviderRoute.Lovense, HapticSinkFactory.AdmittedRoutes);

        using var sink = HapticSinkFactory.Create();
        Assert.Equal(HapticProviderRoute.Lovense, sink.Route);
        Assert.IsType<LovenseHapticSink>(sink);

        // Nothing was asked of a server by CONSTRUCTING it. The client exists; whether a server is
        // running is a separate question this does not touch.
        Assert.Null(sink.LastOutcome);
    }

    /// <summary>A route admitted with no client behind it still THROWS. Admitting Lovense must not
    /// have relaxed the check that catches a fake-available sink - it stops firing for Lovense
    /// because Lovense now has a client, and for nothing else.</summary>
    [Fact]
    public void ARouteWithNoClientStillThrows_BecauseAdmissionDidNotRelaxTheCheck()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => HapticSinkFactory.CreateFrom([HapticProviderRoute.Buttplug]));
        Assert.Contains("admit the route AND", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUNADMITTEDSinkRefusesEVERYTHING_AndNamesTheADMITTEDPROVIDERGap()
    {
        using var sink = HapticSinkFactory.CreateFrom([]);

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

    [Fact]
    public void THETWOROUTESAreDescribedDIFFERENTLY_BecauseTheyCostDifferentThings()
    {
        var buttplug = HapticSinkFactory.DescribeRoute(HapticProviderRoute.Buttplug);
        var lovense = HapticSinkFactory.DescribeRoute(HapticProviderRoute.Lovense);

        // Buttplug is the packet's stopping line: it needs a package this project does not carry.
        Assert.Contains("Buttplug 5.0.1", buttplug, StringComparison.Ordinal);
        Assert.Contains("ONE file", buttplug, StringComparison.Ordinal);
        Assert.Contains("message spec v4", buttplug, StringComparison.Ordinal);

        // Lovense needs no HAPTICS-SPECIFIC package — the shipping provider's wire imports are pure
        // BCL at :1-7, and its one non-BCL using is the app's own logger (Serilog at :8), which this
        // port does not need because it logs through ILogSink. This is the sentence the owner acts
        // on, so it is pinned including the qualifier: "imports only the BCL" was WRONG as written
        // and a reviewer checked it.
        Assert.Contains("NO HAPTICS-SPECIFIC package", lovense, StringComparison.Ordinal);
        Assert.Contains("LovenseProvider.cs:1-7", lovense, StringComparison.Ordinal);
        Assert.Contains("Serilog at :8", lovense, StringComparison.Ordinal);
        // Its real cost is the hold, because the LAN API expires its own command.
        Assert.Contains("timeSec", lovense, StringComparison.Ordinal);
        Assert.DoesNotContain("Buttplug 5.0.1", lovense, StringComparison.Ordinal);

        // The two must not have collapsed into one sentence: that collapse IS the trap.
        Assert.NotEqual(buttplug, lovense);
    }

    [Fact]
    public void THEDEVICEGateIsMarkedDOWNSTREAMOfAdmission_SoNobodyReadsItAsTodaysProblem()
    {
        var gate = HapticSinkFactory.DeviceManualGate;

        Assert.Contains("CANNOT be attempted until a provider client is admitted", gate, StringComparison.Ordinal);
        // The last step is the one nothing on any platform discharges, and it is stated as such.
        Assert.Contains("HUMAN", gate, StringComparison.Ordinal);
        Assert.Contains("STOPPED", gate, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonEmptyAdmittedRouteListWithNoSinkBehindIt_THROWSRatherThanFakingOne()
    {
        // The guard that stops this factory ever manufacturing a no-op for an admitted route. It is
        // executable only because CreateFrom takes the list: a guard nothing can run is a comment
        // with a keyword in front of it.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => HapticSinkFactory.CreateFrom([HapticProviderRoute.Buttplug]));
        Assert.Contains("Buttplug", thrown.Message, StringComparison.Ordinal);

        // The other side of the same expression, now that a route IS admitted. This used to assert
        // an empty product list; that day arrived, and the point it was making survives intact —
        // the outcome is READ off the route list rather than stipulated, so the empty list still
        // refuses and the product list now yields a real client.
        Assert.IsType<UnadmittedHapticSink>(HapticSinkFactory.CreateFrom([]));
        using var product = HapticSinkFactory.CreateFrom(HapticSinkFactory.AdmittedRoutes);
        Assert.Equal(HapticProviderRoute.Lovense, product.Route);
    }

    [Fact]
    public async Task ASinkNamedForAROUTEStillRefuses_AndAddsWhatTHATRouteWouldNeed()
    {
        using var buttplug = HapticSinkFactory.CreateFor(HapticProviderRoute.Buttplug);
        using var lovense = HapticSinkFactory.CreateFor(HapticProviderRoute.Lovense);

        // Naming a route still never ADMITS one — Buttplug has no client, so it carries Route.None
        // and refuses with what that specific route would need. Lovense answers differently now
        // only because it is on the admitted list, which is the distinction this fact exists for.
        Assert.Equal(HapticProviderRoute.None, buttplug.Route);
        Assert.Equal(HapticProviderRoute.Lovense, lovense.Route);

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            await buttplug.ConnectAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HapticReasonCodes.HapticNoAdmittedProvider, refusal.Reason.Code);
        Assert.Contains("Buttplug 5.0.1", refusal.Reason.Detail, StringComparison.Ordinal);

        // The per-route text is still per-route: Buttplug's refusal names the package it needs,
        // and nothing about Buttplug leaks into the admitted route's answer.
        Assert.DoesNotContain("Buttplug 5.0.1",
            LovenseHapticSink.DefaultBaseUrl + HapticSinkFactory.DescribeRoute(HapticProviderRoute.Lovense),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADisposedSinkAnswersWithTheDISPOSAL_NotWithTheAdmissionGap()
    {
        var sink = HapticSinkFactory.CreateFrom([]);
        sink.Dispose();

        var state = Assert.IsType<CapabilityState.Unavailable>(await sink.StopAllAsync());
        Assert.Equal(HapticReasonCodes.HapticSinkDisposed, state.Reason.Code);
    }

    [Fact]
    public async Task TheRefusingSinkValidatesItsArgumentsANYWAY()
    {
        using var sink = HapticSinkFactory.CreateFrom([]);

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

    [Theory]
    // asked, admitted, answered, devices  ->  expected reason code (null = Available)
    [InlineData(false, false, false, 0, HapticReasonCodes.HapticNoAdmittedProvider)]
    [InlineData(true, false, true, 3, HapticReasonCodes.HapticNoAdmittedProvider)]
    [InlineData(false, true, false, 0, CapabilityReasonCodes.NotProbed)]
    [InlineData(false, true, true, 5, CapabilityReasonCodes.NotProbed)]
    [InlineData(true, true, false, 0, HapticReasonCodes.HapticServerUnreachable)]
    [InlineData(true, true, false, 4, HapticReasonCodes.HapticServerUnreachable)]
    [InlineData(true, true, true, 0, HapticReasonCodes.HapticNoDevice)]
    [InlineData(true, true, true, 1, null)]
    [InlineData(true, true, true, 9, null)]
    public void THECLASSIFICATIONSArmOrderIsTheWholeDesign(
        bool asked, bool admitted, bool answered, int devices, string? expectedCode)
    {
        var observation = Observation(asked, admitted, answered, devices);

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
        // to agree rather than argued to.
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

    [Fact]
    public void NOTASKEDIsTheOnlyValueThisBuildProduces_AndEveryFieldInItIsFalse()
    {
        var notAsked = HapticServerObservation.NotAsked;

        Assert.False(notAsked.Asked);
        Assert.False(notAsked.ClientAdmitted);
        Assert.False(notAsked.ServerAnswered);
        Assert.Equal(HapticProviderRoute.None, notAsked.Route);
        Assert.Empty(notAsked.DeviceKeys);
        Assert.Equal(0, notAsked.DeviceCount);
        Assert.False(notAsked.Confirmed);
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

    private static HapticServerObservation Observation(bool asked, bool admitted, bool answered, int devices) =>
        new(asked,
            admitted ? HapticProviderRoute.Buttplug : HapticProviderRoute.None,
            admitted,
            answered,
            Enumerable.Range(0, devices)
                .Select(i => "buttplug:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ToArray());
}
