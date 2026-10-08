using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.HelpLoops;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The drawn help loop in a feature's ? popover (WPF HelpPopover.BuildLoop): the user
/// clicks ?, the loop plays on the stepped clock, the step chips follow it, closing stops it, and
/// motion Off shows the still frame without a running clock.</summary>
public sealed class HelpLoopTests
{
    [Theory]
    [InlineData("PinkFilter", 4000, 0)]     // still 4400 + 4000 wraps to t=1200: step 1
    [InlineData("FlashImages", 1000, 2)]    // still 2900 + 1000 = t=3900: step 3
    public Task ClickingHelpPlaysTheLoopUntilTheCardCloses(string id, long stepMs, int litChip) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        HelpLoopView.Time = clock;
        var (host, button) = Host(id);
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Click(host, button);
            Assert.True(HelpPopover.IsOpen(button));
            var view = Find<HelpLoopView>(button);
            var steps = Find<HelpLoopSteps>(button);
            Assert.True(view.IsRunning, $"the loop did not start when the card opened (failed={view.Failed})");
            Assert.Equal(view.Scene.StillMs, view.CurrentTime);

            clock.Now += TimeSpan.FromMilliseconds(stepMs).Ticks;
            view.Tick();
            var expected = (view.Scene.StillMs + stepMs) % view.Scene.DurationMs;
            Assert.Equal(expected, view.CurrentTime, 3);
            for (int i = 0; i < view.Scene.Steps.Count; i++)
                Assert.Equal(i == litChip, steps.IsLit(i));

            host.CaptureRenderedFrame();
            Assert.False(view.Failed, "the scene threw while drawing");
            var chipTexts = steps.Children.OfType<Border>().Select(b => ((TextBlock)b.Child!).Text).ToArray();
            Assert.All(chipTexts, t => Assert.False(string.IsNullOrEmpty(t) || t.StartsWith("help_loop_"), t));

            Click(host, button);   // second click closes the pinned card
            Assert.False(HelpPopover.IsOpen(button));
            view.Tick();
            Assert.False(view.IsRunning, "the loop kept ticking behind a closed card");
        }
        finally
        {
            HelpPopover.Clear(button);
            host.Close();
            HelpLoopView.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionOffShowsTheStillFrameWithoutAClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var (host, button) = Host("PinkFilter");
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Click(host, button);
            var view = Find<HelpLoopView>(button);
            Assert.False(view.IsRunning);
            Assert.Equal(view.Scene.StillMs, view.CurrentTime);
            Assert.True(Find<HelpLoopSteps>(button).IsLit(1));   // 4400 is inside step 2
        }
        finally
        {
            HelpPopover.Clear(button);
            host.Close();
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReducedPlaysAtHalfSpeedThroughTheFrameSourceAndOneChainSurvivesAReopen() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var clock = new SteppedClock();
        HelpLoopView.Time = clock;
        var frames = new System.Collections.Generic.List<Action<TimeSpan>>();
        HelpLoopView.RequestAnimationFrame = (_, cb) => frames.Add(cb);
        var (host, button) = Host("PinkFilter");
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            Click(host, button);
            var view = Find<HelpLoopView>(button);
            Assert.Single(frames);

            clock.Now += TimeSpan.FromMilliseconds(1000).Ticks;
            RunFrames(frames);
            Assert.Equal(view.Scene.StillMs + 500, view.CurrentTime, 3);   // Reduced = half speed
            Assert.Single(frames);                                          // the frame asked for the next one

            Click(host, button);    // close and reopen inside one frame
            Click(host, button);
            Assert.True(view.IsRunning);
            Assert.Equal(2, frames.Count);   // the stale callback is still pending
            RunFrames(frames);
            Assert.Single(frames);           // ...but only one chain re-requests
        }
        finally
        {
            HelpPopover.Clear(button);
            host.Close();
            HelpLoopView.Time = TimeProvider.System;
            HelpLoopView.RequestAnimationFrame = (top, cb) => top.RequestAnimationFrame(cb);
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AFaultingSceneKeepsItsLastFrameUp() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var scene = new WhiteScene();
        var view = new HelpLoopView(scene);
        var host = new Window { Width = 480, Height = 270, Content = view };
        host.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0xFFFFFFFFu, CenterPixel(host));
            scene.Throw = true;
            view.InvalidateVisual();
            Assert.Equal(0xFFFFFFFFu, CenterPixel(host));   // the faulting pass
            Assert.True(view.Failed);
            view.InvalidateVisual();
            Assert.Equal(0xFFFFFFFFu, CenterPixel(host));   // and every pass after it
        }
        finally { host.Close(); }
        return Task.CompletedTask;
    });

    private sealed class WhiteScene : HelpLoopScene
    {
        public bool Throw;
        public override string Id => "";
        public override double DurationMs => 1000;
        public override double StillMs => 0;
        public override System.Collections.Generic.IReadOnlyList<HelpLoopStep> Steps { get; } = Array.Empty<HelpLoopStep>();
        public override void Draw(LoopFrame f, double t)
        {
            if (Throw) throw new InvalidOperationException("scene fault");
            f.Front.DrawRectangle(global::Avalonia.Media.Brushes.White, null, new Rect(0, 0, LoopFrame.StageWidth, LoopFrame.StageHeight));
        }
    }

    private static uint CenterPixel(Window host)
    {
        var bmp = host.CaptureRenderedFrame()!;
        var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            bmp.CopyPixels(new PixelRect(bmp.PixelSize.Width / 2, bmp.PixelSize.Height / 2, 1, 1), buf, 4, 4);
            return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    private static void RunFrames(System.Collections.Generic.List<Action<TimeSpan>> frames)
    {
        var due = frames.ToArray();
        frames.Clear();
        foreach (var cb in due) cb(TimeSpan.Zero);
    }

    private static (Window Host, Button Button) Host(string id)
    {
        var button = new Button { Content = "?", Width = 44, Height = 44 };
        var host = new Window { Width = 900, Height = 700, Content = new StackPanel { Margin = new Thickness(24), Children = { button } } };
        HelpPopover.Attach(button, HelpContentService.GetContent(id));
        host.Show();
        Dispatcher.UIThread.RunJobs();
        return (host, button);
    }

    private static T Find<T>(Button button) where T : Control =>
        HelpPopover.PopupContent(button)!.GetVisualDescendants().OfType<T>().Single();

    private static void Click(Window host, Button button)
    {
        var p = button.TranslatePoint(new Point(22, 22), host)!.Value;
        host.MouseMove(p, RawInputModifiers.None);
        host.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
