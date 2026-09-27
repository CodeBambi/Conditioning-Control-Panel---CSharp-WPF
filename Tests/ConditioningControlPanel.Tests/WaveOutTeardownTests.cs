using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Services;
using NAudio.Wave;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1295: the order a waveOut player is released in. Stop first, Dispose only once the playback
/// thread has reported out, abandon (never dispose) a device that does not, and never tear the
/// same player down twice.
/// </summary>
public class WaveOutTeardownTests
{
    private sealed class FakePlayer : IWavePlayer
    {
        private readonly bool _reportsOut;
        public readonly List<string> Calls = new();
        public PlaybackState State = PlaybackState.Playing;

        public FakePlayer(bool reportsOut = true, PlaybackState state = PlaybackState.Playing)
        {
            _reportsOut = reportsOut;
            State = state;
        }

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public PlaybackState PlaybackState => State;
        public float Volume { get; set; }
        public WaveFormat OutputWaveFormat => new(44100, 2);
        public void Init(IWaveProvider waveProvider) { }
        public void Play() => State = PlaybackState.Playing;
        public void Pause() => State = PlaybackState.Paused;

        public void Stop()
        {
            lock (Calls) Calls.Add("stop");
            var wasRunning = State != PlaybackState.Stopped;
            State = PlaybackState.Stopped;
            // Like WaveOutEvent: the playback thread raises the event a moment later, elsewhere.
            if (wasRunning && _reportsOut)
                ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(20); PlaybackStopped?.Invoke(this, new StoppedEventArgs()); });
        }

        public void Dispose() { lock (Calls) Calls.Add("dispose"); }
    }

    private sealed class FakeSource : IDisposable
    {
        public int Disposed;
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    [Fact]
    public void PlayingDevice_IsStoppedThenDisposed_AfterItReportsOut()
    {
        var player = new FakePlayer();
        var source = new FakeSource();

        var outcome = WaveOutTeardown.Run(player, new IDisposable?[] { source }, waitMs: 2_000, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Equal(new[] { "stop", "dispose" }, player.Calls);
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public void DeviceThatNeverReportsOut_IsAbandoned_NotDisposed()
    {
        var player = new FakePlayer(reportsOut: false);
        var source = new FakeSource();

        var outcome = WaveOutTeardown.Run(player, new IDisposable?[] { source }, waitMs: 100, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Abandoned, outcome);
        Assert.DoesNotContain("dispose", player.Calls);
        Assert.Equal(0, source.Disposed);   // the stuck thread may still be reading it
    }

    [Fact]
    public void AlreadyStoppedDevice_IsDisposedAfterTheGrace_WithoutWaitingForAnEvent()
    {
        var player = new FakePlayer(state: PlaybackState.Stopped);

        var started = DateTime.UtcNow;
        var outcome = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 5_000, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Contains("dispose", player.Calls);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void SamePlayer_IsOnlyTornDownOnce()
    {
        var player = new FakePlayer();

        var first = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 2_000, graceMs: 10);
        var second = WaveOutTeardown.Run(player, Array.Empty<IDisposable?>(), waitMs: 2_000, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, first);
        Assert.Equal(WaveOutTeardown.Outcome.Duplicate, second);
        Assert.Single(player.Calls.FindAll(c => c == "dispose"));
    }

    [Fact]
    public void SourcesOnly_AreDisposed()
    {
        var a = new FakeSource();
        var b = new FakeSource();

        var outcome = WaveOutTeardown.Run(null, new IDisposable?[] { a, null, b }, waitMs: 100, graceMs: 10);

        Assert.Equal(WaveOutTeardown.Outcome.Disposed, outcome);
        Assert.Equal(1, a.Disposed);
        Assert.Equal(1, b.Disposed);
    }

    [Fact]
    public void Release_ReturnsAtOnce_EvenWhenTheDeviceNeverReportsOut()
    {
        var player = new FakePlayer(reportsOut: false);

        var started = DateTime.UtcNow;
        WaveOutTeardown.Release(player, new FakeSource(), "test");

        // The caller (the UI thread on engine stop) never waits on the driver.
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500));
    }
}
