using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Input;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Pointer;

namespace CcpClient.Tests;

/// <summary>
/// <b>The product's OWN stated teardown path, measured from outside the process that takes it.</b>
///
/// <para><b>What the product says about itself, and why saying it was not enough.</b>
/// <c>Session/SessionParticipant.cs:927-951</c> records that the dispatched surface teardown DOES
/// NOT RUN on the ordinary path and that the surfaces are instead <i>"reclaimed by the OPERATING
/// SYSTEM at process exit"</i>. <see cref="SurfaceTeardownObservations"/> proved the desktop is
/// clean for the case where the disposals DO reach the creating thread — it asserts that they did
/// — so what is covered there is the guarded <c>ShutdownAsync</c> path, and the ORDINARY one was
/// covered by nothing. It is the more important half: relying on process death makes a surface's
/// lifetime the PROCESS's, and anything that keeps the process alive after the shell closes leaves
/// a topmost, input-blocking window on a user's desktop with nothing on screen to close it.</para>
///
/// <para><b>Why a child process and not a fixture in this one.</b> The claim is about what happens
/// when a process DIES, and a fact cannot kill the process it is running in. So the surfaces are
/// placed by a real process — this same test executable, re-entered through
/// <see cref="SurfaceExitChild"/> before xunit ever loads — which then dies two different deaths
/// while THIS process, which never created any of those windows, asks the window manager what is
/// left. Every reading below is the operating system's, taken across a process boundary, which is
/// the only place from which "the OS reclaimed it" is a statement about the OS rather than about
/// the product's own bookkeeping.</para>
///
/// <para><b>The two deaths, and what each one is for.</b>
/// (1) THE ORDINARY PATH: the child tears down through the real
/// <see cref="ApplicationHost.ShutdownAsync"/> with its UI dispatch boundary bound to a dispatch
/// that never delivers — the exact shape of <c>App.axaml.cs:95</c>, where the lifetime's Exit
/// handler blocks the UI thread inside teardown so nothing posted to it can run — and then exits.
/// The disposals are POSTED AND NEVER RUN, which the run proves by counting both, so the surfaces
/// are still up at the last managed instruction and only the process's death can remove them.
/// (2) ABNORMAL TERMINATION: no teardown at all — a second child is killed while it sits with both
/// surfaces up, which is the only remedy left for a wedge no bound inside teardown can reach, and
/// the case the task board named as untested.</para>
///
/// <para><b>What is deliberately NOT a child, and why that is not a gap.</b> The third question —
/// whether a participant whose stop never completes can hold the process alive with its surfaces up
/// — is a <c>TeardownBoundTests</c> fact instead. Composing it with (1) answers the same question
/// with no third process: teardown returns despite such a participant, and (1) is what establishes
/// that a teardown which returns is followed by a process that dies and a desktop the OS cleans. A
/// child could not have been used for it anyway, and the reason is worth recording so nobody spends
/// the afternoon again: this child runs inside a MODULE INITIALIZER, and while a module initializer
/// is executing, any continuation that must resume on another thread and touch a type in the same
/// module blocks on the module's initialization lock — measured, with the main thread inside
/// <c>GetAwaiter().GetResult()</c> and the pool thread waiting on the very cctor that was waiting on
/// it. The ordinary path completes entirely on the calling thread and is unaffected; a bounded
/// wait, by construction, is not.</para>
///
/// <para><b>The vacuous case is closed before either death.</b> Each child is read WHILE it holds
/// its surfaces: the pointer target really won its own centre and the card really held the
/// foreground, in a process that is provably not this one. Without that, "nothing of theirs
/// survives" would be true of two processes that never put anything on the screen.</para>
///
/// <para><b>Two of the six native surfaces, and deliberately those two.</b> The other four are
/// click-through and merely occupy the topmost band. <c>Pointer/Win32PointerSurface.cs:850-852</c>
/// is <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c> with <c>WS_EX_TRANSPARENT</c> deliberately absent
/// because a poppable bubble must receive clicks, and <c>Input/Win32InputPresence.cs:1097-1099</c>
/// is <c>WS_EX_TOOLWINDOW</c> alone and takes the foreground AND the keyboard. Strand either and
/// the user's desktop eats their clicks, or their keyboard.</para>
/// </summary>
internal static class SurfaceExitObservations
{
    private static readonly Lazy<ExitRun> LazyRun = new(Run, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The one run. It starts two real processes that each put two always-on-top windows on
    /// the user's screen and take the foreground, so it happens once per suite execution.</summary>
    internal static ExitRun Observed => LazyRun.Value;

    /// <param name="MachineHasInteractiveDesktop">The machine fact every expectation is compared
    /// against.</param>
    /// <param name="ParentVisibleWindows">Visible top-level windows THIS process owns. Zero is
    /// required in its own right: a parent holding one would mean an earlier fixture stranded a
    /// window, and the child-owned counts below are only meaningful beside a clean parent.</param>
    /// <param name="OrdinaryAlive">The ordinary-path child, read while it held both surfaces.</param>
    /// <param name="OrdinaryTail">Everything that child printed after its teardown: the posted and
    /// delivered disposal counts, and every line its <see cref="ILogSink"/> received.</param>
    /// <param name="OrdinaryAfter">What the window manager holds once that child has exited.</param>
    /// <param name="KilledAlive">The killed child, read while it held both surfaces AND was still
    /// running with no teardown in progress at all. <b>This is the hazard itself, constructed on
    /// purpose:</b> a live process, a topmost window eating its own centre, and nothing on screen
    /// to close it.</param>
    /// <param name="KilledAfter">And what survived <c>TerminateProcess</c>.</param>
    internal sealed record ExitRun(
        bool MachineHasInteractiveDesktop,
        int ParentVisibleWindows,
        ChildSurfaces OrdinaryAlive,
        string OrdinaryTail,
        ChildAftermath OrdinaryAfter,
        ChildSurfaces KilledAlive,
        ChildAftermath KilledAfter);

    /// <param name="Pid">The child's process id. Every window reading below is filtered to it, so a
    /// window of ours or of any other process can never be counted as the child's.</param>
    /// <param name="PointerPlaced">The pointer surface's own <c>Open</c> returned Available.</param>
    /// <param name="CardPlaced">And the input presence's <c>Prompt</c>.</param>
    /// <param name="PointerVisible">The OS reports the pointer target's handle visible.</param>
    /// <param name="PointerTopmost">And in the topmost band — the "permanently topmost" half of the
    /// invariant being armed.</param>
    /// <param name="PointerAteItsPoint"><b>The dangerous surface, proved dangerous:</b> the window
    /// manager routes the target's own centre TO the target. It is not click-through by design, so
    /// while it is up it really is eating that point.</param>
    /// <param name="CardVisible">The card's handle, same question.</param>
    /// <param name="CardHeldForeground"><b>The other dangerous surface:</b> the card holds the
    /// foreground, which the OS lends to exactly one window at a time.</param>
    /// <param name="VisibleWindows">Every visible top-level window the child owns.</param>
    internal sealed record ChildSurfaces(
        int Pid,
        bool PointerPlaced,
        bool CardPlaced,
        bool PointerVisible,
        bool PointerTopmost,
        bool PointerAteItsPoint,
        bool CardVisible,
        bool CardHeldForeground,
        int VisibleWindows);

    /// <param name="Exited">The process really ended. Not inferred from anything the child said —
    /// this is the OS's own answer about the process handle.</param>
    /// <param name="ExitCode">What it ended with.</param>
    /// <param name="VisibleWindows">Visible top-level windows still owned by that pid.</param>
    /// <param name="Survivors">And what they are, for a failure message worth reading.</param>
    /// <param name="PointerHandleGone">The OS no longer knows the pointer target's handle as a
    /// window of that process. Both halves are load-bearing: a handle VALUE can be reissued to
    /// another process's window, so "not a window, or not that process's" is the honest question.</param>
    /// <param name="CardHandleGone">Same, for the card.</param>
    /// <param name="PointerPointRoutesAway">The routing question the user's mouse asks, at the
    /// pointer target's old centre: it resolves to a window, and that window is not the dead
    /// child's. A handle check alone would pass over a window destroyed and re-created.</param>
    /// <param name="CardPointRoutesAway">Same, at the card's old centre.</param>
    /// <param name="ForegroundLeft">The foreground is no longer the dead child's. The card took the
    /// keyboard; something had to give it back.</param>
    internal sealed record ChildAftermath(
        bool Exited,
        int ExitCode,
        int VisibleWindows,
        string Survivors,
        bool PointerHandleGone,
        bool CardHandleGone,
        bool PointerPointRoutesAway,
        bool CardPointRoutesAway,
        bool ForegroundLeft);

    private static ExitRun Run()
    {
        // EVERY child this run starts is registered here BEFORE anything can throw, and the finally
        // kills whatever is still alive. A harness that failed halfway and left a child parked with
        // a topmost, input-blocking window on the machine would have committed the exact defect this
        // file exists to measure — and would then have handed it to every later fact in the suite.
        var started = new List<Child>();
        try
        {
            var parentVisible = Os.VisibleWindowsOf(Environment.ProcessId).Length;

            var ordinary = Launch(started);
            var ordinaryAlive = ReadWhileAlive(ordinary);
            var ordinaryTail = ReleaseAndDrain(ordinary);
            var ordinaryAfter = ReadAfterDeath(ordinary);

            // No release line is ever sent to this one: it stays parked on its standard input with
            // both surfaces up, which is exactly the state a user is left in when a process cannot
            // finish dying. The reading is taken in that state, and then the process is TERMINATED
            // — abnormal, no unwinding, no teardown, no finally.
            var killed = Launch(started);
            var killedAlive = ReadWhileAlive(killed);
            Kill(killed);
            var killedAfter = ReadAfterDeath(killed);

            return new ExitRun(
                MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
                ParentVisibleWindows: parentVisible,
                OrdinaryAlive: ordinaryAlive,
                OrdinaryTail: ordinaryTail,
                OrdinaryAfter: ordinaryAfter,
                KilledAlive: killedAlive,
                KilledAfter: killedAfter);
        }
        finally
        {
            foreach (var child in started)
            {
                Kill(child);
                child.Process.Dispose();
            }
        }
    }

    /// <summary>Terminates a child and waits for the OS to agree it is gone. Idempotent, so the
    /// run's own cleanup can call it over the one it killed on purpose.</summary>
    private static void Kill(Child child)
    {
        if (child.Process.HasExited)
        {
            return;
        }

        child.Process.Kill(entireProcessTree: false);
        TestWait.UntilSync(
            () => child.Process.HasExited,
            $"child pid {child.Pid} to leave the process table after being terminated",
            () => $"pid {child.Pid}");
    }

    /// <summary>
    /// Starts this same test executable in child mode. The mode travels in the child's OWN
    /// environment block (<see cref="ProcessStartInfo.Environment"/>) and never through
    /// <c>Environment.SetEnvironmentVariable</c>: a process-wide mutation here would be visible to
    /// every other fact in this assembly, which is the defect <c>ProcessEnvCollection</c> exists to
    /// prevent for a different variable.
    /// </summary>
    private static Child Launch(List<Child> started)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment[SurfaceExitChild.ModeVariable] = SurfaceExitChild.PlaceAndReport;

        var process = Process.Start(start)
            ?? throw new Xunit.Sdk.XunitException(
                $"could not start a child of {Environment.ProcessPath} — this fact measures what the OS does to a "
                + "process's windows when that process dies, and it has no subject without one");

        var child = new Child(process, Pid: process.Id, 0, 0, (0, 0), (0, 0), false, false);
        started.Add(child); // registered BEFORE the handshake can throw

        var announcement = process.StandardOutput.ReadLineAsync();
        TestWait.UntilSync(
            () => announcement.IsCompleted,
            "the child to announce the surfaces it placed",
            () => $"child pid {process.Id}, exited={process.HasExited}");

        if (announcement.Result is not { } line)
        {
            // Read the child's own words about its failure — safe to block on, because the pipe is
            // closed once the process is gone and Kill has already established that it is.
            Kill(child);
            throw new Xunit.Sdk.XunitException(
                "the child closed its output without announcing anything (exit code "
                + $"{process.ExitCode.ToString(CultureInfo.InvariantCulture)}). "
                + $"Its standard error: {process.StandardError.ReadToEnd()}");
        }

        var fields = line.Split(' ');
        if (fields.Length != 10 || fields[0] != SurfaceExitChild.Ready)
        {
            throw new Xunit.Sdk.XunitException(
                $"the child's announcement was not the agreed handshake: '{line}'");
        }

        return new Child(
            process,
            Pid: int.Parse(fields[1], CultureInfo.InvariantCulture),
            Pointer: (nint)long.Parse(fields[2], CultureInfo.InvariantCulture),
            Card: (nint)long.Parse(fields[3], CultureInfo.InvariantCulture),
            PointerCentre: (int.Parse(fields[4], CultureInfo.InvariantCulture), int.Parse(fields[5], CultureInfo.InvariantCulture)),
            CardCentre: (int.Parse(fields[6], CultureInfo.InvariantCulture), int.Parse(fields[7], CultureInfo.InvariantCulture)),
            PointerPlaced: fields[8] == "1",
            CardPlaced: fields[9] == "1");
    }

