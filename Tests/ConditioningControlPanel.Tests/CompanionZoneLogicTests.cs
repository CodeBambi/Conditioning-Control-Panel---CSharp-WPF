using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Pins the decision logic behind the redesigned Companion tab's zones
/// (<c>ConditioningControlPanel/Views/Controls/Companion/</c>).
///
/// The XAML is deliberately dumb: it draws whatever the viewmodel hands it. Everything that can
/// actually be wrong lives in three small pure helpers, and this suite is where they are held
/// still:
///
/// <list type="bullet">
///   <item><see cref="AttentionCopy"/> — the Z6 budget ladder. Getting a threshold wrong here
///   means telling a user with plenty of budget left that she is running out of energy, or
///   showing the Patreon upsell to someone at full, which is the exact "budget backlash" the
///   design set out to avoid.</item>
///   <item><see cref="ConstellationMath"/> — Z1's five-stage ratchet. An off-by-one lights the
///   wrong star, which is the retention spine's whole readout.</item>
///   <item><see cref="FactOrdering"/> — Z3's wall projection. Boundaries MUST sort first: a
///   remembered "never tease me about X" that scrolls off the bottom is a consent-hygiene
///   failure, not a cosmetic one.</item>
/// </list>
///
/// No WPF window is created (the converters are pure), so these run on any thread.
/// </summary>
public class CompanionZoneLogicTests
{
    // =====================================================================================
    //  Z6 — attention meter copy ladder
    // =====================================================================================

    [Theory]
    [InlineData(1.00, AttentionMood.Plenty)]
    [InlineData(0.72, AttentionMood.Plenty)]
    [InlineData(0.40, AttentionMood.Plenty)]      // boundary is inclusive at the top
    [InlineData(0.3999, AttentionMood.Saving)]
    [InlineData(0.30, AttentionMood.Saving)]
    [InlineData(0.15, AttentionMood.Saving)]      // ditto
    [InlineData(0.1499, AttentionMood.Whispering)]
    [InlineData(0.01, AttentionMood.Whispering)]
    [InlineData(0.0, AttentionMood.Spent)]
    public void AttentionMood_LaddersAtTheDesignedThresholds(double fraction, AttentionMood expected)
        => Assert.Equal(expected, AttentionCopy.MoodFor(fraction));

    [Theory]
    [InlineData(-5.0, AttentionMood.Spent)]
    [InlineData(4.2, AttentionMood.Plenty)]
    [InlineData(double.NaN, AttentionMood.Spent)]
    [InlineData(double.PositiveInfinity, AttentionMood.Spent)]
    public void AttentionMood_ClampsGarbageInsteadOfThrowing(double fraction, AttentionMood expected)
        => Assert.Equal(expected, AttentionCopy.MoodFor(fraction));

