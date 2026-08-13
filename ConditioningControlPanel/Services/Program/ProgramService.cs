using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;

namespace ConditioningControlPanel.Services.Program;

public class ProgramDayEventArgs : EventArgs
{
    public ProgramDefinition Program { get; }
    public ProgramDay Day { get; }
    public ProgramDayRecord Record { get; }

    public ProgramDayEventArgs(ProgramDefinition program, ProgramDay day, ProgramDayRecord record)
    {
        Program = program;
        Day = day;
        Record = record;
    }
}

public class ProgramChapterEventArgs : EventArgs
{
    public ProgramDefinition Program { get; }
    public ProgramChapter Chapter { get; }

    /// <summary>The banked reward's id, lifted off the chapter so a subscriber never has to dig.</summary>
    public string? RewardId => Chapter.RewardId;

    /// <summary>Player-facing description of what the chapter banked.</summary>
    public string? RewardDescription => Chapter.RewardDescription;

    /// <summary>
    /// The enrollment already held this reward when the chapter completed - i.e. this is a later
    /// attempt re-finishing a chapter whose reward was banked on an earlier one. A subscriber that
    /// grants anything MUST check this: banked rewards deliberately survive
    /// <see cref="ProgramEnrollment.RestartForNewAttempt"/>, so without the flag every restart
    /// would re-grant chapter 1 the moment the user reached it again.
    /// </summary>
    public bool AlreadyBanked { get; }

    public ProgramChapterEventArgs(ProgramDefinition program, ProgramChapter chapter, bool alreadyBanked = false)
    {
        Program = program;
        Chapter = chapter;
        AlreadyBanked = alreadyBanked;
    }
}

public class ProgramLapsedEventArgs : EventArgs
{
    public ProgramDefinition Program { get; }
    public ProgramEnrollment Enrollment { get; }
    public IReadOnlyList<int> MissedDays { get; }

    public ProgramLapsedEventArgs(ProgramDefinition program, ProgramEnrollment enrollment, IReadOnlyList<int> missedDays)
    {
        Program = program;
        Enrollment = enrollment;
        MissedDays = missedDays;
    }
}

/// <summary>
/// The multi-day Training Programs runtime: enrollment, the day clock, verification and the ledger.
///
/// Deliberately thin. A program schedules things that already exist - it does not re-implement the
/// session engine, the quest tracking fan-out, or the roadmap's photo plumbing. Verification rides
/// the single choke point every Track* call funnels through (QuestService.UpdateQuestProgress), so
/// there is exactly one tracking pass in the app, not two.
/// </summary>
public class ProgramService : IDisposable
{
    private readonly string _statePath;
    private readonly DispatcherTimer? _saveTimer;
    private readonly DispatcherTimer? _clockTimer;
    private readonly List<Action> _engineUnsubscribe = new();

    /// <summary>
    /// Bumped by every <see cref="MarkDirty"/>, and only ever caught up to by a write that actually
    /// landed. The old boolean was cleared BEFORE the async write ran, so a sharing violation - or
    /// any of the swallowed IO failures below - lost the change silently AND left Dispose with
    /// nothing to flush. A counter also survives a MarkDirty that arrives while a save is in
    /// flight: the write records the generation it serialised, never the current one.
    /// </summary>
    private long _dirtyGeneration;
    private long _savedGeneration;

    /// <summary>Serialises every write to <see cref="_statePath"/>, sync and async alike.</summary>
    private readonly object _writeLock = new();

    /// <summary>Id of the session the program launched, so an unrelated session can't tick the day.</summary>
    private string? _expectedSessionId;

    /// <summary>
    /// The day the launched session was built FOR. A session started at 03:30 finishes after the
    /// 04:00 boundary, by which point "today" is a different day and a different record: crediting
    /// whatever TodayRecord resolves to at completion time stamped the finished day Missed, spent a
    /// day off for it, and filed the completion against tomorrow. Credit the day the work was for.
    /// </summary>
    private int _expectedSessionDayIndex;
    private DateTime _expectedSessionProgramDate;

    /// <summary>
    /// A rollover fell due while the program's own session was still running. The clock is held
    /// until the session ends and its completion has been credited - see <see cref="EvaluateRollover"/>.
    /// </summary>
    private bool _rolloverDeferred;

    /// <summary>
    /// The engine handed over by <see cref="AttachSessionEngine"/>, kept so leaving the program can
    /// end the session the program started. Subscribing to its events was never enough: withdrawing
    /// has to be able to ACT on the engine, not just hear from it.
    /// </summary>
    private SessionEngine? _engine;

    public ProgramState State { get; private set; }

    /// <summary>Every program the user can enroll in. Built-ins today; file-loaded programs later.</summary>
    public IReadOnlyList<ProgramDefinition> Library { get; private set; } = Array.Empty<ProgramDefinition>();

    public event EventHandler? TodayChanged;
    public event EventHandler<ProgramDayEventArgs>? DayCompleted;
    public event EventHandler<ProgramDayEventArgs>? DayMissed;
    public event EventHandler<ProgramChapterEventArgs>? ChapterCompleted;
    public event EventHandler<ProgramDayEventArgs>? ProgramGraduated;
    public event EventHandler<ProgramLapsedEventArgs>? ProgramLapsed;

    public ProgramService()
    {
        _statePath = Path.Combine(
            App.UserDataPath,
            "programs.json");

        State = LoadState();
        Library = BuiltInPrograms.All();

        // Startup rollover check, before any UI reads Today.
        EvaluateRollover();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _saveTimer.Tick += (_, _) =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                if (!HasUnsavedChanges) return;

                SaveAsync();
            };
            _saveTimer.Start();