    private static ChildSurfaces ReadWhileAlive(Child child) => new(
        Pid: child.Pid,
        PointerPlaced: child.PointerPlaced,
        CardPlaced: child.CardPlaced,
        PointerVisible: Os.IsVisibleWindow(child.Pointer),
        PointerTopmost: Os.IsTopmost(child.Pointer),
        PointerAteItsPoint: Os.HitTest(child.PointerCentre.X, child.PointerCentre.Y) == child.Pointer,
        CardVisible: Os.IsVisibleWindow(child.Card),
        CardHeldForeground: Os.Foreground() == child.Card,
        VisibleWindows: Os.VisibleWindowsOf(child.Pid).Length);

    /// <summary>Sends the release line the child is parked on, then collects everything it printed
    /// on its way out. The drain is bounded through the approved helper: a child that never closed
    /// its output would otherwise take the whole run down with no failing test name.</summary>
    private static string ReleaseAndDrain(Child child)
    {
        child.Process.StandardInput.WriteLine(SurfaceExitChild.Release);
        child.Process.StandardInput.Flush();

        var tail = child.Process.StandardOutput.ReadToEndAsync();
        TestWait.UntilSync(
            () => tail.IsCompleted,
            $"child pid {child.Pid} to finish its teardown and close its output",
            () => $"exited={child.Process.HasExited}",
            TestWait.InjectedBudget);

        TestWait.UntilSync(
            () => child.Process.HasExited,
            $"child pid {child.Pid} to leave the process table after its own teardown",
            () => $"tail so far: {tail.Result}",
            TestWait.InjectedBudget);

        return tail.Result;
    }

