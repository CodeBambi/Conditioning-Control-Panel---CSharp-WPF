using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Helpers;

/// <summary>
/// Flashes a WPF window's taskbar button to grab the user's attention when
/// the app is minimized or in the background. Uses Win32 FlashWindowEx.
/// </summary>
internal static class FlashWindowHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_ALL = 3;         // Flash both caption and taskbar button
    private const uint FLASHW_TIMERNOFG = 12;  // Flash until the window comes to the foreground

    public static void Flash(Window window, uint count = 5)
    {
        if (window == null) return;

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero) return;

        var fi = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = helper.Handle,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = count,
            dwTimeout = 0
        };
        FlashWindowEx(ref fi);
    }
}
