using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Services.Speech;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Linux microphone for the Core <see cref="SpeechEngine"/>: a <c>parec</c> subprocess (PulseAudio
    /// or pipewire-pulse) writing 16 kHz mono s16le to stdout, read in 50 ms chunks; <c>pw-record</c>
    /// when parec is missing. Audio only ever lives in the pipe and in memory. If the app dies, the
    /// recorder takes SIGPIPE on its next write; Stop kills it anyway.
    /// </summary>
    internal sealed class PulseMicSource : IMicSource
    {
        /// <summary>The seeded engine (null until <see cref="Seed"/> ran on Linux).</summary>
        internal static SpeechEngine? Speech { get; private set; }

        private readonly string? _sourceOverride;

        /// <param name="sourceOverride">Record from this Pulse source instead of the saved mic
        /// (the capture check points it at a null sink's monitor, never at a real microphone).</param>
        internal PulseMicSource(string? sourceOverride = null) => _sourceOverride = sourceOverride;

        /// <summary>Seed <see cref="CoreSpeech"/> from one engine, as WPF App.xaml.cs:519 does.</summary>
        internal static void Seed()
        {
            if (!OperatingSystem.IsLinux()) return; // ponytail: Windows Avalonia has no mic source yet; reuse WPF's NAudio one when it gets one
            var mic = new PulseMicSource();
            var engine = new SpeechEngine(mic, SpeechEngine.DefaultModelRoots);
            Speech = engine;
            CoreSpeech.IsAvailableProvider = () => engine.IsAvailable;
            CoreSpeech.HasCaptureDeviceProvider = () => mic.HasDevice;
            CoreSpeech.ModelStatusProvider = () => engine.ModelStatus switch
            {
                SpeechModelStatus.Ok => CoreSpeechModelStatus.Ok,
                SpeechModelStatus.NoModelFound => CoreSpeechModelStatus.NoModelFound,
                SpeechModelStatus.LoadFailed => CoreSpeechModelStatus.LoadFailed,
                _ => CoreSpeechModelStatus.NotProbed,
            };
            CoreSpeech.EnumerateInputDevicesProvider = mic.ListDevices;
        }

        public bool HasDevice => ListDevices().Count > 1;

        /// <summary>"System default" (-1), then every Pulse source that is not a sink monitor, by
        /// its source name. The picker saves index + name; <see cref="Start"/> matches by name first.</summary>
        public IReadOnlyList<SpeechInputDevice> ListDevices()
        {
            try { return ParseSources(LibVlcAudio.Pactl("list short sources")); }
            catch { return Array.Empty<SpeechInputDevice>(); }
        }

        internal static IReadOnlyList<SpeechInputDevice> ParseSources(string pactlShortSources)
        {
            var list = new List<SpeechInputDevice> { new(-1, "System default") };
            foreach (var line in pactlShortSources.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var cols = line.Split('\t');
                if (cols.Length < 2 || cols[1].EndsWith(".monitor", StringComparison.Ordinal)) continue;
                list.Add(new SpeechInputDevice(list.Count - 1, cols[1]));
            }
            return list;
        }

        /// <summary>WPF's ResolveDeviceNumber: saved name, then saved index, else the default (null).</summary>
        private string? ResolveSource()
        {
            if (!string.IsNullOrWhiteSpace(_sourceOverride)) return _sourceOverride;
            var s = CoreSettings.Current;
            var devices = ListDevices().Where(d => d.Index >= 0).ToList();
            return devices.FirstOrDefault(d => string.Equals(d.Name, s.SpeechInputDeviceName, StringComparison.OrdinalIgnoreCase)).Name
                ?? devices.FirstOrDefault(d => d.Index == s.SpeechInputDeviceIndex).Name;
        }

        public IDisposable Start(Action<byte[], int> onPcm)
        {
            var src = ResolveSource();
            var psi = OnPath("parec")
                ? new ProcessStartInfo("parec", new[] { "--raw", "--format=s16le", "--rate=16000", "--channels=1", "--latency-msec=50" })
                : new ProcessStartInfo("pw-record", new[] { "--raw", "--format=s16", "--rate=16000", "--channels=1" });
            if (src != null) psi.ArgumentList.Add(psi.FileName == "parec" ? $"--device={src}" : $"--target={src}");
            if (psi.FileName == "pw-record") psi.ArgumentList.Add("-"); // raw to stdout, checked on pipewire 1.6.9
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            var p = Process.Start(psi) ?? throw new InvalidOperationException($"{psi.FileName} did not start");
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            var reader = new Thread(() =>
            {
                var stdout = p.StandardOutput.BaseStream;
                try
                {
                    while (true)
                    {
                        var buf = new byte[1600]; // 50 ms of 16 kHz s16 mono
                        int n = 0, r;
                        while (n < buf.Length && (r = stdout.Read(buf, n, buf.Length - n)) > 0) n += r;
                        if (n == 0) return;
                        onPcm(buf, n);
                        if (n < buf.Length) return;
                    }
                }
                catch (Exception ex) { Log.Debug("PulseMicSource: capture ended: {E}", ex.Message); }
            }) { IsBackground = true, Name = "mic-" + psi.FileName };
            reader.Start();
            Log.Information("PulseMicSource: {Tool} capturing from {Source}", psi.FileName, src ?? "default");
            return new Stop(p);
        }

        private static bool OnPath(string tool) =>
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
                .Any(d => d.Length > 0 && File.Exists(Path.Combine(d, tool)));

        private sealed class Stop(Process p) : IDisposable
        {
            public void Dispose()
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                try { p.WaitForExit(2000); } catch { }
                p.Dispose();
            }
        }
    }
}
