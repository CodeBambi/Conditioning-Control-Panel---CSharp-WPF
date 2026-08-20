using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Scheduling;
using CcpClient.Desktop.Session;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-118 — the scheduler that OWNS the engine.
///
/// <para><b>What makes this file different from the other thirteen modules' files.</b> Every one of
/// those drives a module INSIDE a session someone started; this one starts the session. So the
/// facts below are weighted the other way round: one proves that it works, and the rest prove that
/// it does not fire — outside the window, while a session is running, while switched off, during
/// the start-up grace, after teardown, and, above all, after the user pressed STOP by hand.</para>
///
/// <para><b>No double stands in for the engine.</b> These facts drive a REAL
/// <see cref="SessionEngine"/> over a real <see cref="PersistenceStore{TModel}"/>, with an empty
/// effect list. SP-110's review found a user-harm defect that a whole mutation sweep had missed
/// because "the test double diverged from the product exactly where the bug lived"; the cheapest
/// way not to repeat that is not to have a double.</para>
///
/// <para><b>Zero wall-clock.</b> Both the reading and the timer come from an injected
/// <see cref="IScheduleClock"/> advanced by hand — including the sixty-second grace, which is the
/// single most tempting thing in this port to sleep on.</para>
/// </summary>
public class SchedulerModuleTests
{
    /// <summary>2026-08-17 is a Monday. Every rig starts at 08:00 local on it.</summary>
    private static readonly DateTime MondayMorning = new(2026, 8, 17, 8, 0, 0, DateTimeKind.Local);

    // =====================================================================================
    //  IT WORKS — one fact, and it is the only one in this file that starts anything
    // =====================================================================================

    [Fact]
    public async Task WhenTheWindowOpens_ItReallyStartsASessionNobodyPressedAButtonFor()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();

        // 08:00 on a Monday: enabled, day ticked, outside the window. Nothing.
        Assert.False(rig.Engine.Running);
        Assert.Equal(EffectDotState.Armed, rig.Scheduler.Dot);

        rig.TickAt(new TimeSpan(16, 0, 0));

        Assert.True(rig.Engine.Running);
        Assert.Equal(SchedulerAction.Started, rig.Scheduler.Last!.Action);
        Assert.Equal(SchedulerReasonCodes.SchedulerStartedSession, rig.Scheduler.Last.Code);
        Assert.True(rig.Scheduler.AutoStartedSession);
        Assert.Equal(1, rig.Scheduler.StartsPerformed);
        Assert.Equal(EffectDotState.Live, rig.Scheduler.Dot);