    /// <summary>
    /// What the window manager holds once the child is gone.
    ///
    /// <para><b>The wait is a MEASURED finding, not defensive padding, and it is the most useful
    /// thing this run learned.</b> Reclamation is not atomic with the process's death: a process's
    /// windows are destroyed during the operating system's own rundown of it, which happens AFTER
    /// the process handle becomes signalled and its exit code readable. Measured on the abnormal
    /// path — the first draft read the desktop the instant <c>Process.HasExited</c> went true and
    /// found <b>two visible top-level windows still owned by the terminated pid</b>, whose owning
    /// process id read back as 0 moments later while the handles were being reaped. That is the
    /// operating system finishing its job, not the product failing to do its own, and a fact that
    /// reported it as a stranded surface would have been wrong about the product.</para>
    ///
    /// <para>So the readings are taken once the reclamation has settled, through the shared bounded
    /// helper. The helper is the verdict too: if the OS never reclaims, the window expires with
    /// <c>TIMING-VERDICT:CONDITION-NEVER-TRUE</c> naming the survivors, which is exactly the
    /// failure this whole file exists to catch.</para>
    /// </summary>
    private static ChildAftermath ReadAfterDeath(Child child)
    {
        TestWait.UntilSync(
            () => Os.VisibleWindowsOf(child.Pid).Length == 0
                && !Os.IsWindowOf(child.Pointer, child.Pid)
                && !Os.IsWindowOf(child.Card, child.Pid)
                && !Os.BelongsTo(Os.Foreground(), child.Pid),
            $"the operating system to reclaim every window of dead child pid {child.Pid} — the product's stated "
                + "teardown path rests on this and nothing else destroys these windows",
            () => $"still held: {string.Join("; ", Os.VisibleWindowsOf(child.Pid).Select(Os.Describe))}");

        var survivors = Os.VisibleWindowsOf(child.Pid);
        return new ChildAftermath(
            Exited: child.Process.HasExited,
            ExitCode: child.Process.ExitCode,
            VisibleWindows: survivors.Length,
            Survivors: survivors.Length == 0 ? "(none)" : string.Join("; ", survivors.Select(Os.Describe)),
            PointerHandleGone: !Os.IsWindowOf(child.Pointer, child.Pid),
            CardHandleGone: !Os.IsWindowOf(child.Card, child.Pid),
            PointerPointRoutesAway: Os.RoutesAwayFrom(child.PointerCentre.X, child.PointerCentre.Y, child.Pid),
            CardPointRoutesAway: Os.RoutesAwayFrom(child.CardCentre.X, child.CardCentre.Y, child.Pid),
            ForegroundLeft: !Os.BelongsTo(Os.Foreground(), child.Pid));
    }

