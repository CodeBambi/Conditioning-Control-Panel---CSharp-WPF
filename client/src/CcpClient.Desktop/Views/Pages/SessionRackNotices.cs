using System.Globalization;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// The <b>Scripted Sessions</b> row's sentences: the rack row's cells, the two confirmations, and
/// the live readout.
///
/// <para><b>Why the text lives here and not in the page.</b> Every string below is a ported
/// contract with a WPF citation, and the most load-bearing of them —
/// <see cref="SettingsPromise"/> — is the sentence the restore in
/// <see cref="ScriptedSessionDials"/> exists to keep. A sentence in AXAML can only be checked by a
/// headless test that mounts a window; a sentence here is checked by a unit fact, which is where
/// the port already puts panel prose (<see cref="SchedulerPanelNotices"/>,
/// <see cref="HapticsPanelNotices"/>).</para>
///
/// <para><b>Formatted with <see cref="CultureInfo.InvariantCulture"/> throughout</b>, unlike the
/// dial values on this page, and for a reason that is not tidiness: these strings are read back
/// through UIA by the headed capture harness as text needles
/// (<c>client/docs/verification-harness.md</c>), so a machine whose culture renders digits
/// differently would fail a capture that is looking at a perfectly correct screen.</para>
/// </summary>
public static class SessionRackNotices
{
    /// <summary>
    /// The promise the confirmation makes, and the reason the whole snapshot/restore machinery
    /// exists — upstream's own two lines, verbatim
    /// (<c>MainWindow/MainWindow.Presets.cs:1467-1470</c>: "Your current settings will be
    /// temporarily replaced.\nThey will be restored when the session ends.").
    ///
    /// <para>It is ONE line here rather than two because a text carrying a hard break inside a
    /// notice plate is the layout hazard <c>MainWindow.axaml:275-280</c> records; the wording is
    /// unchanged, which is what the contract is about.</para>
    /// </summary>
    public const string SettingsPromise =
        "Your current settings will be temporarily replaced. They will be restored when the "
        + "session ends.";

    /// <summary>Upstream's closing question (<c>MainWindow.Presets.cs:1470</c>).</summary>
    public const string ReadyToBegin = "Ready to begin?";

    /// <summary>Upstream's confirm button (<c>:1474</c>, <c>en.json:1331</c> "▶ Start Session"),
    /// glyph-stripped as every ported caption on this shell is (§9 D8,
    /// <c>MainWindow.axaml:346-349</c>).</summary>
    public const string ConfirmStart = "Start Session";

    /// <summary>Upstream's refusal button (<c>:1474</c>).</summary>
    public const string CancelStart = "Not yet";

    /// <summary>Upstream's stop confirmation (<c>en.json:3382</c>, "⚠ Stop Session?").</summary>
    public const string StopConfirmTitle = "Stop Session?";

    /// <summary>Upstream's closing question on the stop path
    /// (<c>en.json:3383</c>).</summary>
    public const string StopConfirmQuestion = "Are you sure you want to quit?";

    /// <summary>Upstream's stop confirm button (<c>en.json:3385</c>).</summary>
    public const string ConfirmStop = "Yes, stop session";

    /// <summary>Upstream's stop refusal button (<c>en.json:3386</c>).</summary>
    public const string CancelStop = "Keep going";

    /// <summary>The idle caption of the one button (<c>en.json:1331</c>,
    /// glyph-stripped).</summary>
    public const string StartButtonIdle = "Start Session";

    /// <summary>Upstream's refusal when nothing is picked
    /// (<c>MainWindow.Presets.cs:1463</c> returns in silence; the port says which of the two
    /// guards refused, the way every other panel on this page reports a refusal).</summary>
    public const string NothingSelected = "Pick a session first.";

    /// <summary>The second half of upstream's guard (<c>:1463</c>,
    /// <c>Models/SessionDefinition.cs:47</c>).</summary>
    public const string NotAvailable = "That session is not available.";

    /// <summary>What this surface does NOT do, said where the user is
    /// (§9 D7's rule: absent rather than greyed, and named rather than silently missing).</summary>
    public const string Absences =
        "Not here yet: the session editor, custom and imported sessions, the rack's filter, sort "
        + "and search, pause, and the XP award.";

