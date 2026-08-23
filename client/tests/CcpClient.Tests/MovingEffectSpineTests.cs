using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Overlay;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// FOUR modules under one spine: two paced, one static, one that has to keep moving.
///
/// <para><b>The claim under test.</b> The previous packet split the spine from the scheduler and
/// asked the next question out loud: does a module whose work is PER-FRAME end up re-implementing
/// a scheduler to get its frames? Every fact below fails if the answer is yes. The sharpest is
/// <see cref="PressingStart_ArmsAllFour_AndTheMovingModulesTwoTimersAreBothTheSURFACES"/>, which
/// counts what each module put on the session clock and asserts that the moving module put nothing
/// there at all — its presenter did.</para>
///
/// <para>The spiral surface here is the REAL <see cref="SpiralSurfacePresenter"/> over a recording
/// overlay presence and a stub decoder, precisely so the cadences it arms are on the clock this test
/// drives. Zero wall-clock.</para>
/// </summary>
public class MovingEffectSpineTests
{
    // ---------------------------------------------------------------------------------
    //  one session, four modules, and where each one's work really lives
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task PressingStart_ArmsAllFour_AndTheMovingModulesTwoTimersAreBothTheSURFACES()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();

        Assert.True(rig.Engine.Start());

        // FIVE timers, and every one of them is accounted for — this rig runs the REAL presenters
        // for both continuous modules, so their surface cadences are on this clock too:
        //   1 flash firing + 1 subliminal firing  -> the two PACED MODULES' schedules
        //   1 pink filter                         -> its SURFACE's topmost band. The MODULE has
        //                                            none: the tint's rig, which injects a recording
        //                                            double instead of the presenter, counts 2 here
        //   2 spiral                              -> its SURFACE's topmost band AND its frame
        //                                            advance. The MODULE has none either
        //
        // So of five timers, two belong to modules and three belong to surfaces, and the two
        // modules that draw a persistent layer own not one of them. A moving module that had grown
        // its own scheduler would show a SIXTH here, or would have taken the clock in its
        // constructor to arm one — which is what the tripwire in SpiralOverlayEffectTests pins at
        // the line.
        Assert.Equal(5, rig.Clock.PendingCount);
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.True(rig.Subliminals.ScheduleArmed);

