using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Companion > Personality / Permissions / Links (WPF 5681c132a) on a head with no v2 page: the
/// three pills draw, land on the Companion tab and scroll the room to the zone WPF's page hosts
/// (docs/avalonia-decisions.md, sync6-companion-pages).
/// </summary>
public sealed class CompanionZonePillsTests
{
    [Fact]
    public async Task CompanionPillsLandOnTheirZone()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            MainShellWindow? w = null;
            try
            {
                w = new MainShellWindow { Width = 1400, Height = 800 };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                var room = w.Named<CompanionTabView>("CompanionTab")!.FindControl<CompanionRoomView>("Room")!;
                var scroll = room.FindControl<ScrollViewer>("PageScroll")!;

                w.ShowTab("companion");
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("personality", strip.PillKeys);
                Assert.Contains("permissions", strip.PillKeys);
                Assert.Contains("companionlinks", strip.PillKeys);
                Assert.Equal(0, scroll.Offset.Y);

                // A user click on the pill, as the strip raises it.
                strip.PillFor("permissions")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                Assert.Equal("permissions", w.CurrentTab);
                Assert.Equal("permissions", strip.ActivePillKey);
                Assert.True(w.Named<CompanionTabView>("CompanionTab")!.IsVisible);
                Assert.True(scroll.Offset.Y > 0, $"room not scrolled to permissions ({scroll.Offset.Y})");

                strip.PillFor("companionlinks")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("companionlinks", w.CurrentTab);
                Assert.True(room.FindControl<WorkshopAccordion>("WorkshopZone")!.ViewModel!.IsExpanded);
                // The Library cell itself is on screen, not just the drawer (WPF maps it to companionlinks).
                w.UpdateLayout();
                var cell = room.GetVisualDescendants().OfType<WorkshopLibraryCell>().Single();
                var top = cell.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
                Assert.True(top >= 0 && top < scroll.Viewport.Height, $"library cell not in view (top {top}, viewport {scroll.Viewport.Height}, off {scroll.Offset.Y}, extent {scroll.Extent.Height}, cellH {cell.Bounds.Height})");

                // Personality sits above: from down there the room scrolls back up to it.
                w.UpdateLayout();
                var below = scroll.Offset.Y;
                strip.PillFor("personality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("personality", w.CurrentTab);
                Assert.True(scroll.Offset.Y < below, $"room not scrolled up to personality ({below} -> {scroll.Offset.Y})");
            }
            finally
            {
                w?.Close();
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
