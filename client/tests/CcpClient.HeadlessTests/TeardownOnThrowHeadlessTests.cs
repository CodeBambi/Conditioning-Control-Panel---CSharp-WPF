using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using Xunit;
using Xunit.Sdk;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>The teardown guarantee itself, exercised rather than asserted about.</b> A headless fact that
/// throws mid-body must not hand the next fact a live <see cref="ApplicationHost"/> with a running
/// session on it.
///
/// <para><b>Why this fact exists.</b> The leak only appears on a BROKEN build, which is precisely
/// what makes it expensive: a failing fact could corrupt the one after it, so one genuine failure
/// produced a cascade of misleading ones and sent the reader at the wrong defect. It was found that
/// way — a mutation of the session feature lock reddened an eighth fact it could not touch, and did
/// not reproduce on a second run. Without something that drives the throwing path on every run,
/// <see cref="HeadlessTest"/> is a claim.</para>
///
/// <para><b>How this demonstrates it without leaving a red behind.</b> A test class that really
/// failed would be red for ever, and the rule against weakening gates rules out skipping,
/// allow-listing or retrying it. So the fact that throws is a REAL test-class-shaped type,
/// <see cref="FactThatThrowsMidBody"/> — it derives from <see cref="HeadlessTest"/>, boots a real
/// host through the real composition root, starts a real scripted session, and then fails an
/// assertion exactly where the mutation made a real fact fail, leaving its own trailing
/// <c>await host.ShutdownAsync()</c> unreached. It carries no fact attribute, so xunit never
/// schedules it; this fact invokes its body, observes the leak, and then invokes the SAME
/// <see cref="IAsyncDisposable.DisposeAsync"/> xunit invokes on a test-class instance after every
/// fact whatever its outcome.</para>
///
/// <para><b>The half this fact does not carry, and where that evidence is.</b> That xunit really
/// calls that disposal — on the Avalonia dispatcher thread, and after a FAILING fact as well as a
/// passing one — is a property of the runner, not of this file. The thread half is measured on
/// every run by <c>HeadlessTest.DisposeAsync</c>'s <c>Dispatcher.UIThread.VerifyAccess()</c>, which
/// every fact in this suite passes through. The failing half was measured out of band before this
/// landed, by planting one genuinely red fact that booted a host, started a session, closed its
/// window and then failed an assertion, with the host's log sink writing to a file. The run
/// reported 210 total / 1 failed — that one, by name, with no cascade — and everything after the
/// fact's own last log line was teardown: the session stopped in the reserved pre-drain slot
/// ("session recap: not shown — the app is closing"), the haptics all-stop, and the DTRH
/// participant's stop. The negative control ran the same red fact with the <c>Track</c> call
/// removed and produced NOTHING after that line, which is what makes the positive reading mean
/// what it says.</para>
/// </summary>
public class TeardownOnThrowHeadlessTests : HeadlessTest
{
    [AvaloniaFact]
    public async Task AFactThatThrowsMidBody_LeavesTheNextFactNoLiveHostAndNoRunningSession()
    {
        var throwing = new FactThatThrowsMidBody();

        // The body runs for real and fails for real; nothing here catches it on its behalf.
        var failure = await Assert.ThrowsAsync<FailException>(throwing.Body);
        Assert.Contains("the mid-body failure", failure.Message, StringComparison.Ordinal);

        // THE LEAK, observed rather than described. Its own teardown line was never reached, so a
        // whole host is still up with a scripted session still ticking on it — the exact object the
        // next fact used to inherit.
        var leaked = throwing.Leaked;
        Assert.NotNull(leaked);
        Assert.False(leaked.Host.IsShutdown);
        Assert.True(leaked.Window.Session.Scripted.Running);

        // The one disposal xunit runs after a fact whether it passed or threw.
        await ((IAsyncDisposable)throwing).DisposeAsync();

        // And it reached the leaked host: shut down, and the run stopped with it through the host's
        // own pre-drain slot (Session/SessionParticipant.cs), which is what actually makes the next
        // fact's surface unlocked again.
        Assert.True(leaked.Host.IsShutdown);
        Assert.False(leaked.Window.Session.Scripted.Running);

        // "The next fact still passes", literally: this fact goes on to boot its own host after the
        // leak happened, and gets a clean, independent one.
        var next = await BootAsync();
        Track(next.Host);
        Assert.NotSame(leaked.Host, next.Host);
        Assert.False(next.Host.IsShutdown);
        Assert.False(next.Window.Session.Scripted.Running);
        Assert.True(next.Window.Session.Scripted.Start(Session()));
    }

    /// <summary>
    /// A stand-in for the 171 facts that had no teardown on the throwing path, written in their
    /// shape: boot, work, and <c>await host.ShutdownAsync()</c> as the last statement. No fact
    /// attribute — xunit never runs this; the fact above does.
    /// </summary>
    private sealed class FactThatThrowsMidBody : HeadlessTest
    {
        public Boot? Leaked { get; private set; }

        public async Task Body()
        {
            var boot = await BootAsync();
            Track(boot.Host);
            Leaked = boot;

            Assert.True(boot.Window.Session.Scripted.Start(Session()));
            Assert.False(boot.Host.IsShutdown);

            // Where the eighth fact reddened under the mutation. Assert.Fail returns void as far as
            // the compiler is concerned, so the line below is reachable code that is never reached —
            // which is the defect, not a trick to avoid an unreachable-code warning.
            Assert.Fail("the mid-body failure this stand-in exists to have");

            await boot.Host.ShutdownAsync();
        }
    }

    private sealed record Boot(ApplicationHost Host, MainWindow Window);

    /// <summary>The cold composition-root boot every other class in this suite performs, with the
    /// suite's shared hand-advanced clock so the started session puts nothing on a wall clock.</summary>
    private static async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-teardown-throw-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = () => new ManualScriptedClock(),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window);
    }

    /// <summary>A session with every module off: the only thing it puts on the clock is its own
    /// tick, and this rig never advances the clock, so nothing fires.</summary>
    private static ScriptedSession Session() => new()
    {
        Id = "leak_check",
        Name = "Leak Check",
        Icon = "\U0001F6AA",
        DurationMinutes = 30,
        Settings = new ScriptedSessionSettings(),
    };
}
