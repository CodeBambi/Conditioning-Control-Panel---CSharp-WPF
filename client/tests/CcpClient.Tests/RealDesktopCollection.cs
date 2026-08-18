using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-107. <b>The interactive desktop is a SINGLETON, and until this existed the suite addressed
/// it by constant.</b>
///
/// <para>Every fact in this project that talks to the real window manager reaches shared,
/// MACHINE-GLOBAL state through a fixed name: one evidence folder
/// (<see cref="FlashDrawObservations.EvidenceFolder"/>), one image colour
/// (<see cref="FlashEndToEndObservations.ImageColour"/>), one spawn seed, and rectangles and
/// hit-test points derived from the screen size — which is the same number in every process on
/// the machine. Two <c>check-floor.mjs</c> runs therefore wrote the same file, painted the same
/// colour in the same place, and contested the same points. The port's own gate wrapper permits
/// exactly that: <c>client/tools/gate/with-slot.mjs --slots 3</c>.</para>
///
/// <para><b>Measured, not supposed</b> (SP-107 record §2): 20 floor runs one-at-a-time gave 0 red;
/// 12 floor runs in waves of 3 gave 8 red, and the failure text named the collision every time —
/// <c>IOException ... 'ccp-sp100-flash-draws\desktop-with-a-real-flash.bmp' because it is being
/// used by another process</c>, and <c>Assert.Equal() Expected 0, Actual 676161</c> where 676161
/// is exactly 951x711, the whole pixel area of ANOTHER run's flash.</para>
///
/// <para><b>The mechanism, in two halves.</b>
/// (1) IN-PROCESS: co-location. Every real-desktop class joins this collection, and xunit's
/// intra-collection sequentiality serializes them — the same mechanism, and the same honesty
/// about <c>DisableParallelization</c> being a non-relied-upon hint, as
/// <see cref="ProcessEnvCollection"/> (SP-062/SP-086, <c>DataRootOverrideTests.cs:116-122</c>).
/// Membership is mechanical: <see cref="RealDesktopCollectionGuardTests"/> fails when a class
/// that touches the desktop does not carry the attribute.
/// (2) CROSS-PROCESS: <see cref="RealDesktopLease"/>, an exclusive machine-wide lease held for
/// the life of this collection. A collection fixture is constructed before the collection's first
/// test and disposed after its last, so the whole real-desktop run happens under it.</para>
///
/// <para><b>What this is NOT.</b> Not a retry: nothing is ever re-run, and a run that fails still
/// fails. Not a skip and not an <c>allowedSkips</c> entry: when the lease cannot be taken the
/// collection FAILS, loudly, naming the process that holds the desktop. No assertion anywhere was
/// weakened to obtain it — the OS-level facts are byte-for-byte the ones SP-099 and SP-100
/// earned.</para>
///
/// <para><b>What it does NOT cover.</b> A FOREIGN topmost window — the shipping WPF product
/// re-asserting <c>HWND_TOPMOST</c> on a cadence (<c>Services/Flash/FlashService.cs:206-243</c>),
/// a screen locker, a full-screen game, a Magnifier — can still own a point on the real desktop,
/// and no in-process mechanism can exclude one. That residue is named in
/// <c>client/docs/verification-harness.md</c> as a gap the floor admits rather than hides.</para>
/// </summary>
[CollectionDefinition(nameof(RealDesktopCollection), DisableParallelization = true)]
public sealed class RealDesktopCollection : ICollectionFixture<RealDesktopLease>;

/// <summary>
/// An exclusive, machine-wide lease on "this process is the one putting windows on the real
/// desktop right now".
///
/// <para><b>Why a file handle and not a named <c>Mutex</c>.</b> A <c>Mutex</c> has thread
/// affinity — it must be released by the thread that took it — and xunit is free to construct a
/// collection fixture on one thread and dispose it on another. A <c>FileStream</c> opened with
/// <see cref="FileShare.None"/> is owned by the PROCESS, not by a thread, and the operating system
/// closes it if the process dies, so a crashed run cannot wedge the lease for the next one. That
/// is strictly better than the lock-file-existence scheme in <c>with-slot.mjs</c>, which needs a
/// reaper for exactly that case.</para>
/// </summary>
public sealed class RealDesktopLease : IDisposable
{
    private FileStream? _held;

    /// <summary>Takes the lease, or fails loudly naming what holds it.</summary>
    public RealDesktopLease()
    {
        var path = MachineWidePath;
        TestWait.UntilSync(
            () => TakeOnce(path),
            $"exclusive use of this machine's interactive desktop (lease file {path})",
            () => $"this process is {Environment.ProcessId}; another CcpClient.Tests process is inside its own "
                + "real-desktop collection right now. That is not a flake and must NOT be retried away: the "
                + "desktop is a singleton, and SP-107 measured what happens when two runs share it (8 red in 12 "
                + "concurrent runs). If this message is the failure, the other run took longer than the window.",
            TestWait.InjectedBudget);
    }

    /// <summary>The one lease every <c>CcpClient.Tests</c> process on this machine contends for.</summary>
    public static string MachineWidePath => Path.Combine(Path.GetTempPath(), "ccp-real-desktop.lease");

    /// <summary>True while this instance owns the desktop.</summary>
    public bool IsHeld => _held is not null;

    /// <summary>
    /// One attempt, with no wait of any kind: the exclusive open either wins or it does not.
    /// Returns the handle on success and null when someone else holds it.
    /// </summary>
    public static FileStream? TryTake(string path)
    {
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a delete-pending handle this way; it means held, same as IOException.
            return null;
        }
    }

    public void Dispose()
    {
        _held?.Dispose();
        _held = null;
    }

    /// <summary>
    /// Idempotent by construction: <see cref="TestWait.UntilSync"/> evaluates its condition again
    /// after the loop, and a second exclusive open from THIS process would fail exactly as a
    /// foreign one does. Re-checking <see cref="_held"/> first is what stops that second call from
    /// throwing the lease away.
    /// </summary>
    private bool TakeOnce(string path) => _held is not null || (_held = TryTake(path)) is not null;
}
