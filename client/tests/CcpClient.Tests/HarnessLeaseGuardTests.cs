using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>THE LEASE SERIALISES TEST PROCESSES, AND UNTIL THIS EXISTED NOTHING SERIALISED THE HARNESS.</b>
///
/// <para><see cref="RealDesktopCollection"/> and <see cref="RealDesktopLease"/> close the
/// test-process-versus-test-process half of the problem, and <see cref="RealDesktopCollectionGuardTests"/>
/// makes membership mechanical so a new probe CLASS cannot quietly rejoin the racy default collection.
/// The other half was never guarded at all: a lane running <c>client/tools/verify/capture.ps1</c> or
/// <c>client/tools/verify/capture-wslg.sh</c> puts a top-most window on the SAME machine-global desktop
/// and drives REAL synthetic input into it, and nothing in the suite could see it.</para>
///
/// <para><b>WHAT THAT COSTS, MEASURED ON THIS MACHINE RATHER THAN ARGUED.</b> A stand-in harness run —
/// a second process raising a top-most window over the primary display and clicking on a 400 ms cadence,
/// which is exactly what <c>capture.ps1</c>'s own <c>Click-Rect</c> does — was started 5 s into a filtered
/// floor run of the eight real-desktop input classes (91 facts, 91/91 green with the machine quiet).
/// UNLEASED: <b>10 red runs out of 10</b>, 1 to 8 named failures each, and the failing facts MOVED between
/// runs. LEASED, identical timing and the lease as the only variable: <b>0 red out of 10</b>, with the
/// harness measured waiting 4.08 s to 6.41 s for the run to let the lease go, so it was excluded rather
/// than merely lucky. The names that came out of the unleased runs are the board's own three-times-misdiagnosed
/// signature — <c>the drag did not hold its path</c> in
/// <c>OverlayDesktopInputTests.cs:173</c>, a synthesised keystroke that never became a character in
/// <c>InputCapabilityTests.cs:221</c>, and a foreground stolen by a foreign
/// <c>WindowsForms10.Window</c> class in <c>PointerCoexistenceTests.cs:133</c>.</para>
///
/// <para><b>THE PRE-FLIGHT AND THE LEASE COVER DIFFERENT HALVES, AND BOTH ARE NEEDED.</b> The same
/// stand-in harness started BEFORE the run is caught by <see cref="DesktopPreflight"/>, which refuses at
/// the fixture and names the window (measured: 36 of 38 facts red with the pre-flight's own refusal as the
/// reason). But the pre-flight is a PRE-flight and says so: a harness that starts DURING a run is invisible
/// to it, which is the 10-of-10 condition above. The lease is what covers that, because it is taken for the
/// whole life of both processes rather than sampled once.</para>
///
/// <para><b>WHY A SOURCE WALK.</b> The harness is PowerShell and bash. Nothing about it is reflectable and
/// nothing about it links against this assembly, so the only mechanical grip available is its text — the
/// same grip, and the same lineage, as <see cref="RealDesktopCollectionGuardTests"/>: repo-root walk, never
/// skips, fails closed, file-named violations.</para>
///
/// <para><b>THIS GUARD EXISTS BECAUSE THE DRIFT ALREADY HAPPENED, SILENTLY.</b> <c>capture.ps1</c> DID take
/// the lease, on the contract that was current the day it was written: <c>FileShare.Read</c> with the
/// holder's pid written into the lease body. <see cref="RealDesktopLease"/> then moved the identity into a
/// sidecar, because a file held under an exclusive lock is unreadable to a contender. Nothing noticed.
/// The exclusion survived by luck of Windows share-mode semantics (probed in both orders: a
/// <c>FileShare.Read</c> holder still refuses a <c>FileShare.None</c> open and vice versa), but the IDENTITY
/// half was broken in both directions — measured against <c>capture.ps1</c> as it stood at that commit, a
/// floor run refused by a live capture read the sidecar and got NOTHING back, so it named a stale pid or no
/// holder at all. After the fix the same probe reads the capture's real pid. A guard that only checked
/// "does the harness mention a lease" would have stayed green through all of it, which is why the facts
/// below bind the contract's VALUES, read out of <see cref="RealDesktopLease"/> itself.</para>
///
/// <para><b>THE TWO DIRECTIONS READ DIFFERENT TEXT, AND A MUTATION RUN IS WHY.</b> "Does this file reach
/// the desktop" is asked of the RAW text, comments included, because over-including there only ever demands
/// a lease of a file that might not have needed one. "Does this file TAKE the lease" is asked of CODE LINES
/// ONLY — a line whose first non-whitespace character is <c>#</c> is dropped — because the first draft asked
/// it of the raw text and the broken-detector run caught it: deleting the lease from <c>capture-wslg.sh</c>
/// outright left this guard GREEN, since the paragraph explaining the lease still named the file. A guard
/// satisfied by PROSE ABOUT a mechanism is worse than no guard, because it reports the mechanism present.
/// Two of five mutations were green before this split; all five red after.</para>
///
/// <para><b>HONESTY, both directions.</b> This is LEXICAL and binds at FILE granularity.
/// MISSES: a harness entry point outside <c>client/tools/verify</c>; a file that reaches the desktop
/// through a library call this census does not name; a pinned helper INVOKED BY HAND rather than through
/// its entry point, which takes no lease and which nothing textual can see — the pin is a claim about how
/// the file is CALLED, and giving the helpers their own lease would deadlock them under the parent that
/// already holds it; and — the big one — whether the bytes the harness
/// writes into the sidecar actually PARSE, since proving that from here would mean taking the real
/// machine-wide lease from a class that is deliberately not in the collection. That leg was measured by
/// hand instead, both scripts, and is recorded above. <c>wmclose.py</c> is deliberately NOT in the census:
/// it sends one <c>WM_DELETE_WINDOW</c> client message to a window found by title needle, and takes no
/// pixels, no z-order and no input — a reviewed exclusion rather than an oversight.
/// FALSE-POSITIVE DIRECTION: a token inside a comment counts, so a harness file that only DISCUSSES
/// screen capture is read as doing it. That is the safe direction and it is the same trade
/// <see cref="RealDesktopCollectionGuardTests"/> takes.</para>
/// </summary>
public class HarnessLeaseGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] HarnessParts = ["client", "tools", "verify"];

    /// <summary>
    /// The calls that put a window on the interactive desktop, read pixels off it, or push events into
    /// the machine's one input stream. A harness file carrying any of these contends with every
    /// <see cref="RealDesktopCollection"/> fact on the machine.
    /// </summary>
    private static readonly string[] ContendingCalls =
    [
        "CopyFromScreen",      // Windows screen read
        "XGetImage",           // X11 screen read
        "mouse_event(",        // Windows synthetic pointer
        "keybd_event(",        // Windows synthetic keyboard
        "SetCursorPos(",       // Windows cursor placement
        "XTestFake",           // X11 synthetic pointer and keyboard
        "HWND_TOPMOST",        // Windows top-most raise
    ];

    /// <summary>
    /// Files that reach the desktop but are never entry points: they run only as children of a leasing
    /// entry point, which already holds the desktop when they start. Taking the lease again from here
    /// would DEADLOCK against the parent, so the pin is the remedy and not an exemption — and it is a
    /// checked pin, because
    /// <see cref="EveryPinnedHelper_IsReallyInvokedFromALeasingEntryPoint_OrThePinIsAFictionThatDeadlocks"/>
    /// requires a leasing entry point to actually name each one.
    /// </summary>
    private static readonly string[] HelperFiles = ["xgetimage.py", "xinput.py"];

    /// <summary>Broken-detector controls: these two must always come out as leasing entry points.</summary>
    private static readonly string[] EntryPointControls = ["capture.ps1", "capture-wslg.sh"];

    /// <summary>
    /// The exclusive-open token each entry point must carry, keyed by file. This is the DRIFT ITSELF:
    /// <c>capture.ps1</c> held <c>FileShare.Read</c>, which excludes on Windows and — as
    /// <see cref="RealDesktopLease"/>'s own remarks record from a measured Linux run — maps to a SHARED
    /// <c>flock</c> on Unix and excludes nothing whatever. Only <c>FileShare.None</c> is the contract
    /// (<c>RealDesktopCollection.cs:183</c>), and <c>flock -x</c> is the same primitive from bash: .NET
    /// implements <c>FileShare.None</c> on Unix AS <c>flock(LOCK_EX)</c>, so these are one mechanism and
    /// not two. Measured on this machine's WSL2 kernel, all four directions: <c>flock(1)</c> holding
    /// refuses <c>FileShare.None</c>; <c>FileShare.None</c> holding refuses <c>flock(1)</c>; two
    /// <c>flock(1)</c> holders refuse each other; and a <c>SIGKILL</c>ed holder leaves the lock FREE.
    /// </summary>
    private static readonly (string File, string Token, string Why)[] ExclusiveOpenTokens =
    [
        // Anchored to the VARIABLE and the closing parenthesis, never to the bare share mode.
        // RackPresentationTests pinned the bare mode and went inert on exactly that: its needle
        // "...[IO.FileShare]::Read" is a PREFIX of the sidecar open's "...::ReadWrite" and silently
        // started matching the wrong line, so the drift this guard exists for read as green there.
        ("capture.ps1", "$script:leasePath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)",
            "the ONLY share mode that excludes. FileShare.Read - what this script held until 2026-08-25 - "
            + "denies a second write-open on Windows but maps to a SHARED flock on Unix, where two runs would "
            + "both believe they hold the one interactive desktop"),
        ("capture-wslg.sh", "flock -w 300 -x 9",
            "an EXCLUSIVE flock on a shell file descriptor. -x is the exclusion; the fd is held by the script "
            + "itself so the kernel drops it however the run ends, including SIGKILL; and -w bounds the wait so "
            + "a contended desktop refuses loudly instead of hanging a lane forever"),
    ];

    /// <summary>
    /// Every headed harness entry point takes the machine-wide lease.
    ///
    /// <para>The rule is the same one <see cref="RealDesktopCollectionGuardTests"/> applies to test classes,
    /// pointed at the other family of processes that reach the same desktop: reach it, or be a pinned child
    /// of something that leases it. There is no third option and deliberately no environment-variable
    /// opt-out — an opt-out here would be a quarantine wearing a different hat, exactly as the pre-flight's
    /// own remarks say about its own.</para>
    /// </summary>
    [Fact]
    public void EveryHeadedHarnessEntryPoint_TakesTheSameMachineWideLeaseTheRealDesktopFactsTake()
    {
        var leaseFileName = Path.GetFileName(RealDesktopLease.MachineWidePath);
        var files = HarnessSources();
        var violations = new List<string>();
        var leasing = new List<string>();

        foreach (var (name, raw, code) in files)
        {
            // RAW for "does it reach the desktop": over-including a file that only discusses a screen read
            // costs a lease it did not need. CODE for "does it take the lease": under-including there is how
            // a paragraph about the mechanism came to stand in for the mechanism.
            var reasons = ContendingCalls.Where(c => raw.Contains(c, StringComparison.Ordinal)).ToArray();
            if (reasons.Length == 0)
            {
                continue;
            }

            if (HelperFiles.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (code.Contains(leaseFileName, StringComparison.Ordinal))
            {
                leasing.Add(name);
                continue;
            }

            violations.Add($"client/tools/verify/{name}: reaches the interactive desktop "
                + $"[{string.Join("; ", reasons)}] but never names {leaseFileName}. The desktop is MACHINE-global: "
                + "this run contends with every real-desktop fact in every CcpClient.Tests process on the machine, "
                + "measured at 10 red floor runs out of 10 with an unleased harness beside them and 0 out of 10 with "
                + "a leased one. Take the lease, or - if this file only ever runs as a child of one that does - pin "
                + "it in HelperFiles, which is checked rather than believed.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Equal(
            EntryPointControls.OrderBy(n => n, StringComparer.Ordinal),
            leasing.Where(EntryPointControls.Contains).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// The harness's lease is <see cref="RealDesktopLease"/>'s lease, value for value — not a parallel
    /// scheme that happens to use a file.
    ///
    /// <para>Every expectation below is READ OUT OF <see cref="RealDesktopLease"/> at run time rather than
    /// typed here, so renaming the lease file, moving the identity sidecar or changing the identity prefix
    /// reds this guard on the same commit instead of silently unhooking the harness. That is the exact
    /// failure this file was written after: the sidecar moved and two scripts kept writing to the old place
    /// for weeks, with the exclusion still working and the "who has the desktop" half quietly dead.</para>
    /// </summary>
    [Fact]
    public void TheHarnessLease_IsTheSameFileTheSameSidecarAndTheSameExclusiveOpen_ReadFromRealDesktopLeaseItself()
    {
        var leaseFileName = Path.GetFileName(RealDesktopLease.MachineWidePath);
        var sidecarSuffix = RealDesktopLease.HolderPathFor(string.Empty);
        var files = HarnessSources().ToDictionary(f => f.Name, f => f.Code, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var name in EntryPointControls)
        {
            if (!files.TryGetValue(name, out var text))
            {
                violations.Add($"client/tools/verify/{name}: not found. This guard's controls are the harness "
                    + "entry points themselves; a missing one means the walk is reading the wrong tree and every "
                    + "verdict below is worthless.");
                continue;
            }

            if (!text.Contains(leaseFileName, StringComparison.Ordinal))
            {
                violations.Add($"client/tools/verify/{name}: does not name the lease file '{leaseFileName}'.");
            }

            if (!text.Contains(sidecarSuffix, StringComparison.Ordinal))
            {
                violations.Add($"client/tools/verify/{name}: does not name the identity sidecar suffix "
                    + $"'{sidecarSuffix}'. The lease file itself is held under an EXCLUSIVE lock and is therefore "
                    + "unreadable to a contender on both platforms, so a harness that writes its pid there instead "
                    + "leaves a refused floor run unable to say who has the desktop - which is precisely the state "
                    + "this tree was in, undetected, until 2026-08-25.");
            }

            if (!text.Contains(IdentityPrefix, StringComparison.Ordinal))
            {
                violations.Add($"client/tools/verify/{name}: does not write the identity prefix "
                    + $"'{IdentityPrefix}'. RealDesktopLease.HolderProcessId requires the sidecar to begin literally "
                    + $"'{IdentityPrefix}' (RealDesktopCollection.cs:224), so any other framing reads as no holder.");
            }
        }

        foreach (var (name, token, why) in ExclusiveOpenTokens)
        {
            if (files.TryGetValue(name, out var text) && !text.Contains(token, StringComparison.Ordinal))
            {
                violations.Add($"client/tools/verify/{name}: does not take the lease with '{token}' - {why}.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // The detector's own control. Every expectation above came from RealDesktopLease, and an empty or
        // whitespace one would satisfy Contains() against literally any text.
        Assert.False(string.IsNullOrWhiteSpace(leaseFileName));
        Assert.False(string.IsNullOrWhiteSpace(sidecarSuffix));
    }

    /// <summary>
    /// A pinned helper is a claim that something else leases the desktop before it runs, and an unchecked
    /// claim of that shape is how a deadlock ships: the pin says "my parent already holds it", so if no
    /// parent invokes the helper the pin is a fiction and the file is an unleased entry point after all.
    /// </summary>
    [Fact]
    public void EveryPinnedHelper_IsReallyInvokedFromALeasingEntryPoint_OrThePinIsAFictionThatDeadlocks()
    {
        var leaseFileName = Path.GetFileName(RealDesktopLease.MachineWidePath);
        var files = HarnessSources();
        var leasing = files
            .Where(f => f.Code.Contains(leaseFileName, StringComparison.Ordinal))
            .ToArray();
        var violations = new List<string>();

        foreach (var helper in HelperFiles)
        {
            var callers = leasing
                .Where(f => !string.Equals(f.Name, helper, StringComparison.Ordinal)
                    && f.Code.Contains(helper, StringComparison.Ordinal))
                .Select(f => f.Name)
                .ToArray();

            if (callers.Length == 0)
            {
                violations.Add($"client/tools/verify/{helper}: pinned as a helper that never needs the lease "
                    + "because a leasing entry point already holds it, but NO leasing entry point in this tree "
                    + "invokes it. Either it is really an entry point and must take the lease itself, or its caller "
                    + "lost the lease and the pin is now covering an unleased desktop run.");
            }
        }

        var unpinned = HelperFiles
            .Where(h => !files.Any(f => string.Equals(f.Name, h, StringComparison.Ordinal)))
            .ToArray();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.Empty(unpinned);
        Assert.NotEmpty(leasing); // a walk that found no leasing entry point proves nothing about the pins
    }

    /// <summary>What <c>RealDesktopLease.HolderProcessId</c> demands the sidecar begin with
    /// (<c>RealDesktopCollection.cs:224</c>). Kept beside the reader it mirrors rather than in the harness,
    /// because the harness is what this guard is checking.</summary>
    private const string IdentityPrefix = "pid=";

    /// <summary>
    /// <paramref name="Raw"/> is every byte of the file; <paramref name="Code"/> is the same file with every
    /// whole-line comment dropped. Which one a fact reads is a decision, not a detail — see the class
    /// remarks, and the mutation that forced the split.
    /// </summary>
    private sealed record HarnessFile(string Name, string Raw, string Code);

    private static IReadOnlyList<HarnessFile> HarnessSources()
    {
        var root = Path.Combine([FindRepoRoot(), .. HarnessParts]);
        Assert.True(Directory.Exists(root),
            $"{string.Join('/', HarnessParts)} not found at {root} — the harness lease guard refuses to skip");

        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => f.Replace('\\', '/'))
            .Where(f => !f.Contains("/bin/", StringComparison.Ordinal)
                && !f.Contains("/obj/", StringComparison.Ordinal)
                // The gitignored capture output. Nothing there is source, and a BMP read as text is both
                // meaningless and expensive - the same directory GoonPracticeTests had to stop hashing for
                // the same reason (client/docs/task-board.md).
                && !f.Contains("/artifacts/", StringComparison.Ordinal))
            .Where(f => TextExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(f => File.ReadAllText(f) is var raw
                ? new HarnessFile(Path.GetFileName(f), raw, CodeLines(raw))
                : throw new InvalidOperationException())
            .OrderBy(f => f.Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The file with whole-line comments removed. <c>#</c> is the line comment in all three harness
    /// languages (PowerShell, bash, Python), and <c>capture.ps1</c> carries no <c>&lt;# #&gt;</c> block at
    /// all, so this covers every comment in the tree it walks.
    ///
    /// <para>Only a line that STARTS with <c>#</c> is dropped, never from a <c>#</c> found mid-line. That is
    /// deliberate: <c>capture.ps1</c> is full of <c>'#FFE066FF'</c> colour literals, and a scanner that cut
    /// at the first <c>#</c> anywhere would silently delete real code after them. The cost is that a trailing
    /// comment on a code line still counts as code, which is the harmless direction.</para>
    /// </summary>
    private static string CodeLines(string raw) => string.Join(
        '\n',
        raw.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

    private static readonly string[] TextExtensions = [".ps1", ".sh", ".py", ".cs", ".json", ".mjs"];

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine([directory.FullName, .. RepoAnchorParts])))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException(
            $"repo root not found above {AppContext.BaseDirectory} (anchor: {string.Join('/', RepoAnchorParts)}) — "
            + "the harness lease guard refuses to skip");
    }
}
