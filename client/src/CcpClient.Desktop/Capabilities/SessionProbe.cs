using System.Runtime.InteropServices;

namespace CcpClient.Desktop.Capabilities;

/// <summary>Typed session fact (contract §8.1). Tests assert this enum, never parse detail strings.</summary>
public enum SessionKind
{
    Windows,
    LinuxX11,
    LinuxWayland,

    /// <summary>Wayland session with X11 also offered (XWayland present) — the WSLg shape.</summary>
    LinuxWaylandWithX11,
}

/// <summary>Environment evidence source for the session probe. Production reads the real OS/env; tests inject.</summary>
public interface ISessionEnvironment
{
    bool IsWindows { get; }

    bool IsLinux { get; }

    string? WaylandDisplay { get; }

    string? X11Display { get; }
}

/// <summary>Real session evidence: OS platform + WAYLAND_DISPLAY/DISPLAY (contract §8.1).</summary>
public sealed class RuntimeSessionEnvironment : ISessionEnvironment
{
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public string? WaylandDisplay => Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

    public string? X11Display => Environment.GetEnvironmentVariable("DISPLAY");
}

/// <summary>
/// Demonstrator 1 (contract §8.1): an environment-evidence capability reporting the
/// display session the process was launched in. Reports SESSION FACTS ONLY — never which
/// backend Avalonia selected (X11 by default per proposal §5.1) and never Wayland backend
/// behavior (an open owner question). Under WSLg, WAYLAND_DISPLAY is set while the client
/// runs as an X11 client through XWayland: the truthful kind is LinuxWaylandWithX11.
/// </summary>
public static class SessionProbe
{
    /// <summary>The typed session fact, or null when no display session is offered.</summary>
    public static SessionKind? Detect(ISessionEnvironment env)
    {
        if (env.IsWindows)
        {
            return SessionKind.Windows;
        }

        if (!env.IsLinux)
        {
            return null;
        }

        var wayland = !string.IsNullOrEmpty(env.WaylandDisplay);
        var x11 = !string.IsNullOrEmpty(env.X11Display);
        return (wayland, x11) switch
        {
            (true, true) => SessionKind.LinuxWaylandWithX11,
            (true, false) => SessionKind.LinuxWayland,
            (false, true) => SessionKind.LinuxX11,
            (false, false) => null,
        };
    }

    public static CapabilityState Probe(ISessionEnvironment env)
    {
        var kind = Detect(env);
        if (kind is not null)
        {
            return new CapabilityState.Available(Detail(kind.Value));
        }

        return env is { IsWindows: false, IsLinux: false }
            ? new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.UnsupportedPlatform, "platform is neither Windows nor Linux"))
            : new CapabilityState.Unavailable(new CapabilityReason(
                CapabilityReasonCodes.NoDisplayServer,
                "linux process with neither WAYLAND_DISPLAY nor DISPLAY set (headless)"));
    }

    private static string Detail(SessionKind kind) => kind switch
    {
        SessionKind.Windows => "windows desktop session",
        SessionKind.LinuxX11 => "linux X11 session (DISPLAY set, WAYLAND_DISPLAY unset)",
        SessionKind.LinuxWayland => "linux wayland session (WAYLAND_DISPLAY set, DISPLAY unset)",
        SessionKind.LinuxWaylandWithX11 =>
            "linux wayland session with X11 offered via XWayland (both WAYLAND_DISPLAY and DISPLAY set); " +
            "session facts only — not a claim about the selected Avalonia backend (X11 by default, proposal §5.1)",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