    private sealed record Child(
        Process Process,
        int Pid,
        nint Pointer,
        nint Card,
        (int X, int Y) PointerCentre,
        (int X, int Y) CardCentre,
        bool PointerPlaced,
        bool CardPlaced);

    /// <summary>
    /// <b>The only instrument on the parent side, and it can only ask questions.</b> It creates no
    /// window, raises nothing, moves nothing and synthesises no input: every entry point is a read
    /// of the window manager's own state, about windows this process never made. That is what makes
    /// the readings the operating system's rather than the product's own bookkeeping.
    /// </summary>
    private static class Os
    {
        private const int GwlExstyle = -20;
        private const uint GwHwndnext = 2;
        private const uint WsExTopmost = 0x00000008;

        private static bool WindowsHost => OperatingSystem.IsWindows();

        /// <summary>Every visible top-level window owned by one process, walked off the z-order.
        /// Per-process enumeration rather than per-handle checks, because a surface may own more
        /// than one window and a run that could only ask about handles it was given would be blind
        /// to every other.</summary>
        internal static nint[] VisibleWindowsOf(int processId)
        {
            if (!WindowsHost)
            {
                return [];
            }

            var owned = new List<nint>();
            for (var candidate = GetTopWindow(0); candidate != 0; candidate = GetWindow(candidate, GwHwndnext))
            {
                if (IsWindowVisible(candidate) && BelongsTo(candidate, processId))
                {
                    owned.Add(candidate);
                }
            }

            return [.. owned];
        }

