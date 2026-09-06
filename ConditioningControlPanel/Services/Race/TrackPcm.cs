using System;
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
/// Decodes an audio file to <see cref="TrackPcm"/> with NAudio: MediaFoundationReader first,
/// AudioFileReader as the fallback, then stereo to mono and a WDL resample to 16 kHz.
/// Filled in by PR c4.
/// </summary>
public static class TrackDecoder
{
    public static TrackPcm Decode(string path, IProgress<double>? progress, CancellationToken ct)
        => throw new NotImplementedException("PR c4: TrackDecoder.Decode");

    /// <summary>The cache key: SHA1 of the length prefix + the first 1 MiB, so a rename keeps its chart.</summary>
    public static string HashFile(string path)
        => throw new NotImplementedException("PR c4: TrackDecoder.HashFile");
}
