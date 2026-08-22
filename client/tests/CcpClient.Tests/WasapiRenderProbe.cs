using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CcpClient.Tests;

/// <summary>
/// An independent effect instrument: it asks the WINDOWS AUDIO ENGINE what it believes is
/// happening, without asking the product.
///
/// <para><b>Why it re-declares every COM interface instead of using the product's interop.</b>
/// This is the packet's named trap, and it is the same trap <see cref="TrayShellProbe"/> was written
/// for: an audio capability is trivial to fake because every failure mode is INAUDIBLE. A test that
/// measured "a sound played" through the same code that performs the playback could be fooled by one
/// edit to that code. These declarations are deliberately a second, independent copy, so "the
/// product says it holds a render session" and "the OS says a render session belongs to this process
/// id" are two different facts produced by two different code paths.</para>
///
/// <para><b>Why the peak meter is a valid oracle, and MEASURED before it was relied on.</b>
/// <c>IAudioMeterInformation::GetPeakValue</c> on this process's own session returns the sample level
/// the Windows audio engine metered on the stream WE submitted. Measured on this machine (Windows 11)
/// before any of this was written, with the negative control holding: <b>0 before any device was
/// opened, 0 with the device open and started but nothing cued, 0.405 while a clip rendered, and 0
/// again after teardown.</b> The zero-with-an-open-device reading is what makes the oracle bite — a
/// product that opened a device and played nothing cannot pass a fact built on it.
/// <see cref="SessionFact.MeterReadable"/> is reported separately from the value, so "no meter" can
/// never masquerade as "a meter reading zero".</para>
///
/// <para><b>Provenance of every constant below.</b> Read out of the Windows SDK headers on this
/// machine (10.0.26100.0), not recalled: <c>um/mmdeviceapi.h:1538</c> (CLSID_MMDeviceEnumerator),
/// <c>:754</c> / <c>:565</c> / <c>:424</c> (IMMDeviceEnumerator / IMMDeviceCollection / IMMDevice and
/// their vtable order), <c>um/audiopolicy.h:1155</c> / <c>:762</c> / <c>:1058</c> / <c>:337</c> /
/// <c>:545</c> (IAudioSessionManager2 over IAudioSessionManager, IAudioSessionEnumerator,
/// IAudioSessionControl, IAudioSessionControl2), <c>um/endpointvolume.h:842</c>
/// (IAudioMeterInformation), <c>um/audiosessiontypes.h:149-154</c> (AudioSessionState),
/// <c>um/mmdeviceapi.h:149,194,203</c> (DEVICE_STATE_ACTIVE, eRender, eConsole).</para>
///
/// <para>Every native path is guarded by a platform check HERE, in the helper, so the fact bodies
/// stay free of predicates and none of their assertions can be silenced.</para>
/// </summary>
internal static class WasapiRenderProbe
{
    private const int DeviceStateActive = 0x00000001; // mmdeviceapi.h:149
    private const int ERender = 0;                    // mmdeviceapi.h:194
    private const int EConsole = 0;                   // mmdeviceapi.h:203
    private const uint ClsCtxAll = 23;                // CLSCTX_INPROC_SERVER|HANDLER|LOCAL_SERVER|REMOTE_SERVER

    /// <summary>AudioSessionStateActive (audiosessiontypes.h:152).</summary>
    internal const int StateActive = 1;

    /// <summary>AudioSessionStateInactive (audiosessiontypes.h:151).</summary>
    internal const int StateInactive = 0;

    internal static bool WindowsHost => OperatingSystem.IsWindows();

    /// <summary>
    /// How many ACTIVE render endpoints the operating system reports. A property of the machine,
    /// established by the test rather than taken from the product. Zero is a real answer (a headless
    /// build agent, a box with every output disabled) and it is what flips the honest expectation,
    /// exactly as <c>TrayShellProbe.MachineHasNotificationArea</c> does.
    /// </summary>
    internal static int ActiveRenderEndpointCount() =>
        OperatingSystem.IsWindows() ? ActiveRenderEndpointCountCore() : 0;

    /// <summary>True when this machine can render audio at all.</summary>
    internal static bool MachineHasRenderEndpoint => ActiveRenderEndpointCount() > 0;

    /// <summary>
    /// Enumerate the default console render endpoint's sessions and report the one whose owning
    /// process id is this process. Never throws: a machine with no endpoint answers
    /// <see cref="SessionFact.None"/>.
    /// </summary>
    internal static SessionFact SessionForThisProcess() =>
        OperatingSystem.IsWindows() ? SessionForThisProcessCore() : SessionFact.None;