        /// <summary>Still a window, AND still that process's. A handle value can be reissued once
        /// its window is destroyed, so asking only <c>IsWindow</c> would eventually report a
        /// stranger's window as the dead child's survivor.</summary>
        internal static bool IsWindowOf(nint window, int processId) =>
            WindowsHost && window != 0 && IsWindow(window) && BelongsTo(window, processId);

        internal static bool IsVisibleWindow(nint window) =>
            WindowsHost && window != 0 && IsWindowVisible(window);

        internal static bool IsTopmost(nint window) =>
            WindowsHost && window != 0 && ((uint)GetWindowLongPtrW(window, GwlExstyle) & WsExTopmost) != 0;

        internal static nint HitTest(int x, int y) =>
            WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

        internal static nint Foreground() => WindowsHost ? GetForegroundWindow() : 0;

        internal static bool BelongsTo(nint window, int processId)
        {
            if (!WindowsHost || window == 0)
            {
                return false;
            }

            _ = GetWindowThreadProcessId(window, out var owner);
            return owner == processId;
        }

        /// <summary>The routing question the user's mouse asks: the point resolves to a window, and
        /// that window is not the dead child's. Both halves matter — an answer of 0 would mean the
        /// point resolves to nothing at all, which on a live desktop is a reading taken off-screen
        /// rather than "the desktop got its input back".</summary>
        internal static bool RoutesAwayFrom(int x, int y, int processId)
        {
            var owner = HitTest(x, y);
            return owner != 0 && !BelongsTo(owner, processId);
        }

