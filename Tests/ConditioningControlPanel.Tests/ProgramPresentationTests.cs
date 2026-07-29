using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// PRESENTATION, checked as authored content rather than as engine behaviour - same reasoning as
/// ProgramTakeoverTests: a fortnight-long program's structural mistakes are silent at enrollment and
/// expensive on day 11, which is precisely the class of bug no play-test will find.
///
/// This program has three properties the others do not, and all three are load-bearing promises rather
/// than nice-to-haves:
///
///   1. It is *task-led*. Seven of fourteen days are rituals, alternating with seven lighter screen
///      days, and the sessions are short and passive on purpose. If a future edit inflates the
///      sessions, the program stops being what it is sold as and starts competing with its own tasks
///      for the user's evening.
///
///   2. The ritual load is CAPPED and PACED. An earlier draft ran eleven photographed rituals in
///      fourteen days including three full-body grooming events in twelve. That is not a boredom
///      problem, it is a quit-on-day-nine problem, so the count, the spacing and the number of
///      full-body events are all asserted rather than left to review.
///
///   3. It collects photographs. Content brief 9.2 says local-only, never uploaded, never on a card,
///      deletable - stated at enrollment, not buried in settings. The copy carrying that promise is
///      tested like any other contract, and so is the fact that day 1 and day 14 ask for the same shot.
///
/// Pure data construction - no App reads, no service instances.
/// </summary>
public class ProgramPresentationTests
{
    private static ProgramDefinition Program() => BuiltInPrograms.Presentation();

    /// <summary>The four substrings AchievementService.TrackSessionComplete matches built-ins on.</summary>
    private static readonly string[] ReservedSubstrings =
    {
        "morning drift", "gamer girl", "distant doll", "good girls"
    };

    private static readonly QuestCategory[] PremiumVerifiers =
    {
        QuestCategory.Autonomy, QuestCategory.Lockdown, QuestCategory.Remote,
        QuestCategory.KeywordTrigger, QuestCategory.BlinkTrainer
    };

    private static List<ProgramTask> RitualsOf(ProgramDefinition program) => program.AllDays
        .SelectMany(d => d.Tasks)
        .Where(t => t.Kind == ProgramTaskKind.Ritual)
        .ToList();

    // -------------------------------------------------------------------------------------------
    // Structure
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void Validates()
    {
        Assert.True(Program().Validate(out var error), error);
    }

    [Fact]
    public void IsRebuiltFreshOnEveryCall()
    {
        var a = Program();
        var b = Program();

        Assert.NotSame(a, b);
        Assert.NotSame(a.Chapters[0], b.Chapters[0]);
        Assert.NotSame(a.Chapters[0].Days[0], b.Chapters[0].Days[0]);
    }

