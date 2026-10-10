using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF SetExpandedDoor/IsDoorPanelOpenFor/ApplyNavRailDoorState (MainWindow.TabNavigation.cs:868,
/// MainWindow.NavRail.cs:1294): a navigation while the rail is shut opens no door panel (Discord v6.8.6,
/// "submenu stays open after the menu collapsed"); the chosen door opens with the rail and parks when it shuts.</summary>
public sealed class NavDoorRailGateTests
{
    [Fact]
    public async Task ShutRailOpensNoDoorAndHoverRestoresTheChosenOne()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var motion = CoreSettings.Current.MotionLevel;
            var w = new MainShellWindow();
            try
            {
                CoreSettings.Current.MotionLevel = MotionLevel.Off;   // WPF: AllowTransitions false snaps
                w.Show();
                Dispatcher.UIThread.RunJobs();
                var rail = w.Named<Border>("NavSidebar")!;
                var you = w.Named<Border>("DoorPanelYou")!;
                var studio = w.Named<Border>("DoorPanelStudio")!;
                void Move(double x)
                {
                    w.MouseMove(rail.TranslatePoint(new Point(x, 200), w)!.Value, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.False(w.NavRailExpanded);
                w.ShowTab("quests");   // e.g. a notification or the palette, rail shut
                Assert.Equal("you", w.ExpandedDoor);
                Assert.False(you.IsHitTestVisible, "a shut rail opened the You door");
                Assert.Equal(0, you.Height);

                Move(30);   // the user hovers the rail
                Assert.True(w.NavRailExpanded);
                Assert.True(you.IsHitTestVisible, "opening the rail did not restore the chosen door");
                Assert.True(double.IsNaN(you.Height));
                Assert.False(studio.IsHitTestVisible);

                Move(w.Bounds.Width - 10);   // pointer leaves: the rail shuts and parks the door
                Assert.False(w.NavRailExpanded);
                Assert.False(you.IsHitTestVisible, "shutting the rail left the door panel open");
                Assert.Equal(0, you.Height);
                Assert.Equal("you", w.ExpandedDoor);   // the user's choice survives the collapse

                // A popup hold opens the rail synchronously: at MotionLevel Off the door snaps open
                // in the same call, with no 160ms tween armed (read before any job can run).
                var owner = new object();
                w.HoldNavRailOpen(owner);
                Assert.True(double.IsNaN(you.Height), "motion Off must snap the door open");
                Assert.Null(you.Transitions);

                // WPF SetExpandedDoor touches only the new and the previous door: switching on an
                // open rail opens Studio AND parks You; the same door again is a no-op.
                w.ShowTab("Haptics");
                Assert.Equal("studio", w.ExpandedDoor);
                Assert.True(studio.IsHitTestVisible, "the new door did not open");
                Assert.False(you.IsHitTestVisible, "the previous door stayed open");
                Assert.Equal(0, you.Height);
                w.ShowTab("Haptics");
                Assert.True(studio.IsHitTestVisible);
                w.ReleaseNavRailOpen(owner);
            }
            finally
            {
                CoreSettings.Current.MotionLevel = motion;
                w.Close();
            }
            return Task.CompletedTask;
        });
    }
}
