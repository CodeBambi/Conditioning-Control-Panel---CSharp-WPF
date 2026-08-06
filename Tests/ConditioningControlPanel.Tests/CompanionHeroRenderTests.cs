using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Render coverage for the hero package's own states — the header band, the free-tier plate, and
/// the constellation's intro animations.
///
/// It lives beside <see cref="CompanionZoneRenderSmokeTests"/> rather than inside it because that
/// file is the shared scaffold's; this one is the hero package's. Same technique: build the control
/// against a mock on an STA thread and force a real measure/arrange, because
/// <c>{StaticResource}</c> and template triggers are resolved at tree-build time, not at compile
/// time — a renamed theme key compiles clean and throws the first time a user opens the tab.
/// </summary>
public class CompanionHeroRenderTests
{
    private static void OnStaThread(Action body)
    {
        Exception? escaped = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { escaped = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    private static T Realize<T>(T control, object dataContext, double width) where T : UserControl
    {
        control.DataContext = dataContext;
        control.Measure(new Size(width, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, control.DesiredSize.Height))));
        control.UpdateLayout();
        Assert.True(control.DesiredSize.Height > 0,
            $"{typeof(T).Name} measured to zero height — its content did not realize");
        return control;
    }

    [Theory]
    [InlineData("hero.freeTier")]
    [InlineData("hero.noHeader")]
    public void HeroCard_RealizesInTheHeaderStates(string exhibit)
        => OnStaThread(() => Realize(new CompanionHeroCard(), CompanionMockGallery.Get(exhibit)!, 1160));

    [Fact]
    public void HeaderBand_CollapsesWhenTheHostSuppliesNoHeader()
    {
        OnStaThread(() =>
        {
            var withHeader = Realize(new CompanionHeroCard(), MockCompanionHeroCardVm.Default(), 1160);
            var without = Realize(new CompanionHeroCard(), MockCompanionHeroCardVm.NoHeader(), 1160);

            var shown = (FrameworkElement)withHeader.FindName("HeaderBand");
            var hidden = (FrameworkElement)without.FindName("HeaderBand");

            Assert.Equal(Visibility.Visible, shown.Visibility);
            Assert.Equal(Visibility.Collapsed, hidden.Visibility);

            // …and the card itself must not shrink away with it: a host-drawn header is a normal
            // configuration, not a degraded one.
            Assert.True(without.DesiredSize.Height > 200);
        });
    }

    [Fact]
    public void HeroCard_AmbientLoopIsSafeToDriveByHand()
    {
        // The tab's single Forever storyboard. Starting it unloaded, parking it, and refreshing it
        // against an asleep companion must all be no-throw — a decorative animation may never be
        // the reason the tab fails to open.
        OnStaThread(() =>
        {
            var card = Realize(new CompanionHeroCard(), MockCompanionHeroCardVm.Default(), 1160);
            card.StartAmbientLoop();
            card.RefreshAmbientState();
            card.StopAmbientLoop();
            card.StopAmbientLoop();

            card.ViewModel = MockCompanionHeroCardVm.Asleep();
            card.RefreshAmbientState();
            card.StopAmbientLoop();
        });
    }

    [Fact]
    public void Constellation_IntroIsSafeToReplayInEveryState()
    {
        // PlayIntro is re-runnable on purpose (the host replays it on a stage-up). Calling it on an
        // unloaded, freshly measured strip is the worst case and must be a quiet no-op.
        OnStaThread(() =>
        {
            foreach (var key in new[] { "constellation.live", "constellation.dormant",
                                        "constellation.freshlyMet", "constellation.inevitable" })
            {
                var band = Realize(new RelationshipConstellation(), CompanionMockGallery.Get(key)!, 900);
                band.PlayIntro();
                band.PlayIntro();
            }
        });
    }

    [Fact]
    public void Constellation_GeneratesOneContainerPerStage()
    {
        // The connector geometry assumes five equal columns. If the strip ever renders a different
        // number of nodes the pink run stops in the wrong place, silently.
        OnStaThread(() =>
        {
            var band = Realize(new RelationshipConstellation(), MockRelationshipConstellationVm.Live(), 900);
            var strip = (ItemsControl)band.FindName("NodeStrip");
            Assert.Equal(ConstellationMath.StageCount, strip.Items.Count);
        });
    }
}
