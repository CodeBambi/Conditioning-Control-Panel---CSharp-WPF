using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1306: with page titles allow-listed the companion still only ever talked about switch
/// counts and screen time. The title reached the projection, but the output contract told the model
/// it "cannot see websites, page text, documents" and must "never name one", so every title went
/// unused. The contract now carves out a given title, and the tail says so only when one is there.
/// </summary>
public class AwarenessTitleRuleTests
{
    private static ContextFrame Frame(string? title, string cluster = "site_video") => new()
    {
        AppId = "youtube",
        AppCluster = cluster,
        Category = ActivityCategory.Media,
        ServiceName = "YouTube",
        PageTitleSanitized = title,
        Transition = TransitionKind.NewApp,
        DwellSeconds = 120,
        TimeOfDay = TimeBucket.Afternoon,
        Weekday = DayOfWeek.Monday,
        Tier = RarityTier.Uncommon,
        CutAt = new DateTime(2026, 9, 28, 14, 0, 0, DateTimeKind.Local)
    };

    [Fact]
    public void AnAllowListedTitleIsLicensedInTheTail()
    {
        var tail = AwarenessPromptBuilder.BuildTail(Frame("Knitting for beginners"), local: false, RarityTier.Uncommon);
        Assert.Contains("\"title\":\"Knitting for beginners\"", tail);
        Assert.Contains(AwarenessPromptBuilder.TitleRule, tail);
    }

    [Fact]
    public void NoTitleMeansNoLicence()
    {
        var tail = AwarenessPromptBuilder.BuildTail(Frame(null), local: false, RarityTier.Uncommon);
        Assert.DoesNotContain(AwarenessPromptBuilder.TitleRule, tail);
        Assert.DoesNotContain("\"title\"", tail);
    }

    [Fact]
    public void AnAdultFrameBoundForTheCloudCarriesNoTitleAndNoLicence()
    {
        var frame = Frame("a very specific page", AwarenessClusters.Adult);
        Assert.Null(AwarenessProjection.TitleFor(frame, cloud: true));
        var tail = AwarenessPromptBuilder.BuildTail(frame, local: false, RarityTier.Uncommon);
        Assert.DoesNotContain(AwarenessPromptBuilder.TitleRule, tail);

        // The machine-local path keeps it, and the licence follows the projection.
        var local = AwarenessPromptBuilder.BuildTail(frame, local: true, RarityTier.Uncommon);
        Assert.Contains(AwarenessPromptBuilder.TitleRule, local);
    }

    [Fact]
    public void TheContractNoLongerForbidsAGivenTitle()
    {
        Assert.Contains("except a \"title\" given below", AwarenessPromptBuilder.OutputContract);
    }
}
