#if CCP_WINRT
// The Windows screen reader behind ScreenOcrService: GDI grab per monitor, Windows.Media.Ocr over it
// (WPF ScreenOcrService.CaptureAndRecognizeAsync). WPF went Bitmap -> BMP stream -> BitmapDecoder; this
// hands the grabbed pixels straight to SoftwareBitmap, so there is no stream RCW to leak (WPF #634) and
// one reused pixel buffer per monitor size instead of an 8 MB allocation a scan.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.KeywordTriggers;
using Serilog;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ConditioningControlPanel.Avalonia.Platform;

[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class WinRtScreenReader : IScreenTextReader
{
    private readonly OcrEngine? _engine;
    private readonly Dictionary<int, byte[]> _buffers = new();

    internal WinRtScreenReader()
    {
        try
        {
            _engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (_engine == null) Log.Warning("Screen OCR: no OCR language pack available");
        }
        catch (Exception ex) { Log.Error("Screen OCR: failed to create the OCR engine: {Error}", ex.Message); }
    }

    public bool IsAvailable => _engine != null;

    public async Task<List<OcrWordHit>> ReadAllScreensAsync()
    {
        var all = new List<OcrWordHit>();
        if (_engine == null) return all;
        var monitors = Win32Screen.Monitors();
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            try
            {
                if (m.Width <= 0 || m.Height <= 0) continue;
                if (m.Width > OcrEngine.MaxImageDimension || m.Height > OcrEngine.MaxImageDimension) continue;
                var bytes = m.Width * m.Height * 4;
                if (!_buffers.TryGetValue(bytes, out var px)) _buffers[bytes] = px = new byte[bytes];
                // Fails on a locked desktop, a UAC prompt or protected content: skip this screen (WPF).
                if (!Win32Screen.Capture(m, px)) continue;
                using var bmp = SoftwareBitmap.CreateCopyFromBuffer(px.AsBuffer(), BitmapPixelFormat.Bgra8,
                    m.Width, m.Height, BitmapAlphaMode.Ignore);
                var result = await _engine.RecognizeAsync(bmp);
                if (result == null) continue;
                foreach (var line in result.Lines)
                    foreach (var word in line.Words)
                    {
                        var r = word.BoundingRect;
                        all.Add(new OcrWordHit(word.Text, m.X + (int)r.X, m.Y + (int)r.Y, (int)r.Width, (int)r.Height, i));
                    }
            }
            catch (Exception ex) { Log.Debug("Screen OCR: capture failed for screen {Index}: {Error}", i, ex.Message); }
            finally { if (_buffers.TryGetValue(m.Width * m.Height * 4, out var used)) Array.Clear(used); }
        }
        return all;
    }

    public IReadOnlyList<(int X, int Y, int Width, int Height)> OwnWindowRects() => Win32Screen.OwnWindowRects();
}

/// <summary>Monitor list, a GDI grab and this process's window rectangles. Thread-safe Win32 only.</summary>
[SupportedOSPlatform("windows")]
internal static class Win32Screen
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    private const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    internal readonly record struct Monitor(int X, int Y, int Width, int Height);

    internal static List<Monitor> Monitors()
    {
        var list = new List<Monitor>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT r, IntPtr _) =>
            {
                list.Add(new Monitor(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
                return true;
            }, IntPtr.Zero);
        }
        catch { /* no monitors read as nothing to scan */ }
        return list;
    }

    /// <summary>Copies one monitor into <paramref name="bgra"/> (top-down, 4 bytes a pixel). False on failure.</summary>
    internal static bool Capture(Monitor m, byte[] bgra)
    {
        if (bgra.Length < m.Width * m.Height * 4) return false;
        IntPtr screen = IntPtr.Zero, mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return false;
            mem = CreateCompatibleDC(screen);
            if (mem == IntPtr.Zero) return false;
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = m.Width, biHeight = -m.Height,
                biPlanes = 1, biBitCount = 32, biCompression = 0,
            };
            dib = CreateDIBSection(screen, ref bmi, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return false;
            old = SelectObject(mem, dib);
            if (!BitBlt(mem, 0, 0, m.Width, m.Height, screen, m.X, m.Y, SRCCOPY | CAPTUREBLT)) return false;
            Marshal.Copy(bits, bgra, 0, m.Width * m.Height * 4);
            return true;
        }
        catch { return false; }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(mem, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>WPF App.GetCcpWindowRectsCached: this process's visible windows, minus any that covers a
    /// whole monitor (full-screen overlay containers carry no readable text at the window level and
    /// would swallow every word, WPF #273).</summary>
    internal static IReadOnlyList<(int X, int Y, int Width, int Height)> OwnWindowRects()
    {
        var rects = new List<(int, int, int, int)>();
        try
        {
            var monitors = Monitors();
            var self = (uint)Environment.ProcessId;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != self || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                if (!GetWindowRect(hwnd, out var r)) return true;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return true;
                foreach (var m in monitors)
                    if (w >= m.Width && h >= m.Height) return true;
                rects.Add((r.Left, r.Top, w, h));
                return true;
            }, IntPtr.Zero);
        }
        catch { /* an unreadable window list reads as none */ }
        return rects;
    }
}
#endif
