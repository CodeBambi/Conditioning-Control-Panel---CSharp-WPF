// PORTED from WPF 7.1.5 Services/Deeper/AudioWaveformCache.cs (hunt IB8, HB2): the peak strip the Deeper
// player and editor draw. WPF decoded with NAudio's MediaFoundationReader; this head has no NAudio, so the
// process's shared LibVLC transcodes the file to a small mono WAV (a sout chain has no clock: it runs as fast
// as the decoder goes) and the peaks are read from that. Same bucket rule (6 a second, 64..4096), same cache
// file (magic DPK2 under deeper-cache), keyed on the WHOLE path + size + last write time.
// Everything here runs off the UI thread and stops on the caller's token (the window closing).
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    internal sealed class DeeperWaveformResult
    {
        public float[] Peaks { get; init; } = Array.Empty<float>();
        public double DurationSeconds { get; init; }
    }

    internal static class DeeperWaveform
    {
        private const string CacheMagic = "DPK2";
        internal const int WavSampleRate = 8000;

        /// <summary>Tests point the cache at a temp folder.</summary>
        internal static string? CacheFolderOverride { get; set; }
        internal static string CacheFolder => CacheFolderOverride ?? Path.Combine(CorePaths.UserData, "deeper-cache");

        /// <summary>Source file -> 8 kHz mono 16-bit WAV at the second path. Throws when it cannot. Tests swap it.</summary>
        internal static Action<string, string, CancellationToken> ToWav { get; set; } = TranscodeToWav;

        /// <summary>The peaks for <paramref name="audioPath"/>, from the cache when the same file was seen
        /// before. Null (quietly) when the file is gone, libvlc is missing, the decode fails or the token fires.</summary>
        internal static Task<DeeperWaveformResult?> LoadAsync(string audioPath, CancellationToken ct) =>
            Task.Run(() =>
            {
                try { return Load(audioPath, ct); }
                catch (OperationCanceledException) { return null; }
                catch (Exception ex)
                {
                    Log.Debug("DeeperWaveform: decode failed for {Path}: {Error}", audioPath, ex.Message);
                    return null;
                }
            });

        internal static DeeperWaveformResult? Load(string audioPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)) return null;
            string? cachePath = null;
            try
            {
                cachePath = Path.Combine(CacheFolder, CacheKey(audioPath) + ".peaks");
                if (File.Exists(cachePath) && ReadCache(cachePath) is { } cached) return cached;
            }
            catch (Exception ex) { Log.Debug("DeeperWaveform: cache read failed: {Error}", ex.Message); }

            ct.ThrowIfCancellationRequested();
            string wav = Path.Combine(Path.GetTempPath(), "ccp-peaks-" + Guid.NewGuid().ToString("N") + ".wav");
            DeeperWaveformResult fresh;
            try
            {
                ToWav(audioPath, wav, ct);
                ct.ThrowIfCancellationRequested();
                using var fs = File.OpenRead(wav);
                fresh = PeaksFromWav(fs, ct);
            }
            finally
            {
                try { if (File.Exists(wav)) File.Delete(wav); } catch { }
            }
            if (cachePath != null)
            {
                try { Directory.CreateDirectory(CacheFolder); WriteCache(cachePath, fresh); }
                catch (Exception ex) { Log.Debug("DeeperWaveform: cache write failed: {Error}", ex.Message); }
            }
            return fresh;
        }

        /// <summary>WPF ComputeCacheKey: the whole path (never the bare name), the size and the last write time.</summary>
        internal static string CacheKey(string audioPath)
        {
            var info = new FileInfo(audioPath);
            var key = $"{Path.GetFullPath(audioPath).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>WPF Decode's bucket walk over a PCM WAV (8 or 16 bit, any channel count): the loudest
        /// frame of each bucket, 0..1. A header whose data size was never patched (a stopped muxer) reads to
        /// the end of the stream.</summary>
        internal static DeeperWaveformResult PeaksFromWav(Stream wav, CancellationToken ct = default)
        {
            using var br = new BinaryReader(wav, Encoding.ASCII, leaveOpen: true);
            if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a WAV file");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("not a WAV file");
            int channels = 0, rate = 0, bits = 0;
            long dataLength = -1;
            while (wav.Position + 8 <= wav.Length)
            {
                string id = new string(br.ReadChars(4));
                uint size = br.ReadUInt32();
                if (id == "fmt ")
                {
                    br.ReadUInt16();
                    channels = br.ReadUInt16();
                    rate = br.ReadInt32();
                    br.ReadInt32();
                    br.ReadUInt16();
                    bits = br.ReadUInt16();
                    long extra = (long)size - 16;
                    if (extra > 0) wav.Seek(extra, SeekOrigin.Current);
                }
                else if (id == "data")
                {
                    long left = wav.Length - wav.Position;
                    dataLength = size == 0 || size > left ? left : size;
                    break;
                }
                else wav.Seek(size + (size & 1), SeekOrigin.Current);
            }
            if (channels <= 0 || rate <= 0 || (bits != 8 && bits != 16) || dataLength < 0)
                throw new InvalidDataException("unsupported WAV format");

            int frameBytes = channels * bits / 8;
            long frames = dataLength / frameBytes;
            double duration = frames / (double)rate;
            int peakCount = Math.Clamp((int)(duration * 6), 64, 4096);
            var peaks = new float[peakCount];
            if (frames <= 0) return new DeeperWaveformResult { Peaks = peaks, DurationSeconds = duration };

            double framesPerBucket = Math.Max(1e-9, frames / (double)peakCount);
            var buf = new byte[frameBytes * 4096];
            int bucket = 0;
            double inBucket = 0;
            float max = 0;
            long done = 0;
            while (bucket < peakCount && done < frames)
            {
                ct.ThrowIfCancellationRequested();
                int want = (int)Math.Min(buf.Length, (frames - done) * frameBytes);
                int read = wav.Read(buf, 0, want);
                if (read < frameBytes) break;
                int got = read / frameBytes;
                for (int f = 0; f < got && bucket < peakCount; f++)
                {
                    float frameMax = 0;
                    int at = f * frameBytes;
                    for (int c = 0; c < channels; c++)
                    {
                        float v = bits == 16
                            ? Math.Abs((short)(buf[at + c * 2] | (buf[at + c * 2 + 1] << 8)) / 32768f)
                            : Math.Abs((buf[at + c] - 128) / 128f);
                        if (v > frameMax) frameMax = v;
                    }
                    if (frameMax > max) max = frameMax;
                    if (++inBucket >= framesPerBucket)
                    {
                        peaks[bucket++] = Math.Min(1f, max);
                        max = 0;
                        inBucket -= framesPerBucket;
                    }
                }
                done += got;
            }
            while (bucket < peakCount) { peaks[bucket++] = Math.Min(1f, max); max = 0; }
            return new DeeperWaveformResult { Peaks = peaks, DurationSeconds = duration };
        }

        internal static DeeperWaveformResult? ReadCache(string cachePath)
        {
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs);
            if (new string(br.ReadChars(4)) != CacheMagic) return null;
            var duration = br.ReadDouble();
            var count = br.ReadInt32();
            if (count <= 0 || count > 16384) return null;
            var peaks = new float[count];
            for (int i = 0; i < count; i++) peaks[i] = br.ReadSingle();
            return new DeeperWaveformResult { Peaks = peaks, DurationSeconds = duration };
        }

        internal static void WriteCache(string cachePath, DeeperWaveformResult result)
        {
            using var fs = File.Create(cachePath);
            using var bw = new BinaryWriter(fs);
            bw.Write(CacheMagic.ToCharArray());
            bw.Write(result.DurationSeconds);
            bw.Write(result.Peaks.Length);
            foreach (var p in result.Peaks) bw.Write(p);
        }

        /// <summary>The sout chain: no video, 8 kHz mono signed 16-bit, WAV. Forward slashes and an escaped
        /// quote, so a Windows path survives libvlc's option parser (RaceTrackPlayer.SoutFor's road).</summary>
        internal static string SoutFor(string dest) =>
            ":sout=#transcode{vcodec=none,acodec=s16l,channels=1,samplerate=" + WavSampleRate
            + "}:std{access=file,mux=wav,dst='" + dest.Replace('\\', '/').Replace("'", "\\'") + "'}";

        private static void TranscodeToWav(string source, string dest, CancellationToken ct)
        {
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException("libvlc is not loaded");
            using var done = new ManualResetEventSlim(false);
            bool failed = false;
            using var media = new Media(vlc, source, FromType.FromPath);
            media.AddOption(SoutFor(dest));
            media.AddOption(":no-sout-video");
            media.AddOption(":sout-keep");
            using var player = new MediaPlayer(media);
            player.EndReached += (_, _) => done.Set();
            player.EncounteredError += (_, _) => { failed = true; done.Set(); };
            if (!player.Play()) throw new InvalidDataException("the file would not open");
            try
            {
                // An hour of audio decodes in well under a minute; ten minutes is a wedged decoder.
                var limit = DateTime.UtcNow.AddMinutes(10);
                while (!done.Wait(100))
                {
                    ct.ThrowIfCancellationRequested();
                    if (DateTime.UtcNow > limit) throw new TimeoutException("the file took too long to decode");
                }
            }
            finally
            {
                // The muxer patches the WAV header and closes the file on stop. Never on a libvlc event thread:
                // this is the caller's worker.
                try { player.Stop(); } catch (Exception ex) { Log.Debug("DeeperWaveform: stop: {E}", ex.Message); }
            }
            if (failed) throw new InvalidDataException("the file would not decode");
        }
    }
}
