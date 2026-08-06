using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Instantiates every Companion-tab zone control against its design-time mock and forces a
/// measure/arrange pass.
///
/// This is the test that a compile does not give you. <c>{StaticResource}</c> is resolved when the
/// tree is built, not when the XAML is compiled, so a renamed or missing key in
/// <c>Themes/CompanionTheme.xaml</c> builds clean and then throws
/// <c>ResourceReferenceKeyNotFoundException</c> the first time a user opens the tab. The same goes
/// for a DataTemplate whose converter is not in scope — the exact Known Issues #4 trap this
/// package avoids by instancing its converters inside the theme dictionary.
///
/// Each case runs on its own STA thread because WPF elements demand one; nothing here needs an
/// Application instance, which keeps the suite headless.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionZoneRenderSmokeTests
{
    /// <summary>
    /// Runs <paramref name="body"/> on a fresh STA thread and rethrows whatever it threw, so a
    /// resource failure surfaces as a normal test failure with its original stack.
    /// </summary>
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

    /// <summary>Builds the control, gives it the mock, and lays it out at a realistic width.</summary>
    private static void Realize(Func<UserControl> factory, object dataContext, double width)
    {
        var control = factory();
        control.DataContext = dataContext;

        // A host window is not needed, but a real layout pass is: measure/arrange is what walks
        // the templates and therefore what resolves every StaticResource in them.
        control.Measure(new Size(width, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, control.DesiredSize.Height))));
        control.UpdateLayout();

        Assert.True(control.DesiredSize.Height > 0,
            $"{control.GetType().Name} measured to zero height — its content did not realize");
    }

    [Theory]
    [InlineData("hero.default")]
    [InlineData("hero.fullyAlive")]
    [InlineData("hero.asleep")]
    [InlineData("hero.aiOff")]
    [InlineData("hero.freshUser")]
    public void HeroCard_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new CompanionHeroCard(), CompanionMockGallery.Get(exhibit)!, 1160));

    [Theory]
    [InlineData("constellation.live")]
    [InlineData("constellation.dormant")]
    [InlineData("constellation.freshlyMet")]
    [InlineData("constellation.inevitable")]
    public void Constellation_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new RelationshipConstellation(), CompanionMockGallery.Get(exhibit)!, 900));

    [Theory]
    [InlineData("chat.live")]
    [InlineData("chat.dormant")]
    [InlineData("chat.aiOff")]
    [InlineData("chat.locked")]
    [InlineData("chat.thinking")]
    public void ChatThreshold_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new ChatThresholdView(), CompanionMockGallery.Get(exhibit)!, 660));

    [Theory]
    [InlineData("memory.populated")]
    [InlineData("memory.empty")]
    [InlineData("memory.dormant")]
    [InlineData("memory.boundariesFilter")]
    public void MemoryDiary_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new MemoryDiaryView(), CompanionMockGallery.Get(exhibit)!, 660));

    [Theory]
    [InlineData("personality.dormant")]
    [InlineData("personality.live")]
    [InlineData("personality.interviewed")]
    [InlineData("personality.handEdited")]
    public void MakeHerYours_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new MakeHerYoursView(), CompanionMockGallery.Get(exhibit)!, 430));

    [Theory]
    [InlineData("awareness.live")]
    [InlineData("awareness.dormant")]
    [InlineData("awareness.eyesClosed")]
    public void AwarenessPrivacy_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new AwarenessPrivacyView(), CompanionMockGallery.Get(exhibit)!, 430));

    [Theory]
    [InlineData("attention.plenty")]
    [InlineData("attention.saving")]
    [InlineData("attention.whispering")]
    [InlineData("attention.drained")]
    [InlineData("attention.almostSpent")]
    public void AttentionGauge_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new AttentionGaugeView(), CompanionMockGallery.Get(exhibit)!, 430));

    [Theory]
    [InlineData("engine.cloud")]
    [InlineData("engine.loggedOut")]
    [InlineData("engine.localOllama")]
    [InlineData("engine.off")]
    [InlineData("engine.collapsed")]
    public void EngineRoom_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new EngineRoomDrawer(), CompanionMockGallery.Get(exhibit)!, 1160));

    [Theory]
    [InlineData("workshop.collapsed")]
    [InlineData("workshop.expanded")]
    public void Workshop_RealizesInEveryState(string exhibit)
        => OnStaThread(() => Realize(() => new WorkshopAccordion(), CompanionMockGallery.Get(exhibit)!, 1160));

    [Fact]
    public void ExpandAndReveal_OpensTheDrawersWithoutADispatcherPump()
    {
        // The hero's AI pill and Switch chip both call this. It must be safe to call before the
        // deferred BringIntoView has run — i.e. it must not depend on a message loop existing.
        OnStaThread(() =>
        {
            var engine = new EngineRoomDrawer { DataContext = MockEngineRoomDrawerVm.Collapsed() };
            engine.Measure(new Size(1160, double.PositiveInfinity));
            engine.ExpandAndReveal();
            Assert.True(engine.ViewModel!.IsExpanded);

            var workshop = new WorkshopAccordion { DataContext = MockWorkshopAccordionVm.Collapsed() };
            workshop.Measure(new Size(1160, double.PositiveInfinity));
            workshop.ExpandAndReveal();
            Assert.True(workshop.ViewModel!.IsExpanded);
        });
    }
}
