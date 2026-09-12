using System;
using System.Diagnostics;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The capture route itself (ccp-bugs #1189 / #1179 / #1159 / #984). Everything else in this
/// change is formatting; this is the part that either produces frames or does not, and the only
/// way to be sure a wedged 6.9.4 will produce them is to take a real snapshot of a real process
/// here. It runs against the app assembly, so it also proves the pinned ClrMD package actually
/// resolves at runtime rather than merely compiling.
///
/// It cannot reproduce a WPF UI hang (no message loop in a test host) — it proves the mechanism:
/// snapshot this process from another thread, find a named managed thread in it, read its frames.
/// A hung UI thread is strictly EASIER than this, because it is not moving.
/// </summary>
public class UiThreadStackCaptureTests
{
    public UiThreadStackCaptureTests() => UiThreadStacks.ResetAbandonedLatchForTests();

    [Fact]
    public void AnAbandonedCaptureLatchesSoTheSessionNeverStacksSnapshots()
    {
        var first = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 0);
        Assert.Contains("budget", first);
        var second = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 90_000);
        Assert.Contains("skipped", second);
    }

    [Fact]
    public void ASelfSnapshotYieldsTheCallingThreadsManagedFrames()
    {
        var sw = Stopwatch.StartNew();
        var text = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 90_000);
        sw.Stop();

        // This test method's own frame must appear: that is a managed stack walk having worked,
        // not a placeholder string. Every report in the freeze family is missing exactly this.
        Assert.Contains(nameof(ASelfSnapshotYieldsTheCallingThreadsManagedFrames), text);
        Assert.Contains("UI thread", text);

        // The watchdog runs this on a process that is already in trouble. Measured at ~100ms; the
        // assert is loose because it shares a machine with the rest of the suite, but a capture
        // that took seconds would mean the snapshot route regressed into something unshippable.
        Assert.True(sw.ElapsedMilliseconds < 60_000,
            $"stack capture took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void AnUnknownThreadIdDegradesInsteadOfThrowing()
    {
        // SafeUiManagedThreadId returns -1 when the dispatcher will not answer, and a wedged
        // process is exactly where that happens. The report must still get a printable line.
        var text = UiThreadStacks.CaptureWithBudget(-1, 90_000);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("not present in the snapshot", text);
    }

    [Fact]
    public void AnImpossibleBudgetIsAbandonedRatherThanWaitedOut()
    {
        // The rule the whole design turns on: a hang capture that itself hangs is worse than no
        // capture. Zero budget is the degenerate case of that promise.
        var sw = Stopwatch.StartNew();
        var text = UiThreadStacks.CaptureWithBudget(Environment.CurrentManagedThreadId, 0);
        sw.Stop();

        Assert.Contains("budget", text);
        Assert.True(sw.ElapsedMilliseconds < 30_000, $"gave up after {sw.ElapsedMilliseconds}ms");
    }
}
