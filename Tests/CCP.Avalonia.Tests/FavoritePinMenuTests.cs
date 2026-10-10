using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Nav;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Parity ledger shell#23 (wave A): a rail section row, a named Play card and a strip pill carry
/// the "Pin to favorites" menu (WPF WireFavoritePinMenus + PillCreated -> AttachPinMenu).
/// </summary>
public sealed class FavoritePinMenuTests
{
    [Fact]
    public void PinIdsFollowWpf()
    {
        Assert.Equal("tab.quests", MainShellWindow.PinIdForPill(new NavTab("quests", "x", NavTabKind.Tab)));
        Assert.Null(MainShellWindow.PinIdForPill(new NavTab("no-such-pill", "x", NavTabKind.Tab)));
    }

    [Fact]
    public Task RailRowsPlayCardsAndStripPillsCarryAPinMenu() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("settings");
            shell.WireFavoritePinMenus();   // idempotent; Home's first attach already ran it
            Assert.NotNull(shell.FindControl<Button>("DoorStudio")!.ContextMenu);
            Assert.NotNull(shell.FindControl<Control>("PlayTab")!.FindControl<Control>("BtnPlayArcademy")!.ContextMenu);

            shell.ShowTab("quests");
            Dispatcher.UIThread.RunJobs();
            var pinned = shell.NavStrip!.GetLogicalDescendants().OfType<Control>().Count(c => c.ContextMenu != null);
            Assert.True(pinned > 0, "the You strip's pills carry a pin menu");
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
