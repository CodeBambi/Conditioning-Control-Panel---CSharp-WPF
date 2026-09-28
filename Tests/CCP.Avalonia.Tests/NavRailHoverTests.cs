using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.NavRail.cs: the rail opens under the pointer (56 -> 236, labels fade in)
/// and shuts when the pointer leaves it, measured in the rail's own space inside the Viewbox.</summary>
public sealed class NavRailHoverTests
{
    [Fact]
    public async Task PointerOnRailOpensItAndLeavingShutsIt()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var w = new MainShellWindow();
            w.Show();
            // Scale the 1585-wide canvas well below 1, so window and canvas coordinates disagree.
            w.MinWidth = w.MinHeight = 0;
            w.Width = 800;
            w.Height = 470;
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.NavRailHooked, "the constructor did not hook the rail");
            var rail = w.Named<Border>("NavSidebar")!;
            var label = rail.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == Loc.Get("tab_dashboard"));
            Assert.Equal(56, rail.Width);
            Assert.Equal(0, label.Opacity);   // shut rail: no label bleeds past the medallions
            Assert.All(rail.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Tag as string == "navrailstatic"),
                t => Assert.Equal(1, t.Opacity));   // ...but the icons (Images on WPF) stay

            async Task MoveAndSettle(double railX)
            {
                w.MouseMove(rail.TranslatePoint(new Point(railX, 200), w)!.Value, RawInputModifiers.None);
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(16);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                }
            }

            await MoveAndSettle(30);
            Assert.True(w.NavRailExpanded, "pointer on the rail did not open it");
            Assert.Equal(236, rail.Width, 1);
            Assert.Equal(1, label.Opacity, 2);

            // The 40px gutter beside the shut rail: inside the old window-coordinate test at this
            // scale, outside the rail itself once it has shut. 250 is past the open rail.
            await MoveAndSettle(250);
            Assert.False(w.NavRailExpanded, "pointer off the rail did not shut it");
            await MoveAndSettle(75);
            Assert.False(w.NavRailExpanded, "pointer in the gutter reopened the rail");
            Assert.Equal(56, rail.Width, 1);
            Assert.Equal(0, label.Opacity, 2);

            // A popup hold keeps it out while the pointer is away; releasing shuts it.
            var owner = new object();
            w.HoldNavRailOpen(owner);
            await MoveAndSettle(250);
            Assert.True(w.NavRailExpanded, "a held rail shut under the pointer");
            w.ReleaseNavRailOpen(owner);
            Assert.False(w.NavRailExpanded, "releasing the last hold left the rail open");
            w.Close();
        });
    }
}
