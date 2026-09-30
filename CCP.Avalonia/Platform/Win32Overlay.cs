using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Windows half of <see cref="X11Overlay"/>, reached only through its entry points when the
/// window's platform handle is an "HWND" - callers never see this class. Same bits the WPF head
/// sets on every desktop overlay (ConditioningControlPanel/Services/Notifications/OverlayService.cs:1415,
/// Services/Flash/FlashService.cs:4614 ApplyClickability, :4838 ForceTopmost):
/// WS_EX_LAYERED|WS_EX_TOOLWINDOW always, WS_EX_TRANSPARENT|WS_EX_NOACTIVATE toggled together with
/// click-through (as Chaos/ChaosOverlayWindow.xaml.cs:1073-1076 does, so an interactive mode can
/// take focus), HWND_TOPMOST via SetWindowPos.
///
/// <para><b>Why a style callback and not just SetWindowLong.</b> Avalonia 12.1.2's Win32
/// <c>WindowImpl.UpdateWindowProperties</c> rebuilds GWL_EXSTYLE from scratch (EDGE | NOREDIRECTIONBITMAP
/// | APPWINDOW) whenever ShowInTaskbar, decorations, resizability or window state change, which would
/// silently drop our bits. <see cref="Win32Properties.AddWindowStylesCallback"/> is its sanctioned hook
/// into that rebuild, so the bits survive it.</para>
///
/// <para><b>Opacity is the layered alpha</b> (SetLayeredWindowAttributes/LWA_ALPHA), the twin of
/// X11's _NET_WM_WINDOW_OPACITY: DWM applies it, the app never re-renders. Avalonia never sets
/// WS_EX_LAYERED itself (it uses DirectComposition + NOREDIRECTIONBITMAP), so nothing competes for it.
/// A freshly layered window stays invisible until its attributes are set once, hence the 255 below.</para>
/// </summary>
internal static class Win32Overlay
{
    private const int GwlExStyle = -20;
    internal const uint WsExTopmost = 0x8, WsExTransparent = 0x20, WsExToolWindow = 0x80,
        WsExLayered = 0x80000, WsExNoActivate = 0x08000000;
    internal const uint OverlayBits = WsExLayered | WsExToolWindow;
    private const uint ClickThroughBits = WsExTransparent | WsExNoActivate;
    private const uint LwaAlpha = 2;
    private const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoActivate = 0x10;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] internal static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetWindowLong(IntPtr hwnd, int index, uint value);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // Per window: whether it should currently be click-through, read by the style callback.
    private static readonly ConditionalWeakTable<TopLevel, StrongBox<bool>> Wanted = new();

    internal static uint Style(uint exStyle, bool clickThrough)
        => (exStyle | OverlayBits) & ~ClickThroughBits | (clickThrough ? ClickThroughBits : 0);

    internal static bool SetClickThrough(TopLevel window, IntPtr hwnd, bool clickThrough)
    {
        if (!OperatingSystem.IsWindows()) return false;
        Track(window).Value = clickThrough;
        return Apply(hwnd, clickThrough);
    }

    /// <summary>Tool/no-activate/layered + HWND_TOPMOST: the Windows form of an override-redirect overlay.</summary>
    internal static bool SetOverrideRedirect(TopLevel window, IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return Apply(hwnd, Track(window).Value)
            && SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    internal static bool SetOpacity(TopLevel window, IntPtr hwnd, double alpha)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return Apply(hwnd, Track(window).Value)
            && SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), LwaAlpha);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointI p);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RectI r);

    /// <summary>Is the window-relative px point inside any of the first <paramref name="count"/> rects?</summary>
    internal static bool Hits(global::Avalonia.PixelRect[] rects, int count, int x, int y)
    {
        for (var i = 0; i < count; i++)
        {
            var r = rects[i];   // half-open, like a Win32 RECT (PixelRect.Contains includes the far edge)
            if (x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height) return true;
        }
        return false;
    }

    /// <summary>The Windows form of an X11 input region. DirectComposition windows have no per-pixel
    /// hit-test and HTTRANSPARENT only passes clicks within the same thread, so a caller that re-sends
    /// its rects every step gets WS_EX_TRANSPARENT toggled by the cursor: input only while the pointer
    /// is over a rect, click-through everywhere else - the same answer WPF's per-pixel layered bubble
    /// windows gave. Only a changed state touches the window style.</summary>
    internal static bool SetInputRects(TopLevel window, IntPtr hwnd, global::Avalonia.PixelRect[] rects, int count)
    {
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var p) || !GetWindowRect(hwnd, out var r)) return false;
        var through = !Hits(rects, count, p.X - r.Left, p.Y - r.Top);
        var box = Track(window);
        if (box.Value == through && (GetWindowLong(hwnd, GwlExStyle) & WsExTransparent) != 0 == through) return true;
        return SetClickThrough(window, hwnd, through);
    }

    private static StrongBox<bool> Track(TopLevel window) => Wanted.GetValue(window, w =>
    {
        var box = new StrongBox<bool>();
        Win32Properties.AddWindowStylesCallback(w, (style, ex) => (style, Style(ex, box.Value)));
        return box;
    });

    private static bool Apply(IntPtr hwnd, bool clickThrough)
    {
        SetWindowLong(hwnd, GwlExStyle, Style(GetWindowLong(hwnd, GwlExStyle), clickThrough));
        // Attributes never set = invisible layered window. Checked by attributes, not by the old
        // style bit, because the style callback can make a window layered behind our back.
        return GetLayeredWindowAttributes(hwnd, out _, out _, out _) || SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
    }
}
