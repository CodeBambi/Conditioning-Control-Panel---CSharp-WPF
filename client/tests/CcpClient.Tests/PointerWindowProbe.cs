using System.Runtime.InteropServices;
using System.Text;

namespace CcpClient.Tests;

/// <summary>
/// The pointer facts' independent instrument: a second, private copy of the Win32 calls the product
/// uses, plus the one thing the product must never do — <b>synthesise a mouse click</b>.
///
/// <para><b>Why a new probe and not <see cref="InputWindowProbe"/>.</b> That instrument's whole rig
/// is built to take the foreground and hold the keyboard; every helper on it either grabs the
/// foreground or reads the focus. This packet has to prove the opposite — a window that takes a
/// click and takes NOTHING — so reusing it would mean the instrument for the negative claim shares
/// state with the machinery that makes the positive one true. It is also byte-identical to base
/// after this packet, which is the point: that landed instrument is not modified here.</para>
///
/// <para><b>The injection is the harness's and never the product's.</b> A capability that clicked
/// the mouse to check it could receive clicks would be clicking whatever the user really had under
/// the cursor. So <see cref="InjectClickAt"/> lives here, and nothing shipped calls anything like
/// it.</para>
/// </summary>
internal static class PointerWindowProbe
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExNoactivate = 0x08000000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExAppwindow = 0x00040000;
    private const int GwlExstyle = -20;
    private const uint GwOwner = 4;
    private const int DwmwaCloaked = 14;
    private const uint SwpShowwindow = 0x0040;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint GwHwndnext = 2;
    private const int SmCmonitors = 80;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private const int SwHide = 0;

    private const uint WmLbuttondown = 0x0201;
    private const uint WmLbuttonup = 0x0202;
    private const uint WmMouseactivate = 0x0021;
    private const uint WmMousemove = 0x0200;
    private const uint WmMousewheel = 0x020A;
    private const uint WmKeydown = 0x0100;
    private const uint WmActivate = 0x0006;
    private const uint WmQuit = 0x0012;
    private const uint PmRemove = 0x0001;

    /// <summary>The left button's bit in a mouse message's <c>wParam</c>. A move carrying it is a
    /// DRAG; a move without it is the pointer merely travelling.</summary>
    private const nint MkLbutton = 0x0001;

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfWheel = 0x0800;
    private const uint MouseeventfAbsolute = 0x8000;
    private const uint MouseeventfVirtualdesk = 0x4000;
    private const uint KeyeventfKeyup = 0x0002;

    /// <summary>One notch of the wheel, as <c>WM_MOUSEWHEEL</c> reports it.</summary>
    internal const int WheelDelta = 120;

    /// <summary>A key with no character and no default meaning on any standard layout — the same
    /// choice, and the same reason, as <see cref="InputWindowProbe.VkF13"/>: a keystroke that
    /// escapes to a window this harness does not own must not be able to TYPE anything into
    /// it.</summary>
    internal const ushort VkF13 = 0x7C;

    /// <summary>How many messages one pump drains, and how many pumps a bounded wait takes. Both are
    /// iteration counts with a yield, never a wall-clock wait.</summary>
    private const int MaxPumpIterations = 4096;

    private const int NegativeDrainIterations = 512;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>The machine fact every expectation in the pointer facts is compared against.</summary>
    internal static bool MachineHasInteractiveDesktop =>
        WindowsHost && GetSystemMetrics(SmCmonitors) >= 1 && GetSystemMetrics(SmCxscreen) > 0;

    internal static (int Width, int Height) PrimarySize =>
        WindowsHost ? (GetSystemMetrics(SmCxscreen), GetSystemMetrics(SmCyscreen)) : (0, 0);

    internal static bool WindowExists(nint window) => WindowsHost && window != 0 && IsWindow(window);

    internal static bool WindowIsVisible(nint window) => WindowsHost && window != 0 && IsWindowVisible(window);

    internal static nint Foreground() => WindowsHost ? GetForegroundWindow() : 0;

    internal static nint HitTest(int x, int y) =>
        WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

    /// <summary>
    /// How many times the topmost band is re-asserted before a routing answer is taken as final.
    /// </summary>
    internal const int MaxRaiseAttempts = 8;

    /// <summary>
    /// The routing question, preceded by a re-assertion of the topmost band for
    /// <paramref name="window"/>, repeated while the answer is not that window.
    ///
    /// <para><b>Why the raise, and why it is not the instrument helping the thing under test.</b>
    /// Measured on this machine while writing this file: a scratch target at (343,214,160,160) was
    /// visible, held its exact rectangle, and sat at z-index 6 with the first ordinary window at 7 —
    /// and <c>WindowFromPoint</c> at its own centre still answered
    /// <c>HwndWrapper[ConditioningControlPanel;...]</c>, the SHIPPING WPF PRODUCT, which is topmost
    /// too and re-asserts <c>HWND_TOPMOST</c> on a cadence
    /// (<c>Services/Flash/FlashService.cs:206-243</c>). That is the residue
    /// <see cref="RealDesktopCollection"/> already names and no in-process mechanism can exclude.
    /// Re-asserting the band is what the product does (<c>Pointer/Win32PointerSurface.cs</c>), what
    /// the overlay does, and what upstream's own bubbles do (<c>Services/BubbleService.cs:4778-4787</c>,
    /// <c>BringToFront</c>) — so the instrument asks the same question the same way rather than
    /// reporting a foreign window's cadence as a capability failure.</para>
    ///
    /// <para>It is a bounded iteration count, never a wall-clock wait, and it never grants the
    /// answer: the caller still compares the returned hwnd.</para>
    /// </summary>
    internal static nint HitTestAfterRaising(nint window, int x, int y)
    {
        if (!WindowsHost || window == 0)
        {
            return 0;
        }

        var winner = (nint)0;
        for (var attempt = 0; attempt < MaxRaiseAttempts; attempt++)
        {
            SetWindowPos(window, -1, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
            winner = HitTest(x, y);
            if (winner == window)
            {
                return winner;
            }
        }

        return winner;
    }

    internal static uint ExStyleOf(nint window) =>
        WindowsHost && window != 0 ? (uint)GetWindowLongPtrW(window, GwlExstyle) : 0;

    internal static bool NonActivatingStyleHeld(nint window) => (ExStyleOf(window) & WsExNoactivate) != 0;

    /// <summary>
    /// Write one pixel into a window's own device context from OUTSIDE the capability.
    ///
    /// <para><b>Why the harness is allowed to do this, and why it is the only way.</b> The ink
    /// differential exists precisely because a window's DC is NOT guaranteed to hold what the
    /// painter last drew — <c>Win32PointerSurface.ReadInk</c>'s own remarks say an unpainted
    /// window's DC "holds whatever the OS left in it", and its four control points exist because
    /// "a single one is satisfied by a single stray pixel of the right colour". Both statements are
    /// about a DC something else has touched, and nothing inside the product can construct that.
    /// This can: it dirties one NAMED corner and leaves the other three alone.</para>
    /// </summary>
    /// <returns>True when the OS reports the pixel back as the colour that was written.</returns>
    internal static bool DirtyPixel(nint window, int x, int y, uint colour)
    {
        if (!WindowsHost || window == 0)
        {
            return false;
        }

        var dc = GetDC(window);
        if (dc == 0)
        {
            return false;
        }

        try
        {
            SetPixel(dc, x, y, colour);
            return (GetPixel(dc, x, y) & 0x00FFFFFF) == (colour & 0x00FFFFFF);
        }
        finally
        {
            ReleaseDC(window, dc);
        }
    }

    /// <summary>
    /// Clear <c>WS_EX_NOACTIVATE</c> on a window this process owns, from OUTSIDE the capability.
    ///
    /// <para><b>Why the harness is allowed to do this.</b> The whole reason the capability READS the
    /// style back rather than remembering that it wrote it is that a style is a property of the
    /// operating system's window, and anything with the handle can change it — upstream met exactly
    /// that with recycled pooled shells and said so in its own comment
    /// (<c>Services/BubbleService.cs:4880-4884</c>). This is the only way to construct that state
    /// deterministically on a healthy machine, and without it the capability's style gate is a
    /// branch no fact can reach.</para>
    /// </summary>
    internal static bool ClearNonActivatingStyle(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return false;
        }

        var style = (uint)GetWindowLongPtrW(window, GwlExstyle);
        SetWindowLongPtrW(window, GwlExstyle, (nint)(style & ~WsExNoactivate));
        return (ExStyleOf(window) & WsExNoactivate) == 0;
    }

    internal static bool ClickThroughStyleHeld(nint window) => (ExStyleOf(window) & WsExTransparent) != 0;

    internal static (int X, int Y, int Width, int Height) BoundsOf(nint window)
    {
        if (!WindowsHost || window == 0 || !GetWindowRect(window, out var rect))
        {
            return (0, 0, 0, 0);
        }

        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>Where the OS itself puts a window among the visible top-level ones. A second copy of
    /// the product's walk, deliberately.</summary>
    internal static ZOrderReading ReadZOrder(nint window)
    {
        if (!WindowsHost)
        {
            return new ZOrderReading(-1, -1, 0);
        }

        var index = -1;
        var firstOrdinary = -1;
        var visible = 0;

        for (var candidate = GetTopWindow(0); candidate != 0; candidate = GetWindow(candidate, GwHwndnext))
        {
            if (!IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == window)
            {
                index = visible;
            }
            else if (firstOrdinary < 0 && (ExStyleOf(candidate) & WsExTopmost) == 0)
            {
                firstOrdinary = visible;
            }

            visible++;
        }

        return new ZOrderReading(index, firstOrdinary, visible);
    }

    internal readonly record struct ZOrderReading(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }

    /// <summary>
    /// The four OS read-backs the SHELL's documented rule for "is this window in the task
    /// switcher" is built from, kept apart so a failure can name the clause that decided.
    /// </summary>
    /// <param name="Visible">The OS reports the window visible.</param>
    /// <param name="Owner">The window's owner, or 0 when it is unowned. An OWNED window is
    /// represented in the switcher by its owner, not by itself.</param>
    /// <param name="ToolWindow"><c>WS_EX_TOOLWINDOW</c> is set — the shell's explicit "keep this
    /// out of the task list" bit.</param>
    /// <param name="AppWindow"><c>WS_EX_APPWINDOW</c> is set — the shell's explicit override that
    /// forces an otherwise-excluded window back INTO the list.</param>
    /// <param name="Cloaked">DWM reports the window cloaked (another virtual desktop, a suspended
    /// UWP host). A cloaked window is not offered by the modern switcher.</param>
    internal readonly record struct TaskSwitcherReading(
        bool Visible, nint Owner, bool ToolWindow, bool AppWindow, bool Cloaked)
    {
        /// <summary>
        /// The shell's documented predicate: a window is an ordinary task-switching window when it
        /// is visible, not cloaked, and either unowned-and-not-a-tool-window or explicitly forced in
        /// with <c>WS_EX_APPWINDOW</c>.
        ///
        /// <para><b>What this is and is not.</b> It is the rule the shell publishes and every
        /// switcher-enumeration sample implements; it is NOT the switcher's rendered list, which no
        /// public API exposes. A fact built on it says what the rule answers about a real window's
        /// real, OS-read state — never what a human sees when they hold Alt.</para>
        /// </summary>
        internal bool IsOrdinaryTaskSwitchingWindow =>
            Visible && !Cloaked && (AppWindow || (Owner == 0 && !ToolWindow));

        internal string Clause => !Visible
            ? "not visible"
            : Cloaked ? "DWM-cloaked"
            : AppWindow ? "WS_EX_APPWINDOW forces it in"
            : Owner != 0 ? $"owned by 0x{Owner:X}"
            : ToolWindow ? "WS_EX_TOOLWINDOW keeps it out"
            : "visible, unowned, not a tool window";
    }

    /// <summary>Ask the OS all four parts of the task-switcher rule about one window.</summary>
    internal static TaskSwitcherReading ReadTaskSwitcherState(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return new TaskSwitcherReading(false, 0, false, false, false);
        }

        var style = ExStyleOf(window);
        var cloaked = DwmGetWindowAttribute(window, DwmwaCloaked, out var cloak, sizeof(int)) == 0 && cloak != 0;
        return new TaskSwitcherReading(
            Visible: IsWindowVisible(window),
            Owner: GetWindow(window, GwOwner),
            ToolWindow: (style & WsExToolwindow) != 0,
            AppWindow: (style & WsExAppwindow) != 0,
            Cloaked: cloaked);
    }

    /// <summary>Every visible top-level window this PROCESS owns, in z-order. The teardown suite
    /// walks the z-order the same way; this one keeps the handles rather than counting them,
    /// because the task-switcher rule is asked of each window individually.</summary>
    internal static IReadOnlyList<nint> OurVisibleTopLevelWindows()
    {
        if (!WindowsHost)
        {
            return [];
        }

        var ours = new List<nint>();
        var self = GetCurrentProcessId();
        for (var candidate = GetTopWindow(0); candidate != 0; candidate = GetWindow(candidate, GwHwndnext))
        {
            if (!IsWindowVisible(candidate))
            {
                continue;
            }

            GetWindowThreadProcessId(candidate, out var owner);
            if (owner == self)
            {
                ours.Add(candidate);
            }
        }

        return ours;
    }

    /// <summary>The OS thread that owns a window, or 0. The foreground is scoped to a thread's
    /// input queue, so "these two windows are on different queues" is a question only this can
    /// answer.</summary>
    internal static uint OwningThreadOf(nint window) =>
        WindowsHost && window != 0 ? GetWindowThreadProcessId(window, out _) : 0;

    internal static string DescribeWindow(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return "no window at all";
        }

        var buffer = new StringBuilder(256);
        var copied = GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    /// <summary>
    /// Synthesise a full left click at a point on the virtual desktop: an absolute move, a button
    /// down, and a button up, in one <c>SendInput</c> batch so nothing can move the pointer between
    /// them.
    ///
    /// <para><b>Absolute coordinates are normalised to 0..65535 across the VIRTUAL desktop</b>
    /// (<c>MOUSEEVENTF_VIRTUALDESK</c>), which is why the conversion divides by the primary metrics
    /// only when there is one display and by the virtual extent otherwise. Getting that wrong would
    /// click somewhere else entirely and read as "delivery does not work".</para>
    /// </summary>
    /// <returns>True when the OS accepted all three events. False means UIPI, the secure desktop, or
    /// a locked workstation refused the injection — a machine fact, and every expectation built on it
    /// flips rather than skipping.</returns>
    internal static bool InjectClickAt(int x, int y)
    {
        if (!WindowsHost)
        {
            return false;
        }

        var (nx, ny) = ToAbsolute(x, y);

        var inputs = new Input[3];
        inputs[0].type = InputMouse;
        inputs[0].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = MouseeventfMove | MouseeventfAbsolute | MouseeventfVirtualdesk,
        };
        inputs[1].type = InputMouse;
        inputs[1].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = MouseeventfLeftdown | MouseeventfAbsolute | MouseeventfVirtualdesk | MouseeventfMove,
        };
        inputs[2].type = InputMouse;
        inputs[2].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = MouseeventfLeftup | MouseeventfAbsolute | MouseeventfVirtualdesk | MouseeventfMove,
        };

        return SendInput(3, inputs, Marshal.SizeOf<Input>()) == 3;
    }

    /// <summary>
    /// Synthesise a DRAG onto a named window: a press, a run of moves with the button still held,
    /// and a release, <b>each one drained from this thread's queue before the next is injected</b>.
    ///
    /// <para><b>Why the moves are separate events.</b> The claim being measured is that a passive
    /// region lets a drag reach the desktop underneath, and a drag is <c>WM_MOUSEMOVE</c> carrying
    /// <c>MK_LBUTTON</c> — a single down/up pair with no motion between them is a click and would
    /// prove nothing about the move messages. <paramref name="target"/> counts only the moves that
    /// carry the button.</para>
    ///
    /// <para><b>Why each step waits for its own delivery, MEASURED rather than reasoned.</b> This
    /// was first written as one <c>SendInput</c> batch, then as separate events with a single
    /// <see cref="Pump(int)"/> between them. Both delivered the drag on some runs and not on others
    /// from an identical rig — three runs reading <c>dragMoves=8 moves=10</c> and three reading
    /// <c>dragMoves=0 moves=2</c>. The cause is that <c>WM_MOUSEMOVE</c> is not an ordinary queued
    /// message: the system records the move and SYNTHESISES the message when the thread next peeks,
    /// so an unpeeked move is simply replaced by the next one, and a batch whose button-up had
    /// already landed produced a survivor with no <c>MK_LBUTTON</c> in it. <c>SendInput</c> returns
    /// before the raw input thread has posted anything, so one pump is a race; waiting for the
    /// COUNTER to move is not. That is what a real application does throughout a real drag, and it
    /// is what makes this reading deterministic.</para>
    ///
    /// <para><b>That paragraph is true and it is not the whole story, which cost a day.</b>
    /// <c>dragMoves=0 moves=2</c> has TWO causes that are indistinguishable from those two numbers:
    /// the coalescing above, and the path contention below. Waiting on the counter fixes only the
    /// first, so the reading came back on a machine where nothing about the injection had changed —
    /// and a reader who recognised the signature went looking for a broken <c>SendInput</c> three
    /// times. The counts alone can never separate them; the <see cref="DragReading"/> this now
    /// returns can, and says which.</para>
    ///
    /// <para><b>Why every step HOLDS its own point, and the measurement that forced it.</b> The
    /// press point is the only point the caller ever asked the window manager about, and the drag
    /// then injects eight more at points nobody claimed. Reproduced outside the suite with a second
    /// process holding a topmost window over the drag path and NOT over the press point: the click
    /// lands, the button-down lands, and every single move goes to the interloper's input queue —
    /// <c>downs=2 dragMoves=0 moves=2 wheel=1 keys=1 accepted=True</c>, byte for byte the reading
    /// four <c>OverlayDesktopInputTests</c> facts reported for a day while three diagnoses looked
    /// for a broken injection. The asymmetry is the whole clue and it is structural, not timing: a
    /// BUTTON message is posted to the queue of the window under the cursor when the event is
    /// injected, while a <c>WM_MOUSEMOVE</c> is SYNTHESISED at peek time for whatever owns the
    /// cursor's point then — so a foreign window over part of the rectangle steals the moves and
    /// leaves the clicks alone. <paramref name="hold"/> is therefore called per step and is the
    /// same re-assertion <see cref="HitTestAfterRaising"/> already performs for the click; measured
    /// against the same interloper re-asserting <c>HWND_TOPMOST</c> every 5ms, it recovers 8/8
    /// steps. It never grants the answer: the counters are still the OS's, and a step whose move
    /// does not arrive is reported with the handle that took it.</para>
    ///
    /// <para>Every wait is <see cref="PumpUntil"/> — bounded iteration with a yield, never a
    /// wall-clock wait — so a step whose message never arrives falls out at the ceiling and the
    /// caller reads the unchanged count. The whole path must stay inside the window's rectangle: a
    /// drag that leaves it is a drag over somebody else's window.</para>
    /// </summary>
    /// <param name="target">The window the drag is aimed at, and the counter each step waits on.</param>
    /// <param name="x">Where the press happens.</param>
    /// <param name="y">Ditto.</param>
    /// <param name="deltaX">Total horizontal travel, split evenly across <paramref name="steps"/>.</param>
    /// <param name="deltaY">Total vertical travel.</param>
    /// <param name="steps">How many move events the drag is made of. Must be positive.</param>
    /// <param name="hold">Re-assert the caller's own window stack over a point of the path and
    /// answer who the window manager says owns it. The caller supplies it because only the caller
    /// knows the ordering its leg requires — an overlay that must stay ABOVE the target is
    /// re-asserted here too, so holding the point can never quietly move the target above the very
    /// surface the leg is measuring through.</param>
    /// <returns>What the OS accepted, how much of the path arrived, and who took the rest.</returns>
    internal static DragReading InjectDragAt(
        ScratchTarget target, int x, int y, int deltaX, int deltaY, int steps, Func<int, int, nint> hold)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hold);

        if (!WindowsHost || steps <= 0)
        {
            return new DragReading(false, Math.Max(0, steps), 0, 0, 0, 0);
        }

        // No counter wait on the press-point move: the caller has usually just clicked this exact
        // point, the OS emits no WM_MOUSEMOVE for a cursor that does not move, and waiting for one
        // burned the whole PumpUntil ceiling on EVERY drag — hundreds of milliseconds in which a
        // foreign topmost window can arrive over the path. Nothing measures this move; the
        // synchronisation the drag actually needs is the button-down below, which is waited for,
        // and one drain keeps the queue's key state current before it.
        var accepted = SendMouse(x, y, MouseeventfMove);
        Pump(64);

        var downs = target.Downs;
        accepted &= SendMouse(x, y, MouseeventfLeftdown | MouseeventfMove);
        PumpUntil(() => target.Downs > downs);

        var delivered = 0;
        var contender = (nint)0;
        var contendedX = 0;
        var contendedY = 0;

        for (var step = 1; step <= steps; step++)
        {
            var stepX = x + (deltaX * step / steps);
            var stepY = y + (deltaY * step / steps);

            // Held, then DRAINED, then the counter is read: a z-order change can itself make the
            // system re-synthesise a move, and counting that as this step's delivery would be the
            // instrument answering its own question.
            var owner = hold(stepX, stepY);
            Pump(64);

            var moves = target.Moves;
            accepted &= SendMouse(stepX, stepY, MouseeventfMove);
            if (PumpUntil(() => target.Moves > moves))
            {
                delivered++;
            }
            else if (contender == 0)
            {
                contender = owner;
                contendedX = stepX;
                contendedY = stepY;
            }
        }

        var ups = target.Ups;
        accepted &= SendMouse(x + deltaX, y + deltaY, MouseeventfLeftup | MouseeventfMove);
        PumpUntil(() => target.Ups > ups);
        return new DragReading(accepted, steps, delivered, contender, contendedX, contendedY);
    }

    /// <summary>
    /// What one <see cref="InjectDragAt"/> did, in enough detail that a drag which did not arrive
    /// names the window that took it instead of leaving a reader with two bare numbers.
    /// </summary>
    /// <param name="Accepted">The OS accepted every event of the drag.</param>
    /// <param name="Steps">How many move events the drag was made of.</param>
    /// <param name="Delivered">How many of them reached <c>target</c>'s window procedure.</param>
    /// <param name="Contender">Who the window manager said owned the first step that did not
    /// arrive, read while the caller's own stack was held over that point. 0 when every step
    /// landed.</param>
    /// <param name="ContendedX">And where that step was aimed.</param>
    /// <param name="ContendedY">Ditto.</param>
    internal readonly record struct DragReading(
        bool Accepted, int Steps, int Delivered, nint Contender, int ContendedX, int ContendedY)
    {
        internal string Describe => Delivered >= Steps
            ? $"drag path {Delivered}/{Steps} steps delivered"
            : $"drag path {Delivered}/{Steps} steps delivered — the first that did not arrive was aimed at "
                + $"({ContendedX},{ContendedY}), which the window manager gives to {DescribeWindow(Contender)} "
                + "even with this run's own stack re-asserted over it";
    }

    /// <summary>One absolute mouse event at a point on the virtual desktop.</summary>
    private static bool SendMouse(int x, int y, uint flags)
    {
        var (nx, ny) = ToAbsolute(x, y);
        var inputs = new Input[1];
        inputs[0].type = InputMouse;
        inputs[0].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = flags | MouseeventfAbsolute | MouseeventfVirtualdesk,
        };

        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    /// <summary>
    /// Synthesise a wheel notch with the pointer parked at a point.
    ///
    /// <para><b>Where a wheel notch is DELIVERED is not a hit test</b>, and a fact built on this
    /// must not pretend otherwise: <c>WM_MOUSEWHEEL</c> classically goes to the FOCUS window of the
    /// foreground thread, and Windows 10 and 11 additionally route it to the window under the
    /// pointer when "scroll inactive windows when I hover over them" is on — a per-user setting
    /// this harness neither reads nor changes. Both routes are measured the same way here (the
    /// pointer is moved onto the point AND the window underneath holds the focus), so the reading
    /// is "the notch reached the desktop underneath" under whichever rule this machine uses.</para>
    /// </summary>
    /// <returns>True when the OS accepted both events.</returns>
    internal static bool InjectWheelAt(int x, int y, int notches)
    {
        if (!WindowsHost)
        {
            return false;
        }

        var (nx, ny) = ToAbsolute(x, y);
        var inputs = new Input[2];
        inputs[0].type = InputMouse;
        inputs[0].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = MouseeventfMove | MouseeventfAbsolute | MouseeventfVirtualdesk,
        };
        inputs[1].type = InputMouse;
        inputs[1].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            mouseData = unchecked((uint)(notches * WheelDelta)),
            dwFlags = MouseeventfWheel | MouseeventfAbsolute | MouseeventfVirtualdesk,
        };

        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }

    /// <summary>
    /// Inject one key down/up pair at OS level, through the same system input stream
    /// <see cref="InjectClickAt"/> uses.
    ///
    /// <para>Declared here rather than borrowed from <see cref="InputWindowProbe"/> for the reason
    /// that instrument's own header gives for re-declaring everything: the two probes are
    /// deliberately independent copies, so "the keystroke arrived" and "the click arrived" are not
    /// two readings of one code path.</para>
    /// </summary>
    /// <returns>True when the OS accepted both events. False means UIPI, the secure desktop, or a
    /// locked workstation refused the injection.</returns>
    internal static bool InjectKey(ushort virtualKey)
    {
        if (!WindowsHost)
        {
            return false;
        }

        var inputs = new Input[2];
        inputs[0].type = InputKeyboard;
        inputs[0].U.ki = new KeybdInput { wVk = virtualKey };
        inputs[1].type = InputKeyboard;
        inputs[1].U.ki = new KeybdInput { wVk = virtualKey, dwFlags = KeyeventfKeyup };
        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }

    /// <summary>Move the pointer without pressing anything, so a fact can restore the cursor it
    /// borrowed.</summary>
    internal static bool MovePointerTo(int x, int y)
    {
        if (!WindowsHost)
        {
            return false;
        }

        var (nx, ny) = ToAbsolute(x, y);
        var inputs = new Input[1];
        inputs[0].type = InputMouse;
        inputs[0].U.mi = new MouseInput
        {
            dx = nx,
            dy = ny,
            dwFlags = MouseeventfMove | MouseeventfAbsolute | MouseeventfVirtualdesk,
        };

        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    /// <summary>Where the pointer is right now, so a run can put it back.</summary>
    internal static (int X, int Y) CursorPosition()
    {
        if (WindowsHost && GetCursorPos(out var point))
        {
            return (point.X, point.Y);
        }

        return (0, 0);
    }

    private static (int X, int Y) ToAbsolute(int x, int y)
    {
        var left = GetSystemMetrics(76);     // SM_XVIRTUALSCREEN
        var top = GetSystemMetrics(77);      // SM_YVIRTUALSCREEN
        var width = Math.Max(1, GetSystemMetrics(78));   // SM_CXVIRTUALSCREEN
        var height = Math.Max(1, GetSystemMetrics(79));  // SM_CYVIRTUALSCREEN

        return (
            (int)Math.Round((x - left) * 65535.0 / width),
            (int)Math.Round((y - top) * 65535.0 / height));
    }

    /// <summary>
    /// Put one left-button-down into ONE named window's queue. No cursor, no <c>SendInput</c>, no
    /// hit test and nothing timed: the message is ADDRESSED, so "which window's mail was it" is a
    /// fact rather than a race, and "who drained it" can be asserted exactly.
    /// </summary>
    internal static bool PostLeftDown(nint window) =>
        WindowsHost && window != 0 && PostMessageW(window, WmLbuttondown, 0, 0);

    /// <summary>Drain and dispatch up to <paramref name="max"/> of this thread's messages.</summary>
    internal static int Pump(int max)
    {
        if (!WindowsHost)
        {
            return 0;
        }

        var dispatched = 0;
        while (dispatched < max && PeekMessageW(out var msg, 0, 0, 0, PmRemove))
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
            dispatched++;
        }

        return dispatched;
    }

    /// <summary>Pump until <paramref name="done"/> or the iteration ceiling. Bounded iteration with a
    /// yield, never a wall-clock wait.</summary>
    internal static bool PumpUntil(Func<bool> done)
    {
        for (var i = 0; i < MaxPumpIterations; i++)
        {
            Pump(64);
            if (done())
            {
                return true;
            }

            Thread.Yield();
        }

        return done();
    }

    /// <summary>Drain a fixed budget, so a "nothing arrived" is not satisfied by a message still in
    /// flight.</summary>
    internal static void Drain()
    {
        for (var i = 0; i < NegativeDrainIterations; i++)
        {
            Pump(64);
            Thread.Yield();
        }
    }

    /// <summary>
    /// The instrument's own control rig: a scratch NON-ACTIVATING window that counts the mouse
    /// messages the OS delivers to it and the <c>WM_MOUSEACTIVATE</c> queries it answered.
    ///
    /// <para>Its only purpose is to answer "can this instrument see a click arrive, and can it see
    /// one NOT arrive" without asking the product anything. Every product fact in the pointer suite
    /// is worthless until this one passes.</para>
    /// </summary>
    internal sealed class ScratchTarget : IDisposable
    {
        private readonly string _className;
        private readonly WndProc _proc;
        private readonly nint _module;
        private ushort _atom;
        private nint _window;
        private int _downs;
        private int _ups;
        private int _activationsRefused;
        private int _dragMoves;
        private int _moves;
        private int _wheelNotches;
        private int _keyDowns;
        private int _activations;

        private ScratchTarget(nint module, ushort atom, nint window, WndProc proc, string className)
        {
            _module = module;
            _atom = atom;
            _window = window;
            _proc = proc;
            _className = className;
        }

        internal nint Window => _window;

        internal int Downs => Volatile.Read(ref _downs);

        internal int Ups => Volatile.Read(ref _ups);

        internal int ActivationsRefused => Volatile.Read(ref _activationsRefused);

        /// <summary><c>WM_MOUSEMOVE</c> messages that arrived with <c>MK_LBUTTON</c> held — the
        /// DRAG channel. A move without the button is not counted, because a pointer merely
        /// travelling over a window proves nothing about dragging through it.</summary>
        internal int DragMoves => Volatile.Read(ref _dragMoves);

        /// <summary>EVERY <c>WM_MOUSEMOVE</c> delivered, with or without the button. It exists to
        /// tell two very different failures apart: no move reached this window at all (a routing or
        /// delivery problem), versus moves reached it without <c>MK_LBUTTON</c> (the coalescing
        /// problem <see cref="InjectDragAt"/>'s remarks describe). A drag fact that could not say
        /// which one it hit would send its reader after the wrong cause.</summary>
        internal int Moves => Volatile.Read(ref _moves);

        /// <summary>Wheel notches delivered, summed by magnitude rather than counted by message, so
        /// a machine that coalesces two notches into one <c>WM_MOUSEWHEEL</c> still reports the
        /// travel the harness asked for.</summary>
        internal int WheelNotches => Volatile.Read(ref _wheelNotches);

        /// <summary><c>WM_KEYDOWN</c> messages delivered — the TYPE channel.</summary>
        internal int KeyDowns => Volatile.Read(ref _keyDowns);

        /// <summary>How many times the OS made this window ACTIVE (<c>WM_ACTIVATE</c> with anything
        /// other than <c>WA_INACTIVE</c>). Zero is the reading that says a click somewhere above
        /// this window did not reach down and activate it.</summary>
        internal int Activations => Volatile.Read(ref _activations);

        /// <summary>Create and show a topmost scratch target at a rectangle.</summary>
        /// <param name="activatable">
        /// <b>False (the default) is the landed behaviour</b>: <c>WS_EX_NOACTIVATE</c> plus an
        /// <c>MA_NOACTIVATE</c> answer to every <c>WM_MOUSEACTIVATE</c>, which is what the pointer
        /// and video routing runs want — a window that takes a click and takes NOTHING else.
        ///
        /// <para><b>True is required for two claims that cannot be made against such a window at
        /// all.</b> "A passive region lets the user TYPE into the desktop underneath" needs a window
        /// that can hold the foreground thread's keyboard focus, and a <c>WS_EX_NOACTIVATE</c>
        /// window is exactly the one the OS will not give it to. "A handled overlay click does not
        /// ACTIVATE the application underneath" needs a window the OS would otherwise have been
        /// willing to activate, or the absence being asserted is a property of the instrument
        /// instead of the surface.</para>
        /// </param>
        /// <param name="toolWindow">
        /// <b>True (the default) is the landed behaviour.</b> False drops <c>WS_EX_TOOLWINDOW</c>,
        /// which is what makes a visible unowned top-level window an ORDINARY task-switching window
        /// by the shell's documented rule — the control the task-switcher fact needs so that "no
        /// surface of ours is a task-switching window" is not merely a predicate that always
        /// answers no.
        /// </param>
        internal static ScratchTarget? Create(
            int x,
            int y,
            int width,
            int height,
            bool clickThrough = false,
            bool activatable = false,
            bool toolWindow = true)
        {
            if (!WindowsHost)
            {
                return null;
            }

            var module = GetModuleHandleW(null);
            ScratchTarget? built = null;

            nint Proc(nint window, uint message, nint wParam, nint lParam)
            {
                switch (message)
                {
                    case WmMouseactivate:
                        if (built is not null && !activatable)
                        {
                            Interlocked.Increment(ref built._activationsRefused);
                        }

                        // MA_NOACTIVATE. An activatable target must fall through to DefWindowProc
                        // instead, or it would refuse the very activation the leak fact is asking
                        // the operating system whether it performs.
                        if (!activatable)
                        {
                            return 3;
                        }

                        break;

                    case WmActivate:
                        if (built is not null && (wParam & 0xFFFF) != 0)   // anything but WA_INACTIVE
                        {
                            Interlocked.Increment(ref built._activations);
                        }

                        break;

                    case WmLbuttondown:
                        if (built is not null)
                        {
                            Interlocked.Increment(ref built._downs);
                        }

                        return 0;

                    case WmLbuttonup:
                        if (built is not null)
                        {
                            Interlocked.Increment(ref built._ups);
                        }

                        return 0;

                    case WmMousemove:
                        if (built is not null)
                        {
                            Interlocked.Increment(ref built._moves);
                            if ((wParam & MkLbutton) != 0)
                            {
                                Interlocked.Increment(ref built._dragMoves);
                            }
                        }

                        return 0;

                    case WmMousewheel:
                        if (built is not null)
                        {
                            var delta = (short)((wParam >> 16) & 0xFFFF);
                            Interlocked.Add(ref built._wheelNotches, Math.Abs(delta) / WheelDelta);
                        }

                        return 0;

                    case WmKeydown:
                        if (built is not null)
                        {
                            Interlocked.Increment(ref built._keyDowns);
                        }

                        return 0;
                }

                return DefWindowProcW(window, message, wParam, lParam);
            }

            WndProc proc = Proc;
            var className = "CcpProbePointerTarget." + Guid.NewGuid().ToString("N");
            var windowClass = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = module,
                lpszClassName = className,
            };

            var atom = RegisterClassExW(ref windowClass);
            if (atom == 0)
            {
                return null;
            }

            var exStyle = (activatable ? 0 : WsExNoactivate)
                | (toolWindow ? WsExToolwindow : 0)
                | (clickThrough ? WsExTransparent : 0);
            var window = CreateWindowExW(
                exStyle, className, "CCP probe pointer target", WsPopup, x, y, width, height, 0, 0, module, 0);

            if (window == 0)
            {
                UnregisterClassW(className, module);
                return null;
            }

            built = new ScratchTarget(module, atom, window, proc, className);
            SetWindowPos(window, -1, x, y, width, height, SwpShowwindow | SwpNoactivate);
            return built;
        }

        internal void MoveTo(int x, int y, int width, int height)
        {
            if (WindowsHost && _window != 0)
            {
                SetWindowPos(_window, -1, x, y, width, height, SwpNoactivate);
            }
        }

        internal void Hide()
        {
            if (WindowsHost && _window != 0)
            {
                ShowWindow(_window, SwHide);
            }
        }

        public void Dispose()
        {
            if (!WindowsHost)
            {
                return;
            }

            if (_window != 0)
            {
                DestroyWindow(_window);
                _window = 0;
            }

            if (_atom != 0)
            {
                UnregisterClassW(_className, _module);
                _atom = 0;
            }
        }
    }

    /// <summary>
    /// A <see cref="ScratchTarget"/> created, focused and pumped on a <b>SECOND THREAD</b>.
    ///
    /// <para><b>Why the thread, and why no fact about foreground activation is honest without
    /// it.</b> Windows scopes the foreground to a THREAD'S INPUT QUEUE, and
    /// <c>WS_EX_NOACTIVATE</c> governs the CROSS-QUEUE activation a click would otherwise cause.
    /// Two windows on one thread share one queue, so "the foreground moved from one to the other"
    /// there is a statement about that thread's active window and NOT about the style — measured
    /// directly while writing this: with the foreground keeper on the run's own thread, a click on
    /// the <c>WS_EX_NOACTIVATE</c> overlay moved <c>GetForegroundWindow</c> to the overlay, and
    /// with the identical keeper on its own thread it did not. A fact that skipped this rig would
    /// have reported the port stealing focus when what it had measured was the harness.</para>
    ///
    /// <para>Its thread owns the window for the window's whole life: a native window may be
    /// created, focused and destroyed only by its own thread, so creation, <c>SetFocus</c>, the
    /// message loop and the disposal all happen inside the thread body. The loop is a BLOCKING
    /// QUEUE WAIT rather than a wall-clock wait — it returns when a message arrives and ends when
    /// <see cref="Dispose"/> posts <c>WM_QUIT</c> — which is the same shape, for the same reason, as
    /// <c>InputWindowProbe.ParkedWindow</c>.</para>
    /// </summary>
    internal sealed class ParkedScratchTarget : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new(false);
        private Thread? _thread;
        private ScratchTarget? _target;
        private uint _threadId;
        private bool _disposed;

        private ParkedScratchTarget()
        {
        }

        internal nint Window => _target?.Window ?? 0;

        internal int Downs => _target?.Downs ?? 0;

        internal int KeyDowns => _target?.KeyDowns ?? 0;

        internal int Activations => _target?.Activations ?? 0;

        /// <summary>The OS thread that owns this window. A fact comparing it with the thread that
        /// owns the surface under test is what proves the two queues really are different.</summary>
        internal uint OwningThreadId => _threadId;

        internal static ParkedScratchTarget Start(int x, int y, int width, int height)
        {
            var parked = new ParkedScratchTarget();
            if (!WindowsHost)
            {
                return parked;
            }

            parked._thread = new Thread(() =>
            {
                parked._threadId = GetCurrentThreadId();
                parked._target = ScratchTarget.Create(x, y, width, height, activatable: true);

                // From the owning thread, which is the only thread SetFocus will accept it from.
                if (parked._target is { Window: not 0 })
                {
                    SetFocus(parked._target.Window);
                }

                parked._ready.Set();

                while (GetMessageW(out var message, 0, 0, 0))
                {
                    TranslateMessage(ref message);
                    DispatchMessageW(ref message);
                }

                // DestroyWindow is refused from any thread but this one.
                parked._target?.Dispose();
            })
            {
                IsBackground = true,
                Name = "ccp-pointer-probe-parked",
            };

            parked._thread.Start();

            // BOUNDED, not pinned: the wedge is not the subject here, it is bring-up. A bare wait
            // would take the whole run down with no failing test name if the thread died before
            // Set().
            TestWait.UntilSync(
                () => parked._ready.IsSet, "the parked pointer target thread to create and focus its window");
            return parked;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (WindowsHost && _threadId != 0)
            {
                PostThreadMessageW(_threadId, WmQuit, 0, 0);
            }

            _thread?.Join();
            _ready.Dispose();
        }
    }

    // ---- interop ---------------------------------------------------------------------------

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out RectNative rect);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint parent);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint window, StringBuilder buffer, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg msg, nint window, uint filterMin, uint filterMax, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMessageW(out Msg msg, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetFocus(nint window);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint SetPixel(nint dc, int x, int y, uint colour);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