    [Fact]
    public void AttentionCopy_UsesOneKeyPerRung_AndAllOfThemAreStaged()
    {
        var keys = new[] { 1.0, 0.30, 0.08, 0.0 }.Select(AttentionCopy.CopyKeyFor).ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var key in keys)
        {
            Assert.True(CompanionLocStaging.English.ContainsKey(key),
                $"attention ladder key '{key}' has no EN master in the staging file");
        }
    }

    [Fact]
    public void AttentionUpsell_AppearsOnlyBelowForty()
    {
        Assert.False(AttentionCopy.ShowUpsell(1.0));
        Assert.False(AttentionCopy.ShowUpsell(0.40));   // exactly at the line: still quiet
        Assert.True(AttentionCopy.ShowUpsell(0.3999));
        Assert.True(AttentionCopy.ShowUpsell(0.0));
    }

    [Fact]
    public void SpentMeter_KeepsASliverSoItReadsEmptyRatherThanBroken()
    {
        Assert.Equal(0.04, AttentionCopy.BarFractionFor(0.0), 6);
        Assert.Equal(0.04, AttentionCopy.BarFractionFor(-1.0), 6);
        // anything non-zero draws itself, unmolested
        Assert.Equal(0.72, AttentionCopy.BarFractionFor(0.72), 6);
    }

    [Fact]
    public void AttentionVm_DerivesEveryVisibleFieldFromTheFraction()
    {
        var drained = MockAttentionGaugeVm.Drained();
        Assert.True(drained.ShowUpsell);
        Assert.Equal(0.04, drained.BarFraction, 6);
        Assert.Equal(CompanionLocStaging.English["companion_attention_spent"], drained.StateCopy);

        var plenty = MockAttentionGaugeVm.Plenty();
        Assert.False(plenty.ShowUpsell);
        Assert.Equal(CompanionLocStaging.English["companion_attention_plenty"], plenty.StateCopy);
    }

    // =====================================================================================
    //  Z1 — relationship constellation
    // =====================================================================================

    [Fact]
    public void ConstellationStates_FillBehind_GoldOnCurrent_OutlineAhead()
    {
        var states = Enumerable.Range(0, ConstellationMath.StageCount)
                               .Select(i => ConstellationMath.StateFor(i, currentStage: 2, isLive: true))
                               .ToArray();

        Assert.Equal(new[]
        {
            ConstellationNodeState.Filled,
            ConstellationNodeState.Filled,
            ConstellationNodeState.Current,
            ConstellationNodeState.Future,
            ConstellationNodeState.Future
        }, states);
    }

    [Fact]
    public void DormantConstellation_HasNoFilledOrCurrentNode()
    {
        // Pre-Train 4 the strip is a promise, not a progress bar: names visible, nothing lit.
        for (int i = 0; i < ConstellationMath.StageCount; i++)
        {
            Assert.Equal(ConstellationNodeState.Future,
                ConstellationMath.StateFor(i, currentStage: 3, isLive: false));
        }
        Assert.Equal(0.0, ConstellationMath.ConnectorFraction(3, isLive: false));
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(99, 4)]
    public void ClampStage_NeverEscapesTheFiveStages(int input, int expected)
        => Assert.Equal(expected, ConstellationMath.ClampStage(input));

    [Fact]
    public void ConnectorFraction_GrowsWithTheStage_AndStaysInsideTheStrip()
    {
        double previous = -1;
        for (int stage = 0; stage < ConstellationMath.StageCount; stage++)
        {
            double f = ConstellationMath.ConnectorFraction(stage, isLive: true);
            Assert.InRange(f, 0.0, 1.0);
            Assert.True(f > previous, $"connector must advance at stage {stage}");
            previous = f;
        }
    }

    [Fact]
    public void StageKey_TakesAPerModOverride()
    {
        Assert.Equal("companion_stage_2", ConstellationMath.StageKey(2));
        Assert.Equal("companion_stage_2_sissy", ConstellationMath.StageKey(2, "sissy"));
        // out-of-range indices still produce a valid key rather than a broken one
        Assert.Equal("companion_stage_4", ConstellationMath.StageKey(17));
    }

    [Fact]
    public void EveryStageNameHasAnEnMaster()
    {
        for (int i = 0; i < ConstellationMath.StageCount; i++)
        {
            Assert.True(CompanionLocStaging.English.ContainsKey(ConstellationMath.StageKey(i)),
                $"stage {i} has no EN master");
        }
    }

    // =====================================================================================
    //  Z3 — fact wall projection
    // =====================================================================================

    private static CompanionMemoryFact Fact(string text, string kind,
        bool boundary = false, bool pinned = false, bool dormant = false)
        => new()
        {
            Text = text,
            KindKey = kind,
            KindLabel = kind,
            MetaLabel = string.Empty,
            IsBoundary = boundary,
            IsPinned = pinned,
            IsDormant = dormant
        };

    [Fact]
    public void BoundariesSortFirst_ThenPinned_ThenTheRest_ThenTheDormantPromise()
    {
        var wall = new List<IMemoryFactVm>
        {
            Fact("a joke", "joke"),
            Fact("soon…", "all", dormant: true),
            Fact("a pinned moment", "moment", pinned: true),
            Fact("never tease about chastity", "boundary", boundary: true),
            Fact("a preference", "preference")
        };

        var projected = FactOrdering.Project(wall, "all").Select(f => f.Text).ToArray();

        Assert.Equal(new[]
        {
            "never tease about chastity",
            "a pinned moment",
            "a joke",
            "a preference",
            "soon…"
        }, projected);
    }

    [Fact]
    public void ProjectionIsStable_SoTheCallersSalienceOrderSurvives()
    {
        var wall = new List<IMemoryFactVm>
        {
            Fact("most salient", "joke"),
            Fact("middle", "joke"),
            Fact("least salient", "joke")
        };

        Assert.Equal(new[] { "most salient", "middle", "least salient" },
            FactOrdering.Project(wall, "all").Select(f => f.Text));
    }

    [Fact]
    public void KindFilter_KeepsItsKind_AndAlwaysKeepsTheDormantCard()
    {
        var wall = new List<IMemoryFactVm>
        {
            Fact("a joke", "joke"),
            Fact("a goal", "goal"),
            Fact("soon…", "all", dormant: true)
        };

        var jokes = FactOrdering.Project(wall, "joke").Select(f => f.Text).ToArray();

        // The promise card belongs to the wall, not to a kind — it must not vanish on filter.
        Assert.Equal(new[] { "a joke", "soon…" }, jokes);
    }

    [Fact]
    public void Project_ToleratesNullAndEmptyInput()
    {
        Assert.Empty(FactOrdering.Project(null, "all"));
        Assert.Empty(FactOrdering.Project(new List<IMemoryFactVm>(), "boundary"));
    }

    [Theory]
    [InlineData("joke", null, true)]
    [InlineData("joke", "", true)]
    [InlineData("joke", "all", true)]
    [InlineData("joke", "ALL", true)]
    [InlineData("joke", "joke", true)]
    [InlineData("joke", "goal", false)]
    public void Passes_TreatsAllAndBlankAsTheUnfilteredChip(string kind, string? filter, bool expected)
        => Assert.Equal(expected, FactOrdering.Passes(kind, filter));

    [Fact]
    public void SelectingAFilterChip_ReprojectsTheWall_AndClearsTheOtherChips()
    {
        var vm = MockMemoryDiaryVm.Populated();
        Assert.Equal("all", vm.SelectedFilterKey);

        var boundaryChip = vm.Filters.Single(f => f.Key == "boundary");
        boundaryChip.IsSelected = true;

        Assert.Equal("boundary", vm.SelectedFilterKey);
        Assert.Single(vm.Filters.Where(f => f.IsSelected));
        // boundary card + the always-present dormant promise
        Assert.All(vm.Facts, f => Assert.True(f.IsBoundary || f.IsDormant));
    }

    [Fact]
    public void EmptyWall_IsStillNotBlank_AndKeepsItsProfileStrip()
    {
        var vm = MockMemoryDiaryVm.Empty();
        Assert.True(vm.IsEmpty);
        // level and streak exist from minute one — the strip is the "60% of the feeling" surface
        Assert.NotEmpty(vm.ProfileStats);
    }

    // =====================================================================================
    //  converters
    // =====================================================================================

    [Theory]
    [InlineData(0.62, 0.62)]
    [InlineData(0.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, 1.0)]
    [InlineData(-1.0, 0.0)]
    public void FractionToStar_ProducesAStarGridLength(double input, double expected)
    {
        var result = (GridLength)new FractionToStarConverter()
            .Convert(input, typeof(GridLength), null, CultureInfo.InvariantCulture);

        Assert.True(result.IsStar);
        Assert.Equal(expected, result.Value, 6);
    }

    [Fact]
    public void FractionToStar_AndItsInverse_AlwaysSumToOne()
    {
        var fill = new FractionToStarConverter();
        var rest = new FractionToStarConverter { Invert = true };

        foreach (double f in new[] { 0.0, 0.13, 0.5, 0.87, 1.0 })
        {
            var a = (GridLength)fill.Convert(f, typeof(GridLength), null, CultureInfo.InvariantCulture);
            var b = (GridLength)rest.Convert(f, typeof(GridLength), null, CultureInfo.InvariantCulture);
            Assert.Equal(1.0, a.Value + b.Value, 6);
        }
    }

    [Fact]
    public void FractionToStar_TreatsNonNumericInputAsZero()
    {
        var result = (GridLength)new FractionToStarConverter()
            .Convert("not a number", typeof(GridLength), null, CultureInfo.InvariantCulture);
        Assert.Equal(0.0, result.Value, 6);
    }

    [Fact]
    public void EnumEquals_RoundTrips_AndRefusesToClearTheSourceOnUncheck()
    {
        var conv = new CompanionEnumEqualsConverter();

        Assert.Equal(true, conv.Convert(AwarenessIntensity.BroadStrokes, typeof(bool),
            "BroadStrokes", CultureInfo.InvariantCulture));
        Assert.Equal(false, conv.Convert(AwarenessIntensity.Off, typeof(bool),
            "Everything", CultureInfo.InvariantCulture));

        Assert.Equal(AwarenessIntensity.Everything, conv.ConvertBack(true, typeof(AwarenessIntensity),
            "Everything", CultureInfo.InvariantCulture));

        // A radio group unchecks the outgoing button before checking the new one; if that wrote
        // through, the dial would flicker to a bogus value on every change.
        Assert.Same(System.Windows.Data.Binding.DoNothing,
            conv.ConvertBack(false, typeof(AwarenessIntensity), "Everything", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("x", true)]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void HasContent_DrivesTheEmptyStateVisibilities(object? value, bool expected)
    {
        var conv = new CompanionEmptyToVisibilityConverter();
        var vis = (Visibility)conv.Convert(value, typeof(Visibility), null, CultureInfo.InvariantCulture);
        Assert.Equal(expected ? Visibility.Visible : Visibility.Collapsed, vis);
    }

    [Fact]
    public void HasContent_CountsCollections()
    {
        var conv = new CompanionEmptyToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed,
            conv.Convert(Array.Empty<string>(), typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible,
            conv.Convert(new[] { "a" }, typeof(Visibility), null, CultureInfo.InvariantCulture));
    }

    // =====================================================================================
    //  the state gallery
    // =====================================================================================

    [Fact]
    public void EveryGalleryExhibitBuilds()
    {
        foreach (var key in CompanionMockGallery.Exhibits.Keys)
        {
            Assert.NotNull(CompanionMockGallery.Get(key));
        }
    }

    [Theory]
    // Requirement: every user-visible state in the mockup's state gallery is reachable from
    // the mocks, so a builder (or a play-test) can render locked/dormant/empty/drained/disabled
    // without a service, a login, or a train having landed.
    [InlineData("chat.locked")]
    [InlineData("chat.dormant")]
    [InlineData("chat.aiOff")]
    [InlineData("memory.empty")]
    [InlineData("memory.dormant")]
    [InlineData("constellation.dormant")]
    [InlineData("attention.drained")]
    [InlineData("awareness.dormant")]
    [InlineData("personality.dormant")]
    [InlineData("hero.asleep")]
    [InlineData("engine.loggedOut")]
    public void MockGallery_CoversTheDesignedNonHappyStates(string key)
        => Assert.NotNull(CompanionMockGallery.Get(key));

    [Fact]
    public void LockedChat_ShowsAStagedTeaser_AndCannotSend()
    {
        var vm = MockChatThresholdVm.Locked();
        Assert.Equal(CompanionZoneState.Locked, vm.State);
        Assert.False(vm.CanSend);
        Assert.Empty(vm.Turns);
        Assert.NotEmpty(vm.TeaserTurns);
        Assert.False(string.IsNullOrWhiteSpace(vm.LockCopy));
        Assert.False(string.IsNullOrWhiteSpace(vm.LockCtaLabel));
    }

    [Fact]
    public void AiBadgeRidesIsAiGeneratedOnly_NeverABarkEcho()
    {
        var vm = MockChatThresholdVm.Live();
        foreach (var bubble in vm.Turns)
        {
            if (bubble.Kind == CompanionBubbleKind.Echo)
                Assert.False(bubble.IsAiGenerated, "a spoken bark echo must never wear the AI badge");
            if (bubble.Kind == CompanionBubbleKind.You)
                Assert.False(bubble.IsAiGenerated, "the user's own line is not model output");
        }
        Assert.Contains(vm.Turns, b => b.IsAiGenerated);
    }

    [Fact]
    public void DisabledHero_KeepsTheRestOfThePageAlive()
    {
        var hero = MockCompanionHeroCardVm.Asleep();
        Assert.False(hero.IsCompanionEnabled);
        // The asleep state is copy, not a blanked card: she still has a name, a level and an
        // invitation to wake her.
        Assert.False(string.IsNullOrWhiteSpace(hero.AsleepCopy));
        Assert.False(string.IsNullOrWhiteSpace(hero.Name));
        Assert.NotNull(hero.Constellation);
    }
}
