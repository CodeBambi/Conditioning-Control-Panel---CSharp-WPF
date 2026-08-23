using System.Globalization;
using CcpClient.Desktop.Capabilities;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The LINUX camera-enumeration route: the kernel's own V4L2 device class, read as files.
///
/// <para><b>Why this exists at all, in one sentence.</b> A Windows-only enumeration path is not a
/// capability — it is a capability on one platform and a silent "no camera" on the other, and a
/// silent "no camera" is indistinguishable from a real absence, which is the single failure this
/// port's capability contract exists to prevent. Upstream has NO Linux answer of any kind: both of
/// its enumerators are Windows-only by construction (DirectShow COM in
/// <c>Services/Webcam/WebcamDeviceEnumerator.cs</c>, WinRT in
/// <c>Services/Webcam/WebcamWinRtEnumerator.cs</c>), and the first Avalonia attempt's tracker lived
/// in a <c>.Windows</c> project. So this route has no upstream to port and is derived from the
/// kernel interface instead.</para>
///
/// <para><b>It reads FILES, and never opens a device node.</b> <c>/sys/class/video4linux/videoN</c>
/// is one directory per registered V4L2 node and <c>videoN/name</c> is the driver's own card string.
/// Reading them touches no camera, allocates no buffer, starts no stream and lights no camera
/// indicator — the same property that lets the Windows route run before consent. The obvious
/// alternative, <c>VIDIOC_QUERYCAP</c>, was NOT taken and the reason is the consent contract rather
/// than effort: that ioctl requires <c>open(2)</c> on <c>/dev/videoN</c>, and a capability probe
/// that opens a camera device on every launch is exactly what
/// <c>client/docs/capability-inventory.md</c> forbids when it says opening the dashboard or
/// restoring settings must never start the camera.</para>
///
/// <para><b>Two named ceilings, because they are user-visible and must not be discovered later.</b>
/// (1) WITHOUT <c>VIDIOC_QUERYCAP</c> this route cannot tell a CAPTURE node from a metadata or
/// output node, so a camera that registers both (many UVC devices register a metadata node) can
/// appear twice. The roster is honest about what the kernel registered; which node streams video is
/// the capture slice's question, and it is the slice that will already be opening the node.
/// (2) A <c>/dev/videoN</c> path is a KERNEL-ASSIGNED node number and is not durable across reboot
/// or hotplug, so every device from this route carries
/// <c>CameraDevice.IdentityIsStable = false</c> and must not be persisted as a camera identity. A
/// durable Linux identity means udev's <c>/dev/v4l/by-id</c> symlinks or <c>QUERYCAP</c>'s
/// <c>bus_info</c>, and both belong to the slice that persists one. This slice persists no camera
/// identity at all.</para>
///
/// <para><b>A SANDBOX is a different answer from an empty machine, and this is where that is
/// decided.</b> Under Flatpak or Snap the host's <c>/dev/video*</c> and the V4L2 device class are
/// not visible to the process at all, and the supported route is the XDG Camera portal
/// (<c>org.freedesktop.portal.Camera</c>) delivering a PipeWire node —
/// <c>client/docs/capability-inventory.md</c> names it, and it is unported. Reporting "no camera"
/// to a sandboxed user with a webcam plugged in would send them to buy hardware they already own,
/// so a sandboxed process gets a typed refusal that names the portal instead.</para>
///
/// <para><b>What no run of this repository has done: executed this class on Linux.</b> Its parsing
/// is proved against injected fixture trees, which is real evidence about the parsing and no
/// evidence at all about the layout of a running kernel's sysfs.</para>
/// </summary>
public sealed class V4L2CameraDeviceSource : ICameraDeviceSource
{
    private const string DefaultSysfsRoot = "/sys/class/video4linux";

    /// <summary>Flatpak writes this file into every sandboxed process's root.</summary>
    private const string FlatpakMarker = "/.flatpak-info";

    private readonly string _sysfsRoot;
    private readonly bool? _sandboxed;

    /// <param name="sysfsRoot">The V4L2 device class directory; tests inject a fixture tree, production reads the kernel's.</param>
    /// <param name="sandboxed">Overrides sandbox detection; null detects for real (Flatpak marker file or the Snap environment).</param>
    public V4L2CameraDeviceSource(string? sysfsRoot = null, bool? sandboxed = null)
    {
        _sysfsRoot = sysfsRoot ?? DefaultSysfsRoot;
        _sandboxed = sandboxed;
    }

    /// <inheritdoc/>
    public string Route => "V4L2 device class (" + _sysfsRoot + ")";

