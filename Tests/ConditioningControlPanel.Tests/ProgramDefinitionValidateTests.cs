using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Structural validation of a program definition. Programs are JSON files that can arrive from the
/// catalogue or a community author, and a malformed one is not a cosmetic problem: a day whose
/// template id does not resolve throws mid-run when the user presses Start on day 12, a day-index
/// gap strands the clock on a day that does not exist, and a duplicate task id collides in the day
/// record's progress dictionary so two tasks share one counter. Every one of those surfaces days or
/// weeks into a run that the user has already committed to. Validate is the single gate that has to
/// catch them at import instead, which means it has to be strict about exactly these shapes - and
/// it has to keep accepting a well-formed program, or the whole feature is unreachable.
/// Pure data validation with no App reads.
/// </summary>
public class ProgramDefinitionValidateTests
{
    private static ProgramTask AutoTask(string id) => new()
    {
        Id = id,
        Kind = ProgramTaskKind.AutoVerified,
        Description = "Pop 20 bubbles",
        Verifier = QuestCategory.Bubbles,
        TargetValue = 20
    };

    private static ProgramDay Day(int index, string templateId = "tpl-induction") => new()
    {
        DayIndex = index,
        Title = $"Day {index}",
        Blurb = "Soft and slow.",
        SessionTemplateId = templateId,
        SessionMinutes = 30,
        Intensity = 0.2,
        Tasks = new List<ProgramTask> { AutoTask($"task-{index}") }
    };

    /// <summary>A minimal well-formed program: one template, one chapter, two days.</summary>
    private static ProgramDefinition ValidProgram() => new()
    {
        Id = "test-program",
        Title = "Test Program",
        LengthDays = 2,
        Templates = new List<ProgramSessionTemplate>
        {
            new() { Id = "tpl-induction", Name = "Induction", Floor = new SessionSettings(), Ceiling = new SessionSettings() }
        },
        Chapters = new List<ProgramChapter>
        {
            new()
            {
                Id = "chapter-1",
                Name = "Opening",
                Days = new List<ProgramDay> { Day(1), Day(2) }
            }
        },
        Rules = new ProgramRules { MaxDailyMinutes = 90 }
    };

    [Fact]
    public void WellFormedProgram_Validates()
    {
        var program = ValidProgram();

        Assert.True(program.Validate(out var error), error);
        Assert.Equal("", error);
    }

    [Fact]
    public void DayIndexGap_IsRejected()
    {
        var program = ValidProgram();
        program.Chapters[0].Days[1].DayIndex = 3;   // 1, 3 - day 2 does not exist

        Assert.False(program.Validate(out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void UnknownTemplateReference_IsRejected()
    {
        var program = ValidProgram();
        program.Chapters[0].Days[1].SessionTemplateId = "tpl-does-not-exist";

        Assert.False(program.Validate(out var error));
        Assert.Contains("tpl-does-not-exist", error);
    }

    [Fact]
    public void DuplicateTaskIdWithinADay_IsRejected()
    {
        var program = ValidProgram();
        program.Chapters[0].Days[0].Tasks.Add(AutoTask("task-1"));   // same id twice on day 1

        Assert.False(program.Validate(out var error));
        Assert.Contains("task-1", error);
    }

    [Fact]
    public void SameTaskIdOnDifferentDays_IsAccepted()
    {
        // Task ids are only required to be stable within a day - they key that day's progress
        // dictionary. Rejecting a repeat across days would make authoring pointlessly painful.
        var program = ValidProgram();
        program.Chapters[0].Days[0].Tasks = new List<ProgramTask> { AutoTask("daily-ritual") };
        program.Chapters[0].Days[1].Tasks = new List<ProgramTask> { AutoTask("daily-ritual") };

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void AutoVerifiedTaskWithoutVerifier_IsRejected()
    {
        // Nothing would ever tick it, so the day could never complete.
        var program = ValidProgram();
        program.Chapters[0].Days[0].Tasks[0].Verifier = null;

        Assert.False(program.Validate(out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void RitualTaskWithoutRoadmapStep_IsRejected()
    {
        // Rituals borrow a roadmap step for the objective and photo requirement; without one there
        // is no way to attest it.
        var program = ValidProgram();
        var task = program.Chapters[0].Days[0].Tasks[0];
        task.Kind = ProgramTaskKind.Ritual;
        task.Verifier = null;
        task.RoadmapStepId = null;

        Assert.False(program.Validate(out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void RitualTaskWithRoadmapStep_IsAccepted()
    {
        var program = ValidProgram();
        var task = program.Chapters[0].Days[0].Tasks[0];
        task.Kind = ProgramTaskKind.Ritual;
        task.Verifier = null;
        task.RoadmapStepId = "roadmap-mirror";

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void DayOverTheDailyMinuteCap_IsRejected()
    {
        // The cap exists so authors escalate with intensity and ambient layers, not seat time.
        var program = ValidProgram();
        program.Chapters[0].Days[1].SessionMinutes = 120;   // cap is 90

        Assert.False(program.Validate(out var error));
        Assert.Contains("90", error);
    }

    [Fact]
    public void DayAtExactlyTheCap_IsAccepted()
    {
        var program = ValidProgram();
        program.Chapters[0].Days[1].SessionMinutes = 90;

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void LengthDaysDisagreeingWithTheDayCount_IsRejected()
    {
        // LengthDays drives graduation and the "day N of M" copy; a mismatch either ends the run
        // early or leaves it unable to finish.
        var program = ValidProgram();
        program.LengthDays = 3;   // only two days authored

        Assert.False(program.Validate(out var error));
        Assert.Contains("3", error);
        Assert.Contains("2", error);
    }

    [Fact]
    public void ProgramWithNoChapters_IsRejected()
    {
        var program = ValidProgram();
        program.Chapters.Clear();
        program.LengthDays = 0;

        Assert.False(program.Validate(out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void MissingIdOrTitle_IsRejected()
    {
        var noId = ValidProgram();
        noId.Id = "";
        Assert.False(noId.Validate(out var idError));
        Assert.NotEqual("", idError);

        var noTitle = ValidProgram();
        noTitle.Title = "   ";
        Assert.False(noTitle.Validate(out var titleError));
        Assert.NotEqual("", titleError);
    }

    [Fact]
    public void IntensityOutsideZeroToOne_IsRejected()
    {
        var program = ValidProgram();
        program.Chapters[0].Days[1].Intensity = 1.4;

        Assert.False(program.Validate(out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void DayLookupsSpanChapters()
    {
        // AllDays / GetDay / GetChapterForDay are what the clock's day index resolves through, so a
        // two-chapter program must still see a flat, ordered 1..N.
        var program = ValidProgram();
        program.Chapters.Add(new ProgramChapter
        {
            Id = "chapter-2",
            Name = "Deeper",
            Days = new List<ProgramDay> { Day(3), Day(4) }
        });
        program.LengthDays = 4;

        Assert.True(program.Validate(out var error), error);
        Assert.Equal(new[] { 1, 2, 3, 4 }, program.AllDays.Select(d => d.DayIndex).ToArray());
        Assert.Equal("chapter-2", program.GetChapterForDay(3)?.Id);
        Assert.Equal(4, program.GetDay(4)?.DayIndex);
        Assert.Null(program.GetDay(5));
        Assert.True(program.IsFinalDay(4));
        Assert.False(program.IsFinalDay(3));
    }
}
