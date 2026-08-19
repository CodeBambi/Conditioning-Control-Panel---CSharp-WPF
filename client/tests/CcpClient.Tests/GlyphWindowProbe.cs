using System.Runtime.InteropServices;
using System.Text;

namespace CcpClient.Tests;

/// <summary>
/// SP-115's independent instrument, in the shape <see cref="OverlayWindowProbe"/> and
/// <see cref="TrayShellProbe"/> established: a second, complete copy of every P/Invoke the product
/// uses, so a fact about the glyph surface is never measured through the same declarations the
/// surface measures itself through. A suite that shared them would be one edit away from certifying
/// nothing.
///
/// <para><b>The negative control is the point of this file.</b> The product's own ghost check is
/// <c>PrintWindow(PW_RENDERFULLCONTENT)</c>, and that check is only worth anything if a window that
/// composites NOTHING answers it differently. <see cref="RunNegativeControl"/> builds that window
/// for real, on every suite run, and measures both arms. It also builds the two states that make
/// SP-099's recorded hazard concrete — a uniform-alpha window that refuses a per-pixel composite,
/// and the same window after the style toggle that lets one through — so the hazard this packet
/// designs around is re-measured rather than quoted.</para>
/// </summary>
internal static class GlyphWindowProbe
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExNoactivate = 0x08000000;
    private const uint WsExTopmost = 0x00000008;
    private const int GwlExstyle = -20;
    private const uint LwaAlpha = 0x00000002;
    private const uint UlwAlpha = 0x00000002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNomove = 0x0002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private const uint GwHwndnext = 2;
    private const uint PwRenderfullcontent = 2;
    private const int SmCmonitors = 80;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;
    private static readonly nint HwndTopmost = -1;

    /// <summary>Bounded raise-and-ask, never a wall-clock wait. Same ceiling and same reason as the
    /// overlay probe's.</summary>
    private const int MaxRaiseAttempts = 32;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>Does this session have an interactive desktop with a display on it? Established by
    /// the test, never taken from the product.</summary>
    internal static bool MachineHasInteractiveDesktop =>
        WindowsHost && GetSystemMetrics(SmCmonitors) >= 1 && GetDesktopWindow() != 0;

    internal static (int Width, int Height) PrimarySize =>
        WindowsHost ? (GetSystemMetrics(SmCxscreen), GetSystemMetrics(SmCyscreen)) : (0, 0);

    internal static bool WindowExists(nint window) => WindowsHost && window != 0 && IsWindow(window);

    internal static bool WindowIsVisible(nint window) => WindowsHost && window != 0 && IsWindowVisible(window);

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
    /// The uniform layered alpha, or -1 when the OS holds none.
    ///
    /// <para><b>-1 is the EXPECTED answer for a correctly composited glyph surface</b>, and that is
    /// the whole difficulty this packet was written around: it is also the answer for a layered
    /// window that composites nothing. This probe exposes it so a fact can assert the -1 rather than
    /// leave it implied, and so the overlay's very different expectation (a real number) can be
    /// asserted side by side in the coexistence run.</para>
    /// </summary>
    internal static int LayeredAlphaOf(nint window)
    {
        if (!WindowsHost || window == 0 || !GetLayeredWindowAttributes(window, out _, out var alpha, out _))
        {
            return -1;
        }

        return alpha;
    }

    internal static bool IsForeground(nint window) => WindowsHost && window != 0 && GetForegroundWindow() == window;

    /// <summary>
    /// Writes the extended style through THIS probe's own declaration, so the input differential
    /// can flip click-through without asking the capability under test to certify itself.
    ///
    /// <para><b>It is a style write and it is deliberately not a layered-style TOGGLE.</b> SP-099's
    /// hazard is clearing <c>WS_EX_LAYERED</c> and then compositing; every caller of this method
    /// passes a value that keeps the layered bit exactly as it found it, and
    /// <see cref="RunNegativeControl"/> is the one place in the suite that performs the toggle at
    /// all — on a scratch window it owns, in order to MEASURE the hazard rather than suffer it.</para>
    /// </summary>
    internal static void SetExStyle(nint window, uint style)
    {
        if (WindowsHost && window != 0)
        {
            SetWindowLongPtrW(window, GwlExstyle, (nint)style);
        }
    }

    /// <summary>
    /// What the operating system holds for a window, rendered into a bitmap this probe owns, as
    /// <c>COLORREF</c> values. Empty when the OS refused — a distinguishable answer on purpose.
    /// </summary>
    internal static uint[] ReadSurface(nint window, int width, int height)
    {
        if (!WindowsHost || window == 0 || width <= 0 || height <= 0)
        {
            return [];
        }

        var surface = CreateSurface(width, height);
        if (surface.Dc == 0)
        {
            return [];
        }

        try
        {
            return PrintWindow(window, surface.Dc, PwRenderfullcontent)
                ? ReadPixels(surface.Bits, width, height)
                : [];
        }
        finally
        {
            ReleaseSurface(surface);
        }
    }

    /// <summary>How many of the pixels are not zero. The single number that separates a composited
    /// surface from a ghost.</summary>
    internal static int NonZero(uint[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var count = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    internal static nint HitTest(int x, int y) =>
        WindowsHost ? WindowFromPoint(new Point { X = x, Y = y }) : 0;

    /// <summary>Bounded raise-and-ask. Raising removes contention; it cannot manufacture an answer,
    /// because <c>WindowFromPoint</c> is still the only thing that produces one.</summary>
    internal static nint HitTestExpecting(int x, int y, nint window, bool expectSurface, out int attempts)
    {
        var point = new Point { X = x, Y = y };
        var winner = (nint)0;
        for (attempts = 1; attempts <= MaxRaiseAttempts; attempts++)
        {
            RaiseTopmost(window);
            winner = WindowFromPoint(point);
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

        var buffer = new StringBuilder(256);
        return GetClassNameW(window, buffer, buffer.Capacity) > 0
            ? $"0x{window:X} (class \"{buffer}\")"
            : $"0x{window:X}";
    }

    /// <param name="Index">Where the window sits among visible top-level windows, or -1.</param>
    /// <param name="FirstOrdinaryIndex">Where the first non-topmost visible window sits, or -1.</param>
    /// <param name="VisibleCount">How many visible top-level windows the walk saw.</param>
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
            else if (firstOrdinary < 0 && (GetWindowLongPtrW(candidate, GwlExstyle) & WsExTopmost) == 0)
            {
                firstOrdinary = visible;
            }

            visible++;
        }

        return new ZOrderReading(index, firstOrdinary, visible);
    }

    /// <summary>
    /// <b>THE OCCLUSION ARBITRATION.</b> Who owns a rectangle, decided from the operating system's
    /// own z-order and the intervening windows' own rectangles, rather than assumed from
    /// disjointness.
    ///
    /// <para>SP-113's review recorded that the four-disjoint-rectangles argument does not scale past
    /// four surfaces. It does not scale to this capability AT ALL, because proving that a
    /// transparent pixel shows the background behind it requires the surface to be placed OVER a
    /// known background: the overlap is the evidence. So ownership is measured. This walks the
    /// z-order from the top, finds <paramref name="above"/> and <paramref name="below"/>, and
    /// returns every visible window strictly between them whose own rectangle intersects
    /// <paramref name="area"/> — each named by class and rectangle.</para>
    ///
    /// <para>Both arms were observed while this packet was written: with the pair raised back to
    /// back the answer is empty, and with an ordinary interval between the raises the shipping WPF
    /// product sat between them and the sampled "background" pixels were its own.</para>
    /// </summary>
    /// <returns>An empty list when the pair really is adjacent over that area; otherwise the
    /// intruders, so a failure can name them instead of reporting a wrong colour.</returns>
    internal static IReadOnlyList<string> Intruders(
        nint above, nint below, int x, int y, int width, int height)
    {
        if (!WindowsHost || above == 0 || below == 0)
        {
            return ["the probe cannot walk a z-order on this host"];
        }

        var found = new List<string>();
        var seenAbove = false;
        var seenBelow = false;

        for (var candidate = GetTopWindow(0); candidate != 0; candidate = GetWindow(candidate, GwHwndnext))
        {
            if (!IsWindowVisible(candidate))
            {
                continue;
            }

            if (candidate == above)
            {
                seenAbove = true;
                continue;
            }

            if (candidate == below)
            {
                seenBelow = true;
                break;
            }

            if (!seenAbove || !GetWindowRect(candidate, out var rect))
            {
                continue;
            }

            if (rect.Left < x + width && rect.Right > x && rect.Top < y + height && rect.Bottom > y)
            {
                var buffer = new StringBuilder(128);
                GetClassNameW(candidate, buffer, buffer.Capacity);
                found.Add($"0x{candidate:X} (class \"{buffer}\") at {rect.Left},{rect.Top} "
                    + $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}");
            }
        }

        if (!seenAbove)
        {
            return [$"the upper window {DescribeWindow(above)} is not in the visible z-order at all"];
        }

        if (!seenBelow)
        {
            return [$"the lower window {DescribeWindow(below)} is not below the upper one in the visible z-order"];
        }

        return found;
    }

    /// <param name="MachineHasInteractiveDesktop">Whether this session has a desktop and a display.</param>
    /// <param name="GhostIsVisible">A layered window that was NEVER composited: does the OS call it visible?</param>
    /// <param name="GhostHoldsUniformAlpha">Does it report uniform layered attributes? (It must not.)</param>
    /// <param name="GhostNonZeroPixels">How many non-zero pixels its surface read-back returns. THE control.</param>
    /// <param name="GhostSampledPixels">How many were sampled, so a zero cannot be an empty read.</param>
    /// <param name="CompositedNonZeroPixels">The same window after ONE per-pixel composite.</param>
    /// <param name="CompositedInkMatches">Of the opaque ink points, how many read back exactly their colour.</param>
    /// <param name="CompositedInkPoints">How many were checked.</param>
    /// <param name="UniformModeRefusesPerPixel">
    /// A window given uniform layered attributes: does <c>UpdateLayeredWindow</c> refuse it?
    /// </param>
    /// <param name="UniformModeRefusalError">The Win32 last-error from that refusal (87 = the
    /// documented invalid-parameter answer).</param>
    /// <param name="UniformAlphaSurvivedTheRefusal">Does the uniform window still hold its alpha afterwards?</param>
    /// <param name="StyleToggleClearsUniformAlpha">SP-099's first line: does clearing WS_EX_LAYERED wipe it?</param>
    /// <param name="ToggleThenPerPixelSucceeds">SP-099's second line: does the composite then go through?</param>
    /// <param name="UniformAlphaAfterToggle">And is the uniform read-back gone afterwards? (-1 = gone.)</param>
    /// <param name="ScratchWindowsGoneAfterTeardown">Every scratch window destroyed.</param>
    internal readonly record struct NegativeControl(
        bool MachineHasInteractiveDesktop,
        bool GhostIsVisible,
        bool GhostHoldsUniformAlpha,
        int GhostNonZeroPixels,
        int GhostSampledPixels,
        int CompositedNonZeroPixels,
        int CompositedInkMatches,
        int CompositedInkPoints,
        bool UniformModeRefusesPerPixel,
        int UniformModeRefusalError,
        int UniformAlphaSurvivedTheRefusal,
        bool StyleToggleClearsUniformAlpha,
        bool ToggleThenPerPixelSucceeds,
        int UniformAlphaAfterToggle,
        bool ScratchWindowsGoneAfterTeardown);

    /// <summary>
    /// Builds the states this capability's claims depend on and measures every one, on every suite
    /// run. Nothing here uses the product.
    /// </summary>
    internal static NegativeControl RunNegativeControl()
    {
        if (!WindowsHost)
        {
            return new NegativeControl(
                MachineHasInteractiveDesktop: false,
                GhostIsVisible: false,
                GhostHoldsUniformAlpha: false,
                GhostNonZeroPixels: 0,
                GhostSampledPixels: 0,
                CompositedNonZeroPixels: 0,
                CompositedInkMatches: 0,
                CompositedInkPoints: 0,
                UniformModeRefusesPerPixel: false,
                UniformModeRefusalError: 0,
                UniformAlphaSurvivedTheRefusal: -1,
                StyleToggleClearsUniformAlpha: false,
                ToggleThenPerPixelSucceeds: false,
                UniformAlphaAfterToggle: -1,
                ScratchWindowsGoneAfterTeardown: true);
        }

        var (screenWidth, screenHeight) = PrimarySize;
        const int side = 120;
        var x = Math.Max(0, (screenWidth / 2) - 700);
        var y = Math.Max(0, (screenHeight / 2) - 400);

        // (1) THE GHOST: layered, shown, never composited. The exact state the first attempt
        // shipped, and the state this capability's Available must be impossible to reach from.
        var ghost = ScratchWindow.Create("ccp-glyph-ghost", x, y, side, side);
        var ghostVisible = WindowIsVisible(ghost.Handle);
        var ghostUniform = LayeredAlphaOf(ghost.Handle) >= 0;
        var ghostPixels = ReadSurface(ghost.Handle, side, side);
        var ghostNonZero = NonZero(ghostPixels);

        // (2) The SAME window after one per-pixel composite. Same handle, same call, one difference.
        var ink = new List<(int X, int Y)>();
        var (dc, bits, bitmap) = CreateDib(side, side);
        for (var row = 0; row < side; row++)
        {
            for (var column = 0; column < side; column++)
            {
                var offset = ((row * side) + column) * 4;
                var opaque = column >= side / 2;
                Marshal.WriteByte(bits, offset, opaque ? (byte)0xC0 : (byte)0);
                Marshal.WriteByte(bits, offset + 1, opaque ? (byte)0x20 : (byte)0);
                Marshal.WriteByte(bits, offset + 2, opaque ? (byte)0x90 : (byte)0);
                Marshal.WriteByte(bits, offset + 3, opaque ? (byte)0xFF : (byte)0);
                if (opaque && row % 20 == 0 && column % 20 == 0)
                {
                    ink.Add((column, row));
                }
            }
        }

        Composite(ghost.Handle, x, y, side, side, dc, 255);
        var compositedPixels = ReadSurface(ghost.Handle, side, side);
        var compositedNonZero = NonZero(compositedPixels);
        var inkMatches = 0;
        if (compositedPixels.Length == side * side)
        {
            foreach (var (inkX, inkY) in ink)
            {
                if (compositedPixels[(inkY * side) + inkX] == 0xC02090)
                {
                    inkMatches++;
                }
            }
        }

        // (3) SP-099's HAZARD, re-measured. A uniform-alpha window refuses a per-pixel composite;
        // clearing WS_EX_LAYERED wipes the uniform attributes; restoring it lets the composite
        // through and the uniform read-back is gone for good.
        var uniform = ScratchWindow.Create("ccp-glyph-uniform", x + 200, y, side, side);
        SetLayeredWindowAttributes(uniform.Handle, 0, 153, LwaAlpha);
        SetWindowPos(uniform.Handle, HwndTopmost, x + 200, y, side, side, SwpNoactivate | SwpShowwindow);
        var refused = !Composite(uniform.Handle, x + 200, y, side, side, dc, 255);
        var refusalError = Marshal.GetLastWin32Error();
        var uniformSurvived = LayeredAlphaOf(uniform.Handle);

        var style = (uint)GetWindowLongPtrW(uniform.Handle, GwlExstyle);
        SetWindowLongPtrW(uniform.Handle, GwlExstyle, (nint)(style & ~WsExLayered));
        var toggleCleared = LayeredAlphaOf(uniform.Handle) < 0;
        SetWindowLongPtrW(uniform.Handle, GwlExstyle, (nint)style);
        var toggleThenComposite = Composite(uniform.Handle, x + 200, y, side, side, dc, 255);
        var uniformAfterToggle = LayeredAlphaOf(uniform.Handle);

        var ghostHandle = ghost.Handle;
        var uniformHandle = uniform.Handle;
        ghost.Dispose();
        uniform.Dispose();
        DeleteDC(dc);
        DeleteObject(bitmap);

        return new NegativeControl(
            MachineHasInteractiveDesktop: MachineHasInteractiveDesktop,
            GhostIsVisible: ghostVisible,
            GhostHoldsUniformAlpha: ghostUniform,
            GhostNonZeroPixels: ghostNonZero,
            GhostSampledPixels: ghostPixels.Length,
            CompositedNonZeroPixels: compositedNonZero,
            CompositedInkMatches: inkMatches,
            CompositedInkPoints: ink.Count,
            UniformModeRefusesPerPixel: refused,
            UniformModeRefusalError: refusalError,
            UniformAlphaSurvivedTheRefusal: uniformSurvived,
            StyleToggleClearsUniformAlpha: toggleCleared,
            ToggleThenPerPixelSucceeds: toggleThenComposite,
            UniformAlphaAfterToggle: uniformAfterToggle,
            ScratchWindowsGoneAfterTeardown: !IsWindow(ghostHandle) && !IsWindow(uniformHandle));
    }

    /// <summary>One per-pixel composite through this probe's OWN declaration.</summary>
    private static bool Composite(nint window, int x, int y, int width, int height, nint sourceDc, byte constant)
    {
        var destination = new Point { X = x, Y = y };
        var size = new Size { Cx = width, Cy = height };
        var source = new Point { X = 0, Y = 0 };
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = constant,
            AlphaFormat = 1,
        };
        return UpdateLayeredWindow(window, 0, ref destination, ref size, sourceDc, ref source, 0, ref blend, UlwAlpha);
    }

    /// <summary>A throwaway layered top-level window this probe owns. Created layered and shown,
    /// and NOT composited — the ghost is the default state.</summary>
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

        internal static ScratchWindow Create(string tag, int x, int y, int width, int height)
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
            };

            var handle = RegisterClassExW(ref cls) == 0
                ? 0
                : CreateWindowExW(
                    WsExLayered | WsExTransparent | WsExToolwindow | WsExNoactivate,
                    className, "ccp glyph probe", WsPopup, x, y, width, height, 0, 0, module, 0);

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

    private static uint[] ReadPixels(nint bits, int width, int height)
    {
        var pixels = new uint[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var offset = i * 4;
            pixels[i] = (uint)((Marshal.ReadByte(bits, offset) << 16)
                | (Marshal.ReadByte(bits, offset + 1) << 8)
                | Marshal.ReadByte(bits, offset + 2));
        }

        return pixels;
    }

    private static (nint Dc, nint Bits, nint Bitmap) CreateDib(int width, int height)
    {
        var screen = GetDC(0);
        if (screen == 0)
        {
            return (0, 0, 0);
        }

        try
        {
            var info = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                },
            };

            var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
            var dc = CreateCompatibleDC(screen);
            if (dc != 0 && bitmap != 0)
            {
                SelectObject(dc, bitmap);
            }

            return (dc, bits, bitmap);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    private static Surface CreateSurface(int width, int height)
    {
        var (dc, bits, bitmap) = CreateDib(width, height);
        return new Surface { Dc = dc, Bits = bits, Bitmap = bitmap };
    }

    private static void ReleaseSurface(Surface surface)
    {
        if (surface.Dc != 0)
        {
            DeleteDC(surface.Dc);
        }

        if (surface.Bitmap != 0)
        {
            DeleteObject(surface.Bitmap);
        }
    }

    private struct Surface
    {
        public nint Dc;
        public nint Bits;
        public nint Bitmap;
    }

    private delegate nint WndProc(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] bmiColors;
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

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public nint hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassExW windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        nint window, nint destinationDc, ref Point destinationPoint, ref Size size,
        nint sourceDc, ref Point sourcePoint, uint colourKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder buffer, int capacity);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint deviceContext, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint deviceContext, nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);
}