    /// <summary>
    /// What the OS says about THIS process's render session.
    /// <paramref name="SessionsOnEndpoint"/> is how many sessions the endpoint holds in total — the
    /// negative control for the search, so a probe that degenerated into "found nothing, ever" is
    /// visible rather than silently certifying an absence.
    /// </summary>
    internal sealed record SessionFact(
        bool EndpointReachable,
        int SessionsOnEndpoint,
        bool SessionForThisProcess,
        int State,
        bool MeterReadable,
        float Peak)
    {
        /// <summary>The OS is holding an ACTIVE render session for this process right now.</summary>
        internal bool Active => SessionForThisProcess && State == StateActive;

        internal static SessionFact None { get; } = new(false, 0, false, -1, false, 0f);
    }

    [SupportedOSPlatform("windows")]
    private static int ActiveRenderEndpointCountCore()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator is null
                || enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out collection) != 0
                || collection is null)
            {
                return 0;
            }

            return collection.GetCount(out var count) == 0 ? (int)count : 0;
        }
        catch (COMException)
        {
            return 0;
        }
        catch (InvalidCastException)
        {
            return 0;
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }

    [SupportedOSPlatform("windows")]
    private static SessionFact SessionForThisProcessCore()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator is null
                || enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out endpoint) != 0
                || endpoint is null)
            {
                return SessionFact.None;
            }

            var iid = typeof(IAudioSessionManager2).GUID;
            if (endpoint.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var raw) != 0 || raw == IntPtr.Zero)
            {
                return SessionFact.None;
            }

            manager = (IAudioSessionManager2)ObjectFor(raw);
            ReleasePointer(raw);

            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions is null
                || sessions.GetCount(out var count) != 0)
            {
                return new SessionFact(true, 0, false, -1, false, 0f);
            }

            var self = Environment.ProcessId;
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) != 0 || session is null)
                {
                    continue;
                }

                try
                {
                    if (session is not IAudioSessionControl2 control
                        || control.GetProcessId(out var pid) != 0
                        || pid != self)
                    {
                        continue;
                    }

                    var state = control.GetState(out var value) == 0 ? (int)value : -1;
                    var meterReadable = false;
                    var peak = 0f;
                    if (session is IAudioMeterInformation meter && meter.GetPeakValue(out var measured) == 0)
                    {
                        meterReadable = true;
                        peak = measured;
                    }

                    return new SessionFact(true, count, true, state, meterReadable, peak);
                }
                finally
                {
                    Release(session);
                }
            }

            return new SessionFact(true, count, false, -1, false, 0f);
        }
        catch (COMException)
        {
            return SessionFact.None;
        }
        catch (InvalidCastException)
        {
            return SessionFact.None;
        }
        finally
        {
            Release(sessions);
            Release(manager);
            Release(endpoint);
            Release(enumerator);
        }
    }

    [SupportedOSPlatform("windows")]
    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        return type is null ? null : Activator.CreateInstance(type) as IMMDeviceEnumerator;
    }

    [SupportedOSPlatform("windows")]
    private static object ObjectFor(IntPtr unknown) => Marshal.GetObjectForIUnknown(unknown);

    [SupportedOSPlatform("windows")]
    private static void ReleasePointer(IntPtr unknown) => Marshal.Release(unknown);

    [SupportedOSPlatform("windows")]
    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

    // ---------- the independent declarations (SDK-verified, see the class remarks) ----------

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr instance);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr audioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr notification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr notification);

        [PreserveSig]
        int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);

        [PreserveSig]
        int UnregisterDuckNotification(IntPtr duckNotification);
    }

    [ComImport]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionIndex, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out uint state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        [PreserveSig]
        int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

        [PreserveSig]
        int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingParam);

        [PreserveSig]
        int SetGroupingParam(ref Guid @override, ref Guid eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IntPtr newNotifications);

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

        [PreserveSig]
        int GetProcessId(out int retVal);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference(bool optOut);
    }

    [ComImport]
    [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig]
        int GetPeakValue(out float peak);

        [PreserveSig]
        int GetMeteringChannelCount(out uint channelCount);

        [PreserveSig]
        int GetChannelsPeakValues(uint channelCount, [Out] float[] peakValues);

        [PreserveSig]
        int QueryHardwareSupport(out uint hardwareSupportMask);
    }
}
