using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Billboard;
using Xunit;

namespace CCP.Avalonia.Tests.Board;

/// <summary>
/// The Tonight Board on the real Home page: the slot asks the shell to host the deck the first
/// time it joins the window, the board fills the row the folded browser gives back, and with no network and no
/// account the deck still has cards (Tip and House carry it offline).
/// </summary>
public sealed class BoardShellTests
{
    [Fact]
    public Task Home_hosts_the_board_in_the_fold_row_and_it_carries_the_deck_offline() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BoardHeadTests.EnsureApp();
        BoardHeadTests.Pin();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow { Width = 1600, Height = 1000 };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var host = shell.DashboardBillboardHost;
            Assert.NotNull(host);
            var tab = shell.Named<SettingsTabView>("SettingsTab")!;
            Assert.True(tab.FindControl<Border>("DashBillboard")!.IsVisible);
            // Parity lane E3 (WPF 7.1.5): the board sits in the row the folded browser gives back
            // (the default), the logo dial keeps the centre cell.
            Assert.Same(tab.FindControl<Grid>("HomeRightColumn"), tab.FindControl<Border>("DashBillboard")!.Parent);
            Assert.True(tab.FindControl<Border>("LogoBrandFrame")!.IsVisible);

            var card = host!.CurrentCard;
            Assert.NotNull(card);
            Assert.Contains(card!.Spec.Kind, new[] { BillboardCardKind.House, BillboardCardKind.Tip, BillboardCardKind.Waiting, BillboardCardKind.Resume, BillboardCardKind.Event });
            // No Back Room host on this head: its house card never reaches the deck.
            Assert.DoesNotContain(BillboardWiring.Providers.SelectMany(p => p.Current(BillboardWiring.Context())), c => c.Id == "house.backroom");

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            if (shell.CaptureRenderedFrame() is { } frame) BoardHeadTests.Save(frame, "f-home-shell.png");
        }
        finally
        {
            shell?.Close();
            BoardHeadTests.Unpin();
        }
        return Task.CompletedTask;
    });
}
