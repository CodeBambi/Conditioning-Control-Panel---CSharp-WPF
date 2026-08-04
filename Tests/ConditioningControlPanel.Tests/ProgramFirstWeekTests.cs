using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// First Week is the free 7-day funnel: the conversion path, and the app's only tutorial. It is also
/// the reference implementation three other programs are being authored against, so the things that
/// were wrong with it are the things that will be copied.
///
/// These tests pin the design decisions that are invisible in the source and easy to undo by accident.
/// The load-bearing one is <see cref="EveryDaySessionLeavesItsPromisedFeaturesOn"/>: SessionEngine's
/// ApplySessionSettings does not merely omit a disabled feature, it writes false into live AppSettings
/// and stops the service, so a day whose blurb asks for something its template disables actively
/// *prevents* it for the length of the session. That shipped on day 1 - blurb said "pop a few bubbles",
/// BwDrift.Floor had BubblesEnabled = false - which meant the first minute of the free funnel read as
/// a broken app. Nothing here talks to App; everything is pure data plus ProgramSessionBuilder.
/// </summary>
public class ProgramFirstWeekTests
{
    private static ProgramDefinition Def() => BuiltInPrograms.FirstWeek();

    private static Session BuildDay(int dayIndex)
    {
        var program = Def();
        var day = program.GetDay(dayIndex);
        Assert.NotNull(day);
        return ProgramSessionBuilder.Build(program, day!);
    }

    private static SessionSettings Settings(int dayIndex) => BuildDay(dayIndex).Settings;

    private static IEnumerable<ProgramDay> Days() => Def().AllDays;

    // -----------------------------------------------------------------------------------------
    // Structure
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void FirstWeekValidates()
    {
        Assert.True(Def().Validate(out var error), error);
    }

    [Fact]
    public void FirstWeekIsSevenFreeDaysInOneChapter()
    {
        var program = Def();

        Assert.Equal(7, program.LengthDays);
        Assert.Equal(7, program.AllDays.Count());
        Assert.Single(program.Chapters);
        Assert.Equal(ProgramTier.Free, program.Tier);
        Assert.Equal(BuiltInMods.BambiSleepId, program.ModId);

        // The free tier's safety panel and its one-day-off allowance are policy, not tuning. The owner
        // scales DaysOffAllowed at one per seven days of length, which leaves a 7-day program at 1.
        Assert.False(string.IsNullOrWhiteSpace(program.SafetyNote));
        Assert.Equal(1, program.Rules.DaysOffAllowed);
        Assert.Equal(90, program.Rules.MaxDailyMinutes);
    }

    /// <summary>
    /// The curve is a sawtooth, not a ramp: day 6 sits *below* day 5. Content brief 1.1 mandates a
    /// deload in every program, and the version of First Week without one had day 6 as an accidental
    /// flat spot - the cost of a deload (a day with no progress) with none of the benefit (framed
    /// relief). A 7-day program is not exempt; it needs it more, because day 6 is the day before the
    /// boss and the highest-churn day in the week.
    /// </summary>
    [Fact]
    public void IntensityCurveIsTheAuthoredSawtooth()
    {
        var expected = new[] { 0.05, 0.12, 0.22, 0.32, 0.45, 0.38, 0.75 };

        Assert.Equal(expected, Days().Select(d => d.Intensity).ToArray());
    }

    [Fact]
    public void DaySixIsARealDeloadAndDaySevenIsThePeak()
    {
        var days = Days().ToList();

        var day5 = days[4];
        var day6 = days[5];
        var day7 = days[6];

        // Lower intensity *and* shorter, so the relief is perceptible and not a rounding artefact.
        Assert.True(day6.Intensity < day5.Intensity, "Day 6 must sit below day 5 on the curve.");
        Assert.True(day6.SessionMinutes < day5.SessionMinutes, "Day 6 must also be shorter than day 5.");

        // The boss ignores the deload and is the week's peak.
        Assert.True(day7.IsBoss);
        Assert.Equal(day7.Intensity, days.Max(d => d.Intensity));
        Assert.Equal(1, days.Count(d => d.IsBoss));
    }

