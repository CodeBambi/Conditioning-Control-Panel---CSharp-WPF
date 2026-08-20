namespace CcpClient.Desktop.Scheduling;

/// <summary>
/// One evaluation of the scheduled time window: everything the predicate looked at, and what it
/// concluded.
///
/// <para>It is a READING rather than a bare <c>bool</c> because this is the one module in the port
/// whose predicate decides whether a conditioning session appears on someone's screen. WPF answers
/// the same question and throws the working away into a <c>Debug</c> log line nobody reads
/// (<c>MainWindow/MainWindow.StartStop.cs:694-695</c>). Carrying it lets the panel show the user
/// what was really parsed out of their text — which is the whole of this port's answer to the
/// <c>"8"</c> trap in <see cref="ScheduleWindow.Parse"/>.</para>
/// </summary>
/// <param name="Day">The local day the reading was taken on.</param>
/// <param name="DayActive">Whether that day's box is ticked (<c>:648-659</c>).</param>
/// <param name="TimeOfDay">WPF's <c>now.TimeOfDay</c> (<c>:679</c>).</param>
/// <param name="Start">The parsed start, or the 16:00 fallback (<c>:667-671</c>).</param>
/// <param name="End">The parsed end, or the 22:00 fallback (<c>:673-677</c>).</param>
/// <param name="StartFellBack">True when the start text did not parse and the fallback was used.</param>
/// <param name="EndFellBack">True when the end text did not parse and the fallback was used.</param>
/// <param name="Overnight">WPF's <c>endTime &lt; startTime</c> (<c>:683</c>) — the wrap branch.</param>
/// <param name="InWindow">The verdict: what <c>IsInScheduledTimeWindow()</c> returns.</param>
public readonly record struct ScheduleWindowReading(
    DayOfWeek Day,
    bool DayActive,
    TimeSpan TimeOfDay,
    TimeSpan Start,
    TimeSpan End,
    bool StartFellBack,
    bool EndFellBack,
    bool Overnight,
    bool InWindow)
{
    /// <summary>True when either time was unparseable, so the user's own text is NOT what is in
    /// force. The panel leads with this, because a silent fallback to 16:00-22:00 is the way this
    /// module most plausibly starts a session at a moment nobody asked for.</summary>
    public bool AnyFellBack => StartFellBack || EndFellBack;

    /// <summary>True when the parsed window can never contain any moment: WPF's strict
    /// <c>&lt;</c> at <c>:683</c> sends <c>start == end</c> down the SAME-DAY branch, where the
    /// test is <c>t &gt;= x &amp;&amp; t &lt; x</c>. An equal pair is an EMPTY window, never an
    /// all-day one.</summary>
    public bool Empty => Start == End;
}

/// <summary>
/// WPF's <c>IsInScheduledTimeWindow()</c> (<c>MainWindow/MainWindow.StartStop.cs:642-696</c>),
/// ported clause by clause.
///
/// <para><b>This predicate is the guard, and it is where this module's harm lives.</b> Every other
/// ported module runs inside a session someone started; a false <c>true</c> here puts a
/// conditioning session on a screen unbidden, and a false <c>false</c> refuses to end one. So the
/// clauses below are upstream's operators in upstream's order, and the interesting facts about
/// them are the ones where they say NO.</para>
/// </summary>
public static class ScheduleWindow
{
    /// <summary>WPF's start-time fallback, used when the stored text does not parse
    /// (<c>MainWindow.StartStop.cs:670</c>). NOT the document default, which is
    /// <see cref="SchedulerPresetDocument.DefaultStartTime"/> — 00:00.</summary>
    public static readonly TimeSpan StartFallback = new(16, 0, 0);

    /// <summary>WPF's end-time fallback (<c>MainWindow.StartStop.cs:676</c>). This one happens to
    /// equal the document default.</summary>
    public static readonly TimeSpan EndFallback = new(22, 0, 0);

