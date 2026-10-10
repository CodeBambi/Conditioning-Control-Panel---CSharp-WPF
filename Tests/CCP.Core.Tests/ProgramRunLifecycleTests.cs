using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// Programs 3a on a stepped clock (P08): oracle CHECKPOINT B tests 4-7 and 3a-prereq tests 3 and 6
/// (~/ccp-port/evidence/oracle/programs-checkpoint-B.md, programs-3a-canenroll.md). The service is the
/// full writing instance on a temp profile; the engine is three fake delegates, as the heads attach it.
/// </summary>
[Collection(ProgramStatics.Name)]
public sealed class ProgramRunLifecycleTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-programs-run-").FullName;
    private readonly Func<ProgramTask, bool>? _savedTask = CoreProgram.TaskAvailableProvider;
    private readonly Action<QuestCategory, int>? _savedTrack = CoreQuests.TrackProgramVerifierProvider;
    private readonly Func<bool>? _savedPremium = CoreProgram.HasPremiumProvider;
    private DateTime _now = new(2026, 5, 4, 12, 0, 0, DateTimeKind.Local);
    private bool _running;
    private string? _sessionId;

    private string StatePath => Path.Combine(_dir, "programs.json");

    public void Dispose()
    {
        CoreProgram.TaskAvailableProvider = _savedTask;
        CoreQuests.TrackProgramVerifierProvider = _savedTrack;
        CoreProgram.HasPremiumProvider = _savedPremium;
        Directory.Delete(_dir, recursive: true);
    }

    private ProgramService NewService()
    {
        var svc = new ProgramService(StatePath, readOnly: false, now: () => _now);
        svc.AttachEngine(() => _running, () => _running ? _sessionId : null, _ => _running = false);
        return svc;
    }

    private static ProgramDefinition FirstWeek(ProgramService svc) => svc.Library.First(p => p.Id == "first_week");

    /// <summary>Start today's session the way StartProgramSession does; the engine now reports it.</summary>
    private void Start(ProgramService svc)
    {
        _sessionId = svc.BuildTodaySession()!.Id;
        _running = true;
    }

    private void Complete(ProgramService svc)
    {
        _running = false;
        svc.OnEngineSessionCompleted(_sessionId);
        svc.OnEngineSessionEnded();
    }

    // Oracle B test 4.
    [Fact]
    public void EnrollStartCompleteThenRolloverCreditsTheDayAndStaysActive()
    {
        using var svc = NewService();
        var enrollment = svc.Enroll(FirstWeek(svc))!;
        Start(svc);
        Complete(svc);
        svc.TrackVerifier(QuestCategory.Bubbles, 20);   // day 1's one task
        Assert.True(enrollment.Records[1].DayCompleted);

        _now = _now.AddDays(1);
        svc.EvaluateRollover();

        Assert.Equal(ProgramEnrollmentState.Active, enrollment.State);
        Assert.Equal(2, enrollment.CurrentDay);
        Assert.True(enrollment.Records[1].SessionCompleted);
        Assert.False(enrollment.Records[1].Missed);
        Assert.Equal(1, enrollment.DaysOffRemaining);
    }

    // Oracle B test 5.
    [Fact]
    public void TwoMissedDaysWithOneDayOffLapseOnceAndOneMissIsAReturnDay()
    {
        using (var svc = NewService())
        {
            var lapses = 0;
            svc.ProgramLapsed += (_, _) => lapses++;
            var enrollment = svc.Enroll(FirstWeek(svc))!;
            _now = _now.AddDays(2);
            svc.EvaluateRollover();
            svc.EvaluateRollover();
            Assert.Equal(ProgramEnrollmentState.Lapsed, enrollment.State);
            Assert.Equal(1, lapses);
            svc.Withdraw();
        }

        using (var svc = NewService())
        {
            var enrollment = svc.Enroll(FirstWeek(svc))!;
            _now = _now.AddDays(1);
            svc.EvaluateRollover();
            Assert.Equal(ProgramEnrollmentState.Active, enrollment.State);
            Assert.Equal(0, enrollment.DaysOffRemaining);
            Assert.True(svc.TodayRecord!.IsReturnDay);
        }
    }

    // Oracle B test 6.
    [Fact]
    public void RolloverWhileTheProgramSessionRunsIsHeldUntilItEnds()
    {
        using var svc = NewService();
        var enrollment = svc.Enroll(FirstWeek(svc))!;
        Start(svc);
        _now = _now.AddDays(1);
        svc.EvaluateRollover();
        Assert.Equal(1, enrollment.CurrentDay);   // held: the day is still on screen
        Assert.False(enrollment.Records[1].Missed);

        Complete(svc);   // OnEngineSessionEnded releases the hold (CoreDispatch unseeded: in place)
        Assert.Equal(2, enrollment.CurrentDay);
        Assert.True(enrollment.Records[1].SessionCompleted);
    }

    // Oracle B test 7.
    [Fact]
    public void ACompletionOnADayAlreadyStampedMissedClearsItAndRefundsTheDayOff()
    {
        using var svc = NewService();
        var enrollment = svc.Enroll(FirstWeek(svc))!;
        _sessionId = svc.BuildTodaySession()!.Id;   // pinned to day 1; the engine never reports it running
        _now = _now.AddDays(1);
        svc.EvaluateRollover();
        Assert.True(enrollment.Records[1].Missed);
        Assert.Equal(0, enrollment.DaysOffRemaining);

        svc.OnEngineSessionCompleted(_sessionId);
        Assert.False(enrollment.Records[1].Missed);
        Assert.True(enrollment.Records[1].SessionCompleted);
        Assert.Equal(1, enrollment.DaysOffRemaining);
    }

    // 3a-prereq test 3: the head's capability table seeded, every day driven through QuestService.
    [Fact]
    public void FirstWeekGraduatesThroughTheRealQuestTrackPathWithNoLapse()
    {
        CoreProgram.TaskAvailableProvider = t => t.Verifier != QuestCategory.KeywordTrigger && t.Kind != ProgramTaskKind.Ritual;
        using var svc = NewService();
        CoreQuests.TrackProgramVerifierProvider = svc.TrackVerifier;
        using var quests = new QuestService(null, _dir);
        var lapses = 0;
        svc.ProgramLapsed += (_, _) => lapses++;
        var program = FirstWeek(svc);
        var enrollment = svc.Enroll(program)!;

        for (var day = 1; day <= program.LengthDays; day++)
        {
            Assert.Equal(day, enrollment.CurrentDay);
            Start(svc);
            Complete(svc);
            foreach (var task in program.GetDay(day)!.Tasks.Where(t => !t.Optional && t.Verifier is not null))
                Track(quests, task.Verifier!.Value, task.TargetValue);
            Assert.True(enrollment.Records[day].DayCompleted, $"day {day}");
            _now = _now.AddDays(1);
            svc.EvaluateRollover();
        }

        Assert.Equal(ProgramEnrollmentState.Graduated, enrollment.State);
        Assert.Equal(0, lapses);
        Assert.DoesNotContain(enrollment.Records.Values, r => r.Missed);
    }

    private static void Track(QuestService q, QuestCategory category, int target)
    {
        for (var i = 0; i < target; i++)
        {
            switch (category)
            {
                case QuestCategory.Bubbles: q.TrackBubblePopped(); break;
                case QuestCategory.LockCard: q.TrackLockCardCompleted(); break;
                case QuestCategory.PinkFilter: q.TrackPinkFilterMinutes(1); break;
                case QuestCategory.BubbleCount: q.TrackBubbleCountCompleted(); break;
                case QuestCategory.Video: q.TrackVideoMinutes(1); break;
                default: throw new InvalidOperationException($"first_week grew a {category} task; map it here");
            }
        }
    }

    // CHECKPOINT B: a read-only service (newer-schema file) refuses every lifecycle call; nothing moves or is written.
    [Fact]
    public void ReadOnlyRefusesEveryLifecycleCall()
    {
        using (var writer = NewService()) writer.Enroll(FirstWeek(writer));
        var bytes = File.ReadAllBytes(StatePath);
        using var svc = new ProgramService(StatePath, readOnly: true, now: () => _now);
        var enrollment = svc.ActiveEnrollment!;

        using (var empty = new ProgramService(Path.Combine(_dir, "empty.json"), readOnly: true, now: () => _now))
            Assert.Null(empty.Enroll(FirstWeek(empty)));   // nothing standing in the way but read-only
        Assert.False(svc.Pause());
        enrollment.State = ProgramEnrollmentState.Paused;
        svc.Resume();
        Assert.Equal(ProgramEnrollmentState.Paused, enrollment.State);
        enrollment.State = ProgramEnrollmentState.Active;
        Assert.False(svc.SubmitRitualTask(FirstWeek(svc).GetDay(1)!.Tasks[0].Id));
        svc.Withdraw();
        Assert.Same(enrollment, svc.ActiveEnrollment);
        enrollment.State = ProgramEnrollmentState.Lapsed;
        svc.RestartAfterLapse();
        Assert.Equal(ProgramEnrollmentState.Lapsed, enrollment.State);
        enrollment.State = ProgramEnrollmentState.Graduated;
        svc.DismissGraduated();
        Assert.Same(enrollment, svc.ActiveEnrollment);
        Assert.Empty(svc.State.History);
        svc.Save();
        Assert.Equal(bytes, File.ReadAllBytes(StatePath));
    }

    // 3a-prereq test 6.
    [Fact]
    public void ALoadedRunOfAnUnavailableProgramIsNeverLapsedAndCanStillBeWithdrawn()
    {
        CoreProgram.TaskAvailableProvider = t => t.Verifier != QuestCategory.KeywordTrigger;
        using (var probe = NewService())
        {
            var program = probe.Library.First(p => ProgramService.UnavailableTasks(p, CoreProgram.IsTaskAvailable).Count > 0);
            CoreProgram.TaskAvailableProvider = null;   // enrolled on a head that can run it (WPF)
            CoreProgram.HasPremiumProvider = () => true;
            Assert.NotNull(probe.Enroll(program));
        }
        CoreProgram.TaskAvailableProvider = t => t.Verifier != QuestCategory.KeywordTrigger;
        var written = File.ReadAllBytes(StatePath);

        _now = _now.AddDays(10);
        using var svc = NewService();   // startup repair + rollover run in the ctor
        svc.EvaluateRollover();
        svc.Save();
        Assert.Equal(ProgramEnrollmentState.Active, svc.ActiveEnrollment!.State);
        Assert.Equal(written, File.ReadAllBytes(StatePath));

        svc.Withdraw();
        Assert.Null(svc.ActiveEnrollment);
        using var reread = new ProgramService(StatePath, readOnly: true);
        Assert.Null(reread.ActiveEnrollment);
        Assert.Equal(ProgramEnrollmentState.Withdrawn, reread.State.History.Last().State);
    }
}
