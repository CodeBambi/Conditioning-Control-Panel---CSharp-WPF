using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Catalogue-level invariants across the whole shipped program library.
///
/// The per-program test files each guard their own content. Nothing guarded the *set* until these:
/// that every program is actually reachable from <see cref="BuiltInPrograms.All"/> (a definition
/// nobody wired in is invisible and there is no compiler error to tell you), that ids do not collide
/// (ProgramService keys enrollments by id, so a duplicate silently resumes the wrong run), and that a
/// policy applied "to every program" really was applied to every program rather than to the two
/// somebody remembered.
///
/// Pure data. No App reads, so these run without a WPF Application.
/// </summary>
public class ProgramLibraryTests
{
    private static IReadOnlyList<ProgramDefinition> Library => BuiltInPrograms.All();

    [Fact]
    public void AllFiveProgramsAreWiredIntoTheLibrary()
    {
        // If this drops back to 1 somebody reverted All() while leaving the definition files on disk -
        // which compiles, ships, and makes four programs unreachable with no error anywhere.
        var ids = Library.Select(p => p.Id).ToList();

        Assert.Equal(5, ids.Count);
        Assert.Contains("first_week", ids);
        Assert.Contains("presentation", ids);
        Assert.Contains("firmware_install", ids);
        Assert.Contains("the_takeover", ids);
        Assert.Contains("kept", ids);
    }

    [Fact]
    public void EveryProgramValidates()
    {
        foreach (var program in Library)
        {
            var ok = program.Validate(out var error);
            Assert.True(ok, $"{program.Id} failed Validate(): {error}");
        }
    }

