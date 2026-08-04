using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// KEPT, checked as authored content rather than as engine behaviour.
///
/// Nobody is going to sit through four weeks to discover that day 19's intensity went backwards, that
/// chapter 3 forgot to deload, or that day 21 references a template id somebody renamed. Those are
/// silent at enrollment and expensive on day 21, which is the shape of bug a content test is for.
///
/// Two tests here are about honesty rather than structure, and they are the ones worth keeping:
///   - <see cref="DoesNotClaimTheCounterTheEngineDoesNotHave"/> guards the gap. Kept's brief calls for
///     a second, *declared* "Days Kept" counter beside the verified program day, and that counter does
///     not exist yet. The 28 days are authored to stand up without it; this test stops a later copy
///     pass from quietly promising it back.
///   - <see cref="SafetyNoteIsPlainAndOutOfCharacter"/> guards content brief safety note 1. Kept is
///     the one program in the set that reaches off the screen and into someone's body, and its plain
///     non-fiction line is not decorative.
///
/// Pure data construction - no App reads, no service instances.
/// </summary>
public class ProgramKeptTests
{
    private static ProgramDefinition Program() => BuiltInPrograms.Kept();

    /// <summary>The four substrings AchievementService.TrackSessionComplete matches built-ins on.</summary>
    private static readonly string[] ReservedSubstrings =
    {
        "morning drift", "gamer girl", "distant doll", "good girls"
    };

    private static readonly int[] AllowedDurations = { 30, 45, 60, 75 };

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