    [Fact]
    public void DurationsAreQuantisedAndUnderTheDailyCap()
    {
        var allowed = new[] { 30, 45, 60, 75 };

        foreach (var day in Days())
        {
            Assert.Contains(day.SessionMinutes, allowed);
            Assert.True(day.SessionMinutes <= 90, $"Day {day.DayIndex} exceeds the 90 minute cap.");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Finding 1 - task/session coherence. The one that must never regress.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// For every day, exactly which of the five stompable features its session leaves running. Pinning
    /// the whole grid rather than just day 1 means any future edit that switches a feature off on a day
    /// whose copy or task depends on it fails here with the day number in the message.
    ///
    /// Read the "false" cells as deliberate: those are the days where the task is hand-taught outside
    /// the session and the blurb says so out loud ("while she's running the screen belongs to her").
    /// </summary>
    [Theory]
    // day, bubbles, lockCard, pink, bubbleCount, video
    [InlineData(1, true, false, false, false, false)]
    [InlineData(2, true, false, false, false, false)]
    [InlineData(3, true, false, false, false, false)]
    [InlineData(4, true, false, false, false, false)]
    [InlineData(5, true, false, true, false, false)]
    [InlineData(6, true, true, true, false, false)]
    [InlineData(7, true, true, true, true, true)]
    public void EveryDaySessionLeavesItsPromisedFeaturesOn(
        int dayIndex, bool bubbles, bool lockCard, bool pink, bool bubbleCount, bool video)
    {
        var s = Settings(dayIndex);

        Assert.Equal(bubbles, s.BubblesEnabled);
        Assert.Equal(lockCard, s.LockCardEnabled);
        Assert.Equal(pink, s.PinkFilterEnabled);
        Assert.Equal(bubbleCount, s.BubbleCountEnabled);
        Assert.Equal(video, s.MandatoryVideosEnabled);
    }

    /// <summary>
    /// Day 1's blurb tells a first-time user to pop twenty bubbles, and day 1's task counts them. The
    /// session therefore has to run bubbles *from minute zero*: the delayed-start path writes live
    /// BubblesEnabled = false at t=0 (SessionEngine.cs:1053), so any non-zero start minute makes the
    /// bubbles a user already had on screen vanish for that many minutes.
    /// </summary>
    [Fact]
    public void DayOneRunsBubblesFromMinuteZeroAndCanFinishItsOwnTask()
    {
        var day = Def().GetDay(1)!;
        var s = Settings(1);

        Assert.True(s.BubblesEnabled);
        Assert.Equal(0, s.BubblesStartMinute);
        Assert.False(s.BubblesIntermittent);

        var target = day.Tasks.Single(t => t.Verifier == QuestCategory.Bubbles).TargetValue;

        // BubblesFrequency is bubbles per minute (BubbleService.cs:187), so the session offers
        // frequency x minutes. Day 1 must offer more than its own task asks for, with slack for the
        // ones that drift off screen unpopped.
        var offered = s.BubblesFrequency * (day.SessionMinutes - s.BubblesStartMinute);
        Assert.True(offered >= target,
            $"Day 1 offers {offered} bubbles but its task asks for {target}.");
    }

    /// <summary>
    /// No template may use the delayed bubble start, for the reason above. This is a whole-program rule
    /// rather than a day-1 special case, because the same stop-then-restart is just as confusing on day
    /// 5 as on day 1 - it is only *fatal* on day 1.
    /// </summary>
    [Fact]
    public void NoTemplateDelaysBubbles()
    {
        foreach (var template in Def().Templates)
        {
            Assert.Equal(0, template.Floor.BubblesStartMinute);
            Assert.Equal(0, template.Ceiling.BubblesStartMinute);
        }
    }

    /// <summary>
    /// LockCardFrequency is per *hour* (LockCardService.cs:68-78: interval = 60/frequency, jittered
    /// +/-30%). Day 6's whole point is that the first lock card the program ever fires lands near the
    /// end of a 30-minute session, so the worst-case interval has to fit inside the session. At
    /// frequency 1 - the obvious value - the first card would have been scheduled 42 to 78 minutes out,
    /// i.e. never, and the day's task would be uncompletable in-session.
    /// </summary>
    [Fact]
    public void DaySixLockCardIsScheduledToActuallyFire()
    {
        var day = Def().GetDay(6)!;
        var s = Settings(6);

        Assert.True(s.LockCardEnabled);
        Assert.NotNull(s.LockCardFrequency);

        var worstCaseInterval = (60.0 / s.LockCardFrequency!.Value) * 1.3;
        var latestFirstCard = s.LockCardStartMinute + worstCaseInterval;

        Assert.True(latestFirstCard <= day.SessionMinutes,
            $"Day 6's first lock card can land as late as minute {latestFirstCard:F1} of {day.SessionMinutes}.");
    }

    /// <summary>
    /// Day 7's task asks for three lock cards and the boss should be completable inside the boss. At
    /// the old 1 -> 4 floor/ceiling pair day 7 landed 3/hour, which expects ~2.6 cards over the 52
    /// minutes after the first is scheduled.
    /// </summary>
    [Fact]
    public void DaySevenSchedulesEnoughLockCardsForItsOwnTask()
    {
        var day = Def().GetDay(7)!;
        var s = Settings(7);
        var target = day.Tasks.Single(t => t.Verifier == QuestCategory.LockCard).TargetValue;

        var interval = 60.0 / s.LockCardFrequency!.Value;
        var expectedCards = (day.SessionMinutes - s.LockCardStartMinute) / interval;

        Assert.True(expectedCards >= target,
            $"Day 7 expects {expectedCards:F1} lock cards for a task that asks for {target}.");
    }

    // -----------------------------------------------------------------------------------------
    // Finding 2 - Overrides are the only way a repeated template can be a different day
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// LerpSettings takes every boolean from Floor, so intensity can never turn a feature on. The three
    /// days that reuse yesterday's template (2, 4, 6) therefore *must* carry an Overrides entry or they
    /// are repeats - which is exactly what they were.
    /// </summary>
    [Fact]
    public void TheThreeRepeatedTemplateDaysCarryOverrides()
    {
        var program = Def();

        foreach (var dayIndex in new[] { 2, 4, 6 })
        {
            var day = program.GetDay(dayIndex)!;
            var previous = program.GetDay(dayIndex - 1)!;

            Assert.Equal(previous.SessionTemplateId, day.SessionTemplateId);
            Assert.NotNull(day.Overrides);
            Assert.NotEmpty(day.Overrides!);
        }
    }

    /// <summary>Every override key has to name a real writable SessionSettings property, or it is silently dropped with a log warning.</summary>
    [Fact]
    public void EveryOverrideKeyNamesARealSettingsProperty()
    {
        var known = typeof(SessionSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var day in Days())
        {
            foreach (var key in day.Overrides?.Keys ?? Enumerable.Empty<string>())
            {
                Assert.True(known.Contains(key),
                    $"Day {day.DayIndex} overrides unknown SessionSettings field '{key}'.");
            }
        }
    }

    /// <summary>Each override day adds something its predecessor did not have. Named individually so a failure says which beat was lost.</summary>
    [Fact]
    public void DayTwoAddsHerFloatingWords()
    {
        Assert.False(Settings(1).BouncingTextEnabled);
        Assert.True(Settings(2).BouncingTextEnabled);

        // Late and dim, in the last third of a 30-minute session - a taste, so day 3's full-session
        // bouncing text is an escalation rather than an introduction.
        var s = Settings(2);
        Assert.InRange(s.BouncingTextStartMinute, 18, 26);
        Assert.True(s.BouncingTextSpeed <= Settings(3).BouncingTextSpeed);
        Assert.True(s.BouncingTextOpacity < Settings(3).BouncingTextOpacity);
        Assert.NotEmpty(s.BouncingTextPhrases);
    }

    [Fact]
    public void DayFourAddsFlashAudioAndRehearsesDayFive()
    {
        Assert.False(Settings(3).FlashAudioEnabled);
        Assert.True(Settings(4).FlashAudioEnabled);

        // BW-Pink switches it on permanently from day 5; day 4 is the rehearsal that keeps day 7's
        // "you've done every piece of this" honest.
        Assert.True(Settings(5).FlashAudioEnabled);
    }

    [Fact]
    public void DaySixIsTheRevealAsWellAsTheDeload()
    {
        var before = Settings(5);
        var s = Settings(6);

        Assert.False(before.LockCardEnabled);
        Assert.False(before.MindWipeEnabled);
        Assert.False(before.FlashHydra);

        Assert.True(s.LockCardEnabled);
        Assert.True(s.MindWipeEnabled);
        Assert.True(s.FlashHydra);

        // Mind wipe in session mode plays (multiplier + elapsed/5min) times per five-minute block
        // (MindWipeService.cs:463-471). A multiplier of 1 over the last few minutes is a coin flip on
        // whether the user hears anything at all, and a rehearsal that might not happen is not a
        // rehearsal - so it has to be at least 2, and quieter than day 7's.
        Assert.True(s.MindWipeBaseMultiplier >= 2);
        Assert.True(s.MindWipeStartMinute < Def().GetDay(6)!.SessionMinutes);
        Assert.True(s.MindWipeVolume < Settings(7).MindWipeVolume);
        Assert.True(s.MindWipeBaseMultiplier < Settings(7).MindWipeBaseMultiplier);

        // Still the free tier's optional real-world ritual, plus one non-optional task so the day
        // cannot be completed by sitting through a repeat of day 5.
        var day6 = Def().GetDay(6)!;
        Assert.Contains(day6.Tasks, t => t.Kind == ProgramTaskKind.Ritual && t.Optional);
        Assert.Contains(day6.Tasks, t => t.Kind == ProgramTaskKind.AutoVerified && !t.Optional);
    }

    // -----------------------------------------------------------------------------------------
    // Findings 5 and 6 - perceptibility and the one-step-ahead ladder
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The hand-taught-then-delivered ladder. Every one of these five pairings is real and must stay
    /// real: the user meets the feature by hand on the "taught" day, then finds it already running on
    /// the "delivered" day. Bubbles are the deliberate exception on the taught side - day 1's session
    /// runs them too, because withholding them is not "unassisted", it is the app stopping the service
    /// (see EveryDaySessionLeavesItsPromisedFeaturesOn) - so day 3 is where the *rate* escalates.
    /// </summary>
    [Theory]
    [InlineData(QuestCategory.Bubbles, 1, 1)]
    [InlineData(QuestCategory.LockCard, 2, 6)]
    [InlineData(QuestCategory.PinkFilter, 3, 5)]
    [InlineData(QuestCategory.BubbleCount, 4, 7)]
    [InlineData(QuestCategory.Video, 5, 7)]
    public void OneStepAheadPairingsHold(QuestCategory feature, int taughtDay, int deliveredDay)
    {
        var program = Def();

        // The task that teaches it by hand is on the day claimed.
        Assert.Contains(program.GetDay(taughtDay)!.Tasks, t => t.Verifier == feature);

        // And the first day whose *session* runs it is the day claimed.
        var firstRunningDay = Enumerable.Range(1, 7).First(d => IsFeatureOn(Settings(d), feature));
        Assert.Equal(deliveredDay, firstRunningDay);

        Assert.True(taughtDay <= deliveredDay,
            $"{feature} is delivered on day {deliveredDay} before it is ever taught (day {taughtDay}).");
    }

    private static bool IsFeatureOn(SessionSettings s, QuestCategory feature) => feature switch
    {
        QuestCategory.Bubbles => s.BubblesEnabled,
        QuestCategory.LockCard => s.LockCardEnabled,
        QuestCategory.PinkFilter => s.PinkFilterEnabled,
        QuestCategory.BubbleCount => s.BubbleCountEnabled,
        QuestCategory.Video => s.MandatoryVideosEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(feature))
    };

    /// <summary>
    /// FlashImages is the knob deliberately laddered across the whole week rather than inside a
    /// template, because it is the single most visible number in the file. A pair of floor/ceiling
    /// edits that made day 5 land below day 4 would read as the program going backwards.
    /// </summary>
    [Fact]
    public void FlashImagesLadderNeverGoesBackwards()
    {
        var laddered = Enumerable.Range(1, 7).Select(d => Settings(d).FlashImages).ToArray();

        Assert.Equal(new[] { 1, 1, 2, 2, 3, 3, 4 }, laddered);
    }

    /// <summary>
    /// Days 2, 4 and 6 reuse a template, so intensity alone has to do the work on the numbers - and it
    /// can only do it if the floor/ceiling pair puts a rounding boundary inside the band. Six of
    /// BW-Focus's knobs used to round to the same integer on both the days it runs. Note that pulling a
    /// ceiling *in* shrinks the step: it is (ceiling - floor) x (t2 - t1), so the fix is a wider range,
    /// not a narrower one.
    /// </summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    public void ConsecutiveSameTemplateDaysDifferOnSeveralPerceptibleKnobs(int earlier, int later)
    {
        var a = Settings(earlier);
        var b = Settings(later);

        var stepped = new List<string>();
        if (a.FlashPerHour != b.FlashPerHour) stepped.Add(nameof(a.FlashPerHour));
        if (a.FlashOpacity != b.FlashOpacity) stepped.Add(nameof(a.FlashOpacity));
        if (a.SubliminalPerMin != b.SubliminalPerMin) stepped.Add(nameof(a.SubliminalPerMin));
        if (a.WhisperVolume != b.WhisperVolume) stepped.Add(nameof(a.WhisperVolume));
        if (a.BubblesFrequency != b.BubblesFrequency) stepped.Add(nameof(a.BubblesFrequency));
        if (a.BouncingTextSpeed != b.BouncingTextSpeed) stepped.Add(nameof(a.BouncingTextSpeed));

        Assert.True(stepped.Count >= 3,
            $"Days {earlier} and {later} only differ on {stepped.Count} perceptible knob(s): {string.Join(", ", stepped)}.");

        // And subliminals - the one knob that carries the mod's actual words - must always move.
        Assert.True(b.SubliminalPerMin > a.SubliminalPerMin);
    }

    // -----------------------------------------------------------------------------------------
    // Finding 7 - nothing loud arrives unannounced, least of all on the boss
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Day 7's blurb promises the user already knows how to do every part of it. FlashHydra and
    /// MindWipeEnabled both used to appear for the very first time on the boss, which made that a lie
    /// and made the free funnel's climax the user's first encounter with the two loudest effects in the
    /// app, for sixty minutes, with no rehearsal.
    ///
    /// The two features allowed to debut on day 7 are the two the user spent an earlier day doing by
    /// hand - mandatory video (taught day 5) and the bubble count (taught day 4) - and day 7's copy
    /// names both.
    /// </summary>
    [Fact]
    public void NoFeatureDebutsOnTheBossExceptTheTwoHandTaughtOnes()
    {
        var handTaughtDebuts = new HashSet<string>
        {
            nameof(SessionSettings.MandatoryVideosEnabled),
            nameof(SessionSettings.BubbleCountEnabled)
        };

        var boss = Settings(7);
        var earlier = Enumerable.Range(1, 6).Select(Settings).ToList();

        var bools = typeof(SessionSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanRead);

        foreach (var prop in bools)
        {
            if (!(bool)prop.GetValue(boss)!) continue;
            if (handTaughtDebuts.Contains(prop.Name)) continue;

            Assert.True(earlier.Any(s => (bool)prop.GetValue(s)!),
                $"{prop.Name} is on for the first time on the day-7 boss with no earlier rehearsal.");
        }

        var blurb = Def().GetDay(7)!.Blurb;
        Assert.True(blurb.Contains("screens", StringComparison.OrdinalIgnoreCase), "Day 7's blurb must name the video it debuts.");
        Assert.True(blurb.Contains("counting", StringComparison.OrdinalIgnoreCase), "Day 7's blurb must name the bubble count it debuts.");
    }

    /// <summary>
    /// Continuity: a program is a sequence. Every blurb from day 3 on has to name an earlier day it is
    /// paying off or a later day it is setting up, or the mechanic reads as "the task doesn't match the
    /// session", i.e. as a bug rather than as being taught. Weekday names are deliberately *not* used -
    /// enrollment day is arbitrary, so "on Tuesday" is wrong for six users in seven.
    /// </summary>
    [Fact]
    public void BlurbsFromDayThreeOnReferenceANamedDay()
    {
        var named = new[] { "day one", "day two", "day three", "day four", "day five", "day six", "day seven", "yesterday" };
        var weekdays = new[] { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };

        foreach (var day in Days().Where(d => d.DayIndex >= 3))
        {
            var blurb = day.Blurb.ToLowerInvariant();

            Assert.True(named.Any(n => blurb.Contains(n, StringComparison.Ordinal)),
                $"Day {day.DayIndex}'s blurb references no other day, so its place in the week is invisible.");

            foreach (var weekday in weekdays)
            {
                Assert.False(blurb.Contains(weekday, StringComparison.Ordinal),
                    $"Day {day.DayIndex}'s blurb names {weekday}, but enrollment day is arbitrary.");
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Findings 8, 9, 10 - vocabulary and referent discipline
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// BambiSleep's UserTerm is "Bambi" and its CompanionName is "BambiSprite", so "Bambi" refers to
    /// the *user* and only ever to the user. Two blurbs used to make Bambi the speaker ("Today Bambi
    /// wants something small in return", "Bambi's going to check you're still in there. She's nice
    /// about it"), which is the reads-as-a-reskin failure the file's docstring exists to prevent - and
    /// it breaks harder on a mod swap, because MakeModAware rewrites trigger strings and nothing
    /// rewrites prose. The rule enforced here: in blurb copy the term appears only as a form of
    /// address, i.e. immediately after ", ".
    /// </summary>
    [Fact]
    public void TheUserTermIsOnlyEverUsedToAddressTheUser()
    {
        var userTerm = BuiltInMods.BambiSleep.Identity!.UserTerm ?? "";
        Assert.Equal("Bambi", userTerm);

        foreach (var day in Days())
        {
            foreach (var text in new[] { day.Title, day.Blurb })
            {
                var index = text.IndexOf(userTerm, StringComparison.Ordinal);
                while (index >= 0)
                {
                    Assert.True(index >= 2 && text.Substring(index - 2, 2) == ", ",
                        $"Day {day.DayIndex} uses '{userTerm}' as something other than a form of address: \"{text}\"");

                    var next = index + userTerm.Length;
                    if (next >= text.Length) break;
                    index = text.IndexOf(userTerm, next, StringComparison.Ordinal);
                }
            }
        }
    }

    /// <summary>Every subliminal on every template is byte-exact in the Bambi Sleep pool. Nothing is invented.</summary>
    [Fact]
    public void EverySubliminalIsByteExactInTheManifestPool()
    {
        var pool = BuiltInMods.BambiSleep.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Def().Templates)
        {
            foreach (var phrase in template.Floor.SubliminalPhrases)
            {
                Assert.True(pool.Contains(phrase),
                    $"Template {template.Id} ships subliminal '{phrase}', which is not in BambiSleep.SubliminalPool.");
            }
        }
    }

    /// <summary>
    /// The stated exclusion rule: First Week has to be safe to screenshot, so the five explicit pool
    /// entries are held for the premium 28-day program - which is where the content brief has the arc
    /// end on BAMBI CUM AND COLLAPSE. This used to be four withheld and one (DROP FOR COCK) shipping on
    /// day 7, which was neither screenshot-safe nor the mod's full voice.
    /// </summary>
    [Fact]
    public void TheFiveExplicitSubliminalsAreHeldBackAndTheOtherSixteenAreAllUsed()
    {
        var held = new HashSet<string>(StringComparer.Ordinal)
        {
            "DROP FOR COCK",
            "ZAP COCK DRAIN OBEY",
            "COCK ZOMBIE NOW",
            "COCK TURNS MY BRAIN OFF",
            "BAMBI CUM AND COLLAPSE"
        };

        // Every held phrase really is in the manifest - otherwise this test passes vacuously.
        var pool = BuiltInMods.BambiSleep.SubliminalPool!.Keys.ToHashSet(StringComparer.Ordinal);
        Assert.All(held, h => Assert.Contains(h, pool));

        var used = Def().Templates
            .SelectMany(t => t.Floor.SubliminalPhrases)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(used.Intersect(held, StringComparer.Ordinal));

        // And the rule cuts both ways: the boss uses the whole screenshot-safe pool, so the omission
        // list is a decision rather than an accident of hand-maintained subsets.
        var safe = pool.Except(held, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(safe.OrderBy(x => x, StringComparer.Ordinal), used.OrderBy(x => x, StringComparer.Ordinal));

        var deep = Def().GetTemplate("BW-Deep")!.Floor.SubliminalPhrases.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(safe.OrderBy(x => x, StringComparer.Ordinal), deep.OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Lock card phrases are byte-exact in the manifest's five, on every template - including the ones where the flag is off, because day 6 flips one on.</summary>
    [Fact]
    public void EveryLockCardPhraseIsByteExactInTheManifestPool()
    {
        var pool = BuiltInMods.BambiSleep.LockCardPhrases!.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var template in Def().Templates)
        {
            Assert.NotEmpty(template.Floor.LockCardPhrases);

            foreach (var phrase in template.Floor.LockCardPhrases)
            {
                Assert.True(pool.Contains(phrase),
                    $"Template {template.Id} ships lock card '{phrase}', which is not in BambiSleep.LockCardPhrases.");
            }
        }
    }

    /// <summary>
    /// BambiSleep has no BouncingTextPool, so the bouncing lines are derived from its RandomFloating and
    /// Idle phrase lists - byte-exact, not "sentence-cased", which the docstring used to claim. And no
    /// asterisk stage direction: "Good girl! *giggles*" is a speech-bubble convention that renders as
    /// literal asterisks when it drifts across the screen.
    /// </summary>
    [Fact]
    public void EveryBouncingTextLineIsByteExactAndCarriesNoStageDirection()
    {
        var phrases = BuiltInMods.BambiSleep.Phrases!;
        var pool = phrases["RandomFloating"].Concat(phrases["Idle"]).ToHashSet(StringComparer.Ordinal);

        var lines = Def().Templates
            .SelectMany(t => t.Floor.BouncingTextPhrases)
            .ToList();

        Assert.NotEmpty(lines);

        foreach (var line in lines)
        {
            Assert.True(pool.Contains(line),
                $"Bouncing text line '{line}' is not byte-exact in BambiSleep's RandomFloating/Idle pools.");

            Assert.False(line.Contains('*'), $"Bouncing text line '{line}' carries an asterisk stage direction.");
        }
    }

    // -----------------------------------------------------------------------------------------
    // Achievement collision screen
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// AchievementService.TrackSessionComplete matches built-in sessions by lowercase substring, so a
    /// day title or template name containing one of those substrings would silently unlock someone
    /// else's achievement. ProgramSessionBuilder screens the generated name; this checks the authored
    /// prose never puts it in the position of having to.
    /// </summary>
    [Fact]
    public void NoAuthoredNameTripsTheReservedAchievementSubstrings()
    {
        var program = Def();

        foreach (var template in program.Templates)
        {
            Assert.False(ProgramSessionBuilder.ContainsReserved(template.Name),
                $"Template name '{template.Name}' collides with an achievement substring.");
        }

        foreach (var day in program.AllDays)
        {
            Assert.False(ProgramSessionBuilder.ContainsReserved(day.Title),
                $"Day {day.DayIndex} title '{day.Title}' collides with an achievement substring.");

            var name = ProgramSessionBuilder.BuildSessionName(program, day, program.GetTemplate(day.SessionTemplateId)!);
            Assert.False(ProgramSessionBuilder.ContainsReserved(name),
                $"Day {day.DayIndex} generates the reserved session name '{name}'.");
        }
    }

    /// <summary>Fresh graph every call - a shared static one would let one enrollment's mutated Overrides leak into the next.</summary>
    [Fact]
    public void EachCallReturnsAFreshGraph()
    {
        var a = BuiltInPrograms.FirstWeek();
        var b = BuiltInPrograms.FirstWeek();

        Assert.NotSame(a, b);
        Assert.NotSame(a.GetDay(6), b.GetDay(6));
        Assert.NotSame(a.GetDay(6)!.Overrides, b.GetDay(6)!.Overrides);
        Assert.NotSame(a.GetTemplate("BW-Deep")!.Floor.SubliminalPhrases,
                       b.GetTemplate("BW-Deep")!.Floor.SubliminalPhrases);
    }

    // -----------------------------------------------------------------------------------------
    // The authored numbers themselves, pinned. These are the values the per-template comments in
    // BuiltInPrograms.cs quote and the values the day blurbs promise out loud ("twelve minutes in").
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void DayFiveLandsPinkOnMinuteTwelveAndTheSpiralOnMinuteTwenty()
    {
        var s = Settings(5);

        // Both are jittered +/-3 by the engine at session start; these are the centres of the windows,
        // authored backwards from the beat. Day 5's blurb says "twelve minutes in" in so many words,
        // and the content brief asks for the spiral at minute 20.
        Assert.Equal(12, s.PinkFilterStartMinute);
        Assert.Equal(20, s.SpiralStartMinute);
        Assert.True(Def().GetDay(5)!.Blurb.Contains("twelve minutes in", StringComparison.OrdinalIgnoreCase),
            "Day 5's blurb promises the minute the pink filter arrives; the number and the copy must agree.");
    }

    [Fact]
    public void FlashRateNeverDropsExceptOnTheDeload()
    {
        var perHour = Enumerable.Range(1, 7).Select(d => Settings(d).FlashPerHour).ToArray();

        for (int i = 1; i < perHour.Length; i++)
        {
            // Day 6 (index 5) is the deload and is *supposed* to drop.
            if (i == 5)
            {
                Assert.True(perHour[i] < perHour[i - 1], "Day 6 is the deload; its flash rate must drop.");
                continue;
            }

            Assert.True(perHour[i] > perHour[i - 1],
                $"Day {i + 1} has a flash rate of {perHour[i]}/hr against day {i}'s {perHour[i - 1]}/hr.");
        }
    }
}