    /// <summary>
    /// The icon cell — upstream's, including its fallback for a session that carries none
    /// (<c>MainWindow/MainWindow.SessionIO.cs:429-432</c>: the clapperboard).
    /// </summary>
    public static string RowIcon(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.IsNullOrWhiteSpace(session.Icon) ? "\U0001F3AC" : session.Icon;
    }

    /// <summary>
    /// The blurb cell: the FIRST LINE of the authored description, trimmed — upstream's row,
    /// exactly (<c>MainWindow.SessionIO.cs:470-478</c>), including its fallback for a session with
    /// no description at all (<c>label_custom_session</c>, <c>en.json:32</c>).
    ///
    /// <para><b>Not <c>vibeSummary</c>, and that was measured rather than assumed:</b> a grep of
    /// the whole shipping tree finds exactly one hit for it, its own declaration
    /// (<c>Models/SessionDefinition.cs:31</c>) — upstream's <c>ToSession()</c> drops it and no
    /// surface reads it. The description's first line is what upstream's rack really shows.</para>
    /// </summary>
    public static string RowBlurb(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var description = session.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            return "Custom session";
        }

        var firstLine = description.Split('\n')[0].Trim();
        return firstLine.Length == 0 ? "Custom session" : firstLine;
    }

    /// <summary>
    /// The two right-hand cells as one line: difficulty then duration — upstream's pill and its
    /// <c>rack_duration</c> cell (<c>MainWindow.SessionIO.cs:485-497</c>, <c>en.json:72</c>
    /// "{0} min").
    ///
    /// <para>Upstream's difficulty text carries a leading star rating
    /// (<c>en.json:3561-3564</c>, "⭐⭐ Medium"); the words are kept and the glyphs are dropped
    /// (§9 D8), because the rating they encode is on this row already as the colour of its
    /// difficulty stripe — which is upstream's own at-a-glance channel for it
    /// (<c>MainWindow.SessionIO.cs:421-426</c>).</para>
    ///
    /// <para><b>Upstream's <c>+{0} XP</c> cell (<c>:497-500</c>, <c>en.json:73</c>) is deliberately
    /// NOT here.</b> No XP is awarded anywhere in this build, so a row promising a reward would be
    /// the fake-available shape the capability contract bans, on the one surface whose whole job is
    /// to tell the user what they are about to agree to.</para>
    /// </summary>
    public static string RowMeta(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Difficulty(session.Difficulty)} · {session.DurationMinutes} min");
    }

    /// <summary>Upstream's four bands (<c>en.json:3561-3564</c>), glyph-stripped.</summary>
    public static string Difficulty(ScriptedSessionDifficulty difficulty) => difficulty switch
    {
        ScriptedSessionDifficulty.Medium => "Medium",
        ScriptedSessionDifficulty.Hard => "Hard",
        ScriptedSessionDifficulty.Extreme => "Extreme",
        _ => "Easy",
    };

    /// <summary>
    /// Upstream's own difficulty colours (<c>Resources/Theme/Colors.xaml:191-197</c>), which paint
    /// the 4px full-bleed stripe at the edge of every rack row — "the one part of the row you can
    /// read at a glance while scrolling" (<c>MainWindow.SessionIO.cs:421-422</c>).
    /// </summary>
    public static string DifficultyStripe(ScriptedSessionDifficulty difficulty) => difficulty switch
    {
        ScriptedSessionDifficulty.Medium => "#FFF5C242",
        ScriptedSessionDifficulty.Hard => "#FFFF8A4C",
        ScriptedSessionDifficulty.Extreme => "#FFF23557",
        _ => "#FF57D9A3",
    };

    /// <summary>Upstream's confirm title (<c>MainWindow.Presets.cs:1465</c>, "🌅 Start {Name}?"),
    /// glyph-stripped.</summary>
    public static string StartConfirmTitle(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"Start {session.Name}?";
    }

    /// <summary>Upstream's first confirm line (<c>:1466</c>).</summary>
    public static string StartConfirmDuration(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.Create(
            CultureInfo.InvariantCulture, $"Duration: {session.DurationMinutes} minutes");
    }

    /// <summary>
    /// Upstream's stop-confirm subject line (<c>en.json:3383</c>, "You're currently in a
    /// session:\n{0} {1}" with the session's icon and name).
    /// </summary>
    public static string StopConfirmSubject(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"You're currently in a session: {RowIcon(session)} {session.Name}";
    }

    /// <summary>
    /// Upstream's two timing lines (<c>en.json:3383</c>, "Time elapsed: {2}\nTime remaining: {3}"),
    /// on one line and from ONE clock reading.
    ///
    /// <para><b>Upstream's XP sentence is dropped, not reworded</b> ("If you stop now, you will
    /// lose ALL {4} XP"): this build awards no XP for a session, so the sentence would be a threat
    /// the app cannot carry out. Recorded as a divergence rather than smoothed over.</para>
    /// </summary>
    public static string StopConfirmTiming(ScriptedSessionProgress progress) =>
        $"Time elapsed: {Clock(progress.Elapsed)} — Time remaining: {Clock(progress.Remaining)}";

    /// <summary>
    /// The running caption of the one button — upstream's, verbatim, including the remaining time
    /// it re-renders on every tick (<c>en.json:2321</c> "STOP SESSION ({0}:{1})",
    /// <c>MainWindow.Presets.cs:1752</c>).
    /// </summary>
    public static string StopButtonRunning(ScriptedSessionProgress progress) =>
        $"STOP SESSION ({Clock(progress.Remaining)})";

    /// <summary>
    /// The phase line.
    ///
    /// <para><b>This is the one thing on the surface upstream does not show anybody</b>, and the
    /// divergence is recorded rather than quiet: upstream's phase handler writes the phase's name
    /// and description to the LOG and nowhere else
    /// (<c>MainWindow/MainWindow.Presets.cs:1770-1776</c>). The phases are authored, named, and the
    /// only structure a session's timeline has; showing the working is the same call
    /// <see cref="SchedulerPanelNotices.DescribeReading"/> already made for the scheduler's parse.
    /// </para>
    /// </summary>
    public static string PhaseLine(ScriptedSession session, ScriptedSessionPhase? phase, int index)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (phase is null)
        {
            return "This session has no named phases.";
        }

        var count = session.Phases.Count;
        var head = string.Create(
            CultureInfo.InvariantCulture, $"Phase {index + 1} of {count} — {phase.Name}");
        return string.IsNullOrWhiteSpace(phase.Description) ? head : $"{head}: {phase.Description}";
    }

    /// <summary>
    /// The numbers line: percent, elapsed and remaining, all three out of ONE
    /// <see cref="ScriptedSessionRun.ReadProgress"/> so they cannot disagree with each other —
    /// which is the reason that method exists (upstream builds its own event args from one
    /// <c>ElapsedTime</c> read, <c>Services/Session/SessionEngine.cs:520-524</c>).
    ///
    /// <para>The percentage is upstream's <c>ProgressPercent</c> (<c>:128-130</c>), which upstream
    /// renders as a bar on a surface this port does not have
    /// (<c>MainWindow/MainWindow.ProgramsTab.cs:1507</c>). Truncated, never rounded up: a session
    /// one second in reads 0%, not 1%.</para>
    /// </summary>
    public static string ProgressLine(ScriptedSessionProgress progress) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)progress.Percent}% — {Clock(progress.Elapsed)} elapsed, {Clock(progress.Remaining)} remaining");

    /// <summary>
    /// What the panel says when nothing is running: which session is armed, or that none is.
    /// </summary>
    public static string IdleLine(ScriptedSession? selected) =>
        selected is null
            ? "Nothing is running. Pick a session, then press Start Session."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{selected.Name} is selected — {selected.DurationMinutes} minutes, "
                    + $"{PhaseCount(selected)}. Press Start Session to begin.");

    /// <summary>
    /// Upstream's MM:SS, exactly: total minutes (never wrapped at 60) then seconds, both padded
    /// (<c>MainWindow/MainWindow.Presets.cs:1752</c>,
    /// <c>:1895-1896</c>).
    /// </summary>
    public static string Clock(TimeSpan span) =>
        string.Create(
            CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes:D2}:{span.Seconds:D2}");

    private static string PhaseCount(ScriptedSession session) =>
        session.Phases.Count == 1
            ? "1 phase"
            : string.Create(CultureInfo.InvariantCulture, $"{session.Phases.Count} phases");
}
