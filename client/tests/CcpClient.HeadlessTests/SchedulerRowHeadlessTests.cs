using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Scheduling;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// SP-118 — the Scheduler rack row, driven by REAL headless input on the REAL controls, from a
/// cold composition-root boot with no command-line arguments.
///
/// <para><b>The user story under test is the one the port did not have: the app starts a session by
/// itself.</b> So the facts here are about the SHELL half of that — the row, its panel, its dot,
/// the window getting out of the way, and, above all, the START/STOP button telling the scheduler
/// what a press meant. The predicate and the refusals live next door in
/// <c>CcpClient.Tests.SchedulerWindowTests</c> and <c>SchedulerModuleTests</c>; what cannot be
/// proved there is that the real button on the real shell is wired to any of it.</para>
///
/// <para>Only the two CLOCKS are substituted, and both are declared seams on the composition root.
/// Nothing here waits out the sixty-second grace, or a thirty-second poll, on a wall clock.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, style-resolved
/// classes, real input routing. Nothing here claims composited pixels, and nothing here claims a
/// human ever saw a minimized window or a tray icon.</para>
/// </summary>
public class SchedulerRowHeadlessTests
{
    /// <summary>2026-08-17 is a Monday, 08:00 local: enabled days all ticked, outside a 16:00-22:00
    /// window.</summary>
    private static readonly DateTime MondayMorning = new(2026, 8, 17, 8, 0, 0, DateTimeKind.Local);

    private sealed record Boot(ApplicationHost Host, MainWindow Window, ManualScheduleClock Clock)
    {
        public SessionScheduler Scheduler => Window.Scheduler;

        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);

        /// <summary>WPF's 60 s start-up grace, on the injected clock (<c>MainWindow.xaml.cs:624</c>).</summary>
        public void PassTheGrace() => Clock.Advance(SessionScheduler.StartupGrace);

