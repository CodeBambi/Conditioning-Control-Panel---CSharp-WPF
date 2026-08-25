using System.Globalization;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;

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
    /// (§9 D7's rule: absent rather than greyed, and named rather than silently missing).
    ///
    /// <para>The session EDITOR, CUSTOM sessions and now IMPORTING one left this list because they
    /// stopped being absent (<see cref="SessionEditorRules"/>, <see cref="CustomSessionStore"/>,
    /// <see cref="SessionImport"/>). What is left is AUTHORING A TIMELINE, which is the half of
    /// upstream's editor this port models nothing for (<see cref="SessionEditorRules"/>); the
    /// CORNER-GIF overlay one shipped session offers, which is named here for the first time
    /// (<see cref="ScriptedSession.HasCornerGifOption"/> carries the argued refusal); and the XP
    /// award.</para></summary>
    public const string Absences =
        "Not here yet: authoring a session's timeline, the corner-GIF overlay one session offers, "
        + "and the XP award — so a pause is counted and its penalty recorded, but nothing is "
        + "charged for it.";

    /// <summary>The rack's edit action (<c>MainWindow/MainWindow.SessionIO.cs:538</c>,
    /// <c>tooltip_edit_session</c>). Upstream offers it on EVERY row including a built-in, which is
    /// what makes the copy rule reachable at all.</summary>
    public const string EditButton = "Edit session";

    /// <summary>What the rack says when Edit is pressed with nothing picked. Upstream cannot reach
    /// this state — its action lives ON a row and carries that row's id
    /// (<c>MainWindow/MainWindow.SessionIO.cs:1821-1824</c>) — so this is the port's own refusal for
    /// its own one-button-per-selection shape, worded as the start button's twin
    /// (<see cref="NothingSelected"/>).</summary>
    public const string NothingToEdit = "Pick a session first, then press Edit session.";

    /// <summary>The line the rack shows after a save that really landed. It names the FOLDER and
    /// never the file, because the folder is the actionable half and a full path in a panel is a
    /// line nobody can read.</summary>
    public static string EditorSaved(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"Saved “{session.Name}” to your sessions folder ({CustomSessionStore.FolderName}).";
    }

    /// <summary>The line the rack shows after a delete that really happened.</summary>
    public static string EditorDeleted(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"Deleted “{session.Name}”. The built-in sessions are untouched.";
    }

    /// <summary>The editor window's title — upstream's <c>label_session_editor</c>
    /// (<c>Windows/SessionEditorWindow.xaml:6</c>).</summary>
    public const string EditorTitle = "Session Editor";

    /// <summary>
    /// Which of the two things this edit is, said before the user types anything.
    ///
    /// <para>Upstream never says it in the editor and it does not have to: its built-in branch puts
    /// a Save-As dialog on screen at the end (<c>MainWindow/MainWindow.SessionIO.cs:1840-1850</c>),
    /// which announces the copy by asking where to put it. This port saves without asking — the
    /// folder is the port's decision, not the user's — so the sentence the dialog used to carry has
    /// to be somewhere, and the top of the window is where the decision is being made.</para>
    /// </summary>
    public static string EditorProvenance(ScriptedSessionOrigin origin) =>
        origin == ScriptedSessionOrigin.BuiltIn
            ? "Built-in — saving makes your own copy and leaves this one alone."
            : "Yours — saving overwrites it.";

    /// <summary>What the editor does NOT edit. Three of upstream's fields and the whole of its
    /// timeline (<see cref="SessionEditorRules"/> for the derivation this port cannot
    /// run).</summary>
    public const string EditorAbsences =
        "Name, duration and description only. Difficulty and XP stay as authored — upstream works "
        + "them out from a timeline this build does not model — and the session's effects, phases "
        + "and icon are unchanged.";

    /// <summary>Upstream's delete action (<c>MainWindow/MainWindow.SessionIO.cs:543</c>,
    /// <c>tooltip_delete_session</c>).</summary>
    public const string EditorDelete = "Delete";

    /// <summary>The same button once it is holding the question — upstream puts this on a styled
    /// dialog instead (<c>:1953-1957</c>, <c>btn_delete</c>).</summary>
    public const string EditorDeleteConfirm = "Really delete";

    /// <summary>Upstream's confirmation body (<c>msg_delete_session_confirm_0</c>,
    /// <c>:1956</c>).</summary>
    public const string EditorDeleteQuestion =
        "Press Really delete to remove this session. Cancel leaves it alone.";

    /// <summary>Said when the store refused or could not write
    /// (<see cref="CustomSessionStore.Save"/> returned null). It names the two causes a user can
    /// act on rather than an exception type.</summary>
    public const string EditorSaveFailed =
        "That could not be saved — the sessions folder may be read-only or the disk full. Nothing "
        + "was changed.";

    /// <summary>Said when the delete could not happen
    /// (<see cref="CustomSessionStore.Delete"/> returned false).</summary>
    public const string EditorDeleteFailed =
        "That could not be deleted — the file may be gone already or in use. Nothing was changed.";

    /// <summary>The rack's import action. Upstream's caption for the same gesture is the bare word
    /// "Import" on its editor's toolbar (<c>Windows/SessionEditorWindow.xaml:215</c>, <c>btn_import</c>,
    /// <c>Localization/Languages/en.json:910</c>); the object is
    /// named here for <see cref="EditButton"/>'s reason — this strip sits beside other things a
    /// user can import in this build, and a bare "Import" would not say what.</summary>
    public const string ImportButton = "Import session";

    /// <summary>The hint under it. It names the extension because that is what the OS dialog will
    /// be filtering on (<see cref="SessionImport.FileKind"/>), and it names the OUTCOME — the file
    /// becomes one of yours — because that is upstream's outcome
    /// (<c>Services/Session/SessionManager.cs:129-137</c>) and it is the part a user cannot
    /// guess.</summary>
    public const string ImportTooltip =
        "Add a .session.json file from your computer — it is copied into your own sessions";

    /// <summary>
    /// What a completed import says — upstream's <c>msg_imported_session</c>
    /// (<c>Localization/Languages/en.json:3301</c>, "Imported: {0}", shown at
    /// <c>MainWindow/MainWindow.SessionIO.cs:2096</c>), with <see cref="EditorSaved"/>'s second
    /// clause: it names the FOLDER and never the file, because the folder is the actionable half
    /// and no path may leave the picker seam (<see cref="IUserFilePicker"/>).
    /// </summary>
    public static string Imported(ScriptedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return $"Imported “{session.Name}” into your sessions folder ({CustomSessionStore.FolderName}).";
    }

    /// <summary>
    /// What a refused FILE says. Upstream's is "Invalid session file: {0}"
    /// (<c>en.json:3300</c>, <c>Windows/SessionEditorWindow.xaml.cs:1072</c>) with the validator's
    /// own message substituted; the port substitutes the typed reason, because upstream's
    /// <c>{0}</c> can be the JSON reader's exception text
    /// (<c>Services/Session/SessionFileService.cs:167</c>) and that is a sentence the FILE gets to
    /// author.
    ///
    /// <para>Nothing was written and the rack did not move, which is upstream's order
    /// (<c>Services/Session/SessionManager.cs:103-107</c>) and the reason each sentence can end by
    /// saying so.</para>
    /// </summary>
    public static string ImportRefusedFile(SessionFileRefusal reason) =>
        "That file isn't a session this build can import: " + reason switch
        {
            // Upstream's "Failed to parse session file" (:141) and its JsonException arm (:165-169).
            SessionFileRefusal.NotJson =>
                "the bytes are not a session document. Nothing was added.",
            // Upstream's "Session must have an ID" (:147).
            SessionFileRefusal.NoId => "it has no id. Nothing was added.",
            // Upstream's "Session must have a name" (:153).
            SessionFileRefusal.NoName => "it has no name. Nothing was added.",
            // Upstream's "Session duration must be greater than 0" (:159).
            SessionFileRefusal.NoDuration =>
                "its duration is not a positive number of minutes. Nothing was added.",
            _ => "it cannot be imported by this build. Nothing was added.",
        };

    /// <summary>
    /// What a refused READ says — the picker's own reason, before any file was parsed
    /// (<see cref="SessionImportOutcome.RefusedPicker"/>). Never an exception's message: an
    /// <see cref="IOException"/>'s text carries the full path of the file that failed, which is the
    /// shortest route to defeating the seam (<see cref="UserFileRefusal"/>).
    /// </summary>
    public static string ImportRefusedPicker(UserFileRefusal reason) =>
        "Could not read that file: " + reason switch
        {
            // TopLevel.StorageProvider is never null — it degrades to a NoopStorageProvider — so
            // without the probe this would be a dead button rather than a sentence.
            UserFileRefusal.NoPicker =>
                "this desktop has no file picker, so nothing was asked for and nothing was added.",
            UserFileRefusal.ReadFailed => "it could not be read. Nothing was added.",
            UserFileRefusal.WriteFailed => "the place you chose could not be written.",
            UserFileRefusal.TooLarge =>
                "it is larger than "
                + (UserFilePicker.MaxTextBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture)
                + " MB, which is far more than a session file can be. Nothing was added.",
            _ => "it could not be used. Nothing was added.",
        };

    /// <summary>
    /// What an unexpected fault says. Upstream catches nothing at all around its import — an
    /// exception out of <c>ImportSession</c> reaches the click handler
    /// (<c>MainWindow/MainWindow.SessionIO.cs:2093</c>) — and this port shows the exception TYPE
    /// and nothing else, because the message of the classes this path can raise carries the full
    /// path of the file that failed (<see cref="UserFileRefusal"/>). It is
    /// <see cref="PhraseBackupNotices.Faulted"/>'s rule on the other consumer of the same seam.
    /// </summary>
    public static string ImportFaulted(string exceptionTypeName) =>
        "That import could not finish (" + exceptionTypeName + "). Nothing was added.";

    /// <summary>
    /// The rack row's provenance badge — upstream's, in upstream's own words
    /// (<c>MainWindow/MainWindow.SessionIO.cs:588-597</c>: <c>rack_src_yours</c> "YOURS" and
    /// <c>rack_src_builtin</c> "BUILT-IN", drawn as a pill at <c>:508-517</c>).
    ///
    /// <para><b>It was refused at slice 3 and the refusal's premise is now false.</b> The reason
    /// then was that "every row here is built-in, so a badge saying so on all four would carry no
    /// information". With an editor, editing a built-in produces a session with the SAME NAME
    /// beside it, so the badge is the only thing on the row that tells the two apart — it went from
    /// carrying no information to carrying the row's most important bit.</para>
    ///
    /// <para>Upstream's third source, <c>CAT</c> (<c>:593-594</c>), has no member here — and its
    /// reason was restated when <see cref="SessionImport"/> landed rather than left to rot. It is
    /// NOT "nothing in this build imports one" any more; it is that upstream's own file import does
    /// not produce that source either, because every caller overwrites it with <c>Custom</c>
    /// (<see cref="ScriptedSessionOrigin"/> for the three, measured). An imported session reads
    /// <c>YOURS</c> here exactly as it does upstream.</para>
    /// </summary>
    public static string RowProvenance(ScriptedSessionOrigin origin) =>
        origin == ScriptedSessionOrigin.BuiltIn ? "BUILT-IN" : "YOURS";

    /// <summary>The badge's colour, upstream's own (<c>Resources/Theme/Colors.xaml:200</c>
    /// <c>SessionSrcBuiltIn</c> and <c>:202</c> <c>SessionSrcCustom</c>, resolved through the two
    /// brushes at <c>Resources/Theme/Brushes.xaml:211</c> and <c>:213</c>). Two hues rather than
    /// two words, for the same reason the difficulty stripe is a colour: it is the channel a user
    /// reads while scrolling (<c>MainWindow/MainWindow.SessionIO.cs:421-422</c>).</summary>
    public static string RowProvenanceColour(ScriptedSessionOrigin origin) =>
        origin == ScriptedSessionOrigin.BuiltIn ? "#FF5CC8FF" : "#FFFF69B4";

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
    /// <param name="progress">One reading.</param>
    /// <param name="paused">Whether the session is being held. Upstream appends its own
    /// <c>[PAUSED]</c> marker to the running label (<c>MainWindow/MainWindow.Presets.cs:1759</c>,
    /// <c>en.json:3180</c>). <b>Placement diverges and the reason is a gate:</b> upstream's marker
    /// sits beside the countdown in the START button's label, and this port's countdown is the
    /// button's whole caption and a headed capture needle. The marker goes on the numbers line
    /// instead, where it is next to the same frozen clock and cannot break that needle. Default
    /// false, so every existing caller and the headed needle read the byte-identical string they
    /// always did.</param>
    public static string ProgressLine(ScriptedSessionProgress progress, bool paused = false) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{(int)progress.Percent}% — {Clock(progress.Elapsed)} elapsed, {Clock(progress.Remaining)} remaining")
        + (paused ? $" [{Paused}]" : string.Empty);

    /// <summary>Upstream's marker for a held session (<c>en.json:3180</c>,
    /// <c>MainWindow/MainWindow.Presets.cs:1759</c>).</summary>
    public const string Paused = "PAUSED";

    /// <summary>The idle caption of the pause button — upstream's <c>⏸</c> glyph
    /// (<c>MainWindow/MainWindow.Presets.cs:1810</c>) as a word, because every ported caption on
    /// this shell is glyph-stripped (§9 D8) and a lone glyph is not a UIA needle.</summary>
    public const string PauseButtonIdle = "Pause";

    /// <summary>What the same button says once it is holding one — upstream swaps its glyph to
    /// <c>▶</c> and its tooltip to "Resume session" (<c>:1937-1938</c>,
    /// <c>en.json:2228</c>).</summary>
    public const string PauseButtonPaused = "Resume";

    /// <summary>Upstream's pause confirm title (<c>en.json:3387</c>, "⏸ Pause Session?"),
    /// glyph-stripped.</summary>
    public const string PauseConfirmTitle = "Pause Session?";

    /// <summary>Upstream's first confirm line (<c>en.json:3388</c>, "Pausing will cost you 100 XP
    /// from your session reward."), with its number taken from the constant the arithmetic uses
    /// rather than retyped.</summary>
    public static readonly string PauseConfirmCost = string.Create(
        CultureInfo.InvariantCulture,
        $"Pausing costs {ScriptedSessionRun.XpPenaltyPerPause} XP from this session's reward.");

    /// <summary>
    /// Upstream's running total (<c>en.json:3388</c>, "Current penalty: -{0} XP\nAfter this pause:
    /// -{1} XP"), on one line for the reason <see cref="SettingsPromise"/> gives.
    ///
    /// <para><b>The last clause is this port's, and it is the opposite of a smoothed divergence:</b>
    /// nothing in this build awards session XP (<see cref="ScriptedSessionRun.XpPenaltyPerPause"/>
    /// for why), so quoting upstream's cost without it would be the app telling a user it is about
    /// to charge them something it cannot charge. The arithmetic is upstream's and it is real —
    /// <see cref="ScriptedSessionOutcome.XpPenalty"/> carries it — and what is missing is said.</para>
    /// </summary>
    public static string PauseConfirmPenalty(int pauseCount)
    {
        var now = pauseCount * ScriptedSessionRun.XpPenaltyPerPause;
        var after = (pauseCount + 1) * ScriptedSessionRun.XpPenaltyPerPause;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Current penalty: -{now} XP. After this pause: -{after} XP — recorded, not charged: nothing in this build awards session XP yet.");
    }

    /// <summary>Upstream's closing question on the pause path (<c>en.json:3388</c>).</summary>
    public const string PauseConfirmQuestion = "Are you sure?";

    /// <summary>Upstream's pause confirm button (<c>en.json:3389</c>).</summary>
    public const string ConfirmPause = "Yes, pause";

    /// <summary>Upstream's pause refusal button (<c>en.json:3386</c>) — the same one the stop
    /// confirmation uses, as upstream reuses it.</summary>
    public const string CancelPause = CancelStop;

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
