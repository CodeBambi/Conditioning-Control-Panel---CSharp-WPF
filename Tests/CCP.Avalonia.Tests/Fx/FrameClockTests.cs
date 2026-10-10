using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Xunit;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>
/// The FrameClock twin (WPF Controls/FrameClock.cs): frame-locked, skip-to-interval, 30 fps by
/// default, one source per window that stops asking for frames when nobody listens.
/// </summary>
public sealed class FrameClockTests
{
    /// <summary>A fake frame source stepped by the test: the injected clock.</summary>
    private sealed class FakeFrames : IFrameSource
    {
        private readonly List<Action<TimeSpan>> _subs = new();
        public int Count => _subs.Count;
        public void Subscribe(Action<TimeSpan> onFrame) { if (!_subs.Contains(onFrame)) _subs.Add(onFrame); }
        public void Unsubscribe(Action<TimeSpan> onFrame) => _subs.Remove(onFrame);
        public void Frame(TimeSpan t) { foreach (var s in _subs.ToArray()) s(t); }

        /// <summary>Deliver <paramref name="seconds"/> of frames at <paramref name="hz"/>, starting at <paramref name="startMs"/>.</summary>
        public void Run(double hz, double seconds, double startMs = 1000)
        {
            int n = (int)Math.Round(hz * seconds);
            for (int i = 0; i < n; i++) Frame(TimeSpan.FromMilliseconds(startMs + i * 1000.0 / hz));
        }
    }

    private static (FrameClock Clock, FakeFrames Frames, List<TimeSpan> Ticks) Make(double intervalMs = 33)
    {
        var frames = new FakeFrames();
        var clock = new FrameClock(frames) { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        var ticks = new List<TimeSpan>();
        var sw = TimeSpan.Zero;
        frames.Subscribe(t => sw = t);   // first subscriber records the frame time the clock saw
        clock.Tick += (_, _) => ticks.Add(sw);
        return (clock, frames, ticks);
    }

    [Theory]
    [InlineData(60, 30, 30)]     // every second frame
    [InlineData(30, 30, 30)]     // every frame
    [InlineData(120, 30, 30)]    // every fourth frame
    [InlineData(144, 28, 29)]    // every fifth frame (34.7 ms): 28.8/s, never faster than asked
    [InlineData(75, 25, 25)]     // every third frame (40 ms)
    public void HoldsThirtyFpsOnWholeFrames(double hz, int min, int max)
    {
        var (clock, frames, ticks) = Make();
        clock.Start();
        frames.Run(hz, 1.0);
        Assert.InRange(ticks.Count, min, max);

        // Skip-to-interval: every gap is a whole number of frames, and all gaps are equal, which is
        // the point of the clock (a free-running 33 ms timer on 60 Hz alternates 1 and 2 frames).
        var gaps = ticks.Zip(ticks.Skip(1), (a, b) => (b - a).TotalMilliseconds).ToArray();
        double frame = 1000.0 / hz;
        Assert.All(gaps, g => Assert.Equal(Math.Round(g / frame), g / frame, 3));
        Assert.Single(gaps.Select(g => Math.Round(g, 3)).Distinct());
    }

    [Fact]
    public void SlackTakesAFrameAFewMsEarly()
    {
        var (clock, frames, ticks) = Make();
        clock.Start();
        frames.Frame(TimeSpan.FromMilliseconds(100));
        frames.Frame(TimeSpan.FromMilliseconds(129));   // 29 ms = 33 - 4 slack: due
        frames.Frame(TimeSpan.FromMilliseconds(157));   // 28 ms: not yet
        frames.Frame(TimeSpan.FromMilliseconds(158.5)); // 29.5 ms: due
        Assert.Equal(new[] { 100.0, 129.0, 158.5 }, ticks.Select(t => t.TotalMilliseconds));
    }

    [Fact]
    public void RepeatedFrameTimeTicksOnce()
    {
        var (clock, frames, ticks) = Make();
        clock.Start();
        frames.Frame(TimeSpan.FromMilliseconds(50));
        frames.Frame(TimeSpan.FromMilliseconds(50));
        Assert.Single(ticks);
    }

    [Fact]
    public void StopLeavesTheSourceAndStartRearmsFresh()
    {
        var (clock, frames, ticks) = Make();
        Assert.False(clock.IsEnabled);
        Assert.Equal(1, frames.Count);            // only the recorder
        clock.Start();
        clock.Start();                            // idempotent
        Assert.Equal(2, frames.Count);
        frames.Frame(TimeSpan.FromMilliseconds(10));
        clock.Stop();
        Assert.False(clock.IsEnabled);
        Assert.Equal(1, frames.Count);
        frames.Frame(TimeSpan.FromMilliseconds(100));
        Assert.Single(ticks);

        // A restart ticks on its first frame even 1 ms after the last tick: the gate was reset.
        clock.Start();
        frames.Frame(TimeSpan.FromMilliseconds(11));
        Assert.Equal(2, ticks.Count);
    }

    [Fact]
    public void IntervalFollowsTheTierRate()
    {
        var (clock, frames, ticks) = Make(1000.0 / 24);   // Balanced tier: 24 fps
        clock.Start();
        frames.Run(60, 1.0);
        Assert.Equal(20, ticks.Count);                       // every third frame on 60 Hz
    }

    /// <summary>One headless frame: the compositor tick, then the jobs it posts (batch finished, render).</summary>
    private static void Pump()
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task WindowSourceIsSharedAndGoesIdleWhenNobodyListens() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var a = new Border();
        var b = new Border();
        var w = new Window { Width = 100, Height = 100, Content = new StackPanel { Children = { a, b } } };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var ca = new FrameClock(a) { Interval = TimeSpan.FromMilliseconds(1) };
            var cb = new FrameClock(b) { Interval = TimeSpan.FromMilliseconds(1) };
            int ta = 0, tb = 0;
            // Each tick repaints, as every real subscriber does (FxSurface.Redraw): the composition
            // batch that repaint commits is what brings the next frame round in a headless run, where
            // MediaContext's 16 ms animation DispatcherTimer never fires.
            ca.Tick += (_, _) => { ta++; a.InvalidateVisual(); };
            cb.Tick += (_, _) => { tb++; b.InvalidateVisual(); };
            ca.Start();
            cb.Start();
            Assert.True(ca.IsFrameLocked, "a clock on a shown window must lock to its frames");

            var src = TopLevelFrameSource.For(w);
            Assert.Equal(2, src.SubscriberCount);   // one window source, two clocks
            Assert.True(src.IsRequesting);

            for (int i = 0; i < 5; i++) Pump();
            Assert.True(ta >= 3 && tb >= 3, $"window frames never reached the clocks (a={ta}, b={tb})");

            ca.Stop();
            cb.Stop();
            Assert.Equal(0, src.SubscriberCount);
            for (int i = 0; i < 3; i++) Pump();
            Assert.False(src.IsRequesting, "the window kept asking for frames with no clock listening");
            int before = ta;
            Pump();
            Assert.Equal(before, ta);
        }
        finally
        {
            w.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DetachedOwnerFallsBackToTheDispatcherPump() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var clock = new FrameClock(new Border());
        clock.Start();
        try
        {
            Assert.True(clock.IsEnabled);
            Assert.False(clock.IsFrameLocked);
        }
        finally { clock.Stop(); }
        return Task.CompletedTask;
    });
}
