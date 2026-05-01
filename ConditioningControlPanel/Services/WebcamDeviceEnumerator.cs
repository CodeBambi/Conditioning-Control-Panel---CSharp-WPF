using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Enumerates connected video-capture devices via the DirectShow
    /// SystemDeviceEnum / VideoInputDeviceCategory COM interfaces. Returns the
    /// FriendlyName string for each registered camera, in the same order
    /// DirectShow exposes them.
    ///
    /// Notes on the index mapping: OpenCV's VideoCapture takes an integer
    /// device index. With the DSHOW backend the index matches DirectShow's
    /// enumeration order, so the names from this enumerator line up directly.
    /// With the MSMF backend the order is usually identical on a typical
    /// system, but is not guaranteed — that's why the service logs both the
    /// configured index AND the advertised name when opening, so a mismatch is
    /// obvious in the log.
    /// </summary>
    public static class WebcamDeviceEnumerator
    {
        public readonly record struct WebcamDevice(int Index, string Name);

        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
        private static readonly Guid IID_IPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, int dwFlags);
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

        public static IReadOnlyList<WebcamDevice> Enumerate()
        {
            var devices = new List<WebcamDevice>();
            ICreateDevEnum? devEnum = null;
            IEnumMoniker? enumMoniker = null;

            try
            {
                var devEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum);
                if (devEnumType == null)
                {
                    App.Logger?.Warning("WebcamDeviceEnumerator: SystemDeviceEnum CLSID could not be resolved");
                    return devices;
                }

                devEnum = Activator.CreateInstance(devEnumType) as ICreateDevEnum;
                if (devEnum == null)
                {
                    App.Logger?.Warning("WebcamDeviceEnumerator: ICreateDevEnum activation returned null");
                    return devices;
                }

                Guid videoCat = CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref videoCat, out enumMoniker, 0);
                // hr == 0 (S_OK) → enumMoniker valid; hr == 1 (S_FALSE) → no devices.
                if (hr != 0 || enumMoniker == null)
                {
                    return devices;
                }

                var monikers = new IMoniker[1];
                IntPtr fetched = IntPtr.Zero;
                int idx = 0;
                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null) continue;

                    string name = "(unnamed device)";
                    object? propBagObj = null;
                    try
                    {
                        Guid propBagIid = IID_IPropertyBag;
                        moniker.BindToStorage(null, null, ref propBagIid, out propBagObj);
                        if (propBagObj is IPropertyBag pb)
                        {
                            object value = string.Empty;
                            if (pb.Read("FriendlyName", ref value, IntPtr.Zero) == 0 && value is string s && !string.IsNullOrWhiteSpace(s))
                            {
                                name = s;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Debug(ex, "WebcamDeviceEnumerator: failed to read FriendlyName for device {Index}", idx);
                    }
                    finally
                    {
                        if (propBagObj != null)
                        {
                            try { Marshal.ReleaseComObject(propBagObj); } catch { }
                        }
                        try { Marshal.ReleaseComObject(moniker); } catch { }
                    }

                    devices.Add(new WebcamDevice(idx, name));
                    idx++;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamDeviceEnumerator: enumeration threw");
            }
            finally
            {
                if (enumMoniker != null)
                {
                    try { Marshal.ReleaseComObject(enumMoniker); } catch { }
                }
                if (devEnum != null)
                {
                    try { Marshal.ReleaseComObject(devEnum); } catch { }
                }
            }

            return devices;
        }
    }
}
