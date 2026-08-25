using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The half of the overlay safety invariant that the product asserts about ITSELF:</b>
/// <i>"the posted teardown does not run and the surfaces are reclaimed by the OPERATING SYSTEM at
/// process exit"</i> (<c>Session/SessionParticipant.cs:920-952</c>).
///
/// <para><b>Why this and not the other half.</b> <see cref="SurfaceTeardownTests"/> proves the
/// desktop is clean when the disposals DO reach the creating thread — it asserts that they did — so
/// it covers the guarded <c>ShutdownAsync</c> path. The ordinary path is the one the shipping app
/// actually takes, and on it those disposals never run at all: the lifetime's Exit handler calls
/// <c>ShutdownAsync().GetAwaiter().GetResult()</c> ON the UI thread (<c>App.axaml.cs:95</c>), so the
/// thread that owns every native window is blocked inside teardown for its whole duration and
/// cannot deliver anything posted to it. A native window may be destroyed only by the thread that
/// created it, so no other thread can do it either. What actually removes those windows is the
/// death of the process.</para>
///
/// <para><b>That makes a surface's lifetime the PROCESS's, and that is the whole risk.</b> Two of
/// the six native surfaces are deliberately not click-through — the pointer target
/// (<c>Pointer/Win32PointerSurface.cs:850-852</c>) and the lock card
/// (<c>Input/Win32InputPresence.cs:1097-1099</c>, which takes the foreground AND the keyboard) — so
/// a process that cannot finish dying leaves a topmost window eating a user's clicks or keystrokes
/// with nothing on screen to close it. These facts measure the reclamation half — normal exit and
/// abnormal termination alike — and the OTHER half, that nothing inside teardown can hold the
/// process alive, is <see cref="TeardownBoundTests"/> plus the source walk at the end of this
/// class.</para>
///
/// <para><b>Every reading is the operating system's, taken across a process boundary.</b> The
/// windows are placed by a real child process running this build's own surface types; the process
/// under measurement is never the one doing the measuring, which is the only vantage point from
/// which "the OS reclaimed it" is a statement about the OS.</para>
/// </summary>
[Collection(nameof(RealDesktopCollection))]
public class SurfaceExitTests : RealDesktopFacts
{
    /// <summary>The other reading a routing assertion can fail on, named so a failure sends nobody
    /// after the wrong cause: <c>RoutesAwayFrom</c> is false both when the point still resolves to
    /// the dead child AND when it resolves to no window at all, which on a live desktop means the
    /// point was off-screen. Fact 1 rules the second out — it read the same point winning while the
    /// child held it — but only if fact 1 also passed.</summary>
    private const string OrToNothing =
        " — or to no window at all, which would mean the point was read off-screen rather than "
        + "handed back to the desktop (the fact above rules that out by winning the same point while "
        + "the child held it)";

    /// <summary>
    /// <b>The vacuous case, closed first and for both deaths.</b> Every "nothing survived" reading
    /// below would be trivially true of two processes that never put anything on the screen, so each
    /// child is read WHILE it holds its surfaces: the pointer target really won its own centre, and
    /// the card really held the foreground the OS lends to one window at a time.
    /// </summary>
    [Fact]
    public void EachChildReallyHeldTwoDangerousSurfaces_OrEveryReadingAfterItsDeathIsATestOfNothingHappening()
    {
        var run = SurfaceExitObservations.Observed;
        var expected = run.MachineHasInteractiveDesktop;

        Assert.Equal(0, run.ParentVisibleWindows);

        foreach (var (label, alive) in new[]
        {
            ("the ordinary-path child", run.OrdinaryAlive),
            ("the child that was killed", run.KilledAlive),
        })
        {
            Assert.True(alive.Pid != Environment.ProcessId,
                $"{label} reported this process's own id, so nothing here is being measured across a process "
                + "boundary and 'the OS reclaimed it at process exit' is being asked of a process that never died");

            Assert.Equal(expected, alive.PointerPlaced);
            Assert.Equal(expected, alive.CardPlaced);
            Assert.Equal(expected, alive.PointerVisible);
            Assert.Equal(expected, alive.CardVisible);

            Assert.True(alive.PointerTopmost == expected,
                $"{label}'s pointer target was not in the topmost band, so the 'permanently topmost surface' half "
                + "of the invariant is being asserted over a window that was never topmost");
            Assert.True(alive.PointerAteItsPoint == expected,
                $"{label}'s pointer target did not win its own centre even while it was up, so it was never the "
                + "input-blocking surface this fact is about and 'it stopped blocking input' would be true of a "
                + "window that never blocked any");
            Assert.True(alive.CardHeldForeground == expected,
                $"{label}'s lock card never took the foreground, so 'the desktop got the keyboard back' would be "
                + "true of a card that never took it");
            Assert.True(alive.VisibleWindows >= (expected ? 2 : 0),
                $"{label} owned only {alive.VisibleWindows} visible top-level window(s) where two surfaces were "
                + "placed");
        }
    }

