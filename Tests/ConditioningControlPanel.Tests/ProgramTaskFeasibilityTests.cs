using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #736/#747 — can a day's session actually deliver what its task asks for?
///
/// Nothing asked this until now, and two shipped programs were unpassable as a result. Kept Day 1
/// required a lock card from a 30-minute session that could not produce one before minute 54, and
/// Kept Day 2 required 20 minutes of pink filter from a session that offered at most 21 and usually
/// 16-18. Both tasks are non-optional, so <c>CheckDayCompletion</c> blocked the day permanently and
/// the program was walled shut at the front door. Users reported these as "doesn't track well".
///
/// The class of bug is invisible to every other test: the content is well-formed, the intensity
/// curve is right, the ids are unique, and the day simply never completes. These tests enumerate the
/// whole shipped library so a new day cannot reintroduce it.
///
/// Pure data. No App reads.
/// </summary>
public class ProgramTaskFeasibilityTests
{
    private static IReadOnlyList<ProgramDefinition> Library => BuiltInPrograms.All();

    /// <summary>
    /// Every task in every shipped program, reported together. Validate stops at the first failure,
    /// which hides the rest behind whichever day happens to be earliest.
    /// </summary>
    [Fact]
    public void EveryShippedTaskIsAchievableByItsOwnSession()
    {
        var failures = new List<string>();

        foreach (var program in Library)
        {
            foreach (var day in program.AllDays)
            {
                foreach (var task in day.Tasks)
                {
                    if (!program.IsTaskFeasible(day, task, out var why))
                        failures.Add($"{program.Id} day {day.DayIndex} '{task.Id}': {why}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Program tasks that their own session can never satisfy:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void KeptDay1LockCardIsReachable()
    {
        // The original #736 report. Day 1 is non-optional, so this being false blocks the program.
        var kept = Library.Single(p => p.Id == "kept");
        var day1 = kept.AllDays.Single(d => d.DayIndex == 1);
        var vow = day1.Tasks.Single(t => t.Verifier == QuestCategory.LockCard);

        Assert.True(kept.IsTaskFeasible(day1, vow, out var why), why);
    }

    [Fact]
    public void KeptDay2PinkFilterFitsTheSession()
    {
        // The original #747 report: the task bar sat at 18/20 because the filter only ran ~18 minutes.
        var kept = Library.Single(p => p.Id == "kept");
        var day2 = kept.AllDays.Single(d => d.DayIndex == 2);
        var filter = day2.Tasks.Single(t => t.Verifier == QuestCategory.PinkFilter);

        Assert.True(kept.IsTaskFeasible(day2, filter, out var why), why);
    }

    [Fact]
    public void ATaskWhoseFeatureIsSwitchedOff_IsRejected()
    {
        var program = OneDayProgram(lockCardsEnabled: false, QuestCategory.LockCard, targetValue: 1);

        Assert.False(program.Validate(out var error));
        Assert.Contains("switched off", error);
    }

    [Fact]
    public void ATaskWhoseFeatureIsSwitchedOff_IsAcceptedWhenMarkedOutsideSession()
    {
        // The deliberate "do this on your own time" pattern stays legal - but has to say so.
        var program = OneDayProgram(lockCardsEnabled: false, QuestCategory.LockCard, targetValue: 1);
        program.AllDays.First().Tasks[0].OutsideSession = true;

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void MinuteTargetLongerThanTheSession_IsRejected()
    {
        var program = OneDayProgram(lockCardsEnabled: true, QuestCategory.PinkFilter, targetValue: 40);
        var template = program.Templates[0];
        template.Floor.PinkFilterEnabled = true;
        template.Ceiling.PinkFilterEnabled = true;

        Assert.False(program.Validate(out var error));
        Assert.Contains("minutes of pink filter", error);
    }

    [Fact]
    public void MoreLockCardsThanTheRateCanDeliver_IsRejected()
    {
        // Firmware day 3 shipped asking for 3 cards at 1/hour inside 20 usable minutes.
        var program = OneDayProgram(lockCardsEnabled: true, QuestCategory.LockCard, targetValue: 3);
        program.Templates[0].Floor.LockCardFrequency = 1;
        program.Templates[0].Ceiling.LockCardFrequency = 1;

        Assert.False(program.Validate(out var error));
        Assert.Contains("lock cards", error);
    }

    [Fact]
    public void RitualTasksAreExempt()
    {
        // Rituals are self-attested against the roadmap, not produced by the session.
        var program = OneDayProgram(lockCardsEnabled: false, QuestCategory.LockCard, targetValue: 1);
        var task = program.AllDays.First().Tasks[0];
        task.Kind = ProgramTaskKind.Ritual;
        task.RoadmapStepId = "step-1";

        Assert.True(program.Validate(out var error), error);
    }

    private static ProgramDefinition OneDayProgram(bool lockCardsEnabled, QuestCategory verifier, int targetValue)
    {
        var floor = new SessionSettings { LockCardEnabled = lockCardsEnabled };
        var ceiling = new SessionSettings { LockCardEnabled = lockCardsEnabled };

        return new ProgramDefinition
        {
            Id = "feasibility-fixture",
            Title = "Feasibility Fixture",
            LengthDays = 1,
            Templates = new List<ProgramSessionTemplate>
            {
                new() { Id = "tpl", Name = "Template", Floor = floor, Ceiling = ceiling }
            },
            Chapters = new List<ProgramChapter>
            {
                new()
                {
                    Id = "ch1",
                    Name = "Chapter One",
                    Days = new List<ProgramDay>
                    {
                        new()
                        {
                            DayIndex = 1,
                            Title = "Day 1",
                            Blurb = "Soft and slow.",
                            SessionTemplateId = "tpl",
                            SessionMinutes = 30,
                            Intensity = 0.0,
                            Tasks = new List<ProgramTask>
                            {
                                new()
                                {
                                    Id = "t1",
                                    Kind = ProgramTaskKind.AutoVerified,
                                    Description = "Do the thing",
                                    Verifier = verifier,
                                    TargetValue = targetValue
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
