using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The composed Companion tab — "Her Room".
///
/// <para>The nine zones already have their own suites; this one covers only what composition adds,
/// which is exactly the three things a zone cannot have on its own:</para>
/// <list type="bullet">
///   <item><b>page states</b> — free tier, dormant, empty, drained and disabled are combinations of
///   zone states, and the design's rules are about the combination ("the hero stays alive while Z2
///   locks", "the memory diary is never paywalled"). A zone test cannot see any of that.</item>
///   <item><b>the navigator</b> — a link in one zone landing in another, including the part that is
///   easy to get wrong: a torn-down page must not stay reachable from a live viewmodel.</item>
///   <item><b>the shelf collapse</b> — the page's only real layout decision, and one whose failure
///   mode is an oscillation, i.e. a hang. A render smoke test would never find it.</item>
/// </list>
/// </summary>
public class CompanionRoomCompositionTests
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    private static CompanionRoomView Realize(object dataContext, double width = 1240)
    {
        var room = new CompanionRoomView { DataContext = dataContext };
        room.Measure(new Size(width, double.PositiveInfinity));
        room.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, room.DesiredSize.Height))));
        room.UpdateLayout();
        Assert.True(room.DesiredSize.Height > 0, "the room measured to zero height — nothing realized");
        return room;
    }

    // =====================================================================================
    //  page states
    // =====================================================================================

    [Theory]
    [InlineData("default")]
    [InlineData("freeTier")]
    [InlineData("dormant")]
    [InlineData("empty")]
    [InlineData("drained")]
    [InlineData("disabled")]
    public void EveryPageState_BuildsWithEveryZonePresent(string key)
    {
        var vm = MockCompanionRoomVm.Get(key);
        Assert.NotNull(vm);
        Assert.NotNull(vm!.Hero);
        Assert.NotNull(vm.Chat);
        Assert.NotNull(vm.Memory);
        Assert.NotNull(vm.Personality);
        Assert.NotNull(vm.Awareness);
        Assert.NotNull(vm.Attention);
        Assert.NotNull(vm.Engine);
        Assert.NotNull(vm.Workshop);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("freeTier")]
    [InlineData("dormant")]
    [InlineData("empty")]
    [InlineData("drained")]
    [InlineData("disabled")]
    public void EveryPageState_RealizesTheWholePage(string key)
    {
        // The composition equivalent of the zone smoke tests: {StaticResource} is resolved when the
        // tree is built, so a theme key that only the composed page reaches would compile clean and
        // throw the first time a user opened the tab.
        OnStaThread(() => Realize(MockCompanionRoomVm.Get(key)!));
    }

    [Fact]
    public void EveryPageState_IsReachableFromTheGallery()
    {
        foreach (var key in MockCompanionRoomVm.Variants.Keys)
        {
            var exhibit = CompanionMockGallery.Get("room." + key);
            Assert.True(exhibit is MockCompanionRoomVm, $"room.{key} is missing from the mock gallery");
        }
    }

    [Fact]
    public void FreeTier_LocksTheChat_AndNothingElse()
    {
        // The design's flagship rule: barks are free, so the hero is untouched; the memory diary is
        // local and deterministic, so it is never paywalled (it is the trust surface); and the
        // interview is the conversion surface, so Z4 stays wide open. Only Z2 veils.
        var vm = MockCompanionRoomVm.FreeTier();

        Assert.Equal(CompanionZoneState.Locked, vm.Chat.State);
        Assert.False(vm.Chat.CanSend);

        Assert.True(vm.Hero.IsCompanionEnabled);
        Assert.False(vm.Hero.Header!.HasAiAccess);

        Assert.NotEmpty(vm.Memory.ProfileStats);
        Assert.NotEmpty(vm.Memory.Facts);
        Assert.True(vm.Personality.IsInterviewAvailable || vm.Personality.IsInterviewed,
            "the interview must never be gated — it is the conversion surface");
    }

    [Fact]
    public void Dormant_IsDesignedContent_NeverAnEmptyBox()
    {
        var vm = MockCompanionRoomVm.Dormant();

        Assert.Equal(CompanionZoneState.Dormant, vm.Chat.State);
        Assert.False(string.IsNullOrWhiteSpace(vm.Chat.StateCopy));
        Assert.True(vm.Awareness.IsDormant);
        Assert.False(string.IsNullOrWhiteSpace(vm.Awareness.DormantCopy));
        Assert.False(vm.Personality.IsInterviewAvailable);
        Assert.False(string.IsNullOrWhiteSpace(vm.Personality.InterviewDormantCopy));

        // …and the parts that are real today stay real: the hero never sleeps because a train has
        // not landed, and the profile strip exists from minute one.
        Assert.True(vm.Hero.IsCompanionEnabled);
        Assert.NotEmpty(vm.Memory.ProfileStats);
    }

    [Fact]
    public void Empty_StillHasAPage_ForABrandNewAccount()
    {
        var vm = MockCompanionRoomVm.Empty();

        Assert.Empty(vm.Chat.Turns);
        Assert.Equal(CompanionZoneState.Live, vm.Chat.State);
        Assert.True(vm.Chat.CanSend);

        Assert.True(vm.Memory.IsEmpty);
        Assert.False(string.IsNullOrWhiteSpace(vm.Memory.EmptyCopy));
        // "60% of the feeling from minute one" — the deterministic strip is there before any fact is.
        Assert.NotEmpty(vm.Memory.ProfileStats);

        Assert.Equal(1, vm.Hero.Level);
    }

    [Fact]
    public void Drained_KeepsHerVoice_AndTheBarsSliver()
    {
        var vm = MockCompanionRoomVm.Drained();

        Assert.Equal(0.0, vm.Attention.Fraction);
        Assert.Equal(0.04, vm.Attention.BarFraction);
        Assert.True(vm.Attention.ShowUpsell);
        // The floor promise is a resting line now, not something you have to hover to find.
        Assert.True(vm.Attention.ShowFloorNote);
        Assert.Contains("never runs out", vm.Attention.FloorNote, StringComparison.OrdinalIgnoreCase);

        // Spent thinking is not a spent page: the chat surface is still live.
        Assert.Equal(CompanionZoneState.Live, vm.Chat.State);
    }

    [Fact]
    public void Disabled_IsAsleep_NotBroken()
    {
        var vm = MockCompanionRoomVm.Disabled();

        Assert.False(vm.Hero.IsCompanionEnabled);
        Assert.False(vm.Hero.IsAiLive);
        Assert.False(string.IsNullOrWhiteSpace(vm.Hero.AsleepCopy));
        Assert.Equal(CompanionZoneState.Disabled, vm.Chat.State);
        Assert.Equal(AwarenessIntensity.Off, vm.Awareness.Intensity);
        Assert.Equal(CompanionProviderMode.Off, vm.Engine.Provider);

        // Even asleep the page stays readable — the diary and the workshop are hers, not the AI's.
        Assert.NotEmpty(vm.Memory.Facts);
        Assert.NotEmpty(vm.Workshop.Cells);
    }

    [Fact]
    public void BothDrawersRestClosed()
    {
        // The shelf is the page. A drawer that opens itself would put the plumbing back in front.
        foreach (var key in MockCompanionRoomVm.Variants.Keys)
        {
            var vm = MockCompanionRoomVm.Get(key)!;
            Assert.False(vm.Engine.IsExpanded, $"the Engine Room opens itself in '{key}'");
            Assert.False(vm.Workshop.IsExpanded, $"the Workshop opens itself in '{key}'");
        }
    }

    // =====================================================================================
    //  the navigator
    // =====================================================================================

    [Fact]
    public void SettingTheNavigator_FansItOutToEveryZoneThatCarriesALink()
    {
        var vm = MockCompanionRoomVm.Default();
        Assert.Null(vm.HeroMock.Navigator);

        var nav = new RecordingNavigator();
        vm.Navigator = nav;

        Assert.Same(nav, vm.HeroMock.Navigator);
        Assert.Same(nav, vm.ChatMock.Navigator);
        Assert.Same(nav, vm.AwarenessMock.Navigator);

        vm.Navigator = null;
        Assert.Null(vm.HeroMock.Navigator);
        Assert.Null(vm.ChatMock.Navigator);
        Assert.Null(vm.AwarenessMock.Navigator);
    }

    [Fact]
    public void TheDeepLinks_GoWhereTheDesignSaysTheyGo()
    {
        var vm = MockCompanionRoomVm.Default();
        var nav = new RecordingNavigator();
        vm.Navigator = nav;

        vm.Hero.OpenEngineRoomCommand.Execute(null);
        Assert.Equal(1, nav.EngineRoomCalls);

        vm.Hero.FocusAwarenessCommand.Execute(null);
        Assert.Equal(1, nav.AwarenessCalls);

        vm.Hero.SwitchCommand.Execute(null);
        Assert.Equal(CompanionRoomAnchors.WorkshopRosterCell, nav.LastWorkshopCell);

        vm.Awareness.FineTuningCommand.Execute(null);
        Assert.Equal(CompanionRoomAnchors.WorkshopAwarenessCell, nav.LastWorkshopCell);

        vm.Chat.OpenEngineRoomCommand.Execute(null);
        Assert.Equal(2, nav.EngineRoomCalls);
    }

    [Fact]
    public void ZonesStillWorkStandalone_WithNoNavigatorAtAll()
    {
        // Every zone shipped before the page existed and must keep working in the gallery: the
        // cross-page commands record their tag and stop rather than throwing on a null seam.
        var hero = MockCompanionHeroCardVm.Default();
        hero.OpenEngineRoomCommand.Execute(null);
        Assert.Equal("hero.engineRoom", CompanionRelayCommand.LastInvokedTag);

        MockChatThresholdVm.Live().OpenEngineRoomCommand.Execute(null);
        Assert.Equal("chat.engineRoom", CompanionRelayCommand.LastInvokedTag);

        MockAwarenessPrivacyVm.Live().FineTuningCommand.Execute(null);
        Assert.Equal("awareness.fineTuning", CompanionRelayCommand.LastInvokedTag);
    }

    [Fact]
    public void TheViewClaimsTheViewmodel_AndLetsGoOfIt()
    {
        OnStaThread(() =>
        {
            var first = MockCompanionRoomVm.Default();
            var room = Realize(first);
            Assert.Same(room, first.Navigator);

            // Re-pointing the page at another companion's viewmodel must not leave the old one
            // holding a navigator into a tree it no longer owns.
            var second = MockCompanionRoomVm.FreeTier();
            room.DataContext = second;
            Assert.Null(first.Navigator);
            Assert.Same(room, second.Navigator);
        });
    }

    [Fact]
    public void TheDeepLinksOpenTheRealDrawers_WithoutADispatcherPump()
    {
        // The pills are clicked from a freshly opened tab, before anything has pumped. Expanding
        // must happen synchronously; only the scroll is deferred.
        OnStaThread(() =>
        {
            var vm = MockCompanionRoomVm.Default();
            var room = Realize(vm);

            vm.Hero.OpenEngineRoomCommand.Execute(null);
            Assert.True(vm.Engine.IsExpanded);

            vm.Hero.SwitchCommand.Execute(null);
            Assert.True(vm.Workshop.IsExpanded);

            // And the awareness jump is a no-throw even with no message loop behind it.
            vm.Hero.FocusAwarenessCommand.Execute(null);
        });
    }

    // =====================================================================================
    //  the shelf collapse
    // =====================================================================================

    [Theory]
    [InlineData(1460, false)]
    [InlineData(1240, false)]
    [InlineData(1160, false)]
    [InlineData(1099, true)]
    [InlineData(900, true)]
    public void TheShelfStacksBelowTheThreshold(double width, bool expected)
        => Assert.Equal(expected, CompanionShelfLayout.ShouldStack(isStacked: false, width));

    [Fact]
    public void TheHysteresisBandCannotOscillate()
    {
        // The failure this guards is a hang, not a glitch: with one threshold, a width sitting on
        // the line flips the layout, the new layout reports a width on the other side, and the two
        // trade places forever. Inside the band, whatever state you are in is the state you keep.
        for (double w = CompanionShelfLayout.StackBelow; w < CompanionShelfLayout.UnstackAbove; w += 5)
        {
            Assert.True(CompanionShelfLayout.ShouldStack(isStacked: true, w));
            Assert.False(CompanionShelfLayout.ShouldStack(isStacked: false, w));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AMeaninglessWidthChangesNothing(double width)
    {
        Assert.True(CompanionShelfLayout.ShouldStack(isStacked: true, width));
        Assert.False(CompanionShelfLayout.ShouldStack(isStacked: false, width));
    }

    [Fact]
    public void TheShelfActuallyMovesTheRightColumnUnderTheLeftOne()
    {
        OnStaThread(() =>
        {
            var room = Realize(MockCompanionRoomVm.Default(), width: 1240);
            Assert.False(room.IsShelfStacked);

            var right = (FrameworkElement)room.FindName("ShelfRight");
            var left = (FrameworkElement)room.FindName("ShelfLeft");
            Assert.Equal(2, Grid.GetColumn(right));
            Assert.Equal(0, Grid.GetRow(right));
            Assert.Equal(1, Grid.GetColumnSpan(left));

            room.ApplyShelfLayout(980);
            Assert.True(room.IsShelfStacked);
            Assert.Equal(0, Grid.GetColumn(right));
            Assert.Equal(1, Grid.GetRow(right));
            Assert.Equal(3, Grid.GetColumnSpan(right));
            Assert.Equal(3, Grid.GetColumnSpan(left));

            var gutter = (ColumnDefinition)room.FindName("ShelfGutterColumn");
            Assert.Equal(0, gutter.Width.Value);

            room.ApplyShelfLayout(1240);
            Assert.False(room.IsShelfStacked);
            Assert.Equal(CompanionShelfLayout.GutterWidth, gutter.Width.Value);
        });
    }

    [Fact]
    public void ANarrowPageStillRealizes()
        => OnStaThread(() => Realize(MockCompanionRoomVm.Default(), width: 900));

    // =====================================================================================
    //  the preview harness
    // =====================================================================================

    [Fact]
    public void ThePreviewHarness_BuildsAndSwitchesEveryPageState()
    {
        OnStaThread(() =>
        {
            var window = new CompanionRoomPreviewWindow();
            Assert.Equal(CompanionRoomPreviewWindow.DefaultVariantKey, window.CurrentVariantKey);
            Assert.NotNull(window.CurrentRoom);

            foreach (var key in MockCompanionRoomVm.Variants.Keys)
            {
                Assert.True(window.ShowVariant(key), $"the harness would not show '{key}'");
                Assert.Equal(key, window.CurrentVariantKey);
                Assert.NotNull(window.RoomView.ViewModel);
            }

            // A typo in a driver script must leave the page as it is, not blank it.
            var before = window.CurrentRoom;
            Assert.False(window.ShowVariant("not-a-state"));
            Assert.Same(before, window.CurrentRoom);
            Assert.Equal("disabled", window.CurrentVariantKey);
        });
    }

    [Fact]
    public void ThePreviewStrip_CarriesOneAutomationIdPerPageState()
    {
        OnStaThread(() =>
        {
            var window = new CompanionRoomPreviewWindow();
            var strip = (Panel)window.FindName("StateStrip");

            var ids = strip.Children.OfType<Button>()
                           .Select(System.Windows.Automation.AutomationProperties.GetAutomationId)
                           .ToArray();

            Assert.Equal(MockCompanionRoomVm.Variants.Count, ids.Length);
            foreach (var key in MockCompanionRoomVm.Variants.Keys)
                Assert.Contains("CtabPreview_" + key, ids);
        });
    }

    [Fact]
    public void ThePreviewNeverOpensItself()
    {
        // Nothing in the app calls MaybeShow, and MaybeShow does nothing unless the environment
        // asks. A harness that opens on a user's machine is a bug; one that opens inside a test run
        // is a hung process.
        Assert.False(CompanionRoomPreview.IsRequested());
        Assert.False(CompanionRoomPreview.MaybeShow());
        Assert.Equal("CCP_CTAB_PREVIEW", CompanionRoomPreview.EnvVarName);
    }

    /// <summary>Counts what a zone asked the page to do.</summary>
    private sealed class RecordingNavigator : ICompanionRoomNavigator
    {
        public int EngineRoomCalls { get; private set; }
        public int AwarenessCalls { get; private set; }
        public string? LastWorkshopCell { get; private set; }

        public void RevealEngineRoom() => EngineRoomCalls++;
        public void FocusAwareness() => AwarenessCalls++;
        public void RevealWorkshop(string? cellTitle = null) => LastWorkshopCell = cellTitle;
    }
}
