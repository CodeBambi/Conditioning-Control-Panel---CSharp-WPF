using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ConditioningControlPanel.Services.Race;

/// <summary>A decoded track: mono 16 kHz float PCM plus what the chart needs to name and key it.</summary>
public sealed class TrackPcm
{
    public const int SampleRate = 16000;

    public float[] Mono16k { get; init; } = Array.Empty<float>();
    public double DurationSec { get; init; }
    /// <summary>SHA1 hex of the file length (8 bytes LE) + the first 1 MiB. See CHART.md.</summary>
    public string Hash { get; init; } = "";
    /// <summary>The file name without its directory.</summary>
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
}

/// <summary>
/// Decodes an audio file to <see cref="TrackPcm"/> with NAudio: MediaFoundationReader first,
/// AudioFileReader as the fallback, then stereo to mono and a WDL resample to 16 kHz.
/// </summary>
public static class TrackDecoder
{
    /// <summary>Samples pulled per block; big enough that the read loop is not the cost.</summary>
    private const int BlockSamples = 64 * 1024;

    /// <summary>
    /// Decodes <paramref name="path"/> to mono 16 kHz float PCM. Progress is bytes decoded over the
    /// reader's length, 0..1. Cancellation is checked between blocks and throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    public static TrackPcm Decode(string path, IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Track file not found", path ?? "");

        // Media Foundation reads mp3 / m4a / wav / wma out of the box. Formats it refuses (or a
        // machine with the codec pack stripped) fall through to NAudio's own readers.
        WaveStream reader;
        try
        {
            reader = new MediaFoundationReader(path);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("race-chart: MediaFoundation could not open {Name} ({Message}), using AudioFileReader",
                System.IO.Path.GetFileName(path), ex.Message);
            reader = new AudioFileReader(path);
        }

        using (reader)
        {
            ISampleProvider samples = reader.ToSampleProvider();
            if (samples.WaveFormat.Channels > 1) samples = ToMono(samples);
            if (samples.WaveFormat.SampleRate != TrackPcm.SampleRate)
                samples = new WdlResamplingSampleProvider(samples, TrackPcm.SampleRate);

            long streamLength = 0;
            try { streamLength = reader.Length; }
            catch { /* some readers refuse a length; progress then simply never moves */ }

            var block = new float[BlockSamples];
            var mono = new List<float>(EstimateSamples(reader));
            int read;
            while ((read = samples.Read(block, 0, block.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < read; i++) mono.Add(block[i]);
                if (progress != null && streamLength > 0)
                {
                    long pos = 0;
                    try { pos = reader.Position; } catch { }
                    progress.Report(Math.Clamp(pos / (double)streamLength, 0, 1));
                }
            }
            progress?.Report(1);

            var pcm = mono.ToArray();
            return new TrackPcm
            {
                Mono16k = pcm,
                DurationSec = pcm.Length / (double)TrackPcm.SampleRate,
                Hash = HashFile(path),
                Name = System.IO.Path.GetFileName(path),
                Path = path
            };
        }
    }

    /// <summary>The cache key: SHA1 of the length prefix + the first 1 MiB, so a rename keeps its chart.</summary>
    public static string HashFile(string path)
    {
        long length = new FileInfo(path).Length;
        var head = new byte[1024 * 1024];
        int filled = 0;
        using (var file = File.OpenRead(path))
        {
            int got;
            while (filled < head.Length && (got = file.Read(head, filled, head.Length - filled)) > 0)
                filled += got;
        }

        var prefix = BitConverter.GetBytes(length);
        if (!BitConverter.IsLittleEndian) Array.Reverse(prefix);

        using var sha = SHA1.Create();
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
        sha.TransformFinalBlock(head, 0, filled);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }

    /// <summary>Stereo goes through NAudio's averaging provider; anything wider gets the downmix below.</summary>
    private static ISampleProvider ToMono(ISampleProvider source)
        => source.WaveFormat.Channels == 2
            ? new StereoToMonoSampleProvider(source)
            : new DownmixSampleProvider(source);

    /// <summary>A capacity hint so the sample list does not regrow a hundred times on a long file.</summary>
    private static int EstimateSamples(WaveStream reader)
    {
        try
        {
            double sec = reader.TotalTime.TotalSeconds;
            if (sec > 0 && sec < 24 * 3600) return (int)(sec * TrackPcm.SampleRate) + TrackPcm.SampleRate;
        }
        catch { /* an unknown length is not worth an exception; fall through to the default */ }
        return TrackPcm.SampleRate * 60;
    }

    /// <summary>
    /// Averages any channel count down to mono. StereoToMonoSampleProvider only takes two channels,
    /// and a 5.1 m4a would throw on the way in.
    /// </summary>
    private sealed class DownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _channels;
        private float[] _frames = Array.Empty<float>();

        public DownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _channels = Math.Max(1, source.WaveFormat.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int wanted = count * _channels;
            if (_frames.Length < wanted) _frames = new float[wanted];
            int read = _source.Read(_frames, 0, wanted);
            int frames = read / _channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < _channels; c++) sum += _frames[f * _channels + c];
                buffer[offset + f] = sum / _channels;
            }
            return frames;
        }
    }
}
