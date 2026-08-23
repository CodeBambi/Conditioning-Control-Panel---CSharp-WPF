using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The lease is only worth anything if it is really held while the real-desktop facts run,
/// and really let go afterwards. Both halves are measured rather than asserted about.
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class RealDesktopLeaseTests(RealDesktopLease lease)
{
    [Fact]
    public void TheRealDesktopFacts_RunWhileThisProcessHoldsTheMachineWideLease()
    {
        // Asked from inside the collection the lease guards: a second exclusive open of the SAME
        // lease file must fail, which is the same answer another PROCESS gets while we are in here.
        // That is the whole cross-process claim, taken at the moment it has to be true.
        using var whileTheFactsRun = RealDesktopLease.TryTake(RealDesktopLease.MachineWidePath);

        // And the holder is READABLE while held, which is the difference between a failure message
        // that names the process with the desktop and one that asserts a peer exists on no evidence.
        var holder = RealDesktopLease.HolderProcessId(RealDesktopLease.MachineWidePath);

        Assert.True(lease.IsHeld,
            "the real-desktop collection is running without holding the machine-wide desktop lease — every OS-level "
            + "fact in this collection is then racing any other CcpClient.Tests process on this machine, which is "
            + "the exact defect that was measured (8 red in 12 concurrent floor runs)");
        Assert.Null(whileTheFactsRun);
        Assert.Equal(Environment.ProcessId, holder);
    }
}

/// <summary>
/// The lease primitive on its own, on a private path, so the exclusion and the release are
/// proven without touching the real lease the collection is holding.
/// </summary>
public class RealDesktopLeasePrimitiveTests
{
    [Fact]
    public void TheLease_IsExclusiveWhileHeld_AndHandsTheDesktopBackWhenItIsReleased()
    {
        // NO PLATFORM BRANCH AND NO SKIP, deliberately: this fact is the one that FAILED on the first
        // Linux run of this suite, where the second TryTake handed back a live FileStream because a
        // share mode other than None maps to a SHARED advisory flock there. The assertion was right
        // and the mechanism was missing; the mechanism is what changed (RealDesktopCollection.cs's
        // lease remarks), so the same four assertions now have to hold on both platforms.
        var path = Path.Combine(Path.GetTempPath(), "ccp-sp107-lease-" + Guid.NewGuid().ToString("N"));

        var first = RealDesktopLease.TryTake(path);
        var contendedWhileHeld = RealDesktopLease.TryTake(path);
        var holderSeenByTheContender = RealDesktopLease.HolderProcessId(path);
        first?.Dispose();
        var afterRelease = RealDesktopLease.TryTake(path);
        afterRelease?.Dispose();
        try
        {
            File.Delete(path);
            File.Delete(RealDesktopLease.HolderPathFor(path)); // the identity sidecar goes too
        }
        catch (IOException)
        {
        }

        Assert.NotNull(first);
        Assert.Null(contendedWhileHeld);
        Assert.NotNull(afterRelease);
        Assert.Equal(Environment.ProcessId, holderSeenByTheContender);
    }
}