            // One-minute poll rather than a midnight one-shot, matching QuestService: a one-shot
            // does not survive sleep, DST or the clock being changed under it.
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _clockTimer.Tick += (_, _) =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                EvaluateRollover();
                CheckNudge();
            };
            _clockTimer.Start();
        }

        App.Logger?.Information("ProgramService initialized. Active: {Program} day {Day}, library {Count}",
            State.Active?.ProgramId ?? "none", State.Active?.CurrentDay ?? 0, Library.Count);
    }

    private bool HasPremium => App.Patreon?.HasPremiumAccess == true;

    public ProgramEnrollment? ActiveEnrollment => State.Active;

    public ProgramDefinition? ActiveProgram =>
        State.Active == null ? null : GetProgram(State.Active.ProgramId);

    public ProgramDefinition? GetProgram(string programId) =>
        Library.FirstOrDefault(p => string.Equals(p.Id, programId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The day the user is on right now, or null when nothing is running.</summary>
    public ProgramDay? Today
    {
        get
        {
            var enrollment = State.Active;
            var program = ActiveProgram;
            if (enrollment == null || program == null) return null;
            if (enrollment.State is ProgramEnrollmentState.Graduated or ProgramEnrollmentState.Withdrawn) return null;
            return program.GetDay(enrollment.CurrentDay);
        }
    }

    /// <summary>
    /// Today's ledger row, or null when there is no run or the day has never been touched.
    ///
    /// Deliberately non-mutating. It used to call GetOrCreateRecord, so every repaint of the Today
    /// card inserted a row into the persisted dictionary - without MarkDirty, so the insert either
    /// vanished or rode out on an unrelated save, and it happened even for Graduated and Withdrawn
    /// runs where the day the row claims does not exist. Everything that legitimately needs a row
    /// asks for one through <see cref="EnsureTodayRecord"/>, which marks the state dirty.
    /// </summary>
    public ProgramDayRecord? TodayRecord
    {
        get
        {
            var enrollment = State.Active;
            if (enrollment == null) return null;
            return enrollment.GetRecord(enrollment.CurrentDay);
        }
    }

    /// <summary>
    /// Today's ledger row, created if this is the first thing that happened today. Only for paths
    /// that are actually recording something - see <see cref="TodayRecord"/> for why reading must
    /// never create.
    /// </summary>
    private ProgramDayRecord? EnsureTodayRecord()
    {
        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Active or ProgramEnrollmentState.Paused }) return null;

        var existing = enrollment.GetRecord(enrollment.CurrentDay);
        if (existing != null) return existing;

        var created = enrollment.GetOrCreateRecord(enrollment.CurrentDay, enrollment.CurrentDayDate);
        MarkDirty();
        return created;
    }

    public ProgramChapter? TodayChapter
    {
        get
        {
            var program = ActiveProgram;
            var enrollment = State.Active;
            return program == null || enrollment == null ? null : program.GetChapterForDay(enrollment.CurrentDay);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Enrollment
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the user may start this program. Premium programs need a pledge; an existing active
    /// run blocks a second one (the program is meant to be the authority, not one of five).
    /// </summary>
    public bool CanEnroll(ProgramDefinition program, out string reason)
    {
        reason = "";

        if (program == null) { reason = "Unknown program."; return false; }

        if (State.Active is { State: ProgramEnrollmentState.Active or ProgramEnrollmentState.Paused })
        {
            reason = "Another program is already running.";
            return false;
        }

        // A lapsed run is not finished, it is waiting on a decision (restart or withdraw), and both
        // of those are already on screen. Enrolling straight over it discarded the whole attempt -
        // records, attempt number, banked rewards - with no archive and no prompt. A GRADUATED run
        // stays enrollable: it is done, and Enroll archives it exactly the way DismissGraduated does.
        if (State.Active is { State: ProgramEnrollmentState.Lapsed })
        {
            reason = "Finish or leave the lapsed program first.";
            return false;
        }

        if (program.Tier == ProgramTier.Premium && !HasPremium)
        {
            reason = "This program is Patreon-exclusive.";
            return false;
        }

        if (!program.Validate(out var error))
        {
            reason = error;
            return false;
        }

        return true;
    }

    public ProgramEnrollment? Enroll(
        ProgramDefinition program,
        bool strictMode = false,
        ProgramShareLevel shareLevel = ProgramShareLevel.Private,
        int? dayBoundaryHour = null,
        int nudgeHour = 20)
    {
        if (!CanEnroll(program, out var reason))
        {
            App.Logger?.Warning("Program enrollment refused ({Program}): {Reason}", program?.Id ?? "null", reason);
            return null;
        }

        var now = DateTime.Now;
        var boundary = Math.Clamp(dayBoundaryHour ?? program.Rules.DefaultDayBoundaryHour, 0, 23);

        var enrollment = new ProgramEnrollment
        {
            ProgramId = program.Id,
            StartedAt = now,
            DayBoundaryHour = boundary,
            NudgeHour = nudgeHour,
            CurrentDay = 1,
            CurrentDayDate = ProgramClock.ProgramDate(now, boundary),
            StrictMode = strictMode && program.Rules.StrictAvailable,
            ShareLevel = shareLevel,
            DaysOffRemaining = strictMode && program.Rules.StrictAvailable ? 0 : program.Rules.DaysOffAllowed,
            AttemptNumber = 1,
            State = ProgramEnrollmentState.Active
        };

        enrollment.GetOrCreateRecord(1, enrollment.CurrentDayDate);

        // Whatever was standing there is finished (CanEnroll rejects everything else), so file it
        // in History the way DismissGraduated would rather than dropping it on the floor. The
        // ledger is the only record a completed run leaves.
        if (State.Active != null)
        {
            State.History.Add(State.Active);
            App.Logger?.Information("Archived the previous {State} run of {Program} before enrolling",
                State.Active.State, State.Active.ProgramId);
        }

        State.Active = enrollment;
        MarkDirty();
        Save();

        App.Logger?.Information("Enrolled in program {Program} (strict={Strict}, share={Share}, boundary={Hour})",
            program.Id, enrollment.StrictMode, shareLevel, boundary);

        RaiseTodayChanged();
        return enrollment;
    }

    /// <summary>
    /// Whether the clock can be stopped right now. False only while the program's OWN session is
    /// live, because there is no reading of "paused" that survives it:
    ///
    /// - Let the session run and it finishes into a paused enrollment, where
    ///   <see cref="NotifySessionCompleted"/> refuses to tick the slot (it requires state Active).
    ///   The user completes the day and gets nothing, silently. That is the trap this guard exists
    ///   for and it is the worst of the three outcomes.
    /// - Stop the session on the user's behalf and pausing has just cost them the session they were
    ///   most of the way through, which contradicts "spends nothing".
    /// - Refuse, and pausing keeps its promise exactly. Nothing is lost either way, and STOP is
    ///   right there on the bottom bar for a user who wants the session gone too.
    ///
    /// Note this bounds Pause only. Withdraw is never gated - see <see cref="Withdraw"/>.
    /// </summary>
    public bool CanPause(out string reason)
    {
        reason = "";

        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Active }) return false;

        var engine = _engine;
        if (engine?.IsRunning == true && IsProgramSession(engine.CurrentSession))
        {
            reason = "Today's session is still running.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Stops the clock. Free, reversible, spends nothing - which is exactly why it declines while
    /// today's session is in flight (see <see cref="CanPause"/>). Returns false if it declined.
    /// </summary>
    public bool Pause()
    {
        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Active }) return false;

        if (!CanPause(out var reason))
        {
            App.Logger?.Information("Program {Program} pause declined: {Reason}", enrollment.ProgramId, reason);
            return false;
        }

        enrollment.State = ProgramEnrollmentState.Paused;
        enrollment.PausedAt = DateTime.Now;
        MarkDirty();
        RaiseTodayChanged();
        App.Logger?.Information("Program {Program} paused on day {Day}", enrollment.ProgramId, enrollment.CurrentDay);
        return true;
    }

    /// <summary>
    /// Re-anchors the current day to today so paused time never counts as absence. This is the whole
    /// implementation of "pause stops the clock" - the clock only ever measures from CurrentDayDate.
    /// </summary>
    public void Resume()
    {
        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Paused }) return;

        enrollment.State = ProgramEnrollmentState.Active;
        enrollment.PausedAt = null;
        enrollment.CurrentDayDate = ProgramClock.ProgramDate(DateTime.Now, enrollment.DayBoundaryHour);

        var record = enrollment.GetOrCreateRecord(enrollment.CurrentDay, enrollment.CurrentDayDate);
        record.ProgramDate = enrollment.CurrentDayDate;

        MarkDirty();
        RaiseTodayChanged();
        App.Logger?.Information("Program {Program} resumed on day {Day}", enrollment.ProgramId, enrollment.CurrentDay);
    }

    /// <summary>Always available, on every screen, without commentary.</summary>
    public void Withdraw()
    {
        var enrollment = State.Active;
        if (enrollment == null) return;

        // Before anything else, and before the enrollment is torn down: end today's session if it
        // is still on screen. "Withdraw" has to mean the program stops, not just that its bookkeeping
        // stops. Done first so the stop unwinds against a still-coherent enrollment - see
        // StopProgramSessionIfRunning for the two bugs this closes.
        // The session is being ended BY the withdrawal, not walked out on: it must not read as an
        // abandoned session against the achievement tracker. Leaving a program is a supported exit
        // that costs nothing, and charging it to "Relapse"-style counters made it quietly costly.
        StopProgramSessionIfRunning("withdraw", suppressAbandonTracking: true);

        enrollment.State = ProgramEnrollmentState.Withdrawn;
        State.History.Add(enrollment);
        State.Active = null;
        MarkDirty();
        Save();
        RaiseTodayChanged();
        App.Logger?.Information("Withdrew from program {Program} on day {Day}", enrollment.ProgramId, enrollment.CurrentDay);
    }

    /// <summary>
    /// Start the run over after a lapse. Banked chapter rewards survive untouched and the attempt
    /// counter goes up - a restart should read as devotion, not as a wall of failure.
    /// </summary>
    public void RestartAfterLapse()
    {
        var enrollment = State.Active;
        var program = ActiveProgram;
        if (enrollment == null || program == null) return;
        if (enrollment.State != ProgramEnrollmentState.Lapsed) return;

        enrollment.RestartForNewAttempt(DateTime.Now, program.Rules);
        enrollment.GetOrCreateRecord(1, enrollment.CurrentDayDate);
        MarkDirty();
        Save();
        RaiseTodayChanged();

        App.Logger?.Information("Program {Program} restarted, attempt {Attempt}", program.Id, enrollment.AttemptNumber);
    }

    // ---------------------------------------------------------------------------------------
    // The day clock
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Advance the run to the current program-date, spending days off for anything missed on the way.
    /// Safe to call repeatedly; a no-op when the date has not moved.
    /// </summary>
    public void EvaluateRollover()
    {
        var enrollment = State.Active;
        var program = ActiveProgram;
        if (enrollment == null || program == null) return;
        if (enrollment.State != ProgramEnrollmentState.Active) return;

        // Never judge a day while the user is still inside it. The 04:00 boundary lands in the
        // middle of the sessions this app is actually used for: a 03:30 start crosses it, and the
        // one-minute poll would stamp the day Missed, spend the allowance (or lapse the run) and
        // re-point TodayRecord at tomorrow - all while the session that completes the day is on
        // screen. Held here and re-run from OnSessionEnded once the completion is credited.
        if (IsProgramSessionInFlight)
        {
            if (!_rolloverDeferred)
            {
                _rolloverDeferred = true;
                App.Logger?.Information("Program {Program}: rollover held - day {Day}'s session is still running",
                    program.Id, enrollment.CurrentDay);
            }
            return;
        }

        var today = ProgramClock.ProgramDate(DateTime.Now, enrollment.DayBoundaryHour);

        // Settle the outgoing day before it is judged. Only the day that was current can carry
        // partial progress, so this is the only index worth sweeping.
        if ((today - enrollment.CurrentDayDate).Days > 0)
        {
            SettleAmbientShortfallDay(enrollment, program, enrollment.CurrentDay);
            if (enrollment.State != ProgramEnrollmentState.Active) return;
        }

        var result = ProgramClock.Evaluate(
            enrollment.CurrentDayDate,
            enrollment.CurrentDay,
            today,
            program.LengthDays,
            enrollment.DaysOffRemaining,
            enrollment.IsDayComplete);

        if (result.ClockWentBackwards)
        {
            // The anchor is in the future, so the gap stays negative and every later evaluation
            // returns early: the run parks on this day forever and no amount of waiting fixes it.
            // A clock correction is not an absence - re-anchor and say so, spend nothing.
            App.Logger?.Warning(
                "Program {Program}: system clock moved backwards ({Anchor:yyyy-MM-dd} -> {Today:yyyy-MM-dd}) - re-anchoring day {Day}, nothing spent",
                program.Id, enrollment.CurrentDayDate, today, enrollment.CurrentDay);

            enrollment.CurrentDayDate = today;
            var reanchored = enrollment.GetOrCreateRecord(enrollment.CurrentDay, today);
            reanchored.ProgramDate = today;

            MarkDirty();
            Save();
            RaiseTodayChanged();
            return;
        }

        if (!result.Advanced) return;

        foreach (var dayIndex in result.MissedDays)
        {
            var record = enrollment.GetOrCreateRecord(dayIndex, enrollment.CurrentDayDate.AddDays(dayIndex - enrollment.CurrentDay));
            record.Missed = true;
            record.DayOffSpent = !result.Lapsed;

            var day = program.GetDay(dayIndex);
            if (day != null)
                DayMissed?.Invoke(this, new ProgramDayEventArgs(program, day, record));
        }

        if (result.Lapsed)
        {
            enrollment.DaysOffRemaining = 0;
            enrollment.State = ProgramEnrollmentState.Lapsed;
            enrollment.LapsedAt = DateTime.Now;
            MarkDirty();
            Save();

            App.Logger?.Information("Program {Program} lapsed after {Missed} missed day(s)",
                program.Id, result.MissedDays.Count);

            ProgramLapsed?.Invoke(this, new ProgramLapsedEventArgs(program, enrollment, result.MissedDays));
            RaiseTodayChanged();
            return;
        }

        enrollment.DaysOffRemaining = Math.Max(0, enrollment.DaysOffRemaining - result.DaysOffSpent);
        enrollment.CurrentDay = result.NewCurrentDay;
        enrollment.CurrentDayDate = today;

        var newRecord = enrollment.GetOrCreateRecord(enrollment.CurrentDay, today);
        newRecord.ProgramDate = today;
        if (result.IsReturnDay) newRecord.IsReturnDay = true;

        MarkDirty();
        Save();

        App.Logger?.Information("Program {Program} rolled over to day {Day} (missed {Missed}, days off left {Left})",
            program.Id, enrollment.CurrentDay, result.MissedDays.Count, enrollment.DaysOffRemaining);

        // The clock reports RanPastEnd when the absence swallowed the rest of the program. Nothing
        // read it, so an unbounded absence on the final day cost one day off and then parked the run
        // on that day indefinitely - never graduating, never lapsing, with no exit but Withdraw.
        var closedOut = result.RanPastEnd && ResolveRunPastEnd(program, enrollment);
        if (!closedOut) RaiseTodayChanged();
    }

    /// <summary>
    /// Decide what an absence that outran the program means. Returns true when the run has been
    /// closed out (graduated), false when it stays open on the final day.
    ///
    /// The final day is already finished -> graduate, because the only thing that was ever missing
    /// was someone opening the app. Otherwise the user is left standing on the final day, which the
    /// normal Return Day / allowance path above has already priced; the run is one day's work from
    /// finishing rather than parked forever.
    /// </summary>
    private bool ResolveRunPastEnd(ProgramDefinition program, ProgramEnrollment enrollment)
    {
        var finalDay = program.GetDay(program.LengthDays);
        var finalRecord = enrollment.GetRecord(program.LengthDays);

        if (finalDay != null && finalRecord is { DayCompleted: true }
            && enrollment.State == ProgramEnrollmentState.Active)
        {
            App.Logger?.Information(
                "Program {Program}: absence ran past the end and the final day was already complete - graduating",
                program.Id);

            Graduate(program, enrollment, finalDay, finalRecord);
            Save();
            RaiseTodayChanged();
            return true;
        }

        App.Logger?.Information(
            "Program {Program}: absence ran past the end - the run stands on final day {Day}, still to be finished",
            program.Id, program.LengthDays);
        return false;
    }

    // ---------------------------------------------------------------------------------------
    // The daily session
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Build today's session. Transient: never written to disk and never registered with
    /// SessionManager, so it cannot collide with the user's own sessions or spawn a stray card.
    /// </summary>
    /// <summary>
    /// #747: a Return Day runs shorter than the day it replaces. Single source of truth, because the
    /// Today card used to advertise the authored 30 minutes while the engine built an 18-minute
    /// session - and every minute-denominated task was then measured against the shorter run.
    /// </summary>
    public static int ReturnDayMinutes(int authoredMinutes) =>
        Math.Max(15, (int)Math.Round(authoredMinutes * 0.6));

    public Models.Session? BuildTodaySession()
    {
        var program = ActiveProgram;
        var day = Today;
        if (program == null || day == null) return null;

        try
        {
            var session = ProgramSessionBuilder.Build(program, day);

            // A Return Day is deliberately gentler than the day it replaces.
            var record = EnsureTodayRecord();
            if (record?.IsReturnDay == true)
            {
                session.DurationMinutes = ReturnDayMinutes(session.DurationMinutes);
                session.Name += " (Return)";
            }

            _expectedSessionId = session.Id;

            // Pin the day this session was built for. The session outlives the day whenever it
            // crosses the boundary hour, and the completion belongs to the day that was prescribed.
            _expectedSessionDayIndex = day.DayIndex;
            _expectedSessionProgramDate = State.Active?.CurrentDayDate ?? DateTime.Now.Date;

            return session;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to build program session for {Program} day {Day}", program.Id, day.DayIndex);
            return null;
        }
    }

    /// <summary>
    /// True when <paramref name="session"/> is the one this program launched.
    ///
    /// The Programs tab asks this before it claims a running session as today's: a session
    /// started from the Dashboard, a preset or the remote must never drive the day's progress
    /// bar or mark the day complete. Same discriminator OnSessionCompleted uses, so the two can
    /// never disagree. Reads only in-memory state - safe to call every engine tick.
    /// </summary>
    public bool IsProgramSession(Models.Session? session) =>
        session != null
        && _expectedSessionId != null
        && string.Equals(session.Id, _expectedSessionId, StringComparison.Ordinal);

    /// <summary>The program's own session is on screen right now. Reads in-memory state only.</summary>
    private bool IsProgramSessionInFlight
    {
        get
        {
            try
            {
                var engine = _engine;
                return engine?.IsRunning == true && IsProgramSession(engine.CurrentSession);
            }
            catch
            {
                // A dead engine must never wedge the clock shut - a rollover we run one minute
                // early is survivable, one we never run again is not.
                return false;
            }
        }
    }

    /// <summary>
    /// Ends the running session if - and only if - it is the one this program started, and forgets
    /// the expected id either way. Returns true if a session was actually stopped.
    ///
    /// Leaving a program used to leave its session running, which was two bugs in one. The visible
    /// one: the user withdraws to get out, and the program's flashes, videos and lock cards keep
    /// coming for the rest of the prescribed hour. The quiet one: <see cref="IsProgramSession"/>
    /// answers purely from <see cref="_expectedSessionId"/>, which Withdraw did not clear, so
    /// MainWindow's feature lock (MainWindow.SessionFeatureLock.cs) stayed ON for a program the user had
    /// already left - greying out their own Dashboard toggles and citing a run that no longer
    /// existed. Ending the session fixes both, because the lock is derived from engine liveness.
    ///
    /// Stops with completed:false, i.e. exactly as if the user had pressed STOP: the day's session
    /// slot does not tick, because a withdrawn day was not done. That also means SessionCompleted
    /// never fires, which is why the id is cleared here rather than relying on OnSessionCompleted.
    ///
    /// Foreign sessions are deliberately untouched. If the user is running a preset or a remote
    /// session while a program happens to be enrolled, walking away from the program is not
    /// permission to kill something the program did not start.
    /// </summary>
    public bool StopProgramSessionIfRunning(string reason, bool suppressAbandonTracking = false)
    {
        try
        {
            var engine = _engine;
            if (engine == null || !engine.IsRunning) return false;
            if (!IsProgramSession(engine.CurrentSession)) return false;

            App.Logger?.Information("ProgramService: stopping program session ({Reason})", reason);

            // Raises SessionStopped synchronously, which is what re-derives the feature lock and
            // resets the bottom bar. Clear the id AFTER the stop so anything listening on that
            // event can still tell the session apart from a foreign one while it unwinds.
            engine.StopSession(suppressAbandonTracking: suppressAbandonTracking);
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "ProgramService: could not stop the program session ({Reason})", reason);
            return false;
        }
        finally
        {
            _expectedSessionId = null;
            _expectedSessionDayIndex = 0;
        }
    }

    /// <summary>
    /// Wire the MainWindow-owned session engine. Mirrors BarkService.AttachSessionEngine - the engine
    /// is created lazily on first session, so the service cannot subscribe at its own construction.
    /// Re-attaching safely detaches the previous engine.
    /// </summary>
    public void AttachSessionEngine(SessionEngine engine)
    {
        if (engine == null) return;

        try
        {
            foreach (var unsubscribe in _engineUnsubscribe)
            {
                try { unsubscribe(); } catch { /* detaching a dead engine must never throw */ }
            }
            _engineUnsubscribe.Clear();

            EventHandler<SessionCompletedEventArgs> completed = (_, e) => OnSessionCompleted(e);
            engine.SessionCompleted += completed;
            _engineUnsubscribe.Add(() => engine.SessionCompleted -= completed);

            // A held rollover has to be released on ANY end, not just a completion: a session the
            // user stops at 04:05 would otherwise keep the clock shut until the next minute tick,
            // and a stop during shutdown would never release it at all.
            EventHandler stopped = (_, _) => OnSessionEnded();
            engine.SessionStopped += stopped;
            _engineUnsubscribe.Add(() => engine.SessionStopped -= stopped);

            _engine = engine;

            App.Logger?.Debug("ProgramService: attached to SessionEngine");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "ProgramService: AttachSessionEngine failed");
        }
    }

    private void OnSessionCompleted(SessionCompletedEventArgs e)
    {
        try
        {
            if (e?.Session == null) return;
            if (_expectedSessionId == null) return;
            if (!string.Equals(e.Session.Id, _expectedSessionId, StringComparison.Ordinal)) return;

            _expectedSessionId = null;
            NotifySessionCompleted();

            // Spent. A later call must fall back to the current day rather than credit a day the
            // run has long since moved past.
            _expectedSessionDayIndex = 0;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "ProgramService: session completion handling failed");
        }
    }

    /// <summary>
    /// Releases a rollover held by <see cref="EvaluateRollover"/> once the session it was waiting on
    /// has ended.
    ///
    /// Posted rather than run inline: SessionStopped is raised PART WAY through StopSession, before
    /// the completion event that credits the day. Running the clock here would judge the day a
    /// moment before its completion landed - which is the whole bug the hold exists to prevent.
    /// Background priority is the same "StopSession has fully unwound" beat the Programs tab uses.
    /// </summary>
    private void OnSessionEnded()
    {
        try
        {
            if (!_rolloverDeferred) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                _rolloverDeferred = false;
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!_rolloverDeferred) return;
                    _rolloverDeferred = false;
                    EvaluateRollover();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ProgramService: held rollover failed to run");
                }
            }), DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "ProgramService: session end handling failed");
            _rolloverDeferred = false;
        }
    }

    /// <summary>
    /// Mark the session slot done for the day the session was PRESCRIBED for. Only reachable from a
    /// completed program session.
    ///
    /// The day is taken from <see cref="_expectedSessionDayIndex"/>, not from wherever the clock has
    /// since moved to: a session begun at 03:30 finishes past the 04:00 boundary and belongs to the
    /// day it was built for. Falls back to the current day for a completion that arrives with no
    /// pinned day (a caller outside the build path).
    /// </summary>
    public void NotifySessionCompleted()
    {
        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Active }) return;

        var dayIndex = _expectedSessionDayIndex > 0 ? _expectedSessionDayIndex : enrollment.CurrentDay;
        var programDate = _expectedSessionDayIndex > 0 ? _expectedSessionProgramDate : enrollment.CurrentDayDate;

        var record = enrollment.GetOrCreateRecord(dayIndex, programDate);
        if (record.SessionCompleted) return;

        record.SessionCompleted = true;
        record.SessionCompletedAt = DateTime.Now;

        // The day was on its way to being judged an absence when the work landed. Completing it
        // has to undo that verdict, not sit alongside it, or the record reads as missed AND done.
        if (record.Missed)
        {
            App.Logger?.Information(
                "Program {Program} day {Day}: completion arrived for a day already stamped missed - clearing the miss",
                enrollment.ProgramId, dayIndex);
            record.Missed = false;
            if (record.DayOffSpent)
            {
                record.DayOffSpent = false;
                enrollment.DaysOffRemaining++;
            }
        }

        MarkDirty();

        App.Logger?.Information("Program {Program} day {Day}: session slot completed",
            enrollment.ProgramId, dayIndex);

        CheckDayCompletion(dayIndex);
        RaiseTodayChanged();
    }

    // ---------------------------------------------------------------------------------------
    // Verification
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Mirror of the quest fan-out. Called from QuestService.UpdateQuestProgress - the single private
    /// choke point every category-based Track* call already funnels through - so program tasks and
    /// daily quests observe exactly the same signals and can never disagree about what happened.
    /// </summary>
    public void TrackVerifier(QuestCategory category, int amount)
    {
        if (amount <= 0) return;

        var enrollment = State.Active;
        var day = Today;
        if (enrollment is not { State: ProgramEnrollmentState.Active } || day == null) return;

        var record = EnsureTodayRecord();
        if (record == null || record.DayCompleted) return;

        var progressed = false;
        var taskFinished = false;

        foreach (var task in day.Tasks)
        {
            if (task.Kind != ProgramTaskKind.AutoVerified) continue;
            if (task.Verifier != category) continue;
            if (record.CompletedTaskIds.Contains(task.Id)) continue;
            if (IsTaskBlocked(task)) continue;

            record.TaskProgress.TryGetValue(task.Id, out var current);
            current += amount;
            record.TaskProgress[task.Id] = current;
            progressed = true;

            if (current >= EffectiveTarget(day, task, record))
            {
                record.CompletedTaskIds.Add(task.Id);
                taskFinished = true;
                App.Logger?.Information("Program {Program} day {Day}: task '{Task}' completed",
                    enrollment.ProgramId, day.DayIndex, task.Id);
            }
        }

        if (day.Ambient is { RequiredMinutes: > 0 } ambient && ambient.Verifier == category)
        {
            record.AmbientMinutes += amount;
            progressed = true;
        }

        if (!progressed) return;

        MarkDirty();
        CheckDayCompletion();
        RaiseTodayChanged();

        // Verified work is the one thing in this file the user cannot redo: the signal came and
        // went. The 30s timer could drop up to half a minute of it on a crash, so a finished task
        // is written now. Progress ticks still ride the timer - they arrive far too often to
        // justify a disk write each.
        if (taskFinished) Save();
    }

    /// <summary>
    /// The target a task is actually judged against today.
    ///
    /// A Return Day shortens the session to <see cref="ReturnDayMinutes"/> but the day's tasks were
    /// authored against the full run. For minute-denominated verifiers that made several authored
    /// days arithmetically unpassable exactly when the user was being forgiven - Takeover d4_pink
    /// asks for 15 pink-filter minutes of a 45 minute session starting at minute 11, which a 27
    /// minute Return Day cannot deliver no matter what the user does. Scaled by the same factor the
    /// session was, rounded up, never below 1. The authored definition is never mutated: programs
    /// are shared data and the Return Day is a property of this run's day, not of the program.
    ///
    /// Event-denominated verifiers (flashes, bubbles, lock cards) are left alone - they arrive at a
    /// rate the shorter session already throttles, and scaling them twice would forgive the day.
    /// </summary>
    public static int EffectiveTarget(ProgramDay day, ProgramTask task, ProgramDayRecord? record)
    {
        var authored = Math.Max(1, task.TargetValue);

        if (record?.IsReturnDay != true) return authored;
        if (task.Kind != ProgramTaskKind.AutoVerified) return authored;
        if (task.Verifier is not { } verifier || !MinuteDenominatedVerifiers.Contains(verifier)) return authored;

        var authoredMinutes = Math.Max(1, day.SessionMinutes);
        var returnMinutes = ReturnDayMinutes(authoredMinutes);
        if (returnMinutes >= authoredMinutes) return authored;

        var scaled = (int)Math.Ceiling(authored * (returnMinutes / (double)authoredMinutes));
        return Math.Max(1, Math.Min(authored, scaled));
    }

    /// <summary>
    /// Verifiers whose TargetValue counts MINUTES of runtime rather than events. Mirrors the
    /// MinuteDenominated flags in ProgramDefinition.SessionFeatureVerifiers - the two must agree,
    /// or a day passes authoring validation and then cannot be finished (or the reverse).
    /// </summary>
    private static readonly HashSet<QuestCategory> MinuteDenominatedVerifiers = new()
    {
        QuestCategory.Video,
        QuestCategory.Spiral,
        QuestCategory.PinkFilter,
    };

    /// <summary>
    /// Complete a ritual task. Self-attested; the photo is always filed to the local diary folder and
    /// its path recorded on the day so the program can read its own days back. Ritual photos never
    /// leave the machine - they are not uploaded, not synced and never rendered onto a share card.
    ///
    /// The roadmap track is only advanced when the borrowed step is the user's live one. Filing and
    /// advancing are deliberately separate: a program day must not move a track the user did not
    /// choose to work, but the photo still has to land somewhere the program's own ledger can find it.
    /// Before that split, a ritual photo for any non-active step was dropped without even a log line -
    /// which silently broke Presentation's day-1/day-14 diptych for every user whose roadmap sat
    /// elsewhere, i.e. nearly all of them.
    /// </summary>
    public bool SubmitRitualTask(string taskId, string? photoPath = null, string? note = null)
    {
        var enrollment = State.Active;
        var day = Today;
        if (enrollment is not { State: ProgramEnrollmentState.Active } || day == null) return false;

        var task = day.Tasks.FirstOrDefault(t =>
            t.Kind == ProgramTaskKind.Ritual && string.Equals(t.Id, taskId, StringComparison.OrdinalIgnoreCase));
        if (task == null) return false;

        var record = EnsureTodayRecord();
        if (record == null || record.CompletedTaskIds.Contains(task.Id)) return false;

        if (!string.IsNullOrWhiteSpace(photoPath))
        {
            try
            {
                var roadmap = App.Roadmap;
                var stepId = task.RoadmapStepId;
                string? filed = null;

                if (roadmap != null && !string.IsNullOrWhiteSpace(stepId) && roadmap.IsStepActive(stepId))
                {
                    // The borrowed step is the user's live one, so let the roadmap own the whole
                    // submission: it copies the photo, keeps the note and advances the track. Read the
                    // saved path back rather than copying a second time - these are camera photos.
                    roadmap.SubmitPhoto(stepId!, photoPath!, note);
                    filed = roadmap.GetStepProgress(stepId!)?.PhotoPath;
                }
                else if (roadmap != null)
                {
                    // Either the task borrows no step, or it borrows one the user is not standing on.
                    // File it anyway, prefixed by program and task so program photos stay distinct in
                    // the diary folder, and leave the roadmap untouched.
                    var name = $"{enrollment.ProgramId}_{task.Id}";
                    filed = roadmap.SavePhotoToDiary(name, photoPath!);
                }

                // ??= because an enrollment saved before this field existed deserialises without it,
                // and a null dictionary here would throw on the one path that completes a day.
                if (!string.IsNullOrWhiteSpace(filed))
                    (record.RitualPhotos ??= new Dictionary<string, string>())[task.Id] = filed!;
            }
            catch (Exception ex)
            {
                // The program day must not hinge on the photo being filed.
                App.Logger?.Warning(ex, "Program ritual task '{Task}' could not file its photo", task.Id);
            }
        }

        record.CompletedTaskIds.Add(task.Id);
        MarkDirty();

        App.Logger?.Information("Program {Program} day {Day}: ritual '{Task}' submitted",
            enrollment.ProgramId, day.DayIndex, task.Id);

        CheckDayCompletion();
        RaiseTodayChanged();

        // Same reasoning as the verified-task write in TrackVerifier: a ritual is a photograph the
        // user took once. Losing it to the 30s window is not a thing that can be repeated.
        Save();
        return true;
    }

    /// <summary>
    /// A premium task the user currently cannot verify. Rather than hard-locking someone mid-run,
    /// the task stops blocking the day - they keep their progress and can finish. Which premium
    /// tasks swap to free equivalents on a lapsed pledge is still an open product decision.
    /// </summary>
    public bool IsTaskBlocked(ProgramTask task)
    {
        if (task.RequiresPremium && !HasPremium) return true;

        // A non-optional task the machine cannot possibly satisfy blocks the day invisibly: the
        // Today card shows an unticked box, the user does everything asked, and the day never
        // completes. Blocked tasks are already excluded from RequiredTasks, so naming the missing
        // capability here is what lets the day finish.
        if (task.Kind != ProgramTaskKind.AutoVerified) return false;

        return task.Verifier switch
        {
            // BlinkTrainerService.Start() refuses outright without webcam consent, so the signal
            // can never arrive. Consent is a pure settings read - deliberately NOT a device
            // enumeration, which walks DirectShow and can block the UI thread for seconds.
            QuestCategory.BlinkTrainer => !HasWebcamCapability,

            // BubbleCountService picks from the videos folder (plus active content packs). An empty
            // library means the minigame never plays a video and the counter never moves.
            QuestCategory.BubbleCount => !HasVideoLibrary,

            _ => false
        };
    }

    private bool HasWebcamCapability
    {
        get
        {
            try { return WebcamTrackingService.IsConsentCurrent(); }
            catch { return true; }   // unknown is not blocked - never forgive a day on a guess
        }
    }

    // Probed at most once a minute: IsTaskBlocked runs from RequiredTasks, which the Today card
    // calls on every repaint, and a recursive directory walk on that path would be felt.
    private bool _hasVideoLibrary = true;
    private DateTime _videoLibraryProbedAt = DateTime.MinValue;

    private bool HasVideoLibrary
    {
        get
        {
            if ((DateTime.Now - _videoLibraryProbedAt).TotalSeconds < 60) return _hasVideoLibrary;
            _videoLibraryProbedAt = DateTime.Now;

            try
            {
                var videosPath = Path.Combine(App.EffectiveAssetsPath, "videos");
                var extensions = new[] { ".mp4", ".webm", ".avi", ".mkv", ".mov", ".wmv" };

                // EnumerateFiles, not GetFiles: this stops at the first hit instead of materialising
                // a library that can run to thousands of files.
                _hasVideoLibrary =
                    (Directory.Exists(videosPath)
                     && Directory.EnumerateFiles(videosPath, "*.*", SearchOption.AllDirectories)
                         .Any(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                    || (App.ContentPacks?.GetAllActivePackVideos().Count ?? 0) > 0;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Program video-library probe failed: {Error}", ex.Message);
                _hasVideoLibrary = true;   // unknown is not blocked
            }

            return _hasVideoLibrary;
        }
    }

    /// <summary>Tasks that must be done for the day to count.</summary>
    public IEnumerable<ProgramTask> RequiredTasks(ProgramDay day) =>
        day.Tasks.Where(t => !t.Optional && !IsTaskBlocked(t));

    public bool IsTaskComplete(ProgramDayRecord record, ProgramTask task) =>
        record.CompletedTaskIds.Contains(task.Id);

    // ---------------------------------------------------------------------------------------
    // Completion, rewards, graduation
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Judge one day. <paramref name="dayIndex"/> defaults to the day the run is standing on; the
    /// session-credit path passes the day the session was prescribed for, which is not the same day
    /// once a session crosses the boundary hour.
    /// </summary>
    private void CheckDayCompletion(int dayIndex = 0)
    {
        var enrollment = State.Active;
        var program = ActiveProgram;
        if (enrollment == null || program == null) return;

        if (dayIndex <= 0) dayIndex = enrollment.CurrentDay;

        var day = program.GetDay(dayIndex);
        var record = enrollment.GetRecord(dayIndex);

        if (day == null || record == null) return;
        if (record.DayCompleted) return;
        if (!record.SessionCompleted) return;

        foreach (var task in RequiredTasks(day))
        {
            if (!record.CompletedTaskIds.Contains(task.Id)) return;
        }

        if (day.Ambient is { RequiredMinutes: > 0 } ambient && record.AmbientMinutes < ambient.RequiredMinutes)
            return;

        record.DayCompleted = true;
        record.CompletedAt = DateTime.Now;
        MarkDirty();

        AwardDayXp(day);

        App.Logger?.Information("Program {Program} day {Day} complete", program.Id, day.DayIndex);
        DayCompleted?.Invoke(this, new ProgramDayEventArgs(program, day, record));

        var chapter = program.GetChapterForDay(day.DayIndex);
        if (chapter != null && chapter.Days.All(d => enrollment.IsDayComplete(d.DayIndex)))
            CompleteChapter(program, enrollment, chapter);

        if (program.IsFinalDay(day.DayIndex))
            Graduate(program, enrollment, day, record);

        Save();
    }

    /// <summary>
    /// The ambient layer must never be the SOLE reason a day of real work rolls over as an
    /// absence (#917). It is background accrual - a filter left on through the working day - so a
    /// user who ran the session and cleared every required task can still land short of it
    /// through nothing but forgetting to leave an overlay up, and that shortfall was spending a
    /// day off and, twice over, lapsing the whole run back to day 1.
    ///
    /// The in-day contract is left alone: <see cref="CheckDayCompletion"/> still holds the day
    /// open while the ambient minutes are short, and the Today card keeps showing the live
    /// "12 / 60 min" so nothing is silently forgiven while the user can still act on it. Only at
    /// rollover - when the day is over and the shortfall is no longer fixable - is the day
    /// settled as complete, for continuity (streak, chapter, Return Day) and WITHOUT the day XP,
    /// which the ambient minutes genuinely did not earn.
    /// </summary>
    private void SettleAmbientShortfallDay(ProgramEnrollment enrollment, ProgramDefinition program, int dayIndex)
    {
        var day = program.GetDay(dayIndex);
        var record = enrollment.GetRecord(dayIndex);
        if (day == null || record == null || record.DayCompleted || !record.SessionCompleted) return;
        if (day.Ambient is not { RequiredMinutes: > 0 } ambient) return;
        if (record.AmbientMinutes >= ambient.RequiredMinutes) return;

        foreach (var task in RequiredTasks(day))
        {
            if (!record.CompletedTaskIds.Contains(task.Id)) return;
        }

        record.DayCompleted = true;
        record.CompletedAt = DateTime.Now;
        MarkDirty();

        App.Logger?.Information(
            "Program {Program} day {Day} settled at rollover: session and every task done, ambient {Have}/{Need} min - counted complete, day XP withheld",
            program.Id, dayIndex, record.AmbientMinutes, ambient.RequiredMinutes);

        // The chapter and graduation checks live in CheckDayCompletion, which this day never
        // reached; without them a settled last-day-of-chapter would strand its banked reward and
        // a settled final day would strand the run short of its graduation panel.
        var chapter = program.GetChapterForDay(dayIndex);
        if (chapter != null && chapter.Days.All(d => enrollment.IsDayComplete(d.DayIndex)))
            CompleteChapter(program, enrollment, chapter);

        if (program.IsFinalDay(dayIndex))
            Graduate(program, enrollment, day, record);

        Save();
    }

    private void AwardDayXp(ProgramDay day)
    {
        try
        {
            var xp = 200 + (int)Math.Round(400 * Math.Clamp(day.Intensity, 0, 1));
            if (day.IsBoss) xp = (int)Math.Round(xp * 1.5);
            App.Progression?.AddXP(xp, XPSource.Other);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Program day XP award failed");
        }
    }

    private void CompleteChapter(ProgramDefinition program, ProgramEnrollment enrollment, ProgramChapter chapter)
    {
        if (enrollment.CompletedChapterIds.Contains(chapter.Id)) return;

        enrollment.CompletedChapterIds.Add(chapter.Id);

        // RestartForNewAttempt clears CompletedChapterIds but deliberately keeps BankedRewards, so
        // a second attempt reaching chapter 1 lands here again for a reward the user already owns.
        // The event still fires - the UI wants to celebrate the chapter either way - but it is
        // flagged, because a subscriber that grants on the raise would hand out the same reward
        // once per attempt.
        var alreadyBanked = !string.IsNullOrWhiteSpace(chapter.RewardId)
                            && enrollment.BankedRewards.Contains(chapter.RewardId!);

        if (!string.IsNullOrWhiteSpace(chapter.RewardId) && !alreadyBanked)
            enrollment.BankedRewards.Add(chapter.RewardId!);

        MarkDirty();

        if (string.IsNullOrWhiteSpace(chapter.RewardId))
        {
            App.Logger?.Information("Program {Program} chapter '{Chapter}' complete (no reward)",
                program.Id, chapter.Id);
        }
        else
        {
            App.Logger?.Information(
                "Program {Program} chapter '{Chapter}' complete - reward '{Reward}' {Verb} ({Description})",
                program.Id, chapter.Id, chapter.RewardId,
                alreadyBanked ? "was already banked" : "banked",
                chapter.RewardDescription ?? "no description");
        }

        ChapterCompleted?.Invoke(this, new ProgramChapterEventArgs(program, chapter, alreadyBanked));
    }

    private void Graduate(ProgramDefinition program, ProgramEnrollment enrollment, ProgramDay day, ProgramDayRecord record)
    {
        enrollment.State = ProgramEnrollmentState.Graduated;
        enrollment.GraduatedAt = DateTime.Now;

        if (!State.GraduatedProgramIds.Contains(program.Id))
            State.GraduatedProgramIds.Add(program.Id);

        // Stays in Active so the UI can show the graduation panel; it moves to History on dismiss.
        // Adding it to both here would round-trip as two divergent copies of the same run.

        if (!string.IsNullOrWhiteSpace(program.GraduationBadgeId))
        {
            try { App.Achievements?.TryUnlock(program.GraduationBadgeId!); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Program graduation badge unlock failed"); }
        }

        MarkDirty();
        App.Logger?.Information("Program {Program} graduated on attempt {Attempt} ({Perfect}/{Length} perfect days)",
            program.Id, enrollment.AttemptNumber, enrollment.PerfectDayCount, program.LengthDays);

        ProgramGraduated?.Invoke(this, new ProgramDayEventArgs(program, day, record));
    }

    /// <summary>Clear a finished run so the user can browse and enroll again.</summary>
    public void DismissGraduated()
    {
        if (State.Active is { State: ProgramEnrollmentState.Graduated } graduated)
        {
            State.History.Add(graduated);
            State.Active = null;
            MarkDirty();
            Save();
            RaiseTodayChanged();
        }
    }

    // ---------------------------------------------------------------------------------------
    // The daily nudge
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The reminder the user chose an hour for at enrollment. NudgeHour was persisted from day one
    /// and read by nothing, so every run silently promised a nudge it never sent.
    ///
    /// Fires at most once per program-date (latched on the enrollment, so a restart inside the
    /// nudge hour cannot repeat it), only while the run is Active, only when the hour has actually
    /// arrived, and never once the day is already done - the point is to catch a day that is about
    /// to be lost, not to congratulate. Rides the existing one-minute clock tick rather than adding
    /// a timer, so it survives sleep and clock changes exactly like the rollover does.
    /// </summary>
    private void CheckNudge()
    {
        try
        {
            var enrollment = State.Active;
            var program = ActiveProgram;
            if (enrollment is not { State: ProgramEnrollmentState.Active } || program == null) return;
            if (enrollment.NudgeHour < 0 || enrollment.NudgeHour > 23) return;

            var now = DateTime.Now;
            if (now.Hour != enrollment.NudgeHour) return;

            var programDate = ProgramClock.ProgramDate(now, enrollment.DayBoundaryHour);
            if (enrollment.LastNudgeDate == programDate) return;

            var record = enrollment.GetRecord(enrollment.CurrentDay);
            if (record?.DayCompleted == true)
            {
                // Latch anyway: the day is done, and re-checking it every minute until midnight
                // only risks nudging if something later un-completes it.
                enrollment.LastNudgeDate = programDate;
                MarkDirty();
                return;
            }

            enrollment.LastNudgeDate = programDate;
            MarkDirty();
            Save();

            var day = Today;
            var title = day == null
                ? $"{program.Title}: today is still open."
                : $"{program.Title} - Day {day.DayIndex}: {day.Title} is still waiting.";

            App.Notifications?.Show(title, NotificationType.Info, TimeSpan.FromSeconds(12));

            App.Logger?.Information("Program {Program}: nudged for day {Day} at {Hour}:00",
                program.Id, enrollment.CurrentDay, enrollment.NudgeHour);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Program nudge failed");
        }
    }

    private void RaiseTodayChanged()
    {
        try { TodayChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Warning(ex, "ProgramService TodayChanged handler threw"); }
    }

    // ---------------------------------------------------------------------------------------
    // Persistence
    // ---------------------------------------------------------------------------------------

    public void MarkDirty() => Interlocked.Increment(ref _dirtyGeneration);

    private bool HasUnsavedChanges =>
        Interlocked.Read(ref _dirtyGeneration) != Interlocked.Read(ref _savedGeneration);

    /// <summary>
    /// Records that <paramref name="generation"/> reached disk. Never moves the mark backwards: a
    /// slow write finishing after a faster newer one must not re-open changes that are already
    /// safe, and must not close changes made after the snapshot it wrote.
    /// </summary>
    private void MarkSaved(long generation)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _savedGeneration);
            if (generation <= current) return;
            if (Interlocked.CompareExchange(ref _savedGeneration, generation, current) == current) return;
        }
    }

    private ProgramState LoadState()
    {
        if (File.Exists(_statePath))
        {
            try
            {
                var json = File.ReadAllText(_statePath);
                return JsonSerializer.Deserialize<ProgramState>(json) ?? new ProgramState();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Program state file corrupted, attempting recovery from .tmp");
            }
        }

        // Every leftover temp, newest first: writes use a unique temp name, so an interrupted save
        // can leave more than one behind.
        foreach (var candidate in LeftoverTempFiles())
        {
            try
            {
                var json = File.ReadAllText(candidate);
                var recovered = JsonSerializer.Deserialize<ProgramState>(json);
                if (recovered == null) continue;

                App.Logger?.Warning("Recovered program state from {Temp}", candidate);
                try { File.Move(candidate, _statePath, overwrite: true); } catch { }
                return recovered;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to recover program state from {Temp}", candidate);
            }
        }

        return new ProgramState();
    }

    private IEnumerable<string> LeftoverTempFiles()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<string>();

            return Directory.GetFiles(dir, Path.GetFileName(_statePath) + "*.tmp")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Serialise now, write now. For the paths that must not lose the change.</summary>
    public void Save()
    {
        long generation;
        string json;

        try
        {
            generation = Interlocked.Read(ref _dirtyGeneration);
            json = JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to serialize program state");
            return;
        }

        WriteState(json, generation);
    }

    /// <summary>Serialize on the UI thread, write off it - same split QuestService uses.</summary>
    private void SaveAsync()
    {
        long generation;
        string json;

        try
        {
            generation = Interlocked.Read(ref _dirtyGeneration);
            json = JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to serialize program state");
            return;
        }

        _ = Task.Run(() => WriteState(json, generation));
    }

    /// <summary>
    /// The single writer. Both entry points funnel through it under one lock, with a temp name
    /// unique to the write.
    ///
    /// Before this, the sync and async paths raced on a SHARED programs.json.tmp: two overlapping
    /// saves could File.Move each other's half-written temp over the real file, or fail with a
    /// sharing violation that was swallowed - and because the dirty flag had already been cleared
    /// optimistically, the lost change was never retried and Dispose had nothing to flush. The
    /// generation is only banked on a write that actually landed.
    /// </summary>
    private void WriteState(string json, long generation)
    {
        var tmpPath = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            lock (_writeLock)
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Atomic: temp then rename, so a crash mid-write cannot cost someone their run.
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _statePath, overwrite: true);
            }

            MarkSaved(generation);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            // Deliberately does NOT bank the generation: the change stays dirty, the 30s timer
            // retries, and Dispose still flushes it.
            App.Logger?.Error(ex, "Failed to save program state");
        }
    }

    public void Dispose()
    {
        _saveTimer?.Stop();
        _clockTimer?.Stop();

        foreach (var unsubscribe in _engineUnsubscribe)
        {
            try { unsubscribe(); } catch { }
        }
        _engineUnsubscribe.Clear();
        // Dropped alongside its subscriptions: an engine we no longer listen to is one we must not
        // reach into either, and shutdown is not a moment to be calling StopSession.
        _engine = null;

        if (HasUnsavedChanges) Save();
    }
}
