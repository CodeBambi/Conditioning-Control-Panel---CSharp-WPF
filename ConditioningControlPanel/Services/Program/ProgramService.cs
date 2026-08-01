using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public ProgramChapterEventArgs(ProgramDefinition program, ProgramChapter chapter)
    {
        Program = program;
        Chapter = chapter;
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

    private bool _isDirty;

    /// <summary>Id of the session the program launched, so an unrelated session can't tick the day.</summary>
    private string? _expectedSessionId;

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
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConditioningControlPanel",
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
                if (!_isDirty) return;

                _isDirty = false;
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

    public ProgramDayRecord? TodayRecord
    {
        get
        {
            var enrollment = State.Active;
            if (enrollment == null) return null;
            return enrollment.GetOrCreateRecord(enrollment.CurrentDay, enrollment.CurrentDayDate);
        }
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
        StopProgramSessionIfRunning("withdraw");

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

        var today = ProgramClock.ProgramDate(DateTime.Now, enrollment.DayBoundaryHour);

        var result = ProgramClock.Evaluate(
            enrollment.CurrentDayDate,
            enrollment.CurrentDay,
            today,
            program.LengthDays,
            enrollment.DaysOffRemaining,
            enrollment.IsDayComplete);

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
        RaiseTodayChanged();

        App.Logger?.Information("Program {Program} rolled over to day {Day} (missed {Missed}, days off left {Left})",
            program.Id, enrollment.CurrentDay, result.MissedDays.Count, enrollment.DaysOffRemaining);
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
            var record = TodayRecord;
            if (record?.IsReturnDay == true)
            {
                session.DurationMinutes = ReturnDayMinutes(session.DurationMinutes);
                session.Name += " (Return)";
            }

            _expectedSessionId = session.Id;
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
    public bool StopProgramSessionIfRunning(string reason)
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
            engine.StopSession();
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
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "ProgramService: session completion handling failed");
        }
    }

    /// <summary>Mark today's session slot done. Only reachable from a completed program session.</summary>
    public void NotifySessionCompleted()
    {
        var enrollment = State.Active;
        if (enrollment is not { State: ProgramEnrollmentState.Active }) return;

        var record = TodayRecord;
        if (record == null || record.SessionCompleted) return;

        record.SessionCompleted = true;
        record.SessionCompletedAt = DateTime.Now;
        MarkDirty();

        App.Logger?.Information("Program {Program} day {Day}: session slot completed",
            enrollment.ProgramId, enrollment.CurrentDay);

        CheckDayCompletion();
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

        var record = TodayRecord;
        if (record == null || record.DayCompleted) return;

        var progressed = false;

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

            if (current >= Math.Max(1, task.TargetValue))
            {
                record.CompletedTaskIds.Add(task.Id);
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
    }

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

        var record = TodayRecord;
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
        return true;
    }

    /// <summary>
    /// A premium task the user currently cannot verify. Rather than hard-locking someone mid-run,
    /// the task stops blocking the day - they keep their progress and can finish. Which premium
    /// tasks swap to free equivalents on a lapsed pledge is still an open product decision.
    /// </summary>
    public bool IsTaskBlocked(ProgramTask task) => task.RequiresPremium && !HasPremium;

    /// <summary>Tasks that must be done for the day to count.</summary>
    public IEnumerable<ProgramTask> RequiredTasks(ProgramDay day) =>
        day.Tasks.Where(t => !t.Optional && !IsTaskBlocked(t));

    public bool IsTaskComplete(ProgramDayRecord record, ProgramTask task) =>
        record.CompletedTaskIds.Contains(task.Id);

    // ---------------------------------------------------------------------------------------
    // Completion, rewards, graduation
    // ---------------------------------------------------------------------------------------

    private void CheckDayCompletion()
    {
        var enrollment = State.Active;
        var program = ActiveProgram;
        var day = Today;
        var record = TodayRecord;

        if (enrollment == null || program == null || day == null || record == null) return;
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

        // Banked permanently. A later restart must never revoke this.
        if (!string.IsNullOrWhiteSpace(chapter.RewardId) && !enrollment.BankedRewards.Contains(chapter.RewardId))
            enrollment.BankedRewards.Add(chapter.RewardId);

        MarkDirty();
        App.Logger?.Information("Program {Program} chapter '{Chapter}' complete", program.Id, chapter.Id);
        ChapterCompleted?.Invoke(this, new ProgramChapterEventArgs(program, chapter));
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

    private void RaiseTodayChanged()
    {
        try { TodayChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Warning(ex, "ProgramService TodayChanged handler threw"); }
    }

    // ---------------------------------------------------------------------------------------
    // Persistence
    // ---------------------------------------------------------------------------------------

    public void MarkDirty() => _isDirty = true;

    private ProgramState LoadState()
    {
        var tmpPath = _statePath + ".tmp";

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

        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var recovered = JsonSerializer.Deserialize<ProgramState>(json);
                if (recovered != null)
                {
                    App.Logger?.Warning("Recovered program state from .tmp file");
                    try { File.Move(tmpPath, _statePath, overwrite: true); } catch { }
                    return recovered;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to recover program state from .tmp file");
            }
        }

        return new ProgramState();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true });

            // Atomic: .tmp then rename, so a crash mid-write cannot cost someone their run.
            var tmpPath = _statePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _statePath, overwrite: true);
            _isDirty = false;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to save program state");
        }
    }

    /// <summary>Serialize on the UI thread, write off it - same split QuestService uses.</summary>
    private void SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(State, new JsonSerializerOptions { WriteIndented = true });
            var path = _statePath;
            var tmpPath = path + ".tmp";

            _ = Task.Run(() =>
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(tmpPath, json);
                    File.Move(tmpPath, path, overwrite: true);
                }
                catch (Exception ex)
                {
                    App.Logger?.Error(ex, "Failed to save program state");
                }
            });
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to serialize program state");
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

        if (_isDirty) Save();
    }
}
