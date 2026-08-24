using Avalonia.Threading;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The teardown guarantee for every headless fact that boots a real
/// <see cref="ApplicationHost"/>: the host is shut down on the THROWING path too.
///
/// <para><b>The defect this closes, and how it was found.</b> A mutation check of the session
/// feature lock reddened an eighth fact the mutation could not touch; a second run of the same
/// mutation did not reproduce it, and the clean build passed that fact 10/10 alone, 6/6 as a class,
/// and twice in full assembly. The cause was not the product. Under the mutation a PRECEDING fact
/// threw mid-body, so its trailing <c>await host.ShutdownAsync()</c> was never reached, and a live
/// host — with a RUNNING scripted session on it — was still there when the next fact ran. A single
/// genuine failure could therefore manufacture a cascade of misleading ones and send whoever read
/// the run at the wrong defect. This suite is the port's evidence base; a run that misreports which
/// fact broke is worse than a slow one.</para>
///
/// <para><b>Why a base class rather than the alternatives.</b> xunit constructs a fresh test-class
/// instance per fact and disposes it after the fact runs whatever its outcome, so instance-scoped
/// tracking is exactly per-fact scope. A class fixture is the wrong lifetime — every fact here boots
/// its own host against its own temp data root, and sharing one would change what the facts test.
/// An ambient static collector would be wrong under xunit's default per-class parallelism. And
/// <c>try/finally</c> in each of the 171 exposed bodies is the same guarantee written 171 times,
/// which the next fact anyone adds forgets again.</para>
///
/// <para><b>What it deliberately does NOT do.</b> Only <see cref="ApplicationHost.ShutdownAsync"/>,
/// which is what the seventeen facts that already carried a <c>finally</c> did — so this changes
/// WHEN teardown runs and never WHAT it does. Windows are not closed here: no fact closed its
/// window on the passing path either, and quietly adding that would make this a behaviour change
/// wearing a harness change's clothes.</para>
/// </summary>
public abstract class HeadlessTest : IAsyncDisposable
{
    private readonly List<ApplicationHost> _hosts = [];

    /// <summary>
    /// Hands a freshly started host to the teardown. Called from each class's boot helper, which is
    /// why those helpers are instance methods: the registration has to reach the instance xunit is
    /// about to dispose, and a static helper cannot.
    /// </summary>
    protected void Track(ApplicationHost host) => _hosts.Add(host);

    /// <summary>
    /// Runs after every fact, passing or throwing. <see cref="ApplicationHost.ShutdownAsync"/> is
    /// idempotent and never throws (Lifecycle/ApplicationHost.cs), so a fact that already tore its
    /// own host down loses nothing here and a fact that threw before it could gets it anyway.
    /// Reverse order, matching the host's own reverse-of-start participant stop.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Where the seventeen hand-written `finally` blocks ran: inside the Avalonia dispatch, on
        // the thread that owns the visual trees these hosts are bound to. AvaloniaTestRunner
        // overrides the whole per-test Run — construction, the fact, and this disposal — so that
        // holds here too. Asserted rather than assumed: if a runner change ever moved disposal off
        // that thread, teardown would start silently doing something different (ShutdownAsync
        // swallows participant failures by design), and this makes it say so instead.
        Dispatcher.UIThread.VerifyAccess();

        for (var i = _hosts.Count - 1; i >= 0; i--)
        {
            await _hosts[i].ShutdownAsync();
        }

        _hosts.Clear();
        GC.SuppressFinalize(this);
    }
}
