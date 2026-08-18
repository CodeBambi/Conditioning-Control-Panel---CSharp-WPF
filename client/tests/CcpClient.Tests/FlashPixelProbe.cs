using System.Runtime.InteropServices;

namespace CcpClient.Tests;

/// <summary>
/// SP-100's independent PIXEL instrument. <see cref="OverlayWindowProbe"/> asks the window manager
/// about a surface's STATE; this one asks the operating system what the surface, and the desktop,
/// actually contain.
///
/// <para><b>Why it re-declares every P/Invoke.</b> Same reason as its sibling, and more sharply: the
/// product confirms its own paint by reading the window's device context back. A test that measured
/// the same thing through the same declarations would be one edit away from certifying nothing. So
/// this reads the surface a DIFFERENT way (<c>PrintWindow</c>, the OS rendering the window into a
/// bitmap of the caller's) and reads the COMPOSITED DESKTOP a third way
/// (<c>BitBlt(SRCCOPY | CAPTUREBLT)</c> from the screen device context, which is the only screen
/// read that includes layered windows).</para>
///
/// <para><b>The DPI trap, measured.</b> The test host is DPI-UNAWARE, so USER32 virtualises window
/// coordinates (this machine: 1646x1029) while the screen device context is physical (2880x1800).
/// Reading the desktop at a window's own coordinates therefore samples the WRONG POINT and reports
/// whatever is behind the surface — which, while SP-100 was being written, made a perfectly visible
/// surface look invisible for four measurement rounds. The ratio is derived from the OS itself
/// (<c>DESKTOPHORZRES / HORZRES</c>) and the derivation is asserted, never assumed.</para>
///
/// <para><b>What it still is not.</b> A human. A composited desktop read from inside the process is
/// the strongest evidence a process can produce about its own pixels, and it is still not the
/// headed capture that <c>verification-harness.md</c> requires for a <c>presentation-verified</c>
/// claim. It cannot see a Magnifier, a mirror driver, an exclusive-fullscreen swap chain or a
/// physically dark monitor.</para>
/// </summary>
internal static class FlashPixelProbe
{
    private const int Srccopy = 0x00CC0020;

    /// <summary>Documented under BitBlt: "Includes any windows that are layered on top of your
    /// window in the resulting image." Without it a screen read cannot see this surface at all.</summary>
    private const int CaptureBlt = 0x40000000;

    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const int Horzres = 8;
    private const int Vertres = 10;
    private const int DesktopVertres = 117;
    private const int DesktopHorzres = 118;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>
    /// The OS's own physical-to-virtual scale for the screen device context, as a pair the caller
    /// can assert on. In a DPI-aware process both numbers are equal; in this test host they are
    /// not, and the difference is exactly the trap above.
    /// </summary>
    internal static (int Virtual, int Physical) HorizontalResolutions
    {
        get
        {
            if (!WindowsHost)
            {
                return (0, 0);
            }

            var dc = GetDC(0);
            var result = (GetDeviceCaps(dc, Horzres), GetDeviceCaps(dc, DesktopHorzres));
            ReleaseDC(0, dc);
            return result;
        }
    }

    /// <summary>The vertical twin.</summary>
    internal static (int Virtual, int Physical) VerticalResolutions
    {
        get
        {
            if (!WindowsHost)
            {
                return (0, 0);
            }

            var dc = GetDC(0);
            var result = (GetDeviceCaps(dc, Vertres), GetDeviceCaps(dc, DesktopVertres));
            ReleaseDC(0, dc);
            return result;
        }
    }

