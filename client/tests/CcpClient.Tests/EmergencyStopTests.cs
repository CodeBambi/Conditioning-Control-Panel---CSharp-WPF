using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The two ways the landed emergency stop could still fail the person using it.</b>
///
/// <para><b>Hazard 1 — the stop could disable its own escape hatch.</b> Nothing on the disarm path
/// was wrapped: <c>ReleaseWork</c>/<c>OnDisarmed</c> are native window teardown, decoders and audio,
/// and one of them throwing meant (a) every module after it in rack order kept its surfaces up,
/// (b) that module's own generation was never cancelled so its schedule kept firing, (c) the
/// engine's flag stayed true, and (d) the exception died inside <c>Win32PanicKey</c>'s window
/// procedure, which catches and — until this change — said nothing. The user was then left pressing
/// a chord that re-entered the same throwing branch forever, because the shell's exit rung sat
/// behind that flag.</para>
///
/// <para><b>Hazard 2 — nothing answered a press the UI thread could not take.</b> A measurement on
/// this product at maximum settings recorded the UI thread failing to answer its message loop for
/// 607–1734 ms at a stretch, peaking past a 2000 ms probe ceiling. <see cref="PanicWatchdog"/> is
/// what happens after the press has been seen and the answer has not come back.</para>
///
/// <para><b>Why these live in one file.</b> They are one chain — a release that throws, a rack that
/// must still come down, a sentence the user must be able to read, and a press that must be
/// answered by something. The shared-body fact below would otherwise belong beside the other
/// <see cref="OwnedSessionEffect"/> pins in <c>MovingEffectSpineTests</c>; it is here because the
/// guard it pins was added for this hazard and reads as noise anywhere else.</para>
///
/// <para><b>What no green run here proves.</b> Nothing in this file renders, presses a real key,
/// touches a window or ends a process: <see cref="PanicWatchdog.TerminateThisProcess"/> is never
/// called (a fact cannot kill the process it runs in — that reading is
/// <see cref="SurfaceExitObservations"/>'s, across a process boundary), and the shell's own wiring
/// of these parts is <see cref="PanicKeyTests"/>'s and the headless ladder's.</para>
/// </summary>
public class EmergencyStopTests
{
    // ---------------------------------------------------------------------------------
    //  hazard 1: a module that throws on the way down
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task ADisarmWhoseReleaseTHROWS_StillCANCELSTheModulesOwnGeneration()
    {
        // THE ROOT CAUSE, driven directly at the shared body every rack module inherits rather than
        // through any one module. Disarm used to call ReleaseWork() and OnDisarmed() and only THEN
        // cancel — so a subclass that threw skipped the cancel entirely, and the module's schedule
        // was still live after the user had pressed STOP. Nothing anywhere else in the port could
        // have caught that: the engine sees an exception either way.
        var registry = new OperationRegistry();
        var owner = registry.OwnerFor("ThrowingProbe");
        var probe = new ThrowingProbe(owner, Signal());

        probe.Arm();
        var generation = owner.Generation;
        Assert.True(owner.IsLive(generation), "the arm did not begin a generation, so there is nothing for the stop to cancel");

        // The throw is NOT swallowed by the base class, and that matters as much as the cancel:
        // the engine one layer up is what turns it into a typed, readable outcome, and a base that
        // ate it would put the failure back beyond everyone's reach.
        var thrown = Assert.Throws<InvalidOperationException>(probe.Disarm);
        Assert.Equal(ThrowingProbe.Boom, thrown.Message);

        // More than one release per stop is the shared body's DESIGNED behaviour, not a defect:
        // MovingEffectSpineTests pins exactly three (the synchronous one, the cancellation
        // callback's, and the parked operation's tail), which is why ReleaseWork's contract requires
        // idempotence. All this asserts is that the release really was attempted.
        Assert.True(probe.ReleaseAttempts >= 1, "the shared body never asked this module to let go at all");

        Assert.False(owner.IsLive(generation),
            "the module's generation is STILL LIVE after a disarm that threw — its schedule can keep firing on a "
            + "session the user has already stopped, which is the defect this guard exists for");

        // AND THE OWNED OPERATION ENDS. This is the same defect one layer further down, found by
        // this fact rather than by reading: the parked operation's cancellation callback released
        // the work and only THEN signalled, so a release that threw left the module's completion
        // hanging for the life of the process — teardown's drain would spend its whole bounded wait
        // on it and record it unobserved. Bounded here so a regression reds with a verdict instead
        // of hanging a suite that has no per-test timeout.
        await TestWait.Until(probe.Completion!, "the throwing module's owned operation to terminate",
            () => $"releases={probe.ReleaseAttempts}");

        // Failed, not Cancelled, and that is the honest answer for a module whose release NEVER
        // succeeds: the operation really did end in a failure, and the registry says so in type.
        var outcome = await probe.Completion!;
        var failed = Assert.IsType<OperationOutcome.Failed>(outcome);
        Assert.Contains(ThrowingProbe.Boom, failed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModuleThatThrowsWhileStopping_DisarmsEveryModuleAFTERIt_AndTheSessionNamesTheOneThatBroke()
    {
        await using var rig = await EngineRig.StartAsync();
        var thrower = new ThrowingProbe(rig.Registry.OwnerFor("Thrower"), Signal(), id: "thrower");
        var follower = new CountingProbe(rig.Registry.OwnerFor("Follower"), Signal(), id: "follower");

        // Rack order matters and is the whole point: the module that throws is FIRST, so on the old
        // code the second one was never reached at all.
        var engine = new SessionEngine([thrower, follower], rig.Preset);

        Assert.True(engine.Start());
        Assert.True(engine.Running);
        Assert.IsType<CapabilityState.Available>(engine.ArmOutcomes["follower"]);

        Assert.True(engine.Stop(), "the stop did not run at all");

        Assert.True(follower.ReleaseAttempts > 0,
            "the module AFTER the throwing one was never disarmed. On a real rack that is every surface below the "
            + "break still on the user's screen after they pressed the emergency stop");
        Assert.False(engine.Running,
            "the engine still claims to own a session. That flag is what the shell's STOP caption and the panic "
            + "ladder's stop rung read, so leaving it true is what made the exit unreachable");

        // The failure is a FACT the session carries, in the same typed vocabulary every other module
        // refusal uses — not a log line and not an exception somebody's catch swallowed.
        var (id, reason) = Assert.Single(engine.StopFailures);
        Assert.Equal("thrower", id);
        Assert.Equal(EffectReasonCodes.EffectDisarmFailed, reason.Code);
        Assert.Contains(ThrowingProbe.Boom, reason.Detail, StringComparison.Ordinal);

        var recorded = Assert.IsType<CapabilityState.Unavailable>(engine.ArmOutcomes["thrower"]);
        Assert.Equal(EffectReasonCodes.EffectDisarmFailed, recorded.Reason.Code);
        Assert.Contains(engine.ArmRefusals, r => r.Id == "thrower");

        // And the module that behaved is not smeared with the other one's failure.
        Assert.IsType<CapabilityState.Available>(engine.ArmOutcomes["follower"]);

        // A second, clean stop clears the record: StopFailures always describes the LAST stop, or a
        // toast about a module that has since come down would follow the user around all session.
        Assert.True(engine.Start());
        thrower.StopThrowing();
        Assert.True(engine.Stop());
        Assert.Empty(engine.StopFailures);
    }

    [Fact]
    public async Task AModuleThatThrowsWhileSTARTING_DoesNotStopTheRestOfTheRackFromArming()
    {
        // The same defect on the other edge, and it is not symmetric in severity — a start that
        // half-happens is visible to the user, a stop that half-happens traps them. It is guarded
        // anyway because the exception's exits are worse: a click handler, or a scheduler tick on a
        // pool thread with nobody watching.
        await using var rig = await EngineRig.StartAsync();
        var thrower = new ThrowingProbe(rig.Registry.OwnerFor("Thrower"), Signal(), id: "thrower", onArm: true);
        var follower = new CountingProbe(rig.Registry.OwnerFor("Follower"), Signal(), id: "follower");
        var engine = new SessionEngine([thrower, follower], rig.Preset);

        Assert.True(engine.Start());
        Assert.True(engine.Running);

        Assert.IsType<CapabilityState.Available>(engine.ArmOutcomes["follower"]);
        var recorded = Assert.IsType<CapabilityState.Unavailable>(engine.ArmOutcomes["thrower"]);
        Assert.Equal(EffectReasonCodes.EffectArmFailed, recorded.Reason.Code);
    }

    [Fact]
    public async Task TheSentenceTheUserREADSNamesEveryModuleThatBroke_AndOffersOnlyAWayOutThisProcessREALLYHolds()
    {
        await using var rig = await EngineRig.StartAsync();
        var first = new ThrowingProbe(rig.Registry.OwnerFor("First"), Signal(), id: "first");
        var second = new ThrowingProbe(rig.Registry.OwnerFor("Second"), Signal(), id: "second");
        var engine = new SessionEngine([first, second], rig.Preset);

        // A clean session says NOTHING. Without this arm the fact below would pass over an
        // implementation that warned the user on every single stop.
        Assert.True(engine.Start());
        first.StopThrowing();
        second.StopThrowing();
        Assert.True(engine.Stop());
        Assert.Null(engine.StopFailureNotice("STOP", "Ctrl+Alt+Esc"));

        Assert.True(engine.Start());
        first.ThrowAgain();
        second.ThrowAgain();
        Assert.True(engine.Stop());

        var held = engine.StopFailureNotice("The emergency stop", "Ctrl+Alt+Esc");
        Assert.NotNull(held);
        Assert.Contains("first", held, StringComparison.Ordinal);
        Assert.Contains("second", held, StringComparison.Ordinal);
        Assert.Contains("The emergency stop", held, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Alt+Esc", held, StringComparison.Ordinal);

        // And when the OS refused the chord, the sentence must not send the user to press it. A
        // way out that does not exist is worse than no advice at all — it is the "dead panic key
        // somebody believes in" the shell already refuses to ship.
        var refused = engine.StopFailureNotice("STOP", panicGesture: null);
        Assert.NotNull(refused);
        Assert.DoesNotContain("Ctrl+Alt+Esc", refused, StringComparison.Ordinal);
        Assert.Contains("first", refused, StringComparison.Ordinal);
        Assert.Contains("second", refused, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------
    //  hazard 2: the press the UI thread never takes
    // ---------------------------------------------------------------------------------

    [Fact]
    public async Task APressTheUiThreadNEVERAnswers_RunsTheRealTeardownOffThread_AndTHENEndsTheProcess()
    {
        var lab = new WatchdogLab(deliverToUi: false);

        lab.Watchdog.Press();
        await TestWait.Until(lab.Terminated, "the watchdog to end a process whose UI thread never answered the panic press",
            () => lab.Trace);

        // ORDER IS THE FACT, not the pair. Ending the process first would cost the user everything
        // the teardown's reserved flush slot writes; running the teardown and then NOT ending the
        // process would leave every surface up, because a window belongs to the thread that created
        // it and the premise here is that this thread is unreachable.
        Assert.Equal(["teardown", "terminate"], lab.Steps);
        Assert.Equal(1, lab.Terminations);
        Assert.Contains(lab.Log, l => l.Contains("FALLBACK", StringComparison.Ordinal));
    }

    [Fact]
    public void APressTheUiThreadDOESAnswer_EndsNothingAndTearsNothingDown()
    {
        // The non-vacuous half. Same watchdog, same zero deadline, one difference: the UI thread
        // takes the work. If this ever went red the escalation would be firing on healthy presses,
        // which would make the emergency stop the most dangerous control in the application.
        var lab = new WatchdogLab(deliverToUi: true);

        lab.Watchdog.Press();

        Assert.Equal(1, lab.Handled);
        Assert.Empty(lab.Steps);
        Assert.Equal(0, lab.Terminations);
        Assert.Equal(0, lab.Watchdog.Outstanding);
    }

    [Fact]
    public async Task ASecondPressWhileTheFirstIsSTILLUnanswered_EndsTheProcessWithoutWaitingOutTheDeadline()
    {
        // The user's own lever, and the reason the deadline can afford to be longer than upstream's
        // two seconds. The deadline here is a minute, so nothing that fires can be the timer: the
        // ONLY thing that can end this process is the second press.
        var lab = new WatchdogLab(deliverToUi: false, deadline: TestWait.InjectedBudget);

        lab.Watchdog.Press();
        Assert.Equal(1, lab.Watchdog.Outstanding);
        Assert.Equal(0, lab.Terminations);

        lab.Watchdog.Press();
        await TestWait.Until(lab.Terminated, "the second unanswered press to end the process", () => lab.Trace);
        Assert.Equal(1, lab.Terminations);
    }

    [Fact]
    public async Task WhileTheApplicationIsALREADYTearingDown_TheFallbackStandsDown()
    {
        // Upstream asks exactly this first (MainWindow/MainWindow.xaml.cs:936-946) and for the same
        // reason: the double-press rung ends in a shutdown that outlives any deadline, and a
        // fallback firing into it would race the teardown it is duplicating and truncate the
        // settings flush that teardown is in the middle of.
        var lab = new WatchdogLab(deliverToUi: false) { TeardownStarted = true };

        lab.Watchdog.Press();
        await TestWait.Until(
            () => lab.Log.Any(l => l.Contains("stands down", StringComparison.Ordinal)),
            "the watchdog to stand down while the application is already tearing down",
            () => lab.Trace, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, lab.Terminations);
        Assert.Empty(lab.Steps);

        // And standing down must not SPEND the escalation: the wedge that comes after a shutdown
        // which never finishes is exactly the one still worth answering.
        lab.TeardownStarted = false;
        lab.Watchdog.Press();
        await TestWait.Until(lab.Terminated, "a later press to escalate after an earlier one stood down", () => lab.Trace);
        Assert.Equal(1, lab.Terminations);
    }

    [Fact]
    public void AHandlerThatTHROWS_IsSaidOutLoudAndCountsAsAnAnswer()
    {
        // A thread that can throw is a thread that is ALIVE, so this is not the watchdog's subject
        // and it must not end the process over it. What changed is that it is no longer silent:
        // this exception used to die inside Win32PanicKey's window procedure with the comment
        // "this class holds no log sink", which made the emergency path the quietest one in the app.
        var lab = new WatchdogLab(deliverToUi: true, handlerThrows: true);

        lab.Watchdog.Press();

        Assert.Equal(0, lab.Terminations);
        Assert.Equal(0, lab.Watchdog.Outstanding);
        Assert.Contains(lab.Log, l => l.Contains("panic handler threw", StringComparison.Ordinal)
            && l.Contains(WatchdogLab.HandlerBoom, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATeardownThatItselfWEDGES_DoesNotStopTheProcessFromEnding()
    {
        // The teardown is bounded from inside (ApplicationHost bounds every participant's stop), but
        // the premise of this whole path is that something in this process is not answering. A
        // fallback that waited forever on the teardown would leave the user under the surfaces with
        // the one lever that still works unpulled.
        var lab = new WatchdogLab(deliverToUi: false, teardownWedges: true);

        lab.Watchdog.Press();
        await TestWait.Until(lab.Terminated, "a wedged teardown to be abandoned and the process ended anyway",
            () => lab.Trace);

        Assert.Equal(["teardown", "terminate"], lab.Steps);
        Assert.Equal(1, lab.Terminations);
        Assert.Contains(lab.Log, l => l.Contains("abandoned", StringComparison.Ordinal));
    }

    // ==================================================================================
    //  instruments
    // ==================================================================================

    private static EffectSignal Signal()
    {
        var boundary = new UiDispatchBoundary();
        boundary.Bind(new InlineDispatch());
        return new EffectSignal(boundary, static () => true);
    }

    /// <summary>
    /// A watchdog with every edge of its world under the test's hand: whether the UI thread takes
    /// the work, whether the handler throws, whether the teardown ever finishes, and whether the
    /// application is already on its way down. The termination is COUNTED rather than performed —
    /// a fact cannot kill the process it is running in.
    /// </summary>
    private sealed class WatchdogLab
    {
        internal const string HandlerBoom = "the shell's panic handler is broken";

        private readonly List<string> _log = [];
        private readonly List<string> _steps = [];
        private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _teardownWedge = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _handled;
        private int _terminations;

        public WatchdogLab(
            bool deliverToUi,
            bool handlerThrows = false,
            bool teardownWedges = false,
            TimeSpan? deadline = null)
        {
            Watchdog = new PanicWatchdog(
                // The UI thread, present or absent. "Absent" is not a slow thread: it is the
                // delegate never running at all, which is what a pump that has stopped looks like
                // from out here.
                post: action =>
                {
                    if (deliverToUi)
                    {
                        action();
                    }
                },
                handler: () =>
                {
                    Interlocked.Increment(ref _handled);
                    if (handlerThrows)
                    {
                        throw new InvalidOperationException(HandlerBoom);
                    }
                },
                log: line =>
                {
                    lock (_log)
                    {
                        _log.Add(line);
                    }
                },
                teardownStarted: () => TeardownStarted,
                teardown: () =>
                {
                    Step("teardown");
                    return teardownWedges ? _teardownWedge.Task : Task.CompletedTask;
                },
                terminate: () =>
                {
                    Step("terminate");
                    Interlocked.Increment(ref _terminations);
                    _terminated.TrySetResult();
                },
                // Zero unless a fact needs the deadline NOT to be what fires: an unanswered press
                // is already unanswered at zero, so no clock decides anything here.
                deadline: deadline ?? TimeSpan.Zero);
        }

        public PanicWatchdog Watchdog { get; }

        public bool TeardownStarted { get; set; }

        public int Handled => Volatile.Read(ref _handled);

        public int Terminations => Volatile.Read(ref _terminations);

        public Task Terminated => _terminated.Task;

        public IReadOnlyList<string> Steps
        {
            get
            {
                lock (_steps)
                {
                    return [.. _steps];
                }
            }
        }

        public IReadOnlyList<string> Log
        {
            get
            {
                lock (_log)
                {
                    return [.. _log];
                }
            }
        }

        /// <summary>Everything the watchdog did and said, for a failure message that can be acted on.</summary>
        public string Trace => $"steps=[{string.Join(", ", Steps)}] outstanding={Watchdog.Outstanding} "
            + $"terminations={Terminations} log=[{string.Join(" | ", Log)}]";

        private void Step(string name)
        {
            lock (_steps)
            {
                _steps.Add(name);
            }
        }
    }

    /// <summary>
    /// A module over the real shared body whose release throws on demand. It owns no surface, no
    /// clock and no content: the subject is the base class's teardown, not any module's work.
    /// </summary>
    private sealed class ThrowingProbe(
        AsyncOperationOwner owner, EffectSignal signal, string id = "throwing-probe", bool onArm = false)
        : OwnedSessionEffect(owner, signal, id)
    {
        internal const string Boom = "this module's native teardown failed";

        private bool _throwing = true;

        public override string Id { get; } = id;

        public override string Title => "Throwing probe";

        public override bool Enabled => true;

        /// <summary>How many times the shared body asked this module to let go.</summary>
        public int ReleaseAttempts { get; private set; }

        public override void SetEnabled(bool enabled)
        {
        }

        public void StopThrowing() => _throwing = false;

        public void ThrowAgain() => _throwing = true;

        protected override bool WorkIsRunning => false;

        protected override CapabilityState Engage(int generation) =>
            onArm && _throwing
                ? throw new InvalidOperationException(Boom)
                : new CapabilityState.Available("throwing probe: engaged");

        protected override void ReleaseWork()
        {
            ReleaseAttempts++;
            if (!onArm && _throwing)
            {
                throw new InvalidOperationException(Boom);
            }
        }
    }

    /// <summary>The module that comes AFTER the broken one in rack order, and the only thing it does
    /// is remember whether anybody ever asked it to stop.</summary>
    private sealed class CountingProbe(AsyncOperationOwner owner, EffectSignal signal, string id)
        : OwnedSessionEffect(owner, signal, id)
    {
        public override string Id { get; } = id;

        public override string Title => "Counting probe";

        public override bool Enabled => true;

        public int ReleaseAttempts { get; private set; }

        public override void SetEnabled(bool enabled)
        {
        }

        protected override bool WorkIsRunning => false;

        protected override CapabilityState Engage(int generation) =>
            new CapabilityState.Available("counting probe: engaged");

        protected override void ReleaseWork() => ReleaseAttempts++;
    }

    /// <summary>A real registry and a real preset store on a real temp folder — the engine's two
    /// constructor arguments and nothing else. The rack modules are supplied per fact.</summary>
    private sealed class EngineRig : IAsyncDisposable
    {
        public required string Directory { get; init; }

        public required OperationRegistry Registry { get; init; }

        public required PersistenceStore<SessionPresetDocument> Preset { get; init; }

        public static async Task<EngineRig> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "ccp-emergency-stop-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var registry = new OperationRegistry();
            var preset = new PersistenceStore<SessionPresetDocument>(
                registry.OwnerFor("Preset"), new NullSink(), Path.Combine(directory, "session.json"),
                SessionPresetDocument.CurrentSchemaVersion);
            await preset.StartAsync(TestContext.Current.CancellationToken);
            return new EngineRig { Directory = directory, Registry = registry, Preset = preset };
        }

        public async ValueTask DisposeAsync()
        {
            await Preset.StopAsync();
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class NullSink : ILogSink
        {
            public void Log(string message)
            {
            }
        }
    }

    /// <summary>The dispatch boundary, run where it is posted. Declared here rather than shared, the
    /// convention every module suite in this project already follows: independent instruments, so two
    /// readings are never one code path.</summary>
    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }
}
