using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1189 / #1179 / #1159 / #984 — the sudden-freeze family. Every one of those reports
/// says the same useless thing: a Send-priority dispatcher operation has been running for 71-137
/// seconds with <c>lockCardRunning=True</c>, and nothing names it. <see cref="UiOpTracker"/> is the
/// fix, and the part of it that can be tested headlessly is the NAME: what the hang report actually
/// prints on the <c>op=</c> line. A wrong name here is worse than no name, because triage would
/// chase the wrong subsystem.
///
/// The push/pop half runs on dispatcher hooks and is only exercised by a real WPF message loop, so
/// what is pinned below is the naming and the explicit-label contract.
/// </summary>
public class UiOpNamingTests
{
    public UiOpNamingTests() => UiOpTracker.ResetForTests();

    // ── Delegate name unmangling ─────────────────────────────────────────────

    [Fact]
    public void APlainMethodKeepsItsOwnName()
    {
        Action plain = PlainTarget;
        Assert.Equal("UiOpNamingTests.PlainTarget",
            UiOpTracker.ShortName(plain.Method.DeclaringType, plain.Method.Name));
    }

    [Fact]
    public void AClosureLambdaReportsTheEnclosingTypeAndMethod()
    {
        // This is the shape nearly every dispatcher op in the app has: a lambda that captured a
        // local, so the compiler put it on a "<>c__DisplayClass" nested inside the real type and
        // named it "<TheMethod>b__0". Raw, that reads as gibberish in a bug report.
        int captured = 7;
        Action lambda = () => GC.KeepAlive(captured);

        var name = UiOpTracker.ShortName(lambda.Method.DeclaringType, lambda.Method.Name);

        Assert.Equal("UiOpNamingTests.AClosureLambdaReportsTheEnclosingTypeAndMethod", name);
        Assert.DoesNotContain("<", name);
        Assert.DoesNotContain("DisplayClass", name);
    }

    [Fact]
    public void ACachedLambdaWithNoCaptureAlsoUnwrapsToTheRealType()
    {
        // No capture => the compiler uses the singleton "<>c" cache class instead, a different
        // nesting shape that must unwrap the same way.
        Action lambda = static () => { };
        var name = UiOpTracker.ShortName(lambda.Method.DeclaringType, lambda.Method.Name);

        Assert.StartsWith("UiOpNamingTests.", name);
        Assert.DoesNotContain("<>", name);
    }

    [Fact]
    public void AMissingDeclaringTypeStillProducesAPrintableName()
    {
        // Diagnostics must degrade, never throw: the watchdog calls this while the UI is wedged.
        Assert.Equal("?.Something", UiOpTracker.ShortName(null, "Something"));
        Assert.Equal("UiOpNamingTests", UiOpTracker.ShortName(typeof(UiOpNamingTests), null));
        Assert.Equal("UiOpNamingTests", UiOpTracker.ShortName(typeof(UiOpNamingTests), ""));
    }

    // ── Explicit labels ──────────────────────────────────────────────────────

    [Fact]
    public void AnExplicitLabelIsWhatTheReportPrints()
    {
        // DispatcherTimer ticks all post the SAME internal WPF delegate, so the auto name cannot
        // tell the lock card's tick from any other timer in the app. The label is the only way.
        using (UiOpTracker.Scope("LockCard.Tick"))
        {
            Assert.Contains("LockCard.Tick", UiOpTracker.Describe());
        }
    }

    [Fact]
    public void ALabelIsClearedOnDisposeSoItCannotGoStale()
    {
        using (UiOpTracker.Scope("LockCard.Tick")) { }
        Assert.DoesNotContain("LockCard.Tick", UiOpTracker.Describe());
    }

    [Fact]
    public void NestedLabelsRestoreTheOuterOne()
    {
        using (UiOpTracker.Scope("Outer"))
        {
            using (UiOpTracker.Scope("Inner"))
            {
                Assert.Contains("Inner", UiOpTracker.Describe());
            }
            // A restore bug here would blame the wrong subsystem for the next hang, which is
            // exactly the failure mode this whole change exists to end.
            Assert.Contains("Outer", UiOpTracker.Describe());
        }
    }

    [Fact]
    public void DescribeNeverThrowsAndAlwaysSaysSomething()
    {
        var text = UiOpTracker.Describe();
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    // ── The line the hang report carries ─────────────────────────────────────

    [Fact]
    public void TheCompactHangSnapshotCarriesTheOpName()
    {
        // This is the string that lands in the [WATCHDOG] log line and in hang_*.txt. If "op=" is
        // missing the whole change is invisible to triage.
        using (UiOpTracker.Scope("LockCard.Tick"))
        {
            var compact = HangContext.DescribeCompact();
            Assert.Contains("op=", compact);
            Assert.Contains("LockCard.Tick", compact);
        }
    }

    private static void PlainTarget() { }
}