    /// <summary>
    /// What the OS renders for a window, into a bitmap this probe owns.
    ///
    /// <para><c>PrintWindow</c> is called with flags 0, not <c>PW_RENDERFULLCONTENT</c>, and that is
    /// a measurement rather than a preference: <c>PW_RENDERFULLCONTENT</c> goes through DWM
    /// asynchronously and returned an all-black bitmap on the FIRST call after a show while the
    /// legacy path returned the painted content every time, at three different layered alphas
    /// (SP-100 record §1). A read-back that needs a wall-clock wait to be right is not usable in
    /// this suite at all.</para>
    /// </summary>
    /// <returns>A width*height array of <c>COLORREF</c> values, or an empty array when the OS
    /// refused. Empty is a distinguishable answer on purpose.</returns>
    internal static uint[] RenderWindow(nint window, int width, int height)
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
            return PrintWindow(window, surface.Dc, 0) ? ReadPixels(surface.Bits, width, height) : [];
        }
        finally
        {
            ReleaseSurface(surface);
        }
    }

    /// <summary>
    /// The composited desktop, at a rectangle given in the CALLER's (possibly virtualised) window
    /// coordinate space, mapped to the screen device context's own space through the OS's ratio.
    /// </summary>
    internal static uint[] CaptureDesktop(int x, int y, int width, int height)
    {
        if (!WindowsHost || width <= 0 || height <= 0)
        {
            return [];
        }

        var (virtualWidth, physicalWidth) = HorizontalResolutions;
        var (virtualHeight, physicalHeight) = VerticalResolutions;
        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            return [];
        }

        var scaleX = physicalWidth / (double)virtualWidth;
        var scaleY = physicalHeight / (double)virtualHeight;
        var sourceX = (int)Math.Round(x * scaleX);
        var sourceY = (int)Math.Round(y * scaleY);
        var sourceWidth = Math.Max(1, (int)Math.Round(width * scaleX));
        var sourceHeight = Math.Max(1, (int)Math.Round(height * scaleY));

        var surface = CreateSurface(sourceWidth, sourceHeight);
        if (surface.Dc == 0)
        {
            return [];
        }

        var screen = GetDC(0);
        try
        {
            if (screen == 0)
            {
                return [];
            }

            return BitBlt(surface.Dc, 0, 0, sourceWidth, sourceHeight, screen, sourceX, sourceY, Srccopy | CaptureBlt)
                ? ReadPixels(surface.Bits, sourceWidth, sourceHeight)
                : [];
        }
        finally
        {
            ReleaseDC(0, screen);
            ReleaseSurface(surface);
        }
    }

    /// <summary>How many of <paramref name="pixels"/> are exactly <paramref name="colour"/>.</summary>
    internal static int CountOf(uint[] pixels, uint colour)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var count = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] == colour)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>A 32bpp BMP of a captured rectangle, for a human to look at. Evidence, never an
    /// assertion: nothing in this suite reads it back.</summary>
    internal static void WriteBitmap(string path, int width, int height, uint[] pixels)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length < width * height)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var payload = width * height * 4;
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + payload);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(-height);          // top-down
        writer.Write((short)1);
        writer.Write((short)32);
        writer.Write(0);
        writer.Write(payload);
        writer.Write(2835);
        writer.Write(2835);
        writer.Write(0);
        writer.Write(0);

        for (var i = 0; i < width * height; i++)
        {
            var colour = pixels[i];
            writer.Write((byte)((colour >> 16) & 0xFF));
            writer.Write((byte)((colour >> 8) & 0xFF));
            writer.Write((byte)(colour & 0xFF));
            writer.Write((byte)0xFF);
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

    private static Surface CreateSurface(int width, int height)
    {
        var screen = GetDC(0);
        if (screen == 0)
        {
            return default;
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
                    biCompression = BiRgb,
                },
            };

            var bitmap = CreateDIBSection(screen, ref info, DibRgbColors, out var bits, 0, 0);
            if (bitmap == 0)
            {
                return default;
            }

            var dc = CreateCompatibleDC(screen);
            if (dc == 0)
            {
                DeleteObject(bitmap);
                return default;
            }

            SelectObject(dc, bitmap);
            return new Surface(dc, bitmap, bits);
        }
        finally
        {
            ReleaseDC(0, screen);
        }
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

    private readonly record struct Surface(nint Dc, nint Bitmap, nint Bits);

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
        public uint bmiColors0;
        public uint bmiColors1;
        public uint bmiColors2;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint window, nint dc, uint flags);

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
        nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(nint dc, int index);
}
