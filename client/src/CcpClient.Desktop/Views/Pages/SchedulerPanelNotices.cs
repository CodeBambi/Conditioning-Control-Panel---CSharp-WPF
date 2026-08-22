using System.Globalization;
using CcpClient.Desktop.Scheduling;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>Scheduler</b> panel's sentences.
///
/// <para><b>This panel carries more weight than any other module's, and the reason is not
/// stylistic.</b> Thirteen rows describe something the user asked for. This one describes the
/// conditions under which the app will start a conditioning session <i>without being asked</i>, so
/// the page has to answer three questions a log line cannot: what is in force right now, what the
/// app read out of the times the user typed, and why it will or will not act.</para>
///
/// <para>Upstream throws that working away — <c>App.Logger?.Debug("Scheduler: Current={Current},
/// Start={Start}, End={End}, InWindow={InWindow}", …)</c>
/// (<c>MainWindow/MainWindow.StartStop.cs:694-695</c>) and a <c>Warning</c> on each unparseable
/// time (<c>:669</c>, <c>:675</c>). A user never sees either. The reading is shown here instead,
/// which is this port's whole answer to the parse trap in
/// <see cref="ScheduleWindow.Parse"/>: the predicate is upstream's, unchanged, and what the user
/// is told about it is not.</para>
/// </summary>
public static class SchedulerPanelNotices
{
    /// <summary>
    /// The lead line: what this row does, in the words a user needs before they tick the box.
    /// It says "start a session by itself" because that is the fact — every other row on this page
    /// only acts inside a session someone started.
    /// </summary>
    public static string DescribeWhatItIs(bool enabled) =>
        enabled
            ? "This is the only thing here that can START a session by itself. While it is on, the "
                + "app checks the clock every 30 seconds and begins a session when your window opens."
            : "This is the only thing here that can START a session by itself. It is switched off, "
                + "so nothing on this page will begin a session unless you press START.";

    /// <summary>
    /// The live line: what the dot means right now, said in full.
    ///
    /// <para><b>The grace is named rather than hidden.</b> For the first 60 seconds of a launch the
    /// scheduler is not on the clock at all — WPF's own delay, and WPF's own reason: "prevents
    /// issues when restarting after an update while in a scheduled time window"
    /// (<c>MainWindow/MainWindow.xaml.cs:622-623</c>). The dot is <c>Off</c> for that minute
    /// because the module genuinely cannot act, and a user who has just ticked the box is entitled
    /// to know why nothing happened.</para>
    /// </summary>
    public static string DescribeLiveState(
        EffectDotState dot, bool enabled, bool polling, ScheduleWindowReading reading, bool sessionRunning)
    {
        if (!enabled)
        {
            return "Off — the scheduler is switched off and will not start or stop anything.";
        }

        if (!polling)
        {
            return "Off — switched on, but not watching the clock yet. The app waits 60 seconds "
                + "after it starts before the scheduler can do anything, so a relaunch in the "
                + "middle of your window does not reopen a session on top of what you were doing.";
        }

        var where = $"It is {reading.Day}, {Clock(reading.TimeOfDay)}, and your window is {Window(reading)}.";
        return dot switch
        {
            EffectDotState.Live when sessionRunning =>
                $"Live — you are inside the scheduled window and a session is running. {where}",
            EffectDotState.Live =>
                $"Live — you are inside the scheduled window. {where}",
            _ => $"Armed — waiting for the window to open. {where}",
        };
    }

    /// <summary>
    /// The reading line: exactly what the app parsed out of the two boxes, and whether it kept
    /// what the user typed.
    ///
    /// <para><b>This is the line that catches the "8" trap.</b> The times are parsed with
    /// <see cref="TimeSpan.TryParse(string, out TimeSpan)"/>, which is upstream's own call and is
    /// not a time-of-day parser: <c>"8"</c> parses successfully as <b>eight days</b>, and
    /// <c>"25:00"</c> does not parse at all and silently becomes 16:00. Both are shown as what they
    /// really are, because the alternative is a window the user did not choose and cannot see.</para>
    /// </summary>
    public static string DescribeReading(ScheduleWindowReading reading)
    {
        var parts = new List<string>();

        if (reading.StartFellBack)
        {
            parts.Add("The start time could not be read, so 16:00 is being used instead.");
        }

        if (reading.EndFellBack)
        {
            parts.Add("The end time could not be read, so 22:00 is being used instead.");
        }

        if (!reading.StartFellBack && !reading.EndFellBack)
        {
            parts.Add($"Reading your times as {Raw(reading.Start)} to {Raw(reading.End)}.");
        }

        if (reading.Empty)
        {
            parts.Add("Start and end are the same, which is an EMPTY window — it will never open, "
                + "and nothing will be started.");
        }
        else if (reading.Overnight)
        {
            parts.Add("The end is before the start, so this window runs overnight: it opens at "
                + $"{Clock(reading.Start)} and closes at {Clock(reading.End)} the next day.");
        }

        if (!reading.DayActive)
        {
            parts.Add($"{reading.Day} is not ticked, so nothing will start today whatever the clock says.");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// The refusal line: what the last tick actually did, in its own words.
    ///
    /// <para>It exists because this module's interesting behaviour is its refusals. "You stopped a
    /// session by hand inside this window, so nothing will be started again until it closes" is a
    /// promise the user needs to be able to read, and until the next tick nothing else on the page
    /// would say it.</para>
    /// </summary>
    public static string DescribeLastTick(SchedulerTickOutcome? last) =>
        last is null
            ? "The scheduler has not checked the clock yet in this run."
            : $"Last check: {last.Detail}.";

    /// <summary>
    /// The absence line — what the Windows app does at an auto-start that this build does not.
    ///
    /// <para>On the PAGE rather than only in a record, on the precedent four earlier rows
    /// set for a half-ported row. The minimize IS ported (<c>ShellTray.Duck</c>, upstream's
    /// <c>MinimizeToTray()</c> at <c>MainWindow/MainWindow.StartStop.cs:615</c>); the two tray
    /// balloons beside it (<c>:616</c>, <c>:631</c>) are not, because <c>ShellTray</c> exposes no
    /// entry for an arbitrary notification and <c>Tray/**</c> was outside that packet's File Scope.</para>
    /// </summary>
    public static string DescribeAbsences() =>
        "When the scheduler starts a session it tucks this window out of the way, exactly as the "
        + "Windows app does. The two tray balloons that app also shows — one when a scheduled "
        + "session begins and one when it ends — are not sent by this build, so the tucked window "
        + "and the START button turning red are what tell you it happened.";

    private static string Window(ScheduleWindowReading reading) =>
        reading.Empty
            ? "empty (the start and end are the same)"
            : reading.Overnight
                ? $"{Clock(reading.Start)} to {Clock(reading.End)} the next day"
                : $"{Clock(reading.Start)} to {Clock(reading.End)}";

    /// <summary>A value inside one day as hh:mm; anything else printed whole, because a parsed
    /// "8" really is 8.00:00:00 and truncating it to "00:00" would hide the trap this line
    /// exists to expose.</summary>
    private static string Clock(TimeSpan value) =>
        value.Ticks is >= 0 and < TimeSpan.TicksPerDay
            ? value.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : value.ToString(null, CultureInfo.InvariantCulture);

    private static string Raw(TimeSpan value) => Clock(value);
}