    [Fact]
    public void ProgramIdsAndTitlesAreUnique()
    {
        // ProgramService keys the active enrollment by program id. A collision means enrolling in one
        // program can resume another's saved progress.
        var ids = Library.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var titles = Library.Select(p => p.Title).ToList();
        Assert.Equal(titles.Count, titles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExactlyOneProgramIsFreeAndItIsTheSevenDayFunnel()
    {
        var free = Library.Where(p => p.Tier == ProgramTier.Free).ToList();

        var single = Assert.Single(free);
        Assert.Equal("first_week", single.Id);
        Assert.Equal(7, single.LengthDays);
    }

    [Fact]
    public void BrowseOrderIsFreeFirstThenShortestCommitmentFirst()
    {
        // Browse order is All()'s order. A browsing user should not be asked to weigh a 28-day
        // commitment before a 14-day one.
        Assert.Equal("first_week", Library[0].Id);

        var paidLengths = Library.Skip(1).Select(p => p.LengthDays).ToList();
        Assert.Equal(paidLengths.OrderBy(n => n).ToList(), paidLengths);
    }

    [Fact]
    public void DaysOffScaleAtOnePerSevenDaysOfLength()
    {
        // Owner decision 2026-07-29, replacing "one day off per program regardless of length": one
        // day off buys four times less forgiveness across 28 days than across 7. Asserted as the
        // rule, not as five literals, so a new program cannot be added without obeying it.
        foreach (var program in Library)
        {
            var expected = program.LengthDays / 7;
            Assert.True(expected >= 1, $"{program.Id} is shorter than a week; the rule needs revisiting.");
            Assert.Equal(expected, program.Rules.DaysOffAllowed);
        }
    }

    [Fact]
    public void EveryProgramCapsTheDayAtNinetyMinutes()
    {
        foreach (var program in Library)
            Assert.Equal(90, program.Rules.MaxDailyMinutes);
    }

    [Fact]
    public void NoDaySessionExceedsTheProgramsOwnCap()
    {
        // Validate() checks the shape of a day but not its session length against the program's cap.
        foreach (var program in Library)
        foreach (var day in program.Chapters.SelectMany(c => c.Days))
        {
            Assert.True(day.SessionMinutes <= program.Rules.MaxDailyMinutes,
                $"{program.Id} day {day.DayIndex} is {day.SessionMinutes}m, over the {program.Rules.MaxDailyMinutes}m cap.");
        }
    }

    [Fact]
    public void EveryProgramIsThemedOnAKnownMod()
    {
        // ModId drives banner art lookup and nothing else - it is deliberately NOT an availability
        // gate (see All()). But a typo'd id silently degrades to the default banner, so pin it.
        var known = new[]
        {
            BuiltInMods.BambiSleepId,
            BuiltInMods.SissyHypnoId,
            BuiltInMods.DronificationId,
            BuiltInMods.LockedId,
            BuiltInMods.CCPDefaultId
        };

        foreach (var program in Library)
            Assert.Contains(program.ModId, known);
    }

    [Fact]
    public void DayIndicesAreContiguousFromOneAcrossChapters()
    {
        foreach (var program in Library)
        {
            var indices = program.Chapters.SelectMany(c => c.Days).Select(d => d.DayIndex).ToList();

            Assert.Equal(program.LengthDays, indices.Count);
            Assert.Equal(Enumerable.Range(1, program.LengthDays).ToList(), indices);
        }
    }

    [Fact]
    public void NoSessionOrTemplateNameCollidesWithAnAchievementSubstring()
    {
        // AchievementService.TrackSessionComplete substring-matches these on the session name, so a
        // program template carrying one silently fires an unrelated achievement. Checked library-wide
        // because each program's own test can only see its own strings.
        var reserved = new[] { "morning drift", "gamer girl", "distant doll", "good girls" };

        foreach (var program in Library)
        {
            foreach (var template in program.Templates)
            foreach (var word in reserved)
            {
                Assert.False(template.Name?.ToLowerInvariant().Contains(word) == true,
                    $"{program.Id} template '{template.Name}' contains reserved '{word}'.");
            }

            foreach (var day in program.Chapters.SelectMany(c => c.Days))
            foreach (var word in reserved)
            {
                Assert.False(day.Title?.ToLowerInvariant().Contains(word) == true,
                    $"{program.Id} day {day.DayIndex} title contains reserved '{word}'.");
            }
        }
    }

    [Fact]
    public void EveryDayResolvesItsSessionTemplate()
    {
        // A day whose template id does not resolve throws when the user presses Start, potentially on
        // day 24 of a run they have already paid for in time.
        foreach (var program in Library)
        {
            var templateIds = program.Templates.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var day in program.Chapters.SelectMany(c => c.Days))
            {
                Assert.True(templateIds.Contains(day.SessionTemplateId),
                    $"{program.Id} day {day.DayIndex} references unknown template '{day.SessionTemplateId}'.");
            }
        }
    }

    [Fact]
    public void TaskIdsAreUniqueWithinEveryDay()
    {
        // The day record keys progress by task id, so two tasks sharing one id share one counter.
        foreach (var program in Library)
        foreach (var day in program.Chapters.SelectMany(c => c.Days))
        {
            var ids = day.Tasks.Select(t => t.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void NoShippedDayAuthorsAFlashRateTheEngineWillClamp()
    {
        // AppSettings.FlashFrequency clamps to 1..180 in its setter, and SessionEngine writes the
        // session's rate through that setter twice - once at start (FlashPerHour) and again every
        // second of the ramp toward FlashPerHourEnd. So anything a template authors above 180 is not
        // "a very high rate", it is 180, silently.
        //
        // That is invisible in the source and invisible in every other test, and it had eaten the
        // entire top of the curve: 43 of the 91 shipped days were over, including every day of both
        // 28-day programs' back halves. Kept days 19-28 all ran at a flat 180 - the finale of a
        // four-week paid program with its escalation deleted by a property setter. The Takeover's
        // chapter 4, the four days a user tells someone else about, were pixel-identical to day 18.
        //
        // Raising the clamp is an owner call. Until then, authoring above it is writing numbers
        // nothing reads, and this test is the thing that says so out loud.
        const int engineClamp = 180;
        var offenders = new List<string>();

        foreach (var program in Library)
        foreach (var day in program.AllDays)
        {
            var settings = ProgramSessionBuilder.LerpSettings(
                program.GetTemplate(day.SessionTemplateId)!.Floor,
                program.GetTemplate(day.SessionTemplateId)!.Ceiling,
                day.Intensity);

            if (day.Overrides is { Count: > 0 })
                ProgramSessionBuilder.ApplyOverrides(settings, day.Overrides);

            if (settings.FlashPerHour > engineClamp || settings.FlashPerHourEnd > engineClamp)
            {
                offenders.Add(
                    $"{program.Id} day {day.DayIndex} ({day.SessionTemplateId} @ i {day.Intensity:0.00}) " +
                    $"lands on {settings.FlashPerHour} -> {settings.FlashPerHourEnd}/hour");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Days authoring a flash rate above the engine's {engineClamp}/hour clamp - the excess is " +
            "discarded by AppSettings.FlashFrequency, so the authored escalation does not happen:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void FlashEscalationIsRealWithinEveryTemplateBand()
    {
        // The companion to the clamp test, and the reason the fix was a re-author rather than a
        // Math.Min. Flooring everything at 180 would have passed the test above while leaving the
        // days flat, which is the bug. So: a template used on more than one day must actually move
        // its flash rate between its lowest and highest intensity day, and the within-session ramp
        // must climb rather than fall.
        foreach (var program in Library)
        {
            var byTemplate = program.AllDays.GroupBy(d => d.SessionTemplateId);

            foreach (var group in byTemplate)
            {
                var template = program.GetTemplate(group.Key)!;

                var landings = group
                    .Select(d => (d.DayIndex, d.Intensity,
                        Settings: ProgramSessionBuilder.LerpSettings(template.Floor, template.Ceiling, d.Intensity)))
                    .ToList();

                foreach (var (dayIndex, _, settings) in landings)
                {
                    Assert.True(settings.FlashPerHour <= settings.FlashPerHourEnd,
                        $"{program.Id} day {dayIndex} ramps flashes DOWNWARD, {settings.FlashPerHour} -> {settings.FlashPerHourEnd}/hour.");
                }

                if (landings.Count < 2) continue;

                var lowest = landings.OrderBy(l => l.Intensity).First();
                var highest = landings.OrderBy(l => l.Intensity).Last();
                if (Math.Abs(highest.Intensity - lowest.Intensity) < 0.001) continue;

                Assert.True(highest.Settings.FlashPerHourEnd > lowest.Settings.FlashPerHourEnd,
                    $"{program.Id} template '{group.Key}' ends on {lowest.Settings.FlashPerHourEnd}/hour at its " +
                    $"lowest day and {highest.Settings.FlashPerHourEnd}/hour at its highest - the band is flat.");
            }
        }
    }

    [Fact]
    public void EveryProgramHasAGraduationBadgeAndAContractPhrase()
    {
        foreach (var program in Library)
        {
            Assert.False(string.IsNullOrWhiteSpace(program.GraduationBadgeId), $"{program.Id} has no graduation badge id.");
            Assert.False(string.IsNullOrWhiteSpace(program.ContractPhrase), $"{program.Id} has no contract phrase.");
            Assert.False(string.IsNullOrWhiteSpace(program.SafetyNote), $"{program.Id} has no safety note.");
        }
    }
}
