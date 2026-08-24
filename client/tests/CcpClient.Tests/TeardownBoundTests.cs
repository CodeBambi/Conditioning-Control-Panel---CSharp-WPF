using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The precondition every surface's lifetime rests on: teardown has to RETURN.</b>
///
/// <para><b>Why this is a safety fact and not a tidiness one.</b> On the ordinary path the
/// application's native surfaces are destroyed by the operating system at process exit and by
/// nothing else — the disposals are posted to a UI thread that is blocked inside teardown for the
/// whole of it (<c>Session/SessionParticipant.cs:927-951</c>; <c>App.axaml.cs:95</c> calls
/// <c>ShutdownAsync().GetAwaiter().GetResult()</c> from the lifetime's Exit handler). That makes a
/// surface's lifetime the PROCESS's. So a teardown that never returns is an application that never
/// exits, and two of the six surfaces are deliberately not click-through
/// (<c>Pointer/Win32PointerSurface.cs:850-852</c>, <c>Input/Win32InputPresence.cs:1097-1099</c>):
/// the user is left with a topmost window eating their clicks or their keyboard and nothing on
/// screen to close it.</para>
///
/// <para><b>The one wait that was not bounded, and where it was already named.</b>
/// <c>ApplicationHost.ShutdownAsync</c> bounds its registry drain and its settings flush; its
/// participant loop awaited each <c>StopAsync()</c> with no budget of its own. The sibling guard
/// <see cref="UnboundedWaitGuardTests"/> names that in its own remarks as an outstanding
/// <i>"lifecycle fix, not a lint finding"</i>. This is the fix, and this is its fact.</para>
///
/// <para><b>What it composes with.</b> <c>SurfaceExitTests</c> establishes, in a real process on a
/// real desktop, that a teardown which RETURNS is followed by a process that dies and by a window
/// manager holding nothing of it. Together they answer the whole question without a third child
/// process: nothing inside teardown can hold the process alive with its surfaces up.</para>
///
/// <para><b>What it does not cover, stated plainly.</b> A <c>StopAsync</c> that blocks its caller
/// BEFORE returning a task — a hung native call on the teardown thread itself — never reaches the
/// bounded wait, and no bound taken on the blocked thread could reach it. That case is named in
/// <c>ApplicationHost</c>'s own remark and its only remedy is termination, which
/// <c>SurfaceExitTests.AbnormalTermination_ReclaimsTheSurfacesToo_...</c> measures.</para>
/// </summary>
public class TeardownBoundTests
{
    /// <summary>
    /// The short budget here is deliberate and it is the SUBJECT: this fact is about the bound
    /// elapsing. Positional, exactly as <c>AsyncLifecycleTests</c>'s two bounded-drain facts pass
    /// theirs (<c>AsyncLifecycleTests.cs:279</c>, <c>:299</c>).
    /// </summary>
    private static readonly TimeSpan ShortBound = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// <b>THE FIX'S FACT.</b> A participant whose stop never completes does not hold teardown, and
    /// teardown carries on to the participants behind it.
    ///
    /// <para><b>Mutation that reds it:</b> delete the bounded wait from
    /// <c>ApplicationHost.ShutdownAsync</c>'s participant loop and go back to
    /// <c>await _participants[i].StopAsync()</c>. <c>ShutdownAsync</c> then never completes, the
    /// bounded wait below expires with <c>TIMING-VERDICT:CONDITION-NEVER-TRUE</c>, and the fact
    /// fails as itself rather than hanging the run.</para>
    /// </summary>
    [Fact]
    public async Task AStopThatNeverCompletes_DoesNotHoldTeardown_AndTheParticipantsBehindItStillStop()
    {
        var log = new ListLog();
        var wedged = new StopNeverCompletes();
        var behind = new RecordsItsStop();

        // Reverse start order reaches the wedge FIRST, so `behind` is only stopped at all if
        // teardown really does continue past an abandoned stop.
        var host = new ApplicationHost(
            log, [behind, wedged], new StartupTrace(), new OperationRegistry(), new UiDispatchBoundary(), ShortBound);

        var teardown = host.ShutdownAsync();
        await TestWait.Until(
            teardown,
            "ApplicationHost.ShutdownAsync to return despite a participant whose stop never completes",
            () => $"the wedged participant was asked to stop: {wedged.WasAsked}; the one behind it stopped: {behind.Stopped}");

        Assert.True(wedged.WasAsked, "the wedged participant was never asked to stop, so nothing was bounded");
        Assert.True(behind.Stopped,
            "teardown abandoned the wedged participant and then stopped, so every participant registered before it "
            + "was never torn down at all");
        Assert.True(host.IsShutdown);

        var abandoned = Assert.Single(log.Lines, l => l.Contains("was abandoned", StringComparison.Ordinal));
        Assert.Contains(wedged.Name, abandoned, StringComparison.Ordinal);
        Assert.Contains("so the process can exit", abandoned, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The bound is a backstop, not a policy.</b> A well-behaved stop is still awaited to
    /// completion and is never cut short, and teardown still runs in reverse start order — the
    /// shape <c>TeardownTests</c> pins. Without this, the fix above could have been "stop waiting
    /// for anybody", which would silently truncate every real teardown.
    /// </summary>
    [Fact]
    public async Task AWellBehavedStop_IsStillAwaitedToCompletion_InReverseStartOrder()
    {
        var log = new ListLog();
        var order = new List<string>();
        var first = new RecordsItsStop("first", order);
        var second = new RecordsItsStop("second", order);

        var host = new ApplicationHost(
            log, [first, second], new StartupTrace(), new OperationRegistry(), new UiDispatchBoundary(), ShortBound);

        await host.ShutdownAsync();

        Assert.Equal(["second", "first"], order);
        Assert.True(first.Stopped);
        Assert.True(second.Stopped);
        Assert.DoesNotContain(log.Lines, l => l.Contains("was abandoned", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>And what teardown walked away from does not vanish.</b> Nobody awaits an abandoned stop
    /// any more, so a failure it reaches afterwards would reach nobody at all: unobserved by any
    /// caller — measured, <c>Task.WhenAny</c> does NOT observe the exception of the task that lost
    /// its race — and, worse, absent from the log. A participant that eventually failed to shut its
    /// device or its file down would leave no trace of it anywhere.
    ///
    /// <para><b>Mutation that reds it:</b> delete the <c>ContinueWith</c> in
    /// <c>ApplicationHost.RecordItIfItEverFails</c>. The late failure then goes unrecorded and the
    /// single-line assertion below finds nothing.</para>
    ///
    /// <para>No garbage collection and no wait: the continuation is <c>ExecuteSynchronously</c>, so
    /// the line is in the log by the time the failure is set. An earlier draft of this fact tried to
    /// prove the same thing through <c>TaskScheduler.UnobservedTaskException</c> and a forced
    /// collection — it could not be made to red on its own mutation, because the abandoned task
    /// stayed reachable from the test's own async frame, and a fact that cannot fail is worse than
    /// no fact at all.</para>
    /// </summary>
    [Fact]
    public async Task AnAbandonedStopThatFailsLater_IsRecorded_RatherThanVanishing()
    {
        const string marker = "failed after teardown had moved on";

        var log = new ListLog();
        var wedged = new StopNeverCompletes();
        var host = new ApplicationHost(
            log, [wedged], new StartupTrace(), new OperationRegistry(), new UiDispatchBoundary(), ShortBound);

        await TestWait.Until(
            host.ShutdownAsync(),
            "ApplicationHost.ShutdownAsync to abandon the wedged stop and return");

        Assert.DoesNotContain(log.Lines, l => l.Contains(marker, StringComparison.Ordinal));

        // The abandoned stop fails AFTER teardown has walked away from it — the shape that would
        // otherwise leave no trace anywhere.
        wedged.FailItNow(new InvalidOperationException("the device never answered"));

        var recorded = Assert.Single(log.Lines, l => l.Contains(marker, StringComparison.Ordinal));
        Assert.Contains(wedged.Name, recorded, StringComparison.Ordinal);
        Assert.Contains("the device never answered", recorded, StringComparison.Ordinal);
    }

    private sealed class StopNeverCompletes : IBackgroundParticipant
    {
        private readonly TaskCompletionSource _never = new();

        public string Name => "a stop that never completes";

        public bool Running { get; private set; }

        internal bool WasAsked { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            WasAsked = true;
            return _never.Task;
        }

        internal void FailItNow(Exception failure) => _never.TrySetException(failure);
    }

    private sealed class RecordsItsStop(string name = "behind", List<string>? order = null) : IBackgroundParticipant
    {
        public string Name => name;

        public bool Running { get; private set; }

        internal bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stopped = true;
            Running = false;
            order?.Add(name);
            return Task.CompletedTask;
        }
    }

    private sealed class ListLog : ILogSink
    {
        internal List<string> Lines { get; } = [];

        public void Log(string message) => Lines.Add(message);
    }
}