        internal static string Describe(nint window)
        {
            if (!WindowsHost || window == 0)
            {
                return "(no window)";
            }

            var className = new System.Text.StringBuilder(128);
            _ = GetClassNameW(window, className, className.Capacity);
            _ = GetWindowThreadProcessId(window, out var owner);
            var topmost = IsTopmost(window) ? " TOPMOST" : string.Empty;
            return $"0x{window:X} class '{className}' pid {owner}{topmost}";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")] private static extern nint GetTopWindow(nint window);

        [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);

        [DllImport("user32.dll")] private static extern bool IsWindow(nint window);

        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();

        [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);

        [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassNameW(nint window, System.Text.StringBuilder text, int count);
    }
}

/// <summary>
/// <b>The child half: this test executable, re-entered before xunit exists.</b>
///
/// <para><b>Why a module initializer.</b> A module initializer runs before the entry point of the
/// module that declares it, so the child takes over ahead of the xunit runner: it never discovers a
/// test, never writes a TRX, and — decisively — never contends for
/// <see cref="RealDesktopLease"/>, which the PARENT is holding for the whole of this run. A child
/// that reached the runner would block on that lease while the parent blocked on the child.</para>
///
/// <para><b>And what that costs, stated because it decided the shape of the whole fact.</b> While a
/// module initializer runs, its module's initialization lock is held, so any continuation that must
/// resume on ANOTHER thread and touch a type in this module blocks on that lock — with the main
/// thread inside the initializer waiting for it. Measured, not assumed. Everything below is
/// therefore synchronous on the calling thread, which the ordinary teardown path is anyway; a
/// BOUNDED wait is not, which is why the abandoned-stop question is a <c>TeardownBoundTests</c> fact
/// in the parent process rather than a third child.</para>
///
/// <para><b>Why the mode is an environment variable and not an argument.</b> The xunit v3 runner
/// owns this executable's command line and rejects what it does not recognise. The variable is set
/// on the child's own environment block by the launcher and is absent everywhere else, so an
/// ordinary run of this assembly reads one variable and does nothing.</para>
///
/// <para><b>What it builds is the product's own teardown, not a model of it.</b> The host is a real
/// <see cref="ApplicationHost"/>; the boundary is a real <see cref="UiDispatchBoundary"/>; the
/// participant disposes its surfaces through that boundary in exactly the two-branch shape
/// <c>Session/SessionParticipant.cs:1040-1057</c> uses. The one thing supplied by this file is the
/// <see cref="IUiDispatch"/> behind the boundary, and it is supplied because the product's own is
/// unreachable here: in the shipping app the boundary is bound to the Avalonia UI thread and that
/// thread is BLOCKED inside teardown for its whole duration (<c>App.axaml.cs:95</c> calls
/// <c>ShutdownAsync().GetAwaiter().GetResult()</c> from the lifetime's Exit handler), so a posted
/// disposal is never delivered. <c>NeverDelivers</c> is that, and it counts both sides so the parent
/// can prove the run really took the ordinary path rather than the guarded one.</para>
/// </summary>
internal static class SurfaceExitChild
{
    /// <summary>Set on the child's own environment block only. Never exported process-wide.</summary>
    internal const string ModeVariable = "CCP_SURFACE_EXIT_CHILD";

    /// <summary>The one child behaviour: place the two dangerous surfaces, announce them, wait to be
    /// released, then tear down through the real host with the disposals posted and never
    /// delivered. A child that is never released simply keeps holding them, which is what makes the
    /// same behaviour serve the abnormal-termination reading too.</summary>
    internal const string PlaceAndReport = "place-and-report";

    /// <summary>The handshake the parent parses.</summary>
    internal const string Ready = "READY";

    /// <summary>The line the parent sends to let the child proceed. A child that never receives it
    /// stays parked with both surfaces up, which is the state the kill fact measures.</summary>
    internal const string Release = "GO";

    private const uint Fill = 0x00201020;
    private const uint Ink = 0x00E0C0FF;
    private const int PointerSide = 160;

    [ModuleInitializer]
    internal static void TakeOverWhenLaunchedAsAChild()
    {
        if (Environment.GetEnvironmentVariable(ModeVariable) is not { Length: > 0 })
        {
            return;
        }

        Environment.Exit(Play());
    }

