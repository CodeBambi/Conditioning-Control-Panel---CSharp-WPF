using System.Runtime.InteropServices;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The interactive desktop is a SINGLETON, and until this existed the suite addressed
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
/// <para><b>Measured, not supposed</b> (record §2): 20 floor runs one-at-a-time gave 0 red;
/// 12 floor runs in waves of 3 gave 8 red, and the failure text named the collision every time —
/// <c>IOException ... 'ccp-sp100-flash-draws\desktop-with-a-real-flash.bmp' because it is being
/// used by another process</c>, and <c>Assert.Equal() Expected 0, Actual 676161</c> where 676161
/// is exactly 951x711, the whole pixel area of ANOTHER run's flash.</para>
///
/// <para><b>The mechanism, in two halves.</b>
/// (1) IN-PROCESS: co-location. Every real-desktop class joins this collection, and xunit's
/// intra-collection sequentiality serializes them — the same mechanism, and the same honesty
/// about <c>DisableParallelization</c> being a non-relied-upon hint, as
/// <see cref="ProcessEnvCollection"/> (<c>DataRootOverrideTests.cs:116-122</c>).
/// Membership is mechanical: <see cref="RealDesktopCollectionGuardTests"/> fails when a class
/// that touches the desktop does not carry the attribute.
/// (2) CROSS-PROCESS: <see cref="RealDesktopLease"/>, an exclusive machine-wide lease held for
/// the life of this collection. A collection fixture is constructed before the collection's first
/// test and disposed after its last, so the whole real-desktop run happens under it.</para>
///
/// <para><b>What this is NOT.</b> Not a retry: nothing is ever re-run, and a run that fails still
/// fails. Not a skip and not an <c>allowedSkips</c> entry: when the lease cannot be taken the
/// collection FAILS, loudly, reporting whatever pid the lease file RECORDS and saying plainly when it
/// records none. That is not the same as identifying the live holder: the file keeps the last
/// writer's pid after a <c>Dispose</c>, and a refusal that is an ACL rather than a peer says so
/// instead of naming anyone. No assertion anywhere was
/// weakened to obtain it — the OS-level facts are byte-for-byte the ones the overlay packets
/// earned.</para>
///
/// <para><b>What it does NOT cover.</b> A FOREIGN topmost window — the shipping WPF product
/// re-asserting <c>HWND_TOPMOST</c> on a cadence (<c>Services/Flash/FlashService.cs:206-243</c>),
/// a screen locker, a full-screen game, a Magnifier — can still own a point on the real desktop,
/// and no in-process mechanism can EXCLUDE one. That residue is named in
/// <c>client/docs/verification-harness.md</c> as a gap the floor admits rather than hides.</para>
///
/// <para><b>The gap still cannot be excluded, and it is no longer silent.</b> Excluding a
/// foreign window and DETECTING one are different problems, and only the first is impossible.
/// <see cref="DesktopPreflight"/> runs in <see cref="RealDesktopLease"/>'s constructor, after the
/// lease is held and before this collection's first fact, and refuses by name when a window this
/// process does not own takes a contended point away from a top-most sentinel held there across
/// upstream's re-assert cadence. It is not a skip and it does not make anything green: a refusal
/// FAILS, once, at the fixture, instead of two or three times, late, in assertions that never
/// mention the window responsible.</para>
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
/// <para><b>The share mode is <see cref="FileShare.None"/>, and on the first Linux run of this suite
/// it was NOT, which is the whole reason this paragraph is here.</b> The earlier design opened the
/// lease with <see cref="FileShare.Read"/> so a contender could still read the holder's pid out of
/// it. On Windows that excludes: a second WRITE-open is refused because write sharing was not
/// granted. <b>On Linux it excludes nothing.</b> .NET maps a share mode to an ADVISORY <c>flock</c>
/// — <see cref="FileShare.None"/> to <c>LOCK_EX</c> and every other mode to <c>LOCK_SH</c> — so two
/// runs asking for <see cref="FileShare.Read"/> both took a SHARED lock and both got a live
/// <see cref="FileStream"/> back. Two processes would each have believed they held the one
/// interactive desktop, which is precisely the defect the lease exists to prevent, silently, on the
/// platform where nobody had looked yet.</para>
///
/// <para><b>So the exclusion and the identity are now two different files, because on Linux they
/// cannot be the same one.</b> The lease file carries the LOCK and no bytes anybody reads; the
/// holder's pid goes in a sidecar (<c>&lt;lease&gt;.holder</c>) that nothing holds open, so naming
/// WHO has the desktop still works while the lease is held — under an exclusive lock the lease file
/// itself is unreadable to a contender on BOTH platforms, and a failure message that could only
/// guess is what the first draft of this class shipped, interpolating the CONTENDER's own pid next
/// to a sentence asserting a peer existed.</para>
///
/// <para><b>And the exclusion is VERIFIED at construction rather than assumed.</b> A
/// <c>flock</c> is advisory and not every filesystem implements one — .NET ignores
/// <c>ENOTSUP</c>, which is exactly the shape of a lease that silently stops excluding (NFS, an SMB
/// mount, DrvFs over <c>/mnt/c</c>). The constructor therefore asks for the lease a SECOND time
/// while holding it and refuses loudly if that succeeds, so the next run to lose this mechanism
/// finds out at the fixture instead of in whichever OS-level fact loses the race.</para>
/// </summary>
public sealed class RealDesktopLease : IDisposable
{
    private FileStream? _held;
    private string? _lastRefusal;

