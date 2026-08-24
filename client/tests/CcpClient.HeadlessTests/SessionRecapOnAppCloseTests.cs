using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>The Session Complete recap on the APP-CLOSE path</b> — the defect the app found in its own log
/// during a headed capture and no test had ever seen:
/// <c>session recap: could not be shown (InvalidOperationException: Cannot show a window with a
/// closed owner.)</c>.
///
/// <para>The chain is this branch's own: the scripted session's stop lives in the reserved pre-drain
/// slot so closing the app mid-session cannot persist the session's dials over the user's
/// (<see cref="SessionParticipant.FlushAsync"/>). That stop finalises the media log and raises
/// <c>LogReady</c>, the Studio page's one subscription answers it, and
/// <see cref="Desktop.Navigation.SessionRecapLaunch"/> tried to open a window owned by a shell that
/// had already closed. Upstream never meets the question — its scripted stop is not in a teardown
/// slot — so the answer had to be decided rather than ported, and it is recorded on
/// <see cref="Desktop.Navigation.SessionRecapLaunch"/>: <b>no recap is owed on app close</b>, the
/// ask is refused before a window is constructed, and the run is left where the user can actually
/// read it.</para>
///
/// <para><b>These facts drive the WHOLE chain rather than the guard.</b> Nothing here calls
/// <c>ShowRecap</c>: a real session is started on the real composition root, the real shell window
/// is closed and the real <see cref="ApplicationHost.ShutdownAsync"/> runs, so what is asserted is
/// what the user's close gesture produces. The presentation seam is wrapped rather than replaced —
/// the wrapper counts and then performs the REAL <c>Show</c> — so removing the guard reproduces the
/// verbatim log line above instead of quietly passing.</para>
///
/// <para>Draw-level only (verification-harness.md): visual tree and lifecycle. No composited pixel
/// is claimed here, and a headless close is not a headed one.</para>
/// </summary>
public class SessionRecapOnAppCloseTests : HeadlessTest
{
    /// <summary>Long enough to clear <see cref="ScriptedSessionLogStore.PersistenceMinDuration"/>
    /// (upstream's 30 s, <c>Services/Session/SessionLogService.cs:22-24</c>), because half the
    /// decision is that the run is still THERE afterwards.</summary>
    private static readonly TimeSpan RunLength = TimeSpan.FromSeconds(45);

    [AvaloniaFact]
    public async Task ClosingTheAppMidSessionATTEMPTSNoRecapWindow_AndSaysSoAsADecision()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        var attempts = 0;
        window.Recap.Present = (recap, owner) =>
        {
            attempts++;
            recap.Show(owner);
        };

        Assert.True(window.Session.Scripted.Start(Session()));
        boot.Clock.Advance(RunLength);

        // The app-close path, in the order the classic desktop lifetime produces it: the shell is
        // gone BEFORE teardown runs, because ShutdownMode.OnMainWindowClose reaches ShutdownAsync
        // from desktop.Exit (App.axaml.cs).
        window.Close();
        await boot.Host.ShutdownAsync();

        // The session really was stopped by the pre-drain slot and the log really did reach the
        // recap: without this the rest of the fact would pass on a chain that never ran.
        Assert.False(window.Session.Scripted.Running);
        Assert.Equal(1, window.Recap.RecapCount);

        // THE DEFECT'S OWN LINE, asserted absent first so a regression names itself.
        Assert.DoesNotContain(
            boot.Lines,
            line => line.Contains("could not be shown", StringComparison.Ordinal));
        Assert.DoesNotContain(
            boot.Lines,
            line => line.Contains("Cannot show a window with a closed owner", StringComparison.Ordinal));

        // What replaced it is a DECISION rather than a failure.
        Assert.Contains(
            boot.Lines,
            line => line.Contains("session recap: not shown — the app is closing", StringComparison.Ordinal));

