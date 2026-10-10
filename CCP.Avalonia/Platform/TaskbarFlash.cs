// PORTED from ConditioningControlPanel/Helpers/FlashWindowHelper.cs (7.1.5): flashes a window's taskbar
// button until it comes to the foreground. Windows: FlashWindowEx, the real call.
// Linux: not ported. The X11 twin is the _NET_WM_STATE_DEMANDS_ATTENTION client message (no helper for
// client messages in X11Overlay yet) and Wayland has no such request for an app without a fresh
// activation token; the OS notification the caller also raises is the cue there.
using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class TaskbarFlash
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

    private const uint FLASHW_ALL = 3;         // caption and taskbar button
    private const uint FLASHW_TIMERNOFG = 12;  // until the window comes to the foreground

    /// <summary>Test seam: counts the calls instead of reaching the OS.</summary>
    internal static Func<Window, bool>? Override;

    /// <summary>WPF NotifyRemoteControllerJoined's rule: only a minimised or hidden window flashes.</summary>
    internal static bool ShouldFlash(bool minimized, bool visible) => minimized || !visible;

    /// <summary>True when the OS was asked to flash. Never activates or restores the window.</summary>
    public static bool Flash(Window? window, uint count = 5)
    {
        if (window == null) return false;
        if (Override is { } o) return o(window);
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "HWND") return false;
            var fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle.Handle,
                dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
                uCount = count,
                dwTimeout = 0
            };
            FlashWindowEx(ref fi);
            return true;
        }
        catch (Exception ex) { Serilog.Log.Debug("Taskbar flash failed: {E}", ex.Message); return false; }
    }
}
