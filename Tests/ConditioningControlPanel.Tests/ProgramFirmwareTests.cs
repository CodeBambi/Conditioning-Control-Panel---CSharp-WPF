using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// FIRMWARE INSTALL, checked as authored content rather than as engine behaviour.
///
/// A 14-day program is not something a play-test finds bugs in: nobody sits through two weeks to
/// discover that cycle 11's intensity went backwards, that module 2 forgot to deload, or that cycle 13
/// names a template id somebody renamed. Those are silent at enrollment and expensive on cycle 13,
/// which is exactly the shape of bug a content test exists for.
///
/// The load-bearing test in this file is <see cref="DroneOsNeverPraises"/>. Firmware Install's whole
/// product differentiator is that DroneOS gives you `[OK]` and nothing warmer, and that is a property
/// a copy pass will break by accident and no reviewer will reliably catch across fourteen blurbs, four
/// template descriptions and forty-odd phrases. So it is asserted, not trusted.
///
/// Pure data construction - no App reads, no service instances.
/// </summary>
public class ProgramFirmwareTests
{
    private static ProgramDefinition Program() => BuiltInPrograms.FirmwareInstall();

    /// <summary>The four substrings AchievementService.TrackSessionComplete matches built-ins on.</summary>
    private static readonly string[] ReservedSubstrings =
    {
        "morning drift", "gamer girl", "distant doll", "good girls"
    };

    /// <summary>Durations the content brief quantises to.</summary>
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

