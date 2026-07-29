using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TAKEOVER, checked as authored content rather than as engine behaviour.
///
/// A 28-day program is not something a play-test finds bugs in: nobody is going to sit through four
/// weeks to discover that day 19's intensity went backwards, that chapter 3 forgot to deload, or that
/// day 21 references a template id somebody renamed. Those are all silent at enrollment and expensive
/// on day 21, which is exactly the shape of bug a content test is for.
///
/// Three properties here are worth more than the rest, because each one is a promise the program makes
/// that a human reviewer will not reliably re-check:
///
///   1. The sawtooth is a CONSTANT RATIO, not just a direction. A deload that shrinks as the program
///      lengthens is worse than none - by day 22 a 12% dip near the top of a saturating lerp is inside
///      integer rounding, so the user is promised mercy and handed yesterday's session.
///   2. The finale is DISTINGUISHABLE. Days 25-28 cannot be carried by intensity because near i=1.0
///      every lerped field is saturated, so they are carried by Overrides and one deliberate drop.
///   3. This program owns NONE of Firmware Install's verifiers. That is the whole of the two programs'
///      differentiation and it is one careless task edit away from collapsing.
///
/// Pure data construction - no App reads, no service instances.
/// </summary>
public class ProgramTakeoverTests
{
    private static ProgramDefinition Program() => BuiltInPrograms.TheTakeover();

    /// <summary>The four substrings AchievementService.TrackSessionComplete matches built-ins on.</summary>
    private static readonly string[] ReservedSubstrings =
    {
        "morning drift", "gamer girl", "distant doll", "good girls"
    };

    /// <summary>Verifiers that belong to Firmware Install under the agreed split. See B1 of the review.</summary>
    private static readonly QuestCategory[] FirmwareVerifiers =
    {
        QuestCategory.KeywordTrigger, QuestCategory.BlinkTrainer, QuestCategory.Lockdown
    };

    private static readonly QuestCategory[] PremiumVerifiers =
    {
        QuestCategory.Autonomy, QuestCategory.Lockdown, QuestCategory.Remote,
        QuestCategory.KeywordTrigger, QuestCategory.BlinkTrainer
    };

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
        // ProgramService hands day objects to the UI and the session builder; a shared static graph
        // would let one enrollment's mutation leak into the next.
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

