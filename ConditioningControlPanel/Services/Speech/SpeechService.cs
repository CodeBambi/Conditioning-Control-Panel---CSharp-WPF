using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>
    /// The Windows speech service: the Core <see cref="SpeechEngine"/> (Vosk, grammar, scoring,
    /// session guard) over an NAudio WaveIn microphone. Keeps the WPF-facing statics - device list,
    /// device resolution, model root - which are Windows capture details.
    /// </summary>
    public sealed class SpeechService : SpeechEngine
    {
        public SpeechService() : base(new NAudioMicSource(), new[] { ModelRoot }) { }

        /// <summary>Whether the OS reports at least one audio capture device.</summary>
        public static bool HasCaptureDevice
        {
            get
            {
                try { return WaveInEvent.DeviceCount > 0; }
                catch { return false; }
            }
        }

        /// <summary>A selectable microphone. Index -1 = the Windows default capture device.</summary>
        public readonly record struct InputDevice(int Index, string Name);

        /// <summary>
        /// Enumerate WaveIn capture devices for the mic picker, with friendly names from
        /// <see cref="WaveInEvent.GetCapabilities"/>. The first entry is always the Windows
        /// default (index -1); real devices follow in WaveIn order. The value stored in
        /// AppSettings.SpeechInputDeviceIndex is one of these <see cref="InputDevice.Index"/> values,
        /// and <see cref="ResolveDeviceNumber"/> consumes it on the next capture session.
        /// </summary>
        public static IReadOnlyList<InputDevice> EnumerateInputDevices()
        {
            var list = new List<InputDevice> { new(-1, "System default") };
            try
            {
                int count = WaveInEvent.DeviceCount;
                for (int i = 0; i < count; i++)
                {
                    string name;
                    try { name = WaveInEvent.GetCapabilities(i).ProductName; }
                    catch { name = ""; }
                    if (string.IsNullOrWhiteSpace(name)) name = $"Device {i}";
                    list.Add(new InputDevice(i, name));
                }
            }
            catch { }
            return list;
        }

        /// <summary>Directory we expect the Vosk model to live in (drop the unpacked model here).</summary>
        public static string ModelRoot =>
            Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "vosk");

        /// <summary>The model directory under <see cref="ModelRoot"/>, or null.</summary>
        internal static string? ResolveModelDir() => ResolveModelDir(ModelRoot);

        /// <summary>
        /// Resolve the WaveIn device number to open, preferring a match on the saved device NAME (robust
        /// to NAudio ordinal reshuffling when virtual audio devices appear/disappear — the "voice worked
        /// yesterday, not today" failure, #441b), then the saved ordinal if still valid, else 0 (Windows
        /// default). Never throws. Shared by <see cref="SherpaWakeService"/> so both wake engines agree.
        /// </summary>
        public static int ResolveDeviceNumber(int savedIndex, string? savedName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(savedName))
                {
                    int count = WaveInEvent.DeviceCount;
                    for (int i = 0; i < count; i++)
                    {
                        string name;
                        try { name = WaveInEvent.GetCapabilities(i).ProductName; }
                        catch { name = ""; }
                        if (string.Equals(name, savedName, StringComparison.OrdinalIgnoreCase))
                            return i;
                    }
                }
                if (savedIndex >= 0 && savedIndex < WaveInEvent.DeviceCount) return savedIndex;
            }
            catch { }
            return 0; // WaveIn device 0 == Windows default capture device.
        }

        /// <summary>WaveIn capture, 16 kHz mono s16, 50 ms buffers, on the saved device.</summary>
        private sealed class NAudioMicSource : IMicSource
        {
            public bool HasDevice => HasCaptureDevice;

            public IReadOnlyList<SpeechInputDevice> ListDevices()
            {
                var list = new List<SpeechInputDevice>();
                foreach (var d in EnumerateInputDevices()) list.Add(new SpeechInputDevice(d.Index, d.Name));
                return list;
            }

            public IDisposable Start(Action<byte[], int> onPcm)
            {
                var s = App.Settings?.Current;
                var mic = new WaveInEvent
                {
                    DeviceNumber = ResolveDeviceNumber(s?.SpeechInputDeviceIndex ?? -1, s?.SpeechInputDeviceName),
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 50
                };
                mic.DataAvailable += (_, e) => onPcm(e.Buffer, e.BytesRecorded);
                try { mic.StartRecording(); }
                catch { mic.Dispose(); throw; }
                return new Stop(mic);
            }

            private sealed class Stop(WaveInEvent mic) : IDisposable
            {
                public void Dispose()
                {
                    try { mic.StopRecording(); } catch { }
                    try { mic.Dispose(); } catch { }
                }
            }
        }
    }
}
