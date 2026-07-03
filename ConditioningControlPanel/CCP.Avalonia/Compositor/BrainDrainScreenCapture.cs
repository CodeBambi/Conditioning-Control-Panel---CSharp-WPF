using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Core.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Compositor;

/// <summary>
/// Windows-only GDI screen capture for the brain-drain blur, ported from the WPF head's
/// OverlayService.CaptureScreenOptimized (Services/Notifications/OverlayService.cs, region
/// "Brain Drain Blur (Screen Capture - Optimized)").
/// StretchBlt-shrinks one monitor into a small 32bpp bitmap in a single GDI call; the caller
/// blurs and upscales it (the upscale itself reads as extra blur, so a proportionally smaller
/// radius suffices — WPF parity).
/// Feedback-loop note: GDI screen capture honors WDA_EXCLUDEFROMCAPTURE, and the brain-drain
/// layer renders in the capture-EXCLUDED compositor surface, so this capture never sees the
/// blur itself ("so we don't capture ourselves" — WPF OverlayService.cs:1685).
/// Non-Windows: returns null; the layer falls back to its tint rendering.
/// </summary>
internal static class BrainDrainScreenCapture
{
    /// <summary>
    /// Captures <paramref name="screenBounds"/> (physical pixels, virtual-screen coordinates)
    /// downscaled by <paramref name="downscale"/> into an <see cref="SKImage"/>. Returns null
    /// on failure or on non-Windows platforms. Caller owns (and must dispose) the image.
    /// </summary>
    public static SKImage? CaptureDownscaled(PixelRect screenBounds, int downscale)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (screenBounds.IsEmpty) return null;

        // Downscaled capture target — even dimensions, at least 2px (WPF parity).
        int divisor = Math.Max(1, downscale);
        int dw = Math.Max(2, ((int)screenBounds.Width / divisor) & ~1);
        int dh = Math.Max(2, ((int)screenBounds.Height / divisor) & ~1);

        IntPtr hdcSrc = IntPtr.Zero;
        IntPtr hdcDest = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOld = IntPtr.Zero;

        try
        {
            hdcSrc = GetDC(IntPtr.Zero);
            if (hdcSrc == IntPtr.Zero) return null;

            hdcDest = CreateCompatibleDC(hdcSrc);
            if (hdcDest == IntPtr.Zero) return null;

            // Top-down 32bpp DIB so the pixel buffer maps 1:1 onto Skia BGRA8888 rows.
            var bmi = new BITMAPINFO
            {
                biSize = 40, // sizeof(BITMAPINFOHEADER); must be the header size, not the struct size
                biWidth = dw,
                biHeight = -dh, // negative = top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            };
            hBitmap = CreateDIBSection(hdcDest, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;

            hOld = SelectObject(hdcDest, hBitmap);

            // Shrink the screen content into the small bitmap in one GDI call (WPF parity:
            // HALFTONE + StretchBlt with SRCCOPY).
            SetStretchBltMode(hdcDest, HALFTONE);
            if (!StretchBlt(hdcDest, 0, 0, dw, dh,
                            hdcSrc, (int)screenBounds.X, (int)screenBounds.Y,
                            (int)screenBounds.Width, (int)screenBounds.Height, SRCCOPY))
            {
                return null;
            }

            // GDI is done writing once StretchBlt returns; deselect before reading the bits.
            SelectObject(hdcDest, hOld);
            hOld = IntPtr.Zero;

            // Copy the DIB pixels into an immutable SKImage (alpha from BI_RGB is undefined,
            // so treat as opaque).
            var info = new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Opaque);
            return SKImage.FromPixelCopy(info, bits, dw * 4);
        }
        catch
        {
            return null;
        }
        finally
        {
            // Always cleanup GDI handles in reverse order of creation.
            if (hOld != IntPtr.Zero && hdcDest != IntPtr.Zero)
                SelectObject(hdcDest, hOld);
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
            if (hdcDest != IntPtr.Zero)
                DeleteDC(hdcDest);
            if (hdcSrc != IntPtr.Zero)
                ReleaseDC(IntPtr.Zero, hdcSrc);
        }
    }

    private const int SRCCOPY = 0x00CC0020;
    private const int HALFTONE = 4;
    private const uint DIB_RGB_COLORS = 0;

    // BITMAPINFOHEADER layout; 32bpp BI_RGB needs no color table, so the header alone is a
    // valid BITMAPINFO for CreateDIBSection.
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);
}
