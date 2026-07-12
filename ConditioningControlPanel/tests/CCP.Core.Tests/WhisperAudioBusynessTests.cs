using System;
using System.Threading;
using ConditioningControlPanel.Core.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the whisper-audio busy window (WPF parity: AudioService.cs:737-761
/// IsWhisperAudioPlaying/MarkWhisperAudio). The algorithm must be byte-identical to WPF:
/// duration-based, +0.25s tail, only-extends (a shorter concurrent mark can't shorten a
/// longer window), and a no-op for NaN/&lt;=0. The clock is injectable on
/// <see cref="WhisperAudioBusyness"/> so these are deterministic (no Thread.Sleep).
/// </summary>
public class WhisperAudioBusynessTests
{
    private static DateTime BaseUtc => new(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Idle_IsNotBusy()
    {
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_IsBusyUntilDurationPlusTail()
    {
        // Mark(1.0) opens a window of 1.0 + 0.25 = 1.25s (WPF AudioService.cs:759: +0.25 tail).
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(1.0);

        Assert.True(sut.IsBusy);                                  // at play time: busy
        now = BaseUtc.AddSeconds(1.24);
        Assert.True(sut.IsBusy);                                  // just before window end: busy
        now = BaseUtc.AddSeconds(1.25);
        Assert.False(sut.IsBusy);                                 // at window end: free (Ticks < until is false)
        now = BaseUtc.AddSeconds(1.30);
        Assert.False(sut.IsBusy);                                 // after window: free
    }

    [Fact]
    public void Mark_ShorterConcurrent_DoesNotShortenLongerWindow()
    {
        // WPF AudioService.cs:756-760: the CompareExchange loop only EXTENDS — a shorter
        // concurrent clip must never cut a longer one short.
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(2.0);                                            // window: BaseUtc + 2.25s

        now = BaseUtc.AddSeconds(0.10);
        sut.Mark(0.5);                                            // would end at BaseUtc + 0.85s — ignored

        now = BaseUtc.AddSeconds(0.90);                           // past the 0.5 mark's own window
        Assert.True(sut.IsBusy);                                  // still busy: the 2.0 window holds
        now = BaseUtc.AddSeconds(2.24);
        Assert.True(sut.IsBusy);
        now = BaseUtc.AddSeconds(2.25);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_LongerConcurrent_ExtendsWindow()
    {
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(0.5);                                            // window: BaseUtc + 0.75s
        now = BaseUtc.AddSeconds(0.10);
        sut.Mark(2.0);                                            // extends to BaseUtc + 2.35s

        now = BaseUtc.AddSeconds(0.80);                           // past the 0.5 mark's own window
        Assert.True(sut.IsBusy);                                  // still busy: extended window holds
        now = BaseUtc.AddSeconds(2.34);
        Assert.True(sut.IsBusy);
        now = BaseUtc.AddSeconds(2.35);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_NaN_IsNoOp()
    {
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(double.NaN);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_ZeroOrNegative_IsNoOp()
    {
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(0.0);
        Assert.False(sut.IsBusy);
        sut.Mark(-1.0);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_NaN_AfterValid_DoesNotClearWindow()
    {
        var now = BaseUtc;
        var sut = new WhisperAudioBusyness(() => now);

        sut.Mark(1.0);
        sut.Mark(double.NaN);                                     // NaN must be a true no-op, not a reset
        Assert.True(sut.IsBusy);
        now = BaseUtc.AddSeconds(1.25);
        Assert.False(sut.IsBusy);
    }

    [Fact]
    public void Mark_RealUtcDefaultClock_RoundsTripWithShortSleep()
    {
        // Smoke-check the production default clock (no injection) behaves the same way, with a
        // real (tiny) duration so the test stays fast. The window is 0.05 + 0.25 = 0.30s.
        var sut = new WhisperAudioBusyness();
        sut.Mark(0.05);
        Assert.True(sut.IsBusy);
        Thread.Sleep(450);                                        // past the 0.30s window
        Assert.False(sut.IsBusy);
    }
}
