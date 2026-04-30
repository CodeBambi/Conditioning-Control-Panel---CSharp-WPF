using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Deeper
{
    public class AudioWaveformResult
    {
        public float[] Peaks { get; set; } = Array.Empty<float>();
        public double DurationSeconds { get; set; }
    }

    /// <summary>
    /// Decodes audio files via NAudio's MediaFoundationReader and emits a peak-bucket
    /// summary suitable for timeline rendering. Caches the result to disk keyed on
    /// (path, size, last-write-time) so repeated opens of the same file are instant.
    /// </summary>
    public static class AudioWaveformCache
    {
        private const string CacheMagic = "DPK2";

        public static string CacheFolder => Path.Combine(App.UserDataPath, "deeper-cache");

        /// <summary>
        /// Loads peaks for the given audio file, using a disk cache when available.
        /// Throws on decode failure (callers should catch and fall back to a flat strip).
        /// </summary>
        public static async Task<AudioWaveformResult> LoadAsync(string audioPath)
        {
            return await Task.Run(() => Load(audioPath)).ConfigureAwait(false);
        }

        public static AudioWaveformResult Load(string audioPath)
        {
            if (!File.Exists(audioPath))
                throw new FileNotFoundException("Audio file not found", audioPath);

            try { Directory.CreateDirectory(CacheFolder); } catch { /* best-effort */ }

            var cacheKey = ComputeCacheKey(audioPath);
            var cachePath = Path.Combine(CacheFolder, cacheKey + ".peaks");

            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = ReadCache(cachePath);
                    if (cached != null) return cached;
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("AudioWaveformCache: cache read failed for {Path}: {Error}", audioPath, ex.Message);
                }
            }

            var fresh = Decode(audioPath);

            try { WriteCache(cachePath, fresh); }
            catch (Exception ex) { App.Logger?.Debug("AudioWaveformCache: cache write failed: {Error}", ex.Message); }

            return fresh;
        }

        private static AudioWaveformResult Decode(string audioPath)
        {
            using var reader = new MediaFoundationReader(audioPath);
            var channels = reader.WaveFormat.Channels;
            var sampleRate = reader.WaveFormat.SampleRate;
            var durationSec = reader.TotalTime.TotalSeconds;

            int peakCount = Math.Clamp((int)(durationSec * 6), 64, 4096);
            var peaks = new float[peakCount];

            if (durationSec <= 0 || sampleRate <= 0 || channels <= 0)
                return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };

            double framesPerBucket = (durationSec * sampleRate) / peakCount;
            if (framesPerBucket <= 0) framesPerBucket = 1;

            var sp = reader.ToSampleProvider();
            var buf = new float[8192];
            int currentBucket = 0;
            double frameInBucket = 0;
            float currentMax = 0;

            while (currentBucket < peakCount)
            {
                int read = sp.Read(buf, 0, buf.Length);
                if (read == 0) break;
                int frames = read / channels;
                for (int f = 0; f < frames; f++)
                {
                    float frameMax = 0;
                    int baseIx = f * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        var v = Math.Abs(buf[baseIx + c]);
                        if (v > frameMax) frameMax = v;
                    }
                    if (frameMax > currentMax) currentMax = frameMax;
                    frameInBucket++;
                    if (frameInBucket >= framesPerBucket)
                    {
                        peaks[currentBucket++] = currentMax;
                        currentMax = 0;
                        frameInBucket -= framesPerBucket;
                        if (currentBucket >= peakCount) break;
                    }
                }
            }
            while (currentBucket < peakCount) peaks[currentBucket++] = currentMax;

            return new AudioWaveformResult { Peaks = peaks, DurationSeconds = durationSec };
        }

        private static string ComputeCacheKey(string audioPath)
        {
            var info = new FileInfo(audioPath);
            var key = $"{audioPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            using var sha = SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static AudioWaveformResult? ReadCache(string cachePath)
        {
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs);
            var magic = new string(br.ReadChars(4));
            if (magic != CacheMagic) return null;
            var duration = br.ReadDouble();
            var count = br.ReadInt32();
            if (count <= 0 || count > 16384) return null;
            var peaks = new float[count];
            for (int i = 0; i < count; i++) peaks[i] = br.ReadSingle();
            return new AudioWaveformResult { Peaks = peaks, DurationSeconds = duration };
        }

        private static void WriteCache(string cachePath, AudioWaveformResult result)
        {
            using var fs = File.Create(cachePath);
            using var bw = new BinaryWriter(fs);
            bw.Write(CacheMagic.ToCharArray());
            bw.Write(result.DurationSeconds);
            bw.Write(result.Peaks.Length);
            foreach (var p in result.Peaks) bw.Write(p);
        }
    }
}
