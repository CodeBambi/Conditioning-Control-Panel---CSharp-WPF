using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The Scripted Sessions rack row, driven by REAL headless input on the REAL controls, from a cold
/// composition-root boot with no command-line arguments.
///
/// <para><b>The user story under test is the one slices 1 and 2 could not reach: a user starts a
/// scripted session.</b> The runtime landed with 36 facts and NOTHING CONSTRUCTED A RUN, so what
/// these facts are about is the half no unit test can see — that a real row on a real page, clicked
/// by a real gesture, reaches the run the composition root built, that the start is REFUSED until
/// the promise about the user's settings has been shown and confirmed, and that the one button
/// really does double as STOP.</para>
///
/// <para>Only the SCRIPTED CLOCK is substituted, and it is a declared seam on the composition root
/// (<see cref="CompositionRoot.ScriptedClockFactory"/>). Nothing here waits out a tick on a wall
/// clock; the run's own timer is what fires, on a clock this test moves by hand.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, style-resolved
/// classes, real input routing. Nothing here claims a composited pixel — the headed
/// <c>session-row</c> and <c>session-start</c> captures are what claim those.</para>
/// </summary>
public class SessionRackHeadlessTests
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, ManualScriptedClock Clock)
    {
        public ScriptedSessionRun Run => Window.Session.Scripted;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private static async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-session-rack-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualScriptedClock();
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScriptedClockFactory = () => clock,
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
        return new Boot(host, window, clock);
    }

    private static T Descendant<T>(MainWindow window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control, MouseButton button = MouseButton.Left)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>The user path, every time: the Studio door is where the rack is, and the row is at
    /// the foot of it.</summary>
    private static void OpenTheSessionsRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
    }

    private static string TextOf(MainWindow window, string name) =>
        Descendant<TextBlock>(window, name).Text ?? string.Empty;

    // =====================================================================================
    //  the rack
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheSessionsRowOpensARack_WithOneRowPerShippedSession_CarryingUpstreamsCells()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var panel = Descendant<StackPanel>(window, "ScriptedSessionModulePanel");
        Assert.True(panel.IsVisible);
        Assert.False(Descendant<StackPanel>(window, "RampModulePanel").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "RackHint").IsVisible);

        // FOUR rows, in the port's own file-name order (ScriptedSession.ReadFolder), built from the
        // files beside the binary rather than from markup — so a fifth file would be a fifth row and
        // a missing file would be a missing one.
        var rack = Descendant<StackPanel>(window, "ScriptedSessionRackPanel");
        var rows = rack.Children.OfType<RadioButton>().ToList();
        Assert.Equal(
            ["SessionRowDistantDoll", "SessionRowGamerGirl", "SessionRowGoodGirlsDontCum", "SessionRowMorningDrift"],
            rows.Select(r => r.Name));

        // Upstream's cells: icon, name, the description's first line, difficulty and duration
        // (MainWindow/MainWindow.SessionIO.cs:428-497).
        var texts = rows[3].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(
            [
                "\U0001F305",
                "Morning Drift",
                "Let the morning carry you gently into that soft, floaty space...",
                "Easy · 30 min",
            ],
            texts);

        // The stripe is upstream's difficulty colour, per row (Resources/Theme/Colors.xaml:191-197):
        // green on the two Easy sessions, amber on the Medium one, orange on the Hard one.
        Assert.Equal(
            ["#FF57D9A3", "#FFF5C242", "#FFFF8A4C", "#FF57D9A3"],
            new[] { "DistantDoll", "GamerGirl", "GoodGirlsDontCum", "MorningDrift" }
                .Select(id => StripeOf(window, "SessionStripe" + id)));

        // Upstream puts the WHOLE authored description on the row's tooltip, because the blurb cell
        // is one ellipsised line of it (SessionIO.cs:474-477).
        var tip = ToolTip.GetTip(rows[3]) as string ?? string.Empty;
        Assert.Contains("Just... let it happen.", tip, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();

        static string StripeOf(MainWindow window, string name) =>
            ((ISolidColorBrush)Descendant<Border>(window, name).Background!).Color.ToString().ToUpperInvariant();
    }

    // =====================================================================================
    //  the ceremony
    // =====================================================================================

    [AvaloniaFact]
    public async Task PressingStartWithNothingPicked_StartsNothing_AndSaysWhichGuardRefused()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        // Upstream's first guard: `if (_selectedSession == null || !_selectedSession.IsAvailable)
        // return;` (MainWindow/MainWindow.Presets.cs:1463). It returns in silence; this says why.
        Assert.Contains("Nothing is running", TextOf(window, "ScriptedSessionPhaseState"), StringComparison.Ordinal);

        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));

        Assert.False(boot.Run.Running);
        Assert.False(window.Session.Engine.Running);
        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.Equal("Pick a session first.", TextOf(window, "ScriptedSessionPhaseState"));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task StartASKSBeforeItStarts_AndTheQuestionNamesTheDurationAndThePromise()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Assert.Contains(
            "Morning Drift is selected — 30 minutes, 5 phases",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);

        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));

        // NOTHING HAS STARTED. Upstream shows a modal and starts only `if (confirmed)`
        // (MainWindow.Presets.cs:1465-1476); the whole point of the ceremony is that the first
        // press cannot begin a session.
        Assert.False(boot.Run.Running);
        Assert.False(window.Session.Engine.Running);

        Assert.True(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.Equal("Start Morning Drift?", TextOf(window, "ScriptedSessionConfirmTitle"));
        Assert.Equal("Duration: 30 minutes", TextOf(window, "ScriptedSessionConfirmDetail"));

        // The contract the restore keeps, on screen, in upstream's words (:1467-1470).
        var promise = TextOf(window, "ScriptedSessionConfirmPromise");
        Assert.Contains("temporarily replaced", promise, StringComparison.Ordinal);
        Assert.Contains("restored when the session ends", promise, StringComparison.Ordinal);
        Assert.Equal("Ready to begin?", TextOf(window, "ScriptedSessionConfirmQuestion"));
        Assert.Equal("Start Session", Descendant<Button>(window, "ScriptedSessionConfirmButton").Content);
        Assert.Equal("Not yet", Descendant<Button>(window, "ScriptedSessionCancelButton").Content);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task NotYet_StartsNothingAndPutsTheQuestionAway()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionCancelButton"));

        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.False(boot.Run.Running);
        Assert.False(window.Session.Engine.Running);
        Assert.Equal("Start Session", Descendant<Button>(window, "ScriptedSessionStartButton").Content);

        // The user's own dials were never touched: the snapshot is taken inside Start, and Start
        // was never reached (Session/ScriptedSessionRun.cs, the START order).
        Assert.NotEqual(12, window.Session.Preset.Current.FlashesPerHour);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ConfirmingREALLYStarts_TheEngineComesUpAndTheSessionsDialsAreInForce()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        window.Session.Preset.Mutate(d =>
        {
            d.FlashEnabled = true;
            d.FlashesPerHour = 7;
        });

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        Assert.True(boot.Run.Running);
        Assert.Equal("morning_drift", boot.Run.Current?.Id);

        // Upstream starts the ordinary engine on the way in (MainWindow.Presets.cs:1511-1514), so
        // the SHELL's own button flips too — one session, two surfaces looking at it.
        Assert.True(window.Session.Engine.Running);
        Assert.Equal("STOP", window.FindControl<Button>("SessionStartButton")!.Content);

        // The session's dials are really in force, in the document the Flash Images panel reads —
        // Morning Drift's 12 per hour over the user's 7.
        Assert.Equal(12, window.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(12d, Descendant<Slider>(window, "FlashFrequencySlider").Value);

        // The readout is painted at once, off the phase the run announces at START, and the button
        // has already become the stop (en.json:2321).
        Assert.Contains(
            "Phase 1 of 5 — Settling In",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);
        Assert.Equal("0% — 00:00 elapsed, 30:00 remaining", TextOf(window, "ScriptedSessionProgressState"));
        var button = Descendant<Button>(window, "ScriptedSessionStartButton");
        Assert.Equal("STOP SESSION (30:00)", button.Content);
        Assert.Contains("running", button.Classes);
        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheReadoutFollowsTheRunsOwnTick_AndTheButtonCountsDownWithIt()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        // MINUTE TWO FIRST, AND THE MUTATION CHECK IS WHY. Morning Drift's second phase opens at
        // minute 10 and its first delayed feature at minute 5, so an advance past either of those
        // repaints this page through a DIFFERENT signal — the phase change, or a module arming —
        // and the fact would pass with the per-tick readout unsubscribed entirely (measured: it
        // did). Minute 2 is inside phase 1 with nothing else moving, so the numbers below can only
        // have come from the tick's own ProgressUpdated.
        boot.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Contains(
            "Phase 1 of 5 — Settling In",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);
        Assert.Equal("6% — 02:00 elapsed, 28:00 remaining", TextOf(window, "ScriptedSessionProgressState"));
        Assert.Equal("STOP SESSION (28:00)", Descendant<Button>(window, "ScriptedSessionStartButton").Content);

        // The run's OWN one-second tick fires here, on the injected clock: nothing in this test
        // calls Tick(), and nothing waits.
        boot.Clock.Advance(TimeSpan.FromMinutes(10));

        // Minute 12 is inside Morning Drift's second phase, which opens at minute 10.
        Assert.Equal(
            "Phase 2 of 5 — Pink Awakening: Pink filter begins its gradual embrace",
            TextOf(window, "ScriptedSessionPhaseState"));
        Assert.Equal("40% — 12:00 elapsed, 18:00 remaining", TextOf(window, "ScriptedSessionProgressState"));
        Assert.Equal("STOP SESSION (18:00)", Descendant<Button>(window, "ScriptedSessionStartButton").Content);

        boot.Clock.Advance(TimeSpan.FromMinutes(13));

        Assert.Equal(
            "Phase 4 of 5 — Deep Pink: Pink filter nearing full intensity",
            TextOf(window, "ScriptedSessionPhaseState"));
        Assert.Equal("83% — 25:00 elapsed, 05:00 remaining", TextOf(window, "ScriptedSessionProgressState"));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheOneButtonDoublesAsStop_TheStopIsConfirmedToo_AndTheDialsComeBack()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        window.Session.Preset.Mutate(d =>
        {
            d.FlashEnabled = true;
            d.FlashesPerHour = 7;
        });

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        boot.Clock.Advance(TimeSpan.FromMinutes(3) + TimeSpan.FromSeconds(36));

        // THE SAME BUTTON, and upstream says why in its own comment: "The button doubles as
        // Start/Stop — state dictates which path to run. This also makes us resilient to any
        // stale/duplicate Click subscriptions" (MainWindow/MainWindow.Presets.cs:1455-1459).
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));

        // Still running: the stop is confirmed as well (:1893-1906).
        Assert.True(boot.Run.Running);
        Assert.True(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.Equal("Stop Session?", TextOf(window, "ScriptedSessionConfirmTitle"));
        Assert.Equal(
            "You're currently in a session: \U0001F305 Morning Drift",
            TextOf(window, "ScriptedSessionConfirmDetail"));
        Assert.Equal(
            "Time elapsed: 03:36 — Time remaining: 26:24",
            TextOf(window, "ScriptedSessionConfirmPromise"));
        Assert.Equal("Yes, stop session", Descendant<Button>(window, "ScriptedSessionConfirmButton").Content);
        Assert.Equal("Keep going", Descendant<Button>(window, "ScriptedSessionCancelButton").Content);

        Click(window, Descendant<Button>(window, "ScriptedSessionCancelButton"));
        Assert.True(boot.Run.Running);
        Assert.Equal(12, window.Session.Preset.Current.FlashesPerHour);

        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        // The promise, kept: the user's own dial is back in the document AND on the panel above.
        Assert.False(boot.Run.Running);
        Assert.Equal(7, window.Session.Preset.Current.FlashesPerHour);
        Assert.Equal(7d, Descendant<Slider>(window, "FlashFrequencySlider").Value);
        Assert.Equal("Start Session", Descendant<Button>(window, "ScriptedSessionStartButton").Content);
        Assert.DoesNotContain("running", Descendant<Button>(window, "ScriptedSessionStartButton").Classes);
        Assert.Empty(TextOf(window, "ScriptedSessionProgressState"));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ASessionThatRunsOutItsOwnDuration_EndsItself_AndTheSurfaceGoesIdle()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        window.Session.Preset.Mutate(d =>
        {
            d.FlashEnabled = true;
            d.FlashesPerHour = 7;
        });

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        // THE SHELL'S OWN STOP, PRESSED MID-SESSION, and it is here for two reasons. It is a real
        // user path — that button is never disabled, which is the whole point of a panic button —
        // and it takes the ENGINE down without ending the scripted session, which is upstream's
        // behaviour too (its StopSession is a different handler on a different control). So the
        // completion below happens with nothing else on this page listening, which is what makes
        // the surface's own Ended subscription the only thing that can repaint it. Without that
        // second half the fact passed with Ended unsubscribed entirely (measured).
        Click(window, window.FindControl<Button>("SessionStartButton")!);
        Assert.False(window.Session.Engine.Running);
        Assert.True(boot.Run.Running);

        // Upstream's own completion: the tick that reaches the duration stops the session and does
        // nothing else that tick (Services/Session/SessionEngine.cs:512-517). Nobody presses
        // anything here.
        boot.Clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(boot.Run.Running);
        Assert.Equal(7, window.Session.Preset.Current.FlashesPerHour);
        Assert.Equal("Start Session", Descendant<Button>(window, "ScriptedSessionStartButton").Content);
        Assert.Contains(
            "Morning Drift is selected",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// Both clocks and the tick timer, moved by hand. The scripted run reads a wall clock AND a
    /// monotonic one and compares them, so a seam with one clock could not express what it does;
    /// this one moves them together, which is what "time passed" means.
    /// </summary>
    private sealed class ManualScriptedClock : IScriptedClock
    {
        private readonly List<Entry> _timers = [];
        private DateTimeOffset _wall = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);
        private TimeSpan _monotonic = TimeSpan.Zero;

        public DateTimeOffset Now
        {
            get { lock (_timers) { return _wall; } }
        }

        public TimeSpan Monotonic
        {
            get { lock (_timers) { return _monotonic; } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            Entry entry;
            lock (_timers)
            {
                entry = new Entry
                {
                    Due = _monotonic + (due < TimeSpan.Zero ? TimeSpan.Zero : due),
                    Fire = fire,
                };
                _timers.Add(entry);
            }

            return new CancelHandle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            lock (_timers)
            {
                _wall += by;
                _monotonic += by;
            }

            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers
                        .Where(t => !t.Cancelled && t.Due <= _monotonic)
                        .OrderBy(t => t.Due)
                        .FirstOrDefault();
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
            public TimeSpan Due;

            public required Action Fire;

            public bool Cancelled;
        }

        private sealed class CancelHandle(ManualScriptedClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    entry.Cancelled = true;
                }
            }
        }
    }
}
