using System;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Controls.Leash.Explain;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The first-time rule for the leash explainer and the explainer's copy. The owner's ask: show it
/// the first time someone uses or receives the leash, before they decide, in two lines, and say
/// plainly that the leashed side can cut it any time.
/// </summary>
public class LeashIntroRuleTests
{
    [Theory]
    [InlineData(LeashIntroSide.Holder, false, false, true)]
    [InlineData(LeashIntroSide.Holder, true, false, false)]
    [InlineData(LeashIntroSide.Holder, false, true, true)]
    [InlineData(LeashIntroSide.Leashed, false, false, true)]
    [InlineData(LeashIntroSide.Leashed, false, true, false)]
    [InlineData(LeashIntroSide.Leashed, true, false, true)]
    public void EachSideHasItsOwnFlag(LeashIntroSide side, bool seenHolder, bool seenLeashed, bool expected)
        => Assert.Equal(expected, LeashIntroRule.ShouldExplain(side, seenHolder, seenLeashed));

    [Fact]
    public void PutItOnWaitsForTheReadTimeOnAFirstAsk()
    {
        Assert.False(LeashIntroRule.PutItOnEnabled(false, TimeSpan.Zero));
        Assert.False(LeashIntroRule.PutItOnEnabled(false, TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs - 1)));
        Assert.True(LeashIntroRule.PutItOnEnabled(false, TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs)));
    }

    [Fact]
    public void PutItOnIsFreeOnceSeen()
        => Assert.True(LeashIntroRule.PutItOnEnabled(true, TimeSpan.Zero));

    [Fact]
    public void TheReadTimeIsAGlanceNotAPunishment()
    {
        Assert.InRange(LeashIntroRule.AskReadMs, 1500, 4000);
    }

    [Fact]
    public void WaitCountsDownAndNeverGoesNegative()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs), LeashIntroRule.PutItOnWait(false, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs - 1000), LeashIntroRule.PutItOnWait(false, TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.Zero, LeashIntroRule.PutItOnWait(false, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.Zero, LeashIntroRule.PutItOnWait(true, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(LeashIntroRule.AskReadMs), LeashIntroRule.PutItOnWait(false, TimeSpan.FromSeconds(-3)));
    }

    [Theory]
    [InlineData(LeashIntroMoment.OfferSent, LeashIntroSide.Holder)]
    [InlineData(LeashIntroMoment.AskAnswered, LeashIntroSide.Leashed)]
    public void OnlyADecisionMarksSeen(LeashIntroMoment moment, LeashIntroSide side)
        => Assert.Equal(side, LeashIntroRule.MarksSeen(moment));

    [Theory]
    [InlineData(LeashIntroMoment.OfferDismissed)]
    [InlineData(LeashIntroMoment.AskDismissed)]
    public void ClosingWithoutDecidingShowsItAgainNextTime(LeashIntroMoment moment)
    {
        Assert.Null(LeashIntroRule.MarksSeen(moment));
        var s = new AppSettings();
        Assert.False(LeashIntroRule.Record(s, moment));
        Assert.False(s.LeashIntroSeenHolder);
        Assert.False(s.LeashIntroSeenLeashed);
    }

    [Fact]
    public void RecordFlipsOnceAndOnlyItsOwnSide()
    {
        var s = new AppSettings();
        Assert.True(LeashIntroRule.ShouldExplain(s, LeashIntroSide.Holder));
        Assert.True(LeashIntroRule.Record(s, LeashIntroMoment.OfferSent));
        Assert.True(s.LeashIntroSeenHolder);
        Assert.False(s.LeashIntroSeenLeashed);
        Assert.False(LeashIntroRule.Record(s, LeashIntroMoment.OfferSent));
        Assert.False(LeashIntroRule.ShouldExplain(s, LeashIntroSide.Holder));
        Assert.True(LeashIntroRule.ShouldExplain(s, LeashIntroSide.Leashed));

        Assert.True(LeashIntroRule.Record(s, LeashIntroMoment.AskAnswered));
        Assert.False(LeashIntroRule.ShouldExplain(s, LeashIntroSide.Leashed));
    }

    [Fact]
    public void NoSettingsMeansExplain()
    {
        Assert.True(LeashIntroRule.ShouldExplain((AppSettings?)null, LeashIntroSide.Holder));
        Assert.False(LeashIntroRule.Record(null, LeashIntroMoment.OfferSent));
    }

    [Fact]
    public void FreshSettingsHaveBothFlagsOff()
    {
        var s = new AppSettings();
        Assert.False(s.LeashIntroSeenHolder);
        Assert.False(s.LeashIntroSeenLeashed);
    }

    [Fact]
    public void FlagsSurviveASaveRoundTrip()
    {
        var s = new AppSettings { LeashIntroSeenHolder = true, LeashIntroSeenLeashed = true };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.True(back.LeashIntroSeenHolder);
        Assert.True(back.LeashIntroSeenLeashed);
    }

    [Theory]
    [InlineData("Holder", LeashIntroSide.Holder)]
    [InlineData("Offer", LeashIntroSide.Holder)]
    [InlineData("offer", LeashIntroSide.Holder)]
    [InlineData("Leashed", LeashIntroSide.Leashed)]
    [InlineData("Ask", LeashIntroSide.Leashed)]
    [InlineData("Gate", LeashIntroSide.Leashed)]
    [InlineData(null, LeashIntroSide.Leashed)]
    public void SurfaceRolesMapToASide(string? role, LeashIntroSide side)
        => Assert.Equal(side, LeashExplainer.SideFor(role));

    // ---- copy ----------------------------------------------------------------------------------

    private static string[] ExplainKeys => CompanionLocMasters.English.Keys
        .Where(k => k.StartsWith("leash_explain_", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void EveryExplainKeyReachedAllNineLanguages()
    {
        var keys = ExplainKeys;
        Assert.True(keys.Length >= 20, $"expected the explainer's keys in en.json, found {keys.Length}");
        foreach (var language in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(language);
            foreach (var key in keys)
            {
                Assert.True(file.TryGetValue(key, out var v), $"{language}.json is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(v), $"{language}.json '{key}' is empty");
            }
        }
    }

    [Fact]
    public void TheCopyFollowsTheHouseVoice()
    {
        var dom = new Regex(@"\b(dom|sub|domme|master|mistress|slave|owner|pet)s?\b", RegexOptions.IgnoreCase);
        foreach (var language in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(language);
            foreach (var key in ExplainKeys)
            {
                var v = file[key];
                Assert.DoesNotContain('—', v);
                Assert.DoesNotContain('–', v);
                Assert.DoesNotContain('!', v);
                Assert.False(dom.IsMatch(v), $"{language}.json '{key}' uses a Dom/Sub noun: {v}");
            }
        }
    }

    [Fact]
    public void TheCutLineNeverPromisesTimeBack()
    {
        // Chaster time already pushed to a lock stays on the lock after a cut.
        foreach (var key in new[] { "leash_explain_leashed_cut", "leash_explain_holder_cut", "leash_explain_step4_leashed", "leash_explain_step4_holder" })
        {
            var v = CompanionLocMasters.English[key];
            Assert.DoesNotContain("time", v.Replace("any time", ""), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chaster", v, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lock time", v, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheLeashedCutLineNamesTheSafetySteps()
    {
        var v = CompanionLocMasters.English["leash_explain_leashed_cut"];
        Assert.Contains("any time", v);
        Assert.Contains("one click", v);
        Assert.Contains("Strict Lock off", v);
        Assert.Contains("panic key back on", v);
    }

    [Fact]
    public void TheLeashedSeeLineCarriesTheName()
    {
        Assert.Contains("{0}", CompanionLocMasters.English["leash_explain_leashed_see"]);
        var (see, cut) = LeashExplainCard.LinesFor(LeashIntroSide.Leashed, "Vex");
        Assert.StartsWith("Vex ", see);
        Assert.Contains("cut", cut);
        var (anon, _) = LeashExplainCard.LinesFor(LeashIntroSide.Leashed, "  ");
        Assert.StartsWith("They ", anon);
        var (holder, holderCut) = LeashExplainCard.LinesFor(LeashIntroSide.Holder, "Vex");
        Assert.DoesNotContain("Vex", holder);
        Assert.StartsWith("They can cut", holderCut);
    }
}
