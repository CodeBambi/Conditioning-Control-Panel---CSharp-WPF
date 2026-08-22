using System.Runtime.InteropServices;

namespace CcpClient.Tests;

/// <summary>
/// The independent input instrument: it asks the OPERATING SYSTEM whether a window is really
/// receiving input, without asking the product.
///
/// <para><b>Why it re-declares every P/Invoke instead of using the product's interop.</b> Same
/// reason <see cref="OverlayWindowProbe"/> does, with the polarity reversed and the stakes higher.
/// The overlay packet proved a surface is click-THROUGH and its review recorded that "input passes
/// through" is the property most easily faked, because a window that does not exist satisfies it
/// too. Proving the OPPOSITE is faked just as cheaply: a handler was attached, a style was not set,
/// a call returned. These declarations are a second, independent copy, so "the presence says it has
/// the keyboard" and "the OS routes a synthesised keystroke somewhere else" are two facts produced
/// by two code paths.</para>
///
/// <para><b>THE TRAP THIS INSTRUMENT EXISTS TO EXPOSE, and it is not hypothetical.</b>
/// <c>GetGUIThreadInfo(<i>someThreadId</i>).hwndFocus</c> is THREAD-LOCAL: it answers "which window
/// this thread's input queue considers focused", and it answers with YOUR window while the
/// foreground — and every keystroke — belongs to somebody else entirely. Measured before any of
/// this was written (this packet's plan.md §0, run 1): a plain <c>SetForegroundWindow</c> returned
/// FALSE, zero injected keystrokes arrived, and the thread-local read still said "ours".
/// <see cref="RunNegativeControl"/> REPRODUCES that state deterministically, in-process, on every
/// suite run, by parking a focused window on a second thread while the foreground sits on the
/// first. The system-wide read is <c>GetGUIThreadInfo(<b>0</b>)</c> — documented as the foreground
/// thread — and it is the only one this port is allowed to build a claim on.</para>
///
/// <para><b>Why <c>SendInput</c> is a valid delivery oracle.</b> It injects at the bottom of the
/// system input stream: the OS's own raw input thread routes it by the same rules it routes a
/// keyboard, to the foreground thread's queue and thence to the focus window. It is one step below
/// the driver and one step above the hardware. What it is NOT is a human, or a physical key, and it
/// is refused outright by UIPI against a higher-integrity window and by the secure desktop. The
/// suite detects a refusal (<c>SendInput</c> returns 0, or the foreground grab fails) and the
/// expectation flips with the machine — never a skip.</para>
///
/// <para>Every native path is guarded by a platform check HERE, in the helper, so the fact bodies
/// stay free of predicates and none of their assertions can be silenced.</para>
/// </summary>
internal static class InputWindowProbe
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolwindow = 0x00000080;

    /// <summary>A window the OS refuses to ACTIVATE. See <c>ScratchWindow.Create</c>'s
    /// <c>activatable</c> parameter.</summary>
    private const uint WsExNoactivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const int GwlExstyle = -20;
    private const uint SwpShowwindow = 0x0040;
    private const uint GwHwndnext = 2;
    private const int SmCmonitors = 80;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private static readonly nint HwndTopmost = -1;

    private const uint WmKeydown = 0x0100;
    private const uint WmChar = 0x0102;
    private const uint WmQuit = 0x0012;
    private const uint PmRemove = 0x0001;

    /// <summary>A key with no character and no default meaning on any standard layout. Chosen so
    /// that a keystroke which escapes to another window — the exact thing the negative control
    /// arranges — cannot type anything into it.</summary>
    internal const ushort VkF13 = 0x7C;

    /// <summary>The one character key this probe ever injects, and only ever after the OS has
    /// confirmed the catcher holds the foreground AND the focus.</summary>
    internal const ushort VkA = 0x41;

    /// <summary>
    /// Bounded iteration ceiling for "pump until it arrives". Injected input crosses the OS's raw
    /// input thread before it reaches a queue, so the answer is not available on the calling
    /// instruction — measured arriving on the FIRST pump (plan.md §0, F5). Bounded iteration with
    /// a yield absorbs a loaded machine without a wall-clock wait, which this suite forbids.
    /// </summary>
    private const int MaxPumpIterations = 4096;

    /// <summary>How long a negative is drained for before it is believed. A "did not arrive" that
    /// only pumped once would be satisfied by a message still in flight.</summary>
    private const int NegativeDrainIterations = 512;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>Does this session have an interactive desktop with a display on it? Without one
    /// nothing can take the foreground and the honest expectation flips. A property of the machine,
    /// established by the test rather than taken from the product.</summary>
    internal static bool MachineHasInteractiveDesktop =>
        WindowsHost && GetSystemMetrics(SmCmonitors) >= 1 && GetDesktopWindow() != 0;

    internal static (int Width, int Height) PrimarySize =>
        WindowsHost ? (GetSystemMetrics(SmCxscreen), GetSystemMetrics(SmCyscreen)) : (0, 0);

    internal static bool WindowExists(nint window) => WindowsHost && window != 0 && IsWindow(window);

    internal static bool WindowIsVisible(nint window) => WindowsHost && window != 0 && IsWindowVisible(window);

    internal static nint Foreground() => WindowsHost ? GetForegroundWindow() : 0;

    /// <summary>The OS's system-wide answer to "where does the next keystroke go":
    /// <c>GetGUIThreadInfo(0).hwndFocus</c>, the FOREGROUND thread's focus.</summary>
    internal static nint SystemKeyboardFocus()
    {
        if (!WindowsHost)
        {
            return 0;
        }

        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(0, ref info) ? info.hwndFocus : 0;
    }

    /// <summary>The THREAD-LOCAL answer — the one that lies. Present in this instrument only so a
    /// fact can assert the two disagree in the state that produced the trap.</summary>
    internal static nint ThreadKeyboardFocusOf(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return 0;
        }

        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { cbSize = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
    }

    internal static nint HitTest(int x, int y) =>
        WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

    /// <summary>Where the OS itself puts a window among the visible top-level windows.</summary>
    internal readonly record struct ZOrderReading(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && (FirstOrdinaryIndex < 0 || Index < FirstOrdinaryIndex);
    }

    internal static ZOrderReading ReadZOrder(nint window)
    {
        if (!WindowsHost || window == 0)
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
            else if (firstOrdinary < 0 && ((uint)GetWindowLongPtrW(candidate, GwlExstyle) & WsExTopmost) == 0)
            {
                firstOrdinary = visible;
            }

            visible++;
        }

        return new ZOrderReading(index, firstOrdinary, visible);
    }

    internal static string DescribeWindow(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return "no window";
        }

        var buffer = new System.Text.StringBuilder(256);
        var copied = GetClassNameW(window, buffer, buffer.Capacity);
        return copied > 0 ? $"0x{window:X} (class \"{buffer}\")" : $"0x{window:X}";
    }

    /// <summary>Inject one key down/up pair at OS level. Returns false when the OS refused the
    /// injection outright (a locked workstation, the secure desktop, UIPI).</summary>
    internal static bool InjectKey(ushort virtualKey)
    {
        if (!WindowsHost)
        {
            return false;
        }

        var inputs = new Input[2];
        inputs[0].type = 1;
        inputs[0].U.ki = new KeybdInput { wVk = virtualKey };
        inputs[1].type = 1;
        inputs[1].U.ki = new KeybdInput { wVk = virtualKey, dwFlags = 0x0002 };
        return SendInput(2, inputs, Marshal.SizeOf<Input>()) == 2;
    }

    /// <summary>
    /// Give a window this thread's keyboard focus WITHOUT changing which window is the foreground.
    ///
    /// <para>The two are different facts and this is the only way to hold them apart:
    /// <c>SetFocus</c> moves the focus inside the CALLING thread's input queue, so with two windows
    /// on one thread the foreground can be one of them while the focus is the other. That state is
    /// what makes the foreground clause of a capture check non-redundant.</para>
    /// </summary>
    internal static void FocusWindow(nint window)
    {
        if (WindowsHost && window != 0)
        {
            SetFocus(window);
        }
    }

    /// <summary>
    /// Put one <c>WM_CHAR</c> straight into a window's queue.
    ///
    /// <para><b>This is NOT the OS routing input</b>, and no fact built on it may say it is — that
    /// claim belongs to <see cref="InjectKey"/>, which goes through the system input stream. It
    /// exists for the one thing injection cannot do safely: hand this window a CONTROL character
    /// (Ctrl+V's 0x16, Ctrl+Z's 0x1A) without pressing Ctrl+V on the user's desktop, where a
    /// keystroke that escaped the card would paste into whatever window caught it.</para>
    /// </summary>
    internal static void PostCharacter(nint window, char character)
    {
        if (WindowsHost && window != 0)
        {
            PostMessageW(window, WmChar, character, 0);
        }
    }

    /// <summary>Drain and dispatch up to <paramref name="max"/> of this thread's messages,
    /// translating each so a key becomes a character.</summary>
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

    /// <summary>Pump until <paramref name="done"/> or the iteration ceiling. Bounded iteration with
    /// a yield, never a wall-clock wait.</summary>
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
    /// The probe's own foreground escalation, independent of the product's. Rung 1 is what WPF does
    /// (<c>Windows/LockCardWindow.xaml.cs:598</c>) and was measured FAILING from a non-foreground
    /// process; rung 2 attaches this thread's input queue to the foreground thread's and asks again.
    /// </summary>
    internal static bool TakeForeground(nint window)
    {
        if (!WindowsHost || window == 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < 32; attempt++)
        {
            SetForegroundWindow(window);
            SetFocus(window);
            if (GetForegroundWindow() == window && SystemKeyboardFocus() == window)
            {
                return true;
            }

            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == 0 ? 0u : GetWindowThreadProcessId(foreground, out _);
            var ourThread = GetCurrentThreadId();
            var attached = foregroundThread != 0 && foregroundThread != ourThread
                && AttachThreadInput(ourThread, foregroundThread, true);
            try
            {
                SetForegroundWindow(window);
                BringWindowToTop(window);
                SetActiveWindow(window);
                SetFocus(window);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(ourThread, foregroundThread, false);
                }
            }

            if (GetForegroundWindow() == window && SystemKeyboardFocus() == window)
            {
                return true;
            }
        }

        return false;
    }

    // ---------- the instrument's own self-check ----------

    /// <param name="MachineHasInteractiveDesktop">Whether this session has a desktop and a display.</param>
    /// <param name="CatcherCreated">Whether the scratch catcher came up.</param>
    /// <param name="CatcherTookForeground">The catcher, escalated, must become the foreground window
    /// AND the foreground thread's focus. Without this the whole instrument is inert.</param>
    /// <param name="KeyInjected"><c>SendInput</c> was accepted by the OS.</param>
    /// <param name="KeyDeliveredToCatcher">
    /// The injected <c>VK_F13</c> reached the catcher's own window procedure. THE positive leg: if
    /// this is false the oracle cannot detect delivery at all and every input fact here is worthless.
    /// </param>
    /// <param name="CharacterDeliveredToCatcher">The injected <c>VK_A</c> arrived as a translated
    /// <c>WM_CHAR 'a'</c> — the typing path, not merely the key path.</param>
    /// <param name="ThiefTookForeground">A second scratch window took the foreground away.</param>
    /// <param name="KeyDeliveredWhileThiefHoldsForeground">
    /// Must be FALSE. The differential: without it, "the keystroke arrived" is also true of an
    /// instrument that counts its own calls.
    /// </param>
    /// <param name="ParkedWindowCreated">The second-thread window that reproduces the trap.</param>
    /// <param name="ThreadLocalFocusClaimsTheParkedWindow">
    /// Must be TRUE, and it is the measurement that makes the system-wide read load-bearing: the
    /// thread-local API cheerfully names a window that is not the foreground and receives nothing.
    /// </param>
    /// <param name="SystemFocusRejectsTheParkedWindow">Must be TRUE — the same instant, the
    /// foreground thread's focus is somebody else.</param>
    /// <param name="KeyDeliveredToParkedWindow">Must be FALSE: nothing typed goes there.</param>
    /// <param name="ScratchWindowsExistAfterCleanup">Must be false, or the existence check cannot
    /// say no.</param>
    internal sealed record NegativeControl(
        bool MachineHasInteractiveDesktop,
        bool CatcherCreated,
        bool CatcherTookForeground,
        bool KeyInjected,
        bool KeyDeliveredToCatcher,
        bool CharacterDeliveredToCatcher,
        bool ThiefTookForeground,
        bool KeyDeliveredWhileThiefHoldsForeground,
        bool ParkedWindowCreated,
        bool ThreadLocalFocusClaimsTheParkedWindow,
        bool SystemFocusRejectsTheParkedWindow,
        bool KeyDeliveredToParkedWindow,
        bool ScratchWindowsExistAfterCleanup);

    /// <summary>
    /// Builds a three-window rig — a catcher on this thread, a thief on this thread, and a focused
    /// window parked on a SECOND thread — and measures the three things every input fact in the
    /// suite depends on: that a synthesised keystroke can be seen arriving, that it can be seen NOT
    /// arriving, and that the thread-local focus read is a liar.
    /// </summary>
    internal static NegativeControl RunNegativeControl()
    {
        if (!WindowsHost)
        {
            return new NegativeControl(false, false, false, false, false, false, false, false, false, false, false, false, false);
        }

        var (screenWidth, screenHeight) = PrimarySize;
        const int width = 240;
        const int height = 160;

        // Right of centre, so this rig never shares a point with the observations' own card.
        var x = Math.Max(0, ((screenWidth - width) / 2) + 300);
        var y = Math.Max(0, (screenHeight - height) / 2);

        using var parked = ParkedWindow.Start(x, y + height + 12, width, height);
        var catcher = ScratchWindow.Create("CcpInputProbeCatcher", x, y, width, height, show: true);
        var thief = ScratchWindow.Create("CcpInputProbeThief", x + 24, y + 24, width, height, show: true);

        var catcherTookForeground = TakeForeground(catcher.Handle);

        var injected = InjectKey(VkF13);
        var deliveredToCatcher = PumpUntil(() => catcher.KeyDownCount(VkF13) > 0);

        // The character leg, and it is gated on the catcher genuinely holding the input: a leaked
        // 'a' would be typed into whatever window the OS considered focused instead.
        var characterDelivered = false;
        if (catcherTookForeground && GetForegroundWindow() == catcher.Handle && SystemKeyboardFocus() == catcher.Handle)
        {
            InjectKey(VkA);
            characterDelivered = PumpUntil(() => catcher.LastCharacter == 'a');
        }

        // The trap, read while the catcher holds the foreground: the parked window's OWN thread
        // still calls it focused, and the system does not.
        var threadLocalClaimsParked = ThreadKeyboardFocusOf(parked.Handle) == parked.Handle;
        var systemRejectsParked = SystemKeyboardFocus() != parked.Handle;

        var thiefTookForeground = TakeForeground(thief.Handle);
        var catcherBefore = catcher.KeyDownCount(VkF13);
        InjectKey(VkF13);
        Drain();
        var deliveredWhileThief = catcher.KeyDownCount(VkF13) > catcherBefore;
        var deliveredToParked = parked.KeyDownCount > 0;

        var handles = new[] { catcher.Handle, thief.Handle };
        catcher.Dispose();
        thief.Dispose();

        return new NegativeControl(
            MachineHasInteractiveDesktop: MachineHasInteractiveDesktop,
            CatcherCreated: handles[0] != 0,
            CatcherTookForeground: catcherTookForeground,
            KeyInjected: injected,
            KeyDeliveredToCatcher: deliveredToCatcher,
            CharacterDeliveredToCatcher: characterDelivered,
            ThiefTookForeground: thiefTookForeground,
            KeyDeliveredWhileThiefHoldsForeground: deliveredWhileThief,
            ParkedWindowCreated: parked.Handle != 0,
            ThreadLocalFocusClaimsTheParkedWindow: threadLocalClaimsParked,
            SystemFocusRejectsTheParkedWindow: systemRejectsParked,
            KeyDeliveredToParkedWindow: deliveredToParked,
            ScratchWindowsExistAfterCleanup: handles.Any(WindowExists));
    }

    /// <summary>A throwaway top-level window this probe owns, with a recording window procedure.</summary>
    internal sealed class ScratchWindow : IDisposable
    {
        private readonly string _className;
        private readonly WndProc _proc;
        private readonly nint _module;
        private readonly Dictionary<ushort, int> _keyDowns = new();
        private bool _disposed;

        private ScratchWindow(string className, WndProc proc, nint module, nint handle)
        {
            _className = className;
            _proc = proc;
            _module = module;
            Handle = handle;
        }

        internal nint Handle { get; private set; }

        internal char LastCharacter { get; private set; }

        internal int KeyDownCount(ushort virtualKey) => _keyDowns.TryGetValue(virtualKey, out var n) ? n : 0;

        /// <param name="activatable">
        /// Whether the operating system may make this window the FOREGROUND. <b>False for the
        /// parked rig, and that is a defect fix.</b>
        ///
        /// <para><c>SetFocus</c> ACTIVATES the top-level window it focuses (this packet's record §4,
        /// M-m/M-p), so the parked rig — whose whole purpose is to hold a THREAD-LOCAL focus while
        /// something else owns the foreground — became the foreground itself as soon as this
        /// process had the rights to do it. On the FIRST invocation in a process it has no such
        /// rights and the activation is refused, which is why the trap reproduced; on the SECOND,
        /// after the catcher has already taken the foreground once, it succeeds — and when the
        /// catcher then takes the foreground back, the parked thread is DEACTIVATED and the system
        /// clears its focus. The clause
        /// <c>TheThreadLocalFocusRead_ClaimsAWindowThatReceivesNothing…</c> asserts then fails, for
        /// a reason that has nothing to do with the trap it is measuring. Measured, not supposed:
        /// two consecutive calls returned <c>threadLocal=True</c> then <c>threadLocal=False</c>.</para>
        ///
        /// <para><c>WS_EX_NOACTIVATE</c> is the OS's own answer, and it is the same flag this
        /// port's video surface carries for the same reason (D122): the window is on screen and in
        /// the z-order, and the foreground is never taken from anybody.</para>
        /// </param>
        internal static ScratchWindow Create(
            string tag, int x, int y, int width, int height, bool show, bool activatable = true)
        {
            var className = tag + "." + Guid.NewGuid().ToString("N");
            ScratchWindow? instance = null;
            WndProc proc = (window, message, wParam, lParam) =>
            {
                if (instance is not null)
                {
                    if (message == WmKeydown)
                    {
                        var key = (ushort)(wParam & 0xFFFF);
                        instance._keyDowns[key] = instance.KeyDownCount(key) + 1;
                    }
                    else if (message == WmChar)
                    {
                        instance.LastCharacter = (char)(wParam & 0xFFFF);
                    }
                }

                return DefWindowProcW(window, message, wParam, lParam);
            };

            var module = GetModuleHandleW(null);
            var cls = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = module,
                lpszClassName = className,
                // COLOR_WINDOW + 1: the rig is briefly visible on purpose, because an invisible
                // window is not hit-tested and cannot take the foreground.
                hbrBackground = 6,
            };

            var handle = RegisterClassExW(ref cls) == 0
                ? 0
                : CreateWindowExW(
                    activatable ? WsExToolwindow : WsExToolwindow | WsExNoactivate,
                    className, "ccp input probe", WsPopup, x, y, width, height, 0, 0, module, 0);

            instance = new ScratchWindow(className, proc, module, handle);
            if (handle != 0 && show)
            {
                SetWindowPos(handle, HwndTopmost, x, y, width, height, SwpShowwindow);
            }

            return instance;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!WindowsHost)
            {
                return;
            }

            if (Handle != 0)
            {
                DestroyWindow(Handle);
                Handle = 0;
            }

            UnregisterClassW(_className, _module);
            GC.KeepAlive(_proc);
        }
    }

    /// <summary>
    /// A window created, focused and pumped on a SECOND thread — the rig that reproduces the
    /// thread-local-focus trap deterministically. Its own thread calls <c>SetFocus</c> on it (which
    /// only works from the owning thread), so that thread's input queue considers it focused for as
    /// long as it lives, whatever the rest of the desktop is doing.
    /// </summary>
    internal sealed class ParkedWindow : IDisposable
    {
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private ScratchWindow? _window;
        private uint _threadId;
        private bool _disposed;

        private ParkedWindow(Thread thread) => _thread = thread;

        internal nint Handle => _window?.Handle ?? 0;

        internal int KeyDownCount => _window?.KeyDownCount(VkF13) ?? 0;

        internal static ParkedWindow Start(int x, int y, int width, int height)
        {
            ParkedWindow? parked = null;
            var thread = new Thread(() =>
            {
                var self = parked!;
                self._threadId = GetCurrentThreadId();
                self._window = ScratchWindow.Create(
                    "CcpInputProbeParked", x, y, width, height, show: true, activatable: false);
                if (self._window.Handle != 0)
                {
                    SetFocus(self._window.Handle);
                }

                self._ready.Set();

                // A blocking queue wait, not a wall-clock wait: it returns when a message arrives
                // and ends when Dispose posts WM_QUIT to this thread.
                while (GetMessageW(out var msg, 0, 0, 0))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            })
            {
                IsBackground = true,
                Name = "ccp-input-probe-parked",
            };

            parked = new ParkedWindow(thread);
            thread.Start();
            parked._ready.Wait();
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

            _thread.Join();
            _window?.Dispose();
            _ready.Dispose();
        }
    }

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
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
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
    private struct GuiThreadInfo
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public Rect rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mi;
        [FieldOffset(0)] public KeybdInput ki;
        [FieldOffset(0)] public HardwareInput hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW cls);

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
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint parent);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern nint SetActiveWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder buffer, int max);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attaching);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessageW(out Msg msg, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMessageW(out Msg msg, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
