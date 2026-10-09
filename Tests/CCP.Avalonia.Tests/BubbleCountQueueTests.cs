using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubbleCountService.TriggerGame :321 queues behind InteractionQueue: Test Now on the
/// card while a lock card is up waits (no game, no Bambi Freeze) and plays once the card closes.</summary>
public sealed class BubbleCountQueueTests
{
    [Fact]
    public Task TestNowOverALockCardWaitsForIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var clock = new Clock();
        var host = BubbleCountHost.Instance;
        var b = new BubbleCountScheduler(host, clock, () => new[] { "/none/clip.mp4" });
        var (real, prevEngine, prevFreeze) = (host.Scheduler, CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider);
        var freezes = 0;
        host.Scheduler = b;
        CoreEngine.BubbleCount = b;
        CoreSubliminal.BambiFreezeProvider = () => freezes++;
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = new BubbleCountFeatureControl();
            card.FindControl<Button>("BtnTest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(b.IsQueued);
            Assert.False(b.IsBusy);
            clock.Advance(TimeSpan.FromSeconds(10));               // the card is still up
            Dispatcher.UIThread.RunJobs();
            Assert.True(b.IsQueued);
            Assert.Equal(0, freezes);

            LockCardWindow.ForceCloseAll();
            Dispatcher.UIThread.RunJobs();
            clock.Advance(BubbleCountScheduler.QueuePoll);
            Dispatcher.UIThread.RunJobs();
            Assert.False(b.IsQueued);
            Assert.True(b.IsBusy);                                 // the game starts its lead-in
            Assert.Equal(1, freezes);
        }
        finally
        {
            b.ForceCleanup();
            LockCardWindow.ForceCloseAll();
            (host.Scheduler, CoreEngine.BubbleCount, CoreSubliminal.BambiFreezeProvider) = (real, prevEngine, prevFreeze);
        }
        return Task.CompletedTask;
    });

    /// <summary>Audit #1932: with no LibVLC a strict game is skipped as WPF does (BubbleCountService.cs:373),
    /// not counted as a failure - no window, no WRONG! WATCH AGAIN retry, the scheduler is idle again.</summary>
    [Fact]
    public Task StrictGameWithoutLibVlcIsSkippedNotRetried() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var clip = System.IO.Path.GetTempFileName();
        var clock = new Clock();
        var host = BubbleCountHost.Instance;
        var b = new BubbleCountScheduler(host, clock, () => new[] { clip });
        var s = CoreSettings.Current;
        var (real, prevEngine, strict) = (host.Scheduler, CoreEngine.BubbleCount, s.BubbleCountStrictLock);
        var shared = typeof(global::ConditioningControlPanel.Avalonia.Platform.LibVlcAudio).GetProperty("Shared",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var vlc = shared.GetValue(null);
        host.Scheduler = b;
        CoreEngine.BubbleCount = b;
        s.BubbleCountStrictLock = true;
        shared.SetValue(null, null);
        try
        {
            b.Trigger(forceTest: true);
            clock.Advance(BubbleCountScheduler.LeadIn);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(BubbleCountWindow.OpenWindows);
            Assert.Empty(host.Messages);                           // no WRONG! WATCH AGAIN
            Assert.False(b.IsBusy);                                // skipped: the scheduler is free again
        }
        finally
        {
            shared.SetValue(null, vlc);
            b.ForceCleanup();
            host.CloseAll();
            (host.Scheduler, CoreEngine.BubbleCount, s.BubbleCountStrictLock) = (real, prevEngine, strict);
            System.IO.File.Delete(clip);
        }
        return Task.CompletedTask;
    });

    /// <summary>Stepped clock: timers fire only on <see cref="Advance"/>.</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                foreach (var t in _timers) if (t.Due < next.Due) next = t;
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
            }
            _now = end;
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) { Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due; return true; }
            public void Dispose() => Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
}