        Assert.Equal("firmware_install", program.Id);
        Assert.Equal(BuiltInMods.DronificationId, program.ModId);
        Assert.Equal(ProgramTier.Premium, program.Tier);
        Assert.Equal("#00FF41", program.AccentColor);
        Assert.Equal(14, program.LengthDays);
        Assert.Equal(90, program.Rules.MaxDailyMinutes);
        Assert.False(string.IsNullOrWhiteSpace(program.Pitch));
        Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase));

        // One day off per 7 cycles of length. A single allowance across 14 cycles is twice as strict as
        // the same allowance across 7, and the pressure in this program is the log, not the clock.
        Assert.Equal(2, program.Rules.DaysOffAllowed);
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

        // A template nobody references is dead weight the reader has to reason about anyway.
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

        // Two modules of seven, in order, with no gap and no overlap. GetChapterForDay takes the
        // first chapter containing the index, so an overlap would silently bind a day to the wrong
        // module's accent and reward.
        Assert.Equal(2, program.Chapters.Count);

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
    public void EveryDayCarriesATitleABlurbAndAtLeastOneTask()
    {
        foreach (var day in Program().AllDays)
        {
            Assert.False(string.IsNullOrWhiteSpace(day.Title), $"cycle {day.DayIndex} has no title");
            Assert.False(string.IsNullOrWhiteSpace(day.Blurb), $"cycle {day.DayIndex} has no blurb");
            Assert.NotEmpty(day.Tasks);
        }
    }

    // -------------------------------------------------------------------------------------------
    // The curve
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void IntensityRisesWithinEveryChapter()
    {
        foreach (var chapter in Program().Chapters)
        {
            var days = chapter.Days.OrderBy(d => d.DayIndex).ToList();
            for (int i = 1; i < days.Count; i++)
            {
                Assert.True(days[i].Intensity > days[i - 1].Intensity,
                    $"cycle {days[i].DayIndex} intensity {days[i].Intensity} does not exceed cycle {days[i - 1].DayIndex}'s {days[i - 1].Intensity}");
            }
        }
    }

    /// <summary>
    /// The whole authored curve, pinned. Written out rather than derived so a reviewer can read the
    /// shape at a glance and any edit is a visible diff rather than a drifting number.
    /// </summary>
    private static readonly double[] ExpectedCurve =
    {
        /* module 1, cycles 1-7  */ 0.05, 0.12, 0.18, 0.24, 0.28, 0.32, 0.35,
        /* module 2, cycles 8-14 */ 0.25, 0.32, 0.40, 0.50, 0.60, 0.68, 0.75
    };

    [Fact]
    public void TheCurveIsExactlyAsAuthored()
    {
        var actual = Program().AllDays.Select(d => d.Intensity).ToArray();
        Assert.Equal(ExpectedCurve, actual);
    }

    [Fact]
    public void ModuleTwoOpensAtSeventyPercentOfModuleOnesPeak()
    {
        // Asserted as a *ratio* rather than a direction. The first draft opened module 2 at .28 against
        // a .35 peak - 0.80x - which satisfies "below the previous peak" while being a dip the Unit
        // cannot feel: near the top of a saturating lerp a 20% step moves most authored fields by less
        // than integer rounding. The house rule is 0.70x, and 0.70 x .35 = .245, authored as .25.
        // Tolerance is 0.02 absolute on the ratio to allow that two-decimal rounding.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var opening = chapters[i].Days.OrderBy(d => d.DayIndex).First().Intensity;

            Assert.InRange(opening / previousPeak, 0.68, 0.72);
        }
    }

    [Fact]
    public void ModuleTwoTakesTwoCyclesToExceedModuleOnesPeak()
    {
        // The other half of the rule: a one-cycle dip is a blip, not an opening. Cycles 8 and 9 both
        // sit below module 1's peak and cycle 10 is the first to clear it.
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var days = chapters[i].Days.OrderBy(d => d.DayIndex).ToList();

            Assert.True(days[0].Intensity < previousPeak,
                $"cycle {days[0].DayIndex} should open below the previous peak of {previousPeak}");
            Assert.True(days[1].Intensity < previousPeak,
                $"cycle {days[1].DayIndex} should still be below {previousPeak} - the dip is two cycles wide");
            Assert.True(days[2].Intensity > previousPeak,
                $"cycle {days[2].DayIndex} should be the first to clear {previousPeak}");
        }
    }

    [Fact]
    public void EachModuleExceedsThePreviousModulesPeak()
    {
        var chapters = Program().Chapters;

        for (int i = 1; i < chapters.Count; i++)
        {
            var previousPeak = chapters[i - 1].Days.Max(d => d.Intensity);
            var peak = chapters[i].Days.Max(d => d.Intensity);

            Assert.True(peak > previousPeak,
                $"module {i + 1} peaks at {peak}, which does not exceed module {i}'s peak of {previousPeak}");
        }
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
    public void FinalDayIsTheProgramPeak()
    {
        var program = Program();
        var final = program.GetDay(program.LengthDays);

        Assert.NotNull(final);
        Assert.True(final!.IsBoss);
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
                $"cycle {day.DayIndex} asks for {day.SessionMinutes} minutes against a {program.Rules.MaxDailyMinutes} minute cap");
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
                    $"cycle {day.DayIndex} task '{task.Id}' has a non-positive target");
            }
        }
    }

    [Fact]
    public void EveryTaskIsAutoVerified()
    {
        // Firmware Install authors no rituals on purpose. DroneOS measures; it does not take your
        // word for anything, and a self-attested task in this program would contradict the fiction
        // as much as a compliment would. If a ritual is ever added here it needs a deliberate
        // decision, so this test makes adding one loud.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks)
            {
                Assert.Equal(ProgramTaskKind.AutoVerified, task.Kind);
            }
        }
    }

    [Fact]
    public void PremiumOnlyVerifiersAreFlaggedPremium()
    {
        // The content brief marks these five as [P]. ProgramService gates on RequiresPremium, so a
        // day that forgets the flag would hand a free user a task they cannot start.
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
                        $"cycle {day.DayIndex} task '{task.Id}' uses a premium verifier without RequiresPremium");
                }
            }
        }
    }

    [Fact]
    public void RemoteCommandTaskShipsASoloAlternative()
    {
        // The brief's own warning: a remote-command task needs a second person, so a day built only
        // on Remote is a day most users physically cannot complete. Every Remote task here must be
        // optional, and its day must carry a required non-Remote task that can be done alone.
        var program = Program();

        var remoteDays = program.AllDays
            .Where(d => d.Tasks.Any(t => t.Verifier == QuestCategory.Remote))
            .ToList();

        Assert.NotEmpty(remoteDays);

        foreach (var day in remoteDays)
        {
            foreach (var remote in day.Tasks.Where(t => t.Verifier == QuestCategory.Remote))
            {
                Assert.True(remote.Optional,
                    $"cycle {day.DayIndex} requires remote commands, which needs a second operator");
            }

            Assert.Contains(day.Tasks, t =>
                !t.Optional && t.Verifier != QuestCategory.Remote);
        }
    }

    [Fact]
    public void EveryDirectiveTaskSpeaksInTheDirectiveRegister()
    {
        // B1: a review found this program and The Takeover sharing task types, in the same order, with
        // identical target values - and Firmware arming keyword triggers six days *earlier* than the
        // 28-day program whose whole hook is keyword triggers. The approved split gives Firmware the
        // counting and The Takeover the arming ceremony.
        //
        // Holding Firmware's side of that means the copy never says "trigger". A Unit does not have
        // triggers; it responds to command words. Same verifier, entirely different sentence, and this
        // is what stops a later pass pasting Bambi's phrasing back in.
        foreach (var day in Program().AllDays)
        {
            foreach (var task in day.Tasks.Where(t => t.Verifier == QuestCategory.KeywordTrigger))
            {
                Assert.Contains("[DIRECTIVE]", task.Description);
                Assert.Contains("COMMAND WORDS", task.Description);

                Assert.False(task.Description.ToLowerInvariant().Contains("trigger", StringComparison.Ordinal),
                    $"cycle {day.DayIndex} task '{task.Id}' says \"trigger\" - that is Bambi's word, not DroneOS's");
            }
        }
    }

    [Fact]
    public void TheOpticalRegisterEscalatesAndIsThisProgramsOwn()
    {
        // The second pillar of the B1 split: machine-verified *bodily* compliance - where the Unit's
        // eyes are, how often it blinks - is drone-shaped and no other program in the set touches it.
        // Asserted as a real ladder so it cannot decay into two unrelated one-off drills.
        var program = Program();

        var blinks = program.AllDays
            .SelectMany(d => d.Tasks.Where(t => t.Verifier == QuestCategory.BlinkTrainer)
                                    .Select(t => (d.DayIndex, t.TargetValue)))
            .OrderBy(x => x.DayIndex)
            .ToList();

        Assert.True(blinks.Count >= 2, "the optical register needs more than one blink drill to be a ladder");
        for (int i = 1; i < blinks.Count; i++)
        {
            Assert.True(blinks[i].TargetValue > blinks[i - 1].TargetValue,
                $"cycle {blinks[i].DayIndex} asks for {blinks[i].TargetValue} blinks, which does not escalate on cycle {blinks[i - 1].DayIndex}'s {blinks[i - 1].TargetValue}");
        }

        var fixations = program.AllDays
            .SelectMany(d => d.Tasks.Where(t => t.Verifier == QuestCategory.Spiral)
                                    .Select(t => (d.DayIndex, t.TargetValue)))
            .OrderBy(x => x.DayIndex)
            .ToList();

        Assert.True(fixations.Count >= 2, "vortex fixation is part of the optical register and needs to escalate too");
        for (int i = 1; i < fixations.Count; i++)
        {
            Assert.True(fixations[i].TargetValue > fixations[i - 1].TargetValue);
        }
    }

    [Fact]
    public void EveryDirectiveCountEscalates()
    {
        var counts = Program().AllDays
            .SelectMany(d => d.Tasks.Where(t => t.Verifier == QuestCategory.KeywordTrigger)
                                    .Select(t => (d.DayIndex, t.TargetValue)))
            .OrderBy(x => x.DayIndex)
            .ToList();

        Assert.True(counts.Count >= 3);
        for (int i = 1; i < counts.Count; i++)
        {
            Assert.True(counts[i].TargetValue > counts[i - 1].TargetValue,
                $"cycle {counts[i].DayIndex} does not escalate on cycle {counts[i - 1].DayIndex}");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Load
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void SessionPlusAnyEmbeddedRitualMinutesStaysUnderTheDailyCap()
    {
        // Validate() only checks day.SessionMinutes, so a session carrying a ritual whose objective
        // embeds its own duration ("run a haptic ramp (10 mins)", "Complete a session (30 mins)") can
        // breach the 90-minute cap while passing validation silently. Firmware authors no rituals at
        // all, so today this asserts the sum trivially - it exists so that if a ritual is ever added
        // here, the combined figure is already being checked.
        var program = Program();

        foreach (var day in program.AllDays)
        {
            var embedded = 0;

            foreach (var task in day.Tasks.Where(t => t.Kind == ProgramTaskKind.Ritual))
            {
                embedded = Math.Max(embedded, LargestStatedMinutes(task.Description));

                var step = RoadmapStepDefinition.GetById(task.RoadmapStepId ?? "");
                if (step != null)
                {
                    embedded = Math.Max(embedded, LargestStatedMinutes(step.Objective));
                }
            }

            var combined = day.SessionMinutes + embedded;

            Assert.True(combined <= program.Rules.MaxDailyMinutes,
                $"cycle {day.DayIndex} is {day.SessionMinutes}m of session plus {embedded}m embedded = {combined}m, over the {program.Rules.MaxDailyMinutes}m cap");
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
    // Ambient
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void AmbientOnlyAppearsInModuleTwo()
    {
        // Firmware Install is 14 days, so the "ambient unlocks at chapter 3" cross-program rule
        // lands on module 2 for this program (content brief section 5). Either way the beat is the
        // same: the program leaves the session and enters the day at the halfway mark, and nothing
        // before that should carry an all-day layer.
        var program = Program();

        foreach (var day in program.AllDays.Where(d => d.DayIndex <= 8))
        {
            Assert.Null(day.Ambient);
        }

        foreach (var day in program.AllDays.Where(d => d.DayIndex >= 9))
        {
            Assert.NotNull(day.Ambient);
            Assert.False(string.IsNullOrWhiteSpace(day.Ambient!.Description));
        }
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
        // End to end on today's engine: every cycle resolves its template, lerps, and produces a
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
    // NO PRAISE - the one that matters
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Praise vocabulary. `[OK]` is the warmest thing DroneOS is allowed to say, and "good" is banned
    /// outright rather than pattern-matched on "good job" - a bare "good" is exactly how this leaks,
    /// and the program is authored so it never needs the word (see the remarks in
    /// BuiltInPrograms.Firmware.cs for the four manifest entries deliberately left unused).
    /// </summary>
    private static readonly string[] PraiseVocabulary =
    {
        "good", "well done", "proud", "great", "excellent", "amazing", "perfect",
        "nice", "congrat", "impressive", "praise", "compliment", "lovely", "clever",
        "beautiful", "pretty", "sweet", "brilliant", "wonderful"
    };

    [Fact]
    public void DroneOsNeverPraises()
    {
        // Every authored string in the definition, phrase pools included. The pools can be scanned
        // because the program deliberately declines the four Dronification entries that carry praise
        // vocabulary - a test that has to carve out exceptions is a test that stops catching the
        // regression it exists for.
        foreach (var (label, text) in AuthoredStrings(Program()))
        {
            var lower = text.ToLowerInvariant();

            foreach (var word in PraiseVocabulary)
            {
                Assert.False(lower.Contains(word, StringComparison.Ordinal),
                    $"{label} contains praise vocabulary '{word}': \"{text}\"");
            }
        }
    }

    [Fact]
    public void DroneOsHasNoBubblyPunctuation()
    {
        // No "~", which is Bambi's tell and would read as a different companion entirely. Cheap, and
        // it catches a copy pass that pasted a line in from another program.
        foreach (var (label, text) in AuthoredStrings(Program()))
        {
            Assert.False(text.Contains('~'), $"{label} contains a tilde: \"{text}\"");
        }
    }

    [Fact]
    public void TheOnlyPlaceColdnessIsNamedIsTheSafetyNote()
    {
        // The safety note is the single out-of-character string in the program, and content brief
        // safety note 3 requires it to say the quiet part out loud: this program never encourages
        // you, that is deliberate, and nobody should find that out on cycle 6. It is excluded from
        // the praise scan precisely because it has to be able to *name* the thing it is warning
        // about, so it gets its own assertion instead of a silent exemption.
        var note = Program().SafetyNote;

        Assert.False(string.IsNullOrWhiteSpace(note));
        Assert.Contains("[OK]", note);
        Assert.Contains("Withdraw", note);
        Assert.Contains("cold", note.ToLowerInvariant());
    }

    // -------------------------------------------------------------------------------------------
    // Vocabulary rule
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void EverySubliminalIsADronificationSubliminal()
    {
        var pool = BuiltInMods.Dronification.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryLockCardIsADronificationLockCard()
    {
        var pool = BuiltInMods.Dronification.LockCardPhrases!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.LockCardPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void EveryFloatingDirectiveIsADronificationPhrase()
    {
        // Dronification ships no BouncingTextPool, so - exactly as First Week does for Bambi - the
        // floating directives are taken verbatim from its RandomFloating list.
        Assert.True(BuiltInMods.Dronification.Phrases!.TryGetValue("RandomFloating", out var floating));
        var pool = floating!.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Program().Templates)
        {
            foreach (var phrase in template.Floor.BouncingTextPhrases ?? new List<string>())
            {
                Assert.Contains(phrase, pool);
            }
        }
    }

    [Fact]
    public void TheProtocolLockCycle3NamesIsTheOneItActuallyRuns()
    {
        // Cycle 3's copy names "I AM A UNIT". Asserted against the built session rather than the
        // template, because that is what reaches the Unit - a template pool that later grows would
        // silently turn a named string into a shuffle.
        var program = Program();
        var day = program.GetDay(3);

        Assert.NotNull(day);
        Assert.Contains("I AM A UNIT", day!.Tasks.Single().Description);

        var session = ProgramSessionBuilder.Build(program, day);

        Assert.True(session.Settings.LockCardEnabled);
        Assert.Equal(new[] { "I AM A UNIT" }, session.Settings.LockCardPhrases);
    }

    [Fact]
    public void PhrasePoolsAreNonEmptyWhereverTheFeatureIsOn()
    {
        // SessionEngine.ApplySessionSettings only overrides the live pool when the feature is on, so
        // an enabled feature with an empty pool silently falls back to the user's own dashboard
        // phrases - which is how a Dronification program ends up whispering Bambi's lines.
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
        // the user's dashboard value. That is correct for features the program does not run, and a
        // bug for features it does - a verified task must not depend on a number the user can change.
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
    /// Every player-visible string the definition authors, labelled so a failure names its source.
    /// The safety note is excluded - see <see cref="TheOnlyPlaceColdnessIsNamedIsTheSafetyNote"/>.
    /// </summary>
    private static IEnumerable<(string Label, string Text)> AuthoredStrings(ProgramDefinition program)
    {
        yield return ("Title", program.Title);
        yield return ("Subtitle", program.Subtitle);
        yield return ("Pitch", program.Pitch);
        yield return ("ContractPhrase", program.ContractPhrase);

        foreach (var template in program.Templates)
        {
            yield return ($"template {template.Id} name", template.Name);
            yield return ($"template {template.Id} description", template.Description);

            foreach (var phrase in template.Floor.SubliminalPhrases ?? new List<string>())
                yield return ($"template {template.Id} subliminal", phrase);

            foreach (var phrase in template.Floor.LockCardPhrases ?? new List<string>())
                yield return ($"template {template.Id} lock card", phrase);

            foreach (var phrase in template.Floor.BouncingTextPhrases ?? new List<string>())
                yield return ($"template {template.Id} floating directive", phrase);
        }

        foreach (var chapter in program.Chapters)
        {
            yield return ($"module {chapter.Id} name", chapter.Name);
            yield return ($"module {chapter.Id} subtitle", chapter.Subtitle);

            if (!string.IsNullOrEmpty(chapter.RewardDescription))
                yield return ($"module {chapter.Id} reward", chapter.RewardDescription);
        }

        foreach (var day in program.AllDays)
        {
            yield return ($"cycle {day.DayIndex} title", day.Title);
            yield return ($"cycle {day.DayIndex} blurb", day.Blurb);

            if (!string.IsNullOrEmpty(day.RewardDescription))
                yield return ($"cycle {day.DayIndex} reward", day.RewardDescription);

            if (day.Ambient != null)
                yield return ($"cycle {day.DayIndex} ambient", day.Ambient.Description);

            foreach (var task in day.Tasks)
                yield return ($"cycle {day.DayIndex} task '{task.Id}'", task.Description);
        }
    }
}
