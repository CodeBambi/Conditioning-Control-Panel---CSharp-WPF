namespace ConditioningControlPanel.Core.Platform;

/// <summary>
/// Detected Linux display session type.
/// </summary>
public enum LinuxSessionType
{
    /// <summary>X11 session (DISPLAY set, XDG_SESSION_TYPE is "x11" or absent).</summary>
    X11,

    /// <summary>Native Wayland session (XDG_SESSION_TYPE=="wayland" or WAYLAND_DISPLAY set).</summary>
    Wayland,

    /// <summary>XWayland: Wayland session with X11 compatibility layer (both WAYLAND_DISPLAY and DISPLAY set).</summary>
    XWayland,

    /// <summary>Session type could not be determined.</summary>
    Unknown
}