        /// <summary>Move the local clock to a time on the boot's Monday and let ONE poll fire.</summary>
        public void TickAt(TimeSpan timeOfDay) => Clock.AdvanceTo(MondayMorning.Date + timeOfDay);
    }

    private static async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp118-shell-" + Guid.NewGuid().ToString("N"));
        var clock = new ManualScheduleClock(MondayMorning);
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            ScheduleClockFactory = () => clock,
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

    private static void OpenStudioAndSchedulerRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScheduler"));
    }

    // =====================================================================================
    //  the row
    // =====================================================================================

    [AvaloniaFact]
    public async Task TheSchedulerRowOpensAPanelWithUpstreamsNINEControlsAndNoMore()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);

        var panel = Descendant<StackPanel>(window, "SchedulerModulePanel");
        Assert.True(panel.IsVisible);
        Assert.False(Descendant<StackPanel>(window, "RampModulePanel").IsVisible);

        // Upstream's editor is nine controls: an enable, two times and seven days
        // (Features/SchedulerFeatureControl.xaml.cs:41-51). Asserted as a SET rather than
        // individually, so a tenth control cannot arrive without this fact noticing.
        var boxes = panel.GetVisualDescendants().OfType<CheckBox>().Select(c => c.Name ?? string.Empty).ToList();
        Assert.Equal(
            [
                "SchedulerEnableToggle", "SchedulerDayMon", "SchedulerDayTue", "SchedulerDayWed",
                "SchedulerDayThu", "SchedulerDayFri", "SchedulerDaySat", "SchedulerDaySun",
            ],
            boxes);

        var texts = panel.GetVisualDescendants().OfType<TextBox>().Select(c => c.Name ?? string.Empty).ToList();
        Assert.Equal(["SchedulerStartTimeBox", "SchedulerEndTimeBox"], texts);

        // A fresh install: OFF, 00:00-22:00, every day (CCP.Core/Models/AppSettings.cs:2453,
        // :2510, :2517, :2525-2569) — and NOT the 16:00 the shipping markup shows at design time.
        Assert.False(Descendant<CheckBox>(window, "SchedulerEnableToggle").IsChecked);
        Assert.Equal("00:00", Descendant<TextBox>(window, "SchedulerStartTimeBox").Text);
        Assert.Equal("22:00", Descendant<TextBox>(window, "SchedulerEndTimeBox").Text);
        Assert.All(boxes.Skip(1), name => Assert.True(Descendant<CheckBox>(window, name).IsChecked));

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task RightClickOnTheSchedulerRow_ReallyFlipsItsEnable_AndTheDotFollowsBothWays()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        var row = Descendant<RadioButton>(window, "RowScheduler");
        var page = boot.Studio;

        // The gesture upstream gives this row is `toggle: () => FlipMasterCheckBox(
        // PanelScheduler?.Inner.ChkEnabled)` (Views/Tabs/StudioTabView.xaml.cs:537) — the ONE row
        // in this rack whose right-click does not go through SessionEngine.QuickToggle, because it
        // has no module to arm.
        boot.PassTheGrace();
        Assert.False(boot.Scheduler.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedSchedulerDot);

        Click(window, row, MouseButton.Right);

        Assert.True(boot.Scheduler.Enabled);
        Assert.True(boot.Scheduler.Preset.Enabled);
        Assert.True(Descendant<CheckBox>(window, "SchedulerEnableToggle").IsChecked);

        // Armed, not Live: it is 08:00 and the window is 00:00-22:00... which CONTAINS 08:00, so
        // this is Live. Stated rather than glossed, because the default window is wide.
        Assert.Equal(EffectDotState.Live, page.RenderedSchedulerDot);
        Assert.Contains(
            "live", Descendant<Avalonia.Controls.Shapes.Ellipse>(window, "SchedulerRowDot").Classes);

        Click(window, row, MouseButton.Right);

        Assert.False(boot.Scheduler.Enabled);
        Assert.Equal(EffectDotState.Off, page.RenderedSchedulerDot);
        Assert.DoesNotContain(
            "live", Descendant<Avalonia.Controls.Shapes.Ellipse>(window, "SchedulerRowDot").Classes);

        // The gesture still opens no menu and selects nothing, exactly as on every other row.
        Assert.Null(row.ContextMenu);
        Assert.Empty(window.GetVisualDescendants().OfType<ContextMenu>());

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheDotIsOFFForTheWholeGRACE_EvenWithTheBoxTicked_AndThePanelSaysWhy()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);
        var page = boot.Studio;

        Click(window, Descendant<CheckBox>(window, "SchedulerEnableToggle"));
        Assert.True(boot.Scheduler.Enabled);

        // Upstream's dot would be lit here — it reads the checkbox
        // (Views/Tabs/StudioTabView.xaml.cs:536). This one is a claim about what the module can DO,
        // and for the first sixty seconds of a launch it can do nothing at all.
        Assert.False(boot.Scheduler.Polling);
        Assert.Equal(EffectDotState.Off, page.RenderedSchedulerDot);
        Assert.Contains(
            "not watching the clock yet",
            Descendant<TextBlock>(window, "SchedulerLiveState").Text!,
            StringComparison.Ordinal);

        boot.PassTheGrace();
        window.UpdateLayout();

        Assert.True(boot.Scheduler.Polling);
        Assert.NotEqual(EffectDotState.Off, page.RenderedSchedulerDot);
        Assert.DoesNotContain(
            "not watching the clock yet",
            Descendant<TextBlock>(window, "SchedulerLiveState").Text!,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TypingATimeIntoTheRealBox_WritesTheDocument_AndTheNextTickUsesTheNewWindow()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);

        Click(window, Descendant<CheckBox>(window, "SchedulerEnableToggle"));
        boot.PassTheGrace();

        var start = Descendant<TextBox>(window, "SchedulerStartTimeBox");
        var end = Descendant<TextBox>(window, "SchedulerEndTimeBox");

        // Upstream commits on LostFocus and writes BOTH boxes from one handler
        // (Features/SchedulerFeatureControl.xaml.cs:71-79). Driven the way a user drives it:
        // focus the box, change it, move on.
        start.Focus();
        start.Text = "23:00";
        end.Focus();
        end.Text = "23:30";
        Descendant<CheckBox>(window, "SchedulerEnableToggle").Focus();
        window.UpdateLayout();

        Assert.Equal("23:00", boot.Scheduler.Preset.StartTime);
        Assert.Equal("23:30", boot.Scheduler.Preset.EndTime);

        // 08:00 was inside the OLD default window (00:00-22:00) and is outside the new one, so a
        // tick that still used the old text would start a session here.
        boot.TickAt(new TimeSpan(9, 0, 0));
        Assert.False(window.Session.Engine.Running);

        boot.TickAt(new TimeSpan(23, 0, 0));
        Assert.True(window.Session.Engine.Running);

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task EachDayBoxWritesItsOWNDay_AndLeavesTheOtherSixAlone()
    {
        // M-bl SURVIVED round 1: every day handler rewritten to write Monday. No headless fact
        // clicked a day box at all, so seven controls were wired by inspection only — and a user
        // who unticked Sunday would have unticked Monday instead, which for this row means a
        // session on a morning they had switched off.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);

        (string Box, DayOfWeek Day)[] boxes =
        [
            ("SchedulerDayMon", DayOfWeek.Monday), ("SchedulerDayTue", DayOfWeek.Tuesday),
            ("SchedulerDayWed", DayOfWeek.Wednesday), ("SchedulerDayThu", DayOfWeek.Thursday),
            ("SchedulerDayFri", DayOfWeek.Friday), ("SchedulerDaySat", DayOfWeek.Saturday),
            ("SchedulerDaySun", DayOfWeek.Sunday),
        ];
        Assert.Equal(7, boxes.Length);

        foreach (var (box, day) in boxes)
        {
            Click(window, Descendant<CheckBox>(window, box));

            // Exactly one day changed, and it is this box's own.
            foreach (var (_, candidate) in boxes)
            {
                Assert.Equal(
                    candidate != day,
                    CcpClient.Desktop.Scheduling.ScheduleWindow.IsDayActive(boot.Scheduler.Preset, candidate));
            }

            // Put it back, so the next box starts from a full week.
            Click(window, Descendant<CheckBox>(window, box));
            Assert.True(CcpClient.Desktop.Scheduling.ScheduleWindow.IsDayActive(boot.Scheduler.Preset, day));
        }

        await boot.Host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AnUnreadableTimeIsKeptVERBATIMInTheBox_AndThePanelSaysWhatIsReallyInForce()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);

        var start = Descendant<TextBox>(window, "SchedulerStartTimeBox");
        start.Focus();
        start.Text = "8am";
        Descendant<CheckBox>(window, "SchedulerEnableToggle").Focus();
        window.UpdateLayout();

        // The box keeps what the user typed. Rewriting it to "16:00" would make the control agree
        // with the schedule and hide the fact that their intent was never honoured.
        Assert.Equal("8am", start.Text);
        Assert.Equal("8am", boot.Scheduler.Preset.StartTime);

        // And the panel says what is in force instead — where upstream writes a Warning to a log
        // nobody reads (MainWindow/MainWindow.StartStop.cs:669).
        Assert.Contains(
            "start time could not be read",
            Descendant<TextBlock>(window, "SchedulerReadingState").Text!,
            StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  THE USER STORY, and the harm it must not do
    // =====================================================================================

    [AvaloniaFact]
    public async Task FromAColdShell_TheSchedulerReallyStartsTheSession_AndTheShellGetsOutOfTheWay()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);
        var startStop = window.FindControl<Button>("SessionStartButton")!;

        Click(window, Descendant<CheckBox>(window, "SchedulerEnableToggle"));
        var startBox = Descendant<TextBox>(window, "SchedulerStartTimeBox");
        startBox.Focus();
        startBox.Text = "16:00";
        Descendant<CheckBox>(window, "SchedulerEnableToggle").Focus();
        window.UpdateLayout();

        boot.PassTheGrace();
        Assert.False(window.Session.Engine.Running);
        Assert.Equal("START", startStop.Content);
        Assert.Equal(WindowState.Normal, window.WindowState);

        boot.TickAt(new TimeSpan(16, 0, 0));

        // The session is really running, and the shell's ONE control says so without anyone
        // having pressed it.
        Assert.True(window.Session.Engine.Running);
        await CcpClient.Tests.TestWait.Until(
            () => (string?)startStop.Content == "STOP",
            "the shell's START/STOP control to repaint after a scheduled start",
            () => $"content={startStop.Content}, running={window.Session.Engine.Running}");

        // And the shell got out of the way — WPF's MinimizeToTray() inside the same invoke as the
        // start (MainWindow/MainWindow.StartStop.cs:614). The port NEVER hides it: the taskbar
        // button survives, which is §12 D35's whole point.
        await CcpClient.Tests.TestWait.Until(
            () => window.ShellTray.IsDucked,
            "the shell to duck after a scheduled auto-start",
            () => $"ducked={window.ShellTray.IsDucked}, state={window.WindowState}");
        Assert.Equal(WindowState.Minimized, window.WindowState);
        Assert.True(window.IsVisible);

        window.ShellTray.Restore();
        Assert.Equal(WindowState.Normal, window.WindowState);

        await boot.Host.ShutdownAsync();
        window.Close();
    }

    [AvaloniaFact]
    public async Task PressingTheShellsSTOPButtonInsideTheWindow_StopsItComingBack()
    {
        // THE HARM TEST, through the real button. SP-110 shipped a card the user could not dismiss
        // and a reviewer caught it; the same class here is larger — a session the user stops that
        // restarts itself thirty seconds later, for six hours. The latch is written by the shell's
        // click handler (Views/MainWindow.axaml.cs), which is WPF's own order at
        // MainWindow/MainWindow.StartStop.cs:98-107, and NOTHING else in the product writes it.
        var boot = await BootAsync();
        var window = boot.Window;
        OpenStudioAndSchedulerRow(window);
        var startStop = window.FindControl<Button>("SessionStartButton")!;

        Click(window, Descendant<CheckBox>(window, "SchedulerEnableToggle"));
        boot.PassTheGrace();
        boot.TickAt(new TimeSpan(9, 0, 0));
        Assert.True(window.Session.Engine.Running);
        window.ShellTray.Restore();

        Click(window, startStop);

        Assert.False(window.Session.Engine.Running);
        Assert.True(boot.Scheduler.ManuallyStoppedInWindow);

        // Twelve more polls, all still inside the default 00:00-22:00 window.
        for (var hour = 10; hour < 22; hour++)
        {
            boot.TickAt(TimeSpan.FromHours(hour));
            Assert.False(window.Session.Engine.Running);
        }

        Assert.Equal(1, boot.Scheduler.StartsPerformed);
        Assert.Equal("START", startStop.Content);

        await boot.Host.ShutdownAsync();
        window.Close();
    }

    /// <summary>
    /// The LOCAL clock, by hand — the headless twin of <c>SchedulerModuleTests.ManualScheduleClock</c>.
    /// It is duplicated rather than shared because the two test projects are deliberately separate
    /// (the headless one carries an assembly-wide Avalonia application) and a clock is four
    /// methods; the shared thing that matters — <c>TestWait</c> — is linked, not copied.
    /// </summary>
    private sealed class ManualScheduleClock(DateTime start) : IScheduleClock
    {
        private sealed class Entry
        {
            public DateTime Due;
            public required Action Fire;
            public bool Cancelled;
        }

        private readonly List<Entry> _timers = [];
        private DateTime _now = start;

        public DateTime LocalNow
        {
            get { lock (_timers) { return _now; } }
        }

        public int PendingCount
        {
            get { lock (_timers) { return _timers.Count(t => !t.Cancelled); } }
        }

        public IDisposable Schedule(TimeSpan due, Action fire)
        {
            Entry entry;
            lock (_timers)
            {
                entry = new Entry { Due = _now + due, Fire = fire };
                _timers.Add(entry);
            }

            return new CancelHandle(this, entry);
        }

        public void Advance(TimeSpan by)
        {
            lock (_timers)
            {
                _now += by;
            }

            Pump();
        }

        public void AdvanceTo(DateTime moment)
        {
            lock (_timers)
            {
                if (moment < _now)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(moment), "the local clock does not go backwards in these facts");
                }

                _now = moment;
            }

            Pump();
        }

        private void Pump()
        {
            while (true)
            {
                Entry? next;
                lock (_timers)
                {
                    next = _timers.Where(t => !t.Cancelled && t.Due <= _now).OrderBy(t => t.Due).FirstOrDefault();
                    if (next is null)
                    {
                        return;
                    }

                    _timers.Remove(next);
                }

                next.Fire();
            }
        }

        private sealed class CancelHandle(ManualScheduleClock clock, Entry entry) : IDisposable
        {
            public void Dispose()
            {
                lock (clock._timers)
                {
                    entry.Cancelled = true;
                    clock._timers.Remove(entry);
                }
            }
        }
    }
}
