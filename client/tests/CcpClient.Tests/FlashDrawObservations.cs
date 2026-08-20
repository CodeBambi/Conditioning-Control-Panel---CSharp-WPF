using System.Runtime.InteropServices;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Overlay;

namespace CcpClient.Tests;

/// <summary>
/// One real draw, measured. The product presents a surface and paints a known frame; the operating
/// system is then asked what that window renders as, and what the COMPOSITED DESKTOP contains where
/// the surface is — both through <see cref="FlashPixelProbe"/>, which shares no declaration with
/// the product's own interop.
///
/// <para><b>The frame is two-tone on purpose.</b> A solid frame cannot tell a correct paint from a
/// vertically flipped one, and a flipped DIB is the classic bug of this mechanism (a top-down
/// section is a NEGATIVE <c>biHeight</c>, and getting it wrong is silent). Top half and bottom half
/// carry different colours, and both read-backs check both halves.</para>
///
/// <para><b>Why there is a control.</b> "The desktop capture does not contain the flash" is also
/// what a locked workstation, an RDP session and a machine with no compositor produce. So the probe
/// first builds its OWN painted layered window with its own P/Invokes and asks whether a desktop
/// capture can see THAT. Every composited-pixel expectation is then compared against that measured
/// machine property rather than against a guess, which is the same discipline SP-099's hit-test and
/// alpha oracles already carry.</para>
///
/// <para>The whole thing runs ONCE per suite execution and is cached: it puts real windows on the
/// user's real screen for the duration of a few dozen syscalls.</para>
/// </summary>
internal static class FlashDrawObservations
{
    internal const int SurfaceWidth = 240;
    internal const int SurfaceHeight = 180;

    /// <summary>Top half: COLORREF 0x30C0F0 (a colour nothing on a desktop is by accident).</summary>
    internal const uint TopColour = 0x30C0F0;

    /// <summary>Bottom half: COLORREF 0xF03080.</summary>
    internal const uint BottomColour = 0xF03080;

    /// <summary>The control window's colour, distinct from both halves of the subject frame.</summary>
    internal const uint ControlColour = 0x20E060;

