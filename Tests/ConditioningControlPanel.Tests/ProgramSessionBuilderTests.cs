using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The curve that turns four authored templates into twenty-eight days of session. Three things
/// here are load-bearing.
///
/// First, the split between what lerps and what does not: numbers ride the intensity curve, but
/// booleans, enums and phrase pools come from Floor, because which *features* a day runs is the
/// template's identity. If a boolean ever started lerping, day 1 of a beginner program would
/// silently switch on whatever the ceiling had enabled.
///
/// Second, isolation. One template is reused for every day of a run, so the builder must hand back
/// a fresh object with copied lists; a shared List&lt;string&gt; means editing day 3 mutates the
/// template and therefore days 4 through 28 as well - a bug that would only show up mid-run.
/// Nullable ints the floor leaves null must also stay null, because those fields fall through to the
/// user's own dashboard values and writing a number over them is a silent override of user settings.
///
/// Third, name safety. AchievementService.TrackSessionComplete identifies built-in sessions by
/// lowercase substring match ("morning drift", "gamer girl", "distant doll", "good girls"), so a
/// program day innocently titled "Morning Drift" would falsely unlock somebody's achievement.
/// The generated name has to be screened, not merely prefixed.
///
/// All static; the only App touch is a null-safe App.Logger warning on a bad override key.
/// </summary>
public class ProgramSessionBuilderTests
{
    // ---- fixtures ----

    private static SessionSettings Floor() => new()
    {
        FlashEnabled = true,
        FlashPerHour = 10,
        FlashOpacity = 40,
        SpiralOpacity = 10,
        SpiralEnabled = true,
        FlashHydra = false,
        CornerGifPath = "floor.gif",
        CornerGifPosition = CornerPosition.BottomLeft,
        RampCurve = ConditioningControlPanel.Models.RampCurve.EaseIn,
        SubliminalPhrases = new List<string> { "floor phrase" },
        VideosPerHour = null,             // left to the user's own dashboard value
        LockCardFrequency = 2
    };

    private static SessionSettings Ceiling() => new()
    {
        FlashEnabled = false,             // deliberately opposite - must NOT win
        FlashPerHour = 30,
        FlashOpacity = 100,
        SpiralOpacity = 15,               // 10 -> 15 puts the midpoint on a .5 for the rounding check
        SpiralEnabled = false,
        FlashHydra = true,
        CornerGifPath = "ceiling.gif",
        CornerGifPosition = CornerPosition.TopRight,
        RampCurve = ConditioningControlPanel.Models.RampCurve.Exponential,
        SubliminalPhrases = new List<string> { "ceiling phrase", "another" },
        VideosPerHour = 12,
        LockCardFrequency = 10
    };

    private static ProgramSessionTemplate Template(string id = "tpl-induction", string name = "Induction") => new()
    {
        Id = id,
        Name = name,
        Description = "Template description.",
        Floor = Floor(),
        Ceiling = Ceiling()
    };

    private static ProgramDefinition MakeProgram(string title = "Test Program", params ProgramDay[] days) => new()
    {
        Id = "test-program",
        Title = title,
        Icon = "*",
        LengthDays = days.Length,
        Templates = new List<ProgramSessionTemplate> { Template() },
        Chapters = new List<ProgramChapter>
        {
            new() { Id = "chapter-1", Name = "Opening", Days = days.ToList() }
        }
    };

    private static ProgramDay Day(int index, string title, double intensity = 0.5, int minutes = 45) => new()
    {
        DayIndex = index,
        Title = title,
        Blurb = "Soft and slow.",
        SessionTemplateId = "tpl-induction",
        SessionMinutes = minutes,
        Intensity = intensity
    };

    // ---- LerpSettings: the curve ----