    private static int Play()
    {
        // The floor the product cannot be without: the shipping app always owns at least one
        // top-level window (the Avalonia main window, or Tray/Win32TrayPresence's hidden owner), so
        // a child that could walk itself to zero would be entering a state the product cannot enter
        // and reporting the consequences as the product's. Zero-sized and never shown, so it is
        // absent from every IsWindowVisible walk the parent performs.
        RealDesktopWindowFloor.Ensure();

        var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;

        // The two rectangles the landed teardown run already uses, so this child is a strict
        // extension of an arrangement known to fit on one screen — and disjoint, because each
        // surface's own hit-test point must never be occluded by the other.
        var pointerBounds = new PointerBounds(
            Math.Max(0, (screenWidth / 2) + 260),
            Math.Max(0, (screenHeight / 2) - 320),
            PointerSide,
            PointerSide);
        var pointer = new Win32PointerSurface();
        var pointerPlaced = pointer.Open(new PointerTargetRequest(pointerBounds, Fill, Ink), out var target);
        var pointerWindow = pointer.NativeHandlesFor(target).Window;

        var cardBounds = new InputBounds(
            Math.Max(0, (screenWidth / 2) + 200),
            Math.Max(0, (screenHeight / 2) + 160),
            360,
            180);
        var card = new Win32InputPresence();
        var cardPlaced = card.Prompt(new InputPromptRequest(
            cardBounds,
            new InputPromptContent("say this", "1 of 1", string.Empty, "Press Esc to close"),
            _ => { }));
        var cardWindow = card.NativeHandles.Window;

        var (pointerX, pointerY) = pointerBounds.Centre;
        var (cardX, cardY) = cardBounds.Centre;
        Console.Out.WriteLine(string.Join(' ',
            Ready,
            Environment.ProcessId,
            (long)pointerWindow,
            (long)cardWindow,
            pointerX,
            pointerY,
            cardX,
            cardY,
            pointerPlaced is CapabilityState.Available ? 1 : 0,
            cardPlaced is CapabilityState.Available ? 1 : 0));
        Console.Out.Flush();

        // Parked until the parent has finished reading the live desktop — or forever, when the
        // parent's subject is a process that cannot finish dying and it kills this one instead.
        // A blocking read on a pipe the parent holds open, so there is no wait here to bound: this
        // process is not waiting for time to pass, it is waiting for its parent to speak.
        if (Console.ReadLine() is null)
        {
            return 1;
        }

        var log = new ChildLog();
        var dispatch = new UiDispatchBoundary();
        var blocked = new NeverDelivers();
        dispatch.Bind(blocked);

        var host = new ApplicationHost(
            log,
            [new SurfaceParticipant(dispatch, [pointer, card])],
            new StartupTrace(),
            new OperationRegistry(),
            dispatch);

        // The blocking bridge the shipping app uses, and for the same reason: App.axaml.cs:95 calls
        // exactly this from the lifetime's Exit handler.
        host.ShutdownAsync().GetAwaiter().GetResult(); // wallclock-allow: no wait to bound — this IS the product's own teardown bridge (App.axaml.cs:95) and whether it returns at all is the subject; the parent bounds its observation and kills this process if it never does

        Console.Out.WriteLine($"DONE posted={blocked.Posted} delivered={blocked.Delivered}");
        foreach (var line in log.Lines)
        {
            Console.Out.WriteLine($"LOG {line}");
        }

        Console.Out.Flush();
        return 0;
    }

    /// <summary>
    /// The UI dispatch boundary's far end during the shipping app's teardown: a queue nothing ever
    /// drains. It is not a stub standing in for behaviour that was too hard to reach — it is the
    /// behaviour. The UI thread is inside <c>ShutdownAsync</c> at that moment
    /// (<c>App.axaml.cs:95</c>), so every <c>Post</c> made during teardown is accepted and never
    /// delivered.
    /// </summary>
    private sealed class NeverDelivers : IUiDispatch
    {
        private readonly List<Action> _queued = [];

        internal int Posted => _queued.Count;

        /// <summary>How many queued delegates were actually INVOKED. It is a real counter over a
        /// real queue rather than a constant: nothing in this process ever drains the queue, so it
        /// stays 0 — and it would move the moment anything did, which is what makes the parent's
        /// assertion about it worth making.</summary>
        internal int Delivered { get; private set; }

        public void Post(Action action) => _queued.Add(() =>
        {
            Delivered++;
            action();
        });
    }

    /// <summary>
    /// The surface-disposal half of <c>Session/SessionParticipant.cs:935-1000</c>, in its shape and
    /// with its reason: a native window belongs to the thread that created it, so the disposal is
    /// POSTED when the boundary is bound and taken inline when it is not
    /// (<c>SessionParticipant.cs:1040-1057</c>). Here the boundary is bound, exactly as it is in the
    /// shipping app, so nothing runs.
    /// </summary>
    private sealed class SurfaceParticipant(UiDispatchBoundary dispatch, IDisposable[] surfaces)
        : IBackgroundParticipant
    {
        public string Name => "surfaces";

        public bool Running { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Running = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Running = false;
            foreach (var surface in surfaces)
            {
                if (dispatch.IsBound)
                {
                    dispatch.Post(surface.Dispose);
                }
                else
                {
                    surface.Dispose();
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ChildLog : ILogSink
    {
        internal List<string> Lines { get; } = [];

        public void Log(string message) => Lines.Add(message);
    }
}