    private static readonly Lazy<Measurements> LazyRun = new(Measure, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Where the evidence bitmaps are written. Named, stable, and documented in
    /// <c>spine-tasks/SP-100-flash-draws/record.md</c> so the orchestrator's headed capture has
    /// something to compare against.</summary>
    internal static string EvidenceFolder => Path.Combine(Path.GetTempPath(), "ccp-sp100-flash-draws");

    internal static Measurements Run => LazyRun.Value;

    /// <summary>The two-tone frame the subject paints.</summary>
    internal static OverlayFrame Frame()
    {
        var pixels = new byte[SurfaceWidth * SurfaceHeight * OverlayFrame.BytesPerPixel];
        for (var y = 0; y < SurfaceHeight; y++)
        {
            var colour = y < SurfaceHeight / 2 ? TopColour : BottomColour;
            for (var x = 0; x < SurfaceWidth; x++)
            {
                var offset = ((y * SurfaceWidth) + x) * OverlayFrame.BytesPerPixel;
                pixels[offset] = (byte)((colour >> 16) & 0xFF);
                pixels[offset + 1] = (byte)((colour >> 8) & 0xFF);
                pixels[offset + 2] = (byte)(colour & 0xFF);
                pixels[offset + 3] = 0xFF;
            }
        }

        return new OverlayFrame(SurfaceWidth, SurfaceHeight, pixels);
    }

    /// <summary>
    /// Where the subject goes: below and right of centre on the primary display, measured by the
    /// PROBE, and deliberately not the rectangle SP-099's own lifecycle uses — the two suites run in
    /// one process and must not fight over the same point.
    /// </summary>
    internal static OverlayBounds SubjectBounds
    {
        get
        {
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            return new OverlayBounds(
                Math.Max(0, (screenWidth / 2) - SurfaceWidth),
                Math.Max(0, (screenHeight / 2) + 40),
                SurfaceWidth,
                SurfaceHeight);
        }
    }

    /// <summary>The control's rectangle: same display, well clear of the subject's.</summary>
    internal static OverlayBounds ControlBounds
    {
        get
        {
            var (screenWidth, screenHeight) = OverlayWindowProbe.PrimarySize;
            return new OverlayBounds(
                Math.Max(0, (screenWidth / 2) + 60),
                Math.Max(0, (screenHeight / 4)),
                SurfaceWidth,
                SurfaceHeight);
        }
    }

    /// <param name="MachineHasInteractiveDesktop">The machine fact every window expectation is compared against.</param>
    /// <param name="DesktopCaptureIsLive">
    /// The machine fact every COMPOSITED expectation is compared against: a layered window this
    /// probe painted with its own calls is visible in its own desktop capture. False on a locked
    /// workstation, on a session with no compositor, and anywhere a screen read is not a screen.
    /// </param>
    /// <param name="CompositorFenceHeld">
    /// SP-116. Whether the screen reads in this run were really ordered behind the compositor
    /// (<see cref="FlashPixelProbe.CompositorFenceHeld"/>). False means either the fence has been
    /// removed from <see cref="FlashPixelProbe.CaptureDesktop"/> or DWM refused it, and in both
    /// cases every composited number below was taken with no happens-before edge against the thing
    /// that publishes pixels — measured at 34 misses in 1200 reads (SP-116).
    /// </param>
    /// <param name="ControlWindowRendersItsColour">The control's content read back by
    /// <c>PrintWindow</c> — the instrument saying it can read a window at all.</param>
    /// <param name="ControlWindowGoneAfterTeardown">The control cleaned up after itself.</param>
    /// <param name="VirtualToPhysicalRatio">The DPI ratio the desktop read is mapped through.</param>
    /// <param name="Window">The subject surface's handle.</param>
    /// <param name="PresentClaimedAvailable">Whether placement was claimed.</param>
    /// <param name="PaintClaimedAvailable">Whether the paint was claimed.</param>
    /// <param name="PaintState">The full paint state, for failure messages.</param>
    /// <param name="RenderedPixelsMatchingFrame">How many of the surface's rendered pixels equal the frame.</param>
    /// <param name="RenderedPixelCount">How many pixels came back at all.</param>
    /// <param name="DesktopTopColour">The composited desktop at the centre of the frame's top half.</param>
    /// <param name="DesktopBottomColour">The composited desktop at the centre of the frame's bottom half.</param>
    /// <param name="DesktopColourAfterWithdraw">The same point once the surface has been withdrawn.</param>
    /// <param name="AlphaHeldAfterPaint">
    /// The layered alpha the OS still holds for the surface AFTER it was painted, or -1 when it
    /// holds none. This is the ghost check, asked on the far side of the draw: the whole defence of
    /// the GDI content route (divergence D57) is that it does not cost this answer.
    /// </param>
    /// <param name="RePresentAfterPaintClaimedAvailable">A full placement still earns Available on a
    /// painted surface — every SP-099 confirmation, alpha read-back included, still holds.</param>
    /// <param name="RenderedPixelsMatchingFrameAfterRePresent">And re-placing does not blank it.</param>
    /// <param name="PaintAfterWithdrawCode">Painting a withdrawn surface must refuse.</param>
    /// <param name="PaintAfterWithdrawClaimedAvailable">…and must never claim.</param>
    /// <param name="WrongSizeFrameCode">A frame that is not the surface's size must refuse.</param>
    /// <param name="WrongSizeFrameClaimedAvailable">…and must never claim.</param>
    /// <param name="PaintBeforePresentCode">Painting with nothing presented must refuse.</param>
    /// <param name="PaintAfterDisposeCode">Painting a disposed presence must refuse.</param>
    /// <param name="WindowGoneAfterDispose">The subject cleaned up after itself.</param>
    internal sealed record Measurements(
        bool MachineHasInteractiveDesktop,
        bool DesktopCaptureIsLive,
        bool CompositorFenceHeld,
        bool ControlWindowRendersItsColour,
        bool ControlWindowGoneAfterTeardown,
        double VirtualToPhysicalRatio,
        nint Window,
        bool PresentClaimedAvailable,
        bool PaintClaimedAvailable,
        CapabilityState PaintState,
        int RenderedPixelsMatchingFrame,
        int RenderedPixelCount,
        uint DesktopTopColour,
        uint DesktopBottomColour,
        uint DesktopColourAfterWithdraw,
        int AlphaHeldAfterPaint,
        bool RePresentAfterPaintClaimedAvailable,
        int RenderedPixelsMatchingFrameAfterRePresent,
        string PaintAfterWithdrawCode,
        bool PaintAfterWithdrawClaimedAvailable,
        string WrongSizeFrameCode,
        bool WrongSizeFrameClaimedAvailable,
        string PaintBeforePresentCode,
        string PaintAfterDisposeCode,
        bool WindowGoneAfterDispose);

    private static Measurements Measure()
    {
        var control = RunControl();
        var frame = Frame();
        var bounds = SubjectBounds;
        var request = new OverlaySurfaceRequest(bounds, 1.0, ClickThrough: true);

        var idle = new Win32OverlayPresence();
        var paintBeforePresent = idle.Paint(frame);
        idle.Dispose();
        var paintAfterDispose = idle.Paint(frame);

        var presence = new Win32OverlayPresence();
        var present = presence.Present(request);
        var window = presence.NativeHandles.Window;
        var paint = presence.Paint(frame);

        // What the OS renders for this window, through a call the product never makes.
        var rendered = FlashPixelProbe.RenderWindow(window, SurfaceWidth, SurfaceHeight);
        var matching = CountMatching(rendered, frame);

        // What the COMPOSITED DESKTOP holds where the surface is. No wait of any kind: measured
        // before this was written, a CAPTUREBLT screen read immediately after the blit already
        // carries the painted pixel (SP-100 record §1).
        var desktop = FlashPixelProbe.CaptureDesktop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var (desktopTop, desktopBottom) = SampleHalves(desktop, bounds);
        if (desktop.Length > 0)
        {
            FlashPixelProbe.WriteBitmap(
                Path.Combine(EvidenceFolder, "desktop-where-the-flash-is.bmp"),
                ScaledWidth(bounds.Width), ScaledHeight(bounds.Height), desktop);
        }

        if (rendered.Length > 0)
        {
            FlashPixelProbe.WriteBitmap(
                Path.Combine(EvidenceFolder, "surface-as-the-os-renders-it.bmp"),
                SurfaceWidth, SurfaceHeight, rendered);
        }

        // THE GHOST CHECK, ON THE FAR SIDE OF THE DRAW. SP-099 reads the alpha immediately after
        // Present, before any content exists; nothing asked it again once a frame had been put on
        // the surface. That is the hole a future alpha ramp would walk into: UpdateLayeredWindow is
        // the obvious tool for one, it is mutually exclusive with SetLayeredWindowAttributes, and
        // the port's only measured discriminator against the defect the first attempt shipped would
        // go quiet with every existing fact still green.
        var alphaAfterPaint = OverlayWindowProbe.LayeredAlphaOf(window);
        var rePresent = presence.Present(request);
        var renderedAfterRePresent = FlashPixelProbe.RenderWindow(window, SurfaceWidth, SurfaceHeight);
        var matchingAfterRePresent = CountMatching(renderedAfterRePresent, frame);

        var wrongSize = presence.Paint(OverlayFrame.Solid(SurfaceWidth - 1, SurfaceHeight, 0, 0, 0));

        presence.Withdraw();
        var paintAfterWithdraw = presence.Paint(frame);
        var afterWithdraw = FlashPixelProbe.CaptureDesktop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var (afterWithdrawTop, _) = SampleHalves(afterWithdraw, bounds);

        presence.Dispose();

        var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;

        return new Measurements(
            MachineHasInteractiveDesktop: OverlayWindowProbe.MachineHasInteractiveDesktop,
            DesktopCaptureIsLive: control.DesktopCaptureIsLive,
            CompositorFenceHeld: FlashPixelProbe.CompositorFenceHeld,
            ControlWindowRendersItsColour: control.RendersItsColour,
            ControlWindowGoneAfterTeardown: control.GoneAfterTeardown,
            VirtualToPhysicalRatio: virtualWidth > 0 ? physicalWidth / (double)virtualWidth : 0.0,
            Window: window,
            PresentClaimedAvailable: present is CapabilityState.Available,
            PaintClaimedAvailable: paint is CapabilityState.Available,
            PaintState: paint,
            RenderedPixelsMatchingFrame: matching,
            RenderedPixelCount: rendered.Length,
            DesktopTopColour: desktopTop,
            DesktopBottomColour: desktopBottom,
            DesktopColourAfterWithdraw: afterWithdrawTop,
            AlphaHeldAfterPaint: alphaAfterPaint,
            RePresentAfterPaintClaimedAvailable: rePresent is CapabilityState.Available,
            RenderedPixelsMatchingFrameAfterRePresent: matchingAfterRePresent,
            PaintAfterWithdrawCode: CodeOf(paintAfterWithdraw),
            PaintAfterWithdrawClaimedAvailable: paintAfterWithdraw is CapabilityState.Available,
            WrongSizeFrameCode: CodeOf(wrongSize),
            WrongSizeFrameClaimedAvailable: wrongSize is CapabilityState.Available,
            PaintBeforePresentCode: CodeOf(paintBeforePresent),
            PaintAfterDisposeCode: CodeOf(paintAfterDispose),
            WindowGoneAfterDispose: !OverlayWindowProbe.WindowExists(window));
    }

    /// <summary>How many of a rendered read-back's pixels are the frame's, position by position.</summary>
    private static int CountMatching(uint[] rendered, OverlayFrame frame)
    {
        if (rendered.Length != frame.Width * frame.Height)
        {
            return 0;
        }

        var matching = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (rendered[(y * frame.Width) + x] == frame.ColourAt(x, y))
                {
                    matching++;
                }
            }
        }