    /// <summary>True when this process is inside an application sandbox that hides host devices.</summary>
    public bool Sandboxed => _sandboxed ?? (
        File.Exists(FlatpakMarker) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP")));

    /// <inheritdoc/>
    public CameraInventory Enumerate()
    {
        List<CameraDevice> devices;
        try
        {
            if (!Directory.Exists(_sysfsRoot))
            {
                return CameraInventory.Refusing(Route, MissingDeviceClass());
            }

            devices = ReadNodes();
        }
        catch (UnauthorizedAccessException)
        {
            return CameraInventory.Refusing(Route, new CapabilityState.PermissionRequired(new CapabilityReason(
                CameraReasonCodes.CameraPermissionDenied,
                $"the V4L2 device class at {_sysfsRoot} is not readable by this process, so no camera was "
                + "enumerated. This is a permission on the device class itself, NOT a statement that no camera "
                + "is attached")));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return CameraInventory.Refusing(Route, new CapabilityState.Faulted(new CapabilityReason(
                CameraReasonCodes.CameraEnumerationFailed,
                $"reading the V4L2 device class at {_sysfsRoot} failed with {ex.GetType().Name}; no claim is made "
                + "about whether a camera is attached")));
        }

        // An EMPTY device class inside a sandbox is the portal's territory, not an absent camera.
        if (devices.Count == 0 && Sandboxed)
        {
            return CameraInventory.Refusing(Route, PortalUnported("the device class is present and empty"));
        }

        return CameraInventory.Named(Route, devices);
    }

    /// <summary>
    /// No device class at all. Two very different causes with the same directory missing, so the
    /// sandbox is asked FIRST — a sandboxed process cannot see the host's device class no matter how
    /// many cameras are plugged in, and telling that user their kernel has no camera driver would
    /// send them to the wrong place entirely.
    /// </summary>
    private CapabilityState MissingDeviceClass() => Sandboxed
        ? PortalUnported("the device class is not visible to a sandboxed process at all")
        : new CapabilityState.DependencyMissing(
            "the kernel V4L2 device class (" + _sysfsRoot + ")",
            new CapabilityReason(
                CameraReasonCodes.CameraEnumerationUnsupported,
                $"{_sysfsRoot} does not exist, so no V4L2 node is registered on this kernel and NOTHING WAS "
                + "ENUMERATED. This is not \"no camera found\": the videodev module may not be loaded, no driver "
                + "may be bound to an attached camera, or this container may not expose the device class. A "
                + "USB camera on a desktop kernel normally registers here the moment it is plugged in"));

    private CapabilityState PortalUnported(string observed) => new CapabilityState.DependencyMissing(
        "the XDG Camera portal (org.freedesktop.portal.Camera) and its PipeWire node",
        new CapabilityReason(
            CameraReasonCodes.CameraEnumerationUnsupported,
            $"this process is running inside an application sandbox (Flatpak or Snap) and {observed}, which is "
            + "expected: a sandbox does not hand a confined process the host's /dev/video* nodes. The supported "
            + "route is the XDG Camera portal handing back a PipeWire node, and THIS BUILD HAS NO PORTAL CLIENT — "
            + "so nothing was asked and nothing may be said about whether a camera is attached. Running this "
            + "application unsandboxed would let the V4L2 route answer"));

    /// <summary>
    /// The node walk. Only <c>videoN</c> is a V4L2 video node: the same class directory also holds
    /// <c>v4l-subdevN</c> (sensor sub-devices with no user-facing stream), <c>vbiN</c> (teletext)
    /// and <c>radioN</c> (tuners), none of which is a camera, and listing them would be inventing
    /// devices rather than finding them. Ordered by node number so the roster is stable within one
    /// boot; the ORDER carries no identity, which is the whole point of
    /// <c>CameraDevice.IdentityIsStable</c>.
    /// </summary>
    private List<CameraDevice> ReadNodes()
    {
        var found = new List<(int Number, CameraDevice Device)>();
        foreach (var directory in Directory.GetDirectories(_sysfsRoot))
        {
            var node = Path.GetFileName(directory);
            if (NodeNumber(node) is not { } number)
            {
                continue;
            }

            found.Add((number, new CameraDevice(
                StableId: "/dev/" + node,
                DisplayName: CardName(directory),
                IdentityIsStable: false)));
        }

        return [.. found.OrderBy(entry => entry.Number).Select(entry => entry.Device)];
    }

    /// <summary>The node number of a <c>videoN</c> directory, or null when this is not a video node.</summary>
    private static int? NodeNumber(string node)
    {
        const string prefix = "video";
        if (!node.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(
            node.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    /// <summary>
    /// The driver's card string. Unreadable or empty keeps upstream's own literal placeholder
    /// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:87</c>) rather than dropping the device: a node
    /// the kernel registered must not disappear from a roster because its name would not read.
    /// </summary>
    private static string CardName(string nodeDirectory)
    {
        try
        {
            var name = File.ReadAllText(Path.Combine(nodeDirectory, "name")).Trim();
            return string.IsNullOrEmpty(name) ? "(unnamed device)" : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "(unnamed device)";
        }
    }
}