        // WPF minimizes to tray inside the same invoke as the start (:614). The port's analogue is
        // a projection the shell answers with ShellTray.Duck; here it is counted at the seam.
        Assert.Equal(1, rig.DuckRequests);
    }

    [Fact]
    public async Task WhenTheWindowCloses_ItStopsTheSessionITStarted()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);

        rig.TickAt(new TimeSpan(22, 0, 0));

        Assert.False(rig.Engine.Running);
        Assert.Equal(SchedulerAction.Stopped, rig.Scheduler.Last!.Action);
        Assert.Equal(1, rig.Scheduler.StopsPerformed);
        // WPF clears the flag INSIDE the stop branch (:630), so the very next tick outside the
        // window takes the reset branch rather than trying to stop again.
        Assert.False(rig.Scheduler.AutoStartedSession);

        rig.TickAt(new TimeSpan(22, 30, 0));
        Assert.Equal(SchedulerAction.FlagsCleared, rig.Scheduler.Last!.Action);
        Assert.Equal(1, rig.Scheduler.StopsPerformed);
    }

    // =====================================================================================
    //  THE REFUSALS — the rest of the file
    // =====================================================================================

    [Fact]
    public async Task R1_ADisabledScheduler_DoesNotEvenLOOKAtTheClock()
    {
        // WPF :604 returns BEFORE IsInScheduledTimeWindow() is called. The clock counts its own
        // reads, so this pins the clause ORDER rather than merely the outcome — a scheduler that
        // evaluated first and refused afterwards would pass an outcome-only test and would have
        // moved the one branch that must come first.
        await using var rig = await Rig.StartAsync("00:00", "23:59", enabled: false);
        await rig.PassTheGrace();

        var before = rig.Clock.Reads;
        rig.TickAt(new TimeSpan(12, 0, 0));

        Assert.Equal(before, rig.Clock.Reads);
        Assert.Equal(SchedulerAction.Disabled, rig.Scheduler.Last!.Action);
        Assert.Null(rig.Scheduler.Last.Reading);
        Assert.False(rig.Engine.Running);
        Assert.Equal(EffectDotState.Off, rig.Scheduler.Dot);
    }

    [Fact]
    public async Task R1b_ADisabledSchedulerDoesNotSTOPTheSessionItHadStarted_WhichIsUpstreamsOwnOrdering()
    {
        // A consequence of the enable being the FIRST clause, and a surprising one, so it is
        // pinned rather than discovered later: switching the feature off mid-session leaves the
        // scheduled session running, because the tick returns at :604 before it can reach the stop
        // branch at :622. Upstream behaves exactly this way. The user's STOP button still works,
        // which is why this is survivable — and the panel says the tick "changed nothing".
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);

        rig.Scheduler.SetEnabled(false);
        rig.TickAt(new TimeSpan(22, 0, 0));
        rig.TickAt(new TimeSpan(23, 0, 0));

        Assert.True(rig.Engine.Running);
        Assert.Equal(SchedulerAction.Disabled, rig.Scheduler.Last!.Action);
        Assert.Equal(0, rig.Scheduler.StopsPerformed);
    }

    [Fact]
    public async Task R2_OutsideTheWindow_NoNumberOfTicksStartsAnything()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();

        // A whole working day of polling, on the right day, with the feature on.
        for (var minute = 30; minute < 8 * 60; minute += 30)
        {
            rig.TickAt(new TimeSpan(8, 0, 0) + TimeSpan.FromMinutes(minute));
            Assert.False(rig.Engine.Running);
        }

        Assert.Equal(0, rig.Scheduler.StartsPerformed);
        Assert.Equal(0, rig.DuckRequests);
        Assert.False(rig.Scheduler.AutoStartedSession);
    }

    [Fact]
    public async Task R3andR11_ASessionTheUserStarted_IsNeitherTouchedNorCLAIMED()
    {
        // The two halves are one fact because the second follows from the first: if the scheduler
        // marked a session it did not start, the window closing would stop it. This is the shape
        // WPF really has — StartEngine() has no _isRunning guard (:161) — and the port refuses it.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();

        Assert.True(rig.Engine.Start());
        rig.TickAt(new TimeSpan(17, 0, 0));

        Assert.Equal(SchedulerAction.Held, rig.Scheduler.Last!.Action);
        Assert.Equal(0, rig.Scheduler.StartsPerformed);
        Assert.False(rig.Scheduler.AutoStartedSession);
        Assert.Equal(0, rig.DuckRequests);

        // The window closes on a session the scheduler never started. It must survive.
        rig.TickAt(new TimeSpan(22, 0, 0));
        Assert.True(rig.Engine.Running);
        Assert.Equal(0, rig.Scheduler.StopsPerformed);
        Assert.Equal(SchedulerAction.FlagsCleared, rig.Scheduler.Last!.Action);
    }

    [Fact]
    public async Task R4_AfterTheUserPressesSTOPInsideTheWindow_ItNEVERComesBackWhileThatWindowIsOPEN()
    {
        // THE HEADLINE REFUSAL. WPF latches _manuallyStoppedDuringSchedule at :98-101 and the tick
        // conjoins !it at :608; the only things that clear it are a manual START (:107) and a tick
        // OUTSIDE the window (:638). Without it, a user who pressed STOP at 16:01 would be
        // conditioned again at 16:30, and again at 17:00, for six hours.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);

        // The shell's button: it tells the scheduler what the press MEANS before the engine moves
        // (Views/MainWindow.axaml.cs), which is WPF's own order at :98-107.
        rig.PressStartStopButton();
        Assert.False(rig.Engine.Running);
        Assert.True(rig.Scheduler.ManuallyStoppedInWindow);

        // Eleven more polls, all inside the window.
        for (var minute = 30; minute <= 5 * 60 + 30; minute += 30)
        {
            rig.TickAt(new TimeSpan(16, 0, 0) + TimeSpan.FromMinutes(minute));
            Assert.False(rig.Engine.Running);
        }

        Assert.Equal(1, rig.Scheduler.StartsPerformed);
        Assert.Equal(SchedulerAction.Held, rig.Scheduler.Last!.Action);
        Assert.Contains("you stopped a session by hand", rig.Scheduler.Last.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task R4b_TheHandStopIsForgottenWhenThatWindowCLOSES_AndTomorrowStartsNormally()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));
        rig.PressStartStopButton();
        Assert.True(rig.Scheduler.ManuallyStoppedInWindow);

        // The window closes: WPF's reset branch (:635-639) clears BOTH flags.
        rig.TickAt(new TimeSpan(22, 0, 0));
        Assert.False(rig.Scheduler.ManuallyStoppedInWindow);
        Assert.False(rig.Scheduler.AutoStartedSession);
        Assert.Equal(SchedulerAction.FlagsCleared, rig.Scheduler.Last!.Action);

        // The next day's window opens and is served.
        rig.TickAt(MondayMorning.Date.AddDays(1) + new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);
        Assert.Equal(2, rig.Scheduler.StartsPerformed);
    }

    [Fact]
    public async Task R4c_AManualSTARTOverridesTheHandStop_ImmediatelyAndByItself()
    {
        // WPF :106-107 — pressing START clears the flag before StartEngine runs, because the user
        // starting a session IS them overriding their earlier stop.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));
        rig.PressStartStopButton();
        Assert.True(rig.Scheduler.ManuallyStoppedInWindow);

        rig.PressStartStopButton();

        Assert.True(rig.Engine.Running);
        Assert.False(rig.Scheduler.ManuallyStoppedInWindow);

        // AND THE FLAG THAT SURVIVES, because it is easy to misread as "the current session is the
        // scheduler's": _schedulerAutoStarted records that THIS WINDOW'S OPENING has been served,
        // and nothing in WPF clears it on a manual stop or a manual start (:98-107 write only the
        // other flag). So the window closing at 22:00 still stops the session — the one the user
        // restarted by hand — which is upstream's behaviour exactly (:622-632) and is the SAFE
        // direction of the two: it ends a session, it never begins one.
        Assert.True(rig.Scheduler.AutoStartedSession);
        rig.TickAt(new TimeSpan(22, 0, 0));
        Assert.False(rig.Engine.Running);
        Assert.Equal(SchedulerAction.Stopped, rig.Scheduler.Last!.Action);
    }

    [Fact]
    public async Task R4d_StoppingOUTSIDETheWindowLatchesNothing_BecauseThereIsNoScheduledStartToCountermand()
    {
        // WPF :98 conjoins SchedulerEnabled AND IsInScheduledTimeWindow(). A user who stops a
        // session they started at breakfast has not refused this evening's schedule.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();

        Assert.True(rig.Engine.Start());
        rig.Clock.AdvanceTo(MondayMorning.Date + new TimeSpan(9, 0, 0));
        rig.PressStartStopButton();

        Assert.False(rig.Engine.Running);
        Assert.False(rig.Scheduler.ManuallyStoppedInWindow);

        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);
    }

    [Fact]
    public async Task R4e_AndTheSameStopWithTheSchedulerOFFLatchesNothingEither()
    {
        // The other conjunct at :98. Kept separate so a mutation that drops only the enable check
        // cannot hide behind the window check.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: false);
        await rig.PassTheGrace();

        rig.Clock.AdvanceTo(MondayMorning.Date + new TimeSpan(17, 0, 0));
        Assert.True(rig.Engine.Start());
        rig.PressStartStopButton();

        Assert.False(rig.Scheduler.ManuallyStoppedInWindow);
    }

    [Fact]
    public async Task R5_AnUntickedDayRefusesForTheWholeDay_HoweverManyTimesItIsAsked()
    {
        await using var rig = await Rig.StartAsync("00:00", "23:59", enabled: true);
        rig.Scheduler.SetDay(DayOfWeek.Monday, false);
        await rig.PassTheGrace();

        for (var hour = 9; hour < 24; hour++)
        {
            rig.TickAt(new TimeSpan(hour, 0, 0));
            Assert.False(rig.Engine.Running);
        }

        Assert.Equal(0, rig.Scheduler.StartsPerformed);

        // Tuesday is still ticked, so the refusal was the day's and not the module's.
        rig.TickAt(MondayMorning.Date.AddDays(1) + new TimeSpan(9, 0, 0));
        Assert.True(rig.Engine.Running);
    }

    [Fact]
    public async Task R6_AnUnreadableStartTimeReallyDOESMoveTheWindowToSixteenHundred_ThroughTheLiveTick()
    {
        // The fallback is not a curiosity of the predicate: it decides when a real session begins.
        // A user whose start box says "8am" (which TimeSpan.TryParse rejects) gets 16:00, and this
        // fact is the one that shows the consequence rather than the branch.
        await using var rig = await Rig.StartAsync("8am", "22:00", enabled: true);
        await rig.PassTheGrace();

        rig.TickAt(new TimeSpan(8, 30, 0));
        Assert.False(rig.Engine.Running);

        rig.TickAt(new TimeSpan(15, 59, 0));
        Assert.False(rig.Engine.Running);

        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.True(rig.Engine.Running);
        Assert.True(rig.Scheduler.Last!.Reading!.Value.StartFellBack);
    }

    [Fact]
    public async Task R7_TheWindowsCLOSEDEndIsWhatTheLiveTickUses_NotAnInclusiveOne()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(21, 59, 0));
        Assert.True(rig.Engine.Running);

        // 22:00 exactly. A `<=` end would leave the session running for one more poll.
        rig.TickAt(new TimeSpan(22, 0, 0));
        Assert.False(rig.Engine.Running);
    }

    [Fact]
    public async Task R8_AnEmptyWindowStartsNothing_AllDayAndEveryDay()
    {
        await using var rig = await Rig.StartAsync("10:00", "10:00", enabled: true);
        await rig.PassTheGrace();

        for (var hour = 9; hour < 24; hour++)
        {
            rig.TickAt(new TimeSpan(hour, 0, 0));
        }

        rig.TickAt(MondayMorning.Date.AddDays(1) + new TimeSpan(10, 0, 0));

        Assert.False(rig.Engine.Running);
        Assert.Equal(0, rig.Scheduler.StartsPerformed);
    }

    [Fact]
    public async Task R9_NothingHappensDuringTheSixtySecondGrace_EvenSittingInsideTheWindow()
    {
        // WPF delays the whole scheduler by 60 s and says why: "prevents issues when restarting
        // after an update while in a scheduled time window" (MainWindow.xaml.cs:622-623). A user
        // who relaunches at 18:00 with a 16:00-22:00 window must not be conditioned before the
        // splash clears.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true, at: 18);

        Assert.False(rig.Participant.GracePassed);
        Assert.False(rig.Scheduler.Polling);
        Assert.Equal(EffectDotState.Off, rig.Scheduler.Dot);

        rig.Clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(rig.Engine.Running);
        Assert.Null(rig.Scheduler.Last);

        // The sixtieth second: the start-up check runs and finds the window open.
        rig.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(rig.Participant.GracePassed);
        Assert.True(rig.Engine.Running);
        Assert.Equal(SchedulerAction.Started, rig.Scheduler.Last!.Action);
    }

    [Fact]
    public async Task R10_AfterTeardown_NoAmountOfClockMakesItStartASession()
    {
        // The mirror of the session spine's own AfterStop_NoAmountOfClock… and it matters more
        // here: this is the one thing in the port that could start a session with the app already
        // on its way out.
        var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        Assert.True(rig.Scheduler.Polling);

        await rig.Host.ShutdownAsync();

        Assert.False(rig.Participant.Running);
        Assert.False(rig.Scheduler.Polling);
        Assert.Equal(EffectDotState.Off, rig.Scheduler.Dot);
        Assert.Equal(0, rig.Clock.PendingCount);

        for (var hour = 16; hour < 22; hour++)
        {
            rig.Clock.AdvanceTo(MondayMorning.Date + TimeSpan.FromHours(hour));
        }

        Assert.False(rig.Engine.Running);
        Assert.Equal(0, rig.Scheduler.StartsPerformed);
        await rig.DisposeAsync();
    }

    [Fact]
    public async Task R12_AStartTheSessionRefuses_IsNotCountedAsSchedulerStarted()
    {
        // The start-up check is where WPF's missing guard bites: it does not test _isRunning at
        // all (:562-581), so a session the user started during the 60 s grace would be re-armed
        // AND marked auto-started. The port's engine refuses the second start and the scheduler
        // records the refusal instead of claiming it.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true, at: 18);

        Assert.True(rig.Engine.Start());
        rig.Clock.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(SchedulerAction.StartRefusedBySession, rig.Scheduler.Last!.Action);
        Assert.Equal(SchedulerReasonCodes.SchedulerStartRefusedBySession, rig.Scheduler.Last.Code);
        Assert.False(rig.Scheduler.AutoStartedSession);
        Assert.Equal(0, rig.Scheduler.StartsPerformed);
        Assert.Equal(0, rig.DuckRequests);

        // And therefore the window closing leaves the user's session alone.
        rig.TickAt(new TimeSpan(22, 0, 0));
        Assert.True(rig.Engine.Running);
    }

    [Fact]
    public async Task R13_OneWindowOpeningStartsExactlyONESession_HoweverManyTicksItGets()
    {
        // WPF's !_schedulerAutoStarted conjunct (:608). Without it the scheduler would re-arm the
        // whole rack every thirty seconds for the length of the window.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);
        await rig.PassTheGrace();
        rig.TickAt(new TimeSpan(16, 0, 0));

        rig.PressStartStopButton(); // stop it, so !_isRunning is true again...
        rig.Scheduler.NoteManualToggle(sessionWasRunning: false); // ...and clear the hand-stop latch
        Assert.False(rig.Scheduler.ManuallyStoppedInWindow);
        Assert.True(rig.Scheduler.AutoStartedSession);

        for (var minute = 30; minute <= 300; minute += 30)
        {
            rig.TickAt(new TimeSpan(16, 0, 0) + TimeSpan.FromMinutes(minute));
        }

        // Only the auto-started flag is left standing, and it is enough on its own.
        Assert.Equal(1, rig.Scheduler.StartsPerformed);
        Assert.Equal(1, rig.DuckRequests);
    }

    // =====================================================================================
    //  THE POLL — really on the clock, at WPF's cadence
    // =====================================================================================

    [Fact]
    public async Task TheGraceIsSixtySecondsAndThePollIsThirty_MeasuredOnTheClockItself()
    {
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);

        Assert.Equal(TimeSpan.FromSeconds(60), SessionScheduler.StartupGrace);
        Assert.Equal(TimeSpan.FromSeconds(30), SessionScheduler.PollInterval);

        // One timer at +60 s, and nothing else.
        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.Equal(MondayMorning + TimeSpan.FromSeconds(60), rig.Clock.NextDue);

        rig.Clock.Advance(TimeSpan.FromSeconds(60));

        // The poll took over at +30 s, WPF's DispatcherTimer interval (MainWindow.xaml.cs:618),
        // and there is still exactly ONE timer: a scheduler that leaked one per tick would start
        // firing in bursts.
        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.Equal(MondayMorning + TimeSpan.FromSeconds(90), rig.Clock.NextDue);

        rig.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, rig.Clock.PendingCount);
        Assert.Equal(MondayMorning + TimeSpan.FromSeconds(120), rig.Clock.NextDue);
    }

    [Fact]
    public async Task TheDotIsEarned_OffDuringTheGrace_ArmedOutsideTheWindow_LiveInsideIt()
    {
        // Upstream's dot is `() => App.Settings?.Current?.SchedulerEnabled`
        // (Views/Tabs/StudioTabView.xaml.cs:536) — a claim about a checkbox. This one is a claim
        // about what the module can DO, so the first sixty seconds are Off even though the box is
        // ticked, and the panel says why in words.
        await using var rig = await Rig.StartAsync("16:00", "22:00", enabled: true);

        Assert.True(rig.Scheduler.Enabled);
        Assert.Equal(EffectDotState.Off, rig.Scheduler.Dot);

        await rig.PassTheGrace();
        Assert.Equal(EffectDotState.Armed, rig.Scheduler.Dot);

        rig.TickAt(new TimeSpan(16, 0, 0));
        Assert.Equal(EffectDotState.Live, rig.Scheduler.Dot);

        // Live is about the WINDOW, not about a running session: a scheduler whose session the
        // user stopped is still doing its job.
        rig.PressStartStopButton();
        Assert.False(rig.Engine.Running);
        Assert.Equal(EffectDotState.Live, rig.Scheduler.Dot);

        rig.Scheduler.SetEnabled(false);
        Assert.Equal(EffectDotState.Off, rig.Scheduler.Dot);
    }

    // =====================================================================================
    //  THE SETTINGS — and the store is in all four lists (the D178 class)
    // =====================================================================================

    [Fact]
    public async Task TheNineSettingsSurviveARestart_BecauseTheStoreIsInEveryLifecycleList()
    {
        // SP-117 found a landed store that was in NONE of StartAsync/LogIfDegraded/StopAsync/
        // FlushAsync, so a whole module's dials were silently lost at every launch (D178). This
        // document's are the settings that decide whether a session appears tomorrow morning, so
        // the round trip is pinned rather than assumed.
        var dir = Rig.NewDirectory();
        await using (var rig = await Rig.StartAsync("16:00", "22:00", enabled: false, directory: dir))
        {
            rig.Scheduler.SetEnabled(true);
            rig.Scheduler.SetTimes("19:30", "23:15");
            rig.Scheduler.SetDay(DayOfWeek.Wednesday, false);
            await rig.Participant.FlushAsync(TimeSpan.FromSeconds(5));
        }

        // Reopened with NO settings written by the rig: what is asserted below is what came off
        // disk, not what this test just put there.
        await using var reopened = await Rig.StartAsync(directory: dir);
        Assert.True(reopened.Scheduler.Enabled);
        Assert.Equal("19:30", reopened.Scheduler.Preset.StartTime);
        Assert.Equal("23:15", reopened.Scheduler.Preset.EndTime);
        Assert.False(reopened.Scheduler.Preset.Wednesday);
        Assert.True(reopened.Scheduler.Preset.Monday);
    }

    [Fact]
    public async Task TheTimeBoxesKeepTheUsersOWNTEXT_SoTheirMistakeStaysVisibleToThem()
    {
        // The document stores what was typed and the predicate parses it every evaluation, which
        // is upstream's shape (:667-677). Rewriting "8am" to "16:00" on save would make the box
        // agree with the schedule and hide the fact that the user's intent was never honoured.
        var dir = Rig.NewDirectory();
        await using (var rig = await Rig.StartAsync("16:00", "22:00", enabled: true, directory: dir))
        {
            rig.Scheduler.SetTimes("8am", "quarter past ten");
            await rig.Participant.FlushAsync(TimeSpan.FromSeconds(5));
        }

        await using var reopened = await Rig.StartAsync(directory: dir);
        Assert.Equal("8am", reopened.Scheduler.Preset.StartTime);
        Assert.Equal("quarter past ten", reopened.Scheduler.Preset.EndTime);
        Assert.True(reopened.Scheduler.Reading.StartFellBack);
        Assert.True(reopened.Scheduler.Reading.EndFellBack);
    }

    [Fact]
    public async Task TheRealCompositionRootRegistersIt_AndItsSettingsReachDiskThroughTheRootsOwnTeardown()
    {
        // The wiring, not a hand-built rig: the product's composition root, the product's
        // registration order, and the pre-drain flush slot the root reserves.
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp118-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var clock = new ManualScheduleClock(MondayMorning);
            var root = new CcpClient.Desktop.Lifecycle.CompositionRoot
            {
                SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
                ScheduleClockFactory = () => clock,
            };
            Assert.True(root.Validate(out _));
            var host = root.Build(new StartupTrace());

            var participant = Assert.IsType<SchedulerParticipant>(host.Participants[^1]);
            Assert.IsType<SessionParticipant>(host.Participants[^2]);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            participant.Scheduler.SetEnabled(true);
            participant.Scheduler.SetTimes("19:30", "23:15");

            await host.ShutdownAsync();

            var path = Path.Combine(dir, SchedulerPresetDocument.FileName);
            Assert.True(File.Exists(path), $"the scheduler's settings never reached disk at {path}");
            var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("19:30", json, StringComparison.Ordinal);

            // And teardown really killed the poll: the scheduler's timer is gone from the clock.
            Assert.Equal(0, clock.PendingCount);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // =====================================================================================
    //  the rig
    // =====================================================================================

    private sealed class Rig : IAsyncDisposable
    {
        private readonly bool _ownsDirectory;

        private Rig(
            ApplicationHost host, SchedulerParticipant participant, SessionEngine engine,
            ManualScheduleClock clock, string directory, bool ownsDirectory)
        {
            Host = host;
            Participant = participant;
            Engine = engine;
            Clock = clock;
            Directory = directory;
            _ownsDirectory = ownsDirectory;
            participant.Scheduler.AutoStarted += () => DuckRequests++;
        }

        public ApplicationHost Host { get; }

        public SchedulerParticipant Participant { get; }

        public SessionScheduler Scheduler => Participant.Scheduler;

        public SessionEngine Engine { get; }

        public ManualScheduleClock Clock { get; }

        public string Directory { get; }

        /// <summary>How many times the scheduler asked the shell to get out of the way — WPF's
        /// <c>MinimizeToTray()</c> at <c>MainWindow/MainWindow.StartStop.cs:614</c>.</summary>
        public int DuckRequests { get; private set; }

        public static string NewDirectory() =>
            Path.Combine(Path.GetTempPath(), "ccp-sp118-" + Guid.NewGuid().ToString("N"));

        /// <param name="start">The start box's text, or null to keep whatever was LOADED — which
        /// is what a restart fact needs, and getting it wrong is how a round-trip test silently
        /// asserts its own input.</param>
        /// <param name="end">The end box's text, or null to keep what was loaded.</param>
        /// <param name="enabled">The enable, or null to keep what was loaded.</param>
        public static async Task<Rig> StartAsync(
            string? start = null, string? end = null, bool? enabled = null, int at = 8,
            string? directory = null)
        {
            var dir = directory ?? NewDirectory();
            System.IO.Directory.CreateDirectory(dir);
            var registry = new OperationRegistry();
            var log = new CollectingSchedulerLog();
            var boundary = new UiDispatchBoundary();
            // Bound to an INLINE dispatch so the auto-start projection really travels its product
            // route (EffectSignal.Post skips when unbound), and the signal-thread test is forced
            // true so nothing in this pure-logic project touches an Avalonia dispatcher.
            boundary.Bind(new InlineDispatch());
            var infra = new ParticipantInfrastructure(registry, boundary, log);

            var sessionPreset = new PersistenceStore<SessionPresetDocument>(
                infra.OwnerFor("SessionPreset"), log,
                Path.Combine(dir, SessionPresetDocument.FileName),
                SessionPresetDocument.CurrentSchemaVersion);
            // A REAL engine with no effects: the scheduler's subject is the session's
            // start/stop/running, and an empty rack makes every one of those observable without a
            // single double standing between the scheduler and the thing it drives.
            var engine = new SessionEngine([], sessionPreset);

            var clock = new ManualScheduleClock(MondayMorning.Date + TimeSpan.FromHours(at));
            var participant = new SchedulerParticipant(infra, dir, engine, clock, onSignalThread: () => true);
            var host = new ApplicationHost(
                log, [sessionPreset, participant], new StartupTrace(), registry, boundary);

            Assert.IsType<StartupOutcome.Success>(
                await host.StartParticipantsAsync(TestContext.Current.CancellationToken));

            // The settings under test, written through the module's own setters (never the
            // document directly), so every fact below runs against a document the product wrote.
            if (start is not null || end is not null)
            {
                participant.Scheduler.SetTimes(
                    start ?? participant.Scheduler.Preset.StartTime,
                    end ?? participant.Scheduler.Preset.EndTime);
            }

            if (enabled is { } wanted)
            {
                participant.Scheduler.SetEnabled(wanted);
            }

            return new Rig(host, participant, engine, clock, dir, ownsDirectory: directory is null);
        }

        /// <summary>Let WPF's 60-second start-up grace elapse, on the injected clock.</summary>
        public Task PassTheGrace()
        {
            Clock.Advance(SessionScheduler.StartupGrace);
            return Task.CompletedTask;
        }

        /// <summary>Move the local clock to a time on the rig's Monday and let ONE poll fire
        /// there.</summary>
        public void TickAt(TimeSpan timeOfDay) => Clock.AdvanceTo(MondayMorning.Date + timeOfDay);

        /// <summary>Move the local clock to an absolute moment and let ONE poll fire there.</summary>
        public void TickAt(DateTime moment) => Clock.AdvanceTo(moment);

        /// <summary>
        /// The shell's ONE start/stop control, in its exact order: the scheduler is told what the
        /// press means while the session's state still says which press it was
        /// (<c>Views/MainWindow.axaml.cs</c>, WPF's <c>MainWindow.StartStop.cs:52,98-107</c>).
        /// </summary>
        public void PressStartStopButton()
        {
            Scheduler.NoteManualToggle(Engine.Running);
            Engine.Toggle();
        }

        public async ValueTask DisposeAsync()
        {
            await Host.ShutdownAsync();
            if (!_ownsDirectory)
            {
                return;
            }

            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // A temp directory that will not delete is not this fact's subject.
            }
        }
    }

    private sealed class InlineDispatch : IUiDispatch
    {
        public void Post(Action action) => action();
    }

    private sealed class CollectingSchedulerLog : ILogSink
    {
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get { lock (_lines) { return [.. _lines]; } }
        }

        public void Log(string message)
        {
            lock (_lines)
            {
                _lines.Add(message);
            }
        }
    }

    /// <summary>
    /// The LOCAL clock, by hand. It counts its own <see cref="LocalNow"/> reads, which is what
    /// lets <c>R1</c> pin the clause ORDER — that a disabled scheduler never even asks what time
    /// it is — rather than only the outcome.
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
        private int _reads;

        public DateTime LocalNow
        {
            get
            {
                Interlocked.Increment(ref _reads);
                lock (_timers)
                {
                    return _now;
                }
            }
        }

        /// <summary>How many times anything has asked what time it is.</summary>
        public int Reads => Volatile.Read(ref _reads);

        /// <summary>Live (uncancelled) timers. After a stop this must be zero.</summary>
        public int PendingCount
        {
            get { lock (_timers) { return _timers.Count(t => !t.Cancelled); } }
        }

        /// <summary>When the soonest live timer is due, or null when none is.</summary>
        public DateTime? NextDue
        {
            get
            {
                lock (_timers)
                {
                    return _timers.Where(t => !t.Cancelled).Select(t => (DateTime?)t.Due).Min();
                }
            }
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
