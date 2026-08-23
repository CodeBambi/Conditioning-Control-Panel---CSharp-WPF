namespace CcpClient.Desktop.Camera;

/// <summary>The platform a camera-enumeration route is chosen for. A SELECTION input only:
/// naming a platform never makes a route work on it.</summary>
public enum CameraHostPlatform
{
    Windows,
    Linux,
    MacOs,
    Unknown,
}

/// <summary>
/// Chooses the camera-enumeration route.
///
/// <para><b>Selection is not availability</b> (<c>runtime-capability-contract.md</c> §2 rule 2).
/// Nothing here produces a <see cref="Capabilities.CapabilityState"/> of any kind, and nothing here
/// enumerates: it hands back the object that would ask, and
/// <see cref="CameraParticipant.ProbeAsync"/> decides whether it is allowed to.</para>
///
/// <para><b>The platform is a PARAMETER, which is what makes both arms executable.</b> The
/// <c>Pointer/PointerSurfaceFactory.cs</c> shape: a suite running on Windows can drive the Linux
/// selection and a suite running on Linux can drive the Windows one, so neither arm is a branch
/// only the other platform's machine ever reaches. <see cref="ForCurrentPlatform"/> is the product
/// entry point and takes no parameter at all.</para>
/// </summary>
public static class CameraDeviceSourceFactory
{
    /// <summary>The route for the platform this process is really on.</summary>
    public static ICameraDeviceSource ForCurrentPlatform() => For(CurrentPlatform());

    /// <summary>The route for a named platform. Every arm returns a source; none of them looks at anything yet.</summary>
    public static ICameraDeviceSource For(CameraHostPlatform platform) => platform switch
    {
        // Guarded so the Windows-only COM route is never CONSTRUCTED off Windows: this arm is
        // reachable on a Linux machine (the platform is a parameter), and a type carrying
        // [SupportedOSPlatform("windows")] must not be instantiated there even to be refused by.
        CameraHostPlatform.Windows when OperatingSystem.IsWindows() => new DirectShowCameraDeviceSource(),

        CameraHostPlatform.Windows => new UnsupportedCameraDeviceSource(
            "DirectShow SystemDeviceEnum over CLSID_VideoInputDeviceCategory",
            "the Windows camera-enumeration route was selected on a process that is not running on Windows, so "
            + "nothing was asked and nothing may be said about whether a camera is attached"),

        CameraHostPlatform.Linux => new V4L2CameraDeviceSource(),

        CameraHostPlatform.MacOs => new UnsupportedCameraDeviceSource(
            "none",
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so no camera "
            + "enumeration route exists here and nothing was attempted"),

        _ => new UnsupportedCameraDeviceSource(
            "none",
            "this platform is not one this build has a camera-enumeration route for, and nothing was attempted"),
    };

    /// <summary>The platform this process is on, as a selection input only.</summary>
    public static CameraHostPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return CameraHostPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return CameraHostPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return CameraHostPlatform.MacOs;
        }

        return CameraHostPlatform.Unknown;
    }
}