    /// <summary>
    /// Takes the lease, or fails loudly reporting exactly why it could not — and then, holding it,
    /// runs the pre-flight and fails loudly again if something else owns the desktop.
    ///
    /// <para><b>The order is load-bearing.</b> The lease comes FIRST so that every peer
    /// <c>CcpClient.Tests</c> process and every <c>capture.ps1</c> run is already excluded
    /// (<c>client/docs/verification-harness.md:39</c>) by the time the pre-flight looks. A
    /// pre-flight that ran before the lease would spend its refusals naming our own harness.</para>
    /// </summary>
    public RealDesktopLease()
    {
        var path = MachineWidePath;
        TestWait.UntilSync(
            () => TakeOnce(path),
            $"exclusive use of this machine's interactive desktop (lease file {path})",
            () => DescribeRefusal(path),
            TestWait.InjectedBudget);

        // THE LEASE IS ASKED TO PROVE ITSELF, ONCE, WHILE IT IS HELD. A share mode is a promise the
        // FILESYSTEM keeps, and on Unix it is an advisory flock that a filesystem may not implement
        // at all (.NET ignores ENOTSUP, so the open just succeeds). A lease that has quietly stopped
        // excluding looks exactly like a lease that works, until two runs share the desktop — the
        // measured Linux failure. One extra open costs nothing and turns that into a refusal here.
        if (TryTake(path, out _) is { } notExcluded)
        {
            notExcluded.Dispose();
            throw new Xunit.Sdk.XunitException(
                $"the machine-wide desktop lease at {path} was taken, and then taken AGAIN while it was held. "
                + "The exclusion this collection rests on does not exist on this filesystem: a FileShare.None "
                + "open maps to an advisory flock on Unix and .NET ignores ENOTSUP where the filesystem has no "
                + "flock (NFS, SMB, WSL's DrvFs over /mnt/c). Two concurrent runs would BOTH believe they hold "
                + "the interactive desktop, which is the exact contention this fixture exists to prevent. Run "
                + "the suite from a filesystem that implements file locking, or give the lease a mechanism that "
                + "does not rest on open semantics.");
        }

        Preflight = DesktopPreflight.Observe();
        if (DesktopPreflight.Refusal(Preflight) is { } refusal)
        {
            throw new Xunit.Sdk.XunitException(refusal);
        }
    }

    /// <summary>
    /// What the pre-flight measured, kept so the collection's own broken-detector control
    /// (<c>DesktopPreflightTests</c>) can check that it really observed a desktop rather than
    /// silently degenerating into a clean verdict over zero readings.
    /// </summary>
    public DesktopPreflight.Observation Preflight { get; } = DesktopPreflight.NotObserved;

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
            // FileShare.None is the EXCLUSION on both platforms: a denied second write-open on
            // Windows, an exclusive flock on Unix. Every other share mode takes a SHARED flock there
            // and excludes nothing (see the class remarks). The identity is written afterwards, to a
            // sidecar, because this handle now makes the lease file unreadable to everyone else.
            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            WriteHolder(path);
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

    /// <summary>The sidecar that names the holder. Never held open by anyone, which is what makes it
    /// readable while the lease itself is locked shut.</summary>
    public static string HolderPathFor(string leasePath) => leasePath + ".holder";

