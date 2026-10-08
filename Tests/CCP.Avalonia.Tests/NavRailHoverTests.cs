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

/// <summary>WPF 7.1.5 nav rework: the rail is ALWAYS open and labelled (96 px column, every row
/// wears its word), so the pointer opens and shuts nothing. The old hover rail (56 -> 236 under the
/// pointer) is retired; HoldNavRailOpen / ReleaseNavRailOpen stay as no-ops for their callers.</summary>
public sealed class NavRailHoverTests
{
    [Fact]
    public async Task TheRailIsAlwaysOpenAndThePointerChangesNothing()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var w = new MainShellWindow();
            try
            {
                w.Show();
                // Scale the canvas well below 1, so window and canvas coordinates disagree.
                w.MinWidth = w.MinHeight = 0;
                w.Width = 800;
                w.Height = 470;
                Dispatcher.UIThread.RunJobs();
                Assert.True(w.NavRailHooked, "the constructor did not hook the rail");
                var rail = w.Named<Border>("NavSidebar")!;
                var label = w.Named<Button>("DoorHome")!.GetLogicalDescendants().OfType<TextBlock>()
                    .First(t => t.Tag as string == "navsectionlabel");
                Assert.Equal(Loc.Get("nav_door_home"), label.Text, ignoreCase: true);   // the rail draws it in capitals

                void AssertOpen(string when)
                {
                    Assert.True(w.NavRailExpanded, $"the rail is not open {when}");
                    Assert.Equal(96, rail.Bounds.Width, 1);
                    Assert.Equal(1, label.Opacity, 2);
                    Assert.True(label.IsEffectivelyVisible, $"the Home row's word is hidden {when}");
                }

                async Task MoveAndSettle(double railX)
                {
                    w.MouseMove(rail.TranslatePoint(new Point(railX, 200), w)!.Value, RawInputModifiers.None);
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(16);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Dispatcher.UIThread.RunJobs();
                    }
                }

                AssertOpen("at startup");
                await MoveAndSettle(30);
                AssertOpen("under the pointer");
                await MoveAndSettle(250);
                AssertOpen("after the pointer left");

                var owner = new object();
                w.HoldNavRailOpen(owner);
                w.ReleaseNavRailOpen(owner);
                AssertOpen("after a hold and a release");
            }
            finally
            {
                // A shell left open keeps answering language changes from other tests' threads.
                w.Close();
            }
        });
    }
}
