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
            "DirectShow SystemDeviceEnum over CLSID_VideoInputDeviceCategory, then Media Foundation "
            + "MFEnumDeviceSources when it names none",
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

    /// <summary>The capture route for the platform this process is really on.</summary>
    public static ICameraCaptureSource CaptureForCurrentPlatform() => CaptureFor(CurrentPlatform());

    /// <summary>
    /// The capture route for a named platform. Every arm returns a source; none of them opens
    /// anything until <see cref="ICameraCaptureSource.Open"/> is called, and
    /// <see cref="CameraParticipant.StartCaptureAsync"/> decides whether that is allowed.
    ///
    /// <para><b>THERE IS ONE REAL CAPTURE ROUTE IN THIS BUILD AND IT IS WINDOWS-ONLY.</b> That is a
    /// gap, it is named here rather than discovered later, and it is NOT hidden behind a Linux arm
    /// that would compile and then fail on a real machine. A V4L2 streaming path
    /// (<c>VIDIOC_S_FMT</c>, <c>VIDIOC_REQBUFS</c>, <c>mmap</c>, <c>VIDIOC_QBUF</c>,
    /// <c>VIDIOC_STREAMON</c>) is perfectly writable, but no machine available to this port has a
    /// V4L2 device to run it against — the evidence host's <c>/sys/class/video4linux</c> is empty —
    /// and hand-laid-out <c>ioctl</c> structures that have never executed against a kernel are not a
    /// capability, they are a compile-time claim. <c>docs/constitution.md</c>'s rule is the same one:
    /// a successful build never establishes Linux support. So Linux gets a typed refusal that names
    /// exactly what is missing, and a Linux user is told the truth instead of being handed a silent
    /// failure.</para>
    /// </summary>
    public static ICameraCaptureSource CaptureFor(CameraHostPlatform platform) => platform switch
    {
        // Guarded for the reason the enumeration arm above is: a type carrying
        // [SupportedOSPlatform("windows")] must never be CONSTRUCTED off Windows, even to be
        // refused by, and this arm is reachable on a Linux machine because the platform is a parameter.
        CameraHostPlatform.Windows when OperatingSystem.IsWindows() => new MediaFoundationCameraCapture(),

        CameraHostPlatform.Windows => new UnsupportedCameraCaptureSource(
            "none",
            "the Windows capture route was selected on a process that is not running on Windows, so no camera "
            + "was opened and nothing may be said about whether one could have been",
            "a Windows process to run Media Foundation in"),

        CameraHostPlatform.Linux => new UnsupportedCameraCaptureSource(
            "none",
            "THIS BUILD HAS NO LINUX CAPTURE PATH, and no camera was opened. The Linux route enumerates cameras "
            + "from the kernel's V4L2 device class but cannot stream from one: that needs VIDIOC_S_FMT, "
            + "VIDIOC_REQBUFS, mmap and VIDIOC_STREAMON against /dev/videoN, or — inside a Flatpak or Snap "
            + "sandbox — the XDG Camera portal handing back a PipeWire node. Neither is implemented, and neither "
            + "has ever run against a real Linux camera in this project. This is a gap in the port, NOT a "
            + "statement about your hardware: a camera plugged into this machine is almost certainly fine",
            "a V4L2 streaming capture path (or the XDG Camera portal's PipeWire node inside a sandbox)"),

        CameraHostPlatform.MacOs => new UnsupportedCameraCaptureSource(
            "none",
            "macOS is outside this port's supported Windows/Linux target set (architecture.md), so there is no "
            + "camera capture route here and nothing was attempted",
            "a macOS AVFoundation capture path this port does not target"),

        _ => new UnsupportedCameraCaptureSource(
            "none",
            "this platform is not one this build has a camera capture route for, and nothing was attempted",
            "a camera capture path for this platform"),
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
