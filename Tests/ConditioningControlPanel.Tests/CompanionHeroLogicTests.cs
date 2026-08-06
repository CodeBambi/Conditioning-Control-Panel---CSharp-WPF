using System.Globalization;
using System.Windows;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Logic behind the Companion tab's hero package (Z0 header band + Z1 card + the relationship
/// constellation). Everything here is a pure function or a mock viewmodel, so it runs headless.
///
/// The geometry tests matter more than they look: the constellation's pink run is a star-width
/// column, and a wrong fraction is the kind of bug that renders as "slightly off" forever rather
/// than as a crash.
/// </summary>
public class CompanionHeroLogicTests
{
    // ------------------------------------------------------------------ connector geometry

    [Theory]
    [InlineData(0, 0.00)]
    [InlineData(1, 0.25)]
    [InlineData(2, 0.50)]
    [InlineData(3, 0.75)]
    [InlineData(4, 1.00)]
    public void FillFraction_LandsExactlyOnEachNodeCentre(int stage, double expected)
    {
        // The connector spans first-centre to last-centre, so stage n sits at n/(StageCount-1).
        Assert.Equal(expected, ConstellationFillConverter.FillFraction(stage, isLive: true), 6);
    }

    [Fact]
    public void FillFraction_IsZeroWhileDormant_NoMatterTheStage()
    {
        // Pre-Train-4 the strip is a promise, not a progress bar: nothing may look earned.
        for (int stage = 0; stage < ConstellationMath.StageCount; stage++)
            Assert.Equal(0.0, ConstellationFillConverter.FillFraction(stage, isLive: false), 6);
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(99)]
    public void FillFraction_ClampsOutOfRangeStages(int stage)
    {
        double f = ConstellationFillConverter.FillFraction(stage, isLive: true);
        Assert.InRange(f, 0.0, 1.0);
    }

    [Fact]
    public void Converter_ProducesStarColumnsThatSumToOne()
    {
        var fill = new ConstellationFillConverter();
        var rest = new ConstellationFillConverter { Invert = true };
        object[] values = { 3, true };

        var a = (GridLength)fill.Convert(values, typeof(GridLength), null, CultureInfo.InvariantCulture);
        var b = (GridLength)rest.Convert(values, typeof(GridLength), null, CultureInfo.InvariantCulture);

        Assert.True(a.IsStar);
        Assert.True(b.IsStar);
        Assert.Equal(0.75, a.Value, 6);
        Assert.Equal(1.0, a.Value + b.Value, 6);
    }

    [Fact]
    public void Converter_SurvivesMissingOrJunkValues()
    {
        // MultiBindings hand over UnsetValue while a DataContext is still settling; a converter
        // that throws there takes the whole band down with it.
        var fill = new ConstellationFillConverter();

        var none = (GridLength)fill.Convert(null!, typeof(GridLength), null, CultureInfo.InvariantCulture);
        var junk = (GridLength)fill.Convert(new object[] { DependencyProperty.UnsetValue, "yes" },
                                            typeof(GridLength), null, CultureInfo.InvariantCulture);

        Assert.Equal(0.0, none.Value, 6);
        Assert.Equal(0.0, junk.Value, 6);
    }

    // ------------------------------------------------------------------ node ladder

    [Theory]
    [InlineData(ConstellationNodeState.Filled, "✦")]
    [InlineData(ConstellationNodeState.Current, "★")]
    [InlineData(ConstellationNodeState.Future, "✧")]
    public void NodeGlyphs_FollowTheMockupLadder(ConstellationNodeState state, string glyph)
        => Assert.Equal(glyph, MockRelationshipConstellationVm.GlyphFor(state));

    [Fact]
    public void LiveConstellation_HasOneCurrentNodeAndFilledOnesBehindIt()
    {
        var vm = MockRelationshipConstellationVm.Live();

        Assert.Equal(ConstellationMath.StageCount, vm.Nodes.Count);
        Assert.Equal(ConstellationNodeState.Current, vm.Nodes[vm.CurrentStage].State);

        for (int i = 0; i < vm.Nodes.Count; i++)
        {
            var expected = i < vm.CurrentStage ? ConstellationNodeState.Filled
                         : i == vm.CurrentStage ? ConstellationNodeState.Current
                         : ConstellationNodeState.Future;
            Assert.Equal(expected, vm.Nodes[i].State);
            Assert.Equal(MockRelationshipConstellationVm.GlyphFor(expected), vm.Nodes[i].Glyph);
        }
    }

    [Fact]
    public void DormantConstellation_ShowsNamesButNothingEarned()
    {
        var vm = MockRelationshipConstellationVm.Dormant();

        Assert.All(vm.Nodes, n => Assert.Equal(ConstellationNodeState.Future, n.State));
        Assert.All(vm.Nodes, n => Assert.False(string.IsNullOrWhiteSpace(n.Name)));
        Assert.False(string.IsNullOrWhiteSpace(vm.DormantCopy));
    }

