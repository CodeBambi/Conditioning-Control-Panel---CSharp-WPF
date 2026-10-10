using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

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
/// Decodes an audio file to <see cref="TrackPcm"/>. WPF does it with NAudio (MediaFoundationReader,
/// then AudioFileReader, stereo to mono, a WDL resample to 16 kHz), which is Windows only. Here the
/// head's LibVLC transcodes the file to a 16 kHz mono 16-bit WAV (<see cref="ToWavProvider"/>, seeded
/// by Views/Games/RaceTrackPlayer) and this class reads that WAV; a file that already IS such a WAV
/// is read as it stands. The chart downstream sees the same shape either way.
/// </summary>
public static class TrackDecoder
{
    /// <summary>(source, destination WAV, progress 0..1, cancel): write the source as 16 kHz mono
    /// signed 16-bit PCM WAV. Throws on a file it cannot open. Seeded by the head; unseeded means
    /// this build has no decoder, and Decode says so.</summary>
    public static volatile Action<string, string, IProgress<double>?, CancellationToken>? ToWavProvider;

    /// <summary>Where a transcode waits while it is read. Emptied as it goes.</summary>
    private static string DecodeTempRoot => System.IO.Path.Combine(CorePaths.UserData, "race", "decode");

    /// <summary>
    /// Decodes <paramref name="path"/> to mono 16 kHz float PCM. Progress is 0..1. Cancellation
    /// throws <see cref="OperationCanceledException"/>.
    /// </summary>
    public static TrackPcm Decode(string path, IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Track file not found", path ?? "");

        float[]? pcm = TryReadWav16kMono(path, ct);
        if (pcm == null)
        {
            var toWav = ToWavProvider ?? throw new InvalidOperationException("this build cannot decode audio");
            Directory.CreateDirectory(DecodeTempRoot);
            string temp = System.IO.Path.Combine(DecodeTempRoot, "decode-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                toWav(path, temp, progress, ct);
                ct.ThrowIfCancellationRequested();
                pcm = TryReadWav16kMono(temp, ct)
                    ?? throw new InvalidDataException("the track would not decode");
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception ex) { Serilog.Log.Debug("race-chart: decode temp delete: {E}", ex.Message); }
            }
        }
        if (pcm.Length == 0) throw new InvalidDataException("the track has no audio in it");
        progress?.Report(1);

        return new TrackPcm
        {
            Mono16k = pcm,
            DurationSec = pcm.Length / (double)TrackPcm.SampleRate,
            Hash = HashFile(path),
            Name = System.IO.Path.GetFileName(path),
            Path = path
        };
    }

    /// <summary>
    /// A RIFF WAV that is exactly 16 kHz, mono, 16-bit PCM, as floats; null for anything else (the
    /// caller then transcodes). A data chunk whose length was never patched in (a muxer that could
    /// not seek back) is read to the end of the file.
    /// </summary>
    internal static float[]? TryReadWav16kMono(string path, CancellationToken ct)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var r = new BinaryReader(file);
            if (file.Length < 44) return null;
            if (new string(r.ReadChars(4)) != "RIFF") return null;
            r.ReadUInt32();
            if (new string(r.ReadChars(4)) != "WAVE") return null;
            bool fmtOk = false;
            while (file.Position + 8 <= file.Length)
            {
                string id = new string(r.ReadChars(4));
                long size = r.ReadUInt32();
                long body = file.Position;
                if (id == "fmt ")
                {
                    if (size < 16) return null;
                    int format = r.ReadUInt16();
                    int channels = r.ReadUInt16();
                    int rate = r.ReadInt32();
                    r.ReadInt32(); r.ReadUInt16();
                    int bits = r.ReadUInt16();
                    if (format != 1 || channels != 1 || rate != TrackPcm.SampleRate || bits != 16) return null;
                    fmtOk = true;
                }
                else if (id == "data")
                {
                    if (!fmtOk) return null;
                    long left = file.Length - body;
                    if (size <= 0 || size > left) size = left;
                    int count = (int)Math.Min(int.MaxValue / 2, size / 2);
                    var pcm = new float[count];
                    var block = new byte[64 * 1024];
                    int at = 0;
                    while (at < count)
                    {
                        ct.ThrowIfCancellationRequested();
                        int want = (int)Math.Min(block.Length, (long)(count - at) * 2);
                        int got = file.Read(block, 0, want);
                        if (got < 2) break;
                        for (int i = 0; i + 1 < got; i += 2)
                            pcm[at++] = (short)(block[i] | (block[i + 1] << 8)) / 32768f;
                    }
                    return at == count ? pcm : pcm[..at];
                }
                file.Position = body + size + (size & 1);
            }
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Serilog.Log.Debug("race-chart: {Name} is not a plain wav ({E})", System.IO.Path.GetFileName(path), ex.Message);
            return null;
        }
    }

    /// <summary>CHART.md: the hash reads the first 1 MiB and nothing else.</summary>
    public const int HashHead = 1024 * 1024;

    /// <summary>The cache key: SHA1 of the length prefix + the first 1 MiB, so a rename keeps its chart.</summary>
    public static string HashFile(string path)
    {
        long length = new FileInfo(path).Length;
        var head = new byte[HashHead];
        int filled = 0;
        using (var file = File.OpenRead(path))
        {
            int got;
            while (filled < head.Length && (got = file.Read(head, filled, head.Length - filled)) > 0)
                filled += got;
        }
        return HashBytes(length, filled == head.Length ? head : head[..filled]);
    }

    /// <summary>
    /// The same number as <see cref="HashFile"/> from a length and the head bytes on their own, for
    /// the caller that has those without having the file: the cloud path asks the CDN for a length
    /// and one megabyte so an authored or already charted track costs no download. Pass at most the
    /// first <see cref="HashHead"/> bytes, and fewer when the file is shorter than that.
    /// </summary>
    public static string HashBytes(long length, byte[] head)
    {
        if (head == null) throw new ArgumentNullException(nameof(head));
        int count = Math.Min(head.Length, HashHead);

        var prefix = BitConverter.GetBytes(length);
        if (!BitConverter.IsLittleEndian) Array.Reverse(prefix);

        using var sha = SHA1.Create();
        sha.TransformBlock(prefix, 0, prefix.Length, null, 0);
        sha.TransformFinalBlock(head, 0, count);
        return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>()).ToLowerInvariant();
    }
}