        // And NOTHING WAS ATTEMPTED. Not "it threw and was caught" — the window was never built.
        Assert.Equal(0, attempts);
        Assert.Null(window.Recap.CurrentRecap);
    }

    [AvaloniaFact]
    public async Task TheRunTheUserClosedOutOfIsStillThere_WhichIsWhyNoRecapIsOwed()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        Assert.True(window.Session.Scripted.Start(Session()));
        boot.Clock.Advance(RunLength);

        window.Close();
        await boot.Host.ShutdownAsync();

        // Complete() persists SYNCHRONOUSLY before it raises LogReady
        // (Session/ScriptedSessionLog.cs; upstream Services/Session/SessionLogService.cs:93-101),
        // so the run the user closed out of is on disk and is the newest row under Studio's
        // "Recent sessions" the next time they open the app. That is the third leg of the decision
        // and the reason "they are told nothing" is not what happens.
        var recent = window.Session.MediaLog.LoadRecent();
        var run = Assert.Single(recent);
        Assert.Equal("closing_time", run.SessionId);
        Assert.False(run.Completed);
        Assert.Equal(RunLength, run.Duration);
    }

    /// <summary>
    /// The negative control, and it is not optional: a guard that refused every recap would pass
    /// both facts above. The shell stays OPEN here and the same ending opens the card.
    /// </summary>
    [AvaloniaFact]
    public async Task ARunThatEndsWhileTheShellIsOPENStillOpensItsRecap()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        var attempts = 0;
        window.Recap.Present = (recap, owner) =>
        {
            attempts++;
            recap.Show(owner);
        };

        Assert.True(window.Session.Scripted.Start(Session()));
        boot.Clock.Advance(RunLength);
        Assert.True(window.Session.Scripted.Stop());

        Assert.Equal(1, attempts);
        var card = window.Recap.CurrentRecap;
        Assert.NotNull(card);
        Assert.True(card.IsVisible);
        Assert.Equal("closing_time", card.Log.SessionId);
        Assert.Contains(
            boot.Lines,
            line => line.Contains("session recap: shown for a run of 45s", StringComparison.Ordinal));

        card.Close();
        window.Close();
        await boot.Host.ShutdownAsync();
    }

    /// <summary>A session with every module off, so the only thing on the clock is the run's own
    /// tick — and this rig's clock schedules nothing at all, so not even that fires.</summary>
    private static ScriptedSession Session() => new()
    {
        Id = "closing_time",
        Name = "Closing Time",
        Icon = "\U0001F6AA",
        DurationMinutes = 30,
        Settings = new ScriptedSessionSettings(),
    };

    private async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-recap-close-" + Guid.NewGuid().ToString("N"));
        var clock = new DeadScriptedClock();
        var lines = new CollectingSink();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = () => clock,
            LogSinkFactory = () => lines,
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window, clock, lines.Lines);
    }

    private sealed record Boot(
        ApplicationHost Host, MainWindow Window, DeadScriptedClock Clock, IReadOnlyList<string> Lines);

    /// <summary>
    /// A scripted clock whose readings move by hand and that puts NOTHING on any timer: the run's
    /// own tick is scheduled and never fires. That is deliberate rather than lazy — these facts are
    /// about what a CLOSE produces, so a tick arriving between the close and the teardown would be a
    /// second author of the result. No wall clock is read anywhere.
    /// </summary>
    private sealed class DeadScriptedClock : IScriptedClock
    {
        private readonly object _gate = new();
        private DateTimeOffset _wall = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        private TimeSpan _monotonic = TimeSpan.Zero;

        public DateTimeOffset Now
        {
            get { lock (_gate) { return _wall; } }
        }

        public TimeSpan Monotonic
        {
            get { lock (_gate) { return _monotonic; } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire) => new Nothing();

        public void Advance(TimeSpan by)
        {
            lock (_gate)
            {
                _wall += by;
                _monotonic += by;
            }
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    private sealed class CollectingSink : ILogSink
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines => _lines;

        public void Log(string message)
        {
            lock (_lines)
            {
                _lines.Add(message);
            }
        }
    }
}
