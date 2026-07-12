using System;

namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Detects the Linux display session type from environment variables.
/// Pure logic, fully unit-testable.
/// </summary>
public static class LinuxSessionDetector
{
    /// <summary>
    /// Detects the Linux session type from the current environment.
    /// </summary>
    public static LinuxSessionType Detect() =>
        Detect(
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>
    /// Detects the Linux session type from provided environment variable values.
    /// Pure function for unit testing.
    /// </summary>
    /// <param name="xdgSessionType">Value of XDG_SESSION_TYPE.</param>
    /// <param name="waylandDisplay">Value of WAYLAND_DISPLAY.</param>
    /// <param name="display">Value of DISPLAY.</param>
    public static LinuxSessionType Detect(string? xdgSessionType, string? waylandDisplay, string? display)
    {
        bool isWayland = string.Equals(xdgSessionType, "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(waylandDisplay);

        bool isX11 = string.Equals(xdgSessionType, "x11", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(display);

        if (isWayland && isX11)
        {
            // Wayland session with XWayland compatibility layer
            return LinuxSessionType.XWayland;
        }

        if (isWayland)
        {
            return LinuxSessionType.Wayland;
        }

        if (isX11)
        {
            return LinuxSessionType.X11;
        }

        return LinuxSessionType.Unknown;
    }
}
