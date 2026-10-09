using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StudioTabView.xaml.cs: clicking a rack row crossfades the incoming panel in over
/// 120ms (FadeInDetail :1384) and a right-click quick-toggle pops the row's state dot 2x -> 1x over
/// 260ms (PingDot :1257); MotionLevel Off snaps both. Driven by real pointer input on the shell.</summary>
public sealed class StudioRackFxTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task RowClickFadesThePanelAndRightClickPopsTheDot(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (oldMotion, oldSub) = (s.MotionLevel, s.SubliminalEnabled);
        var clock = new SteppedClock();
        StudioTabView.Time = clock;
        var shell = new MainShellWindow();
        try
        {
            s.MotionLevel = level;
            shell.Show();
            shell.ShowTab("studio");
            var rack = shell.StudioRack!;
            void Frame() { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            void Step(int ms) { for (int t = 0; t < ms; t += 16) { clock.Now += TimeSpan.FromMilliseconds(16).Ticks; rack.StepFx(); Frame(); } }
            Frame();
            var row = rack.GetVisualDescendants().OfType<RadioButton>().First(r => r.Tag as string == "subliminal");
            Point Center() => row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), shell)!.Value;

            shell.MouseDown(Center(), MouseButton.Left);
            shell.MouseUp(Center(), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var host = rack.HostFor("subliminal")!;
            Assert.True(host.IsVisible);
            if (level == MotionLevel.Off) { Assert.Equal(1, host.Opacity); Assert.False(rack.FxRunning); }
            else
            {
                Assert.Equal(0, host.Opacity);
                Assert.True(rack.FxRunning);
                Step(48);
                Assert.InRange(host.Opacity, 0.3, 0.99);
                Step(96);
                Assert.Equal(1, host.Opacity);
                Assert.False(rack.FxRunning);   // no clock survives the fade
            }

            bool before = s.SubliminalEnabled;
            shell.MouseDown(Center(), MouseButton.Right);
            shell.MouseUp(Center(), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(before, s.SubliminalEnabled);
            var dot = rack.DotFor("subliminal")!;
            double Scale() => (dot.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
            if (level == MotionLevel.Off) Assert.Equal(1, Scale());
            else
            {
                Assert.Equal(2, Scale(), 3);
                Step(128);
                Assert.InRange(Scale(), 1.01, 1.9);
                Step(160);
                Assert.Equal(1, Scale());
                Assert.False(rack.FxRunning);
            }
        }
        finally
        {
            shell.Close();
            StudioTabView.Time = TimeProvider.System;
            s.MotionLevel = oldMotion;
            s.SubliminalEnabled = oldSub;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
