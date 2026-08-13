using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A Return Day is the app forgiving an absence, and it does that by shortening the session to
/// 60% (floor 15 min). The day's tasks, however, were authored against the FULL run - so every
/// minute-denominated target kept asking for the authored number of minutes out of a session that
/// no longer contains them. Takeover day 4 is the worst case: 15 pink-filter minutes, filter
/// starting at minute 11 of a 45-minute session, which a 27-minute Return Day cannot deliver no
/// matter what the user does. The day the user is being let off is the day they cannot pass.
/// Event-denominated targets (flashes, bubbles, lock cards) must NOT be scaled - the shorter
/// session already throttles their arrival, and scaling them too would hand the day over free.
/// Pure static judgement over a day/task/record triple, so no WPF Application is required.
/// </summary>
public class ProgramReturnDayTargetTests
{
    private static ProgramDay Day(int sessionMinutes = 45) =>
        new() { DayIndex = 4, SessionMinutes = sessionMinutes };

    private static ProgramTask Task(QuestCategory verifier, int target) => new()
    {
        Id = "task",
        Kind = ProgramTaskKind.AutoVerified,
        Verifier = verifier,
        TargetValue = target
    };

    private static ProgramDayRecord Record(bool returnDay) =>
        new() { DayIndex = 4, IsReturnDay = returnDay };

    [Fact]
    public void OrdinaryDay_JudgesTheAuthoredTarget()
    {
        var target = ProgramService.EffectiveTarget(Day(), Task(QuestCategory.PinkFilter, 15), Record(returnDay: false));
        Assert.Equal(15, target);
    }

    [Fact]
    public void NullRecord_JudgesTheAuthoredTarget()
    {
        // The Today card can ask before anything has happened today.
        var target = ProgramService.EffectiveTarget(Day(), Task(QuestCategory.PinkFilter, 15), null);
        Assert.Equal(15, target);
    }

    [Fact]
    public void ReturnDay_ScalesMinuteDenominatedTargetsByTheSameFactorAsTheSession()
    {
        // 45 -> 27 minutes, so 15 pink-filter minutes -> 9. The session starts the filter at
        // minute 11, leaving 16 - which now clears the bar instead of falling six minutes short.
        var day = Day(sessionMinutes: 45);
        Assert.Equal(27, ProgramService.ReturnDayMinutes(day.SessionMinutes));

        var target = ProgramService.EffectiveTarget(day, Task(QuestCategory.PinkFilter, 15), Record(returnDay: true));
        Assert.Equal(9, target);
    }

    [Theory]
    [InlineData(QuestCategory.Video)]
    [InlineData(QuestCategory.Spiral)]
    [InlineData(QuestCategory.PinkFilter)]
    public void ReturnDay_ScalesEveryMinuteDenominatedVerifier(QuestCategory verifier)
    {
        // The set must stay in step with ProgramDefinition.SessionFeatureVerifiers' MinuteDenominated
        // flags: a verifier scaled in one place and not the other passes authoring validation and
        // then cannot be finished, or the reverse.
        var target = ProgramService.EffectiveTarget(Day(45), Task(verifier, 20), Record(returnDay: true));
        Assert.Equal(12, target);
    }

    [Theory]
    [InlineData(QuestCategory.Flash)]
    [InlineData(QuestCategory.Bubbles)]
    [InlineData(QuestCategory.LockCard)]
    [InlineData(QuestCategory.BubbleCount)]
    public void ReturnDay_LeavesEventDenominatedTargetsAlone(QuestCategory verifier)
    {
        var target = ProgramService.EffectiveTarget(Day(45), Task(verifier, 20), Record(returnDay: true));
        Assert.Equal(20, target);
    }

    [Fact]
    public void ReturnDay_RoundsUpSoTheDayIsShortenedNotGivenAway()
    {
        // 30 -> 18 minutes (0.6). A 5-minute target scales to 3.0 exactly; a 7-minute one to 4.2,
        // which must become 5, not 4 - the forgiveness is the shorter session, not a cheaper bar.
        Assert.Equal(3, ProgramService.EffectiveTarget(Day(30), Task(QuestCategory.Spiral, 5), Record(true)));
        Assert.Equal(5, ProgramService.EffectiveTarget(Day(30), Task(QuestCategory.Spiral, 7), Record(true)));
    }

    [Fact]
    public void ReturnDay_NeverScalesATargetBelowOne()
    {
        Assert.Equal(1, ProgramService.EffectiveTarget(Day(30), Task(QuestCategory.Video, 1), Record(true)));
    }

    [Fact]
    public void ReturnDay_NeverRaisesATarget()
    {
        // Short days hit the 15-minute floor, so the "shortened" session is the same length as the
        // authored one (or longer, if someone authors a 10-minute day). The bar must not go up.
        var day = Day(sessionMinutes: 15);
        Assert.Equal(15, ProgramService.ReturnDayMinutes(day.SessionMinutes));

        var target = ProgramService.EffectiveTarget(day, Task(QuestCategory.Video, 10), Record(true));
        Assert.Equal(10, target);
    }

    [Fact]
    public void ReturnDay_LeavesRitualTasksAlone()
    {
        var ritual = new ProgramTask
        {
            Id = "photo",
            Kind = ProgramTaskKind.Ritual,
            RoadmapStepId = "step",
            TargetValue = 12
        };

        Assert.Equal(12, ProgramService.EffectiveTarget(Day(45), ritual, Record(true)));
    }
}
