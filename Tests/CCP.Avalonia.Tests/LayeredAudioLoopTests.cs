using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Audit #1778: the real LibVLC layer player (the class LibVlcAudio.Seed hands LayeredAudio)
/// loops like WPF's LoopingSampleProvider: the next pass is armed on the clock and starts while the
/// current one still plays, not by reopening the stream at EOF. Dummy audio output (no device);
/// the overlap timer runs on a stepped clock.</summary>
public sealed class LayeredAudioLoopTests
{
    [Fact]
    public void NextPassStartsBeforeTheCurrentOneEnds()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e)
        {
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
            Assert.Skip("libvlc not installed: " + e.Message);
        }
        var dir = Directory.CreateTempSubdirectory("ccp-layerloop-").FullName;
        var wav = Path.Combine(dir, "drone.wav");
        WriteSilentWav(wav, ms: 6000);
        var clock = new Clock();
        var player = new LayeredAudio.VlcLayerPlayer(LibVlcAudio.Shared!, wav, time: clock) { Volume = 50 };
        try
        {
            Assert.True(SpinWait.SpinUntil(() => player.Armed, 10_000), "next pass armed once the first plays");
            Assert.Equal(1, player.Copies);
            clock.Advance(TimeSpan.FromMilliseconds(6000 - 120));   // 120 ms before the first pass ends
            Assert.Equal(2, player.Copies);
            Assert.Equal(0, player.GapStarts);                       // the first pass was still live
        }
        finally
        {
            player.Dispose();
            player.Closing.Wait(5000);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void WriteSilentWav(string path, int ms)
    {
        const int rate = 8000;
        var data = rate * ms / 1000 * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
    }

    /// <summary>Stepped clock: timers fire only on <see cref="Advance"/>; thread-safe because libvlc's
    /// TimeChanged re-aims the timer from the thread pool.</summary>
    private sealed class Clock : TimeProvider
    {
        private readonly object _g = new();
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() { lock (_g) return _now; }
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            lock (_g) _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            DateTimeOffset end;
            lock (_g) end = _now + by;
            while (true)
            {
                T? next;
                lock (_g)
                {
                    next = _timers.Find(t => t.Due <= end);
                    if (next == null) { _now = end; return; }
                    _now = next.Due!.Value;
                    next.Due = null;
                }
                next.Fire();
            }
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period)
            {
                lock (clock._g) Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due;
                return true;
            }
            public void Dispose() { lock (clock._g) Due = null; }
            public System.Threading.Tasks.ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
}