    /// <summary>
    /// Evaluate the window against a LOCAL reading of the clock.
    ///
    /// <para><b>Clause order is upstream's, and the one difference is inert by construction.</b>
    /// WPF returns at the day gate before it parses anything (<c>:660-664</c>); this method parses
    /// anyway so the panel can report what the text means, and then conjoins
    /// <see cref="ScheduleWindowReading.DayActive"/> into the verdict — which is exactly
    /// <c>isDayActive ? inWindow : false</c>. Parsing has no side effects and cannot change
    /// <c>inWindow</c>, so the two are the same function.</para>
    /// </summary>
    /// <param name="preset">The user's ten settings.</param>
    /// <param name="localNow">The LOCAL wall clock — WPF's <c>DateTime.Now</c> (<c>:645</c>).
    /// Never <c>UtcNow</c>: a user who typed 16:00 meant their own clock.</param>
    public static ScheduleWindowReading Read(SchedulerPresetDocument preset, DateTime localNow)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var day = localNow.DayOfWeek;
        var dayActive = IsDayActive(preset, day);

        var start = Parse(preset.StartTime, StartFallback, out var startFellBack);
        var end = Parse(preset.EndTime, EndFallback, out var endFellBack);

        var timeOfDay = localNow.TimeOfDay;

        // WPF :683. STRICT less-than, which is why an equal pair takes the same-day branch below
        // and yields an EMPTY window rather than an all-day one.
        var overnight = end < start;

        // WPF :686 and :691. Both branches are HALF-OPEN: the start boundary is in (>=) and the
        // end boundary is out (<). At exactly `end` the window is closed, on either branch.
        var withinTimes = overnight
            ? timeOfDay >= start || timeOfDay < end
            : timeOfDay >= start && timeOfDay < end;

        return new ScheduleWindowReading(
            day, dayActive, timeOfDay, start, end, startFellBack, endFellBack, overnight,
            InWindow: dayActive && withinTimes);
    }

    /// <summary>WPF's seven-armed day switch (<c>MainWindow.StartStop.cs:648-659</c>), including
    /// its unreachable <c>_ =&gt; false</c> default arm.</summary>
    public static bool IsDayActive(SchedulerPresetDocument preset, DayOfWeek day)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return day switch
        {
            DayOfWeek.Monday => preset.Monday,
            DayOfWeek.Tuesday => preset.Tuesday,
            DayOfWeek.Wednesday => preset.Wednesday,
            DayOfWeek.Thursday => preset.Thursday,
            DayOfWeek.Friday => preset.Friday,
            DayOfWeek.Saturday => preset.Saturday,
            DayOfWeek.Sunday => preset.Sunday,
            _ => false,
        };
    }

    /// <summary>
    /// WPF's <c>TimeSpan.TryParse(text, out t)</c> plus its fallback
    /// (<c>MainWindow.StartStop.cs:667-671</c>, <c>:673-677</c>) — the SAME BCL call, on the same
    /// current-culture overload, so the accepted grammar is upstream's by construction rather than
    /// by imitation.
    ///
    /// <para><b>And that grammar is not a time-of-day grammar, which is the sharpest edge in this
    /// module.</b> Measured on this build's runtime, not remembered: <c>"8"</c> parses
    /// SUCCESSFULLY as <b>8 days</b> (192 h), <c>"25:00"</c> and <c>"24:00"</c> fail into the
    /// fallback, <c>"-01:00"</c> parses as minus one hour, and <c>"9:5"</c> is 09:05. A user who
    /// types <c>8</c> meaning eight in the morning gets a start of 8.00:00:00, which no
    /// <c>TimeOfDay</c> can ever reach — so with an end of 22:00 the pair goes down the OVERNIGHT
    /// branch and the window silently becomes 00:00-22:00.</para>
    ///
    /// <para><b>The port does not change the parse.</b> Changing it would change when sessions
    /// start, which is the one thing this row must not do differently. Instead the reading is
    /// carried out to the panel and shown, so the user reads "8.00:00:00" instead of a
    /// <c>Debug</c> log line they will never see.</para>
    /// </summary>
    public static TimeSpan Parse(string? text, TimeSpan fallback, out bool fellBack)
    {
        if (TimeSpan.TryParse(text, out var parsed))
        {
            fellBack = false;
            return parsed;
        }

        fellBack = true;
        return fallback;
    }
}
