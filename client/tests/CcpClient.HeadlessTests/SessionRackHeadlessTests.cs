using Avalonia;
using Avalonia.Automation;
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
public class SessionRackHeadlessTests : HeadlessTest
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, ManualScriptedClock Clock)
    {
        public ScriptedSessionRun Run => Window.Session.Scripted;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);
    }

    private async Task<Boot> BootAsync()
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
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();
        return new Boot(host, window, clock);
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
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

    private static string TextOf(Window window, string name) =>
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

        // Upstream's cells: icon, name, the description's first line, the provenance badge, and
        // difficulty with duration (MainWindow/MainWindow.SessionIO.cs:428-497, badge at :508-517).
        // The badge joined this list when the editor landed — before it every row was built-in and
        // upstream's own pill carried no information here; now a saved edit puts a session of the
        // same name in the rack beside its original and the badge is what separates them.
        var texts = rows[3].GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Equal(
            [
                "\U0001F305",
                "Morning Drift",
                "Let the morning carry you gently into that soft, floaty space...",
                "BUILT-IN",
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

    /// <summary>
    /// <b>Two buttons that start a session are on screen together, and until 2026-08-25 they
    /// answered to the SAME accessible name.</b>
    ///
    /// <para>Both captions are upstream's own <c>en.json:1331</c> "Start Session" — the rack's
    /// button (<c>SessionRackNotices.StartButtonIdle</c>) and the confirmation's
    /// (<c>.ConfirmStart</c>) — and the caption is the ported outcome, so neither may change.
    /// Windows tells them apart by <c>AutomationId</c>, which is how this port's own capture
    /// harness finds them. <b>AT-SPI carries no AutomationId at all</b>, so a Linux screen-reader
    /// user was offered two indistinguishable targets, one of which starts a session — measured on
    /// WSL2/Ubuntu 26.04 through the AT-SPI route at <c>518,456 123x39</c> and
    /// <c>505,518 220x45</c> with identical names.</para>
    ///
    /// <para>So the confirmation's SPOKEN name carries "(confirm)" and its caption does not. This
    /// fact pins both halves: the captions still match upstream, and the names no longer do.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheTwoStartButtonsThatAreOnScreenTogether_DoNotAnswerToTheSameACCESSIBLENAME()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));

        var confirm = Descendant<Button>(window, "ScriptedSessionConfirmButton");
        var rack = Descendant<Button>(window, "ScriptedSessionStartButton");

        // The collision only exists while BOTH are on screen, so that is established first rather
        // than assumed: a hidden confirmation would make the names below a comparison of one
        // reachable control against one that no assistive technology could reach.
        Assert.True(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.True(confirm.IsVisible);
        Assert.True(rack.IsVisible);

        // The captions are upstream's and stay identical — this fix is not allowed to change what
        // the user SEES.
        Assert.Equal("Start Session", confirm.Content);
        Assert.Equal("Start Session", rack.Content);

        var confirmName = AutomationProperties.GetName(confirm);
        var rackName = AutomationProperties.GetName(rack);

        Assert.Equal("Start Session", rackName);
        Assert.Equal("Start Session (confirm)", confirmName);
        Assert.NotEqual(rackName, confirmName);

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

    // =====================================================================================
    //  what the user sees when it ends
    // =====================================================================================

    [AvaloniaFact]
    public async Task WhenTheSessionENDSTheRecapIsUP_AndItIsBuiltFromTheRunThatJustHappened()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        // NOTHING IS UP WHILE IT RUNS. Upstream's recap is raised by the media log's LogReady and by
        // nothing else (MainWindow/MainWindow.xaml.cs:373-378), so a session in progress has no
        // recap over it.
        boot.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Null(window.Recap.CurrentRecap);

        // Upstream's own completion path: the tick that reaches the duration ends the session
        // (Services/Session/SessionEngine.cs:512-517). Nobody presses anything.
        boot.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(boot.Run.Running);

        var recap = window.Recap.CurrentRecap;
        Assert.NotNull(recap);
        Assert.True(recap.IsVisible);
        Assert.Equal("morning_drift", recap.Log.SessionId);
        Assert.True(recap.Log.Completed);

        // Upstream's headline pair for a finished run (Windows/SessionCompleteWindow.xaml.cs:80-83).
        Assert.Equal("Good Girl!", TextOf(recap, "Headline"));
        Assert.Equal("\U0001F305 Morning Drift Completed", TextOf(recap, "Subtitle"));
        Assert.Equal("Morning Drift", TextOf(recap, "SessionName"));
        Assert.Equal("30:00", TextOf(recap, "Duration"));

        // Nothing played: this build's flash and video modules never came due on the real clock in
        // this test, so the recap shows upstream's empty-media sentence (en.json:2791) and no count.
        Assert.True(Descendant<TextBlock>(recap, "NoMedia").IsVisible);
        Assert.Empty(TextOf(recap, "MediaCount"));
        Assert.Empty(Descendant<StackPanel>(recap, "MediaList").Children);

        // The two refusals are ON THE WINDOW, not merely in a constant.
        Assert.Contains("never a name or a path", TextOf(recap, "NamesNotice"), StringComparison.Ordinal);
        Assert.Contains("No XP", TextOf(recap, "AwardsNotice"), StringComparison.Ordinal);

        Click(recap, Descendant<Button>(recap, "CloseButton"));
        Assert.Null(window.Recap.CurrentRecap);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AnABORTEDRunGetsARecapToo_AndOnlyONERecapIsEverUp()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        // Upstream shows the recap for BOTH endings (Services/Session/SessionEngine.cs:420-423) and
        // the headline is the whole difference.
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(4));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        var first = window.Recap.CurrentRecap;
        Assert.NotNull(first);
        Assert.False(first.Log.Completed);
        Assert.Equal("Session Ended Early", TextOf(first, "Headline"));
        Assert.Equal("\U0001F305 Morning Drift", TextOf(first, "Subtitle"));
        Assert.Equal("04:00", TextOf(first, "Duration"));

        // A second run ends with the first recap still on screen. Upstream keeps exactly one -
        // "two runs ending in quick succession would stack two live cards. Keep exactly one"
        // (MainWindow/MainWindow.Presets.cs:1690-1692).
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(7));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        var second = window.Recap.CurrentRecap;
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.False(first.IsVisible);
        Assert.Equal("07:00", TextOf(second, "Duration"));
        Assert.Equal(2, window.Recap.RecapCount);

        Click(second, Descendant<Button>(second, "CloseButton"));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheRecentSessionsButtonOpensTheHistory_AndARowREOPENSThatRunsRecap()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        // One run long enough to be kept (30 minutes is past the 30-second floor,
        // Services/Session/SessionLogService.cs:24, :94) and one deliberately not - a four second
        // abort with nothing on screen is the accidental start upstream refuses to keep (:22-23).
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.False(boot.Run.Running);
        Click(
            window.Recap.CurrentRecap!,
            Descendant<Button>(window.Recap.CurrentRecap!, "CloseButton"));

        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromSeconds(4));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Click(
            window.Recap.CurrentRecap!,
            Descendant<Button>(window.Recap.CurrentRecap!, "CloseButton"));

        // Upstream's door button (MainWindow/MainWindow.Presets.cs:1440).
        Click(window, Descendant<Button>(window, "ScriptedSessionHistoryButton"));

        var history = window.Recap.CurrentHistory;
        Assert.NotNull(history);
        Assert.True(history.IsVisible);
        Assert.Single(history.Rows);
        Assert.True(history.Rows[0].Completed);
        Assert.Equal("1 sessions", TextOf(history, "HistoryCount"));
        Assert.False(Descendant<TextBlock>(history, "EmptyLine").IsVisible);
        Assert.Equal("🌅 Morning Drift", TextOf(history, "SessionHistoryTitle0"));
        Assert.Contains("30:00", TextOf(history, "SessionHistoryDetail0"), StringComparison.Ordinal);
        Assert.Contains("0 videos · 0 images", TextOf(history, "SessionHistoryDetail0"), StringComparison.Ordinal);
        Assert.Equal("Completed", TextOf(history, "SessionHistoryStatus0"));

        // The row reopens THAT run in the same recap window the session's own end raised - upstream's
        // second caller (Windows/SessionLogHistoryWindow.xaml.cs:46-50).
        Click(history, Descendant<Button>(history, "SessionHistoryRow0"));
        var reopened = window.Recap.CurrentRecap;
        Assert.NotNull(reopened);
        Assert.Equal("morning_drift", reopened.Log.SessionId);
        Assert.Equal("30:00", TextOf(reopened, "Duration"));
        Assert.Equal("Good Girl!", TextOf(reopened, "Headline"));

        Click(reopened, Descendant<Button>(reopened, "CloseButton"));
        Click(history, Descendant<Button>(history, "CloseButton"));
        Assert.Null(window.Recap.CurrentHistory);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the hold — upstream's BtnPauseSession (MainWindow/MainWindow.Presets.cs:1908-1940)
    // =====================================================================================

    [AvaloniaFact]
    public async Task ThePauseButtonExistsONLYWhileASessionIsRunning()
    {
        // Upstream's own rule: the button is declared Collapsed (MainWindow.xaml:2606-2607), shown
        // when a session starts (:1809) and collapsed again when it ends (:1855). Absent rather
        // than greyed, which is §9 D7 and upstream's own choice here.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var pause = Descendant<Button>(window, "ScriptedSessionPauseButton");
        Assert.False(pause.IsVisible);

        StartMorningDrift(window);
        Assert.True(pause.IsVisible);
        Assert.Equal("Pause", pause.Content);

        // It runs itself out (30 minutes) and the button goes with it — no gesture involved, so
        // this is the surface following the RUN rather than following a click.
        boot.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.False(boot.Run.Running);
        Assert.False(pause.IsVisible);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task PausingASKSFirst_NamesWhatItWouldCost_AndHoldsNOTHINGUntilItIsAnswered()
    {
        // Upstream puts a dialog up before a pause and names the running penalty in it
        // (:1928-1932, en.json:3387-3389). The whole point of the ceremony is that the first click
        // changes nothing — the same thing the START confirmation's own fact asserts.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(4));

        Click(window, Descendant<Button>(window, "ScriptedSessionPauseButton"));

        // THE QUESTION IS UP AND NOTHING IS HELD.
        Assert.False(boot.Run.Paused);
        Assert.Equal(0, boot.Run.PauseCount);
        Assert.True(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.Equal("Pause Session?", TextOf(window, "ScriptedSessionConfirmTitle"));
        Assert.Equal(
            "Pausing costs 100 XP from this session's reward.",
            TextOf(window, "ScriptedSessionConfirmDetail"));
        Assert.Equal(
            "Current penalty: -0 XP. After this pause: -100 XP — recorded, not charged: nothing in "
                + "this build awards session XP yet.",
            TextOf(window, "ScriptedSessionConfirmPromise"));
        Assert.Equal("Are you sure?", TextOf(window, "ScriptedSessionConfirmQuestion"));
        Assert.Equal("Yes, pause", Descendant<Button>(window, "ScriptedSessionConfirmButton").Content);
        Assert.Equal("Keep going", Descendant<Button>(window, "ScriptedSessionCancelButton").Content);

        // The session is still running underneath the question, and its clock is still running too.
        boot.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("16% — 05:00 elapsed, 25:00 remaining", TextOf(window, "ScriptedSessionProgressState"));

        // "Keep going" — upstream's own refusal button, and it holds nothing either.
        Click(window, Descendant<Button>(window, "ScriptedSessionCancelButton"));
        Assert.False(boot.Run.Paused);
        Assert.Equal(0, boot.Run.PauseCount);
        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ConfirmingTheHoldREALLYHoldsTheRUN_TheReadoutSaysSo_AndTheClockStopsMoving()
    {
        // THE REACHABILITY FACT FOR THIS SLICE. Every step is a real gesture on the real shell —
        // the Studio door, the Sessions row, a rack row, START, its confirmation, then the PAUSE
        // button and its own confirmation — and what it asserts at the end is the state of the ONE
        // ScriptedSessionRun the composition root built (Lifecycle/CompositionRoot.cs:275 →
        // Session/SessionParticipant.cs:620). Nothing here calls Pause(): if the button stops
        // reaching the run, this fails.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("20% — 06:00 elapsed, 24:00 remaining", TextOf(window, "ScriptedSessionProgressState"));

        Click(window, Descendant<Button>(window, "ScriptedSessionPauseButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));

        // The RUN is held, and it is still running: a hold is not a stop.
        Assert.True(boot.Run.Paused);
        Assert.True(boot.Run.Running);
        Assert.Equal(1, boot.Run.PauseCount);
        Assert.Equal(100, boot.Run.XpPenalty);

        // The surface says so, in upstream's own word (en.json:3180, :1759), and the button has
        // become the other half of itself.
        Assert.Equal(
            "20% — 06:00 elapsed, 24:00 remaining [PAUSED]",
            TextOf(window, "ScriptedSessionProgressState"));
        Assert.Equal("Resume", Descendant<Button>(window, "ScriptedSessionPauseButton").Content);
        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);

        // AND THE CLOCK REALLY STOPPED. Ten minutes of wall clock, and not one of them is the
        // session's — including the minute-10 phase change and the pink filter's delayed start,
        // both of which would have repainted this line had the hold leaked.
        boot.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(
            "20% — 06:00 elapsed, 24:00 remaining [PAUSED]",
            TextOf(window, "ScriptedSessionProgressState"));
        Assert.Contains(
            "Phase 1 of 5 — Settling In",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);
        Assert.Equal("STOP SESSION (24:00)", Descendant<Button>(window, "ScriptedSessionStartButton").Content);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task ResumingAsksNOTHING_AndTheCountdownMovesAgainFromWhereItStopped()
    {
        // Upstream's asymmetry, verbatim: the paused branch calls ResumeSession() immediately
        // (:1919-1924) while the running branch asks first (:1928-1939). A resume spends nothing,
        // so there is nothing to ask about.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);
        StartMorningDrift(window);
        boot.Clock.Advance(TimeSpan.FromMinutes(6));

        var pause = Descendant<Button>(window, "ScriptedSessionPauseButton");
        Click(window, pause);
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        boot.Clock.Advance(TimeSpan.FromMinutes(10));

        // ONE click, no question, and it is running again.
        Click(window, pause);
        Assert.False(boot.Run.Paused);
        Assert.True(boot.Run.Running);
        Assert.False(Descendant<Border>(window, "ScriptedSessionConfirmPanel").IsVisible);
        Assert.Equal("Pause", pause.Content);
        Assert.Equal("20% — 06:00 elapsed, 24:00 remaining", TextOf(window, "ScriptedSessionProgressState"));

        // From SIX, not sixteen: the ten held minutes were never the session's.
        boot.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal("26% — 08:00 elapsed, 22:00 remaining", TextOf(window, "ScriptedSessionProgressState"));

        // The count survives the resume — one gesture, one pause, one penalty (:440, :2014).
        Assert.Equal(1, boot.Run.PauseCount);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the toolbar — filter, order, search
    // =====================================================================================

    /// <summary>
    /// THE DOOR to the whole toolbar. Every control below is found by walking the REAL page mounted
    /// in a REAL window from a cold composition-root boot — so a toolbar deleted from the markup, or
    /// a page that never fills it, fails here rather than shipping a feature nobody can reach.
    ///
    /// <para>The captions are read off the controls, not off the constants that made them: the
    /// bands' words and the orders' labels are the only evidence a user has for what a control
    /// does.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheRackCarriesAToolbar_FourBandsAnOrderAndASearch_AllOpenOnEverything()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        // THE DOOR, from the wrong side first: on the Studio page with another module open, none of
        // these controls is on screen. A toolbar that lived outside the Scripted Sessions panel
        // would be found by the walk below either way, so this is the half that proves WHERE it is.
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Assert.False(Descendant<TextBox>(window, "ScriptedSessionSearchBox").IsEffectivelyVisible);
        Assert.False(Descendant<ComboBox>(window, "ScriptedSessionSortBox").IsEffectivelyVisible);

        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));

        var bands = Bands(window);
        Assert.Equal(
            ["SessionFilterEasy", "SessionFilterMedium", "SessionFilterHard", "SessionFilterExtreme"],
            bands.Select(band => band.Name));
        Assert.Equal(["Easy", "Medium", "Hard", "Extreme"], bands.Select(band => band.Content as string));

        // All four on, which is upstream's default and the only state in which the rack shows
        // everything it has (MainWindow/MainWindow.SessionIO.cs:189-195, :672).
        Assert.All(bands, band => Assert.True(band.IsChecked));

        var sort = Descendant<ComboBox>(window, "ScriptedSessionSortBox");
        Assert.Equal(
            ["As installed", "Name A-Z", "Easiest first", "Hardest first", "Shortest first"],
            sort.Items.OfType<ComboBoxItem>().Select(item => item.Content as string));
        Assert.Equal(0, sort.SelectedIndex);

        var search = Descendant<TextBox>(window, "ScriptedSessionSearchBox");
        Assert.True(string.IsNullOrEmpty(search.Text));
        Assert.Equal("Search…", search.PlaceholderText);

        // ON SCREEN, not merely in the tree: an invisible control is still a visual descendant, so
        // every assertion above would hold over a toolbar the user cannot see. These are the ones
        // that would not.
        Assert.All<Control>(
            [search, sort, .. bands, Descendant<TextBlock>(window, "ScriptedSessionRackCount")],
            control =>
            {
                Assert.True(control.IsEffectivelyVisible);
                Assert.True(control.Bounds.Width > 0);
                Assert.True(control.Bounds.Height > 0);
            });

        // Nothing is filtered, so the count is the whole rack rather than "4 of 4".
        Assert.Equal("4 sessions", TextOf(window, "ScriptedSessionRackCount"));
        Assert.Equal(4, Rows(window).Count);

        // AND THE SURFACE HAS STOPPED CLAIMING THIS IS MISSING. The absence notice is what tells a
        // user which of upstream's rack they are looking at; leaving it naming a filter that is now
        // two inches above it would be the rot this port amends at the land.
        var absences = TextOf(window, "ScriptedSessionAbsenceState");
        Assert.DoesNotContain("filter", absences, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("search", absences, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the XP award", absences, StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// A user types, and the rack answers — real keystrokes into the real box, one character at a
    /// time, through the control's own text input (upstream's <c>TxtRackSearch_TextChanged</c> ->
    /// <c>RepaintSessionRack</c>, <c>MainWindow/MainWindow.SessionIO.cs:804-818</c>).
    ///
    /// <para>"gamer" appears in exactly one shipped file, so the surviving row is named rather than
    /// counted; "gamerz" appears in none, which is the state upstream's empty line exists for
    /// (<c>en.json:76</c>). Every backspace is a keystroke too, so the rack coming back is driven by
    /// the same road it left by.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TypingInTheSearchBoxRedrawsTheRack_AndAMissSaysSoWhereTheRowsWere()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var search = Descendant<TextBox>(window, "ScriptedSessionSearchBox");
        TypeIntoSearch(window, search, "gamer");

        Assert.Equal("gamer", search.Text);
        Assert.Equal(["SessionRowGamerGirl"], Rows(window).Select(row => row.Name));
        Assert.Equal("1 of 4", TextOf(window, "ScriptedSessionRackCount"));
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Name == "ScriptedSessionRackNoMatch");

        // One more letter and nothing matches. The rack does not go blank: it says why, where the
        // rows were.
        Type(window, "z");
        Assert.Empty(Rows(window));
        Assert.Equal("0 of 4", TextOf(window, "ScriptedSessionRackCount"));
        Assert.Equal("No sessions match — clear a filter.", TextOf(window, "ScriptedSessionRackNoMatch"));

        // And the whole rack comes back when the box is emptied.
        Backspace(window, 6);
        Assert.True(string.IsNullOrEmpty(search.Text));
        Assert.Equal(4, Rows(window).Count);
        Assert.Equal("4 sessions", TextOf(window, "ScriptedSessionRackCount"));
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Name == "ScriptedSessionRackNoMatch");

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The band filters, clicked — upstream's difficulty dots
    /// (<c>MainWindow/MainWindow.SessionIO.cs:778-787</c>), four independent switches over the four
    /// files the app ships: two Easy, one Medium, one Hard and no Extreme.
    ///
    /// <para>The last step turns them ALL off, which is one click past a state a user reaches by
    /// accident, and it lands on the same line a failed search does.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task SwitchingOffABandTakesItsRowsOut_AndSwitchingItBackOnBringsThemBack()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var easy = Bands(window)[0];
        Click(window, easy);

        // The Distant Doll and Morning Drift are the two Easy files; the Medium and Hard ones stay.
        Assert.Equal(
            ["SessionRowGamerGirl", "SessionRowGoodGirlsDontCum"],
            Rows(window).Select(row => row.Name));
        Assert.Equal("2 of 4", TextOf(window, "ScriptedSessionRackCount"));

        Click(window, easy);
        Assert.Equal(4, Rows(window).Count);
        Assert.Equal("4 sessions", TextOf(window, "ScriptedSessionRackCount"));

        foreach (var band in Bands(window))
        {
            Click(window, band);
        }

        Assert.Empty(Rows(window));
        Assert.Equal("0 of 4", TextOf(window, "ScriptedSessionRackCount"));
        Assert.Equal("No sessions match — clear a filter.", TextOf(window, "ScriptedSessionRackNoMatch"));

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// The order, chosen on the real combo. Four of the five entries are asserted as four DIFFERENT
    /// permutations of the same four rows, so an order that quietly did nothing would fail three of
    /// them.
    /// </summary>
    [AvaloniaFact]
    public async Task ChoosingAnOrderRedrawsTheRackInIt_AndTheDefaultIsTheOrderTheFilesWereRead()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var sort = Descendant<ComboBox>(window, "ScriptedSessionSortBox");
        Assert.Equal(
            ["SessionRowDistantDoll", "SessionRowGamerGirl", "SessionRowGoodGirlsDontCum", "SessionRowMorningDrift"],
            Rows(window).Select(row => row.Name));

        sort.SelectedIndex = 1;   // Name A-Z
        Assert.Equal(
            ["SessionRowGamerGirl", "SessionRowGoodGirlsDontCum", "SessionRowMorningDrift", "SessionRowDistantDoll"],
            Rows(window).Select(row => row.Name));

        sort.SelectedIndex = 3;   // Hardest first
        Assert.Equal(
            ["SessionRowGoodGirlsDontCum", "SessionRowGamerGirl", "SessionRowDistantDoll", "SessionRowMorningDrift"],
            Rows(window).Select(row => row.Name));

        sort.SelectedIndex = 2;   // Easiest first
        Assert.Equal(
            ["SessionRowMorningDrift", "SessionRowDistantDoll", "SessionRowGamerGirl", "SessionRowGoodGirlsDontCum"],
            Rows(window).Select(row => row.Name));

        // An order is a VIEW: the count never moves, because nothing was filtered.
        Assert.Equal("4 sessions", TextOf(window, "ScriptedSessionRackCount"));

        sort.SelectedIndex = 0;
        Assert.Equal(
            ["SessionRowDistantDoll", "SessionRowGamerGirl", "SessionRowGoodGirlsDontCum", "SessionRowMorningDrift"],
            Rows(window).Select(row => row.Name));

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// THE ONE THAT MATTERS MOST, because it is where a repaint can quietly take something away
    /// from a user: the rows are rebuilt on every keystroke, and the row a user has already picked
    /// is a control that gets thrown away and made again.
    ///
    /// <para><b>A pick survives a repaint that hides it.</b> Upstream keeps its selected id across
    /// every repaint too; here the readout under the button goes on naming the armed session while
    /// its row is filtered out, so nothing can be started from a row nobody can see without the
    /// panel saying which one it is. And when the filter is lifted, the row comes back ALREADY
    /// CHECKED — a rebuild that dropped the check would leave the panel and the rack disagreeing
    /// about what is armed.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task AFilterMayHideThePickedRow_ButItNeverDisarmsIt_AndTheRowComesBackChecked()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Assert.Contains(
            "Morning Drift is selected",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);

        var search = Descendant<TextBox>(window, "ScriptedSessionSearchBox");
        TypeIntoSearch(window, search, "gamer");

        // The row is gone and the pick is not.
        Assert.Equal(["SessionRowGamerGirl"], Rows(window).Select(row => row.Name));
        Assert.False(Rows(window)[0].IsChecked);
        Assert.Contains(
            "Morning Drift is selected",
            TextOf(window, "ScriptedSessionPhaseState"),
            StringComparison.Ordinal);

        Backspace(window, 5);

        var morningDrift = Rows(window).Single(row => row.Name == "SessionRowMorningDrift");
        Assert.True(morningDrift.IsChecked);
        Assert.All(
            Rows(window).Where(row => row.Name != "SessionRowMorningDrift"),
            row => Assert.False(row.IsChecked));

        // And the pick still starts THAT session, through the same four gestures as ever.
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Assert.Equal("Start Morning Drift?", TextOf(window, "ScriptedSessionConfirmTitle"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.True(boot.Run.Running);
        Assert.Equal("Morning Drift", boot.Run.Current?.Name);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>The rack's rows, in the order they are drawn. The empty lines are TextBlocks, so
    /// this cannot count one as a row.</summary>
    private static IReadOnlyList<RadioButton> Rows(MainWindow window) =>
        [.. Descendant<StackPanel>(window, "ScriptedSessionRackPanel").Children.OfType<RadioButton>()];

    /// <summary>The four band filters, in the order the toolbar built them.</summary>
    private static IReadOnlyList<CheckBox> Bands(MainWindow window) =>
        [.. Descendant<StackPanel>(window, "ScriptedSessionFilterPanel").Children.OfType<CheckBox>()];

    /// <summary>
    /// Put the caret in the search box, then type into it.
    ///
    /// <para><b>The focus is moved by the control's own <c>Focus()</c> and not by the click above
    /// it, and that is a measured property of the headless platform rather than a shortcut:</b> a
    /// real <c>MouseDown</c>/<c>MouseUp</c> on this box leaves <c>IsFocused</c> false, so every
    /// keystroke after it would go to the window and the box would stay empty. The same suite's
    /// scheduler facts drive their boxes the same way
    /// (<c>CcpClient.HeadlessTests/SchedulerRowHeadlessTests.cs:232-236</c>). What arrives after the
    /// caret is there is REAL input — key down, the platform's text, key up — through the TextBox's
    /// own editing, never an assignment to <c>Text</c>.</para>
    /// </summary>
    private static void TypeIntoSearch(Window window, TextBox search, string text)
    {
        search.Focus();
        window.UpdateLayout();
        Assert.True(search.IsFocused);
        Type(window, text);
    }

    /// <summary>Real typing into whatever holds focus: the key event, then the text the platform's
    /// own translation produced, then the release — the pattern
    /// <c>MantraWindowHeadlessTests.TypeChar</c> measured, because a <c>KeyPress</c> alone carries no
    /// text into the input pipeline.</summary>
    private static void Type(Window window, string text)
    {
        foreach (var c in text)
        {
            var key = Enum.Parse<Key>(char.ToUpperInvariant(c).ToString());
            var physical = Enum.Parse<PhysicalKey>(char.ToUpperInvariant(c).ToString());
            window.KeyPress(key, RawInputModifiers.None, physical, c.ToString());
            window.KeyTextInput(c.ToString());
            window.KeyRelease(key, RawInputModifiers.None, physical, c.ToString());
            window.UpdateLayout();
        }
    }

    /// <summary>Backspace is a keystroke like any other, and the box's own key handling is what
    /// erases the character — so the rack coming back is driven by real input rather than by a test
    /// assigning <c>Text</c>.</summary>
    private static void Backspace(Window window, int count)
    {
        for (var i = 0; i < count; i++)
        {
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
            window.UpdateLayout();
        }
    }

    /// <summary>The four gestures a start really takes, so no fact here reaches past the surface to
    /// call Start().</summary>
    private static void StartMorningDrift(MainWindow window)
    {
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Click(window, Descendant<Button>(window, "ScriptedSessionConfirmButton"));
        Assert.True(window.Session.Scripted.Running);
    }
}