    [Fact]
    public void LerpAtZero_ReturnsFloorNumbers()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        Assert.Equal(10, result.FlashPerHour);
        Assert.Equal(40, result.FlashOpacity);
        Assert.Equal(10, result.SpiralOpacity);
        Assert.Equal(2, result.LockCardFrequency);
    }

    [Fact]
    public void LerpAtOne_ReturnsCeilingNumbers()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);

        Assert.Equal(30, result.FlashPerHour);
        Assert.Equal(100, result.FlashOpacity);
        Assert.Equal(15, result.SpiralOpacity);
        Assert.Equal(10, result.LockCardFrequency);
    }

    [Fact]
    public void LerpAtHalf_IsTheMidpointRounded()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.5);

        Assert.Equal(20, result.FlashPerHour);   // 10 -> 30
        Assert.Equal(70, result.FlashOpacity);   // 40 -> 100
        Assert.Equal(13, result.SpiralOpacity);  // 10 -> 15 = 12.5, rounded away from zero
        Assert.Equal(6, result.LockCardFrequency);
    }

    [Fact]
    public void OutOfRangeIntensity_IsClampedNotExtrapolated()
    {
        // A hand-edited program.json with intensity 3 must not produce a flash rate three times the
        // authored ceiling.
        var over = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 4.0);
        var under = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), -2.0);

        Assert.Equal(30, over.FlashPerHour);
        Assert.Equal(10, under.FlashPerHour);
    }

    [Fact]
    public void BooleansComeFromFloorEvenAtFullIntensity()
    {
        // Which features run is the template's identity, not something the curve decides.
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);

        Assert.True(result.FlashEnabled);     // ceiling says false
        Assert.True(result.SpiralEnabled);    // ceiling says false
        Assert.False(result.FlashHydra);      // ceiling says true
    }

    [Fact]
    public void EnumsAndStringsComeFromFloorEvenAtFullIntensity()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);

        Assert.Equal(CornerPosition.BottomLeft, result.CornerGifPosition);
        Assert.Equal("floor.gif", result.CornerGifPath);
        Assert.Equal(ConditioningControlPanel.Models.RampCurve.EaseIn, result.RampCurve);
    }

    [Fact]
    public void PhraseListsComeFromFloorEvenAtFullIntensity()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);

        Assert.Equal(new[] { "floor phrase" }, result.SubliminalPhrases.ToArray());
    }

    [Fact]
    public void PhraseListIsACopy_NotSharedWithTheTemplate()
    {
        // The template is reused for every day of the run; a shared list means mutating day 3's
        // settings silently rewrites days 4..28.
        var floor = Floor();
        var result = ProgramSessionBuilder.LerpSettings(floor, Ceiling(), 0.5);

        Assert.NotSame(floor.SubliminalPhrases, result.SubliminalPhrases);

        result.SubliminalPhrases.Add("added during the run");

        Assert.Single(floor.SubliminalPhrases);
        Assert.Equal("floor phrase", floor.SubliminalPhrases[0]);
    }

    [Fact]
    public void ResultIsAFreshObject_NotTheFloorInstance()
    {
        var floor = Floor();
        var result = ProgramSessionBuilder.LerpSettings(floor, Ceiling(), 1.0);

        Assert.NotSame(floor, result);
        Assert.Equal(10, floor.FlashPerHour);   // lerping did not write back into the template
    }

    [Fact]
    public void NullableIntNullInFloor_StaysNull()
    {
        // These fields fall through to the user's own dashboard values; writing a number over them
        // would be a silent override the user never asked for.
        var atZero = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);
        var atOne = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);

        Assert.Null(atZero.VideosPerHour);
        Assert.Null(atOne.VideosPerHour);   // ceiling has 12 - it must not leak through
    }

    [Fact]
    public void NullableIntSetInBoth_Lerps()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.25);

        Assert.Equal(4, result.LockCardFrequency);   // 2 -> 10 at 0.25
    }

    [Fact]
    public void MissingCeiling_FallsBackToFloor()
    {
        var result = ProgramSessionBuilder.LerpSettings(Floor(), null!, 1.0);

        Assert.Equal(10, result.FlashPerHour);
        Assert.Equal(2, result.LockCardFrequency);
    }

    [Fact]
    public void Clone_IsADeepEnoughCopy()
    {
        var source = Floor();
        var clone = ProgramSessionBuilder.Clone(source);

        Assert.NotSame(source, clone);
        Assert.NotSame(source.SubliminalPhrases, clone.SubliminalPhrases);
        Assert.Equal(source.FlashPerHour, clone.FlashPerHour);
        Assert.Equal(source.CornerGifPath, clone.CornerGifPath);
    }

    // ---- ApplyOverrides: JSON round-trip hazard ----

    /// <summary>Values as they arrive after a .program.json round-trip: boxed JsonElement, not int/bool.</summary>
    private static Dictionary<string, object> AsJsonElements(JsonDocument doc) =>
        doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value);

    [Fact]
    public void Overrides_MaterialiseJsonElementValues()
    {
        // Bare Convert.ChangeType throws on a JsonElement, so every value shape has to be unwrapped
        // explicitly - the same hazard TimelineEvent.GetSetting hit in #429.
        const string json = """
        {
          "flashPerHour": 42,
          "flashHydra": true,
          "cornerGifPath": "override.gif",
          "cornerGifPosition": "TopRight",
          "subliminalPhrases": ["one", "two"],
          "videosPerHour": 6,
          "rampCurve": "SCurve"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        ProgramSessionBuilder.ApplyOverrides(settings, AsJsonElements(doc));

        Assert.Equal(42, settings.FlashPerHour);
        Assert.True(settings.FlashHydra);
        Assert.Equal("override.gif", settings.CornerGifPath);
        Assert.Equal(CornerPosition.TopRight, settings.CornerGifPosition);
        Assert.Equal(new[] { "one", "two" }, settings.SubliminalPhrases.ToArray());
        Assert.Equal(6, settings.VideosPerHour);
        Assert.Equal(ConditioningControlPanel.Models.RampCurve.SCurve, settings.RampCurve);
    }

    [Fact]
    public void Overrides_AreCaseInsensitiveOnTheKey()
    {
        // .program.json is written camelCase but hand-authored files use the C# names.
        const string json = """{ "FlashPerHour": 7 }""";

        using var doc = JsonDocument.Parse(json);
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        ProgramSessionBuilder.ApplyOverrides(settings, AsJsonElements(doc));

        Assert.Equal(7, settings.FlashPerHour);
    }

    [Fact]
    public void Overrides_CanClearANullableBackToNull()
    {
        const string json = """{ "lockCardFrequency": null }""";

        using var doc = JsonDocument.Parse(json);
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 1.0);
        Assert.Equal(10, settings.LockCardFrequency);

        ProgramSessionBuilder.ApplyOverrides(settings, AsJsonElements(doc));

        Assert.Null(settings.LockCardFrequency);
    }

    [Fact]
    public void UnknownOverrideKey_IsIgnoredNotThrown()
    {
        // A community program written against a newer build must not take the whole day down.
        const string json = """{ "notARealSettingsField": 5, "flashPerHour": 25 }""";

        using var doc = JsonDocument.Parse(json);
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        var ex = Record.Exception(() => ProgramSessionBuilder.ApplyOverrides(settings, AsJsonElements(doc)));

        Assert.Null(ex);
        Assert.Equal(25, settings.FlashPerHour);   // the good key still applied
    }

    [Fact]
    public void UnparseableOverrideValue_IsSkippedAndLeavesTheLerpedValue()
    {
        const string json = """{ "rampCurve": "NotACurveAtAll", "flashPerHour": 33 }""";

        using var doc = JsonDocument.Parse(json);
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        var ex = Record.Exception(() => ProgramSessionBuilder.ApplyOverrides(settings, AsJsonElements(doc)));

        Assert.Null(ex);
        Assert.Equal(ConditioningControlPanel.Models.RampCurve.EaseIn, settings.RampCurve);
        Assert.Equal(33, settings.FlashPerHour);
    }

    [Fact]
    public void NullOverrideDictionary_IsANoOp()
    {
        var settings = ProgramSessionBuilder.LerpSettings(Floor(), Ceiling(), 0.0);

        var ex = Record.Exception(() => ProgramSessionBuilder.ApplyOverrides(settings, null!));

        Assert.Null(ex);
        Assert.Equal(10, settings.FlashPerHour);
    }

    // ---- BuildSessionName: achievement-substring safety ----

    [Fact]
    public void OrdinaryDayTitle_AppearsInTheName()
    {
        var day = Day(3, "Bubble Induction");
        var program = MakeProgram("Test Program", day);

        var name = ProgramSessionBuilder.BuildSessionName(program, day, Template());

        Assert.StartsWith(ProgramSessionBuilder.NamePrefix, name);
        Assert.Contains("Day 3", name);
        Assert.Contains("Bubble Induction", name);
    }

    [Fact]
    public void BlankDayTitle_FallsBackToTheTemplateName()
    {
        var day = Day(1, "");
        var program = MakeProgram("Test Program", day);

        var name = ProgramSessionBuilder.BuildSessionName(program, day, Template(name: "Deep Induction"));

        Assert.Contains("Deep Induction", name);
    }

    [Fact]
    public void DayTitleContainingAReservedAchievementSubstring_IsScreenedOut()
    {
        // AchievementService.TrackSessionComplete matches these as lowercase substrings of the
        // session name. A day titled "Morning Drift" would falsely unlock somebody's achievement.
        var reserved = new[] { "morning drift", "gamer girl", "distant doll", "good girls" };

        foreach (var phrase in reserved)
        {
            var title = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(phrase);
            var day = Day(4, title);
            var program = MakeProgram("Test Program", day);

            var name = ProgramSessionBuilder.BuildSessionName(program, day, Template());

            Assert.DoesNotContain(phrase, name.ToLowerInvariant(), StringComparison.Ordinal);
            Assert.Contains("Test Program", name);   // fell back to the authored-text-free form
            Assert.Contains("Day 4", name);
        }
    }

    [Fact]
    public void ReservedSubstringEmbeddedMidTitle_IsAlsoScreened()
    {
        // The match is a substring, not an exact name, so a title that merely contains the phrase is
        // just as dangerous.
        var day = Day(9, "For good girls only");
        var program = MakeProgram("Test Program", day);

        var name = ProgramSessionBuilder.BuildSessionName(program, day, Template());

        Assert.DoesNotContain("good girls", name.ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateNameContainingAReservedSubstring_IsScreenedToo()
    {
        // Blank day title means the template name is what lands in the session name.
        var day = Day(2, "");
        var program = MakeProgram("Test Program", day);

        var name = ProgramSessionBuilder.BuildSessionName(program, day, Template(name: "Gamer Girl Warmup"));

        Assert.DoesNotContain("gamer girl", name.ToLowerInvariant(), StringComparison.Ordinal);
    }

    // ---- Build: end to end ----

    [Fact]
    public void Build_ProducesATransientSessionWithLerpedSettings()
    {
        var day = Day(3, "Bubble Induction", intensity: 0.5, minutes: 45);
        var program = MakeProgram("Test Program", day);

        var session = ProgramSessionBuilder.Build(program, day);

        Assert.Equal("program_test-program_day3", session.Id);
        Assert.Equal(45, session.DurationMinutes);
        Assert.Equal(program.Icon, session.Icon);
        Assert.True(session.IsAvailable);
        Assert.Equal(SessionSource.Custom, session.Source);
        Assert.Equal("Soft and slow.", session.Description);
        Assert.Equal(SessionDifficulty.Medium, session.Difficulty);
        Assert.Equal(20, session.Settings.FlashPerHour);   // lerped at 0.5

        // The editor's representation is deliberately empty - a lerped session carrying the
        // template's un-lerped timeline would display a lie.
        Assert.Empty(session.Phases);
        Assert.Empty(session.TimelineEvents);
    }

    [Fact]
    public void Build_DoesNotHandBackTheTemplatesOwnSettings()
    {
        var day = Day(1, "Opening", intensity: 1.0);
        var program = MakeProgram("Test Program", day);
        var template = program.Templates[0];

        var session = ProgramSessionBuilder.Build(program, day);
        session.Settings.FlashPerHour = 999;
        session.Settings.SubliminalPhrases.Add("day-local phrase");

        Assert.NotSame(template.Floor, session.Settings);
        Assert.NotSame(template.Ceiling, session.Settings);
        Assert.Equal(10, template.Floor.FlashPerHour);
        Assert.Single(template.Floor.SubliminalPhrases);
    }

    [Fact]
    public void Build_ClampsAZeroLengthDayToOneMinute()
    {
        var day = Day(1, "Opening", minutes: 0);
        var program = MakeProgram("Test Program", day);

        var session = ProgramSessionBuilder.Build(program, day);

        Assert.Equal(1, session.DurationMinutes);
    }

    [Fact]
    public void Build_AppliesTheDaysOverridesOnTopOfTheCurve()
    {
        var day = Day(2, "Opening", intensity: 0.0);
        day.Overrides = new Dictionary<string, object> { ["FlashPerHour"] = 55 };
        var program = MakeProgram("Test Program", day);

        var session = ProgramSessionBuilder.Build(program, day);

        Assert.Equal(55, session.Settings.FlashPerHour);
    }

    [Fact]
    public void Build_BossDayEarnsMoreBonusXpThanTheSameDayWouldOtherwise()
    {
        var plain = Day(5, "Opening", intensity: 0.8);
        var boss = Day(5, "Opening", intensity: 0.8);
        boss.IsBoss = true;

        var plainSession = ProgramSessionBuilder.Build(MakeProgram("Test Program", plain), plain);
        var bossSession = ProgramSessionBuilder.Build(MakeProgram("Test Program", boss), boss);

        Assert.True(bossSession.BonusXP > plainSession.BonusXP);
    }

    [Fact]
    public void Build_UnknownTemplate_ThrowsRatherThanSilentlyRunningADefaultSession()
    {
        var day = Day(1, "Opening");
        day.SessionTemplateId = "tpl-missing";
        var program = MakeProgram("Test Program", day);

        var ex = Assert.Throws<InvalidOperationException>(() => ProgramSessionBuilder.Build(program, day));
        Assert.Contains("tpl-missing", ex.Message);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var day = Day(1, "Opening");
        var program = MakeProgram("Test Program", day);

        Assert.Throws<ArgumentNullException>(() => ProgramSessionBuilder.Build(null!, day));
        Assert.Throws<ArgumentNullException>(() => ProgramSessionBuilder.Build(program, null!));
    }
}
