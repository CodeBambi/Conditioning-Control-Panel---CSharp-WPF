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
/// collection fixture on one thread and dispose it on another. A <c>FileStream</c> is owned by the
/// PROCESS, not by a thread, and the operating system closes it if the process dies, so a crashed
/// run cannot wedge the lease for the next one. That is strictly better than the
/// lock-file-existence scheme in <c>with-slot.mjs</c>, which needs a reaper for exactly that
/// case.</para>
///
/// <para><b>The share mode is <see cref="FileShare.Read"/>, not <see cref="FileShare.None"/>, and
/// that is the whole difference between CLAIMING to name the holder and naming it.</b> The holder
/// opens for WRITE and writes its own process id into the file; a contender's write-open is refused
/// because write sharing is not granted, and a contender's READ-open still succeeds, so it can say
/// WHO has the desktop instead of asserting that somebody must. Under <c>FileShare.None</c> the
/// file is unreadable while held and the failure message could only ever have been a guess — which
/// is what the first draft of this class shipped, interpolating the CONTENDER's own pid next to a
/// sentence asserting a peer existed.</para>
/// </summary>
public sealed class RealDesktopLease : IDisposable
{
    private FileStream? _held;
    private string? _lastRefusal;

    /// <summary>Takes the lease, or fails loudly reporting exactly why it could not.</summary>
    public RealDesktopLease()
    {
        var path = MachineWidePath;
        TestWait.UntilSync(
            () => TakeOnce(path),
            $"exclusive use of this machine's interactive desktop (lease file {path})",
            () => DescribeRefusal(path),
            TestWait.InjectedBudget);
    }

    /// <summary>The one lease every <c>CcpClient.Tests</c> process on this machine contends for.</summary>
    public static string MachineWidePath => Path.Combine(Path.GetTempPath(), "ccp-real-desktop.lease");

    /// <summary>True while this instance owns the desktop.</summary>
    public bool IsHeld => _held is not null;

    /// <summary>
    /// One attempt, with no wait of any kind: the write-open either wins or it does not. On success
    /// the holder's process id is in the file, so a contender can read it.
    /// </summary>
    public static FileStream? TryTake(string path) => TryTake(path, out _);

    /// <summary>
    /// As <see cref="TryTake(string)"/>, and says WHY on refusal. The distinction is load-bearing:
    /// an <see cref="UnauthorizedAccessException"/> is an ACL, a read-only volume or a file-locking
    /// scanner, and reporting that as "another test process holds the desktop" sends the reader
    /// after a process that does not exist.
    /// </summary>
    internal static FileStream? TryTake(string path, out string? refusal)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            var identity = System.Text.Encoding.UTF8.GetBytes($"pid={Environment.ProcessId}");
            stream.Write(identity, 0, identity.Length);
            stream.Flush();
            refusal = null;
            return stream;
        }
        catch (IOException error)
        {
            stream?.Dispose();
            refusal = $"the lease is open for writing elsewhere ({error.GetType().Name}: {error.Message})";
            return null;
        }
        catch (UnauthorizedAccessException error)
        {
            stream?.Dispose();
            refusal = $"the lease file could not be opened AT ALL ({error.GetType().Name}: {error.Message}). "
                + "That is an ACL, a read-only volume, or a file-locking scanner — NOT another test process "
                + "holding the desktop, and no peer should be hunted for it";
            return null;
        }
    }

    /// <summary>
    /// The holder's process id, read out of the lease file, or null when nothing readable is there.
    /// It works WHILE the lease is held precisely because the holder grants read sharing.
    /// </summary>
    public static int? HolderProcessId(string path)
    {
        try
        {
            using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[64];
            var read = reader.Read(buffer, 0, buffer.Length);
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            return text.StartsWith("pid=", StringComparison.Ordinal) && int.TryParse(text.AsSpan(4), out var pid)
                ? pid
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
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
    /// after the loop, and a second write-open from THIS process would fail exactly as a foreign one
    /// does. Re-checking <see cref="_held"/> first is what stops that second call from throwing the
    /// lease away.
    /// </summary>
    private bool TakeOnce(string path)
    {
        if (_held is not null)
        {
            return true;
        }

        _held = TryTake(path, out _lastRefusal);
        return _held is not null;
    }

    /// <summary>
    /// What the failure says. It reports the holder's pid when the file names one, and says plainly
    /// that it does not know when it cannot read one — never "another process has it" on no evidence.
    /// </summary>
    private string DescribeRefusal(string path)
    {
        var holder = HolderProcessId(path);
        var who = holder is { } pid
            ? (pid == Environment.ProcessId
                ? $"the lease file names THIS process ({pid}), so an earlier lease of ours was left behind rather "
                    + "than a peer holding it"
                : $"the lease file names process {pid} as the holder")
            : "the lease file names no readable holder, so WHO has the desktop is unknown";

        return $"this process is {Environment.ProcessId}; {who}. Refusal: {_lastRefusal ?? "none recorded"}. "
            + "A contended desktop is not a flake and must NOT be retried away: the desktop is a singleton, and "
            + "SP-107 measured what happens when two runs share it (8 red in 12 concurrent runs).";
    }
}
