using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The program-freeze wave (day-14 corner GIF, day-2 lock card, #984) is a HARD freeze: the UI
/// thread stops, nothing throws, crash.log stays empty and the user task-kills. The watchdog is the
/// only thing that can turn the NEXT one into evidence, so its verdict has to be right in both
/// directions: it must fire on a real wedge, and it must NOT fire during the long synchronous
/// startup init (firing there is what caused the v6.2.x "stuck on splash, relaunch a couple of
/// times" wave, because the dump write froze the app on top of a start that was merely slow).
///
/// These lock the staleness rule and the state snapshot that rides along with it. The loop itself
/// can only be exercised by wedging a real dispatcher, which is why the verdict was extracted.
/// </summary>
public class UiHangWatchdogTests
{
    // ── Staleness verdict ────────────────────────────────────────────────────

    [Fact]
    public void ShortStall_IsNotAHang()
    {
        // A GC pause, a slow frame, a settings save: seconds of silence are normal.
        Assert.False(UiHangWatchdog.IsHung(silenceMs: 3_000, hasPumpedOnce: true));
        Assert.False(UiHangWatchdog.IsHung(silenceMs: 9_999, hasPumpedOnce: true));
    }

    [Fact]
    public void SilenceBeyondTheRunningBudget_IsAHang()
    {
        // Past this the app is unusable and every field report describes a task-kill.
        Assert.True(UiHangWatchdog.IsHung(silenceMs: 10_001, hasPumpedOnce: true));
        Assert.True(UiHangWatchdog.IsHung(silenceMs: 120_000, hasPumpedOnce: true));
    }

    [Fact]
    public void BeforeTheFirstHeartbeat_ALongStartupIsNotAHang()
    {
        // OnStartup runs the whole service init synchronously on the UI thread, so it CANNOT pump a
        // heartbeat until it finishes. A cold disk cache or a post-update AV rescan makes that take
        // far longer than the running budget, and treating it as a wedge is the v6.2.x regression.
        Assert.False(UiHangWatchdog.IsHung(silenceMs: 30_000, hasPumpedOnce: false));
        Assert.False(UiHangWatchdog.IsHung(silenceMs: 119_000, hasPumpedOnce: false));
    }

    [Fact]
    public void BeforeTheFirstHeartbeat_AnExtremeStallIsStillAHang()
    {
        // A genuinely dead-forever init step (a wedge inside MainWindow creation) must not get an
        // infinite pass just because it happened before the first beat.
        Assert.True(UiHangWatchdog.IsHung(silenceMs: 121_000, hasPumpedOnce: false));
    }

    [Fact]
    public void TheStartupBudgetIsStrictlyMoreGenerousThanTheRunningOne()
    {
        Assert.True(UiHangWatchdog.ThresholdFor(hasPumpedOnce: false)
                  > UiHangWatchdog.ThresholdFor(hasPumpedOnce: true));
    }

    // ── State snapshot (HangContext) ─────────────────────────────────────────
    //
    // A stack alone is half a diagnosis. The reports we have say the UI thread died with last UI
    // mark "(idle)" — i.e. somewhere uninstrumented — so the report has to carry WHICH FEATURE was
    // live instead. These lock that the snapshot actually records it.

    [Fact]
    public void ActiveFeaturesAppearInTheCompactSnapshot()
    {
        HangContext.Enter("test.cornerGif");
        try
        {
            Assert.Contains("test.cornerGif", HangContext.DescribeCompact());
        }
        finally
        {
            HangContext.Leave("test.cornerGif");
        }
    }

    [Fact]
    public void LeavingAFeatureRemovesItFromTheSnapshot()
    {
        HangContext.Enter("test.lockCard");
        HangContext.Leave("test.lockCard");
        // Only the "active" list must drop it; the breadcrumb trail deliberately keeps the history.
        var compact = HangContext.DescribeCompact();
        Assert.DoesNotContain("test.lockCard(", compact);
    }

    [Fact]
    public void ScopeLeavesTheFeatureOnDispose()
    {
        using (HangContext.Scope("test.scoped"))
        {
            Assert.Contains("test.scoped", HangContext.DescribeCompact());
        }
        Assert.DoesNotContain("test.scoped(", HangContext.DescribeCompact());
    }

    [Fact]
    public void TheLastBreadcrumbIsWhatTheReportShows()
    {
        HangContext.Note("test.breadcrumb.marker");
        Assert.Contains("test.breadcrumb.marker", HangContext.DescribeCompact());
    }

    [Fact]
    public void TheBreadcrumbRingDoesNotGrowWithoutBound()
    {
        // The ring is written on ordinary feature paths and read while the UI is wedged, so it must
        // stay a fixed-size buffer rather than a list that balloons during a long session.
        for (int i = 0; i < 500; i++) HangContext.Note("test.flood." + i);
        var compact = HangContext.DescribeCompact();
        Assert.Contains("test.flood.499", compact);
        Assert.DoesNotContain("test.flood.0 ", compact);
    }

    [Fact]
    public void NoteAndLeaveAreSafeWithJunkInput()
    {
        // Diagnostics run on feature paths and must never be the thing that takes the app down.
        HangContext.Note("");
        HangContext.Note(null!);
        HangContext.Enter("");
        HangContext.Leave("test.never-entered");
        Assert.NotNull(HangContext.DescribeCompact());
    }

    [Fact]
    public void UptimeIsNonNegative()
    {
        Assert.True(HangContext.UptimeMs >= 0);
    }
}
