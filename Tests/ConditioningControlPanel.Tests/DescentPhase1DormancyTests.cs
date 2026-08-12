using System;
using System.Linq;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Events;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE DESCENT, PHASE 1 — the five "S" items, and the one property all of them share:
/// with no event running and no date window authored, every seam added for the Descent
/// resolves to exactly the answer the app gave before it existed.
///
/// These tests are mostly about DORMANCY rather than behaviour. The behaviour half
/// cannot ship until the server grows an events block and the embed route deploys; what
/// can be proved today is that nothing lit up early.
/// </summary>
public class DescentPhase1DormancyTests
{
    // ============================ event XP boost (§14 row 2) ============================

    [Fact]
    public void XpBoost_IsZero_WhenNoEventRunning()
        => Assert.Equal(0.0, LiveEventService.ClampXpBoost(new LiveEventService().XpBoost));

    [Fact]
    public void XpBoost_ClampsToTheCeiling()
        => Assert.Equal(LiveEventService.MaxXpBoost, LiveEventService.ClampXpBoost(99.0));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    public void XpBoost_MalformedOrNegative_CollapsesToZero(double value)
        => Assert.Equal(0.0, LiveEventService.ClampXpBoost(value));

    [Fact]
    public void XpBoost_PassesThroughUnderTheCap()
        => Assert.Equal(0.10, LiveEventService.ClampXpBoost(0.10));

    [Fact]
    public void Apply_ThenClear_ReturnsToDormant()
    {
        var svc = new LiveEventService();
        svc.Apply("snowglobe", "#88CCFF", 5.0);
        Assert.True(svc.IsActive);
        Assert.Equal(LiveEventService.MaxXpBoost, svc.XpBoost);   // clamped on the way in

        svc.Clear();
        Assert.False(svc.IsActive);
        Assert.Null(svc.SkinId);
        Assert.Null(svc.AccentHex);
        Assert.Equal(0.0, svc.XpBoost);
    }

    [Fact]
    public void NewService_IsDormant()
    {
        var svc = new LiveEventService();
        Assert.False(svc.IsActive);
        Assert.Null(svc.SkinId);
        Assert.Null(svc.AccentHex);
        Assert.Equal(0.0, svc.XpBoost);
    }

    // ============================ event palette (§14 row 4) ============================