        Assert.Equal("the_takeover", program.Id);
        Assert.Equal(BuiltInMods.BambiSleepId, program.ModId);
        Assert.Equal(ProgramTier.Premium, program.Tier);
        Assert.Equal("#FF69B4", program.AccentColor);
        Assert.Equal(28, program.LengthDays);
        Assert.Equal(90, program.Rules.MaxDailyMinutes);
        Assert.False(string.IsNullOrWhiteSpace(program.Pitch));
        Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase));
    }

    [Fact]
    public void DaysOffScaleWithProgramLength()
    {
        // One day off per seven days of length. A single allowance across 28 days is four times
        // stricter than the same allowance across 7, and losing 24 days of a paid program to a second
        // illness is the most likely refund cause the feature has.
        var program = Program();

        Assert.Equal(4, program.Rules.DaysOffAllowed);
        Assert.Equal(program.LengthDays / 7, program.Rules.DaysOffAllowed);
        Assert.True(program.Rules.StrictAvailable);
    }

    [Fact]
    public void DayCountMatchesLengthDays()
    {
        var program = Program();

        Assert.Equal(program.LengthDays, program.AllDays.Count());
        Assert.Equal(28, program.AllDays.Count());
    }

    [Fact]
    public void HasFourTemplatesAndFourChapters()
    {
        var program = Program();

        Assert.Equal(4, program.Templates.Count);
        Assert.Equal(4, program.Chapters.Count);
    }

    [Fact]
    public void EveryChapterIsSevenContiguousDaysAndTheChaptersTileOneToTwentyEight()
    {
        var program = Program();
        var expected = 1;

        foreach (var chapter in program.Chapters)
        {
            Assert.Equal(7, chapter.Days.Count);

            foreach (var day in chapter.Days)
            {
                // Contiguous within the chapter AND continuous across the chapter boundary, which is
                // the property that keeps ProgramService's day clock resolving to a real day.
                Assert.Equal(expected, day.DayIndex);
                expected++;
            }
        }

        Assert.Equal(29, expected);

        for (int day = 1; day <= 28; day++)
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

        // An unused template is authored content nobody will ever see - almost always a typo in a
        // day's SessionTemplateId rather than a deliberate spare.
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
    // The 90-minute cap and the duration quantisation
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
        // Validate() only checks day.SessionMinutes against the cap, so a ritual whose *objective*
        // embeds minutes is invisible to it. t2_boss's objective ends "+ Complete a session (30 mins)"
        // - stacked naively on day 28's 75 minutes that is 105 minutes of seated time and a breach.
        //
        // The call taken here: the roadmap's session clause exists only because the roadmap has no
        // other way to require a session, and inside a program that requirement is already met by the
        // day's own session. So there is no breach - but the *copy* has to say so, or the user reads
        // the objective and sits down twice.
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

    // -------------------------------------------------------------------------------------------
    // The sawtooth
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EveryChapterOpensAtSeventyPercentOfThePreviousChaptersPeak()
    {
        // The ratio, not just the direction. Dips of 0.67x / 0.82x / 0.875x - which is what an earlier
        // draft had - mean the deload gets shallower exactly as accumulated fatigue gets worse.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var opening = chapters[i].Days.First().Intensity;
            var ratio = opening / previousPeak;

            Assert.InRange(ratio, 0.675, 0.725);
        }
    }

    [Fact]
    public void EveryChapterTakesTwoDaysBeforeExceedingThePreviousPeak()
    {
        // The brief's justification for the sawtooth is that *chapter openings* feel like relief. A dip
        // one day wide is a dip the user is already past before they notice it.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var days = chapters[i].Days;

            Assert.True(days[0].Intensity <= previousPeak,
                $"{chapters[i].Id} day {days[0].DayIndex} ({days[0].Intensity}) already exceeds the previous peak {previousPeak}");
            Assert.True(days[1].Intensity <= previousPeak,
                $"{chapters[i].Id} day {days[1].DayIndex} ({days[1].Intensity}) exceeds the previous peak {previousPeak} after only one day");
            Assert.True(days[2].Intensity > previousPeak,
                $"{chapters[i].Id} day {days[2].DayIndex} ({days[2].Intensity}) still has not exceeded the previous peak {previousPeak}");
        }
    }

    [Fact]
    public void IntensityRisesWithinEveryChapterExceptForTheOneAuthoredHeldBreath()
    {
        // Exactly one day in the program dips without a chapter boundary under it: day 27, the held
        // breath before the finale. Anything else that dips is an authoring slip, and the assertion is
        // shaped to catch that rather than to bless any dip.
        var dips = new List<int>();

        foreach (var chapter in Program().Chapters)
        {
            for (int i = 1; i < chapter.Days.Count; i++)
            {
                if (chapter.Days[i].Intensity <= chapter.Days[i - 1].Intensity)
                    dips.Add(chapter.Days[i].DayIndex);
            }
        }

        Assert.Equal(new[] { 27 }, dips.ToArray());
    }

    [Fact]
    public void DayTwentySevenIsAHeldBreathAndDayTwentyEightLandsFromIt()
    {
        var program = Program();
        var d26 = program.GetDay(26)!;
        var d27 = program.GetDay(27)!;
        var d28 = program.GetDay(28)!;

        // Down on every axis the user can feel: intensity, duration, and - the one intensity can never
        // do, because the builder reads booleans from Floor - a template with fewer features in it.
        Assert.True(d27.Intensity < d26.Intensity);
        Assert.True(d27.SessionMinutes < d26.SessionMinutes);
        Assert.NotEqual(d26.SessionTemplateId, d27.SessionTemplateId);
        Assert.False(d27.IsBoss);

        var quieter = program.GetTemplate(d27.SessionTemplateId)!;
        var louder = program.GetTemplate(d26.SessionTemplateId)!;
        Assert.False(quieter.Floor.MindWipeEnabled);
        Assert.True(louder.Floor.MindWipeEnabled);

        // And the finale climbs back out of it.
        Assert.True(d28.Intensity > d27.Intensity);
        Assert.True(d28.SessionMinutes > d27.SessionMinutes);
        Assert.True(d28.IsBoss);
    }

    [Fact]
    public void EveryChaptersPeakIsItsBossDay()
    {
        // "Boss days ignore the deload" - a boss that is not the chapter peak is a boss the chapter
        // already beat on day 5.
        foreach (var chapter in Program().Chapters)
        {
            var boss = chapter.Days.Single(d => d.IsBoss);
            Assert.Equal(chapter.Days.Max(d => d.Intensity), boss.Intensity);
        }
    }

    [Fact]
    public void TheCurveStartsNearZeroAndEndsAtOne()
    {
        var days = Program().AllDays.ToList();

        Assert.InRange(days.First().Intensity, 0.0, 0.10);
        Assert.Equal(1.00, days.Last().Intensity, precision: 3);

        foreach (var day in days)
        {
            Assert.InRange(day.Intensity, 0.0, 1.0);
        }
    }

    // -------------------------------------------------------------------------------------------
    // The finale is four distinguishable days
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void TheLastFourDaysEachChangeSomethingIntensityCouldNotHaveChanged()
    {
        // Days 25-28 at .88/.93/.74/1.00 on a saturated curve would be near-identical sessions without
        // this. Each of 25, 26 and 28 carries exactly one new idea as Overrides; day 27 carries its
        // change as a template and duration drop instead.
        var program = Program();

        var d25 = program.GetDay(25)!;
        var d26 = program.GetDay(26)!;
        var d27 = program.GetDay(27)!;
        var d28 = program.GetDay(28)!;

        Assert.NotNull(d25.Overrides);
        Assert.NotNull(d26.Overrides);
        Assert.True(d27.Overrides == null || d27.Overrides.Count == 0);
        Assert.NotNull(d28.Overrides);

        // The three overriding days touch disjoint parts of the session, so no day's "new thing" is
        // just a louder version of the previous day's.
        Assert.Contains("FlashImages", d25.Overrides!.Keys);
        Assert.Contains("MindWipeBaseMultiplier", d26.Overrides!.Keys);
        Assert.Contains("CornerGifEnabled", d28.Overrides!.Keys);
        Assert.DoesNotContain("MindWipeBaseMultiplier", d25.Overrides.Keys);
        Assert.DoesNotContain("FlashImages", d26.Overrides.Keys);
    }

    [Fact]
    public void FinaleOverridesActuallyLandOnTheBuiltSessions()
    {
        // If an override key is ever misspelled, ApplyOverrides logs a warning and moves on - silently.
        // The finale would then quietly run at day 24's numbers and nobody would find out.
        var program = Program();

        var d25 = ProgramSessionBuilder.Build(program, program.GetDay(25)!);
        Assert.Equal(6, d25.Settings.FlashImages);

        var d26 = ProgramSessionBuilder.Build(program, program.GetDay(26)!);
        Assert.Equal(4, d26.Settings.MindWipeBaseMultiplier);
        Assert.Equal(75, d26.Settings.MindWipeVolume);
        Assert.Equal(0, d26.Settings.MindWipeStartMinute);

        var d28 = ProgramSessionBuilder.Build(program, program.GetDay(28)!);
        Assert.True(d28.Settings.CornerGifEnabled);
        Assert.Equal(35, d28.Settings.CornerGifOpacity);
        Assert.Equal(14, d28.Settings.SubliminalPerMin);
        Assert.Equal(95, d28.Settings.FlashOpacity);
        Assert.Equal(100, d28.Settings.FlashOpacityEnd);
        Assert.Equal(4, d28.Settings.MindWipeBaseMultiplier);
    }

    [Fact]
    public void TheCornerGifIsHeldBackForTheFinale()
    {
        // It is the only feature no template in this program enables, which is exactly what makes it
        // available as day 28's single new thing. If a template ever turns it on, the finale loses its
        // last card and this test is the warning.
        var program = Program();

        foreach (var template in program.Templates)
        {
            Assert.False(template.Floor.CornerGifEnabled, $"{template.Id} enables the corner GIF");
            Assert.False(template.Ceiling.CornerGifEnabled, $"{template.Id} enables the corner GIF on its ceiling");
        }

        var daysTurningItOn = program.AllDays
            .Where(d => d.Overrides != null && d.Overrides.ContainsKey("CornerGifEnabled"))
            .Select(d => d.DayIndex)
            .ToArray();

        Assert.Equal(new[] { 28 }, daysTurningItOn);
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
    public void TheTakeoverOwnsNoFirmwareVerifiers()
    {
        // The differentiation between this program and Firmware Install, asserted rather than trusted.
        // Firmware's four cold registers are optical (flash/blink/gaze), packet (bubble count/spiral),
        // directive (keyword triggers) and lockdown. It arms triggers six days earlier than this
        // program does, so if The Takeover also counted them the premium 28-day flagship would be
        // beaten to its own marketing hook by the cheaper 14-day program - and a user who bought both
        // would get the same assignments twice.
        //
        // The hook survives because the hook was never the counter: it is that the words go live.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks)
            {
                if (task.Verifier == null) continue;

                Assert.DoesNotContain(task.Verifier.Value, FirmwareVerifiers);
            }
        }
    }

    [Fact]
    public void TheArmingIsACeremonyOnDayFifteenRatherThanACounter()
    {
        var program = Program();
        var d15 = program.GetDay(15)!;

        // Day 15 is the arming. Its weight is the decision, so its session is the lightest of the
        // chapter and its task is small.
        Assert.Equal("Armed", d15.Title);
        Assert.NotNull(d15.Ambient);
        Assert.Contains("armed", d15.Ambient!.Description, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(d15.RewardDescription));
        Assert.Contains("Disarming", d15.RewardDescription!);

        var chapter3 = program.GetChapterForDay(15)!;
        Assert.Equal(chapter3.Days.Min(d => d.SessionMinutes), d15.SessionMinutes);
        Assert.Equal(chapter3.Days.Min(d => d.Intensity), d15.Intensity);
    }

    [Fact]
    public void AutonomyIsTheSignaturePremiumVerbAndItLadders()
    {
        // With the Firmware verifiers gone, Takeover minutes are what carries the premium half of the
        // program. A ladder that does not climb is four days of the same assignment.
        var targets = Program().AllDays
            .SelectMany(d => d.Tasks)
            .Where(t => t.Verifier == QuestCategory.Autonomy)
            .Select(t => t.TargetValue)
            .ToList();

        Assert.Equal(new[] { 15, 25, 30, 40 }, targets.ToArray());
    }

    [Fact]
    public void EveryRitualTaskBorrowsARealRoadmapStep()
    {
        var real = RoadmapStepDefinition.AllSteps.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                Assert.False(string.IsNullOrWhiteSpace(task.RoadmapStepId));
                Assert.Contains(task.RoadmapStepId!, real);
            }
        }
    }

    [Fact]
    public void EveryRitualTaskSaysThePhotoStaysOnTheMachine()
    {
        // Content brief 1.3: ritual photos never leave the machine, and the copy has to say so - for
        // this audience it is the difference between a feature and a liability. Asserted per task
        // rather than once on the SafetyNote, because the task card is where the user is standing when
        // they decide whether to take the photo.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                Assert.Contains("stays on this machine", task.Description);
                Assert.Contains("never uploaded", task.Description);
            }
        }
    }

    [Fact]
    public void PremiumOnlyVerifiersAreMarkedRequiresPremium()
    {
        // ProgramService.IsTaskBlocked reads RequiresPremium to decide whether a task is required for
        // the day. A premium-only verifier that is not marked would produce a day a lapsed pledge can
        // never complete.
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
        // A day whose every task is Optional cannot be failed and therefore cannot be completed in any
        // meaningful sense. The Takeover is premium and gates on nothing free-tier, so it authors none.
        foreach (var day in Program().AllDays)
        {
            Assert.Contains(day.Tasks, t => !t.Optional);
        }
    }

    [Fact]
    public void EveryTaskFeatureIsLeftOnByTheDaysOwnSessionOrTheBlurbSequencesIt()
    {
        // SessionEngine.ApplySessionSettings does not merely fail to provide a feature the template
        // leaves off - it writes `false` into live AppSettings and stops the service. So a task naming
        // a feature the day's session disables is actively prevented, and the counter stalls for the
        // whole session unless the user does it before pressing Start. Where that is the intent (the
        // one-step-ahead tutorial), the blurb has to carry the sequencing so the design is visible
        // instead of only the contradiction.
        var program = Program();

        // The only in-session features a task here can name.
        var sessionOwned = new Dictionary<QuestCategory, Func<SessionSettings, bool>>
        {
            [QuestCategory.Flash] = s => s.FlashEnabled,
            [QuestCategory.Bubbles] = s => s.BubblesEnabled,
            [QuestCategory.PinkFilter] = s => s.PinkFilterEnabled,
            [QuestCategory.LockCard] = s => s.LockCardEnabled,
            [QuestCategory.BubbleCount] = s => s.BubbleCountEnabled,
            [QuestCategory.Video] = s => s.MandatoryVideosEnabled
        };

        // The three deliberate exceptions - the one-step-ahead tutorial - each with the phrase its own
        // blurb uses to point at the day the feature starts running unprompted. A fourth day appearing
        // here is not a new tutorial beat, it is a task the session silently prevents.
        var narratedExceptions = new Dictionary<int, string>
        {
            [2] = "Thursday",       // lock cards, taught by hand -> TK-Install runs them from day 4
            [6] = "fortnight",      // bubble count -> TK-BambiTime runs it from day 18
            [10] = "week three"     // video -> TK-BambiTime runs it from day 18
        };

        foreach (var day in program.AllDays)
        {
            var settings = ProgramSessionBuilder.Build(program, day).Settings;

            foreach (var task in day.Tasks.Where(t => t.Verifier != null))
            {
                if (!sessionOwned.TryGetValue(task.Verifier!.Value, out var isOn)) continue;
                if (isOn(settings)) continue;

                Assert.True(narratedExceptions.TryGetValue(day.DayIndex, out var marker),
                    $"day {day.DayIndex} task verifier {task.Verifier} is switched OFF by the day's own session and the blurb does not sequence it");
                Assert.Contains(marker!, day.Blurb);
            }
        }
    }

    [Fact]
    public void BlurbsCarryTheContinuityTheProgramClaims()
    {
        // A program is a sequence. A day that could be dropped in anywhere is a session, not a day - and
        // the one-step-ahead tutorial is invisible unless the copy points at it. At least half the days
        // should reference another day, a weekday or a named earlier beat.
        var markers = new[]
        {
            "day two", "day four", "day six", "day ten", "day twelve", "day thirteen", "day eighteen",
            "day twenty-four", "Tuesday", "Thursday", "Sunday", "Monday", "yesterday", "last night",
            "week three", "two weeks", "all week", "fortnight", "halfway", "twenty-eight", "tomorrow"
        };

        var days = Program().AllDays.ToList();
        var withContinuity = days.Count(d => markers.Any(m => d.Blurb.Contains(m, StringComparison.OrdinalIgnoreCase)));

        Assert.True(withContinuity >= days.Count / 2,
            $"only {withContinuity} of {days.Count} blurbs reference another day");
    }

    // -------------------------------------------------------------------------------------------
    // Ambient
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void AmbientUnlocksAtChapterThreeAndNeverBefore()
    {
        foreach (var day in Program().AllDays)
        {
            if (day.DayIndex < 15)
                Assert.Null(day.Ambient);
            else
                Assert.NotNull(day.Ambient);
        }
    }

    [Fact]
    public void EveryAmbientHasCopyAndClaimsNoVerificationItCannotDo()
    {
        // ProgramService.TrackVerifier only accumulates ambient minutes when RequiredMinutes > 0 AND
        // the category matches. There is no QuestCategory for corner-GIF minutes, out-of-session
        // subliminal minutes or minutes-with-triggers-armed, and borrowing an overlay category would
        // let the session itself silently satisfy the ambient. So every ambient here is flavour, and
        // this test is the thing that stops a future edit from quietly wiring in a fake verifier.
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
    public void EverySubliminalPhraseIsAKeyOfTheBambiSleepManifestPool()
    {
        var pool = BuiltInMods.BambiSleep.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases)
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryLockCardPhraseIsAKeyOfTheBambiSleepManifestPool()
    {
        var pool = BuiltInMods.BambiSleep.LockCardPhrases!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.LockCardPhrases)
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryBouncingTextLineIsAVerbatimBambiSleepPhrase()
    {
        // The Bambi Sleep manifest carries no BouncingTextPool, so the lines are drawn from its
        // RandomFloating and Idle lists - still the mod's own words, which is the point of the rule.
        var allowed = BuiltInMods.BambiSleep.Phrases!
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
        // "Good girl! *giggles*" is verbatim in RandomFloating, so the vocabulary rule permits it - but
        // asterisks are an avatar speech-bubble convention. Drifting across the screen as bouncing text
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
    public void TheExplicitTriggersFirstWeekWithholdsAreUsedHere()
    {
        // First Week withholds the four most explicit SubliminalPool entries so a free seven-day funnel
        // is safe to screenshot. That is only a coherent rule if the premium program they are being
        // held for actually uses them - otherwise the whole catalogue ships a sanded-down version of
        // the mod's own voice.
        var explicitTriggers = new[]
        {
            "ZAP COCK DRAIN OBEY", "COCK ZOMBIE NOW", "COCK TURNS MY BRAIN OFF", "BAMBI CUM AND COLLAPSE"
        };

        var used = Program().Templates
            .SelectMany(t => t.Floor.SubliminalPhrases)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var trigger in explicitTriggers)
        {
            Assert.Contains(trigger, used);
        }
    }

    [Fact]
    public void EveryTemplateCarriesItsOwnPhrasePoolsSoNothingFallsThroughToTheActiveMod()
    {
        // Rule: the program reads identically whether or not Bambi Sleep is the active mod. An empty
        // pool on a template whose feature is ON would fall through to whatever mod happens to be
        // loaded, which is exactly the reskin failure the vocabulary rule exists to prevent.
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
        // The real property. AchievementService.TrackSessionComplete matches built-in sessions by
        // lowercase substring, so a colliding day title would silently unlock someone else's
        // achievement on completion. Checked through the builder rather than by inspection, because
        // the builder is what actually names the session.
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
        // End to end on today's engine: template + intensity + overrides -> a Session the engine can
        // accept. A day that throws here is a day the user presses Start on and gets an exception.
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
        // Ceilings were rescaled when the curve was retuned, and the arithmetic wanted values above
        // 100 on several opacity fields. Those were clamped by hand; this is the guard that they
        // stayed clamped, including after Overrides are applied.
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
    public void IntensityActuallyMovesTheNumbersWithinEveryTemplatesUsedBand()
    {
        // A floor/ceiling pair authored too close together makes a whole chapter feel flat, and a pair
        // authored for i=1.0 on a template that only runs in a narrow band is the same bug wearing a
        // different hat. For every template, the lowest and highest day that use it must produce
        // visibly different sessions.
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

            // Real margins rather than "greater than": one or two units of anything in this app is
            // inside integer rounding, so a difference that small is not a difference.
            var rateMoved = high.Settings.FlashPerHour - low.Settings.FlashPerHour >= 10;
            var opacityMoved = high.Settings.SubliminalOpacity - low.Settings.SubliminalOpacity >= 8;

            // ...or the template's band is genuinely too narrow for any floor/ceiling pair, in which
            // case every day after its first appearance has to carry an Overrides entry instead. That
            // is the only other honest way to differentiate a day, since booleans come from Floor and
            // intensity can therefore never add a feature.
            var coveredByOverrides = days.Skip(1).All(d => d.Overrides is { Count: > 0 });

            Assert.True((rateMoved && opacityMoved) || coveredByOverrides,
                $"{template.Id} i{days.First().Intensity}..i{days.Last().Intensity}: flash rate moves " +
                $"{high.Settings.FlashPerHour - low.Settings.FlashPerHour}/hr and subliminal opacity moves " +
                $"{high.Settings.SubliminalOpacity - low.Settings.SubliminalOpacity} points, and the days are not covered by Overrides");
        }
    }

    [Fact]
    public void SafetyNoteWarnsAboutTheLiveKeywordTriggersBeforeEnrollment()
    {
        // Content brief 9.4: the live arming is this program's best hook and its biggest footgun, so
        // the enrollment ceremony has to state it, and state that disarming is free.
        var note = Program().SafetyNote.ToLowerInvariant();

        Assert.Contains("trigger", note);
        Assert.Contains("disarm", note);
        Assert.Contains("withdraw", note);
        Assert.Contains("pausing", note);
    }
}
