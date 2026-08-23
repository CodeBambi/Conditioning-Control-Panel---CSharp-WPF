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
    private const int GwlExstyle = -20;
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
    private const uint PmRemove = 0x0001;

    private const uint InputMouse = 0;
    private const uint MouseeventfMove = 0x0001;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const uint MouseeventfAbsolute = 0x8000;
    private const uint MouseeventfVirtualdesk = 0x4000;

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

        // THE CLICK IS WHAT ARMS THE REVOCATION, so the floor goes up here and nowhere else.
        // RealDesktopWindowFloor carries the measurement; the short version is that a thread which
        // reaches ZERO top-level windows after one of them was clicked costs the WHOLE PROCESS the
        // top-most band, permanently and silently. This call is idempotent and per-thread, and it
        // sits at the INJECTION rather than at any one caller, so every present and future clicked
        // window on whatever thread does the clicking is covered by construction.
        RealDesktopWindowFloor.Ensure();

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

        /// <summary>Create and show a non-activating, topmost scratch target at a rectangle.</summary>
        internal static ScratchTarget? Create(int x, int y, int width, int height, bool clickThrough = false)
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
                        if (built is not null)
                        {
                            Interlocked.Increment(ref built._activationsRefused);
                        }

                        return 3;   // MA_NOACTIVATE

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

            var exStyle = WsExNoactivate | WsExToolwindow | (clickThrough ? WsExTransparent : 0);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg msg, nint window, uint filterMin, uint filterMax, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DispatchMessageW(ref Msg msg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint SetPixel(nint dc, int x, int y, uint colour);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