        Assert.Equal("kept", program.Id);
        Assert.Equal(BuiltInMods.LockedId, program.ModId);
        Assert.Equal(ProgramTier.Premium, program.Tier);
        Assert.Equal("#E81CA8", program.AccentColor);
        Assert.Equal(28, program.LengthDays);
        Assert.Equal(90, program.Rules.MaxDailyMinutes);
        Assert.False(string.IsNullOrWhiteSpace(program.Pitch));
        Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase));

        // One day off per 7 days of length. A single allowance across 28 days is four times stricter
        // than the same allowance across 7, and losing 24 days of a paid program to a second bad week
        // is the most likely refund in the set.
        Assert.Equal(4, program.Rules.DaysOffAllowed);
        Assert.Equal(program.LengthDays / 7, program.Rules.DaysOffAllowed);
    }

    [Fact]
    public void DayCountMatchesLengthDays()
    {
        var program = Program();
        Assert.Equal(program.LengthDays, program.AllDays.Count());
    }

    [Fact]
    public void HasExactlyFourTemplatesAndEveryDayUsesOne()
    {
        var program = Program();

        Assert.Equal(4, program.Templates.Count);

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
    public void ChapterMembershipIsContiguousAndComplete()
    {
        var program = Program();

        // Four chapters of seven, in order, with no gap and no overlap. GetChapterForDay takes the
        // first chapter containing the index, so an overlap would silently bind a day to the wrong
        // chapter's accent and banked reward.
        Assert.Equal(4, program.Chapters.Count);

        var seen = new List<int>();
        foreach (var chapter in program.Chapters)
        {
            var indices = chapter.Days.Select(d => d.DayIndex).ToList();
            Assert.Equal(7, indices.Count);

            for (int i = 1; i < indices.Count; i++)
            {
                Assert.Equal(indices[i - 1] + 1, indices[i]);
            }

            seen.AddRange(indices);
        }

        Assert.Equal(Enumerable.Range(1, program.LengthDays), seen);

        foreach (var day in program.AllDays)
        {
            Assert.NotNull(program.GetChapterForDay(day.DayIndex));
        }
    }

    [Fact]
    public void EveryChapterBanksARewardThatSurvivesARestart()
    {
        // One day off, and a second absence restarts at day 1 - but banked chapter rewards survive.
        // A chapter with no RewardId has nothing to record on the enrollment, so a restart silently
        // takes back four weeks of work.
        foreach (var chapter in Program().Chapters)
        {
            Assert.False(string.IsNullOrWhiteSpace(chapter.RewardId));
            Assert.False(string.IsNullOrWhiteSpace(chapter.RewardDescription));
        }
    }

    [Fact]
    public void EveryDayCarriesATitleABlurbAndAtLeastOneTask()
    {
        foreach (var day in Program().AllDays)
        {
            Assert.False(string.IsNullOrWhiteSpace(day.Title), $"day {day.DayIndex} has no title");
            Assert.False(string.IsNullOrWhiteSpace(day.Blurb), $"day {day.DayIndex} has no blurb");
            Assert.NotEmpty(day.Tasks);
        }
    }

    // -------------------------------------------------------------------------------------------
    // The curve
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The whole authored curve, pinned. Written out rather than derived so a reviewer can read the
    /// shape at a glance and so any edit to it is a deliberate, visible diff rather than a drifting
    /// number. Day 27's dip is in here on purpose - see <see cref="Day27IsAnAuthoredDipNotAMistake"/>.
    /// </summary>
    private static readonly double[] ExpectedCurve =
    {
        /* ch1 d1-7   */ 0.05, 0.11, 0.16, 0.21, 0.25, 0.28, 0.30,
        /* ch2 d8-14  */ 0.21, 0.26, 0.33, 0.40, 0.46, 0.51, 0.55,
        /* ch3 d15-21 */ 0.38, 0.46, 0.56, 0.66, 0.72, 0.76, 0.80,
        /* ch4 d22-28 */ 0.56, 0.66, 0.82, 0.88, 0.93, 0.74, 1.00
    };

    [Fact]
    public void TheCurveIsExactlyAsAuthored()
    {
        var actual = Program().AllDays.Select(d => d.Intensity).ToArray();
        Assert.Equal(ExpectedCurve, actual);
    }

    [Fact]
    public void IntensityRisesWithinEveryChapterExceptTheOneAuthoredDip()
    {
        // Day 27 is a deliberate held breath before the finale (B3) - the only backwards step inside a
        // chapter in the program. Everything else must climb, and the exception is named here rather
        // than loosened into a tolerance, so a *second* dip appearing anywhere still fails.
        const int authoredDipDay = 27;

        foreach (var chapter in Program().Chapters)
        {
            var days = chapter.Days.OrderBy(d => d.DayIndex).ToList();
            for (int i = 1; i < days.Count; i++)
            {
                if (days[i].DayIndex == authoredDipDay)
                {
                    Assert.True(days[i].Intensity < days[i - 1].Intensity,
                        $"day {authoredDipDay} is supposed to be the held breath but does not drop");
                    continue;
                }

                Assert.True(days[i].Intensity > days[i - 1].Intensity,
                    $"day {days[i].DayIndex} intensity {days[i].Intensity} does not exceed day {days[i - 1].DayIndex}'s {days[i - 1].Intensity}");
            }
        }
    }

    [Fact]
    public void EveryChapterOpensAtSeventyPercentOfThePreviousPeak()
    {
        // The house rule, asserted as a *ratio* rather than a direction. The first draft satisfied
        // "opens below the previous peak" with dips of 0.73x / 0.82x / 0.875x, which meant that by day
        // 22 - peak accumulated fatigue - the mercy Circe promises out loud was a 12% dip near the top
        // of a saturating lerp, i.e. inside integer rounding and indistinguishable from day 21.
        // Direction alone is not a strong enough assertion to catch that, which is why this one is
        // numeric. Tolerance is 0.02 absolute on the ratio to allow the two-decimal rounding the
        // authored curve uses (0.70 x .55 = .385, authored as .38 = 0.691x).
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var opening = chapters[i].Days.OrderBy(d => d.DayIndex).First().Intensity;

            var ratio = opening / previousPeak;

            Assert.InRange(ratio, 0.68, 0.72);
        }
    }

    [Fact]
    public void EveryChapterTakesTwoDaysToExceedThePreviousPeak()
    {
        // The other half of the rule. §1.1's justification for the sawtooth is that *chapter openings*
        // feel like relief - a one-day dip is a blip, not an opening. So the first two days of each
        // chapter sit below the previous peak and the third is the first to clear it.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var days = chapters[i].Days.OrderBy(d => d.DayIndex).ToList();

            Assert.True(days[0].Intensity < previousPeak,
                $"day {days[0].DayIndex} should open below the previous peak of {previousPeak}");
            Assert.True(days[1].Intensity < previousPeak,
                $"day {days[1].DayIndex} should still be below the previous peak of {previousPeak} - the dip is two days wide");
            Assert.True(days[2].Intensity > previousPeak,
                $"day {days[2].DayIndex} should be the first to clear the previous peak of {previousPeak}");
        }
    }

    [Fact]
    public void EveryChapterExceedsThePreviousChaptersPeak()
    {
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var peak = chapters[i].Days.Max(d => d.Intensity);

            Assert.True(peak > previousPeak,
                $"chapter {i + 1} peaks at {peak}, which does not exceed chapter {i}'s peak of {previousPeak}");
        }
    }

    [Fact]
    public void Day27IsAnAuthoredDipNotAMistake()
    {
        // B3: the last four days were four copies of one session, and intensity structurally cannot
        // fix that (booleans come from Floor, and near i=1.0 every number is saturated). So days 25-28
        // each add exactly one thing via Overrides, and day 27 pulls the curve down so day 28 has
        // somewhere to land from. This test exists so the dip reads as intent to the next person.
        var program = Program();

        var d26 = program.GetDay(26)!;
        var d27 = program.GetDay(27)!;
        var d28 = program.GetDay(28)!;

        Assert.True(d27.Intensity < d26.Intensity);
        Assert.True(d28.Intensity > d26.Intensity);

        // And the held breath has to be perceptibly lighter, not just numerically: shorter, on the
        // gentlest template in the program, and with nothing counted.
        Assert.Equal(45, d27.SessionMinutes);
        Assert.Equal("KP-Offer", d27.SessionTemplateId);
        Assert.True(d27.SessionMinutes < d26.SessionMinutes);
        Assert.Single(d27.Tasks);
    }

    [Fact]
    public void TheFinalFourDaysEachAddSomethingTheCurveCannot()
    {
        // B3, asserted structurally: days 25, 26 and 28 each carry an Overrides entry, because a new
        // *feature* or a value above the template ceiling is the only thing that can distinguish days
        // at the top of the curve. Day 27 is exempt - its distinguishing feature is that it takes
        // things away.
        var program = Program();

        foreach (var dayIndex in new[] { 25, 26, 28 })
        {
            var day = program.GetDay(dayIndex);
            Assert.NotNull(day);
            Assert.NotNull(day!.Overrides);
            Assert.NotEmpty(day.Overrides!);
        }

        // Each of the three adds something genuinely different, so no two of them are the same day.
        var d25 = program.GetDay(25)!.Overrides!.Keys.ToHashSet();
        var d26 = program.GetDay(26)!.Overrides!.Keys.ToHashSet();
        var d28 = program.GetDay(28)!.Overrides!.Keys.ToHashSet();

        Assert.Empty(d25.Intersect(d26));
        Assert.Empty(d26.Intersect(d28));
        Assert.Empty(d25.Intersect(d28));
    }

    [Fact]
    public void BossDaysAreTheChapterPeak()
    {
        foreach (var chapter in Program().Chapters)
        {
            var boss = chapter.Days.SingleOrDefault(d => d.IsBoss);
            Assert.NotNull(boss);
            Assert.Equal(chapter.Days.Max(d => d.Intensity), boss!.Intensity);
        }
    }

    [Fact]
    public void FinalDayIsTheProgramPeakAtFullIntensity()
    {
        var program = Program();
        var final = program.GetDay(program.LengthDays);

        Assert.NotNull(final);
        Assert.True(final!.IsBoss);
        Assert.Equal(1.0, final.Intensity);
        Assert.Equal(program.AllDays.Max(d => d.Intensity), final.Intensity);
    }

    // -------------------------------------------------------------------------------------------
    // The 90-minute cap
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void NoDayExceedsTheDailyCap()
    {
        var program = Program();

        foreach (var day in program.AllDays)
        {
            Assert.True(day.SessionMinutes <= program.Rules.MaxDailyMinutes,
                $"day {day.DayIndex} asks for {day.SessionMinutes} minutes against a {program.Rules.MaxDailyMinutes} minute cap");
        }
    }

    [Fact]
    public void EveryDurationIsQuantised()
    {
        foreach (var day in Program().AllDays)
        {
            Assert.Contains(day.SessionMinutes, AllowedDurations);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Tasks
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryTaskVerifierIsARealQuestCategory()
    {
        var known = Enum.GetValues<QuestCategory>().ToHashSet();

        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks)
            {
                if (task.Kind != ProgramTaskKind.AutoVerified) continue;

                Assert.NotNull(task.Verifier);
                Assert.Contains(task.Verifier!.Value, known);
                Assert.True(task.TargetValue > 0,
                    $"day {day.DayIndex} task '{task.Id}' has a non-positive target");
            }
        }
    }

    [Fact]
    public void RitualTasksNameARealRoadmapStep()
    {
        var known = RoadmapStepDefinition.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        var rituals = Program().AllDays
            .SelectMany(d => d.Tasks.Select(t => (d.DayIndex, Task: t)))
            .Where(x => x.Task.Kind == ProgramTaskKind.Ritual)
            .ToList();

        Assert.NotEmpty(rituals);

        foreach (var (dayIndex, task) in rituals)
        {
            Assert.False(string.IsNullOrWhiteSpace(task.RoadmapStepId),
                $"day {dayIndex} ritual '{task.Id}' names no roadmap step");
            Assert.Contains(task.RoadmapStepId!, known);
        }
    }

    [Fact]
    public void RitualTasksSayPhotosNeverLeaveTheMachine()
    {
        // Every roadmap step carries a photo requirement, so every ritual here is implicitly a photo
        // task. Content brief 1.3: ritual photos are local-only, never uploaded, never synced, never
        // on a share card - and that has to be said in the copy the user reads on the day, not buried
        // in settings. For this audience it is the difference between a feature and a liability.
        var rituals = Program().AllDays
            .SelectMany(d => d.Tasks.Select(t => (d.DayIndex, Task: t)))
            .Where(x => x.Task.Kind == ProgramTaskKind.Ritual)
            .ToList();

        Assert.NotEmpty(rituals);

        foreach (var (dayIndex, task) in rituals)
        {
            var copy = task.Description.ToLowerInvariant();

            Assert.Contains("stays on this machine", copy);
            Assert.Contains("never uploaded", copy);
            Assert.Contains("never synced", copy);
        }
    }

    [Fact]
    public void PremiumOnlyVerifiersAreFlaggedPremium()
    {
        // ProgramService gates on RequiresPremium, so a day that forgets the flag hands a free user a
        // task they cannot start. One-directional on purpose: flagging a free verifier premium is a
        // content choice, forgetting to flag a premium one is a bug.
        var premiumCategories = new[]
        {
            QuestCategory.Autonomy, QuestCategory.Lockdown, QuestCategory.Remote,
            QuestCategory.KeywordTrigger, QuestCategory.BlinkTrainer
        };

        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Verifier.HasValue))
            {
                if (premiumCategories.Contains(task.Verifier!.Value))
                {
                    Assert.True(task.RequiresPremium,
                        $"day {day.DayIndex} task '{task.Id}' uses a premium verifier without RequiresPremium");
                }
            }
        }
    }

    [Fact]
    public void NoDayRequiresASecondPerson()
    {
        // Kept has no remote-command day, and it should stay that way: a remote task needs a second
        // person, and this is the one program whose fiction is explicitly one-to-one with Circe.
        foreach (var day in Program().AllDays)
        {
            Assert.DoesNotContain(day.Tasks, t => t.Verifier == QuestCategory.Remote && !t.Optional);
        }
    }

    [Fact]
    public void TheVowOpensAndClosesTheProgram()
    {
        // Day 1's task is the vow the enrollment ceremony also asks for, and day 28 is the same vow
        // completed. That pairing is the spine of the run - it is what carries the shape of the
        // program while the declared counter does not exist - so it is asserted rather than assumed.
        var program = Program();

        var first = program.GetDay(1);
        var last = program.GetDay(28);

        Assert.NotNull(first);
        Assert.NotNull(last);

        Assert.Equal("CIRCE HOLDS MY KEY.", program.ContractPhrase);
        Assert.Contains("CIRCE HOLDS MY KEY.", first!.Tasks.Single().Description);
        Assert.Contains("I EXIST TO BE HERS.", last!.Tasks.Single().Description);

        // Both are lock cards, so both are typed back by the user rather than merely displayed.
        Assert.Equal(QuestCategory.LockCard, first.Tasks.Single().Verifier);
        Assert.Equal(QuestCategory.LockCard, last.Tasks.Single().Verifier);
    }

    // -------------------------------------------------------------------------------------------
    // Ambient - the chapter 3 threshold
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void AmbientUnlocksAtChapterThree()
    {
        // Cross-program rule: ambient layers unlock at chapter 3, and it should read as a threshold
        // rather than a setting. Days 1-14 carry no all-day layer; days 15-28 all do.
        var program = Program();

        foreach (var day in program.AllDays.Where(d => d.DayIndex <= 14))
        {
            Assert.Null(day.Ambient);
        }

        foreach (var day in program.AllDays.Where(d => d.DayIndex >= 15))
        {
            Assert.NotNull(day.Ambient);
            Assert.False(string.IsNullOrWhiteSpace(day.Ambient!.Description));
        }

        // And the threshold is the first day of chapter 3, not somewhere inside it.
        Assert.Equal(15, program.Chapters[2].Days.Min(d => d.DayIndex));
    }

    [Fact]
    public void AmbientVerifiersAreRealAndMatchTheirMinutes()
    {
        var known = Enum.GetValues<QuestCategory>().ToHashSet();

        foreach (var day in Program().AllDays.Where(d => d.Ambient != null))
        {
            var ambient = day.Ambient!;

            if (ambient.Verifier.HasValue)
            {
                Assert.Contains(ambient.Verifier.Value, known);
            }

            // Minutes without a verifier is a requirement nothing can ever satisfy.
            if (ambient.RequiredMinutes > 0)
            {
                Assert.NotNull(ambient.Verifier);
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Session naming
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void NoAuthoredNameTripsAnAchievementSubstring()
    {
        // Worth checking carefully here rather than assuming: Circe's vocabulary is full of "GOOD
        // BOY" and "GOOD BOYS DON'T DECIDE.", one letter away from the reserved "good girls".
        var program = Program();

        var candidates = new List<string> { program.Title, program.Subtitle };
        candidates.AddRange(program.Templates.Select(t => t.Name));
        candidates.AddRange(program.Templates.Select(t => t.Id));
        candidates.AddRange(program.Chapters.Select(c => c.Name));
        candidates.AddRange(program.AllDays.Select(d => d.Title));

        foreach (var candidate in candidates)
        {
            Assert.False(ProgramSessionBuilder.ContainsReserved(candidate),
                $"'{candidate}' contains a substring AchievementService matches built-in sessions on");
        }
    }

    [Fact]
    public void EveryDayBuildsARunnableSessionWithACleanName()
    {
        // End to end on today's engine: all 28 days resolve their template, lerp, and produce a
        // session whose name cannot falsely unlock somebody else's achievement.
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var session = ProgramSessionBuilder.Build(program, day);

            Assert.False(string.IsNullOrWhiteSpace(session.Name));
            Assert.False(ProgramSessionBuilder.ContainsReserved(session.Name), session.Name);
            Assert.Equal(day.SessionMinutes, session.DurationMinutes);
            Assert.NotNull(session.Settings);

            foreach (var reserved in ReservedSubstrings)
            {
                Assert.DoesNotContain(reserved, session.Name.ToLowerInvariant());
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Honesty
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void SafetyNoteIsPlainAndOutOfCharacter()
    {
        // Content brief safety note 1. Kept states a 28-day denial vow, which is the one thing in
        // this set that reaches off the screen, so the enrollment ceremony needs a plain non-fiction
        // line: it is a game, you can end it any moment, Withdraw is everywhere, and no counter is
        // worth ignoring your body over. Exactly once, and not in Circe's voice.
        var note = Program().SafetyNote;

        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.Contains("Withdraw", note);

        var lower = note.ToLowerInvariant();
        Assert.Contains("game", lower);
        Assert.Contains("body", lower);

        // Out of character means out of character: none of her pet names belong in this string.
        foreach (var petName in new[] { "pet", "good boy", "sweet thing" })
        {
            Assert.False(lower.Contains(petName, StringComparison.Ordinal),
                $"the safety note slipped into Circe's voice with '{petName}'");
        }
    }

    [Fact]
    public void DoesNotClaimTheCounterTheEngineDoesNotHave()
    {
        // The gap guard. Kept's brief calls for a second, *declared* counter ("Days Kept") beside the
        // verified program day, with a daily one-tap prompt and a confession that resets it. None of
        // that exists yet, and the 28 days are deliberately authored to stand up without it.
        //
        // The failure mode this catches is a well-meaning copy pass writing "Day 9 - Kept 9" or "she
        // asks if you are still locked" into a blurb, which would promise a mechanic that will not
        // fire and make the program look broken rather than unfinished. Scoped to authored prose, not
        // the phrase pools - "STAY LOCKED" and "LOCKED IS HOME" are manifest subliminals and say
        // nothing about a counter.
        var forbidden = new[]
        {
            "days kept", "confession", "confess", "still locked for me", "one tap", "tap yes"
        };

        foreach (var (label, text) in AuthoredProse(Program()))
        {
            var lower = text.ToLowerInvariant();

            foreach (var claim in forbidden)
            {
                Assert.False(lower.Contains(claim, StringComparison.Ordinal),
                    $"{label} promises the unbuilt declared counter with '{claim}': \"{text}\"");
            }
        }
    }

    [Fact]
    public void DoesNotPromiseTheVerdictSceneThatDoesNotExistYet()
    {
        // Same reasoning for the ending. Day 28's reward copy is allowed to say the decision is hers
        // - that is the fiction, and it is true - but nothing may promise a release scene, a rolled
        // outcome or a 7-day extension, because none of those are implemented.
        var forbidden = new[] { "release chance", "extension", "her extension", "rolls", "coin" };

        foreach (var (label, text) in AuthoredProse(Program()))
        {
            var lower = text.ToLowerInvariant();

            foreach (var claim in forbidden)
            {
                Assert.False(lower.Contains(claim, StringComparison.Ordinal),
                    $"{label} promises the unbuilt Verdict branch with '{claim}': \"{text}\"");
            }
        }
    }

    [Fact]
    public void CirceGivesHerReadFromDay15AndNeverQuotesANumber()
    {
        // B4: the midgame had no structural beat and the ending's weighting was invisible for 27 of 28
        // days. Fixed with copy rather than engine work - from day 15 Circe says how she thinks you are
        // doing, in her own voice, in the day blurb.
        //
        // The hard rule is the second half of this test. She never quotes a number, a fraction or a
        // percentage: partly because she is possessive and unhurried and would not read you odds, and
        // partly because any figure would be a *lie* - the weighting it would come from does not exist
        // yet. This assertion is what stops a later pass helpfully turning her opinion into a progress
        // readout.
        var program = Program();
        var opinionDays = new[] { 15, 16, 17, 19, 21, 23 };

        foreach (var dayIndex in opinionDays)
        {
            var day = program.GetDay(dayIndex);
            Assert.NotNull(day);

            // Her read is quoted speech, which is how it is visually distinct from the narrator's
            // description of the day.
            Assert.Contains('"', day!.Blurb);

            var quoted = day.Blurb.Substring(day.Blurb.IndexOf('"'));

            Assert.DoesNotContain('%', quoted);
            Assert.False(quoted.Any(char.IsDigit),
                $"day {dayIndex} has Circe quoting a number: \"{quoted}\"");
        }

        // And it starts at the chapter-3 threshold, not before - days 1-14 have no opinion line,
        // because the stakes are not visible until the program moves into the day.
        foreach (var day in program.AllDays.Where(d => d.DayIndex <= 14))
        {
            Assert.DoesNotContain('"', day.Blurb);
        }
    }

    [Fact]
    public void TheThirdTapCopyExistsAndBanksRatherThanResets()
    {
        // B10: the daily vow prompt needs a third answer beside "still locked" and the confession -
        // "I'm done with this vow" - which ends the denial track, keeps the program running, and banks
        // the counter instead of resetting it. The mechanic is engine work on the gap list, but the
        // copy is authored now so it does not get written under deadline.
        //
        // What this asserts is the *tone contract*, because that is the part that is easy to get wrong:
        // she must not punish, must not bargain, and must not treat the banked days as a loss.
        var copy = BuiltInPrograms.KeptVowRetiredResponse();

        Assert.False(string.IsNullOrWhiteSpace(copy));

        var lower = copy.ToLowerInvariant();

        // She keeps the days rather than taking them back.
        Assert.Contains("keep the days", lower);
        Assert.Contains("not taking them back", lower);

        // She does not ask again, and the rest of the program is unaffected.
        Assert.Contains("won't ask you about it again", lower);

        // No bargaining and no guilt-trip vocabulary. This is the list a well-meaning rewrite reaches
        // for, and every one of them would turn a safety valve back into a pressure point.
        foreach (var forbidden in new[]
                 {
                     "disappointed", "shame", "failure", "failed", "weak", "gave up", "quit",
                     "are you sure", "one more", "try again", "instead of"
                 })
        {
            Assert.False(lower.Contains(forbidden, StringComparison.Ordinal),
                $"the vow-retired response reads as punishment or bargaining via '{forbidden}'");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Load - session minutes plus minutes embedded in a ritual objective
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void SessionPlusRitualMinutesStaysUnderTheDailyCap()
    {
        // Validate() only checks day.SessionMinutes against the cap, so a 60-minute session carrying a
        // ritual whose objective embeds "run a haptic ramp (10 mins)" is 70 minutes of seat time that
        // passes validation silently. The review found this stacking breaching 90 minutes elsewhere in
        // the feature (a 75-minute session plus t2_boss, which contains its own 30-minute session), so
        // Kept's three rituals are checked here against the combined figure.
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var embedded = 0;

            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                // Both the authored task copy and the roadmap step's own objective can state a
                // duration, and the real load is the larger of the two - the step is the authority on
                // what the ritual actually is.
                embedded = Math.Max(embedded, LargestStatedMinutes(task.Description));

                var step = RoadmapStepDefinition.GetById(task.RoadmapStepId ?? "");
                if (step != null)
                {
                    embedded = Math.Max(embedded, LargestStatedMinutes(step.Objective));
                }
            }

            var combined = day.SessionMinutes + embedded;

            Assert.True(combined <= program.Rules.MaxDailyMinutes,
                $"day {day.DayIndex} is {day.SessionMinutes}m of session plus {embedded}m embedded in its ritual = {combined}m, over the {program.Rules.MaxDailyMinutes}m cap");
        }
    }

    [Fact]
    public void RitualDurationsAreWrittenAsDigitsSoTheLoadCheckCanSeeThem()
    {
        // The combined-load test above parses "N min" out of the copy. That makes the copy an *input*
        // to a safety check, and a spelled-out duration ("Twenty minutes with two forms of...") is
        // invisible to it - the check would pass while silently under-counting twenty minutes. Caught
        // exactly that on day 14 during the load audit.
        //
        // So: any ritual whose copy talks about minutes at all must state the figure as a digit.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                var copy = task.Description.ToLowerInvariant();
                if (!copy.Contains("minute")) continue;

                Assert.True(LargestStatedMinutes(task.Description) > 0,
                    $"day {day.DayIndex} ritual '{task.Id}' mentions minutes but states no parseable figure, so the load check cannot see it: \"{task.Description}\"");
            }
        }
    }

    [Fact]
    public void NoRitualBorrowsAStepThatContainsItsOwnSession()
    {
        // The specific trap the review found: t2_boss's objective is "Full Shave + Full Makeup + Full
        // Uniform + Complete a session (30 mins)". Inside a program that session requirement is already
        // met by the program's own session, so borrowing that step double-counts thirty minutes and
        // blows the cap. Kept borrows none of them; this keeps it that way.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                var step = RoadmapStepDefinition.GetById(task.RoadmapStepId ?? "");
                if (step == null) continue;

                Assert.DoesNotContain("complete a session", step.Objective.ToLowerInvariant());
            }
        }
    }

    /// <summary>
    /// Largest "N min(s)" / "N-minute" figure stated in a piece of copy, or 0. Deliberately greedy -
    /// for a load check, over-counting is the safe direction.
    /// </summary>
    private static int LargestStatedMinutes(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var largest = 0;

        foreach (System.Text.RegularExpressions.Match match in
                 System.Text.RegularExpressions.Regex.Matches(text, @"(\d+)\s*-?\s*min", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            if (int.TryParse(match.Groups[1].Value, out var minutes))
            {
                largest = Math.Max(largest, minutes);
            }
        }

        return largest;
    }

    // -------------------------------------------------------------------------------------------
    // Vocabulary rule
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EverySubliminalIsACirceSubliminal()
    {
        var pool = BuiltInMods.Locked.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryLockCardIsACirceLockCard()
    {
        var pool = BuiltInMods.Locked.LockCardPhrases!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.LockCardPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryBouncingLineIsACirceBouncingLine()
    {
        // Unlike Bambi Sleep and Dronification, Circe's Lock ships a real BouncingTextPool, so
        // nothing has to be borrowed from her chat phrase lists.
        var pool = BuiltInMods.Locked.BouncingTextPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.BouncingTextPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void TheLockCardsTheBriefNamesAreThePhrasesThoseDaysActuallyRun()
    {
        // Five days name a specific lock card in their copy. Asserted against the *built session*
        // rather than the template, because that is what reaches the user: the day-1 vow and the
        // day-28 finale are pinned with per-day overrides, and a promise like "Complete 3 lock cards
        // - GOOD BOYS DON'T DECIDE." has to be certain rather than a one-in-27 shuffle.
        //
        // This test caught the real thing it was written for: day 3 originally named the gratitude
        // line while running a template pool narrowed to the vow alone, so its copy named a sentence
        // the session could never show.
        var program = Program();

        void AssertRuns(int dayIndex, string phrase)
        {
            var day = program.GetDay(dayIndex);
            Assert.NotNull(day);

            var session = ProgramSessionBuilder.Build(program, day!);

            Assert.True(session.Settings.LockCardEnabled,
                $"day {dayIndex} names a lock card but its session does not run lock cards");
            Assert.Equal(new[] { phrase }, session.Settings.LockCardPhrases);
        }

        AssertRuns(1, "CIRCE HOLDS MY KEY.");
        AssertRuns(3, "I AM KEPT, AND I AM GRATEFUL.");
        AssertRuns(9, "GOOD BOYS DON'T DECIDE.");
        AssertRuns(20, "I DON'T NEED CONTROL. SHE HAS IT.");
        AssertRuns(28, "I EXIST TO BE HERS.");
    }

    [Fact]
    public void EveryOverrideTargetsARealSettingsFieldAndSurvivesTheBuilder()
    {
        // ProgramSessionBuilder.ApplyOverrides logs a warning and skips an unknown field rather than
        // throwing, so a typo'd override key is completely silent - the day just quietly runs the
        // un-overridden template. Every override here is checked to have actually landed.
        var program = Program();
        var settingsFields = typeof(SessionSettings)
            .GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var daysWithOverrides = program.AllDays.Where(d => d.Overrides is { Count: > 0 }).ToList();
        Assert.NotEmpty(daysWithOverrides);

        foreach (var day in daysWithOverrides)
        {
            foreach (var key in day.Overrides!.Keys)
            {
                Assert.Contains(key, settingsFields);
            }

            var session = ProgramSessionBuilder.Build(program, day);

            if (day.Overrides.TryGetValue("LockCardPhrases", out var raw) && raw is List<string> expected)
            {
                Assert.Equal(expected, session.Settings.LockCardPhrases);
            }
        }
    }

    [Fact]
    public void PhrasePoolsAreNonEmptyWhereverTheFeatureIsOn()
    {
        // SessionEngine.ApplySessionSettings only overrides the live pool when the feature is on, so
        // an enabled feature with an empty pool silently falls back to the user's own dashboard
        // phrases - which is how Circe's program ends up whispering Bambi's lines.
        foreach (var template in Program().Templates)
        {
            var floor = template.Floor;

            if (floor.SubliminalEnabled) Assert.NotEmpty(floor.SubliminalPhrases);
            if (floor.LockCardEnabled) Assert.NotEmpty(floor.LockCardPhrases);
            if (floor.BouncingTextEnabled) Assert.NotEmpty(floor.BouncingTextPhrases);
        }
    }

    [Fact]
    public void TemplatesOwnTheirFrequenciesWhereTheFeatureIsOn()
    {
        // LerpSettings leaves an int? null when the floor leaves it null, and a null falls through to
        // the user's dashboard value. Correct for features the program does not run, a bug for
        // features it does - a verified task must not depend on a number the user can change.
        foreach (var template in Program().Templates)
        {
            var floor = template.Floor;
            var ceiling = template.Ceiling;

            if (floor.LockCardEnabled)
            {
                Assert.NotNull(floor.LockCardFrequency);
                Assert.NotNull(ceiling.LockCardFrequency);
            }

            if (floor.BubbleCountEnabled)
            {
                Assert.NotNull(floor.BubbleCountFrequency);
                Assert.NotNull(ceiling.BubbleCountFrequency);
            }

            if (floor.MandatoryVideosEnabled)
            {
                Assert.NotNull(floor.VideosPerHour);
                Assert.NotNull(ceiling.VideosPerHour);
            }
        }
    }

    [Fact]
    public void EnableFlagsMatchBetweenFloorAndCeiling()
    {
        // Only the floor's booleans are read. Writing them identically in both halves is a
        // readability contract - a reader should never have to remember which half wins.
        foreach (var template in Program().Templates)
        {
            var f = template.Floor;
            var c = template.Ceiling;

            Assert.Equal(f.FlashEnabled, c.FlashEnabled);
            Assert.Equal(f.SubliminalEnabled, c.SubliminalEnabled);
            Assert.Equal(f.AudioWhispersEnabled, c.AudioWhispersEnabled);
            Assert.Equal(f.BouncingTextEnabled, c.BouncingTextEnabled);
            Assert.Equal(f.BubblesEnabled, c.BubblesEnabled);
            Assert.Equal(f.PinkFilterEnabled, c.PinkFilterEnabled);
            Assert.Equal(f.SpiralEnabled, c.SpiralEnabled);
            Assert.Equal(f.CornerGifEnabled, c.CornerGifEnabled);
            Assert.Equal(f.MandatoryVideosEnabled, c.MandatoryVideosEnabled);
            Assert.Equal(f.LockCardEnabled, c.LockCardEnabled);
            Assert.Equal(f.BubbleCountEnabled, c.BubbleCountEnabled);
            Assert.Equal(f.MindWipeEnabled, c.MindWipeEnabled);
            Assert.Equal(f.FlashHydra, c.FlashHydra);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Authored prose only - titles, blurbs, task copy, reward and ambient descriptions. Deliberately
    /// excludes the phrase pools, which are the mod's own vocabulary and say nothing about mechanics.
    /// </summary>
    private static IEnumerable<(string Label, string Text)> AuthoredProse(ProgramDefinition program)
    {
        yield return ("Title", program.Title);
        yield return ("Subtitle", program.Subtitle);
        yield return ("Pitch", program.Pitch);
        yield return ("ContractPhrase", program.ContractPhrase);

        foreach (var template in program.Templates)
        {
            yield return ($"template {template.Id} name", template.Name);
            yield return ($"template {template.Id} description", template.Description);
        }

        foreach (var chapter in program.Chapters)
        {
            yield return ($"chapter {chapter.Id} name", chapter.Name);
            yield return ($"chapter {chapter.Id} subtitle", chapter.Subtitle);

            if (!string.IsNullOrEmpty(chapter.RewardDescription))
                yield return ($"chapter {chapter.Id} reward", chapter.RewardDescription);
        }

        foreach (var day in program.AllDays)
        {
            yield return ($"day {day.DayIndex} title", day.Title);
            yield return ($"day {day.DayIndex} blurb", day.Blurb);

            if (!string.IsNullOrEmpty(day.RewardDescription))
                yield return ($"day {day.DayIndex} reward", day.RewardDescription);

            if (day.Ambient != null)
                yield return ($"day {day.DayIndex} ambient", day.Ambient.Description);

            foreach (var task in day.Tasks)
                yield return ($"day {day.DayIndex} task '{task.Id}'", task.Description);
        }
    }
}
