using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Behaviors/HoverPopBehavior.cs: pointer in pops the art to 1.06 with a wobble that
/// settles at 0 degrees, pointer out rides home to 1.0; at MotionLevel.Off it snaps, no wobble.</summary>
public sealed class HoverPopTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Off)]
    public Task PointerEnterPopsAndLeaveSettles(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // Restored below: a leaked provider hands this private profile to every later test (the
        // Quests/ShellTray/Awareness flakes, whichever of them xUnit v3's random order ran after this).
        var oldSettings = CoreSettings.ServiceProvider;
        var clock = new SteppedClock();
        HoverPop.Time = clock;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            CoreSettings.Current.MotionLevel = level;

            var art = new Border { Width = 100, Height = 100, Background = Brushes.HotPink };
            HoverPop.SetIsEnabled(art, true);
            var w = new Window { Width = 300, Height = 300, Content = art };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(art.RenderTransform);   // the rig waits for the first hover, as WPF

            double maxTilt = 0;
            (ScaleTransform s, RotateTransform r) Rig()
            {
                var g = (TransformGroup)art.RenderTransform!;
                return ((ScaleTransform)g.Children[^2], (RotateTransform)g.Children[^1]);
            }
            // 16 ms frames on a stepped clock: a wall-clock sampler on a loaded machine skipped the
            // 90 ms peak (max tilt 1.37) whenever the real timer fired late.
            void Settle(int ms)
            {
                for (int t = 0; t < ms; t += 16)
                {
                    clock.Now += TimeSpan.FromMilliseconds(16).Ticks;
                    HoverPop.Step(art);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    maxTilt = Math.Max(maxTilt, Math.Abs(Rig().r.Angle));
                }
            }

            w.MouseMove(new Point(150, 150), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            if (level == MotionLevel.Off) Assert.Equal(1.06, Rig().s.ScaleX, 3);   // snapped, not tweened
            else Assert.True(Rig().s.ScaleX < 1.06, "motion on should tween, not snap");
            Settle(600);
            Assert.Equal(1.06, Rig().s.ScaleX, 3);
            Assert.Equal(1.06, Rig().s.ScaleY, 3);
            Assert.Equal(0, Rig().r.Angle, 3);
            if (level == MotionLevel.Off) Assert.Equal(0, maxTilt);
            else Assert.True(maxTilt > 1.5, $"no wobble (max tilt {maxTilt:0.00})");

            w.MouseMove(new Point(5, 5), RawInputModifiers.None);
            Settle(400);
            Assert.Equal(1.0, Rig().s.ScaleX, 3);
            Assert.Equal(0, Rig().r.Angle, 3);
            w.Close();
        }
        finally { service.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings; HoverPop.Time = TimeProvider.System; }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
