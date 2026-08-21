using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-134. The pre-flight's VERDICT, pinned against synthetic observations so every branch is
/// exercised without a desktop and without contending for one.
///
/// <para><b>Why the verdict is separable at all.</b> <see cref="DesktopPreflight.Observe"/> is
/// impure by nature — it raises windows and reads the screen — and a rule that can only be
/// exercised by arranging a real contended desktop is a rule nobody can regression-test. So the
/// sampling produces a <see cref="DesktopPreflight.Observation"/> and
/// <see cref="DesktopPreflight.Refusal"/> judges it, and these facts feed it observations by hand.
/// What they do NOT establish is that the sampler produces faithful ones; that is the in-collection
/// control in <see cref="DesktopPreflightTests"/>, plus the two real-window demonstrations recorded
/// in this packet's <c>record.md</c>.</para>
///
/// <para><b>This class is deliberately NOT in <see cref="RealDesktopCollection"/></b>: it opens no
/// window, reads no pixel and asks the window manager nothing, so putting it behind the
/// machine-wide lease would serialise pure arithmetic against the desktop for no evidence — the
/// same call, for the same reason, that <c>VideoLetterboxTests.cs:9-11</c> makes. That does leave
/// this file carrying two classes with one <c>[Collection]</c> attribute between them, which is
/// exactly the lexical blind spot <c>RealDesktopCollectionGuardTests.cs:29-35</c> already names for
/// <c>RealDesktopLeaseTests.cs</c>: the attribute is per-FILE to that guard, so a real-desktop
/// class added to this file later would be accepted without joining anything. Neither class here
/// reaches the desktop, so nothing is mis-bound today, and this paragraph is the record of it
/// rather than an inference someone has to make later.</para>
/// </summary>
public class DesktopPreflightVerdictTests
{
    [Fact]
    public void AForeignWindowThatTookAContendedPoint_Refuses_NamingTheProcessTheTitleAndTheOwnership()
    {
        var refusal = DesktopPreflight.Refusal(Contended(
            Loss(processName: "SomeOtherApp", title: "A Foreign Window", processId: 4242)));

        // The whole difference between nine runs of elimination and one line of diagnosis: the
        // refusal has to NAME the thing, not describe the condition.
        Assert.NotNull(refusal);
        Assert.Contains("DESKTOP-CONTENDED", refusal, StringComparison.Ordinal);
        Assert.Contains("SomeOtherApp", refusal, StringComparison.Ordinal);
        Assert.Contains("A Foreign Window", refusal, StringComparison.Ordinal);
        Assert.Contains("pid 4242", refusal, StringComparison.Ordinal);
        Assert.Contains("took a contended point away", refusal, StringComparison.Ordinal);

        // And it must say what it is NOT, because the one repair a reader must never reach for is
        // the one that makes this green.
        Assert.Contains("NOT a flake", refusal, StringComparison.Ordinal);
        Assert.Contains("NOT retried", refusal, StringComparison.Ordinal);
        Assert.Contains("NOT skipped", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ASentinelThatHeldEveryPointForTheWholeSpan_IsClean()
    {
        var refusal = DesktopPreflight.Refusal(Clean());

        Assert.Null(refusal);
    }

    /// <summary>
    /// THE CADENCE FACT, and the reason the rule is any-sample rather than a majority. The
    /// contender re-asserts <c>HWND_TOPMOST</c> on a ~1 s beat
    /// (<c>Services/Chaos/ChaosModeService.cs:937-940</c> into
    /// <c>Services/Flash/FlashService.cs:206-243</c>), so its loss can be a thin slice of the span.
    /// A detector that wanted most of the readings to be lost before it spoke would report a clean
    /// desktop and then watch the same three tests fail — a green light in front of the same red.
    /// </summary>
    [Fact]
    public void AForeignOwnerAtASingleReadingOutOfHundreds_StillRefuses_BecauseTheContenderReAssertsOnACadence()
    {
        var oneInSevenFifty = Contended(
            Loss(processName: "SomeOtherApp", title: "A Foreign Window", processId: 4242, readings: 1),
            samples: 250,
            ownedReadings: 749);

        var refusal = DesktopPreflight.Refusal(oneInSevenFifty);

        Assert.NotNull(refusal);
        Assert.Contains("DESKTOP-CONTENDED", refusal, StringComparison.Ordinal);
        Assert.Contains("1 of 750 readings", refusal, StringComparison.Ordinal);

        // The counts travel WITH the refusal precisely because this rule cannot itself distinguish
        // a cadence from a one-shot occluder, and saying so is the honest half of the trade.
        Assert.Contains("ONE-SHOT OR CADENCE?", refusal, StringComparison.Ordinal);
        Assert.Contains("ANY-sample", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroReadingObservationOnAWindowsHost_RefusesAsBlind_BecauseADetectorThatSawNothingMustNotReportClean()
    {
        var sampledNothing = Clean() with { Samples = 0, OwnedReadings = 0, SpanMs = 0 };

        var refusal = DesktopPreflight.Refusal(sampledNothing);

        Assert.NotNull(refusal);
        Assert.Contains("PREFLIGHT-BLIND", refusal, StringComparison.Ordinal);
        Assert.Contains("established NOTHING", refusal, StringComparison.Ordinal);
        Assert.Contains("This is a failure, never a skip", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ARigThatCouldNotBeBuilt_RefusesAsBlind_RatherThanReportingTheDesktopClean()
    {
        var noRig = Clean() with { RigFailure = "RegisterClassExW returned 0", Samples = 0, OwnedReadings = 0 };

        var refusal = DesktopPreflight.Refusal(noRig);

        Assert.NotNull(refusal);
        Assert.Contains("PREFLIGHT-BLIND", refusal, StringComparison.Ordinal);
        Assert.Contains("RegisterClassExW returned 0", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The vacuity guard on the happy path. Readings taken but never once owned is not a clean
    /// desktop and is not a named contender either; it is a pre-flight that proved nothing, and it
    /// has to say which of the two it is rather than defaulting to the reassuring one.
    /// </summary>
    [Fact]
    public void AnObservationThatNeverOnceOwnedAPoint_RefusesRatherThanReportingClean()
    {
        var neverOwned = Clean() with { Samples = 250, OwnedReadings = 0 };

        var refusal = DesktopPreflight.Refusal(neverOwned);

        Assert.NotNull(refusal);
        Assert.Contains("PREFLIGHT-BLIND", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// Trap 3: our own capture raises real windows, and a pre-flight that cannot tell it from a
    /// foreign product is useless. It still refuses — our product contends for the band exactly as
    /// anything else does — but it sends the reader after the harness contract instead of after an
    /// application that is behaving normally.
    /// </summary>
    [Fact]
    public void OurOwnHarnessOrProduct_StillRefuses_ButIsNamedAsOursAndPointsAtTheLeaseContract()
    {
        var refusal = DesktopPreflight.Refusal(Contended(
            Loss(processName: "CcpClient.Desktop", title: "Conditioning Control Panel", processId: 777)));

        Assert.NotNull(refusal);
        Assert.Contains("OUR OWN harness or product", refusal, StringComparison.Ordinal);
        Assert.Contains("verification-harness.md:39", refusal, StringComparison.Ordinal);
        Assert.Contains("hunt a harness bug, not a foreign application", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("THIS IS THE SHIPPING WPF PRODUCT", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowOfOURSThatIsNotTheSentinel_IsNamedAsALeakedRig_NotAsSomethingOnTheMachine()
    {
        var refusal = DesktopPreflight.Refusal(Contended(
            Loss(processName: "CcpClient.Tests", title: "ccp overlay probe", processId: Environment.ProcessId)));

        Assert.NotNull(refusal);
        Assert.Contains("THIS PROCESS owns that window", refusal, StringComparison.Ordinal);
        Assert.Contains("left on the desktop", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// The known cause, reachable in one line instead of nine runs. When the owner is the shipping
    /// WPF product the refusal carries the standing citations AND the reason an idle copy is
    /// harmless, so the reader is not left wondering why the application they have had open all
    /// week only broke the suite today.
    /// </summary>
    [Fact]
    public void TheShippingWpfProduct_CarriesItsStandingCitationAndTheIdleVersusRunningDistinction()
    {
        var refusal = DesktopPreflight.Refusal(Contended(
            Loss(processName: "ConditioningControlPanel", title: "Conditioning Control Panel v6.8.1", processId: 16712)));

        Assert.NotNull(refusal);
        Assert.Contains("THIS IS THE SHIPPING WPF PRODUCT", refusal, StringComparison.Ordinal);
        Assert.Contains("RealDesktopCollection.cs:45-49", refusal, StringComparison.Ordinal);
        Assert.Contains("FlashService.cs:206-243", refusal, StringComparison.Ordinal);
        Assert.Contains("ChaosModeService.cs:930", refusal, StringComparison.Ordinal);
        Assert.Contains("has a session RUNNING", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostWithNoDesktopToObserve_IsNotObserved_AndIsNeitherCleanNorARefusal()
    {
        var refusal = DesktopPreflight.Refusal(DesktopPreflight.NotObserved);

        // No refusal, because there was nothing to refuse ABOUT: this is the non-Windows answer.
        Assert.Null(refusal);

        // And it must never be mistaken for a measured clean desktop, which is what the recorded
        // observation is for: it says plainly that it observed nothing.
        Assert.False(DesktopPreflight.NotObserved.Observed);
        Assert.Equal(0, DesktopPreflight.NotObserved.Samples);
        Assert.Equal(0, DesktopPreflight.NotObserved.Points);
        Assert.Contains("not observed", DesktopPreflight.NotObserved.ForegroundAtStart, StringComparison.Ordinal);
    }

    [Fact]
    public void TheObservationSpanCoversAtLeastTwoOfUpstreamsReAssertBeats()
    {
        // The span is not a taste: one beat inside it would let a contender land entirely between
        // readings. Two is the floor, and this pins the arithmetic rather than the comment.
        Assert.True(
            DesktopPreflight.ObservationSpanMs >= 2 * DesktopPreflight.UpstreamReassertPeriodMs,
            $"the pre-flight watches for {DesktopPreflight.ObservationSpanMs} ms against an upstream re-assert "
            + $"period of {DesktopPreflight.UpstreamReassertPeriodMs} ms (ChaosModeService.cs:937 fires every "
            + "fourth tick of the :503 250 ms timer): a span shorter than two beats can miss the cadence entirely");
        Assert.True(DesktopPreflight.MinimumSamples > 0);
        Assert.Equal(3, DesktopPreflight.ContendedPoints);
    }

    private static DesktopPreflight.Observation Clean() => new(
        Observed: true,
        Samples: 250,
        SpanMs: DesktopPreflight.ObservationSpanMs,
        Points: DesktopPreflight.ContendedPoints,
        OwnedReadings: 750,
        LostReadings: 0,
        Losses: [],
        ForegroundAtStart: "'a terminal' (process 'WindowsTerminal', pid 1, thread 2, window 0x3, class 'x')",
        ForegroundAtEnd: "'a terminal' (process 'WindowsTerminal', pid 1, thread 2, window 0x3, class 'x')",
        RigFailure: null);

    private static DesktopPreflight.Observation Contended(
        DesktopPreflight.Loss loss, int samples = 250, int ownedReadings = 700) =>
        Clean() with
        {
            Samples = samples,
            OwnedReadings = ownedReadings,
            LostReadings = loss.Readings,
            Losses = [loss],
        };

    private static DesktopPreflight.Loss Loss(
        string processName, string title, int processId, int readings = 50) =>
        new(
            PointIndex: 1,
            X: 823,
            Y: 514,
            FirstSampleIndex: 41,
            FirstElapsedMs: 412,
            Readings: readings,
            Window: 0x130938,
            ProcessId: processId,
            ProcessName: processName,
            Title: title,
            ClassName: "HwndWrapper[Something;;guid]",
            Rect: "(42,19)-(1605,962)");
}

/// <summary>
/// SP-134's broken-detector control, and the one fact here that must run inside the collection.
///
/// <para><b>What it is for.</b> Every other fact in this file judges the verdict against
/// observations written by hand. This one judges the OBSERVATION, taken by the real sampler, on the
/// real desktop, moments before the collection's first OS-level fact — because the failure mode a
/// pre-flight is most likely to reach on its own is not a wrong refusal but a silent one: a sampler
/// that raises nothing, reads nothing and reports a clean desktop over zero evidence. This
/// collection is RUNNING, so the pre-flight must have passed, and this pins what "passed" is
/// allowed to mean.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class DesktopPreflightTests(RealDesktopLease lease)
{
    [Fact]
    public void ThePreflightReallyObservedTheDesktop_BeforeAnyRealDesktopFactRanAndOverASpanThatCoversTheCadence()
    {
        var observation = lease.Preflight;
        var expected = DesktopPreflight.HostCanBeObserved;

        // Every reading it took, it OWNED. The collection is running, so the pre-flight returned no
        // refusal, and this is what that has to mean rather than an assertion about nothing.
        Assert.Equal(observation.Samples * observation.Points, observation.OwnedReadings);
        Assert.Empty(observation.Losses);
        Assert.Equal(0, observation.LostReadings);
        Assert.Null(observation.RigFailure);

        // Both branches are ASSERTED rather than returned from, so this fact cannot silence itself
        // on a host where the desktop is absent: it pins NotObserved just as hard as it pins a real
        // observation (VacuousShapeDetector's early-return/platform-predicate shapes are exactly
        // the two ways a fact like this rots into a no-op).
        Assert.Equal(expected, observation.Observed);
        Assert.Equal(expected ? DesktopPreflight.ContendedPoints : 0, observation.Points);
        Assert.Equal(expected, observation.Samples >= DesktopPreflight.MinimumSamples);
        Assert.Equal(expected, observation.SpanMs >= 2 * DesktopPreflight.UpstreamReassertPeriodMs);
        Assert.Equal(expected, observation.SpanMs >= DesktopPreflight.ObservationSpanMs);

        // And it looked at a real foreground window rather than leaving the field at its
        // never-reached placeholder, which is what a rig that threw before the span would leave.
        Assert.DoesNotContain("not reached", observation.ForegroundAtStart, StringComparison.Ordinal);
        Assert.DoesNotContain("not reached", observation.ForegroundAtEnd, StringComparison.Ordinal);
    }
}