        Assert.True(rig.SpiralSurface.Showing);
        Assert.True(rig.SpiralSurface.Running);
        Assert.Equal(EffectDotState.Live, rig.Spiral.Dot);
        Assert.Equal(EffectDotState.Live, rig.PinkFilter.Dot);
    }

    [Fact]
    public async Task WhileTheSessionRuns_TheSpiralKeepsChangingAndTheOtherThreeDoNot()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        var tintPlacements = rig.PinkPresence.Calls.Count(c => c == "present");
        var framesAtStart = rig.SpiralPresence.Calls.Count(c => c == "paint");

        for (var i = 0; i < 20; i++)
        {
            rig.Clock.Advance(SpiralFrameDelay.Default);
        }

        // THE WHOLE AXIS, in three assertions. The moving module repainted twenty times; the static
        // one placed nothing new and was never taken down; and neither of them re-presented a
        // window, because a frame is a paint.
        Assert.Equal(framesAtStart + 20, rig.SpiralPresence.Calls.Count(c => c == "paint"));
        Assert.Equal(1, rig.SpiralPresence.Calls.Count(c => c == "present"));
        Assert.Equal(tintPlacements, rig.PinkPresence.Calls.Count(c => c == "present"));
        Assert.True(rig.PinkFilterSurface.Showing);
    }

    [Fact]
    public async Task Stop_TakesTheSpiralOffAtOnce_AndNoAmountOfClockBringsAnyOfTheFourBack()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();
        rig.Engine.Start();
        rig.Clock.Advance(SpiralFrameDelay.Default);

        Assert.True(rig.Engine.Stop());

        // Stop DECIDES synchronously and QUEUES the teardown before it returns (this rig's dispatch
        // runs a post inline; on a real dispatcher the withdraw lands on the UI thread's next turn).
        // What is proved here is the ORDER: nothing is left scheduled and nothing can repaint
        // itself afterwards.
        Assert.False(rig.SpiralSurface.Showing);
        Assert.False(rig.SpiralSurface.Running);
        Assert.False(rig.PinkFilterSurface.Showing);
        Assert.Equal(0, rig.Clock.PendingCount);

        var frames = rig.SpiralPresence.Calls.Count(c => c == "paint");
        var flashes = rig.Flash.FlashCount;
        for (var i = 0; i < 40; i++)
        {
            rig.Clock.Advance(SpiralFrameDelay.Default);
        }

        Assert.Equal(frames, rig.SpiralPresence.Calls.Count(c => c == "paint"));
        Assert.Equal(flashes, rig.Flash.FlashCount);
        Assert.False(rig.SpiralSurface.Showing);
    }

    [Fact]
    public async Task ASessionThatNeverStarted_LeavesTheMovingModuleWithNoTimerAtAll()
    {
        await using var rig = await Rig.StartAsync();

        // The module ships ON (AppSettings.cs:2645) and there IS a spiral in this rig's library, so
        // the only thing keeping it off the screen is that nobody pressed START. A module that
        // armed its cadence at construction would be turning a layer nobody asked for.
        Assert.True(rig.Spiral.Enabled);
        Assert.Equal(0, rig.Clock.PendingCount);
        Assert.False(rig.SpiralSurface.Showing);
        Assert.Equal(EffectDotState.Armed, rig.Spiral.Dot);
    }

    // ---------------------------------------------------------------------------------
    //  the rack's second gesture, on a module that moves
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task TheQuickToggleStopsTheMovingModuleMidSession_AndTheFramesStopWithIt()
    {
        await using var rig = await Rig.StartAsync();
        rig.Engine.Start();
        Assert.True(rig.SpiralSurface.Showing);

        Assert.True(rig.Engine.QuickToggle(SpiralOverlayEffect.EffectId));

        Assert.False(rig.Spiral.Enabled);
        Assert.False(rig.SpiralSurface.Showing);
        Assert.Equal(EffectDotState.Off, rig.Spiral.Dot);

        // And the session records WHY, per module, exactly as it does for the other three.
        var outcome = Assert.IsType<CapabilityState.Unavailable>(
            rig.Engine.ArmOutcomes[SpiralOverlayEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.EffectDialOff, outcome.Reason.Code);

        // The frame cadence went with it: a stopped module that kept repainting would be the exact
        // defect a moving module can have and the other three cannot.
        var frames = rig.SpiralPresence.Calls.Count(c => c == "paint");
        rig.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(frames, rig.SpiralPresence.Calls.Count(c => c == "paint"));
    }

    [Fact]
    public async Task TheQuickToggleStartsItAgainMidSession_AndItIsMovingImmediately()
    {
        await using var rig = await Rig.StartAsync();
        rig.Engine.Start();
        var stopped = rig.Spiral.Completion;
        rig.Engine.QuickToggle(SpiralOverlayEffect.EffectId);
        Assert.False(rig.SpiralSurface.Showing);

        // Drained on purpose: this fact is about what the RE-ARM does, so the previous generation is
        // finished with before it runs. That the re-arm also survives an UNdrained one is the
        // separate claim of AStaleTeardownArrivingAfterARestart_….
        Assert.IsType<OperationOutcome.Cancelled>(await stopped!);

        Assert.True(rig.Engine.QuickToggle(SpiralOverlayEffect.EffectId));

        Assert.True(rig.Spiral.Enabled);
        Assert.True(rig.SpiralSurface.Showing);
        Assert.Equal(EffectDotState.Live, rig.Spiral.Dot);

        var frames = rig.SpiralPresence.Calls.Count(c => c == "paint");
        rig.Clock.Advance(SpiralFrameDelay.Default);
        Assert.Equal(frames + 1, rig.SpiralPresence.Calls.Count(c => c == "paint"));
    }

    [Fact]
    public async Task AStaleTeardownArrivingAfterARestart_MustNotTakeTheNewSessionsWorkDown()
    {
        // A DEFECT IN THE SHARED BODY, found by this module and fixed for all four
        // (OwnedSessionEffect.ReleaseIfStillOurs). The parked operation's tail runs on a thread-pool
        // continuation, so it arrives AFTER the Disarm that cancelled it — and "after" can be after
        // the module has been switched back on. It was found the honest way: the fact below
        // (TheQuickToggleStartsItAgainMidSession_…) went red inside a full-suite run and green on
        // its own, and the cause was a dead generation's teardown withdrawing the LIVE session's
        // layer and killing its cadence with it.
        //
        // WHAT THIS FACT IS AND IS NOT. Awaiting the old generations' completions guarantees their
        // tails HAVE run by the time the assertions execute, so this can never red spuriously — with
        // the guard in place it passes always. It is SOUND but NOT COMPLETE: whether the tail lands
        // before or after the re-arm is the thread pool's choice, so with the guard removed it fails
        // only on the runs where the bad ordering really happens (measured: it does happen under
        // suite load, and did not on an isolated run). The bound is stated here rather than glossed.
        //
        // THE REST OF THIS PARAGRAPH IS SUPERSEDED. It used to say a pin that could force the
        // ordering "would need either a wall-clock wait, which is banned, or a scheduler seam in the
        // shared body". Both were wrong: ATailThatLandsAfterSomethingWasPutBackUp_… below forces it
        // with neither, by parking the release itself on a gate the test opens. The sentence is
        // corrected rather than deleted, because the wrong half is why the defect went unpinned.
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        var spiralGeneration = rig.Spiral.Completion;
        var pinkGeneration = rig.PinkFilter.Completion;

        rig.Engine.QuickToggle(SpiralOverlayEffect.EffectId);
        rig.Engine.QuickToggle(PinkFilterEffect.EffectId);
        rig.Engine.QuickToggle(SpiralOverlayEffect.EffectId);
        rig.Engine.QuickToggle(PinkFilterEffect.EffectId);

        Assert.IsType<OperationOutcome.Cancelled>(await spiralGeneration!);
        Assert.IsType<OperationOutcome.Cancelled>(await pinkGeneration!);

        // Both continuous modules are back on screen, and the moving one is still MOVING — a stale
        // release would have killed its cadence as well as its layer, which is why this module is
        // the one that could see the defect at all.
        Assert.True(rig.SpiralSurface.Showing);
        Assert.True(rig.SpiralSurface.Running);
        Assert.True(rig.PinkFilterSurface.Showing);

        var frames = rig.SpiralPresence.Calls.Count(c => c == "paint");
        rig.Clock.Advance(SpiralFrameDelay.Default);
        Assert.Equal(frames + 1, rig.SpiralPresence.Calls.Count(c => c == "paint"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReleaseTaggedWithADeadGenerationReleasesNOTHING_AndOneTaggedWithTheLiveGenerationReleases(
        bool useTheLiveGeneration)
    {
        // THE DETERMINISTIC HALF OF THE FIX, and the one that reds on EVERY run when the guard
        // clause in OwnedSessionEffect.ReleaseIfStillOurs is deleted.
        //
        // The fact above it drives the same defect end to end, but whether the stale tail lands
        // before or after the re-arm is the thread pool's choice, so it is sound and not complete.
        // This one asks the predicate directly, which is available because AsyncOperationOwner
        // exposes its Generation and the guard is protected — the same shape
        // AsyncLifecycleTests.StaleGenerationCompletion_IsDiscarded_CannotOverwriteNewerState uses
        // to pin the registry's own stale-generation rule without a wall clock.
        //
        // This is SHARED code that all four modules inherit, which is why it is pinned as a
        // predicate rather than only as a story about one of them.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("GenerationProbe");
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        var probe = new GenerationProbe(owner, new EffectSignal(boundary, static () => true));

        probe.Arm();
        var dead = owner.Generation;

        probe.Disarm();
        probe.Arm();
        var live = owner.Generation;
        Assert.NotEqual(dead, live);

        var before = probe.Releases;
        probe.ReleaseFor(useTheLiveGeneration ? live : dead);

        // The dead generation's teardown must release nothing at all; the live one's must release.
        // Both arms are asserted, because a guard that refused EVERY release would pass the first
        // half and break every stop in the port.
        Assert.Equal(useTheLiveGeneration ? before + 1 : before, probe.Releases);
    }

    [Fact]
    public async Task ONESTOPRELEASESTHEWORKTHREETIMES_AndTheTHIRDIsOnAPoolThreadAfterDisarmReturned()
    {
        // The shared body's teardown is not one call and it does not all happen on the
        // caller's thread. Per stop, EXACTLY three:
        //   1. Disarm's own, synchronous, on the caller's thread (OwnedSessionEffect.cs:219);
        //   2. the generation's cancellation registration (:352), which runs inside
        //      AsyncOperationOwner.Cancel on the disarming thread when the parked body has already
        //      registered, and inline on the POOL thread when it has not (registering an
        //      already-cancelled token invokes the callback immediately);
        //   3. the tail after `await stopped.Task` (:357), which is a thread-pool continuation
        //      because the TCS carries RunContinuationsAsynchronously — so it lands an unbounded
        //      time after Disarm returned, and Disarm does not clear _generation, so the guard at
        //      :417 lets it through.
        // The count is deterministic and so is the ordering against Completion: the tail runs
        // BEFORE the owned task completes, which is exactly what makes awaiting Completion a real
        // edge rather than a hopeful pause. This is why the contract requires ReleaseWork to be
        // idempotent, and why a test that observes rig state after a disarm must await first.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("TailProbe");
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        var probe = new TailProbe(owner, new EffectSignal(boundary, static () => true));

        probe.Arm();
        var stopped = probe.Completion!;
        probe.Disarm();
        Assert.IsType<OperationOutcome.Cancelled>(await stopped);

        Assert.True(probe.Releases == 3,
            $"the owned task completed with {probe.Releases} releases behind it rather than 3. The tail's "
            + "release must be ordered BEFORE the completion, or awaiting Completion buys no ordering at all "
            + "and every post-disarm observation in this project is back to being the pool's choice");
    }

    [Fact]
    public async Task ATailThatLandsAfterSomethingWasPutBackUp_TAKESITDOWN_WhichIsWhyThePostDisarmAwaitIsNotOptional()
    {
        // The base tree was measured red 1 in 7 on
        // SpiralOverlayEffectTests.DisarmReleasesTheWorkUNCONDITIONALLY_…, a fact that touches no
        // OS at all — the strand that "the desktop was contended" cannot explain. THIS is the
        // mechanism, with the thread pool's choice taken away: the probe parks its third release
        // until this test opens the gate, so the interleaving that used to be a coin flip happens
        // every run.
        //
        // The control that would fail if the reading were wrong is the wait itself: if the tail did
        // NOT reach ReleaseWork after Disarm returned, TailArrived never completes and TestWait
        // fails loudly with CONDITION-NEVER-TRUE instead of quietly passing.
        //
        // Nothing here says the PRODUCT has a defect. On a real dispatcher the withdraw is posted
        // to the UI thread, every touch of a surface is single-threaded by construction, and a
        // second withdraw of an already-withdrawn surface is the no-op the contract requires. What
        // this pins is that a TEST which re-engages a surface behind the module's back, on a rig
        // whose dispatch runs posts inline on whatever thread posted them, is racing its own
        // module's teardown unless it awaits Completion first.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("TailProbe");
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        var probe = new TailProbe(owner, new EffectSignal(boundary, static () => true));

        probe.ParkReleaseNumber(3);
        probe.Arm();
        var stopped = probe.Completion!;
        try
        {
            // DisarmUnderObservation, not Disarm: it marks the thread that is INSIDE the disarm, so
            // the probe can refuse to park a release that arrived synchronously on it. Without that
            // refusal, a refactor making release 3 synchronous would park the only thread able to
            // open the gate (OpenGate is in the finally, BELOW the disarm) and this fact would HANG
            // rather than fail, in a suite with no per-test timeout. The test is exact rather than
            // a thread-id guess: a pool continuation cannot be running on that thread while that
            // thread is still inside Disarm, and once Disarm returns the mark is cleared, so a pool
            // thread that later REUSES the same managed id still parks. (A first draft compared
            // against the test thread's id alone and reddened this fact under full-suite load for
            // exactly that reason.)
            probe.DisarmUnderObservation();
            await TestWait.Until(
                probe.TailArrived,
                "the parked operation's tail to reach ReleaseWork after Disarm had already returned",
                () => $"releases so far = {probe.Releases}");

            // The re-engagement, standing in for SpiralOverlayEffectTests' direct Surface.Engage.
            probe.Live = true;
        }
        finally
        {
            probe.OpenGate();
        }

        Assert.IsType<OperationOutcome.Cancelled>(await stopped);
        Assert.False(
            probe.Live,
            "the re-engagement made after the disarm survived the parked release. Either the guard "
            + "at OwnedSessionEffect.cs:417 now blocks a release for the module's OWN live "
            + "generation, or release 3 has become synchronous inside Disarm and the park was "
            + "skipped to keep this fact from hanging — see DisarmUnderObservation");
        Assert.Equal(3, probe.Releases);
    }

    [Fact]
    public void TheGuardIsNotADisarmGuard_AReleaseForTheLiveGenerationStillWorksAfterAStopAndStart()
    {
        // The negative control for the theory above, stated separately because it is the failure a
        // too-eager guard would produce: after a stop and a start, the CURRENT generation's own
        // teardown must still work. A guard keyed on "has anything ever been superseded" rather
        // than on THIS generation would break every second session and nothing else would notice.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("GenerationProbe");
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        var probe = new GenerationProbe(owner, new EffectSignal(boundary, static () => true));

        probe.Arm();
        probe.Disarm();
        probe.Arm();
        probe.Disarm();
        probe.Arm();

        var before = probe.Releases;
        probe.ReleaseFor(owner.Generation);

        Assert.Equal(before + 1, probe.Releases);
    }

    [Fact]
    public async Task TheFourModulesDialsAreIndependent_AndSwitchingOneNeverMovesAnother()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        rig.Engine.QuickToggle(FlashImagesEffect.EffectId);

        Assert.False(rig.Flash.Enabled);
        Assert.False(rig.Subliminals.Enabled);
        Assert.True(rig.Spiral.Enabled);
        Assert.True(rig.PinkFilter.Enabled);
        Assert.True(rig.SpiralSurface.Showing);
        Assert.True(rig.PinkFilterSurface.Showing);
    }

    // ---------------------------------------------------------------------------------
    //  the shared body, and the fourth generation
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task EveryModuleOwnsItsOwnGeneration_AndAllFourTerminateCancelledOnStop()
    {
        await using var rig = await Rig.StartAsync();
        rig.EnableSubliminals();
        rig.EnablePinkFilter();
        rig.Engine.Start();

        var completions = new[]
        {
            rig.Flash.Completion,
            rig.Subliminals.Completion,
            rig.Spiral.Completion,
            rig.PinkFilter.Completion,
        };
        Assert.All(completions, Assert.NotNull);
        Assert.Equal(4, completions.Distinct().Count());

        rig.Engine.Stop();

        foreach (var completion in completions)
        {
            Assert.IsType<OperationOutcome.Cancelled>(await completion!);
        }
    }

    [Fact]
    public async Task HostTeardown_StopsTheMovingModuleToo_AndItsDotStopsClaimingToBeLive()
    {
        var rig = await Rig.StartAsync();
        rig.Engine.Start();
        Assert.Equal(EffectDotState.Live, rig.Spiral.Dot);

        await rig.Host.ShutdownAsync();

        // The participant's stop runs Engine.Stop() before the stores stop, and the host has already
        // cancelled every generation. Either route alone must leave the dot honest AND the cadence
        // dead — a repainting timer that outlived teardown would be a live handle on a dead process.
        Assert.False(rig.SpiralSurface.Showing);
        Assert.NotEqual(EffectDotState.Live, rig.Spiral.Dot);
        Assert.Equal(0, rig.Clock.PendingCount);
        await rig.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------
    //  the first-run state, which is what most users will actually see
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task WithNoSpiralInTheLibrary_TheSessionStillArmsIt_AndSaysWhatIsMissing()
    {
        await using var rig = await Rig.StartAsync(withSpiral: false);

        rig.Engine.Start();

        // Degraded, not Unavailable: the module really took the session. It is NOT in ArmRefusals
        // for the same reason Subliminals-over-an-empty-pool is not — a hole with a dial behind it
        // is a different thing from a module that declined.
        var degraded = Assert.IsType<CapabilityState.Degraded>(
            rig.Engine.ArmOutcomes[SpiralOverlayEffect.EffectId]);
        Assert.Equal(EffectReasonCodes.SpiralNoImage, degraded.Reason.Code);
        Assert.DoesNotContain(SpiralOverlayEffect.EffectId, rig.Engine.ArmRefusals.Select(r => r.Id));

        // And it costs the clock NOTHING AT ALL: with no content there is nothing to advance and no
        // surface to keep on top, so the only timer in the session is Flash Images' firing
        // (Subliminals and Pink Filter both ship off and are not enabled in this rig). A module
        // that armed a frame cadence over a layer it never placed would repaint a window that does
        // not exist for the whole session.
        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.True(rig.Flash.ScheduleArmed);
        Assert.False(rig.SpiralSurface.Showing);
        Assert.Equal(EffectDotState.Armed, rig.Spiral.Dot);
    }

    // =====================================================================================

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _directory;

        private Rig(
            ApplicationHost host, SessionParticipant session, ManualSessionClock clock,
            RecordingSpiralPresence spiralPresence, RecordingSpiralPresence pinkPresence, string directory)
        {
            Host = host;
            Session = session;
            Clock = clock;
            SpiralPresence = spiralPresence;
            PinkPresence = pinkPresence;
            _directory = directory;
        }

        public ApplicationHost Host { get; }

        public SessionParticipant Session { get; }

        public ManualSessionClock Clock { get; }

        /// <summary>The overlay the spiral really drew on. Its call log is the frame path.</summary>
        public RecordingSpiralPresence SpiralPresence { get; }

        /// <summary>The tint's overlay, for the contrast: it is placed once and never repainted.</summary>
        public RecordingSpiralPresence PinkPresence { get; }

        public SessionEngine Engine => Session.Engine;

        public FlashImagesEffect Flash => Session.Flash;

        public SubliminalsEffect Subliminals => Session.Subliminals;

        public SpiralOverlayEffect Spiral => Session.Spiral;

        public PinkFilterEffect PinkFilter => Session.PinkFilter;

        public ISpiralSurface SpiralSurface => Session.SpiralSurface;

        public IPinkFilterSurface PinkFilterSurface => Session.PinkFilterSurface;

        public static async Task<Rig> StartAsync(bool withSpiral = true)
        {
            var dir = Path.Combine(Path.GetTempPath(), "ccp-sp106-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (withSpiral)
            {
                var spirals = SpiralLibrary.Folder(SessionParticipant.AssetsRootFor(dir));
                Directory.CreateDirectory(spirals);
                // A real file, so SpiralLibrary really resolves it; its BYTES are never decoded,
                // because the frame source below is a stub. The decoder has its own facts.
                File.WriteAllBytes(Path.Combine(spirals, "classic.gif"), [0x00]);
            }

            var registry = new OperationRegistry();
            var log = new NullSink();
            var boundary = new UiDispatchBoundary();
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);
            var clock = new ManualSessionClock();
            var spiralPresence = new RecordingSpiralPresence();
            var pinkPresence = new RecordingSpiralPresence();

            // The REAL presenters over recording presences: what the modules put on the clock has to
            // be the clock this test drives, or the counting facts above would prove nothing.
            var spiralSurface = new SpiralSurfacePresenter(
                clock, action => action(), () => spiralPresence, new StubSpiralFrames(),
                () => new OverlayBounds(0, 0, 1920, 1080));
            var pinkSurface = new PinkFilterSurfacePresenter(
                clock, action => action(), () => pinkPresence,
                () => new OverlayBounds(0, 0, 1920, 1080));

            var session = new SessionParticipant(
                infra, dir, clock, new StubPool(3), new NullFlashSurface(), new NullSubliminalSurface(),
                static () => true, pinkSurface, spiralSurface);
            var host = new ApplicationHost(log, [session], new StartupTrace(), registry, infra.UiDispatch);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            return new Rig(host, session, clock, spiralPresence, pinkPresence, dir);
        }

        public void EnableSubliminals() => Session.SubliminalPreset.Mutate(p => p.Enabled = true);

        public void EnablePinkFilter() => Session.PinkFilterPreset.Mutate(p => p.Enabled = true);

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// The smallest possible module over the shared body: it owns no surface, no clock and no
    /// content, and exists only so the generation-guarded release can be asked directly.
    ///
    /// <para>It is here rather than in a module's own file because the guard is
    /// <see cref="OwnedSessionEffect"/>'s and all four modules inherit it.</para>
    /// </summary>
    private sealed class GenerationProbe(AsyncOperationOwner owner, EffectSignal signal)
        : OwnedSessionEffect(owner, signal, "generation-probe")
    {
        public int Releases { get; private set; }

        public override string Id => "generation-probe";

        public override string Title => "Generation probe";

        public override bool Enabled => true;

        public override void SetEnabled(bool enabled)
        {
        }

        /// <summary>Ask the shared guard directly, as a stale or a live teardown would.</summary>
        public void ReleaseFor(int generation) => ReleaseIfStillOurs(generation);

        protected override bool WorkIsRunning => false;

        protected override CapabilityState Engage(int generation) =>
            new CapabilityState.Available("generation probe: engaged");

        protected override void ReleaseWork() => Releases++;
    }

    /// <summary>
    /// A module that does nothing but COUNT its releases and, on request, hold one of them
    /// until the test lets it go — so the ordering the thread pool picks at random becomes the
    /// ordering this test chose.
    ///
    /// <para>The instrument does not carry the defect it measures: the counter is
    /// <see cref="Interlocked"/>, every field crossing the boundary is read and written through
    /// <see cref="Volatile"/>, and the gate is opened in a <c>finally</c> so a failed assertion can
    /// never leave a pool thread parked for the rest of the process.</para>
    /// </summary>
    private sealed class TailProbe(AsyncOperationOwner owner, EffectSignal signal)
        : OwnedSessionEffect(owner, signal, "tail-probe")
    {
        private readonly ManualResetEventSlim _gate = new(false);
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _releases;
        private int _parked = -1;
        private int _disarmingThread = -1;
        private int _live;

        public override string Id => "tail-probe";

        public override string Title => "Tail probe";

        public override bool Enabled => true;

        /// <summary>How many times the shared body has released this module's work.</summary>
        public int Releases => Volatile.Read(ref _releases);

        /// <summary>Completes when the parked release has ARRIVED and is waiting on the gate.</summary>
        public Task TailArrived => _arrived.Task;

        /// <summary>Stands in for "something is on screen": set by the test after the disarm, and
        /// cleared by any release that runs afterwards.</summary>
        public bool Live
        {
            get => Volatile.Read(ref _live) == 1;
            set => Volatile.Write(ref _live, value ? 1 : 0);
        }

        public override void SetEnabled(bool enabled)
        {
        }

        /// <summary>Hold the Nth release until <see cref="OpenGate"/>, unless it arrives
        /// synchronously inside <see cref="DisarmUnderObservation"/> — see there for why.</summary>
        public void ParkReleaseNumber(int number) => Volatile.Write(ref _parked, number);

        /// <summary>
        /// <see cref="OwnedSessionEffect.Disarm"/>, with the calling thread marked for as long as it
        /// is inside it.
        ///
        /// <para><b>Why the mark exists.</b> The gate is opened by the thread that called this, in a
        /// <c>finally</c> BELOW it. If a future change made the third release land SYNCHRONOUSLY
        /// inside the disarm, parking it would block the only thread that can ever open the gate and
        /// the fact would hang forever in a suite with no per-test timeout. With the mark it refuses
        /// to park that one release, the re-engagement survives, and the <c>Live</c> assertion reds
        /// with a verdict a reader can act on.</para>
        ///
        /// <para><b>Why the mark and not the thread id alone.</b> A pool continuation cannot be
        /// running on this thread while this thread is still inside <c>Disarm</c>, and the mark is
        /// cleared the moment it returns — so a pool thread that later REUSES the same managed id
        /// still parks, which is the common case under suite load and which a bare id comparison got
        /// wrong.</para>
        /// </summary>
        public void DisarmUnderObservation()
        {
            Volatile.Write(ref _disarmingThread, Environment.CurrentManagedThreadId);
            try
            {
                Disarm();
            }
            finally
            {
                Volatile.Write(ref _disarmingThread, -1);
            }
        }

        public void OpenGate() => _gate.Set();

        protected override bool WorkIsRunning => false;

        protected override CapabilityState Engage(int generation) =>
            new CapabilityState.Available("tail probe: engaged");

        protected override void ReleaseWork()
        {
            var number = Interlocked.Increment(ref _releases);
            if (number == Volatile.Read(ref _parked))
            {
                _arrived.TrySetResult();
                if (Environment.CurrentManagedThreadId != Volatile.Read(ref _disarmingThread))
                {
                    _gate.Wait(); // wallclock-allow: the wedge IS the subject — parks the release inside ReleaseWork so the disarm tail is observable; OpenGate() releases it
                }
            }

            Volatile.Write(ref _live, 0);
        }
    }

    /// <summary>A four-frame clip that never touches a file.</summary>
    private sealed class StubSpiralFrames : ISpiralFrameSource
    {
        public ISpiralAnimation? Open(string path, int width, int height) =>
            new StubAnimation(width, height);

        private sealed class StubAnimation(int width, int height) : ISpiralAnimation
        {
            public int FrameCount => 4;

            public TimeSpan FrameDelay => SpiralFrameDelay.Default;

            public OverlayFrame? Render(int index) =>
                OverlayFrame.Solid(width, height, (byte)index, (byte)index, (byte)index);

            public void Dispose()
            {
            }
        }
    }

    /// <summary>An overlay that records what it was asked to do, in order, and never touches a
    /// screen.</summary>
    private sealed class RecordingSpiralPresence : IOverlayPresence
    {
        private OverlaySurfaceRequest? _current;

        public List<string> Calls { get; } = [];

        public bool IsPresenting => _current is not null;

        public CapabilityState Present(OverlaySurfaceRequest request)
        {
            Calls.Add("present");
            _current = request;
            return new CapabilityState.Available("recording presence: placed");
        }

        public CapabilityState Paint(OverlayFrame frame)
        {
            Calls.Add("paint");
            return new CapabilityState.Available("recording presence: painted");
        }

        public void Reassert() => Calls.Add("reassert");

        public CapabilityState SetClickThrough(bool clickThrough)
        {
            Calls.Add("set-click-through");
            return new CapabilityState.Available("recording presence: flipped");
        }

        public CapabilityState Withdraw()
        {
            Calls.Add("withdraw");
            _current = null;
            return new CapabilityState.Available("recording presence: withdrawn");
        }

        public void Dispose() => _current = null;
    }

    private sealed class NullFlashSurface : IFlashSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(IReadOnlyList<string> images)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class NullSubliminalSurface : ISubliminalSurface
    {
        public CapabilityState? LastPlacement => null;

        public void Show(SubliminalCard card)
        {
        }

        public void HideAll()
        {
        }
    }

    private sealed class StubPool(int population) : IFlashImagePool
    {
        public IReadOnlyList<string> Draw(int count) =>
            [.. Enumerable.Range(0, Math.Min(count, population)).Select(i => $"image-{i}.png")];
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }

    /// <summary>The manual clock, in the shape every module test shares. Zero wall-clock.</summary>
    private sealed class ManualSessionClock : ISessionClock
    {
        private readonly List<Entry> _timers = [];

        public DateTimeOffset UtcNow { get; private set; } = new(2026, 8, 19, 0, 0, 0, TimeSpan.Zero);

        public int PendingCount
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Count;
                }
            }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            var entry = new Entry { Due = UtcNow + due, Fire = fire };
            lock (_timers)
            {
                _timers.Add(entry);
            }

            return new Handle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => t.Due <= UtcNow).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class Entry
        {
            public DateTimeOffset Due { get; init; }

            public required Action Fire { get; init; }
        }

        private sealed class Handle(ManualSessionClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
