using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using CcpClient.Desktop.Capabilities;
using Microsoft.Win32;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The WINDOWS camera-enumeration route: the DirectShow system device enumerator over the
/// video-input device category, plus the OS privacy setting that can deny the whole thing before a
/// single moniker is bound.
///
/// <para><b>This is upstream's route, and it is the route because of what it does NOT do.</b>
/// <c>Services/Webcam/WebcamDeviceEnumerator.cs:49-136</c> walks
/// <c>CLSID_SystemDeviceEnum</c> / <c>CLSID_VideoInputDeviceCategory</c> (<c>:27-28</c>) and reads
/// each moniker's <c>FriendlyName</c> out of its property bag (<c>:96</c>). Every one of those is a
/// REGISTRY-backed metadata read: no capture graph is built, no device is opened, no camera
/// indicator lights, and the user's consent is therefore not needed to perform it — which is
/// precisely why the consent gate sits ABOVE this class and refuses before it is called
/// (<see cref="CameraParticipant.ProbeAsync"/>).</para>
///
/// <para><b>THERE IS A SECOND WALK BEHIND THIS ONE, and it runs only when this one names
/// nothing.</b> Upstream has a second enumerator — WinRT
/// <c>DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)</c>
/// (<c>Services/Webcam/WebcamWinRtEnumerator.cs:26-59</c>) — used as a fallback when DirectShow
/// returns an empty list, because a 64-bit process misses cameras that register only 32-bit
/// DirectShow filters or are Media-Foundation-only (<c>:12-17</c>, issues #282/#279/#291). WinRT
/// itself is not available here: <c>Windows.Devices.Enumeration</c> needs a
/// <c>net10.0-windows10.0.*</c> target framework and this client is deliberately a single
/// cross-platform <c>net10.0</c> (<c>client/src/CcpClient.Desktop/CcpClient.Desktop.csproj</c>).
/// The DEVICE LIST behind WinRT is reachable without it, though, and that is what is used:
/// <see cref="MediaFoundationCameraCapture.EnumerateDevices"/> reads the same Media Foundation
/// Frame Server source list through <c>MFEnumDeviceSources</c>, which upstream's own comment names
/// as what WinRT is backed by (<c>Services/Webcam/WebcamWinRtEnumerator.cs:10-11</c>).</para>
///
/// <para>Slice 1 shipped WITHOUT that fallback and recorded the consequence rather than hiding it —
/// on a machine with only an MF-only camera this route reported
/// <see cref="CameraReasonCodes.CameraNoDevice"/> where upstream would list it — and deferred the
/// fix to the capture slice, because that is the API family the capture path would use anyway. The
/// capture slice landed that API family and then used it only to MATCH a device the roster had
/// already named, which left the roster gap exactly where it was. It is closed here.
/// <see cref="CameraEnumerationFallback"/> holds the rule for when the second walk may run, and
/// the answer is still never a device this process did not really see.</para>
///
/// <para><b>The privacy read is a DOWNGRADE and can never be an upgrade</b>
/// (<c>runtime-capability-contract.md</c> §2 rule 4, the rule
/// <c>Capabilities/AtomicFileSystemProbe.cs</c>'s mount-table evidence follows). It can turn a
/// would-be device list into <see cref="CapabilityState.PermissionRequired"/>; it can never turn
/// anything into an admission, and an absent or unreadable key means NO CLAIM — never "allowed".
/// Upstream can only reach this state by trying to open the device
/// (<c>Services/Webcam/WebcamTrackingService.cs:82</c>, <c>WebcamTrackingState.CameraDenied</c>);
/// reading the setting instead means a denied user is told what to change without a camera ever
/// being touched.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DirectShowCameraDeviceSource : ICameraDeviceSource
{
    /// <summary>The Windows Camera privacy setting, per user. <c>Value</c> is <c>Allow</c> or
    /// <c>Deny</c>; the <c>NonPackaged</c> subkey carries the same setting for desktop apps like
    /// this one, which is the row a user toggles under "Let desktop apps access your camera".</summary>
    private const string ConsentStoreKey =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    private const string DenyValue = "Deny";

    private static readonly Guid ClsidSystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
    private static readonly Guid ClsidVideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
    private static readonly Guid IidPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");

    /// <inheritdoc/>
    public string Route =>
        "DirectShow SystemDeviceEnum over CLSID_VideoInputDeviceCategory, then Media Foundation "
        + "MFEnumDeviceSources when it names none";

    /// <inheritdoc/>
    public CameraInventory Enumerate()
    {
        if (DeniedByPrivacySetting() is { } denial)
        {
            return CameraInventory.Refusing(Route, denial);
        }

        CameraInventory monikers;
        try
        {
            monikers = CameraInventory.Named(Route, EnumerateMonikers());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type name only. An HRESULT message on this path can carry a device interface path,
            // which is hardware identity and belongs in nobody's log by accident.
            return CameraInventory.Refusing(Route, new CapabilityState.Faulted(new CapabilityReason(
                CameraReasonCodes.CameraEnumerationFailed,
                $"the DirectShow device enumeration failed with {ex.GetType().Name}; no claim is made about "
                + "whether a camera is attached")));
        }

        // The second walk, on upstream's own trigger and no other
        // (Services/Webcam/WebcamTrackingService.cs:1120-1134). A moniker walk that named even one
        // camera, and a walk that refused, both return above without Media Foundation being started.
        return CameraEnumerationFallback.Apply(
            Route, monikers, MediaFoundationCameraCapture.EnumerateDevices);
    }

    /// <summary>
    /// The user setting, read as evidence that can only REFUSE. Both the per-user gate and the
    /// desktop-app ("NonPackaged") gate must be clear; either one reading exactly <c>Deny</c> denies
    /// this process. Anything else — absent key, absent value, unreadable hive, an unexpected string
    /// — returns null, which asserts nothing at all.
    /// </summary>
    private static CapabilityState? DeniedByPrivacySetting()
    {
        foreach (var (hive, subKey, scope) in new[]
        {
            (Registry.CurrentUser, ConsentStoreKey, "this user account"),
            (Registry.CurrentUser, ConsentStoreKey + @"\NonPackaged", "desktop apps for this user account"),
            (Registry.LocalMachine, ConsentStoreKey, "every account on this machine"),
        })
        {
            string? value;
            try
            {
                using var key = hive.OpenSubKey(subKey);
                value = key?.GetValue("Value") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // An unreadable setting is NOT a denial and is not an allowance either: say nothing.
                continue;
            }

            if (string.Equals(value, DenyValue, StringComparison.OrdinalIgnoreCase))
            {
                return new CapabilityState.PermissionRequired(new CapabilityReason(
                    CameraReasonCodes.CameraPermissionDenied,
                    $"Windows camera access is set to Deny for {scope}, so no camera was enumerated and none could "
                    + "be opened. Turn it on under Settings > Privacy & security > Camera (the \"Let desktop apps "
                    + "access your camera\" row covers this application). This was read from the Windows privacy "
                    + "setting itself — no camera was touched to discover it"));
            }
        }

        return null;
    }

    /// <summary>
    /// Upstream's walk, kept step for step because every step is a metadata read
    /// (<c>Services/Webcam/WebcamDeviceEnumerator.cs:49-136</c>). Three divergences, all about what
    /// the port says rather than what it does:
    ///
    /// <list type="number">
    /// <item>The moniker's DISPLAY NAME is captured as the device identity. Upstream carries only
    /// <c>(int Index, string Name)</c> (<c>:25</c>) and warns twice that the index is not the index
    /// its own capture backend opens (<c>:15-21</c>). The display name is the device interface path,
    /// which is what a persisted camera identity must be built from
    /// (<c>client/docs/capability-inventory.md</c>: <i>"Never use only a transient camera index"</i>).</item>
    /// <item>A moniker whose property bag has no readable <c>FriendlyName</c> keeps upstream's
    /// literal placeholder text (<c>:87</c>) rather than being dropped: a camera the user can see in
    /// Device Manager must not vanish from a roster because its name was unreadable.</item>
    /// <item>A per-moniker failure does not abandon the walk, exactly as upstream's does not
    /// (<c>:102-105</c>); the outer trap in <see cref="Enumerate"/> is what turns a route-level
    /// failure into a typed <see cref="CapabilityState.Faulted"/> instead of upstream's empty list
    /// (<c>:119-122</c>).</item>
    /// </list>
    /// </summary>
    private static List<CameraDevice> EnumerateMonikers()
    {
        var devices = new List<CameraDevice>();
        object? devEnumObject = null;
        IEnumMoniker? enumMoniker = null;

        try
        {
            var devEnumType = Type.GetTypeFromCLSID(ClsidSystemDeviceEnum);
            if (devEnumType is null)
            {
                return devices;
            }

            devEnumObject = Activator.CreateInstance(devEnumType);
            if (devEnumObject is not ICreateDevEnum devEnum)
            {
                return devices;
            }

            var category = ClsidVideoInputDeviceCategory;
            // S_OK (0) means the enumerator is valid; S_FALSE (1) means the category is empty —
            // upstream's own note at :73. An empty category is a real "no camera", not a failure.
            if (devEnum.CreateClassEnumerator(ref category, out enumMoniker, 0) != 0 || enumMoniker is null)
            {
                return devices;
            }

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
            {
                var moniker = monikers[0];
                if (moniker is null)
                {
                    continue;
                }

                object? propertyBagObject = null;
                try
                {
                    var stableId = DisplayNameOf(moniker);
                    var friendly = "(unnamed device)";
                    var bagIid = IidPropertyBag;
                    // Both parameters are declared non-nullable by the ComTypes signature and both are
                    // documented NULL for a device moniker; upstream passes null too
                    // (Services/Webcam/WebcamDeviceEnumerator.cs:92).
                    moniker.BindToStorage(null!, null!, ref bagIid, out propertyBagObject);
                    if (propertyBagObject is IPropertyBag bag)
                    {
                        object value = string.Empty;
                        if (bag.Read("FriendlyName", ref value, IntPtr.Zero) == 0
                            && value is string name && !string.IsNullOrWhiteSpace(name))
                        {
                            friendly = name;
                        }
                    }

                    if (stableId is not null)
                    {
                        devices.Add(new CameraDevice(stableId, friendly, IdentityIsStable: true));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One unreadable device must not cost the roster the others, which is
                    // upstream's behaviour at :102-105.
                }
                finally
                {
                    Release(propertyBagObject);
                    Release(moniker);
                }
            }
        }
        finally
        {
            Release(enumMoniker);
            Release(devEnumObject);
        }

        return devices;
    }

    /// <summary>The moniker's display name — the device interface path. Null when the moniker will
    /// not give one, in which case this device has no identity worth carrying.</summary>
    private static string? DisplayNameOf(IMoniker moniker)
    {
        try
        {
            // Same signature/COM mismatch as BindToStorage above: a device moniker takes no binding
            // context and has nothing to its left.
            moniker.GetDisplayName(null!, null!, out var displayName);
            return string.IsNullOrWhiteSpace(displayName) ? null : displayName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.ReleaseComObject(comObject);
        }
        catch (ArgumentException)
        {
            // Not an RCW (a test double, or already released). Upstream swallows the same way at :110-112.
        }
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid category, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read(
            [MarshalAs(UnmanagedType.LPWStr)] string propertyName,
            [In, Out, MarshalAs(UnmanagedType.Struct)] ref object value,
            IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propertyName, ref object value);
    }
}
