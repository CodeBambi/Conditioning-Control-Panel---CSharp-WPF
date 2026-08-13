using System;
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

    // ---------------------------------------------------------------------------------------------
    // Event-denominated verifiers. Until the supply models landed, only lock cards had a rate check:
    // flash, bubbles and the bubble count were waved through on "the feature is on and it starts
    // before the end", so a day could ask for any number at all. Two shipped days did.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void FirmwareCycle1AsksForNoMoreFlashesThanItsOwnSessionProduces()
    {
        // Shipped asking for 30 images from FW-Boot at i .05 - 10 -> 18 an hour, one image a burst,
        // thirty minutes - which produces about seven. Cycle 1 of a paid program, non-optional, and
        // not flagged OutsideSession, so the counter sat at 7/30 with the how-to line insisting the
        // session would deliver it.
        var firmware = Library.Single(p => p.Id == "firmware_install");
        var cycle1 = firmware.AllDays.Single(d => d.DayIndex == 1);
        var injections = cycle1.Tasks.Single(t => t.Verifier == QuestCategory.Flash);

        Assert.True(firmware.IsTaskFeasible(cycle1, injections, out var why), why);
    }

    [Fact]
    public void TakeoverDay1AsksForNoMoreFlashesThanItsOwnSessionProduces()
    {
        // The same bug in the 28-day flagship: 25 images from TK-Bubble at i .05, which produces
        // about eight. First evening of the purchase.
        var takeover = Library.Single(p => p.Id == "the_takeover");
        var day1 = takeover.AllDays.Single(d => d.DayIndex == 1);
        var flash = day1.Tasks.Single(t => t.Verifier == QuestCategory.Flash);

        Assert.True(takeover.IsTaskFeasible(day1, flash, out var why), why);
    }

    [Fact]
    public void MoreFlashesThanTheRateAndImageCountCanDeliver_IsRejected()
    {
        // 10/hour x 2 images x 30 minutes = 10.
        var program = FixtureWith(s =>
        {
            s.FlashEnabled = true;
            s.FlashPerHour = 10;
            s.FlashPerHourEnd = 10;
            s.FlashImages = 2;
        }, QuestCategory.Flash, targetValue: 11);

        Assert.False(program.Validate(out var error));
        Assert.Contains("flash images", error);
    }

    [Fact]
    public void FlashSupplyCountsEveryImageInABurst_NotOnePerBurst()
    {
        // FlashService tracks once per spawned WINDOW and a burst spawns FlashImages of them, so
        // doubling the image count doubles the credit. Getting this backwards would have made the
        // model twice as strict as the app and rejected honest days.
        var twoUp = FixtureWith(s =>
        {
            s.FlashEnabled = true;
            s.FlashPerHour = 10;
            s.FlashPerHourEnd = 10;
            s.FlashImages = 2;
        }, QuestCategory.Flash, targetValue: 10);

        var oneUp = FixtureWith(s =>
        {
            s.FlashEnabled = true;
            s.FlashPerHour = 10;
            s.FlashPerHourEnd = 10;
            s.FlashImages = 1;
        }, QuestCategory.Flash, targetValue: 10);

        Assert.True(twoUp.Validate(out var twoError), twoError);
        Assert.False(oneUp.Validate(out _));
    }

    [Fact]
    public void AFlashRateAboveTheEngineClampIsModelledAtTheClamp()
    {
        // AppSettings.FlashFrequency clamps to 1..180, so an authored 1000/hour is 180/hour. A model
        // that took the authored number at face value would bless a day the app cannot serve.
        // 180/hour x 1 image x 30 minutes = 90, not 500.
        var program = FixtureWith(s =>
        {
            s.FlashEnabled = true;
            s.FlashPerHour = 1000;
            s.FlashPerHourEnd = 1000;
            s.FlashImages = 1;
        }, QuestCategory.Flash, targetValue: 100);

        Assert.False(program.Validate(out var error));
        Assert.Contains("at most 90", error);
    }

    [Fact]
    public void MoreBubblesThanTheSpawnRateCanDeliver_IsRejected()
    {
        // BubblesFrequency is per MINUTE, not per hour - 2/min over 30 minutes is 60 bubbles.
        var program = FixtureWith(s =>
        {
            s.BubblesEnabled = true;
            s.BubblesFrequency = 2;
            s.BubblesClickable = true;
        }, QuestCategory.Bubbles, targetValue: 61);

        Assert.False(program.Validate(out var error));
        Assert.Contains("bubbles", error);
    }

    [Fact]
    public void BubblesWithClickingOffCanNeverCredit()
    {
        // BubbleService awards on the POP. An unclickable bubble is never popped, so a session with
        // clicking off produces exactly zero credit however many it spawns - a case the old check,
        // which only asked whether bubbles were enabled, passed happily.
        var program = FixtureWith(s =>
        {
            s.BubblesEnabled = true;
            s.BubblesFrequency = 10;
            s.BubblesClickable = false;
        }, QuestCategory.Bubbles, targetValue: 1);

        Assert.False(program.Validate(out var error));
        Assert.Contains("at most 0", error);
    }

    [Fact]
    public void MoreBubbleCountGamesThanTheRateCanDeliver_IsRejected()
    {
        // 2 games/hour over 30 usable minutes is one game.
        var program = FixtureWith(s =>
        {
            s.BubbleCountEnabled = true;
            s.BubbleCountFrequency = 2;
        }, QuestCategory.BubbleCount, targetValue: 2);

        Assert.False(program.Validate(out var error));
        Assert.Contains("bubble count", error);
    }

    [Fact]
    public void ARateTheTemplateLeavesNullIsNotGuessedAt()
    {
        // int? frequencies fall through to the user's own dashboard value when a template leaves
        // them null, so the definition genuinely does not know the rate. Refusing to model it is
        // right; inventing a number would reject days that are fine for most users.
        var program = FixtureWith(s =>
        {
            s.BubbleCountEnabled = true;
            s.BubbleCountFrequency = null;
        }, QuestCategory.BubbleCount, targetValue: 99);

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void VideoIsModelledAsClipsRatherThanContinuousPlayback()
    {
        // The old model asked only "is TargetValue <= availableMinutes", so twenty minutes of video
        // out of a thirty-minute session read as comfortable. In fact Video credits actual playback
        // of the user's own files and the session only STARTS clips, at VideosPerHour - one an hour
        // across 30 minutes is half a clip. Six shipped days were sitting on this.
        var program = FixtureWith(s =>
        {
            s.MandatoryVideosEnabled = true;
            s.VideosPerHour = 1;
        }, QuestCategory.Video, targetValue: 20);

        Assert.False(program.Validate(out var error));
        Assert.Contains("minutes of mandatory videos", error);
    }

    [Fact]
    public void EveryShippedVideoTaskIsEitherOutsideSessionOrModestEnoughToBeProduced()
    {
        // The rule the library now holds to, stated once. Nothing here may quietly go back to
        // promising twenty minutes of playback from a session that starts one clip.
        foreach (var program in Library)
        foreach (var day in program.AllDays)
        foreach (var task in day.Tasks.Where(t => t.Verifier == QuestCategory.Video))
        {
            Assert.True(program.IsTaskFeasible(day, task, out var why),
                $"{program.Id} day {day.DayIndex} '{task.Id}': {why}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Per-day Overrides. The check used to read the raw template, so a day that switched a feature
    // ON was judged switched off - a hard Validate failure, and therefore a program CanEnroll would
    // refuse outright - and a day that switched one OFF or pushed its start later passed wrongly.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ADayOverrideThatSwitchesTheFeatureOn_MakesTheTaskFeasible()
    {
        var program = FixtureWith(s => s.PinkFilterEnabled = false, QuestCategory.PinkFilter, targetValue: 20);
        Assert.False(program.Validate(out var withoutOverride));
        Assert.Contains("switched off", withoutOverride);

        program.AllDays.First().Overrides = new Dictionary<string, object>
        {
            ["PinkFilterEnabled"] = true,
            ["PinkFilterStartMinute"] = 0
        };

        Assert.True(program.Validate(out var withOverride), withOverride);
    }

    [Fact]
    public void ADayOverrideThatSwitchesTheFeatureOff_IsRejected()
    {
        var program = FixtureWith(s => s.PinkFilterEnabled = true, QuestCategory.PinkFilter, targetValue: 20);
        Assert.True(program.Validate(out var before), before);

        program.AllDays.First().Overrides = new Dictionary<string, object>
        {
            ["PinkFilterEnabled"] = false
        };

        Assert.False(program.Validate(out var error));
        Assert.Contains("switched off", error);
    }

    [Fact]
    public void ADayOverrideThatPushesTheStartPastTheSession_IsRejected()
    {
        var program = FixtureWith(s => s.PinkFilterEnabled = true, QuestCategory.PinkFilter, targetValue: 5);
        Assert.True(program.Validate(out var before), before);

        // Minute 28 of a 30-minute session, and the engine jitters a delayed start by up to +3.
        program.AllDays.First().Overrides = new Dictionary<string, object>
        {
            ["PinkFilterStartMinute"] = 28
        };

        Assert.False(program.Validate(out var error));
        Assert.Contains("starts at minute 28", error);
    }

    // ---------------------------------------------------------------------------------------------
    // Ambient layers. Exempt from validation entirely until now, which is how four cycles shipped
    // asking for sixty minutes of filter from 45-to-60-minute sessions. An unreachable ambient does
    // not block the day - ProgramService settles it at rollover with the day XP withheld - so the
    // user is quietly underpaid for a day they finished, with nothing anywhere saying why.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AmbientMinutesBeyondTheSessionsBudget_AreRejected()
    {
        var program = FixtureWith(s => s.PinkFilterEnabled = true, QuestCategory.PinkFilter, targetValue: 5);
        program.AllDays.First().Ambient = new ProgramAmbient
        {
            Description = "Filter on all day",
            RequiredMinutes = 40,
            Verifier = QuestCategory.PinkFilter
        };

        Assert.False(program.Validate(out var error));
        Assert.Contains("ambient layer", error);
    }

    [Fact]
    public void AmbientMarkedOutsideSessionIsExempt()
    {
        var program = FixtureWith(s => s.PinkFilterEnabled = true, QuestCategory.PinkFilter, targetValue: 5);
        program.AllDays.First().Ambient = new ProgramAmbient
        {
            Description = "Filter on all day",
            RequiredMinutes = 40,
            Verifier = QuestCategory.PinkFilter,
            OutsideSession = true
        };

        Assert.True(program.Validate(out var error), error);
    }

    [Fact]
    public void AnAmbientRequiringMinutesWithNoVerifierIsRejected()
    {
        // Nothing can ever credit it, so the day would hold open until rollover every single time.
        var program = FixtureWith(s => s.PinkFilterEnabled = true, QuestCategory.PinkFilter, targetValue: 5);
        program.AllDays.First().Ambient = new ProgramAmbient
        {
            Description = "Something, vaguely",
            RequiredMinutes = 30
        };

        Assert.False(program.Validate(out var error));
        Assert.Contains("no verifier", error);
    }

    [Fact]
    public void EveryShippedAmbientIsReachableOrFlaggedOutsideSession()
    {
        var failures = new List<string>();

        foreach (var program in Library)
        foreach (var day in program.AllDays)
        {
            if (!program.IsAmbientFeasible(day, out var why))
                failures.Add($"{program.Id} day {day.DayIndex}: {why}");
        }

        Assert.True(failures.Count == 0,
            "Ambient layers their own session can never satisfy:\n  " + string.Join("\n  ", failures));
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

    private static ProgramDefinition OneDayProgram(bool lockCardsEnabled, QuestCategory verifier, int targetValue) =>
        FixtureWith(s => s.LockCardEnabled = lockCardsEnabled, verifier, targetValue);

    /// <summary>
    /// One 30-minute day at intensity 0 whose template Floor and Ceiling are both shaped by
    /// <paramref name="configure"/>. Intensity 0 with an identical pair means the lerp is a no-op, so
    /// a test can state the settings it means and reason about the supply arithmetic directly.
    /// </summary>
    private static ProgramDefinition FixtureWith(Action<SessionSettings> configure, QuestCategory verifier, int targetValue)
    {
        var floor = new SessionSettings();
        var ceiling = new SessionSettings();
        configure(floor);
        configure(ceiling);

        return BuildFixture(floor, ceiling, verifier, targetValue);
    }

    private static ProgramDefinition BuildFixture(SessionSettings floor, SessionSettings ceiling, QuestCategory verifier, int targetValue)
    {
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