    /// <summary>
    /// <b>THE PRODUCT'S STATED ORDINARY PATH, MEASURED.</b> The child tears down through the real
    /// <c>ApplicationHost.ShutdownAsync</c> with its UI dispatch boundary bound, exactly as the
    /// shipping app's is, to a dispatch that accepts posts and never delivers them — which is what
    /// <c>App.axaml.cs:95</c> makes true of the real one for the whole of teardown. The disposals
    /// are posted and none runs. Then the process exits, and the window manager holds nothing of
    /// its.
    ///
    /// <para><b>The posted/delivered counts are the anti-vacuity leg.</b> Without them this would
    /// pass just as well over a run that took the GUARDED path and disposed everything properly,
    /// which is the path already covered by <see cref="SurfaceTeardownTests"/> and is not what this
    /// fact claims to be about.</para>
    /// </summary>
    [Fact]
    public void OnTheOrdinaryPath_NoDisposalRuns_AndTheOperatingSystemReclaimsEverySurfaceAtProcessExit()
    {
        var run = SurfaceExitObservations.Observed;

        Assert.Contains("DONE posted=2 delivered=0", run.OrdinaryTail, StringComparison.Ordinal);

        Assert.True(run.OrdinaryAfter.Exited,
            "the ordinary-path child never left the process table, so nothing below is a reading about process exit");
        Assert.Equal(0, run.OrdinaryAfter.ExitCode);

        Assert.True(run.OrdinaryAfter.VisibleWindows == 0,
            $"{run.OrdinaryAfter.VisibleWindows} visible top-level window(s) outlived the process that owned them: "
            + $"{run.OrdinaryAfter.Survivors}. The product's own remark says the operating system reclaims these at "
            + "process exit; on this machine it did not, and a user would be left with a window they cannot see and "
            + $"cannot close. The child's teardown said: {run.OrdinaryTail}");

        Assert.True(run.OrdinaryAfter.PointerHandleGone,
            "the pointer target's window outlived the process, and it is NOT click-through by design "
            + "(Pointer/Win32PointerSurface.cs:850-852), so it is still eating clicks");
        Assert.True(run.OrdinaryAfter.CardHandleGone,
            "the lock card's window outlived the process, and it takes the foreground AND the keyboard by design "
            + "(Input/Win32InputPresence.cs:1097-1099)");

        Assert.True(run.OrdinaryAfter.PointerPointRoutesAway,
            "the pointer target's old centre still routes to the dead process" + OrToNothing);
        Assert.True(run.OrdinaryAfter.CardPointRoutesAway,
            "the card's old centre still routes to the dead process" + OrToNothing);
        Assert.True(run.OrdinaryAfter.ForegroundLeft,
            "the dead process still holds the foreground, so the keyboard never came back");
    }

    /// <summary>
    /// <b>ABNORMAL TERMINATION — kill, no unwinding, no teardown, no finally — which the task board
    /// named as untested.</b> It is also the only remedy left for the wedge no bound can cover: a
    /// <c>StopAsync</c> that blocks its caller before returning a task never reaches
    /// <c>ApplicationHost</c>'s bounded wait at all, and no patience taken on the blocked thread
    /// helps. So the question that matters is whether killing such a process gets the user's desktop
    /// back, and this asks the window manager rather than assuming.
    ///
    /// <para>The child is terminated while parked with both surfaces up and no teardown in progress
    /// — the exact state a user is left in — so this is also the closest reading in the suite to the
    /// hazard itself: fact 1 has already established that at this moment the pointer target was
    /// eating its own centre and the card held the foreground.</para>
    ///
    /// <para><b>And what this path measured that the ordinary one did not:</b> reclamation is not
    /// atomic with death. Read at the instant <c>HasExited</c> went true, two visible top-level
    /// windows were still owned by the terminated process — the OS reaps them during its own
    /// rundown, slightly after the exit code becomes readable. The run therefore reads once the
    /// reclamation has settled, through the shared bounded helper, and that helper's expiry is what
    /// would report a genuinely stranded window.</para>
    /// </summary>
    [Fact]
    public void AbnormalTermination_ReclaimsTheSurfacesToo_WhichIsTheOnlyRemedyForASynchronousWedge()
    {
        var run = SurfaceExitObservations.Observed;

        Assert.True(run.KilledAfter.Exited, "the killed child is still in the process table");

        Assert.True(run.KilledAfter.VisibleWindows == 0,
            $"{run.KilledAfter.VisibleWindows} visible top-level window(s) survived TerminateProcess: "
            + $"{run.KilledAfter.Survivors}. Nothing else can clean these up — there is no unwinding, no teardown "
            + "and no finally on this path");
        Assert.True(run.KilledAfter.PointerHandleGone, "the pointer target's window survived the kill");
        Assert.True(run.KilledAfter.CardHandleGone, "the card's window survived the kill");
        Assert.True(run.KilledAfter.PointerPointRoutesAway,
            "the killed process's pointer target still owns its old centre" + OrToNothing);
        Assert.True(run.KilledAfter.CardPointRoutesAway,
            "the killed process's card still owns its old centre" + OrToNothing);
        Assert.True(run.KilledAfter.ForegroundLeft,
            "the killed process still holds the foreground, so its card kept the keyboard past its own death");
    }