    /// <summary>
    /// The holder's process id, read out of the sidecar, or null when nothing readable is there.
    /// It works WHILE the lease is held precisely because the sidecar is not the locked file.
    /// </summary>
    public static int? HolderProcessId(string path)
    {
        try
        {
            // ReadWrite sharing on BOTH sides (here and in WriteHolder) so a read that lands in the
            // same instant as a holder's write is answered rather than refused on Windows; a torn
            // read simply fails to parse and comes back null, which is the same answer as no file.
            using var reader = new FileStream(
                HolderPathFor(path), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[64];
            var read = reader.Read(buffer, 0, buffer.Length);
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            return text.StartsWith("pid=", StringComparison.Ordinal) && int.TryParse(text.AsSpan(4), out var pid)
                ? pid
                : null;
        }
        catch (IOException)
        {
            // FileNotFoundException and DirectoryNotFoundException are IOExceptions: a lease nobody
            // has taken yet has no sidecar, and "no readable holder" is the honest answer for it.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes this process's id beside the lease it has just taken. Best effort by design:
    /// the lease is already HELD by the time this runs, and an unnamed holder degrades a failure
    /// message rather than the exclusion.</summary>
    private static void WriteHolder(string path)
    {
        try
        {
            using var identity = new FileStream(
                HolderPathFor(path), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var bytes = System.Text.Encoding.UTF8.GetBytes($"pid={Environment.ProcessId}");
            identity.Write(bytes, 0, bytes.Length);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
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
            + "what happens when two runs share it was measured (8 red in 12 concurrent runs).";
    }
}

/// <summary>
/// <b>The pre-flight: a contended desktop says so, at the fixture, naming the window.</b>
///
/// <para><b>The problem this exists for.</b> The wave-66 land burned NINE full floor runs to learn
/// why it could not certify a green tree. Three green, six red; every failure a
/// <see cref="RealDesktopCollection"/> fact; the failing test MOVED between runs
/// (<c>VideoOverlayCoexistenceTests</c> four times, <c>PointerCapabilityTests</c> once,
/// <c>InputCapabilityTests</c> once); all of them passed 5/5 in isolation. The cause was the
/// shipping WPF product on the same desktop. <see cref="RealDesktopCollection"/> names that residue
/// in advance (<c>:45-49</c>) and admits it rather than hiding it. What was missing is that it
/// ANNOUNCES itself.</para>
///
/// <para><b>What it is NOT.</b> Not a skip, not a quarantine, not a retry, not an
/// <c>allowedSkips</c> entry, and deliberately not overridable by an environment variable — an
/// opt-out would be a quarantine wearing a different hat. A refusal here FAILS the run. What
/// changes is that it fails once, at the fixture, by name, instead of two or three times, late,
/// through assertions a reader has to reverse-engineer.</para>
///
/// <para><b>THE MEASUREMENT THAT DECIDED THE DESIGN.</b> The obvious detector — "a foreign
/// top-most window owns a contended point" — was built, run, and REJECTED on evidence. On the
/// machine this was written on, <c>ConditioningControlPanel v6.8.1</c> (pid 16712) held the
/// foreground in 12 of 12 samples and its <c>WS_EX_TOPMOST</c> main window owned all five probed
/// points in 12 of 12 samples, covering the whole contended band — <b>and the floor run under
/// exactly that contention was green, 2573/2573 and 152/152.</b> A detector built on presence
/// would have reddened 212 tests, that day, on a tree that was fine.</para>
///
/// <para><b>Why both are true, and it is one line upstream.</b>
/// <c>ChaosModeService.RunTick</c> returns at <c>Services/Chaos/ChaosModeService.cs:930</c> unless
/// <c>_spawning &amp;&amp; !_paused &amp;&amp; !_manualPaused</c>, and <c>KeepChromeTopmost</c>
/// returns again at <c>:787</c> unless <c>_spawning</c>. So an IDLE or PAUSED copy of the product
/// cannot re-assert at all: it is PARKED in the top-most band, and a window raised after it goes
/// above it. A RUNNING session re-asserts <c>HWND_TOPMOST</c> on a ~1 s cadence (<c>:937-940</c>,
/// four ticks of the <c>:503</c> 250 ms timer, reaching <c>App.Flash?.RaiseAllToFront()</c> at
/// <c>:772</c> and <c>:802</c> and thence <c>Services/Flash/FlashService.cs:206-243</c>) and climbs
/// back over whatever the suite raised. <b>The harm is the re-assertion, not the presence</b> —
/// which is also why wave-66 saw three green and six red with the same application present
/// throughout.</para>
///
/// <para><b>So this measures the capability the collection actually depends on:</b> can this
/// process put a window on top of the contended band and KEEP it there for longer than upstream's
/// re-assert period? Three sentinels are raised at the three x-offsets the suite's own rigs occupy,
/// lifted to the top of the band, and then WATCHED — never re-raised, because re-raising would take
/// the point back every poll and mask the very thing being detected.</para>
///
/// <para><b>The lift, and why it is needed at all.</b> A plain <c>SetWindowPos(HWND_TOPMOST)</c>
/// from a process that does not own the foreground lands BELOW the foreground process's own
/// top-most windows: measured here at z-order rank 6, under three <c>ConditioningControlPanel</c>
/// windows, the taskbar and <c>Click to Do</c>, with both windows reporting <c>GetWindowBand</c> = 1,
/// so it is the foreground restriction and not a band. The suite's own probes get above that by
/// attaching to the foreground thread's input queue (<c>InputWindowProbe.cs:295-318</c>, the
/// attach ladder), and this pre-flight uses the SAME lift minus <c>SetForegroundWindow</c> and
/// <c>BringWindowToTop</c>: measured to reach rank 1 and own the point while leaving the foreground
/// window UNCHANGED, so a floor run never steals the keyboard from whoever is at the machine. With
/// the product parked and idle it then held all three points for 250 of 250 readings.</para>
///
/// <para><b>What one sample can and cannot establish.</b> One sample establishes only that at that
/// instant our window did or did not own the point. It CANNOT distinguish holding the top from
/// holding it until the next beat, because the contender re-asserts on a cadence — so a
/// single-sample detector reports a clean desktop and then the same three tests fail anyway, which
/// is a green light in front of the same red. The <see cref="ObservationSpanMs"/> span covers at
/// least two full beats of <see cref="UpstreamReassertPeriodMs"/>, and the refusal is ANY-sample,
/// never a majority: one loss in 250 readings is a contender, not noise.</para>
///
/// <para><b>Named blind spots, both directions.</b>
/// MISSES: a contender whose period exceeds the span, or that starts after the fixture is built
/// (this is a PRE-flight and says so); anything outside the three probed points at the vertical
/// centre, INCLUDING A SECOND MONITOR, since the geometry comes from
/// <c>GetSystemMetrics(SM_CXSCREEN/SM_CYSCREEN)</c> — the PRIMARY display, the same number the
/// probes use; and a foreground-only thief that never raises a top-most window, because the
/// attach ladder (<c>Win32InputPresence.cs:525-579</c>) beats a passive foreground holder, which is
/// why the foreground is REPORTED in every refusal and never refused on by itself.
/// FALSE-POSITIVE DIRECTION, and it is the one this design must keep naming: a TRANSIENT ONE-SHOT
/// OCCLUDER — a notification toast, an installer popup, a window animating across the band once —
/// owning a point at a single sample inside the span refuses a run the suite would have passed.
/// The any-sample rule is deliberate and is what catches the cadence, so the evidence to tell the
/// two apart travels WITH the refusal instead of changing it: the first-loss round index, the
/// elapsed ms at that first loss, and the readings-lost-out-of-taken count. A one-shot reads as a
/// handful of readings at one index; a re-asserter reads as a large fraction from its first beat
/// onward.</para>
///
/// <para><b>It is falsifiable.</b> If wave-66's condition recurs and this reports a clean desktop
/// while those three tests fail, the detector is wrong.</para>
/// </summary>
public static class DesktopPreflight
{
    /// <summary>
    /// How long the sentinels are held and watched. Bounded on both sides by measurement rather
    /// than taste: it must exceed upstream's ~1 s re-assert period
    /// (<see cref="UpstreamReassertPeriodMs"/>) so at least two beats fall inside it, and it stays
    /// under the 5 s at which Windows ghosts a window whose thread has not pumped — which the
    /// per-poll message drain makes moot, and which is still the reason not to widen this to
    /// seconds. Paid once per test process, at fixture construction.
    /// </summary>
    public const int ObservationSpanMs = 2500;

    /// <summary>
    /// Upstream's re-assert period: <c>ChaosModeService.cs:937</c> fires every fourth tick of the
    /// <c>:503</c> <c>DispatcherTimer</c> of 250 ms, and <c>FlashService.cs:206-243</c> documents
    /// the same "~once a second" cadence in its own words.
    /// </summary>
    public const int UpstreamReassertPeriodMs = 1000;

    /// <summary>
    /// The floor under a real observation. A span of <see cref="ObservationSpanMs"/> at
    /// <c>TestWait</c>'s 10 ms poll yields ~250 rounds; this is an order of magnitude below that,
    /// so it binds "the loop really ran" without binding the machine's speed.
    ///
    /// <para><b>Where this is and is NOT enforced, because the asymmetry is deliberate.</b>
    /// <see cref="Refusal"/> does NOT consult this. It refuses on zero rounds, on zero owned
    /// readings and on a rig that could not be built — so a sampler that degenerated to a HANDFUL
    /// of rounds still passes the FIXTURE. That case is caught one level down, by the in-collection
    /// control in <c>DesktopPreflightTests</c>, which is a hard red on the same run rather than a
    /// softer signal. The split is on purpose: a fixture refusal fans out across every fact in the
    /// collection, so it is reserved for "the desktop is contended" and for "there is no
    /// observation at all", while "the observation is too thin to mean anything" is a defect in
    /// this suite and belongs to a test that names it.</para>
    /// </summary>
    public const int MinimumSamples = 20;

    /// <summary>The points probed per round, one per rig offset.</summary>
    public const int ContendedPoints = 3;

    /// <summary>The shipping WPF product, named because it is the residue's own example.</summary>
    public const string ShippingProduct = "ConditioningControlPanel";

    /// <summary>
    /// The x-offsets from the primary display's centre at which the suite's own rigs sit, so the
    /// pre-flight contends for the same band the facts do: <c>-260</c> is the overlay negative
    /// control (<c>OverlayWindowProbe.cs:286-289</c>, <c>((W-220)/2)-260</c> plus half of 220),
    /// <c>0</c> is where the observations place their surfaces, and <c>+300</c> is the input
    /// negative control (<c>InputWindowProbe.cs:387-393</c>, <c>((W-240)/2)+300</c> plus half of
    /// 240).
    /// </summary>
    private static readonly int[] RigOffsets = [-260, 0, 300];

    /// <summary>
    /// Our own harness and product. The lease already serialises these
    /// (<c>client/docs/verification-harness.md:39</c>: <c>capture.ps1</c> takes
    /// <c>%TEMP%/ccp-real-desktop.lease</c> before the app launches and until after it exits), so
    /// seeing one here is a HARNESS bug, and the refusal says so instead of sending the reader
    /// after a foreign application.
    /// </summary>
    private static readonly string[] OurFamily =
        ["CcpClient.Tests", "CcpClient.Desktop", "CcpVerify", "testhost"];

    private const int SentinelSize = 96;

    /// <summary>
    /// True where there is a desktop to observe. It lives here rather than in a test body because
    /// <c>VacuousShapeDetector</c> classifies a platform predicate inside a fact as a silencing
    /// shape — correctly: a fact that decides its own expectations by asking the OS can pass while
    /// asserting nothing.
    /// </summary>
    public static bool HostCanBeObserved => OperatingSystem.IsWindows();

    /// <summary>
    /// One (point, window) pair that took a contended point, with everything a reader needs
    /// captured AT THE FIRST LOSS, while that window is certainly still alive to be asked.
    /// </summary>
    public sealed record Loss(
        int PointIndex,
        int X,
        int Y,
        int FirstSampleIndex,
        long FirstElapsedMs,
        int Readings,
        nint Window,
        int ProcessId,
        string ProcessName,
        string Title,
        string ClassName,
        string Rect);

    /// <summary>
    /// What the pre-flight saw. <see cref="Observed"/> false means no desktop was observable at
    /// all, which is neither clean nor a refusal and must never be read as either.
    /// </summary>
    public sealed record Observation(
        bool Observed,
        int Samples,
        long SpanMs,
        int Points,
        int OwnedReadings,
        int LostReadings,
        IReadOnlyList<Loss> Losses,
        string ForegroundAtStart,
        string ForegroundAtEnd,
        string? RigFailure);

    /// <summary>The non-Windows answer: nothing was observed, and it says so.</summary>
    public static Observation NotObserved { get; } = new(
        Observed: false,
        Samples: 0,
        SpanMs: 0,
        Points: 0,
        OwnedReadings: 0,
        LostReadings: 0,
        Losses: [],
        ForegroundAtStart: "not observed: this host has no interactive desktop to observe",
        ForegroundAtEnd: "not observed: this host has no interactive desktop to observe",
        RigFailure: null);

    /// <summary>
    /// The verdict, PURE and separated from the sampling so it can be pinned against synthetic
    /// observations without touching a desktop (<c>DesktopPreflightVerdictTests</c>). Null is clean.
    /// </summary>
    public static string? Refusal(Observation observation)
    {
        if (!observation.Observed)
        {
            return null;
        }

        if (observation.Losses.Count > 0)
        {
            return Contended(observation);
        }

        // Clean REQUIRES evidence. A run that sampled nothing, or never once held a point, has
        // established nothing about the desktop, and reporting that as clean would be the exact
        // green-light-in-front-of-red this packet exists to prevent.
        if (observation.RigFailure is not null || observation.Samples == 0 || observation.OwnedReadings == 0)
        {
            return Blind(observation);
        }

        return null;
    }

    /// <summary>
    /// Raises the sentinels, lifts them to the top of the band, watches for
    /// <see cref="ObservationSpanMs"/>, and tears them down on EVERY path — the refusal throws from
    /// a constructor, and without the <c>finally</c> a contended run would leave three top-most
    /// windows on the owner's desktop for the life of the process.
    /// </summary>
    public static Observation Observe()
    {
        if (!HostCanBeObserved)
        {
            return NotObserved;
        }

        var width = GetSystemMetrics(SmCxscreen);
        var height = GetSystemMetrics(SmCyscreen);
        if (width <= 0 || height <= 0)
        {
            return NotObserved with
            {
                Observed = true,
                RigFailure = $"GetSystemMetrics reported a {width}x{height} primary display, so there is no band "
                    + "to contend for and no point could be probed",
            };
        }

        var sentinels = new List<Sentinel>();
        var points = new (int X, int Y)[RigOffsets.Length];
        var losses = new Dictionary<(int Point, nint Window), LossBuilder>();
        var foregroundAtStart = "not reached";
        var foregroundAtEnd = "not reached";
        string? rigFailure = null;
        var samples = 0;
        var owned = 0;
        var lost = 0;
        long spanMs = 0;

        try
        {
            for (var i = 0; i < RigOffsets.Length; i++)
            {
                var x = Math.Clamp(
                    (width / 2) + RigOffsets[i] - (SentinelSize / 2), 0, Math.Max(0, width - SentinelSize));
                var y = Math.Clamp(
                    (height / 2) - (SentinelSize / 2), 0, Math.Max(0, height - SentinelSize));
                points[i] = (x + (SentinelSize / 2), y + (SentinelSize / 2));

                var sentinel = Sentinel.Create(x, y, SentinelSize);
                if (sentinel is null)
                {
                    rigFailure = $"the pre-flight could not put its own window on this desktop (sentinel {i + 1} of "
                        + $"{RigOffsets.Length} at ({x},{y}) {SentinelSize}x{SentinelSize} failed to register or "
                        + $"create; Win32 error {Marshal.GetLastWin32Error()}). Nothing about the desktop has been "
                        + "established, so this reports as a blind pre-flight and never as a clean one";
                    break;
                }

                sentinels.Add(sentinel);
            }

            if (rigFailure is null)
            {
                foreach (var sentinel in sentinels)
                {
                    Lift(sentinel.Handle);
                }

                foregroundAtStart = DescribeForeground();
                var started = TestWait.MonotonicNow();

                // TestWait is the ONLY legal timing route in client/tests: TestTimingGuardTests
                // (:20-46) bans every other sleeping and elapsed-time construct outright across
                // this tree, and its marker escape hatch would need an edit to that guard, which
                // this packet's file scope does not include. So the helper supplies the sleeping,
                // this condition takes one reading per 10 ms poll, and TestWait.MonotonicNow --
                // named at TestWait.cs:47-51 as the one permitted clock read out here -- supplies
                // the span. This comment names no banned token because the guard is LINE-based and
                // reads comments: spelling one here is itself a violation, which is how the first
                // draft of this block failed at RealDesktopCollection.cs:525.
                TestWait.UntilSync(
                    () =>
                    {
                        Pump();
                        for (var p = 0; p < sentinels.Count; p++)
                        {
                            var owner = OwnerAt(points[p].X, points[p].Y);
                            if (owner == sentinels[p].Handle)
                            {
                                owned++;
                                continue;
                            }

                            lost++;
                            var key = (p, owner);
                            if (!losses.TryGetValue(key, out var builder))
                            {
                                builder = LossBuilder.Capture(
                                    p, points[p].X, points[p].Y, samples, TestWait.MonotonicNow() - started, owner);
                                losses[key] = builder;
                            }

                            builder.Readings++;
                        }

                        samples++;
                        return TestWait.MonotonicNow() - started >= ObservationSpanMs;
                    },
                    $"the desktop pre-flight's {ObservationSpanMs} ms observation span to elapse",
                    () => $"rounds so far={samples}, readings owned={owned}, readings lost={lost}",
                    TestWait.InjectedBudget);

                spanMs = TestWait.MonotonicNow() - started;
                foregroundAtEnd = DescribeForeground();
            }
        }
        finally
        {
            foreach (var sentinel in sentinels)
            {
                sentinel.Dispose();
            }
        }

        return new Observation(
            Observed: true,
            Samples: samples,
            SpanMs: spanMs,
            Points: sentinels.Count,
            OwnedReadings: owned,
            LostReadings: lost,
            Losses: [.. losses.Values.Select(b => b.Build()).OrderBy(l => l.FirstSampleIndex).ThenBy(l => l.PointIndex)],
            ForegroundAtStart: foregroundAtStart,
            ForegroundAtEnd: foregroundAtEnd,
            RigFailure: rigFailure);
    }

    /// <summary>
    /// The refusal a reader gets. It names the PROCESS, the TITLE, the pid and the ownership —
    /// wave-66 found this with <c>GetForegroundWindow</c> plus <c>GetWindowThreadProcessId</c>
    /// after eight runs of elimination, and the whole point of this text is that the ninth run is
    /// not needed.
    /// </summary>
    private static string Contended(Observation observation)
    {
        var readings = observation.OwnedReadings + observation.LostReadings;
        var text = new System.Text.StringBuilder();
        text.Append("DESKTOP-CONTENDED — the pre-flight refused before any real-desktop fact ran. ");
        text.Append("A window this process does not own took a contended point away from a top-most sentinel ");
        text.Append($"held there for {observation.SpanMs} ms: {observation.LostReadings} of {readings} readings ");
        text.Append($"lost across {observation.Samples} rounds over {observation.Points} point(s).");

        foreach (var loss in observation.Losses)
        {
            text.Append($"{Environment.NewLine}  point {loss.PointIndex + 1} at ({loss.X},{loss.Y}) was owned by ");
            text.Append($"process '{loss.ProcessName}' (pid {loss.ProcessId}), window 0x{loss.Window:X}, ");
            text.Append($"title '{loss.Title}', class '{loss.ClassName}', rect {loss.Rect}");
            text.Append($"{Environment.NewLine}      first lost at round {loss.FirstSampleIndex} of ");
            text.Append($"{observation.Samples} ({loss.FirstElapsedMs} ms into the span), {loss.Readings} reading(s)");
            text.Append(Note(loss));
        }

        text.Append($"{Environment.NewLine}  the foreground when the span opened: {observation.ForegroundAtStart}");
        text.Append($"{Environment.NewLine}  the foreground when it closed:      {observation.ForegroundAtEnd}");
        text.Append($"{Environment.NewLine}  ONE-SHOT OR CADENCE? A transient occluder (a toast, an installer ");
        text.Append("popup, a window animating across the band once) reads as a few readings at a single round; a ");
        text.Append("contender re-asserting on a cadence reads as a large fraction of the readings from its first ");
        text.Append("beat onward. The counts above are what tells them apart — the refusal is deliberately ");
        text.Append("ANY-sample, because a majority rule would miss the cadence this pre-flight exists to catch.");
        text.Append($"{Environment.NewLine}  This is NOT a flake, is NOT retried and is NOT skipped ");
        text.Append("(RealDesktopCollection.cs:36-39). A contended desktop cannot certify an OS-level fact in ");
        text.Append("either direction: wave-66 measured 3 green and 6 red across 9 floor runs with the same window ");
        text.Append("present, and the failing test moved between runs.");
        return text.ToString();
    }

    /// <summary>The per-loss annotation: which of the four kinds of owner this is.</summary>
    private static string Note(Loss loss)
    {
        if (loss.ProcessId == Environment.ProcessId)
        {
            return $"{Environment.NewLine}      *** THIS PROCESS owns that window, and it is not the sentinel. A "
                + "real-desktop rig from an earlier fact was left on the desktop, so the leak is in this suite and "
                + "not on the machine. ***";
        }

        if (OurFamily.Contains(loss.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            return $"{Environment.NewLine}      *** This is OUR OWN harness or product, which the machine-wide "
                + "lease is supposed to serialise: client/docs/verification-harness.md:39 has capture.ps1 take "
                + "%TEMP%/ccp-real-desktop.lease BEFORE the app launches and hold it until after it exits, and this "
                + "pre-flight runs while that lease is held. Seeing it here means that contract was broken — hunt a "
                + "harness bug, not a foreign application. ***";
        }

        if (loss.ProcessName.Equals(ShippingProduct, StringComparison.OrdinalIgnoreCase))
        {
            return $"{Environment.NewLine}      *** THIS IS THE SHIPPING WPF PRODUCT — the residue "
                + "RealDesktopCollection.cs:45-49 names in advance. It re-asserts HWND_TOPMOST on a ~1 s cadence "
                + "(Services/Flash/FlashService.cs:206-243, driven from Services/Chaos/ChaosModeService.cs:937-940). "
                + "An IDLE or PAUSED copy cannot do this — RunTick returns at ChaosModeService.cs:930 unless a "
                + "session is spawning and unpaused — so this window is not merely OPEN, it has a session RUNNING. "
                + "Stop that session, or close the application, and run again. ***";
        }

        return $"{Environment.NewLine}      *** A foreign application: not the shipping WPF product and not ours. "
            + "No in-process mechanism can exclude one (RealDesktopCollection.cs:45-49). ***";
    }

    private static string Blind(Observation observation) =>
        "PREFLIGHT-BLIND — the desktop pre-flight could not observe the desktop, so it reports that it "
        + "established NOTHING rather than reporting a clean one. "
        + $"rig failure: {observation.RigFailure ?? "none"}; rounds={observation.Samples}; "
        + $"span={observation.SpanMs} ms; points={observation.Points}; readings owned={observation.OwnedReadings}; "
        + $"readings lost={observation.LostReadings}; foreground at open: {observation.ForegroundAtStart}. "
        + "A detector that saw nothing must never read as a clean desktop: that is a green light in front of "
        + "whatever red follows, which is worse than no light at all. This is a failure, never a skip.";

    /// <summary>
    /// The z-order lift, and the reason the pre-flight can work at all. The attach ladder
    /// (<c>InputWindowProbe.cs:295-318</c>) minus <c>SetForegroundWindow</c> and
    /// <c>BringWindowToTop</c>: attaching to the foreground thread's input queue is what lets
    /// <c>SetWindowPos(HWND_TOPMOST)</c> reach the TOP of the top-most band instead of the bottom
    /// of it, and dropping the other two rungs is what keeps the foreground where the user left it.
    /// Measured: rank 6 before, rank 1 after, foreground unchanged.
    /// </summary>
    private static void Lift(nint window)
    {
        var foreground = GetForegroundWindow();
        var ourThread = GetCurrentThreadId();
        var foregroundThread = foreground == 0 ? ourThread : GetWindowThreadProcessId(foreground, out _);
        var attached = foregroundThread != ourThread && AttachThreadInput(ourThread, foregroundThread, true);

        SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);

        if (attached)
        {
            AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    /// <summary>
    /// A non-blocking drain, so holding the sentinels for 2.5 s never makes the OS treat them as a
    /// hung window and ghost them out from under the measurement.
    /// </summary>
    private static void Pump()
    {
        while (PeekMessageW(out var message, 0, 0, 0, PmRemove))
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    /// <summary>
    /// The top-level window owning a screen point, resolved to its root so a child control never
    /// reads as a different owner than the window it belongs to.
    /// </summary>
    private static nint OwnerAt(int x, int y)
    {
        var window = WindowFromPoint(new Point { X = x, Y = y });
        return window == 0 ? 0 : GetAncestor(window, GaRoot);
    }

    private static string DescribeForeground()
    {
        var window = GetForegroundWindow();
        if (window == 0)
        {
            return "no window held the foreground";
        }

        var thread = GetWindowThreadProcessId(window, out var processId);
        return $"'{TitleOf(window)}' (process '{NameOf((int)processId)}', pid {processId}, thread {thread}, "
            + $"window 0x{window:X}, class '{ClassOf(window)}')";
    }

    private static string TitleOf(nint window)
    {
        var buffer = new System.Text.StringBuilder(256);
        GetWindowTextW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ClassOf(nint window)
    {
        var buffer = new System.Text.StringBuilder(256);
        GetClassNameW(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string RectOf(nint window) =>
        GetWindowRect(window, out var rect)
            ? $"({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})"
            : "<unreadable>";

    /// <summary>
    /// A pid's process name, or an honest marker. Never throws: this runs while a failure message
    /// is being built, and a second exception there would replace the diagnosis with noise.
    /// </summary>
    private static string NameOf(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "<process already gone>";
        }
        catch (InvalidOperationException)
        {
            return "<process already gone>";
        }
    }

    /// <summary>
    /// Accumulates one (point, window) loss. Everything except the count is captured at the FIRST
    /// loss, while the offending window is certainly still alive to be asked.
    /// </summary>
    private sealed class LossBuilder
    {
        private Loss _seed = null!;

        public int Readings { get; set; }

        public static LossBuilder Capture(int point, int x, int y, int sample, long elapsedMs, nint window)
        {
            var processId = 0;
            if (window != 0)
            {
                GetWindowThreadProcessId(window, out var raw);
                processId = (int)raw;
            }

            return new LossBuilder
            {
                _seed = new Loss(
                    PointIndex: point,
                    X: x,
                    Y: y,
                    FirstSampleIndex: sample,
                    FirstElapsedMs: elapsedMs,
                    Readings: 0,
                    Window: window,
                    ProcessId: processId,
                    ProcessName: window == 0 ? "<no window owns that point>" : NameOf(processId),
                    Title: window == 0 ? string.Empty : TitleOf(window),
                    ClassName: window == 0 ? string.Empty : ClassOf(window),
                    Rect: window == 0 ? "<none>" : RectOf(window)),
            };
        }

        public Loss Build() => _seed with { Readings = Readings };
    }

    /// <summary>
    /// One top-most sentinel. Same shape as <c>OverlayWindowProbe.cs:341-388</c>'s scratch rig and
    /// deliberately NOT a reference to it: this fixture GATES the probes, so depending on one would
    /// let a broken probe break the gate that is supposed to judge it. Opaque rather than layered,
    /// because opaque is the style the rank-1 lift was measured on, and an unverified assumption
    /// about layered hit-testing is not worth the pixels it would save.
    /// </summary>
    private sealed class Sentinel : IDisposable
    {
        private readonly string _className;
        private readonly WndProc _proc;
        private readonly nint _module;
        private bool _disposed;

        private Sentinel(string className, WndProc proc, nint module, nint handle)
        {
            _className = className;
            _proc = proc;
            _module = module;
            Handle = handle;
        }

        public nint Handle { get; private set; }

        public static Sentinel? Create(int x, int y, int size)
        {
            var className = "CcpDesktopPreflight." + Guid.NewGuid().ToString("N");
            WndProc proc = DefWindowProcW;
            var module = GetModuleHandleW(null);
            var descriptor = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = module,
                lpszClassName = className,
                hbrBackground = 6, // COLOR_WINDOW + 1: it must be a real, hit-testable surface
            };

            if (RegisterClassExW(ref descriptor) == 0)
            {
                return null;
            }

            var handle = CreateWindowExW(
                WsExToolwindow | WsExNoactivate, className, "ccp desktop pre-flight sentinel",
                WsPopup, x, y, size, size, 0, 0, module, 0);
            if (handle == 0)
            {
                UnregisterClassW(className, module);
                return null;
            }

            SetWindowPos(handle, HwndTopmost, x, y, size, size, SwpNoactivate | SwpShowwindow);
            return new Sentinel(className, proc, module, handle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (Handle != 0)
            {
                DestroyWindow(Handle);
                Handle = 0;
            }

            UnregisterClassW(_className, _module);
            GC.KeepAlive(_proc);
        }
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const uint GaRoot = 2;
    private const uint PmRemove = 1;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExNoactivate = 0x08000000;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private static readonly nint HwndTopmost = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassExW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW descriptor);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, nint module);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint module, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg message, nint window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DispatchMessageW(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

/// <summary>
/// <b>One hidden, never-shown window per thread that injects a click, because a thread which
/// reaches ZERO top-level windows after one of them was clicked costs the WHOLE PROCESS the
/// top-most band — silently, and for good.</b>
///
/// <para><b>The rule, measured rather than reasoned.</b>
/// <see href="https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos">
/// <c>SetWindowPos</c></see> states the dependency outright: <i>"To use SetWindowPos to bring a
/// window to the top, the process that owns the window must have SetForegroundWindow
/// permission."</i> When that permission is absent, <c>SetWindowPos(HWND_TOPMOST)</c> RETURNS TRUE
/// AND APPLIES NOTHING — no error and no last-error, which is why the product's own bounded
/// re-assertion loop (<c>Overlay/Win32OverlayPresence.cs:504-546</c>) cannot recover from it: it is
/// a refusal, not a race. <c>SetForegroundWindow</c>'s remarks list the conditions that grant the
/// permission, and on a machine whose <c>ForegroundLockTimeout</c> is large the "time-out has
/// expired" escape never fires.</para>
///
/// <para><b>What was measured, outside this suite, in about sixty lines of P/Invoke.</b>
/// (1) Create a <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c> pop-up, destroy it: the band survives.
/// (2) Create it, <c>SendInput</c> a click at it, destroy it: the band is gone and stays gone.
/// (3) Keep ANY other top-level window alive on the SAME THREAD across that destruction —
/// including a hidden, never-shown, zero-sized one: the band survives.
/// (4) A floor window held on a DIFFERENT thread does NOT help, which is why this is
/// <see cref="ThreadStaticAttribute"/> and not a fixture-lifetime singleton: the trigger is scoped
/// to the thread that owned the clicked window.
/// (5) Once triggered the loss is PROCESS-WIDE — a different, never-poisoned thread is refused
/// too — so one unprotected click poisons every later real-desktop fact in the run.</para>
///
/// <para><b>How the suite reached that state.</b> <c>PointerSurfaceObservations.RunDelivery</c>
/// injects clicks into its own scratch targets and then disposes them; whichever target is disposed
/// LAST takes its thread to zero windows, and every real-desktop fact afterwards is measured
/// against a process that can no longer place a window in the top-most band. Bisected to that
/// method by instrumenting seventeen points inside it: the band is held at every checkpoint in the
/// body and lost at the <c>using</c> disposals, on whichever scratch target is disposed last —
/// reversing the disposal order moves the loss to the other one.</para>
///
/// <para><b>Why this is not a paper-over.</b> It does not weaken a fact, retry one, or repair the
/// permission after the fact. Repairing it with a teardown <c>SetForegroundWindow</c> would work
/// and would be WRONG: that masks the revocation instead of not causing it, and would hide the next
/// real regression of this class. What this does instead is make the test process REPRESENTATIVE of
/// the product — the shipping app always owns at least one top-level window, the Avalonia main
/// window or <c>Tray/Win32TrayPresence.cs</c>'s hidden owner window, so it can never walk itself to
/// zero. The harness was entering a state the product cannot enter, and then reporting the
/// consequences as product failures.</para>
///
/// <para><b>Why it cannot perturb what the collection measures.</b> The window is zero-sized, is
/// never shown, is never raised and never joins the top-most band: it can win no hit test, own no
/// point, occlude nothing, and it is absent from every <c>IsWindowVisible</c> enumeration this
/// suite performs — including <see cref="DesktopPreflight"/>'s sentinel arbitration, which resolves
/// owners with <c>WindowFromPoint</c> and so can never name a window that covers no pixel. It is
/// <c>WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE</c>, so it is absent from the taskbar and Alt-Tab and can
/// never take activation from the surfaces under test.</para>
///
/// <para><b>Named limits.</b> It is armed at <c>PointerWindowProbe.InjectClickAt</c>, the only
/// place in this suite that injects a click; a future probe that synthesises input by some other
/// route must call <see cref="Ensure"/> itself, and nothing mechanical enforces that yet. The
/// window is never destroyed: it dies with its thread or with the process, because
/// <c>DestroyWindow</c> is refused from any thread but the owning one, and a floor that could be
/// torn down early would be a floor that is not a floor. Whether the same revocation is armed by
/// injected KEYSTROKES rather than clicks is untested — <c>InputWindowProbe</c>'s facts take the
/// foreground outright, so they hold the permission by a different route.</para>
/// </summary>
internal static class RealDesktopWindowFloor
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExNoactivate = 0x08000000;

    [ThreadStatic]
    private static nint _floor;

    /// <summary>
    /// Idempotent, per-thread, and cheap enough to sit on the injection path: after the first call
    /// on a thread it is one <c>IsWindow</c> check.
    /// </summary>
    internal static void Ensure()
    {
        if (!OperatingSystem.IsWindows() || (_floor != 0 && IsWindow(_floor)))
        {
            return;
        }

        // "Static" is a stock system class, so there is no class to register and no window procedure
        // to keep alive. Zero-sized and NEVER shown: it has to be countable, not visible.
        _floor = CreateWindowExW(
            WsExToolwindow | WsExNoactivate, "Static", "CcpRealDesktopWindowFloor", WsPopup,
            0, 0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>This thread's floor handle, or 0 where none is up. Read by the collection's own
    /// control so a floor that silently stopped being created cannot pass unnoticed.</summary>
    internal static nint Window => _floor;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);
}
