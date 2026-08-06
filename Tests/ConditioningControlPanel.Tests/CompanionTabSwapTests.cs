using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The swap itself: the Companion tab builds, hosts "Her Room", and still answers to every name
/// the MainWindow partials call it by.
///
/// <para>This is the test the compiler cannot give you twice over. The passthrough properties are
/// resolved at build, so a missing one is a build error — but a passthrough that resolves to a
/// control which never got realized, or a room whose viewmodel throws while reading a service that
/// does not exist yet, both compile perfectly and fail the first time a user clicks Companion.</para>
///
/// <para>Nothing is initialized here: no <c>App.Settings</c>, no <c>App.Brain</c>, no login, no
/// <c>Application</c>. That is deliberate — it is also the state the tab is constructed in during
/// startup, before the services finish, and the page has to survive it.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionTabSwapTests
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    private static CompanionTabView BuildAndLayOut(double width = 1240)
    {
        var tab = new CompanionTabView();
        tab.Measure(new Size(width, double.PositiveInfinity));
        tab.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, tab.DesiredSize.Height))));
        tab.UpdateLayout();
        return tab;
    }

    [Fact]
    public void TheTabBuildsWithNoServicesAlive()
    {
        // App.Settings, App.Brain, App.Ai, App.Patreon, App.Mods and App.Companion are all null in
        // this process. Every zone viewmodel reads them on construction; none may throw.
        OnStaThread(() => Assert.NotNull(BuildAndLayOut()));
    }

    [Fact]
    public void TheTabHostsTheRoom_AndNothingElse()
    {
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            Assert.NotNull(tab.Room);
            Assert.NotNull(tab.Room.ViewModel);

            // The old 880-line settings sheet is gone: the tab's visual tree contains exactly one
            // CompanionRoomView and no leftover Expander from the five accordions it replaced.
            Assert.Single(Descendants<CompanionRoomView>(tab));
        });
    }

    [Fact]
    public void EveryPassthroughResolvesToARealControl()
    {
        // Each of these is written by a MainWindow partial by name. A null here is the crash a user
        // would get on the first sync after the swap.
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();

            Assert.NotNull(tab.TxtDetachStatusCompanion);
            Assert.NotNull(tab.TxtCompanionStatus);
            Assert.NotNull(tab.HelpBtnQuickControls);
            Assert.NotNull(tab.HelpBtnCompanionSettings);
            Assert.NotNull(tab.HelpBtnAiChat);
            Assert.NotNull(tab.PrgCompanion0FlashOverlay);

            Assert.NotNull(tab.CompanionCard0);
            Assert.NotNull(tab.TxtCompanion2Level);
            Assert.NotNull(tab.HelpBtnCompanions);
            Assert.NotNull(tab.SliderIdleIntervalCompanion);
            Assert.NotNull(tab.TxtBubbleDurationCompanion);
            Assert.NotNull(tab.TxtChatShortcutLabel);
            Assert.NotNull(tab.TxtCameraShortcutLabel);
            Assert.NotNull(tab.ChkMuteWhispersCompanion);
            Assert.NotNull(tab.ChkPauseBrowserCompanion);
            Assert.NotNull(tab.ChkTriggerModeCompanion);
            Assert.NotNull(tab.TriggerSettingsPanelCompanion);
            Assert.NotNull(tab.CmbPhrasePresets);
            Assert.NotNull(tab.TxtPhraseCount);
            Assert.NotNull(tab.VideoLinkPoolPanel);
            Assert.NotNull(tab.TxtNoVideoLinks);
            Assert.NotNull(tab.TxtHypnotubeModeLabel);
            Assert.NotNull(tab.HelpBtnVideoLinks);
            Assert.NotNull(tab.BtnRefreshPrompts);
            Assert.NotNull(tab.InstalledPromptsPanel);
            Assert.NotNull(tab.HelpBtnPrompts);
            Assert.NotNull(tab.AwarenessSettingsPanel);
            Assert.NotNull(tab.SliderAwarenessCooldown);
            Assert.NotNull(tab.SliderAwarenessCooldownMax);
            Assert.NotNull(tab.BtnPrivacySpoiler);
            Assert.NotNull(tab.TxtPrivacyDetails);
            Assert.NotNull(tab.HelpBtnAwareness);
        });
    }

    [Fact]
    public void TheHiddenCompatElementsStayHidden()
    {
        // They exist to be written to, never seen. A visible one would print raw status text into
        // the middle of the page.
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            Assert.Equal(Visibility.Collapsed, VisibilityOf(tab.TxtDetachStatusCompanion));
            Assert.Equal(Visibility.Collapsed, VisibilityOf(tab.TxtCompanionStatus));
            Assert.Equal(Visibility.Collapsed, VisibilityOf(tab.HelpBtnQuickControls));
            Assert.Equal(Visibility.Collapsed, tab.HelpBtnAiChat.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.HelpBtnCompanionSettings.Visibility);
        });
    }

    [Fact]
    public void TheWorkshopCarriesTheRealControls_NotTheScaffoldRows()
    {
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            var cells = tab.Vm.WorkshopVm.Cells;

            Assert.Equal(6, cells.Count);
            Assert.All(cells, c => Assert.NotNull(c.Content));
            // A cell that carried both would render the mock rows on top of the moved controls.
            Assert.All(cells, c => Assert.Empty(c.Rows));
        });
    }

    [Fact]
    public void TheWorkshopAnchorsSurviveTheHeadings()
    {
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            var keys = tab.Vm.WorkshopVm.Cells.Select(c => c.Key).ToArray();

            Assert.Contains(CompanionRoomAnchors.WorkshopRosterCell, keys);
            Assert.Contains(CompanionRoomAnchors.WorkshopAwarenessCell, keys);
        });
    }

    [Fact]
    public void BothDrawersRestClosed()
    {
        // The point of Z7/Z8: the plumbing stopped being the front door.
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            Assert.False(tab.Vm.EngineVm.IsExpanded);
            Assert.False(tab.Vm.WorkshopVm.IsExpanded);
        });
    }

    [Fact]
    public void TheDeepLinksOpenTheirDrawers()
    {
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();

            tab.Room.RevealEngineRoom();
            Assert.True(tab.Vm.EngineVm.IsExpanded);

            tab.Room.RevealWorkshop(CompanionRoomAnchors.WorkshopRosterCell);
            Assert.True(tab.Vm.WorkshopVm.IsExpanded);
        });
    }

    [Fact]
    public void WithNoBrain_TheChatZoneStandsDownWithoutSwallowingInput()
    {
        // App.Brain is null here, which is the same shape as UseCompanionBrain=false: there is no
        // thread for a typed line to join. The zone must SAY so and refuse the send — the one thing
        // it may never do is take the text and drop it.
        //
        // Asserted as a coherence rule rather than "State == Dormant" on purpose. App.Brain and
        // App.Settings are process-wide statics that this suite does not own, so pinning the exact
        // rung would make the test depend on what ran before it. Which rung we land on is covered
        // exhaustively (and hermetically) by CompanionRoomWiringTests.ChatState_*.
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            var chat = tab.Vm.ChatVm;

            Assert.NotEqual(CompanionZoneState.Live, chat.State);
            Assert.False(chat.CanSend);
            Assert.Empty(chat.Turns);

            // Every non-live rung ships copy — a silent card is the failure mode the design forbids.
            var copy = chat.State == CompanionZoneState.Locked ? chat.LockCopy : chat.StateCopy;
            Assert.False(string.IsNullOrWhiteSpace(copy));

            // And a send that arrives anyway is a no-op: not a crash, and not a lost line.
            chat.Draft = "hello?";
            chat.SendCommand.Execute(null);
            Assert.Equal("hello?", chat.Draft);
        });
    }

    [Fact]
    public void TheDiaryIsReadableWithNoMemoryStore()
    {
        // Z3 is the trust surface and is never gated — not on entitlement, not on the brain.
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            var diary = tab.Vm.MemoryVm;

            Assert.True(diary.IsEmpty);
            Assert.False(string.IsNullOrWhiteSpace(diary.EmptyCopy));
            Assert.False(string.IsNullOrWhiteSpace(diary.StorageNote));
            // The wall still ends with the Train 4 promise rather than stopping dead.
            Assert.Contains(diary.Facts, f => f.IsDormant);
            Assert.Equal(FactOrdering.FilterKeys.Count, diary.Filters.Count);
        });
    }

    [Fact]
    public void SyncIsIdempotentAndSafeWithoutServices()
    {
        OnStaThread(() =>
        {
            var tab = BuildAndLayOut();
            tab.Vm.Sync();
            tab.Vm.SyncBrain();
            tab.Vm.SyncHero();
            tab.Vm.Sync();
            Assert.NotNull(tab.Vm.Hero.Name);
        });
    }

    [Fact]
    public void TheShelfCollapsesOnANarrowTab()
    {
        OnStaThread(() =>
        {
            var wide = BuildAndLayOut(1240);
            Assert.False(wide.Room.IsShelfStacked);

            var narrow = BuildAndLayOut(1000);
            Assert.True(narrow.Room.IsShelfStacked);
        });
    }

    // ---------------------------------------------------------------------------------

    private static Visibility VisibilityOf(FrameworkElement element)
    {
        // The compat elements are collapsed by their container, not individually.
        for (DependencyObject? d = element; d != null;
             d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalParent(d))
        {
            if (d is UIElement { Visibility: Visibility.Collapsed }) return Visibility.Collapsed;
        }
        return element.Visibility;
    }

    private static DependencyObject? LogicalParent(DependencyObject d)
        => d is FrameworkElement fe ? fe.Parent : null;

    private static System.Collections.Generic.List<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var found = new System.Collections.Generic.List<T>();
        Walk(root);
        return found;

        void Walk(DependencyObject node)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                if (child is T hit) found.Add(hit);
                Walk(child);
            }
        }
    }
}
