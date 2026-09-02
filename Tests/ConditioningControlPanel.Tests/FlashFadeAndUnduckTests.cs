using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1107 (takeover ducking never restores) and the 6.9.0 fade-slider frame-rate report (meadow).
///
/// The unduck used to be scheduled as Task.Delay(ms, runToken).ContinueWith(.., NotOnCanceled).
/// FlashService.Stop() cancelled the run token but never replaced it, and TriggerFlashOnce (the
/// one-shot entry Takeover uses) never made one of its own, so once anything had stopped the
/// service the delay came back already-cancelled and the continuation was dropped: the audio duck
/// ref leaked and every other app stayed quiet until the five minute watchdog.
///
/// The fade ramp used to write Window.Opacity every frame. A flash window is AllowsTransparency,
/// so each write is a re-rasterise plus a monitor-sized UpdateLayeredWindow blit on the UI thread.
/// </summary>
public class FlashFadeAndUnduckTests
{
    private const double Eps = 1.0 / 32.0;

    // ---- Bug 1: the unduck must not be cancellable ---------------------------------------

    [Fact]
    public void ScheduleUnduck_TakesNoCancellationToken()
    {
        // The regression guard: re-threading a token through here is exactly what broke #1107.
        var method = typeof(FlashService).GetMethod(
            "ScheduleUnduck", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.DoesNotContain(method!.GetParameters(),
            p => p.ParameterType == typeof(CancellationToken) || p.ParameterType == typeof(CancellationTokenSource));
    }

    [Fact]
    public async Task ScheduleUnduck_RunsTheCallback()
    {
        int calls = 0;
        await FlashService.ScheduleUnduck(1, () => Interlocked.Increment(ref calls));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ScheduleUnduck_RunsEvenWhenAnAmbientSourceIsAlreadyCancelled()
    {
        // Stands in for the post-Stop state that used to swallow the unduck.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        int calls = 0;
        var task = FlashService.ScheduleUnduck(1, () => Interlocked.Increment(ref calls));
        await task;

        Assert.Equal(TaskStatus.RanToCompletion, task.Status);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ScheduleUnduck_NegativeDelayStillRuns()
    {
        int calls = 0;
        await FlashService.ScheduleUnduck(-5000, () => Interlocked.Increment(ref calls));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ScheduleUnduck_NullCallbackIsANoOp()
    {
        await FlashService.ScheduleUnduck(1, null!);
    }

    // ---- Bug 2a: the layered ramp is clamped ---------------------------------------------

    [Fact]
    public void ResolveFadeSeconds_ClampsTheLayeredPath()
    {
        // 100% on the slider = a 1.0 s ramp; layered windows get at most 0.5 s of blits.
        Assert.Equal(0.5, FlashService.ResolveFadeSeconds(1.0, cheapAlpha: false));
    }

    [Fact]
    public void ResolveFadeSeconds_LeavesShortLayeredRampsAlone()
    {
        Assert.Equal(0.4, FlashService.ResolveFadeSeconds(0.4, cheapAlpha: false), 9);
        Assert.Equal(0.0, FlashService.ResolveFadeSeconds(0.0, cheapAlpha: false), 9);
    }

    [Fact]
    public void ResolveFadeSeconds_DoesNotTouchCompositorOrHost()
    {
        Assert.Equal(1.0, FlashService.ResolveFadeSeconds(1.0, cheapAlpha: true), 9);
        Assert.Equal(0.4, FlashService.ResolveFadeSeconds(0.4, cheapAlpha: true), 9);
    }

    // ---- Bug 2b: opacity writes are quantised, terminals always land ----------------------

    [Fact]
    public void SubEpsilonStepIsSkipped()
        => Assert.False(FlashService.ShouldWriteAlpha(last: 0.50, next: 0.51, target: 1.0, epsilon: Eps));

    [Fact]
    public void EpsilonSizedStepIsWritten()
        => Assert.True(FlashService.ShouldWriteAlpha(last: 0.50, next: 0.50 + Eps, target: 1.0, epsilon: Eps));

    [Fact]
    public void ReachingTheTargetIsAlwaysWritten()
    {
        // The fade-in must settle exactly on the Opacity slider's value, however small the last step.
        Assert.True(FlashService.ShouldWriteAlpha(last: 0.999, next: 1.0, target: 1.0, epsilon: Eps));
        Assert.True(FlashService.ShouldWriteAlpha(last: 0.699, next: 0.7, target: 0.7, epsilon: Eps));
    }

    [Fact]
    public void ReachingZeroIsAlwaysWritten()
    {
        // The heartbeat removes the window on "newAlpha <= 0"; a skipped zero strands it on screen.
        Assert.True(FlashService.ShouldWriteAlpha(last: 0.01, next: 0.0, target: 0.0, epsilon: Eps));
        Assert.True(FlashService.ShouldWriteAlpha(last: 0.001, next: 0.0, target: 0.0, epsilon: Eps));
    }

    [Fact]
    public void FadingDownIsQuantisedToo()
    {
        Assert.False(FlashService.ShouldWriteAlpha(last: 0.50, next: 0.49, target: 0.0, epsilon: Eps));
        Assert.True(FlashService.ShouldWriteAlpha(last: 0.50, next: 0.40, target: 0.0, epsilon: Eps));
    }

    [Fact]
    public void NaNIsNeverWritten()
        => Assert.False(FlashService.ShouldWriteAlpha(last: 0.5, next: double.NaN, target: 1.0, epsilon: Eps));

    [Fact]
    public void FullRamp_CostsAtMostThirtyThreeWrites()
    {
        // The point of the change: a 0.5 s layered fade-in at 60 fps used to be one blit per frame.
        int writes = 0;
        double last = 0.0, alpha = 0.0;
        const double target = 1.0;
        const double step = 1.0 / 240.0;   // deliberately finer than any real frame step

        while (alpha < target)
        {
            alpha = Math.Min(target, alpha + step);
            if (FlashService.ShouldWriteAlpha(last, alpha, target, Eps))
            {
                writes++;
                last = alpha;
            }
        }

        Assert.Equal(target, last, 9);       // the ramp ends exactly on target
        Assert.InRange(writes, 1, 33);       // 32 quantised steps plus the terminal
    }

    [Fact]
    public void RampNeverStalls_EveryStepEitherWritesOrKeepsClimbing()
    {
        // Guards the FadeAlpha/LastWrittenAlpha split: the logical ramp advances every frame even
        // when the write is skipped, so a skipped write can never freeze the fade.
        double alpha = 0.0, last = 0.0;
        const double target = 0.75;
        const double step = 0.004;
        int frames = 0;

        while (alpha < target && frames < 10_000)
        {
            frames++;
            alpha = Math.Min(target, alpha + step);
            if (FlashService.ShouldWriteAlpha(last, alpha, target, Eps)) last = alpha;
        }

        Assert.True(alpha >= target, "the ramp must reach the target");
        Assert.Equal(target, last, 9);
    }
}
