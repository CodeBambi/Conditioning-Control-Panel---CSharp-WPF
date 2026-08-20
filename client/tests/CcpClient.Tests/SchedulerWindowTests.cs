using CcpClient.Desktop.Scheduling;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-118 — <c>IsInScheduledTimeWindow()</c> (<c>MainWindow/MainWindow.StartStop.cs:642-696</c>),
/// clause by clause.
///
/// <para><b>Every fact in this file is about the predicate saying NO.</b> It decides whether an app
/// starts a conditioning session with nobody at the keyboard, so a false <c>true</c> here is not a
/// degraded effect — it is a session on someone's screen that nobody asked for. The boundaries, the
/// fallbacks, the empty window and the day gate are therefore pinned individually rather than
/// sampled, and the two that are easiest to get subtly wrong — the half-open END and the equal
/// start/end pair — are pinned at the exact tick.</para>
///
/// <para>Pure: no clock, no store, no timer. The local "now" is a value.</para>
/// </summary>
public class SchedulerWindowTests
{
    /// <summary>A Monday. Every fact that is not about the day gate uses it, so a day-of-week
    /// mistake cannot hide inside a time fact.</summary>
    private static readonly DateTime Monday = new(2026, 8, 17, 0, 0, 0, DateTimeKind.Local);

    private static SchedulerPresetDocument Doc(string start, string end, bool enabled = true)
    {
        var doc = new SchedulerPresetDocument { Enabled = enabled, StartTime = start, EndTime = end };
        return doc;
    }

    private static DateTime At(TimeSpan timeOfDay) => Monday.Date + timeOfDay;

    // =====================================================================================
    //  THE BOUNDARIES — half-open, [start, end), on BOTH branches
    // =====================================================================================

