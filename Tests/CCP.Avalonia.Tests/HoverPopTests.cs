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
    public Task PointerEnterPopsAndLeaveSettles(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // Restored below: a leaked provider hands this private profile to every later test (the
        // Quests/ShellTray/Awareness flakes, whichever of them xUnit v3's random order ran after this).
        var oldSettings = CoreSettings.ServiceProvider;
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
            async Task Settle(int ms)
            {
                for (int t = 0; t < ms; t += 16)
                {
                    await Task.Delay(16);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    maxTilt = Math.Max(maxTilt, Math.Abs(Rig().r.Angle));
                }
            }

            w.MouseMove(new Point(150, 150), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            if (level == MotionLevel.Off) Assert.Equal(1.06, Rig().s.ScaleX, 3);   // snapped, not tweened
            else Assert.True(Rig().s.ScaleX < 1.06, "motion on should tween, not snap");
            await Settle(600);
            Assert.Equal(1.06, Rig().s.ScaleX, 3);
            Assert.Equal(1.06, Rig().s.ScaleY, 3);
            Assert.Equal(0, Rig().r.Angle, 3);
            if (level == MotionLevel.Off) Assert.Equal(0, maxTilt);
            else Assert.True(maxTilt > 1.5, $"no wobble (max tilt {maxTilt:0.00})");

            w.MouseMove(new Point(5, 5), RawInputModifiers.None);
            await Settle(400);
            Assert.Equal(1.0, Rig().s.ScaleX, 3);
            Assert.Equal(0, Rig().r.Angle, 3);
            w.Close();
        }
        finally { CoreSettings.ServiceProvider = oldSettings; }
    });
}
