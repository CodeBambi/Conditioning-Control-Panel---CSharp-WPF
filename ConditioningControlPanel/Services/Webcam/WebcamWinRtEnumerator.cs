using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Enumerates video-capture devices via WinRT <see cref="DeviceInformation"/>,
    /// which is backed by the same Media Foundation / Frame Server source list that
    /// OpenCV's MSMF backend opens by index. Unlike the legacy DirectShow
    /// SystemDeviceEnum path (<see cref="WebcamDeviceEnumerator"/>), this is
    /// architecture-neutral: a 64-bit process misses cameras that register only
    /// 32-bit DirectShow filters or are Media-Foundation-only, so DirectShow returns
    /// an empty list even though Discord / Windows Camera / OpenCV-MSMF open the
    /// device fine. This enumerator catches those (#282 / #279 / #291) and is used as
    /// a fallback when the DirectShow list comes back empty.
    ///
    /// Index is the position in WinRT's enumeration order, which lines up with the
    /// MSMF index OpenCV uses to open — the same "usually identical, not guaranteed"
    /// caveat as the DirectShow path, which is why the open path logs the configured
    /// index AND the device name so a mismatch is visible.
    /// </summary>
    public static class WebcamWinRtEnumerator
    {
        /// <summary>
        /// The forgiving form, used by the device picker: a throw or a timeout is logged and read
        /// as "no cameras". See <see cref="EnumerateStrict"/> for the one caller that must tell an
        /// empty machine apart from a failed count.
        /// </summary>
        public static IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice> Enumerate()
        {
            try
            {
                return EnumerateStrict();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "WebcamWinRtEnumerator: enumeration threw");
                return new List<WebcamDeviceEnumerator.WebcamDevice>();
            }
        }

        /// <summary>
        /// The same enumeration with the swallow taken off: a wedged device-enumeration service
        /// (the timeout) or a WinRT failure THROWS, so only a clean run that found nothing returns
        /// empty. The quest hardware gate is the caller that needs that difference.
        /// </summary>
        public static IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice> EnumerateStrict()
        {
            var devices = new List<WebcamDeviceEnumerator.WebcamDevice>();

            // Run the async enumeration on a thread-pool thread and block the
            // caller with a timeout. FindAllAsync completes off the caller's
            // context so this can't deadlock the UI thread, and the timeout
            // guards against a wedged device-enumeration service.
            var task = Task.Run(async () =>
                await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture));

            if (!task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("WebcamWinRtEnumerator: FindAllAsync timed out");

            var collection = task.Result;
            int idx = 0;
            foreach (var di in collection)
            {
                var name = string.IsNullOrWhiteSpace(di.Name) ? "(unnamed device)" : di.Name;
                devices.Add(new WebcamDeviceEnumerator.WebcamDevice(idx, name));
                idx++;
            }
            App.Logger?.Information("WebcamWinRtEnumerator: {Count} video-capture device(s) via WinRT", devices.Count);
            return devices;
        }
    }
}
