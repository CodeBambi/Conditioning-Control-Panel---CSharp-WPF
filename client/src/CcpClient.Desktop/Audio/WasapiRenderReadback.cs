using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CcpClient.Desktop.Audio;

/// <summary>
/// The Windows audio-render READ-BACK: what the operating system says about this process's own
/// output stream, asked directly rather than inferred from a call that returned.
///
/// <para><b>Why this file exists at all.</b> A backend can always tell you it started. Nothing in
/// <see cref="IAudioBackend"/>, and nothing in any audio library, can tell you the OS agrees — and
/// for audio that gap is the whole problem, because every failure mode past the API boundary is
/// silent from inside the process. This asks WASAPI three questions whose answers the process does
/// not own:</para>
/// <list type="number">
/// <item><b>How many ACTIVE render endpoints exist</b>
/// (<c>IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)</c>). Zero is a real
/// answer and it is what makes "audio is unavailable here" a measured fact rather than a platform
/// guess.</item>
/// <item><b>Does the default endpoint hold a session owned by THIS process</b>
/// (<c>IAudioSessionManager2::GetSessionEnumerator</c> →
/// <c>IAudioSessionControl2::GetProcessId</c>), and <b>is it Active</b>
/// (<c>IAudioSessionControl::GetState</c>). This is the <c>Shell_NotifyIcon(NIM_MODIFY)</c>
/// round-trip of audio: the OS, asked separately, reports our stream.</item>
/// <item><b>What sample level did the OS METER on that stream</b>
/// (<c>IAudioMeterInformation::GetPeakValue</c>). The strongest fact available: the number is
/// produced by the Windows audio engine from the samples it consumed from us, and it reads ZERO on
/// an open device with nothing playing — so it distinguishes "a device is open" from "audio is
/// really being rendered".</item>
/// </list>
///
/// <para><b>Where it STOPS, and this is on the class rather than in a record file.</b> Every fact
/// here is about the Windows audio ENGINE. None of them says the endpoint was unmuted, that a DAC
/// converted anything, that speakers are attached, or that a person heard it. A virtual or RDP sink
/// answers all three identically with no physical output at all — the port has recorded that class
/// already (<c>client/docs/audio-backend-spike.md:24,88</c>, the WSLg "RDP Sink"). Audibility is a
/// manual gate.</para>
///
/// <para><b>Provenance.</b> Every GUID and every vtable slot below was read out of the Windows SDK
/// headers (10.0.26100.0) rather than recalled: <c>um/mmdeviceapi.h:1538</c> (CLSID),
/// <c>:754</c>/<c>:565</c>/<c>:424</c>, <c>um/audiopolicy.h:1155</c> (IAudioSessionManager2 over
/// IAudioSessionManager at <c>:762</c>), <c>:1058</c>, <c>:337</c>, <c>:545</c>,
/// <c>um/endpointvolume.h:842</c>, <c>um/audiosessiontypes.h:149-154</c>,
/// <c>um/mmdeviceapi.h:149,194,203</c>.</para>
///
/// <para><b>Never throws.</b> A read-back that faulted would be an unavailable capability reported
/// as a crash; every path here answers with a record instead.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WasapiRenderReadback
{
    private const int DeviceStateActive = 0x00000001; // mmdeviceapi.h:149
    private const int ERender = 0;                    // mmdeviceapi.h:194
    private const int EConsole = 0;                   // mmdeviceapi.h:203
    private const uint ClsCtxAll = 23;
    private const uint AudioSessionStateActive = 1;   // audiosessiontypes.h:152

    private static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    /// <summary>
    /// How many ACTIVE render endpoints the OS reports. Zero means there is nowhere to play, which
    /// is WPF's own suppression precondition (<c>Services/AudioService.cs:163-166</c>).
    /// </summary>
    internal static int ActiveRenderEndpointCount()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator is null || enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out collection) != 0
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

    /// <summary>
    /// The session facts for this process on the default console render endpoint.
    /// <paramref name="endpointName"/> is carried through unchanged — it is the BACKEND's name for
    /// the device it opened, and this method does not second-guess it; what it adds is everything
    /// the process cannot know about itself.
    /// </summary>
    internal static AudioRenderObservation Observe(string? endpointName)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? endpoint = null;
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessions = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator is null || enumerator.GetDefaultAudioEndpoint(ERender, EConsole, out endpoint) != 0
                || endpoint is null)
            {
                return new AudioRenderObservation(true, endpointName, false, false, false, 0f, 0);
            }

            var iid = typeof(IAudioSessionManager2).GUID;
            if (endpoint.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var raw) != 0 || raw == IntPtr.Zero)
            {
                return new AudioRenderObservation(true, endpointName, false, false, false, 0f, 0);
            }

            manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(raw);
            Marshal.Release(raw);

            if (manager.GetSessionEnumerator(out sessions) != 0 || sessions is null
                || sessions.GetCount(out var count) != 0)
            {
                return new AudioRenderObservation(true, endpointName, false, false, false, 0f, 0);
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

                    var active = control.GetState(out var state) == 0 && state == AudioSessionStateActive;
                    // The per-session meter is a QueryInterface on the session control object.
                    // This path was verified before the capability depended on it; it is never
                    // inferred from a successful playback call.
                    var meterReadable = false;
                    var peak = 0f;
                    if (session is IAudioMeterInformation meter && meter.GetPeakValue(out var value) == 0)
                    {
                        meterReadable = true;
                        peak = value;
                    }

                    return new AudioRenderObservation(true, endpointName, true, active, meterReadable, peak, count);
                }
                finally
                {
                    Release(session);
                }
            }

            // Asked, reachable, and the OS holds no session for us. NOT the same as "not asked",
            // and this is exactly the state a capability that trusted its own return value would
            // have reported as working.
            return new AudioRenderObservation(true, endpointName, false, false, false, 0f, count);
        }
        catch (COMException)
        {
            return new AudioRenderObservation(true, endpointName, false, false, false, 0f, 0);
        }
        catch (InvalidCastException)
        {
            return new AudioRenderObservation(true, endpointName, false, false, false, 0f, 0);
        }
        finally
        {
            Release(sessions);
            Release(manager);
            Release(endpoint);
            Release(enumerator);
        }
    }

    private static IMMDeviceEnumerator? CreateEnumerator()
    {
        var type = Type.GetTypeFromCLSID(MmDeviceEnumeratorClsid);
        return type is null ? null : Activator.CreateInstance(type) as IMMDeviceEnumerator;
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }

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
        // IAudioSessionManager (audiopolicy.h:762) is the base, so its two slots come first.
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
        // IAudioSessionControl (audiopolicy.h:337) is the base, so its nine slots come first.
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