    [Fact]
    public void TheSameDayWindowIsHalfOpen_TheStartTickIsIN_AndTheENDTickIsOUT()
    {
        var doc = Doc("16:00", "22:00");

        // WPF :691 — `currentTime >= startTime && currentTime < endTime`.
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(15, 59, 59))).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(16, 0, 0))).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(21, 59, 59))).InWindow);

        // THE CLOSED END. One tick before 22:00 is in; 22:00 itself is out. A `<=` here would
        // hold the window open for a whole extra second every evening — invisible in any sampled
        // test and exactly the kind of edge a scheduled start lands on.
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).InWindow);
        Assert.True(ScheduleWindow.Read(
            doc, At(new TimeSpan(22, 0, 0) - TimeSpan.FromTicks(1))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 1))).InWindow);
    }

    [Fact]
    public void TheOvernightWrapIsHalfOpenTOO_AndItsENDIsTheCLOSEDOne()
    {
        // WPF :686 — `currentTime >= startTime || currentTime < endTime`, taken because
        // endTime < startTime (:683).
        var doc = Doc("22:00", "02:00");
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).Overnight);

        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(21, 59, 59))).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(23, 59, 59))).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(TimeSpan.Zero)).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(1, 59, 59))).InWindow);

        // THE WRAP'S CLOSED END, at the tick.
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(2, 0, 0))).InWindow);
        Assert.True(ScheduleWindow.Read(
            doc, At(new TimeSpan(2, 0, 0) - TimeSpan.FromTicks(1))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(2, 0, 1))).InWindow);
    }

    [Fact]
    public void AnEqualStartAndEnd_IsAnEMPTYWindow_NeverAWholeDay()
    {
        // The single most plausible "helpful" mistake in this predicate: reading start == end as
        // "always on". WPF's `<` at :683 is STRICT, so an equal pair takes the SAME-DAY branch and
        // the test becomes `t >= x && t < x` — false for every t there is.
        var doc = Doc("10:00", "10:00");
        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(10, 0, 0)));
        Assert.False(reading.Overnight);
        Assert.True(reading.Empty);

        for (var minute = 0; minute < 24 * 60; minute += 7)
        {
            Assert.False(ScheduleWindow.Read(doc, At(TimeSpan.FromMinutes(minute))).InWindow);
        }

        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(9, 59, 59))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(10, 0, 0))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(10, 0, 1))).InWindow);
    }

    // =====================================================================================
    //  THE DAY GATE — and it short-circuits
    // =====================================================================================

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    [InlineData(DayOfWeek.Saturday)]
    [InlineData(DayOfWeek.Sunday)]
    public void EachDayIsReadFromItsOwnBox_AndOnlyItsOwn(DayOfWeek day)
    {
        // WPF :648-659 is seven separate settings, not a bitmask. Seven cases, each ticking ONE
        // box, is what catches a switch arm wired to the neighbouring day — which reads correctly
        // on any test that ticks them all.
        var doc = Doc("00:00", "23:59");
        doc.Monday = doc.Tuesday = doc.Wednesday = doc.Thursday = doc.Friday = doc.Saturday = doc.Sunday = false;
        SetDay(doc, day, true);

        // Depth-0 pin (SP-066 framing (c)): the loop below carries every assertion in this fact,
        // so an empty source would make it pass while checking nothing. Seven is also the fact —
        // upstream's switch has seven arms and an unreachable default (:648-659).
        var everyDay = Enum.GetValues<DayOfWeek>();
        Assert.Equal(7, everyDay.Length);

        foreach (var candidate in everyDay)
        {
            var reading = ScheduleWindow.Read(doc, DateOf(candidate) + new TimeSpan(12, 0, 0));
            Assert.Equal(candidate, reading.Day);
            Assert.Equal(candidate == day, reading.DayActive);
            Assert.Equal(candidate == day, reading.InWindow);
        }
    }

    [Fact]
    public void ADayOutSIDETheSevenFailsCLOSED_WhichIsUpstreamsOwnDefaultArm()
    {
        // M-m SURVIVED round 1: the `_ => false` default arm (:659) flipped to `_ => true`. Every
        // fact fed it a real DayOfWeek, so the arm was unreachable and its absence was invisible —
        // and for a module that STARTS SESSIONS the direction of an unreachable default is not a
        // detail. C# enums are not closed sets: a cast produces a value no switch arm names, and
        // upstream chose to answer NO there.
        var doc = Doc("00:00", "23:59");
        Assert.True(ScheduleWindow.IsDayActive(doc, DayOfWeek.Monday));
        Assert.False(ScheduleWindow.IsDayActive(doc, (DayOfWeek)7));
        Assert.False(ScheduleWindow.IsDayActive(doc, (DayOfWeek)99));
        Assert.False(ScheduleWindow.IsDayActive(doc, (DayOfWeek)(-1)));
    }

    [Fact]
    public void AnUntickedDay_RefusesInTheMIDDLEOfAnOtherwisePerfectWindow()
    {
        var doc = Doc("00:00", "23:59");
        doc.Monday = false;

        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(12, 0, 0)));
        Assert.False(reading.DayActive);
        Assert.False(reading.InWindow);

        // And the day gate is a CONJUNCT of the verdict, not a filter applied afterwards: the time
        // test itself is satisfied here, which is what makes the refusal the day's and nothing
        // else's.
        doc.Monday = true;
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(12, 0, 0))).InWindow);
    }

    [Fact]
    public void TheDayGateShortCircuits_SoEvenAnUnreadableTimeOnAnUntickedDayStillRefuses()
    {
        // WPF returns at :664 BEFORE either TimeSpan.TryParse runs. Porting the parse to the front
        // is only safe because the verdict conjoins DayActive; this fact is what keeps that true.
        var doc = Doc("nonsense", "also nonsense");
        doc.Monday = false;

        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(17, 0, 0)));
        Assert.False(reading.InWindow);
        Assert.True(reading.StartFellBack);
        Assert.True(reading.EndFellBack);
        // The fallback window (16:00-22:00) contains 17:00, so the ONLY thing refusing here is the
        // unticked day.
        Assert.True(reading.TimeOfDay >= reading.Start && reading.TimeOfDay < reading.End);
    }

    // =====================================================================================
    //  THE PARSE — and it is not a time-of-day parser
    // =====================================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("25:00")]
    [InlineData("24:00")]
    [InlineData("12:00 PM")]
    public void AnUnreadableStartFallsBackToSIXTEENHUNDRED_WhichIsNotTheDocumentDefault(string text)
    {
        // WPF :667-671. The fallback is 16:00 and the fresh-install DEFAULT is 00:00
        // (CCP.Core/Models/AppSettings.cs:2510) — two different values, and confusing them would
        // move a user's window by sixteen hours.
        var doc = Doc(text, "22:00");
        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(17, 0, 0)));

        Assert.True(reading.StartFellBack);
        Assert.Equal(new TimeSpan(16, 0, 0), reading.Start);
        Assert.NotEqual(SchedulerPresetDocument.DefaultStartTime, "16:00");
        Assert.True(reading.InWindow);

        // 15:00 is inside the DEFAULT window (00:00-22:00) and outside the FALLBACK one. If the
        // fallback were the default, this would be true.
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(15, 0, 0))).InWindow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("25:00")]
    public void AnUnreadableEndFallsBackToTWENTYTWOHUNDRED(string text)
    {
        // WPF :673-677.
        var doc = Doc("16:00", text);
        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(21, 59, 59)));

        Assert.True(reading.EndFellBack);
        Assert.Equal(new TimeSpan(22, 0, 0), reading.End);
        Assert.True(reading.InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).InWindow);
    }

    [Fact]
    public void TheParserIsNotATimeOfDayParser_AndAUserWhoTypesEightGetsEIGHTDAYS()
    {
        // Measured on this runtime before anything rested on it: TimeSpan.TryParse("8") SUCCEEDS
        // and yields 8.00:00:00. This is upstream's own call (:667) and the port keeps it, because
        // changing the parse would change when sessions start.
        //
        // What it does to a real user: start "8" (192 h) is greater than any TimeOfDay, so with an
        // end of 22:00 the pair goes down the OVERNIGHT branch and the window silently becomes
        // 00:00-22:00 — twenty-two hours a day instead of the fourteen they meant.
        var doc = Doc("8", "22:00");
        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(9, 0, 0)));

        Assert.False(reading.StartFellBack);
        Assert.Equal(TimeSpan.FromDays(8), reading.Start);
        Assert.True(reading.Overnight);
        Assert.True(reading.InWindow);

        Assert.True(ScheduleWindow.Read(doc, At(TimeSpan.Zero)).InWindow);
        Assert.True(ScheduleWindow.Read(doc, At(new TimeSpan(21, 59, 59))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).InWindow);

        // The reading carries the real value, which is the whole of this port's mitigation: the
        // panel shows "8.00:00:00" where upstream writes a Debug line nobody reads (:694-695).
        Assert.Equal("8.00:00:00", reading.Start.ToString());
    }

    [Fact]
    public void ANullTimeCanNeverReachTheParser_BecauseTheDocumentCoalescesItFirst()
    {
        // WPF's setters do this (CCP.Core/Models/AppSettings.cs:2514, :2521), which is why the
        // 16:00 fallback is reachable ONLY through non-null text a user typed.
        var doc = new SchedulerPresetDocument { Enabled = true, StartTime = null, EndTime = null };

        Assert.Equal(SchedulerPresetDocument.DefaultStartTime, doc.StartTime);
        Assert.Equal(SchedulerPresetDocument.DefaultEndTime, doc.EndTime);

        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(12, 0, 0)));
        Assert.False(reading.StartFellBack);
        Assert.False(reading.EndFellBack);
        Assert.Equal(TimeSpan.Zero, reading.Start);
        Assert.Equal(new TimeSpan(22, 0, 0), reading.End);
    }

    [Fact]
    public void AFreshInstallsWindowIsMidnightToTwentyTwoHundred_EveryDay_AndTheFeatureIsOFF()
    {
        // The defaults are the model's (AppSettings.cs:2453, :2510, :2517, :2525-2569), not the
        // markup's — SchedulerFeatureControl.xaml:47 shows "16:00" as design-time text that
        // LoadFromSettings overwrites on Loaded (.xaml.cs:43-44).
        var doc = new SchedulerPresetDocument();

        Assert.False(doc.Enabled);
        Assert.Equal("00:00", doc.StartTime);
        Assert.Equal("22:00", doc.EndTime);
        Assert.True(doc.Monday && doc.Tuesday && doc.Wednesday && doc.Thursday
            && doc.Friday && doc.Saturday && doc.Sunday);

        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(12, 0, 0)));
        Assert.True(reading.InWindow);
        Assert.False(reading.Overnight);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(22, 0, 0))).InWindow);
        Assert.False(ScheduleWindow.Read(doc, At(new TimeSpan(23, 0, 0))).InWindow);
    }

    // =====================================================================================
    //  THE READING — what the panel is allowed to say
    // =====================================================================================

    [Fact]
    public void TheReadingCarriesEveryInputTheVerdictUsed_SoThePanelNeedsToGuessAtNothing()
    {
        var doc = Doc("22:00", "02:00");
        var reading = ScheduleWindow.Read(doc, At(new TimeSpan(23, 30, 0)));

        Assert.Equal(DayOfWeek.Monday, reading.Day);
        Assert.True(reading.DayActive);
        Assert.Equal(new TimeSpan(23, 30, 0), reading.TimeOfDay);
        Assert.Equal(new TimeSpan(22, 0, 0), reading.Start);
        Assert.Equal(new TimeSpan(2, 0, 0), reading.End);
        Assert.False(reading.StartFellBack);
        Assert.False(reading.EndFellBack);
        Assert.True(reading.Overnight);
        Assert.False(reading.Empty);
        Assert.True(reading.InWindow);
    }

    [Fact]
    public void TheVerdictIsExactlyDayActiveAndTheTimeTest_OverEverySampledMomentOfAWeek()
    {
        // The one place the port's clause ORDER differs from upstream's: WPF returns at the day
        // gate before parsing, this reads the times first and conjoins DayActive. The two are the
        // same function, and this is the fact that says so over a whole week rather than a point.
        var doc = Doc("22:00", "02:00");
        doc.Wednesday = false;
        doc.Sunday = false;

        var samples = 0;
        var inWindow = 0;
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            for (var minute = 0; minute < 24 * 60; minute += 11)
            {
                var now = DateOf(day) + TimeSpan.FromMinutes(minute);
                var reading = ScheduleWindow.Read(doc, now);
                var expectedTimes = now.TimeOfDay >= reading.Start || now.TimeOfDay < reading.End;
                Assert.Equal(reading.DayActive && expectedTimes, reading.InWindow);
                samples++;
                inWindow += reading.InWindow ? 1 : 0;
            }
        }

        // Depth-0 pins (SP-066 framing (c)), and they are not bookkeeping: a sweep whose loop ran
        // zero times, or whose predicate was stuck on one answer, would satisfy every assertion
        // inside the loop while proving nothing. Both verdicts must really have been produced.
        Assert.Equal(7 * 131, samples);
        Assert.True(inWindow > 0, "the sweep never once entered the window, so it proved nothing");
        Assert.True(inWindow < samples, "the sweep was inside the window at every sampled moment");
    }

    private static DateTime DateOf(DayOfWeek day) =>
        // 2026-08-16 is a Sunday, so adding the enum value lands on the named day.
        new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Local).AddDays((int)day);

    private static void SetDay(SchedulerPresetDocument doc, DayOfWeek day, bool value)
    {
        switch (day)
        {
            case DayOfWeek.Monday: doc.Monday = value; break;
            case DayOfWeek.Tuesday: doc.Tuesday = value; break;
            case DayOfWeek.Wednesday: doc.Wednesday = value; break;
            case DayOfWeek.Thursday: doc.Thursday = value; break;
            case DayOfWeek.Friday: doc.Friday = value; break;
            case DayOfWeek.Saturday: doc.Saturday = value; break;
            case DayOfWeek.Sunday: doc.Sunday = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(day));
        }
    }
}
