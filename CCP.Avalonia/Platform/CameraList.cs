using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>The one camera list every picker and the capture path read: V4L2 sysfs nodes on Linux
    /// (<see cref="V4l2Cameras"/>), DirectShow FriendlyNames on Windows (WPF WebcamDeviceEnumerator), with
    /// the WinRT list as the fallback WPF added for cameras DirectShow cannot see (#282 / #279 / #291).
    /// Listing never opens a camera. Anything that goes wrong reads as "no cameras".</summary>
    internal static class CameraList
    {
        /// <summary>WPF QuestHardwareGate's strict probe: true / false only from a clean count; a broken
        /// enumeration THROWS so the gate fails open instead of trusting "absent" forever.</summary>
        internal static bool AnyStrict()
        {
            if (Override is { } o) return o().Count > 0;
            if (Disabled) return true;   // tests: fail open, never ask the machine
            if (OperatingSystem.IsWindows())
                return DirectShowCameras.Enumerate(DirectShowCameras.VideoInput, strict: true).Count > 0 || WinRtCameras.Enumerate().Count > 0;
            return System.Linq.Enumerable.Any(System.IO.Directory.EnumerateFiles("/dev", "video*"));
        }

        /// <summary>Tests: no test may ask the real machine for its cameras.</summary>
        internal static bool Disabled;

        /// <summary>Tests hand in a fixed list (or <see cref="V4l2Cameras.Enumerate"/> over a fake tree).</summary>
        internal static Func<IReadOnlyList<(int Index, string Name)>>? Override;

        public static IReadOnlyList<(int Index, string Name)> Enumerate()
        {
            try
            {
                if (Override is { } o) return o();
                if (Disabled) return Array.Empty<(int, string)>();
                if (OperatingSystem.IsLinux()) return V4l2Cameras.Enumerate();
                if (OperatingSystem.IsWindows())
                {
                    var list = DirectShowCameras.Enumerate();
                    if (list.Count == 0) list = WinRtCameras.Enumerate();
                    return list;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam: camera enumeration failed"); }
            return Array.Empty<(int, string)>();
        }
    }

    /// <summary>WPF WebcamDeviceEnumerator: SystemDeviceEnum / VideoInputDeviceCategory, in DirectShow's
    /// own order, which is the index OpenCV's DSHOW backend opens.</summary>
    [SupportedOSPlatform("windows")]
    internal static class DirectShowCameras
    {
        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
        internal static Guid VideoInput => CLSID_VideoInputDeviceCategory;
        private static readonly Guid IID_IPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator(ref Guid pType, out IEnumMoniker? ppEnumMoniker, int dwFlags);
        }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
                     [In, Out, MarshalAs(UnmanagedType.Struct)] ref object pVar,
                     IntPtr pErrorLog);
            [PreserveSig]
            int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
        }

        public static IReadOnlyList<(int Index, string Name)> Enumerate() => Enumerate(CLSID_VideoInputDeviceCategory);

        /// <summary>Any DirectShow device category, in its own order (the camera list uses video input).</summary>
        internal static IReadOnlyList<(int Index, string Name)> Enumerate(Guid category, bool strict = false)
        {
            var devices = new List<(int, string)>();
            ICreateDevEnum? devEnum = null;
            IEnumMoniker? enumMoniker = null;
            try
            {
                var type = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum);
                if (type == null) return devices;
                devEnum = Activator.CreateInstance(type) as ICreateDevEnum;
                if (devEnum == null) return devices;

                Guid cat = category;
                int hr = devEnum.CreateClassEnumerator(ref cat, out enumMoniker, 0);
                // S_OK = a list; S_FALSE (1) = no devices; negative = failure, read as none unless strict.
                if (hr < 0 && strict) throw Marshal.GetExceptionForHR(hr) ?? new InvalidOperationException($"CreateClassEnumerator failed (0x{hr:X8})");
                if (hr != 0 || enumMoniker == null) return devices;

                var monikers = new IMoniker[1];
                int idx = 0;
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null) continue;
                    string name = "(unnamed device)";
                    object? bag = null;
                    try
                    {
                        Guid iid = IID_IPropertyBag;
                        moniker.BindToStorage(null!, null, ref iid, out bag);
                        if (bag is IPropertyBag pb)
                        {
                            object value = string.Empty;
                            if (pb.Read("FriendlyName", ref value, IntPtr.Zero) == 0 && value is string s && !string.IsNullOrWhiteSpace(s))
                                name = s;
                        }
                    }
                    catch (Exception ex) { Log.Debug(ex, "Webcam: no FriendlyName for device {Index}", idx); }
                    finally
                    {
                        if (bag != null) { try { Marshal.ReleaseComObject(bag); } catch { } }
                        try { Marshal.ReleaseComObject(moniker); } catch { }
                    }
                    devices.Add((idx, name));
                    idx++;
                }
            }
            catch (Exception ex) when (!strict) { Log.Warning(ex, "Webcam: DirectShow enumeration threw"); }
            finally
            {
                if (enumMoniker != null) { try { Marshal.ReleaseComObject(enumMoniker); } catch { } }
                if (devEnum != null) { try { Marshal.ReleaseComObject(devEnum); } catch { } }
            }
            return devices;
        }
    }

    /// <summary>WPF WebcamWinRtEnumerator: the Media Foundation source list, for cameras that register no
    /// 64-bit DirectShow filter. Empty on a build without the WinRT projection.</summary>
    internal static class WinRtCameras
    {
        public static IReadOnlyList<(int Index, string Name)> Enumerate()
        {
            var devices = new List<(int, string)>();
#if CCP_WINRT
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return devices;
            try
            {
                var task = System.Threading.Tasks.Task.Run(Find);
                if (!task.Wait(TimeSpan.FromSeconds(5))) { Log.Warning("Webcam: WinRT enumeration timed out"); return devices; }
                int idx = 0;
                foreach (var name in task.Result) devices.Add((idx++, name));
                Log.Information("Webcam: {Count} video-capture device(s) via WinRT", devices.Count);
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam: WinRT enumeration threw"); }
#endif
            return devices;
        }

#if CCP_WINRT
        [SupportedOSPlatform("windows10.0.19041.0")]
        private static async System.Threading.Tasks.Task<List<string>> Find()
        {
            var names = new List<string>();
            var found = await global::Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                global::Windows.Devices.Enumeration.DeviceClass.VideoCapture);
            foreach (var di in found) names.Add(string.IsNullOrWhiteSpace(di.Name) ? "(unnamed device)" : di.Name);
            return names;
        }
#endif
    }
}
