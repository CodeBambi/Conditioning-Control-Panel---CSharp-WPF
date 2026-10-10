// PORTED from ConditioningControlPanel/Services/Awareness/AwarenessProbes.cs (7.1.5) WasapiMicrophoneProbe:
// "is anything using the microphone?", one half of the awareness meeting guard (the other half is the
// foreground process). WPF sweeps the capture endpoints through NAudio; this head has no NAudio, so the
// same sweep is written against the Core Audio COM interfaces directly.
//
// Linux: no probe. It answers "unknown", which the observer (and WPF) reads as "not in a meeting":
// the fullscreen, typing-burst and CCP-surface gates still apply. See docs/avalonia-linux-exceptions.md.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The cached answer (WPF CacheSeconds = 5: the observer polls every 1.5 s and one sweep mints a COM
/// wrapper per endpoint, per session manager and per session). A sweep that cannot tell (null) reads
/// as "not in use".
/// </summary>
internal sealed class MicrophoneInUseProbe : IMicrophoneProbe
{
    /// <summary>How long one sweep's answer is reused.</summary>
    public const int CacheSeconds = 5;

    private readonly object _lock = new();
    private readonly Func<bool?> _sweep;
    private DateTime _checkedAt = DateTime.MinValue;
    private bool _inUse;

    public MicrophoneInUseProbe() : this(PlatformSweep) { }

    /// <summary>Tests: a sweep that never touches the audio stack.</summary>
    internal MicrophoneInUseProbe(Func<bool?> sweep) => _sweep = sweep;

    /// <summary>False on a platform with no probe (Linux): the answer there is always "unknown".</summary>
    public static bool Supported => OperatingSystem.IsWindows();

    public bool IsInUse(DateTime at)
    {
        lock (_lock)
        {
            if ((at - _checkedAt).TotalSeconds < CacheSeconds) return _inUse;
            _checkedAt = at;
            bool? answer;
            try { answer = _sweep(); } catch { answer = null; }
            _inUse = answer == true;
            return _inUse;
        }
    }

    /// <summary>True / false on Windows, null where nothing can tell.</summary>
    internal static bool? PlatformSweep() => OperatingSystem.IsWindows() ? WasapiSweep() : null;

    // ---- Windows: capture endpoints -> session manager -> any session in the Active state ----

    private const int ECapture = 1;
    private const uint DeviceStateActive = 0x1;
    private const uint ClsCtxAll = 23;
    private const int AudioSessionStateActive = 1;
    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        // IAudioSessionManager's two methods come first in the vtable; they are never called here.
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool? WasapiSweep()
    {
        // Every wrapper is registered and released on the way out (WPF #686: one left to the finalizer
        // leaks a native handle).
        var owned = new List<object?>();
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidMmDeviceEnumerator);
            if (type == null || Activator.CreateInstance(type) is not IMMDeviceEnumerator enumerator) return null;
            owned.Add(enumerator);

            if (enumerator.EnumAudioEndpoints(ECapture, DeviceStateActive, out var endpoints) != 0 || endpoints == null) return null;
            owned.Add(endpoints);
            if (endpoints.GetCount(out var deviceCount) != 0) return null;

            for (uint d = 0; d < deviceCount; d++)
            {
                try
                {
                    if (endpoints.Item(d, out var device) != 0 || device == null) continue;
                    owned.Add(device);

                    var iid = IidAudioSessionManager2;
                    if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var activated) != 0 || activated is not IAudioSessionManager2 manager) continue;
                    owned.Add(activated);

                    if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions == null) continue;
                    owned.Add(sessions);
                    if (sessions.GetCount(out var sessionCount) != 0) continue;

                    for (var i = 0; i < sessionCount; i++)
                    {
                        if (sessions.GetSession(i, out var session) != 0 || session == null) continue;
                        owned.Add(session);
                        if (session.GetState(out var state) == 0 && state == AudioSessionStateActive) return true;
                    }
                }
                catch { /* endpoint vanished mid-sweep */ }
            }
            return false;
        }
        catch (Exception ex)
        {
            // No audio stack, no permission, or a device in a bad state. "Mic unknown" must read as
            // "not in a meeting": silently muting awareness forever because an endpoint threw is worse.
            Log.Debug("AwarenessObserver: microphone probe failed - {Error}", ex.Message);
            return null;
        }
        finally
        {
            for (var i = owned.Count - 1; i >= 0; i--)
            {
                try { if (owned[i] is { } com && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); } catch { }
            }
        }
    }
}
