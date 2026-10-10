using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using CCP.Avalonia.Tests.Board;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Nav;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>k4 HA3: the 7.1.5 "What moved" card. The Help row opens it in read mode (nothing counted), a
/// fresh install spends the flag silently, Show me closes the card and lands on the row's new home.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class WhatMovedCardTests
{
    private static void Settle()
    {
        for (int i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task TheCard_HasOneRowPerMove_AndShowMeClosesThenCalls() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        var asked = new List<WhatMovedRow>();
        var card = new WhatMovedCard(asked.Add, readMode: false);
        bool closed = false;
        card.Closed += (_, _) => closed = true;
        card.Show();
        Settle();
        try
        {
            Assert.False(card.ReadMode);
            Assert.Equal(WhatMovedPlan.Rows.Count, card.Rows.Children.Count);
            var buttons = card.Rows.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(WhatMovedPlan.Rows.Count, buttons.Count);
            Assert.All(buttons, b => Assert.Equal(Loc.Get("whatmoved_show"), b.Content));

            buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.True(closed);
            Assert.Equal(new[] { WhatMovedPlan.Rows[1] }, asked);
        }
        finally { if (!closed) card.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TheShell_OffersOnce_ReplaysFromHelp_AndShowMeNavigates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        var s = CoreSettings.Current;
        int saved = s.WhatMovedCardShown;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow { Width = 1600, Height = 1000 };
            shell.Show();
            Settle();
            var opened = new List<bool>();
            shell.WhatMovedPresenter = opened.Add;

            // A fresh install spends the flag and shows nothing.
            s.WhatMovedCardShown = 0;
            shell.OfferWhatMovedIfNeeded(freshInstall: true);
            Settle();
            Assert.Equal(1, s.WhatMovedCardShown);
            Assert.Empty(opened);

            // Already shown: an upgrade launch offers nothing.
            shell.OfferWhatMovedIfNeeded(freshInstall: false);
            Settle();
            Assert.Empty(opened);

            // Help replay: read mode, the counter untouched, the help panel closed.
            s.WhatMovedCardShown = 5;
            shell.SetTutorialOverlay(true);
            shell.Named<Button>("BtnWhatMovedReplay")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal(new[] { true }, opened);
            Assert.Equal(5, s.WhatMovedCardShown);
            Assert.False(shell.Named<Control>("MainTutorialOverlay")!.IsVisible);

            // Show me lands on each row's new home.
            foreach (var row in WhatMovedPlan.Rows)
            {
                shell.WhatMovedShowMe(row);
                Settle();
                Assert.Equal(row.Tab, shell.CurrentTab);
                Assert.NotNull(NavSections.SectionForTab(row.Tab));
            }
        }
        finally
        {
            shell?.Close();
            BoardHeadTests.Unpin();
            s.WhatMovedCardShown = saved;
            CoreSettings.SaveImmediate();
        }
        return Task.CompletedTask;
    });
}