    [Fact]
    public void Palette_EventUnset_LeavesTheModChainExactlyAsItWas()
        => Assert.Equal("#00FF41", ModService.ResolveEventThemeHex(null, "#00FF41"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Palette_BlankEvent_FallsThroughLikeNull(string ev)
        => Assert.Equal("#00FF41", ModService.ResolveEventThemeHex(ev, "#00FF41"));

    [Fact]
    public void Palette_EventSet_OutranksTheMod()
        => Assert.Equal("#88CCFF", ModService.ResolveEventThemeHex("#88CCFF", "#00FF41"));

    [Fact]
    public void Palette_ModChain_IsUntouchedBehindTheNewLink()
    {
        // The pre-existing three-link chain must still answer identically; the event link
        // sits in front of it rather than replacing any part of it.
        Assert.Equal("#112233", ModService.ResolveFxSlotHex(null, "#112233", "#445566"));
        Assert.Equal("#FF69B4", ModService.ResolveFxSlotHex(null, null, null));
    }

    [Theory]
    [InlineData("#88CCFF")]          // canonical
    [InlineData("  #88ccff  ")]      // trimmed + upper-cased
    public void EventHex_AcceptsSixDigitRrggbb(string input)
        => Assert.Equal("#88CCFF", LiveEventService.NormalizeHex(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("88CCFF")]           // no hash
    [InlineData("#88CCF")]           // too short
    [InlineData("#FF88CCFF")]        // eight digits: ModService.ParseHexColor would return hot pink
    [InlineData("#88CCFZ")]          // not hex
    [InlineData("rebeccapurple")]
    public void EventHex_RejectsAnythingElse_SoThePaletteFallsThrough(string? input)
        => Assert.Null(LiveEventService.NormalizeHex(input));

    [Fact]
    public void ShadeHex_DerivesLightAndDarkRimsFromTheEventAccent()
    {
        Assert.Equal("#FFFFFF", ModService.ShadeHex("#FFFFFF", 1.18));   // clamps, never wraps
        Assert.Equal("#000000", ModService.ShadeHex("#000000", 0.82));
        Assert.Equal("#808080", ModService.ShadeHex("#808080", 1.0));    // identity
    }

    // ============================ event bubble skin (§14 row 1) ============================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SkinId_BlankIsNoSkin(string? id)
        => Assert.Null(LiveEventService.NormalizeId(id));

    [Fact]
    public void SkinId_IsTrimmed()
        => Assert.Equal("snowglobe", LiveEventService.NormalizeId("  snowglobe  "));

    [Fact]
    public void ResourceResolver_WithNoEvent_StillResolvesTheEmbeddedBubble()
    {
        // App.LiveEvent is null in the test host, which is the same answer as "dormant":
        // the event probe is skipped and the mod-then-embedded chain runs unchanged.
        Assert.NotNull(ModResourceResolver.ResolveImage("bubble.png"));
        Assert.StartsWith("pack://", ModResourceResolver.ResolveUri("bubble.png"));
        Assert.False(ModResourceResolver.HasModOverride("bubble.png"));
    }

    // ============================ quest date window (§14 row 3) ============================

    private static readonly DateTime Today = new(2026, 8, 12);

    [Fact]
    public void Window_NoBounds_IsAlwaysActive()
        => Assert.True(QuestService.IsQuestInDateWindow(null, null, Today));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("12/08/2026")]      // ambiguous culture format: NOT a constraint
    [InlineData("2026-13-45")]      // out of range
    public void Window_UnparseableBound_FailsOpen(string bound)
    {
        Assert.True(QuestService.IsQuestInDateWindow(bound, null, Today));
        Assert.True(QuestService.IsQuestInDateWindow(null, bound, Today));
    }

    [Fact]
    public void Window_BeforeActiveFrom_IsHidden()
        => Assert.False(QuestService.IsQuestInDateWindow("2026-08-13", null, Today));

    [Fact]
    public void Window_OnActiveFrom_IsActive()
        => Assert.True(QuestService.IsQuestInDateWindow("2026-08-12", null, Today));

    [Fact]
    public void Window_AfterActiveUntil_IsHidden()
        => Assert.False(QuestService.IsQuestInDateWindow(null, "2026-08-11", Today));

    [Fact]
    public void Window_OnActiveUntil_IsStillActive_BoundIsInclusive()
        => Assert.True(QuestService.IsQuestInDateWindow(null, "2026-08-12", Today));

    [Fact]
    public void Window_InsideBothBounds_IsActive()
        => Assert.True(QuestService.IsQuestInDateWindow("2026-08-01", "2026-08-31", Today));

    [Fact]
    public void Window_InvertedBounds_HideTheQuest_RatherThanThrow()
        => Assert.False(QuestService.IsQuestInDateWindow("2026-09-01", "2026-07-01", Today));

    [Fact]
    public void Window_IsANoOp_ForEveryQuestThatShipsInThisBuild()
    {
        // The whole safety claim for this item in one assertion: no embedded quest carries
        // a window, so the new filter cannot remove any of them on any date.
        var all = QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, q =>
        {
            Assert.Null(q.ActiveFrom);
            Assert.Null(q.ActiveUntil);
            Assert.True(QuestService.IsQuestInDateWindow(q.ActiveFrom, q.ActiveUntil, Today));
            Assert.True(QuestService.IsQuestInDateWindow(q.ActiveFrom, q.ActiveUntil, new DateTime(2099, 1, 1)));
        });
    }

    // ============================ spiral rail (§9) ============================

    [Fact]
    public void SpiralRail_IsOffByDefault()
        => Assert.False(new AppSettings().DescentSpiralRailEnabled);

    [Theory]
    [InlineData(0, "·")]      // the real pre-begin rung draws a dot, never "0"
    [InlineData(1, "I")]
    [InlineData(7, "VII")]
    [InlineData(8, "VIII")]
    [InlineData(9, "9")]      // past the authored ladder: a numeral, not a wrong Roman
    public void SpiralRail_StageBadge_NeverInventsStageCopy(int n, string expected)
        => Assert.Equal(expected, SpiralRailHost.StageNumeral(n));

    [Fact]
    public void SpiralEmbed_UrlCarriesTheMode_AndOnlyTheEmbedOrigin()
    {
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=mini", SpiralEmbedView.BuildUrl("mini"));
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=map", SpiralEmbedView.BuildUrl("map"));
        // An unknown mode must not reach the wire as-is.
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=mini", SpiralEmbedView.BuildUrl("nonsense"));
    }
}