        return matching;
    }

    private static (uint Top, uint Bottom) SampleHalves(uint[] capture, OverlayBounds bounds)
    {
        if (capture.Length == 0)
        {
            return (0, 0);
        }

        var width = ScaledWidth(bounds.Width);
        var height = ScaledHeight(bounds.Height);
        if (capture.Length < width * height)
        {
            return (0, 0);
        }

        var top = capture[((height / 4) * width) + (width / 2)];
        var bottom = capture[((height * 3 / 4) * width) + (width / 2)];
        return (top, bottom);
    }

    private static int ScaledWidth(int width)
    {
        var (virtualWidth, physicalWidth) = FlashPixelProbe.HorizontalResolutions;
        return virtualWidth <= 0 ? width : Math.Max(1, (int)Math.Round(width * (physicalWidth / (double)virtualWidth)));
    }

    private static int ScaledHeight(int height)
    {
        var (virtualHeight, physicalHeight) = FlashPixelProbe.VerticalResolutions;
        return virtualHeight <= 0
            ? height
            : Math.Max(1, (int)Math.Round(height * (physicalHeight / (double)virtualHeight)));
    }

    private static string CodeOf(CapabilityState state) =>
        state is CapabilityState.Unavailable unavailable ? unavailable.Reason.Code : "no-refusal";

    // ---------------------------------------------------------------------------------------
    //  the control: can a desktop capture see a painted layered window on THIS machine at all?
    // ---------------------------------------------------------------------------------------

    private sealed record Control(bool RendersItsColour, bool DesktopCaptureIsLive, bool GoneAfterTeardown);

    private static Control RunControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Control(false, false, true);
        }

        var bounds = ControlBounds;
        var proc = new WndProc(DefWindowProcW);
        var className = "CcpFlashPixelControl." + Guid.NewGuid().ToString("N");
        var windowClass = new WndClassExW
        {
            cbSize = (uint)Marshal.SizeOf<WndClassExW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className,
        };

        if (RegisterClassExW(ref windowClass) == 0)
        {
            return new Control(false, false, true);
        }

        var window = CreateWindowExW(
            WsExLayered | WsExToolwindow | WsExNoactivate, className, "ccp flash pixel control", WsPopup,
            bounds.X, bounds.Y, bounds.Width, bounds.Height, 0, 0, windowClass.hInstance, 0);

        if (window == 0)
        {
            UnregisterClassW(className, windowClass.hInstance);
            return new Control(false, false, true);
        }

        SetLayeredWindowAttributes(window, 0, 255, LwaAlpha);
        SetWindowPos(window, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            SwpNoactivate | SwpShowwindow);

        PaintSolid(window, bounds.Width, bounds.Height, ControlColour);

        // The topmost band is contested on this machine — the shipping WPF product sits in it and
        // re-asserts (SP-099 §2(c)). The product's own Present absorbs that with a bounded
        // re-raise loop; the control must do the same or it measures "a buried window is not
        // visible", which is true and useless.
        var (centreX, centreY) = bounds.Centre;
        OverlayWindowProbe.HitTestExpecting(centreX, centreY, window, expectSurface: true, out _);

        var rendered = FlashPixelProbe.RenderWindow(window, bounds.Width, bounds.Height);
        var rendersItsColour = rendered.Length > 0
            && FlashPixelProbe.CountOf(rendered, ControlColour) > rendered.Length / 2;

        var desktop = FlashPixelProbe.CaptureDesktop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var live = desktop.Length > 0 && FlashPixelProbe.CountOf(desktop, ControlColour) > desktop.Length / 2;

        DestroyWindow(window);
        UnregisterClassW(className, windowClass.hInstance);
        GC.KeepAlive(proc);

        return new Control(rendersItsColour, live, GoneAfterTeardown: !OverlayWindowProbe.WindowExists(window));
    }

    private static void PaintSolid(nint window, int width, int height, uint colour)
    {
        var screen = GetDC(0);
        var info = new BitmapInfoHeaderControl
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeaderControl>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0,
        };

        var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
        var memory = CreateCompatibleDC(screen);
        SelectObject(memory, bitmap);

        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)((colour >> 16) & 0xFF);
            pixels[i + 1] = (byte)((colour >> 8) & 0xFF);
            pixels[i + 2] = (byte)(colour & 0xFF);
            pixels[i + 3] = 0xFF;
        }

        Marshal.Copy(pixels, 0, bits, pixels.Length);

        var target = GetDC(window);
        BitBlt(target, 0, 0, width, height, memory, 0, 0, Srccopy);
        ReleaseDC(window, target);

        DeleteDC(memory);
        DeleteObject(bitmap);
        ReleaseDC(0, screen);
    }

    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExNoactivate = 0x08000000;
    private const uint LwaAlpha = 0x00000002;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;
    private const int Srccopy = 0x00CC0020;
    private static readonly nint HwndTopmost = -1;

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
    private struct BitmapInfoHeaderControl
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
        public uint mask0;
        public uint mask1;
        public uint mask2;
    }

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint gdiObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(
        nint dc, ref BitmapInfoHeaderControl info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int rop);
}