    [Fact]
    public void HeaderIsAsSpecified()
    {
        var program = Program();

        Assert.Equal("presentation", program.Id);
        Assert.Equal(BuiltInMods.SissyHypnoId, program.ModId);
        Assert.Equal(ProgramTier.Premium, program.Tier);
        Assert.Equal("#9B59B6", program.AccentColor);
        Assert.Equal(14, program.LengthDays);
        Assert.Equal(90, program.Rules.MaxDailyMinutes);
        Assert.False(string.IsNullOrWhiteSpace(program.Pitch));
        Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase));
    }

    [Fact]
    public void DaysOffScaleWithProgramLength()
    {
        // One day off per seven days of length, so a fortnight gets two.
        var program = Program();

        Assert.Equal(2, program.Rules.DaysOffAllowed);
        Assert.Equal(program.LengthDays / 7, program.Rules.DaysOffAllowed);
        Assert.True(program.Rules.StrictAvailable);
    }

    [Fact]
    public void DayCountMatchesLengthDays()
    {
        var program = Program();

        Assert.Equal(program.LengthDays, program.AllDays.Count());
        Assert.Equal(14, program.AllDays.Count());
    }

    [Fact]
    public void HasFourTemplatesAndTwoChapters()
    {
        var program = Program();

        Assert.Equal(4, program.Templates.Count);
        Assert.Equal(2, program.Chapters.Count);
    }

    [Fact]
    public void EveryChapterIsSevenContiguousDaysAndTheChaptersTileOneToFourteen()
    {
        var program = Program();
        var expected = 1;

        foreach (var chapter in program.Chapters)
        {
            Assert.Equal(7, chapter.Days.Count);

            foreach (var day in chapter.Days)
            {
                Assert.Equal(expected, day.DayIndex);
                expected++;
            }
        }

        Assert.Equal(15, expected);

        for (int day = 1; day <= 14; day++)
        {
            Assert.NotNull(program.GetChapterForDay(day));
        }
    }

    [Fact]
    public void EveryChapterHasExactlyOneBossAndItIsTheLastDay()
    {
        foreach (var chapter in Program().Chapters)
        {
            var bosses = chapter.Days.Where(d => d.IsBoss).ToList();
            Assert.Single(bosses);
            Assert.Equal(chapter.Days.Last().DayIndex, bosses[0].DayIndex);
        }
    }

    [Fact]
    public void EveryDayReferencesAnAuthoredTemplateAndEveryTemplateIsUsed()
    {
        var program = Program();

        foreach (var day in program.AllDays)
        {
            Assert.NotNull(program.GetTemplate(day.SessionTemplateId));
        }

        var used = program.AllDays.Select(d => d.SessionTemplateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var template in program.Templates)
        {
            Assert.Contains(template.Id, used);
        }
    }

    [Fact]
    public void EveryDayHasATitleABlurbAndAtLeastOneTask()
    {
        foreach (var day in Program().AllDays)
        {
            Assert.False(string.IsNullOrWhiteSpace(day.Title), $"day {day.DayIndex} has no title");
            Assert.False(string.IsNullOrWhiteSpace(day.Blurb), $"day {day.DayIndex} has no blurb");
            Assert.NotEmpty(day.Tasks);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Load: the 90-minute cap, the quantisation, and this program's own promises
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void NoDayExceedsTheNinetyMinuteCap()
    {
        var program = Program();

        foreach (var day in program.AllDays)
        {
            Assert.True(day.SessionMinutes <= program.Rules.MaxDailyMinutes,
                $"day {day.DayIndex} is {day.SessionMinutes} min, over the {program.Rules.MaxDailyMinutes} min cap");
        }
    }

    [Fact]
    public void BossDaysAlsoRespectTheCap()
    {
        foreach (var boss in Program().AllDays.Where(d => d.IsBoss))
        {
            Assert.True(boss.SessionMinutes <= 90, $"boss day {boss.DayIndex} is {boss.SessionMinutes} min");
        }
    }

    [Fact]
    public void EveryDurationIsQuantisedToThirtyFortyFiveSixtyOrSeventyFive()
    {
        var allowed = new[] { 30, 45, 60, 75 };

        foreach (var day in Program().AllDays)
        {
            Assert.Contains(day.SessionMinutes, allowed);
        }
    }

    [Fact]
    public void AnyRitualBorrowingAStepThatEmbedsItsOwnSessionSaysThatTodaysSessionCounts()
    {
        // Validate() only checks day.SessionMinutes against the cap, so minutes embedded in a ritual's
        // *objective* are invisible to it. t2_boss's objective ends "+ Complete a session (30 mins)":
        // stacked naively on day 14's 60 minutes that is 90 minutes of seated time on the same day the
        // user also has to shave, paint, dress and photograph themselves.
        //
        // The call taken here: the roadmap's session clause exists only because the roadmap has no
        // other way to require a session, and inside a program that requirement is already met by the
        // day's own session - which at 60 minutes is double what the step asks for. So there is no
        // breach and the finale keeps its hour. But the copy has to say so, or the user reads the
        // objective and sits down twice.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                var step = RoadmapStepDefinition.GetById(task.RoadmapStepId!);
                Assert.NotNull(step);

                if (!step!.Objective.Contains("session", StringComparison.OrdinalIgnoreCase)) continue;

                Assert.Contains("counts as the session", task.Description);
                Assert.Contains("do not owe a second one", task.Description);
            }
        }
    }

    [Fact]
    public void SessionsStayShortBecauseTheTaskIsTheStar()
    {
        // The pitch is seven photographs, not fourteen sessions. A day that asks for a forty-minute
        // grooming ritual AND seventy-five minutes of screen is a day most users will not finish, and
        // the half they skip is the ritual - which is the entire program. Only the finale reaches 60.
        var days = Program().AllDays.ToList();

        Assert.All(days, d => Assert.True(d.SessionMinutes <= 60,
            $"day {d.DayIndex} is {d.SessionMinutes} min - Presentation's sessions support the task, they do not replace it"));

        var finale = Assert.Single(days, d => d.SessionMinutes == 60);
        Assert.Equal(14, finale.DayIndex);
    }

    // -------------------------------------------------------------------------------------------
    // The ritual load - count, pacing, and how much body work it asks for
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void ThereAreExactlySevenRituals()
    {
        // Seven, not eleven. Eleven photographed rituals in fourteen days reads as relentless, leaves
        // the session/task inversion no room to breathe, and consumes nearly the whole roadmap step
        // library on a two-week horizon it was never authored for.
        var program = Program();

        Assert.Equal(7, RitualsOf(program).Count);
        Assert.Equal(7, program.AllDays.Count(d => d.Tasks.Any(t => t.Kind == ProgramTaskKind.Ritual)));
    }

    [Fact]
    public void RitualDaysAndLighterDaysAlternate()
    {
        // A light day between each ritual is what makes seven feel generous rather than seven feel like
        // eleven. Two rituals back to back is the failure mode.
        var program = Program();
        var ritualDays = program.AllDays
            .Where(d => d.Tasks.Any(t => t.Kind == ProgramTaskKind.Ritual))
            .Select(d => d.DayIndex)
            .ToList();

        Assert.Equal(new[] { 1, 3, 5, 7, 10, 12, 14 }, ritualDays.ToArray());

        for (int i = 1; i < ritualDays.Count; i++)
        {
            Assert.True(ritualDays[i] - ritualDays[i - 1] >= 2,
                $"days {ritualDays[i - 1]} and {ritualDays[i]} are consecutive rituals");
        }

        // ...and the seven days that are not rituals all carry a screen task instead, so a light day is
        // still a day.
        foreach (var day in program.AllDays.Where(d => !ritualDays.Contains(d.DayIndex)))
        {
            Assert.Contains(day.Tasks, t => t.Kind == ProgramTaskKind.AutoVerified);
        }
    }

    [Fact]
    public void AtMostTwoFullBodyGroomingEventsAndTheyAreAWeekApart()
    {
        // t1_step2 ("shave legs for the first time"), t1_boss ("shave all body hair") and t2_boss
        // ("Full Shave") are month-scale milestones. Three of them inside twelve days - which is what
        // the brief's table asked for - is the single most likely reason a user abandons this program.
        // Two, seven days apart, is a plausible maintenance interval.
        var program = Program();
        var fullBody = new[] { "t1_step2", "t1_boss", "t2_boss" };

        var days = program.AllDays
            .Where(d => d.Tasks.Any(t => t.Kind == ProgramTaskKind.Ritual
                                         && fullBody.Contains(t.RoadmapStepId, StringComparer.OrdinalIgnoreCase)))
            .Select(d => d.DayIndex)
            .ToList();

        Assert.Equal(new[] { 7, 14 }, days.ToArray());
        Assert.True(days[1] - days[0] >= 7);
    }

    // -------------------------------------------------------------------------------------------
    // The sawtooth
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void IntensityRisesStrictlyWithinEveryChapter()
    {
        // Unlike The Takeover, this program authors no held-breath dip - it is short enough that the one
        // chapter boundary carries the whole sawtooth.
        foreach (var chapter in Program().Chapters)
        {
            for (int i = 1; i < chapter.Days.Count; i++)
            {
                Assert.True(chapter.Days[i].Intensity > chapter.Days[i - 1].Intensity,
                    $"{chapter.Id}: day {chapter.Days[i].DayIndex} ({chapter.Days[i].Intensity}) does not rise above day {chapter.Days[i - 1].DayIndex} ({chapter.Days[i - 1].Intensity})");
            }
        }
    }

    [Fact]
    public void ChapterTwoOpensAtSeventyPercentOfChapterOnesPeak()
    {
        // The ratio, not just the direction. An earlier draft opened at .28 against a .35 peak - a 0.80x
        // dip one day wide, which on a saturating lerp is inside integer rounding. The deload was a
        // promise the numbers did not keep.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var ratio = chapters[i].Days.First().Intensity / previousPeak;

            Assert.InRange(ratio, 0.675, 0.725);
        }
    }

    [Fact]
    public void ChapterTwoTakesTwoDaysBeforeExceedingChapterOnesPeak()
    {
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var days = chapters[i].Days;

            Assert.True(days[0].Intensity <= previousPeak);
            Assert.True(days[1].Intensity <= previousPeak,
                $"day {days[1].DayIndex} exceeds the previous peak after only one day");
            Assert.True(days[2].Intensity > previousPeak,
                $"day {days[2].DayIndex} still has not exceeded the previous peak");
        }
    }

    [Fact]
    public void EveryChaptersPeakIsItsBossDay()
    {
        foreach (var chapter in Program().Chapters)
        {
            var boss = chapter.Days.Single(d => d.IsBoss);
            Assert.Equal(chapter.Days.Max(d => d.Intensity), boss.Intensity);
        }
    }

    [Fact]
    public void TheCurveStartsNearZeroAndStopsShortOfOne()
    {
        // Deliberate, and worth pinning so nobody "fixes" it: a 14-day program that ended at i1.00
        // would claim the same terminal intensity as the 28-day flagship. Presentation tops out at .70
        // because its last week is spent in front of a mirror, not in front of a mind wipe.
        var days = Program().AllDays.ToList();

        Assert.InRange(days.First().Intensity, 0.0, 0.10);
        Assert.InRange(days.Last().Intensity, 0.60, 0.80);

        foreach (var day in days)
        {
            Assert.InRange(day.Intensity, 0.0, 1.0);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Verifiers and tasks
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryAutoVerifiedTaskNamesARealQuestCategoryWithAPositiveTarget()
    {
        var real = Enum.GetValues<QuestCategory>().ToHashSet();

        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.AutoVerified))
            {
                Assert.NotNull(task.Verifier);
                Assert.Contains(task.Verifier!.Value, real);
                Assert.True(task.TargetValue > 0, $"day {day.DayIndex} task '{task.Id}' has target {task.TargetValue}");
            }
        }
    }

    [Fact]
    public void EveryRitualTaskBorrowsARealRoadmapStep()
    {
        var real = RoadmapStepDefinition.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var task in RitualsOf(Program()))
        {
            Assert.False(string.IsNullOrWhiteSpace(task.RoadmapStepId));
            Assert.Contains(task.RoadmapStepId!, real);
        }
    }

    [Fact]
    public void TheLedgerConsumesEachRoadmapStepOnceAndLaddersInTrackOrder()
    {
        // The photo ledger IS the roadmap steps, in order. A repeated step would ask for the same
        // photograph twice and produce a seven-page ledger with six distinct pages; an out-of-order
        // step would put the eyes before the face, or "The Inspection" before "The First Shedding".
        var stepIds = RitualsOf(Program()).Select(t => t.RoadmapStepId!).ToList();

        Assert.Equal(stepIds.Count, stepIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var steps = stepIds.Select(id => RoadmapStepDefinition.GetById(id)!).ToList();
        Assert.All(steps, s => Assert.NotNull(s));

        // Track 1 is entirely consumed before track 2 begins - the roadmap gates them on each other by
        // design - and step numbers only ever move forward within a track.
        for (int i = 1; i < steps.Count; i++)
        {
            var previous = steps[i - 1];
            var current = steps[i];

            if (previous.Track == current.Track)
                Assert.True(current.StepNumber > previous.StepNumber,
                    $"roadmap step '{current.Id}' comes after '{previous.Id}' but has a lower step number");
            else
                Assert.True((int)current.Track > (int)previous.Track,
                    $"roadmap track goes backwards at '{current.Id}'");
        }

        // The ladder: skin -> lips -> posture -> the one shedding -> face -> eyes -> the full look.
        Assert.Equal(
            new[] { "t1_step1", "t1_step5", "t1_step6", "t1_boss", "t2_step3", "t2_step4", "t2_boss" },
            stepIds.ToArray());
    }

    [Fact]
    public void TheDiptychAnchorsAreDayOneAndDayFourteenAndAskForTheSameShot()
    {
        var program = Program();

        var day1 = program.GetDay(1)!.Tasks.Single(t => t.Kind == ProgramTaskKind.Ritual);
        var day14 = program.GetDay(14)!.Tasks.Single(t => t.Kind == ProgramTaskKind.Ritual);

        Assert.Equal("t1_step1", day1.RoadmapStepId);
        Assert.Equal("t2_boss", day14.RoadmapStepId);

        // The copy tells the user which two photographs matter, because the payoff only lands if day
        // one's was taken as a "before".
        Assert.Contains("day-one photograph", day1.Description);
        Assert.Contains("day-fourteen photograph", day14.Description);

        // And - the part that actually makes it a diptych - both ends ask for the SAME framing, spelled
        // out identically. Two different poses compare nothing, and "take a photo" is not an
        // instruction a user will reproduce thirteen days later from memory.
        const string framing = "stand square to the mirror, feet together, arms loose at your sides, camera at chest height";
        Assert.Contains(framing, day1.Description);
        Assert.Contains(framing, day14.Description);

        // The blurbs point at each other too, so the user knows on day 1 that it will be compared and
        // knows on day 14 to go and look.
        Assert.Contains("measured against", program.GetDay(1)!.Blurb);
        Assert.Contains("day one", program.GetDay(14)!.Blurb);
    }

    [Fact]
    public void EveryRitualTaskSaysThePhotoStaysOnTheMachine()
    {
        // Content brief 9.2, asserted per task rather than once on the SafetyNote: the task card is
        // where the user is standing when they decide whether to point a camera at themselves.
        foreach (var task in RitualsOf(Program()))
        {
            Assert.Contains("stays on this machine", task.Description);
            Assert.Contains("never uploaded", task.Description);
            Assert.Contains("never on a share card", task.Description);
        }
    }

    [Fact]
    public void SafetyNoteStatesTheLocalOnlyPhotoPromiseAndTheRightToDelete()
    {
        var note = Program().SafetyNote.ToLowerInvariant();

        Assert.Contains("photograph", note);
        Assert.Contains("never uploaded", note);
        Assert.Contains("delete", note);
        Assert.Contains("withdraw", note);
    }

    [Fact]
    public void PremiumOnlyVerifiersAreMarkedRequiresPremium()
    {
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Verifier != null && PremiumVerifiers.Contains(t.Verifier!.Value)))
            {
                Assert.True(task.RequiresPremium,
                    $"day {day.DayIndex} task '{task.Id}' uses {task.Verifier} but is not marked RequiresPremium");
            }
        }
    }

    [Fact]
    public void NoDayIsMadeOfOptionalTasksOnly()
    {
        foreach (var day in Program().AllDays)
        {
            Assert.Contains(day.Tasks, t => !t.Optional);
        }
    }

    [Fact]
    public void EveryScreenTaskFeatureIsLeftOnByTheDaysOwnSession()
    {
        // SessionEngine.ApplySessionSettings writes `false` into live AppSettings and stops the service
        // for any feature the template leaves off, so a task naming a feature the day's session disables
        // is actively prevented, not merely unhelped. Unlike The Takeover this program runs no
        // one-step-ahead tutorial, so there are no exceptions: every screen task must be ON today.
        var program = Program();

        var sessionOwned = new Dictionary<QuestCategory, Func<SessionSettings, bool>>
        {
            [QuestCategory.Flash] = s => s.FlashEnabled,
            [QuestCategory.Bubbles] = s => s.BubblesEnabled,
            [QuestCategory.PinkFilter] = s => s.PinkFilterEnabled,
            [QuestCategory.LockCard] = s => s.LockCardEnabled,
            [QuestCategory.BubbleCount] = s => s.BubbleCountEnabled,
            [QuestCategory.Video] = s => s.MandatoryVideosEnabled
        };

        foreach (var day in program.AllDays)
        {
            var settings = ProgramSessionBuilder.Build(program, day).Settings;

            foreach (var task in day.Tasks.Where(t => t.Verifier != null))
            {
                if (!sessionOwned.TryGetValue(task.Verifier!.Value, out var isOn)) continue;

                Assert.True(isOn(settings),
                    $"day {day.DayIndex} asks for {task.Verifier} but its own session switches that feature off");
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Ambient
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void AmbientUnlocksInChapterTwoAndNotOnItsDeloadDay()
    {
        // Deviation from brief section 1.4 ("chapter 3 in every program"), which section 4 itself
        // overrides: a 14-day program has no chapter 3, and the unlock belongs at the halfway
        // threshold. Day 8 is the deload and gets nothing - the unlock should feel like a threshold,
        // and handing it out on the rest day spends the beat for free.
        var program = Program();

        foreach (var day in program.AllDays)
        {
            if (day.DayIndex <= 8)
                Assert.Null(day.Ambient);
            else
                Assert.NotNull(day.Ambient);
        }

        // And the day it arrives says so, including that it has an off switch.
        var d9 = program.GetDay(9)!;
        Assert.False(string.IsNullOrWhiteSpace(d9.RewardDescription));
        Assert.Contains("off switch", d9.RewardDescription!);
    }

    [Fact]
    public void EveryAmbientHasCopyAndClaimsNoVerificationItCannotDo()
    {
        // Same reasoning as The Takeover: no QuestCategory accumulates corner-GIF minutes or
        // out-of-session subliminal minutes, and borrowing an overlay category would let the session
        // silently satisfy the ambient. Flavour is honest; a fake verifier is not.
        foreach (var day in Program().AllDays.Where(d => d.Ambient != null))
        {
            var ambient = day.Ambient!;
            Assert.False(string.IsNullOrWhiteSpace(ambient.Description));
            Assert.Equal(0, ambient.RequiredMinutes);
            Assert.Null(ambient.Verifier);
        }
    }

    // -------------------------------------------------------------------------------------------
    // The vocabulary rule
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EverySubliminalPhraseIsAKeyOfTheSissyHypnoManifestPool()
    {
        var pool = BuiltInMods.SissyHypno.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases)
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryLockCardPhraseIsAKeyOfTheSissyHypnoManifestPool()
    {
        var pool = BuiltInMods.SissyHypno.LockCardPhrases!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.LockCardPhrases)
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryBouncingTextLineIsAVerbatimSissyHypnoPhrase()
    {
        var allowed = BuiltInMods.SissyHypno.Phrases!
            .SelectMany(kvp => kvp.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var line in template.Floor.BouncingTextPhrases)
            {
                Assert.Contains(line, allowed);
            }
        }
    }

    [Fact]
    public void NoBouncingTextLineCarriesASpeechBubbleStageDirection()
    {
        // Asterisks are an avatar speech-bubble convention. Drifting across the screen as bouncing text
        // they render as literal punctuation.
        foreach (var template in Program().Templates)
        {
            foreach (var line in template.Floor.BouncingTextPhrases)
            {
                Assert.DoesNotContain("*", line, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NoBambiVocabularyLeaksIntoTheSissyProgram()
    {
        // The two Sissy pools are near-clones of the Bambi ones with the Bambi-prefixed entries renamed
        // (BAMBI SLEEP -> DEEP SLEEP, BAMBI FREEZE -> FREEZE, and so on). Copy-pasting a Takeover phrase
        // list in would therefore compile, run, and be wrong in a way that only shows up on screen.
        // This is the test that catches it.
        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases.Concat(template.Floor.LockCardPhrases))
            {
                Assert.DoesNotContain("BAMBI", phrase, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var line in template.Floor.BouncingTextPhrases)
            {
                Assert.DoesNotContain("Bambi", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void EveryTemplateCarriesItsOwnPhrasePoolsSoNothingFallsThroughToTheActiveMod()
    {
        foreach (var template in Program().Templates)
        {
            if (template.Floor.SubliminalEnabled)
                Assert.NotEmpty(template.Floor.SubliminalPhrases);

            if (template.Floor.LockCardEnabled)
                Assert.NotEmpty(template.Floor.LockCardPhrases);

            if (template.Floor.BouncingTextEnabled)
                Assert.NotEmpty(template.Floor.BouncingTextPhrases);
        }
    }

    [Fact]
    public void EveryEnableFlagAgreesAcrossFloorAndCeiling()
    {
        // The builder reads booleans from Floor only. A Ceiling that disagrees is dead weight that
        // reads like intent, and the next author will assume the feature ramps in.
        foreach (var t in Program().Templates)
        {
            Assert.Equal(t.Floor.FlashEnabled, t.Ceiling.FlashEnabled);
            Assert.Equal(t.Floor.SubliminalEnabled, t.Ceiling.SubliminalEnabled);
            Assert.Equal(t.Floor.AudioWhispersEnabled, t.Ceiling.AudioWhispersEnabled);
            Assert.Equal(t.Floor.BouncingTextEnabled, t.Ceiling.BouncingTextEnabled);
            Assert.Equal(t.Floor.BubblesEnabled, t.Ceiling.BubblesEnabled);
            Assert.Equal(t.Floor.PinkFilterEnabled, t.Ceiling.PinkFilterEnabled);
            Assert.Equal(t.Floor.SpiralEnabled, t.Ceiling.SpiralEnabled);
            Assert.Equal(t.Floor.CornerGifEnabled, t.Ceiling.CornerGifEnabled);
            Assert.Equal(t.Floor.MandatoryVideosEnabled, t.Ceiling.MandatoryVideosEnabled);
            Assert.Equal(t.Floor.LockCardEnabled, t.Ceiling.LockCardEnabled);
            Assert.Equal(t.Floor.BubbleCountEnabled, t.Ceiling.BubbleCountEnabled);
            Assert.Equal(t.Floor.MindWipeEnabled, t.Ceiling.MindWipeEnabled);
            Assert.Equal(t.Floor.FlashHydra, t.Ceiling.FlashHydra);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Session name screening
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void NoAuthoredNameContainsAReservedAchievementSubstring()
    {
        var program = Program();

        var names = new List<string> { program.Title, program.Subtitle };
        names.AddRange(program.Templates.Select(t => t.Id));
        names.AddRange(program.Templates.Select(t => t.Name));
        names.AddRange(program.Chapters.Select(c => c.Name));
        names.AddRange(program.Chapters.Select(c => c.Subtitle));
        names.AddRange(program.AllDays.Select(d => d.Title));

        foreach (var name in names)
        {
            foreach (var reserved in ReservedSubstrings)
            {
                Assert.DoesNotContain(reserved, name.ToLowerInvariant(), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void NoBuiltSessionNameTripsAchievementServicesSubstringMatch()
    {
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var template = program.GetTemplate(day.SessionTemplateId)!;
            var name = ProgramSessionBuilder.BuildSessionName(program, day, template);

            Assert.False(ProgramSessionBuilder.ContainsReserved(name), $"day {day.DayIndex} builds the reserved name '{name}'");
        }
    }

    // -------------------------------------------------------------------------------------------
    // The definition actually runs
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryDayBuildsARunnableSession()
    {
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var session = ProgramSessionBuilder.Build(program, day);

            Assert.Equal(day.SessionMinutes, session.DurationMinutes);
            Assert.NotNull(session.Settings);
            Assert.False(string.IsNullOrWhiteSpace(session.Name));
            Assert.NotEmpty(session.Settings.SubliminalPhrases);
        }
    }

    [Fact]
    public void NoBuiltSessionExceedsAPercentageOfOneHundred()
    {
        // Ceilings were rescaled when the curve was retuned and the arithmetic wanted values above 100
        // on several opacity fields. Those were clamped by hand; this is the guard that they stayed
        // clamped, including after Overrides are applied.
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var s = ProgramSessionBuilder.Build(program, day).Settings;

            Assert.InRange(s.FlashOpacity, 0, 100);
            Assert.InRange(s.FlashOpacityEnd, 0, 100);
            Assert.InRange(s.SubliminalOpacity, 0, 100);
            Assert.InRange(s.BouncingTextOpacity, 0, 100);
            Assert.InRange(s.PinkFilterStartOpacity, 0, 100);
            Assert.InRange(s.PinkFilterEndOpacity, 0, 100);
            Assert.InRange(s.SpiralOpacity, 0, 100);
            Assert.InRange(s.SpiralOpacityEnd, 0, 100);
            Assert.InRange(s.CornerGifOpacity, 0, 100);
            Assert.InRange(s.AudioDuckLevel, 0, 100);
        }
    }

    [Fact]
    public void OverridesAreUsedForExactlyTwoPurposesAndNothingElse()
    {
        // Five days carry Overrides, in two groups:
        //
        //   days 2, 8, 9   - switch ON the feature the day's own task names. Mandatory, not decorative:
        //                    the engine stops the service for anything the template omits, so without
        //                    these the task counter stalls for the whole session (see
        //                    EveryScreenTaskFeatureIsLeftOnByTheDaysOwnSession, which is what caught it).
        //   days 13, 14    - add the one new thing the saturated top of the curve cannot express, since
        //                    PR-Show spans only thirteen intensity points.
        //
        // A sixth day appearing here is a signal to check which group it belongs to and say so.
        var program = Program();

        var withOverrides = program.AllDays
            .Where(d => d.Overrides is { Count: > 0 })
            .Select(d => d.DayIndex)
            .ToArray();

        Assert.Equal(new[] { 2, 8, 9, 13, 14 }, withOverrides);

        // The three task-enabling days each switch on exactly the feature their task is verified by.
        Assert.Contains("LockCardEnabled", program.GetDay(2)!.Overrides!.Keys);
        Assert.Contains("PinkFilterEnabled", program.GetDay(8)!.Overrides!.Keys);
        Assert.Contains("MandatoryVideosEnabled", program.GetDay(9)!.Overrides!.Keys);

        // Day 13 is "the sound got louder", day 14 is "everything, plus the corner GIF" - not the same
        // idea twice with a bigger number.
        var d13 = program.GetDay(13)!.Overrides!;
        var d14 = program.GetDay(14)!.Overrides!;

        Assert.Contains("MindWipeBaseMultiplier", d13.Keys);
        Assert.Contains("CornerGifEnabled", d14.Keys);
        Assert.DoesNotContain("CornerGifEnabled", d13.Keys);
    }

    [Fact]
    public void FinaleOverridesActuallyLandOnTheBuiltSessions()
    {
        // If an override key is ever misspelled, ApplyOverrides logs a warning and moves on - silently -
        // so the finale would quietly run at day 12's numbers and nobody would find out.
        var program = Program();

        var d13 = ProgramSessionBuilder.Build(program, program.GetDay(13)!);
        Assert.Equal(3, d13.Settings.MindWipeBaseMultiplier);
        Assert.Equal(55, d13.Settings.MindWipeVolume);
        Assert.Equal(4, d13.Settings.MindWipeStartMinute);

        var d14 = ProgramSessionBuilder.Build(program, program.GetDay(14)!);
        Assert.True(d14.Settings.CornerGifEnabled);
        Assert.Equal(30, d14.Settings.CornerGifOpacity);
        Assert.Equal(12, d14.Settings.SubliminalPerMin);
        Assert.Equal(4, d14.Settings.MindWipeBaseMultiplier);
        Assert.Equal(62, d14.Settings.MindWipeVolume);
    }

    [Fact]
    public void TheCornerGifIsHeldBackForTheFinale()
    {
        // No template enables it, which is what makes it available as day 14's single new thing - and it
        // has been the all-day ambient layer since day 10, so it is a payoff rather than an ambush.
        var program = Program();

        foreach (var template in program.Templates)
        {
            Assert.False(template.Floor.CornerGifEnabled, $"{template.Id} enables the corner GIF");
            Assert.False(template.Ceiling.CornerGifEnabled, $"{template.Id} enables it on its ceiling");
        }

        var daysTurningItOn = program.AllDays
            .Where(d => d.Overrides != null && d.Overrides.ContainsKey("CornerGifEnabled"))
            .Select(d => d.DayIndex)
            .ToArray();

        Assert.Equal(new[] { 14 }, daysTurningItOn);
    }

    [Fact]
    public void IntensityActuallyMovesTheNumbersWithinEveryTemplatesUsedBand()
    {
        // For every template, the lowest and highest day that use it must produce visibly different
        // sessions - or the template's band must be too narrow for that to be possible, in which case
        // every day after its first appearance has to carry an Overrides entry instead. PR-Show is the
        // one that takes the second branch, and it is the reason the branch exists.
        var program = Program();

        foreach (var template in program.Templates)
        {
            var days = program.AllDays
                .Where(d => string.Equals(d.SessionTemplateId, template.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.Intensity)
                .ToList();

            Assert.NotEmpty(days);
            if (days.Count < 2) continue;

            var low = ProgramSessionBuilder.Build(program, days.First());
            var high = ProgramSessionBuilder.Build(program, days.Last());

            var rateMoved = high.Settings.FlashPerHour - low.Settings.FlashPerHour >= 10;
            var opacityMoved = high.Settings.SubliminalOpacity - low.Settings.SubliminalOpacity >= 8;
            var coveredByOverrides = days.Skip(1).All(d => d.Overrides is { Count: > 0 });

            Assert.True((rateMoved && opacityMoved) || coveredByOverrides,
                $"{template.Id} i{days.First().Intensity}..i{days.Last().Intensity}: flash rate moves " +
                $"{high.Settings.FlashPerHour - low.Settings.FlashPerHour}/hr and subliminal opacity moves " +
                $"{high.Settings.SubliminalOpacity - low.Settings.SubliminalOpacity} points, and the days are not covered by Overrides");
        }
    }

    [Fact]
    public void ThePassiveTemplatesStayPassive()
    {
        // PR-Soft runs on day 1 (the heaviest ritual day), day 2, and day 8 (the deload). If a future
        // edit turns lock cards or mandatory videos on here, those three days stop being rest - and day
        // 1 in particular is a user who has just spent forty minutes on their skin and steeled
        // themselves to photograph the result.
        var soft = Program().GetTemplate("PR-Soft")!;

        Assert.False(soft.Floor.LockCardEnabled);
        Assert.False(soft.Floor.MandatoryVideosEnabled);
        Assert.False(soft.Floor.BubbleCountEnabled);
        Assert.False(soft.Floor.MindWipeEnabled);
        Assert.False(soft.Floor.BouncingTextEnabled);
        Assert.False(soft.Floor.SpiralEnabled);
    }
}
