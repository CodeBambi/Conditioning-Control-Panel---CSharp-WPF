using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Speech;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Windows microphone for the Core <see cref="SpeechEngine"/>, the twin of WPF SpeechService's NAudio
    /// <c>WaveInEvent</c> (16 kHz, 16-bit mono, 50 ms buffers, 3 of them) over the same winmm waveIn API,
    /// with no package. Device list = "System default" then every waveIn device by its product name; the
    /// saved NAME wins over the saved index (WPF ResolveDeviceNumber, #441b). Audio only lives in memory.
    /// <para>Deviation: "System default" opens WAVE_MAPPER (the OS default input) where WPF opened device 0.</para>
    /// ponytail: WPF's MicFrontEnd (gain/AGC) and the sherpa "Hey Bambi" spotter + Silero VAD are not ported
    /// (platform#16); Vosk closed grammar carries the wake word here as on Linux.
    /// </summary>
    internal sealed class WinMmMicSource : IMicSource
    {
        private const int SampleRate = 16000;
        private const int BufferBytes = SampleRate * 2 / 20;   // 50 ms of s16 mono = 1600 bytes
        private const int BufferCount = 3;
        private const uint WaveMapper = 0xFFFFFFFF;
        private const uint CallbackEvent = 0x00050000;
        private const uint WhdrDone = 0x00000001;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WaveInCaps
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
        private static extern int waveInGetDevCaps(UIntPtr id, ref WaveInCaps caps, uint size);
        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr hwi, uint id, ref WaveFormatEx fmt, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr hwi);

        private static readonly uint HdrSize = (uint)Marshal.SizeOf<WaveHdr>();

        /// <summary>waveIn devices the OS reports (0 = no microphone, or not Windows).</summary>
        internal static int DeviceCount
        {
            get
            {
                if (!OperatingSystem.IsWindows()) return 0;
                try { return (int)waveInGetNumDevs(); } catch { return 0; }
            }
        }

        private static string NameOf(int i)
        {
            try
            {
                var caps = new WaveInCaps();
                return waveInGetDevCaps((UIntPtr)(uint)i, ref caps, (uint)Marshal.SizeOf<WaveInCaps>()) == 0 ? caps.szPname ?? "" : "";
            }
            catch { return ""; }
        }

        public bool HasDevice => DeviceCount > 0;

        /// <summary>WPF SpeechService.EnumerateInputDevices: "System default" (-1), then each device.</summary>
        public IReadOnlyList<SpeechInputDevice> ListDevices()
        {
            var list = new List<SpeechInputDevice> { new(-1, "System default") };
            var n = DeviceCount;
            for (var i = 0; i < n; i++)
            {
                var name = NameOf(i);
                list.Add(new SpeechInputDevice(i, string.IsNullOrWhiteSpace(name) ? $"Device {i}" : name));
            }
            return list;
        }

        /// <summary>WPF ResolveDeviceNumber: saved name, then saved index, else the default.</summary>
        internal static uint ResolveDevice(int savedIndex, string? savedName)
        {
            var n = DeviceCount;
            if (!string.IsNullOrWhiteSpace(savedName))
                for (var i = 0; i < n; i++)
                    if (string.Equals(NameOf(i), savedName, StringComparison.OrdinalIgnoreCase)) return (uint)i;
            return savedIndex >= 0 && savedIndex < n ? (uint)savedIndex : WaveMapper;
        }

        public IDisposable Start(Action<byte[], int> onPcm)
        {
            var s = CoreSettings.Current;
            return new Session(ResolveDevice(s.SpeechInputDeviceIndex, s.SpeechInputDeviceName), onPcm);
        }

        private sealed class Session : IDisposable
        {
            private readonly IntPtr _hwi;
            private readonly IntPtr[] _hdrs = new IntPtr[BufferCount];
            private readonly AutoResetEvent _ready = new(false);
            private readonly Thread _reader;
            private volatile bool _stopping;
            private int _disposed;

            internal Session(uint device, Action<byte[], int> onPcm)
            {
                var fmt = new WaveFormatEx
                {
                    wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, nAvgBytesPerSec = SampleRate * 2,
                    nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0,
                };
                var rc = waveInOpen(out _hwi, device, ref fmt, _ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
                if (rc != 0) { _ready.Dispose(); throw new InvalidOperationException($"waveInOpen failed ({rc})"); }
                try
                {
                    for (var i = 0; i < BufferCount; i++)
                    {
                        var hdr = new WaveHdr { lpData = Marshal.AllocHGlobal(BufferBytes), dwBufferLength = BufferBytes };
                        _hdrs[i] = Marshal.AllocHGlobal((int)HdrSize);
                        Marshal.StructureToPtr(hdr, _hdrs[i], false);
                        Check(waveInPrepareHeader(_hwi, _hdrs[i], HdrSize), "prepare");
                        Check(waveInAddBuffer(_hwi, _hdrs[i], HdrSize), "add");
                    }
                    Check(waveInStart(_hwi), "start");
                }
                catch { Dispose(); throw; }

                _reader = new Thread(() => Pump(onPcm)) { IsBackground = true, Name = "WinMmMic" };
                _reader.Start();
            }

            private static void Check(int rc, string what)
            {
                if (rc != 0) throw new InvalidOperationException($"waveIn {what} failed ({rc})");
            }

            private void Pump(Action<byte[], int> onPcm)
            {
                while (!_stopping)
                {
                    _ready.WaitOne(200);
                    foreach (var p in _hdrs)
                    {
                        if (_stopping) return;
                        var hdr = Marshal.PtrToStructure<WaveHdr>(p);
                        if ((hdr.dwFlags & WhdrDone) == 0) continue;
                        var n = (int)hdr.dwBytesRecorded;
                        if (n > 0)
                        {
                            var buf = new byte[n];
                            Marshal.Copy(hdr.lpData, buf, 0, n);
                            try { onPcm(buf, n); }
                            catch (Exception ex) { Log.Debug(ex, "WinMmMicSource: consumer threw"); }
                        }
                        if (!_stopping) waveInAddBuffer(_hwi, p, HdrSize);   // back in the queue (stays prepared)
                    }
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _stopping = true;
                try { if (_hwi != IntPtr.Zero) waveInReset(_hwi); } catch { }
                try { _ready.Set(); } catch { }
                try { if (_reader is { IsAlive: true } && Thread.CurrentThread != _reader) _reader.Join(1000); } catch { }
                // A buffer the reader re-queued as the stop landed is pulled back before its memory goes.
                try { if (_hwi != IntPtr.Zero) waveInReset(_hwi); } catch { }
                foreach (var p in _hdrs)
                {
                    if (p == IntPtr.Zero) continue;
                    try
                    {
                        var hdr = Marshal.PtrToStructure<WaveHdr>(p);
                        waveInUnprepareHeader(_hwi, p, HdrSize);
                        if (hdr.lpData != IntPtr.Zero) Marshal.FreeHGlobal(hdr.lpData);
                    }
                    catch { }
                    Marshal.FreeHGlobal(p);
                }
                try { if (_hwi != IntPtr.Zero) waveInClose(_hwi); } catch { }
                _ready.Dispose();
            }
        }
    }
}