    /// <summary>
    /// <b>The other way the process could fail to end, closed at the source.</b> After
    /// <c>Main</c> returns, the .NET runtime waits for every FOREGROUND managed thread before the
    /// process may exit — so one such thread anywhere in the product would hold the app alive after
    /// the shell closed, with the surfaces still up, and no bound inside teardown could reach it.
    ///
    /// <para>The port creates two managed threads outside the pool and both are <c>IsBackground</c>
    /// on purpose. The first is <c>Audio/SoundArbitration.cs:1332-1341</c> — <i>"named +
    /// IsBackground so a wedged native call never blocks process exit"</i>, the same judgement this
    /// row reached from the other end, already made at the one native wedge this port has measured.
    /// The second is the emergency stop's own pump (<c>Input/Win32PanicKey.cs</c>), which owns the
    /// hidden window <c>WM_HOTKEY</c> is posted to so the chord is dequeued whatever the UI thread
    /// is doing — and which, as a foreground thread, would hold the process open with every surface
    /// up: the panic key springing the trap it exists to open.</para>
    ///
    /// <para><b>Mutation that reds it:</b> drop <c>IsBackground = true</c> from that construction,
    /// or add any other <c>new Thread(...)</c> to <c>client/src</c> without it.</para>
    ///
    /// <para><b>What it does NOT cover, stated because a source walk always has an edge.</b> Threads
    /// created by dependencies through native code are invisible here — and irrelevant to this
    /// question, because the runtime waits only for MANAGED foreground threads. A thread started
    /// through <c>ThreadPool</c> or <c>Task</c> is always background and needs no marking.</para>
    /// </summary>
    [Fact]
    public void TheProductStartsNoForegroundThread_SoNothingItCreatesCanHoldTheProcessPastMain()
    {
        var unmarked = new List<string>();
        var found = 0;
        foreach (var file in Directory.EnumerateFiles(ProductSourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("new Thread(", StringComparison.Ordinal)
                    || lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                found++;
                if (!lines[i].Contains("IsBackground = true", StringComparison.Ordinal))
                {
                    unmarked.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(found > 0,
            "the walk found no explicit thread construction in client/src at all, so it is not reading the product "
            + "and proves nothing about foreground threads");
        Assert.True(unmarked.Count == 0,
            "a managed FOREGROUND thread in the product would keep the process alive after Main returns, with every "
            + "native surface still on the user's desktop and nothing on screen to close it — the surfaces are "
            + "reclaimed by the OS at process exit and by nothing else (Session/SessionParticipant.cs:920-952). "
            + "Unmarked thread construction(s): " + string.Join("; ", unmarked));
    }

    /// <summary>The tree this walk reads, with its refusal. Kept OUT of the [Fact] body with the
    /// rest of the tree-existence plumbing, so no <c>fs-predicate</c> shape lands in a fact — the
    /// convention <c>ProcessEnvCollectionGuardTests.cs</c> and <c>ArcademyServingTests.cs:70</c>
    /// already follow. It refuses rather than skipping: a walk over a tree that is not there proves
    /// nothing and must say so.</summary>
    private static string ProductSourceRoot()
    {
        var source = Path.Combine(FindRepoRoot(), "client", "src");
        Assert.True(Directory.Exists(source), $"client/src not found at {source} — this walk refuses to skip");
        return source;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "client", "CcpClient.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
