using System.Runtime.InteropServices;

namespace CcpClient.Tests;

/// <summary>
/// The independent effect instrument: it asks the WINDOW MANAGER about a surface without
/// asking the product.
///
/// <para><b>Why it re-declares every P/Invoke instead of using the product's interop.</b> This is
/// the packet's named trap. An overlay is the easiest capability in this codebase to fake, because
/// every one of its failure modes is invisible from inside the process: a window that is present
/// and invisible, present and buried, present and swallowing every click all look identical to the
/// code that created them. A test that measured any of that through the same declarations the
/// product uses could be fooled by one edit to those declarations. These are deliberately a second,
/// independent copy, so "the backend says the surface is click-through" and "the window manager
/// routes the point elsewhere" are two facts produced by two code paths.</para>
///
/// <para><b>Why <c>WindowFromPoint</c> is a valid input oracle.</b> It is the window manager's
/// own hit-test walk over the real z-ordered stack of real windows, and it honours
/// <c>WS_EX_TRANSPARENT</c>. Measured on this machine (Windows 11) before any of this was written:
/// an opaque topmost scratch window wins the point; a <c>WS_EX_TRANSPARENT</c> scratch window
/// raised ABOVE it does not, and the opaque one still wins; clearing the flag hands the point back
/// to the upper window. <see cref="RunNegativeControl"/> re-runs that differential on this probe's
/// own scratch windows on every suite run, so an oracle that degenerated into "always answer the
/// top window" fails the suite instead of silently certifying everything.</para>
///
/// <para><b>What it is NOT.</b> Delivered input. Proving that a physical mouse click passes through
/// needs <c>SendInput</c> — which moves the user's cursor mid-suite and fails silently on a locked
/// workstation — or a human hand. That is a headed gate and this probe does not pretend to it. It
/// is also not a rendering oracle: nothing here reads a pixel.</para>
///
/// <para>Every native path is guarded by a platform check HERE, in the helper, so the fact bodies
/// stay free of predicates and none of their assertions can be silenced.</para>
/// </summary>
internal static class OverlayWindowProbe
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExNoactivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const int GwlExstyle = -20;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private const uint GwHwndnext = 2;
    private const int SmCmonitors = 80;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private static readonly nint HwndTopmost = -1;

    /// <summary>
    /// Hard ceiling on the raise-and-ask loop. The topmost band is CONTESTED on this machine — the
    /// shipping WPF product re-asserts <c>HWND_TOPMOST</c> on its own windows on a cadence
    /// (<c>Services/Flash/FlashService.cs:206-243</c>) and was empirically the window that won the
    /// point while this probe was being written. Bounded iteration absorbs that without a wall-clock
    /// wait, which this suite forbids outright. Raising is an ENVIRONMENT action taken with this
    /// probe's own P/Invoke; it removes contention and cannot manufacture an answer, because
    /// <see cref="WindowFromPoint"/> is still the only thing that produces one.
    /// </summary>
    private const int MaxRaiseAttempts = 32;

    /// <summary>The alpha the control's visible scratch window is given, to be read back.</summary>
    internal const byte ControlAlpha = 160;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>
    /// Does this session have an interactive desktop with a display on it? Without one there is
    /// nowhere for a surface to go and the honest expectation flips. A property of the machine,
    /// established by the test rather than taken from the product.
    /// </summary>
    internal static bool MachineHasInteractiveDesktop =>
        WindowsHost && GetSystemMetrics(SmCmonitors) >= 1 && GetDesktopWindow() != 0;

    /// <summary>The OS's own display count, for cross-checking the product's enumeration.</summary>
    internal static int MonitorCount => WindowsHost ? GetSystemMetrics(SmCmonitors) : 0;

    /// <summary>The OS's own primary display size, same purpose.</summary>
    internal static (int Width, int Height) PrimarySize =>
        WindowsHost ? (GetSystemMetrics(SmCxscreen), GetSystemMetrics(SmCyscreen)) : (0, 0);

    internal static bool WindowExists(nint window) => WindowsHost && window != 0 && IsWindow(window);

    internal static bool WindowIsVisible(nint window) => WindowsHost && window != 0 && IsWindowVisible(window);

    /// <summary>The rectangle the OS holds, or all -1 when it will not say.</summary>
    internal static (int X, int Y, int Width, int Height) RectOf(nint window)
    {
        if (!WindowsHost || window == 0 || !GetWindowRect(window, out var rect))
        {
            return (-1, -1, -1, -1);
        }

        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    internal static uint ExStyleOf(nint window) =>
        WindowsHost && window != 0 ? (uint)GetWindowLongPtrW(window, GwlExstyle) : 0;

    /// <summary>
    /// The alpha the OS holds for a layered window, or <b>-1 when it holds none at all</b>.
    ///
    /// <para>The -1 is the whole point. A window with <c>WS_EX_LAYERED</c> whose attributes were
    /// never set reports <c>IsWindowVisible = TRUE</c> and composites nothing — the exact state
    /// <c>CCP.Avalonia.Desktop.Windows/WindowsOverlaySurface.cs:26-45</c> puts a window into. This
    /// is the only instrument in the suite that can tell that window from a real one.</para>
    /// </summary>
    internal static int LayeredAlphaOf(nint window)
    {
        if (!WindowsHost || window == 0 || !GetLayeredWindowAttributes(window, out _, out var alpha, out var flags))
        {
            return -1;
        }

        return (flags & LwaAlpha) != 0 ? alpha : -1;
    }

    internal static bool IsForeground(nint window) =>
        WindowsHost && window != 0 && GetForegroundWindow() == window;

    /// <summary>Where the OS itself puts a window among the visible top-level windows.</summary>
    internal readonly record struct ZOrderReading(int Index, int FirstOrdinaryIndex, int VisibleCount)
    {
        /// <summary>
        /// The ORDERING fact, not the <c>WS_EX_TOPMOST</c> flag: the window appears before the
        /// first visible window that is not in the topmost band. Deliberately does not claim
        /// "above every other window" — other topmost windows exist and contest, which is why WPF
        /// re-raises its own (<c>FlashService.cs:206-243</c>).
        /// </summary>
        internal bool AboveEveryOrdinaryWindow =>
            Index >= 0 && FirstOrdinaryIndex >= 0 && Index < FirstOrdinaryIndex;
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

    /// <summary>One hit-test question, with no manipulation of anything.</summary>
    internal static nint HitTest(int x, int y) =>
        WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

    /// <summary>
    /// Raise <paramref name="window"/> into the top of the topmost band and ask the hit test, up
    /// to <see cref="MaxRaiseAttempts"/> times, stopping as soon as the answer matches
    /// <paramref name="expectSurface"/>. Returns the last answer the window manager gave.
    ///
    /// <para>Raising cannot manufacture the answer: with <paramref name="expectSurface"/> false,
    /// raising the window can only make it MORE likely to win the point, so an answer of "not this
    /// window" after a raise is stronger evidence, not weaker. And a loop that always returned what
    /// it wanted would return the SAME window in both polarities, which is exactly what the
    /// differential in <see cref="RunNegativeControl"/> and in the facts would then fail on.</para>
    /// </summary>
    internal static nint HitTestExpecting(int x, int y, nint window, bool expectSurface, out int attempts)
    {
        attempts = 0;
        if (!WindowsHost || window == 0)
        {
            return 0;
        }

        var winner = (nint)0;
        for (attempts = 1; attempts <= MaxRaiseAttempts; attempts++)
        {
            RaiseTopmost(window);
            winner = WindowFromPoint(new Point { X = x, Y = y });
            if (expectSurface ? winner == window : winner != window)
            {
                return winner;
            }
        }

        attempts = MaxRaiseAttempts;
        return winner;
    }

    internal static void RaiseTopmost(nint window)
    {
        if (WindowsHost && window != 0)
        {
            SetWindowPos(window, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }
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

    // ---------- the instrument's own self-check ----------

    /// <param name="MachineHasInteractiveDesktop">Whether this session has a desktop and a display.</param>
    /// <param name="OpaqueWindowCreated">Whether the scratch catcher came up.</param>
    /// <param name="TransparentWindowCreated">Whether the scratch click-through window came up.</param>
    /// <param name="OpaqueWindowWinsItsOwnPoint">The catcher, raised topmost, must win its own centre.</param>
    /// <param name="TransparentWindowAboveIsSkipped">
    /// With a WS_EX_TRANSPARENT window raised ABOVE the catcher and covering the same point, the
    /// hit test must still answer the catcher. If this is false the oracle cannot detect
    /// click-through at all and every input fact in the suite is worthless.
    /// </param>
    /// <param name="ClearingTheFlagHandsThePointBack">
    /// Clearing WS_EX_TRANSPARENT on the upper window must hand the point to it. The other half of
    /// the differential: without it, "the point went elsewhere" could just mean the upper window
    /// was never over the point.
    /// </param>
    /// <param name="AlphaOfWindowWithAttributesSet">Must equal <see cref="ControlAlpha"/>.</param>
    /// <param name="AlphaOfLayeredWindowWithNoAttributes">
    /// Must be -1. The ghost detector's negative control: a WS_EX_LAYERED window whose attributes
    /// were never set.
    /// </param>
    /// <param name="GhostWindowStillReportsVisible">
    /// Must be TRUE, and this is the measurement that makes the alpha check load-bearing: the OS
    /// happily calls that ghost visible.
    /// </param>
    /// <param name="ScratchWindowsExistAfterCleanup">Must be false, or the existence check cannot say no.</param>
    internal sealed record NegativeControl(
        bool MachineHasInteractiveDesktop,
        bool OpaqueWindowCreated,
        bool TransparentWindowCreated,
        bool OpaqueWindowWinsItsOwnPoint,
        bool TransparentWindowAboveIsSkipped,
        bool ClearingTheFlagHandsThePointBack,
        int AlphaOfWindowWithAttributesSet,
        int AlphaOfLayeredWindowWithNoAttributes,
        bool GhostWindowStillReportsVisible,
        bool ScratchWindowsExistAfterCleanup);

    /// <summary>
    /// Builds a three-window rig out of this probe's own declarations and measures the two things
    /// every overlay fact in the suite depends on: that the hit test can distinguish a
    /// click-through window from an opaque one, and that the alpha reader can say "the OS holds
    /// none".
    /// </summary>
    internal static NegativeControl RunNegativeControl()
    {
        if (!WindowsHost)
        {
            return new NegativeControl(false, false, false, false, false, false, -1, -1, false, false);
        }

        var (screenWidth, screenHeight) = PrimarySize;
        const int width = 220;
        const int height = 160;

        // Left of centre, so the control's rig never shares a point with the surface the
        // observations place at the centre.
        var x = Math.Max(0, ((screenWidth - width) / 2) - 260);
        var y = Math.Max(0, (screenHeight - height) / 2);
        var centreX = x + (width / 2);
        var centreY = y + (height / 2);

        using var catcher = ScratchWindow.Create("CcpOverlayProbeCatcher", WsExToolwindow | WsExNoactivate, x, y, width, height, alpha: null);
        using var above = ScratchWindow.Create("CcpOverlayProbeAbove", WsExLayered | WsExTransparent | WsExToolwindow | WsExNoactivate, x, y, width, height, alpha: ControlAlpha);
        using var ghost = ScratchWindow.Create("CcpOverlayProbeGhost", WsExLayered | WsExToolwindow | WsExNoactivate, x, y + height + 8, width, height, alpha: null);

        // 1. The catcher alone must win its own centre.
        var catcherWins = HitTestExpecting(centreX, centreY, catcher.Handle, expectSurface: true, out _) == catcher.Handle;

        // 2. The click-through window raised ABOVE it must not take the point away.
        var transparentSkipped = false;
        for (var attempt = 0; attempt < MaxRaiseAttempts; attempt++)
        {
            RaiseTopmost(catcher.Handle);
            RaiseTopmost(above.Handle);
            var winner = WindowFromPoint(new Point { X = centreX, Y = centreY });
            transparentSkipped = winner == catcher.Handle;
            if (transparentSkipped)
            {
                break;
            }
        }

        // 3. Clearing the flag on the upper window must hand it the point.
        var upperStyle = (uint)GetWindowLongPtrW(above.Handle, GwlExstyle);
        SetWindowLongPtrW(above.Handle, GwlExstyle, (nint)(upperStyle & ~WsExTransparent));
        var flagClearedWins = HitTestExpecting(centreX, centreY, above.Handle, expectSurface: true, out _) == above.Handle;
        SetWindowLongPtrW(above.Handle, GwlExstyle, (nint)upperStyle);

        var alphaSet = LayeredAlphaOf(above.Handle);
        var alphaGhost = LayeredAlphaOf(ghost.Handle);
        var ghostVisible = WindowIsVisible(ghost.Handle);

        var handles = new[] { catcher.Handle, above.Handle, ghost.Handle };
        catcher.Dispose();
        above.Dispose();
        ghost.Dispose();

        return new NegativeControl(
            MachineHasInteractiveDesktop: MachineHasInteractiveDesktop,
            OpaqueWindowCreated: handles[0] != 0,
            TransparentWindowCreated: handles[1] != 0,
            OpaqueWindowWinsItsOwnPoint: catcherWins,
            TransparentWindowAboveIsSkipped: transparentSkipped,
            ClearingTheFlagHandsThePointBack: flagClearedWins,
            AlphaOfWindowWithAttributesSet: alphaSet,
            AlphaOfLayeredWindowWithNoAttributes: alphaGhost,
            GhostWindowStillReportsVisible: ghostVisible,
            ScratchWindowsExistAfterCleanup: handles.Any(WindowExists));
    }

    /// <summary>A throwaway top-level window this probe owns, for oracle self-checks.</summary>
    internal sealed class ScratchWindow : IDisposable
    {
        private readonly string _className;
        private readonly WndProc _proc;
        private readonly nint _module;
        private bool _disposed;

        private ScratchWindow(string className, WndProc proc, nint module, nint handle)
        {
            _className = className;
            _proc = proc;
            _module = module;
            Handle = handle;
        }

        internal nint Handle { get; private set; }

        internal static ScratchWindow Create(string tag, uint exStyle, int x, int y, int width, int height, byte? alpha)
        {
            var className = tag + "." + Guid.NewGuid().ToString("N");
            WndProc proc = DefWindowProcW;
            var module = GetModuleHandleW(null);
            var cls = new WndClassExW
            {
                cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
                hInstance = module,
                lpszClassName = className,
                // COLOR_WINDOW + 1: the scratch rig is briefly visible on purpose, because an
                // invisible window is not hit-tested and the control would prove nothing.
                hbrBackground = 6,
            };

            var handle = RegisterClassExW(ref cls) == 0
                ? 0
                : CreateWindowExW(exStyle, className, "ccp overlay probe", WsPopup, x, y, width, height, 0, 0, module, 0);

            if (handle != 0 && alpha is { } value)
            {
                SetLayeredWindowAttributes(handle, 0, value, LwaAlpha);
            }

            if (handle != 0)
            {
                SetWindowPos(handle, HwndTopmost, x, y, width, height, SwpNoactivate | SwpShowwindow);
            }

            return new ScratchWindow(className, proc, module, handle);
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

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint colorKey, out byte alpha, out uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint parent);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassNameW(nint window, System.Text.StringBuilder buffer, int max);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);
}