    [Fact]
    public void StageNames_ComeFromTheStagedLocKeys()
    {
        var vm = MockRelationshipConstellationVm.Live();
        for (int i = 0; i < vm.Nodes.Count; i++)
        {
            Assert.Equal(CompanionLocStaging.Resolve($"companion_stage_{i}"), vm.Nodes[i].Name);
        }
    }

    // ------------------------------------------------------------------ hero card viewmodel

    [Fact]
    public void MiniToggles_FlipExactlyOncePerInvocation()
    {
        // The chips bind IsChecked OneWay and act through their command. If someone re-introduces a
        // TwoWay binding the click will land twice and cancel itself out; this pins the contract the
        // XAML depends on — the command is the only writer.
        var vm = MockCompanionHeroCardVm.Default();

        Assert.True(vm.IsCompanionShown);
        vm.ToggleShownCommand.Execute(null);
        Assert.False(vm.IsCompanionShown);

        Assert.False(vm.IsMuted);
        vm.ToggleMuteCommand.Execute(null);
        Assert.True(vm.IsMuted);
    }

    [Fact]
    public void DefaultHero_IsDormantOnMoodAndConstellation_ButOtherwiseFullyAlive()
    {
        var vm = MockCompanionHeroCardVm.Default();

        Assert.True(vm.IsCompanionEnabled);
        Assert.True(vm.IsAiLive);
        Assert.False(vm.IsMoodLive);          // Train 4 has not landed on the artboard state
        Assert.False(vm.Constellation.IsLive);
        Assert.NotNull(vm.Header);
        Assert.True(vm.Header!.HasAiAccess);
    }

    [Fact]
    public void FullyAliveHero_TurnsOnTheMoodTokenAndTheConstellation()
    {
        var vm = MockCompanionHeroCardVm.FullyAlive();

        Assert.True(vm.IsMoodLive);
        Assert.Equal("bratty", vm.MoodWord);
        Assert.True(vm.Constellation.IsLive);
    }

    [Fact]
    public void AsleepHero_KeepsEveryAffordanceItNeedsToWakeUp()
    {
        var vm = MockCompanionHeroCardVm.Asleep();

        Assert.False(vm.IsCompanionEnabled);
        Assert.False(string.IsNullOrWhiteSpace(vm.AsleepCopy));
        Assert.NotNull(vm.WakeCommand);
        Assert.True(vm.WakeCommand.CanExecute(null));
    }

    [Fact]
    public void FreeTierHero_DimsOnlyTheHeaderPlate_TheCardItselfStaysAlive()
    {
        // Design rule §4: barks are free, so the hero never degrades for a free user. The single
        // visible difference is the entitlement plate going dim behind its teaser ribbon.
        var vm = MockCompanionHeroCardVm.FreeTier();

        Assert.NotNull(vm.Header);
        Assert.False(vm.Header!.HasAiAccess);
        Assert.False(string.IsNullOrWhiteSpace(vm.Header.TeaserRibbonLabel));

        Assert.True(vm.IsCompanionEnabled);
        Assert.False(string.IsNullOrWhiteSpace(vm.Name));
        Assert.False(string.IsNullOrWhiteSpace(vm.Flavor));
        Assert.Equal(5, vm.Constellation.Nodes.Count);
    }

    [Fact]
    public void HeroWithoutAHeader_IsLegal_SoAHostCanDrawItsOwn()
    {
        // ICompanionHeroCardVm.Header defaults to null precisely so this stays an additive change
        // to a contract other packages implement.
        Assert.Null(MockCompanionHeroCardVm.NoHeader().Header);
    }

    [Fact]
    public void HeaderCopy_ResolvesThroughTheStagedLocTable()
    {
        var header = MockCompanionHeaderVm.Entitled();

        Assert.Equal(CompanionLocStaging.Resolve("companion_header_title"), header.Title);
        Assert.Equal(CompanionLocStaging.Resolve("companion_header_subtitle"), header.Subtitle);
        Assert.Equal(CompanionLocStaging.Resolve("companion_header_plate_ai"), header.AiPlateLabel);
        Assert.NotEqual("companion_header_title", header.Title);   // i.e. the key really is staged
    }

    // ------------------------------------------------------------------ gallery wiring

    [Theory]
    [InlineData("hero.default")]
    [InlineData("hero.fullyAlive")]
    [InlineData("hero.asleep")]
    [InlineData("hero.aiOff")]
    [InlineData("hero.freshUser")]
    [InlineData("hero.freeTier")]
    [InlineData("hero.noHeader")]
    public void EveryHeroExhibit_ResolvesToAHeroViewmodel(string key)
        => Assert.IsAssignableFrom<ICompanionHeroCardVm>(CompanionMockGallery.Get(key)!);

    [Theory]
    [InlineData("constellation.live")]
    [InlineData("constellation.dormant")]
    [InlineData("constellation.freshlyMet")]
    [InlineData("constellation.inevitable")]
    public void EveryConstellationExhibit_ResolvesToAConstellationViewmodel(string key)
        => Assert.IsAssignableFrom<IRelationshipConstellationVm>(CompanionMockGallery.Get(key)!);
}
