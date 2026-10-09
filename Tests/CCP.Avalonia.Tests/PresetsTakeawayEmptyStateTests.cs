using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Takeaway shelf on the Presets tab: this head has no order drawer and no Just Drop door,
/// so it paints WPF PaintTakeawayShelf's empty state (MainWindow.Takeaway.cs:107-185) - never
/// sample orders.</summary>
public sealed class PresetsTakeawayEmptyStateTests
{
    [Fact]
    public Task ShelfShowsWpfEmptyStateNotSampleOrders() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.ShowTab("presets");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<PresetsTabView>("PresetsTab")!;

            var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
            var tray = tab.FindControl<StackPanel>("TakeawayTray")!;
            var empty = tab.FindControl<TextBlock>("TxtTakeawayEmpty")!;
            Assert.Empty(shelf.Children);
            Assert.Empty(tray.Children);
            Assert.False(shelf.IsVisible);
            Assert.False(tab.FindControl<Border>("TakeawayTrayHost")!.IsVisible);
            Assert.Equal("", tab.FindControl<TextBlock>("TxtTakeawayCount")!.Text ?? "");
            Assert.True(empty.IsVisible);
            Assert.Equal(Loc.Get("sd_takeaway_empty"), empty.Text);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
