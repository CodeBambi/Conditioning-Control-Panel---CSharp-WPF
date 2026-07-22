using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-027 slice b5: the graceful-exit flow (DtrhExitFlow — wind-down → bounded exit-done
/// wait → force close; WPF CloseActive :149-160, exit :312-314, exit-done :316, exit
/// watchdog 1200ms :880) and the 0x800700AA stale-profile-lock classification + typed
/// recovery outcomes (DtrhProfileLock; SP-023 surprise #7).
/// </summary>
public class DtrhExitFlowTests
{
    // ---------- graceful exit: the three paths ----------

    [Fact]
    public void HostClose_LivePage_RequestsWindDown_ThenExitDoneClosesFast()
    {
        // CloseActive :154-159 — a live page gets end-run + bounded wait; the fast path
        // (boot.js:197-198 → shutdown :119-123) answers exit-done within milliseconds.
        var f = new DtrhExitFlow();
        Assert.Equal(DtrhExitFlow.DtrhExitAction.RequestWindDown, f.RequestClose(pageLive: true));
        Assert.True(f.Exiting);
        Assert.Equal(DtrhExitFlow.DtrhExitAction.CloseNow, f.ExitDone());
    }

    [Fact]
    public void HostClose_LivePage_TimeoutForcesClose()
    {
        // The wedged-mid-shutdown case: no exit-done inside 1200ms → force (WPF :881).
        var f = new DtrhExitFlow();
        Assert.Equal(DtrhExitFlow.DtrhExitAction.RequestWindDown, f.RequestClose(pageLive: true));
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ForceClose, f.Timeout());
    }

    [Fact]
    public void HostClose_PageNotLive_ClosesImmediately()
    {
        // WPF :157-159 — no ready page, no wind-down (a boot-stuck window must not hang).
        var f = new DtrhExitFlow();
        Assert.Equal(DtrhExitFlow.DtrhExitAction.CloseNow, f.RequestClose(pageLive: false));
        Assert.False(f.Exiting);
    }

    [Fact]
    public void PageExit_ArmsBoundedWait_ExitDoneCloses()
    {
        // ESC-hold (WPF :312-314): the page winds itself down; the host only arms the wait.
        var f = new DtrhExitFlow();
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ArmBoundedWait, f.PageExit());
        Assert.True(f.Exiting);
        Assert.Equal(DtrhExitFlow.DtrhExitAction.CloseNow, f.ExitDone());
    }

    [Fact]
    public void PageExit_WedgedMidShutdown_TimeoutForces()
    {
        var f = new DtrhExitFlow();
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ArmBoundedWait, f.PageExit());
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ForceClose, f.Timeout());
    }

    // ---------- idempotence (consult CORRECTION 3: one latch, one-shot close) ----------

    [Fact]
    public void LateExitDone_AfterForceClose_IsANoOp()
    {
        var f = new DtrhExitFlow();
        f.RequestClose(pageLive: true);
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ForceClose, f.Timeout());
        Assert.Equal(DtrhExitFlow.DtrhExitAction.None, f.ExitDone());
        Assert.Equal(DtrhExitFlow.DtrhExitAction.None, f.Timeout());
    }

    [Fact]
    public void SecondCloseRequest_WhileExiting_ClosesNow()
    {
        // WPF :153-159 — a second CloseActive while exiting disposes immediately (the
        // user hitting X twice must not wait another 1200ms).
        var f = new DtrhExitFlow();
        f.RequestClose(pageLive: true);
        Assert.Equal(DtrhExitFlow.DtrhExitAction.CloseNow, f.RequestClose(pageLive: true));
        Assert.Equal(DtrhExitFlow.DtrhExitAction.None, f.ExitDone());
    }

    [Fact]
    public void PageExit_AfterHostWindDown_RearmsNeverDuplicates()
    {
        // Host already winding down; the page's own exit arrives — still one close.
        var f = new DtrhExitFlow();
        f.RequestClose(pageLive: true);
        Assert.Equal(DtrhExitFlow.DtrhExitAction.ArmBoundedWait, f.PageExit());
        Assert.Equal(DtrhExitFlow.DtrhExitAction.CloseNow, f.ExitDone());
        Assert.Equal(DtrhExitFlow.DtrhExitAction.None, f.ExitDone());
    }
}

/// <summary>SP-027 slice b5: 0x800700AA stale-profile-lock classification + recovery.</summary>
public class DtrhProfileLockTests
{
    [Fact]
    public void Classifies_LockClass_ByHResult_AndMessage()
    {
        // ERROR_BUSY (170) rendered as a failure HRESULT; COMException carries it in
        // HResult, generic wrappers may carry only the hex in the message.
        Assert.True(DtrhProfileLock.IsStaleProfileLock(
            new System.Runtime.InteropServices.COMException("resource busy", unchecked((int)0x800700AA))));
        Assert.True(DtrhProfileLock.IsStaleProfileLock(
            new InvalidOperationException("The process cannot access the file (0x800700AA)")));
        Assert.True(DtrhProfileLock.IsStaleProfileLock(
            new InvalidOperationException("outer", new System.Runtime.InteropServices.COMException("inner", unchecked((int)0x800700AA)))));
    }

    [Fact]
    public void Rejects_UnrelatedFailures_AndNull()
    {
        Assert.False(DtrhProfileLock.IsStaleProfileLock(null));
        Assert.False(DtrhProfileLock.IsStaleProfileLock(new IOException("disk full")));
        // ERROR_SHARING_VIOLATION (32) is a DIFFERENT class — never conflated.
        Assert.False(DtrhProfileLock.IsStaleProfileLock(
            new System.Runtime.InteropServices.COMException("sharing violation", unchecked((int)0x80070020))));
    }

    [Fact]
    public void TryRecover_NoMatchingChildren_TypedNothingToKill_OrUnsupported()
    {
        // Real mechanism exercised against a profile dir nothing holds: on Windows the
        // PowerShell CIM path runs and finds no match; on Linux the typed unsupported
        // outcome stands (named limit — never faked).
        var outcome = DtrhProfileLock.TryRecover(Path.Combine(Path.GetTempPath(), "ccp-sp027-no-such-profile"));
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<DtrhProfileLock.DtrhProfileRecovery.NothingToKill>(outcome);
        }
        else
        {
            Assert.IsType<DtrhProfileLock.DtrhProfileRecovery.Unsupported>(outcome);
        }
    }

    [Fact]
    public void TryRecover_NeverThrows_OnGarbageInput()
    {
        var outcome = DtrhProfileLock.TryRecover("'; rm -rf /; '");
        Assert.NotNull(outcome); // typed outcome whatever the platform; quoting holds
    }
}
