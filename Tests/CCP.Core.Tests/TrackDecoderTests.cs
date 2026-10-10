using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The race's decoder on this head: Core reads the 16 kHz mono WAV the head's LibVLC writes
/// (WPF decodes with NAudio), and the CHART.md hash is the same number either way.</summary>
public class TrackDecoderTests
{
    internal static string WriteWav(int sampleRate, int channels, double seconds, string? path = null)
    {
        path ??= Path.Combine(Path.GetTempPath(), "ccp-race-" + Guid.NewGuid().ToString("N") + ".wav");
        int frames = (int)(sampleRate * seconds);
        int dataBytes = frames * channels * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
        w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);
        var rng = new Random();
        for (int i = 0; i < frames; i++)
        {
            // A tone that swells every two seconds, with a little noise so no two files hash alike.
            double t = i / (double)sampleRate;
            double env = 0.15 + 0.8 * Math.Pow(Math.Sin(Math.PI * t / 2.0), 2);
            short s = (short)(env * 20000 * Math.Sin(2 * Math.PI * 220 * t) + rng.Next(-200, 200));
            for (int c = 0; c < channels; c++) w.Write(s);
        }
        return path;
    }

    [Fact]
    public void A16kMonoWavIsReadAsItStands()
    {
        var path = WriteWav(16000, 1, 3);
        try
        {
            var pcm = TrackDecoder.Decode(path, null, CancellationToken.None);
            Assert.Equal(48000, pcm.Mono16k.Length);
            Assert.Equal(3.0, pcm.DurationSec, 3);
            Assert.Equal(TrackDecoder.HashFile(path), pcm.Hash);
            Assert.Equal(Path.GetFileName(path), pcm.Name);
            Assert.Contains(pcm.Mono16k, v => Math.Abs(v) > 0.2f);
            Assert.All(pcm.Mono16k, v => Assert.InRange(v, -1f, 1f));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnyOtherShapeGoesToTheHeadsTranscoder_AndItsFailureIsTheError()
    {
        var path = WriteWav(44100, 2, 1);
        var old = TrackDecoder.ToWavProvider;
        try
        {
            Assert.Null(TrackDecoder.TryReadWav16kMono(path, CancellationToken.None));

            TrackDecoder.ToWavProvider = null;
            Assert.Throws<InvalidOperationException>(() => TrackDecoder.Decode(path, null, CancellationToken.None));

            // The head writes the WAV; Core reads it and deletes the temp file.
            string? temp = null;
            TrackDecoder.ToWavProvider = (src, dest, progress, ct) => { temp = dest; WriteWav(16000, 1, 2, dest); };
            var pcm = TrackDecoder.Decode(path, null, CancellationToken.None);
            Assert.Equal(32000, pcm.Mono16k.Length);
            Assert.NotNull(temp);
            Assert.False(File.Exists(temp));

            TrackDecoder.ToWavProvider = (_, _, _, _) => throw new InvalidDataException("the track would not decode");
            Assert.Throws<InvalidDataException>(() => TrackDecoder.Decode(path, null, CancellationToken.None));
        }
        finally { TrackDecoder.ToWavProvider = old; File.Delete(path); }
    }

    [Fact]
    public void HashBytesMatchesHashFile_AndAMissingFileIsNamed()
    {
        var path = WriteWav(16000, 1, 1);
        try
        {
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(TrackDecoder.HashFile(path), TrackDecoder.HashBytes(bytes.Length, bytes));
        }
        finally { File.Delete(path); }
        Assert.Throws<FileNotFoundException>(() => TrackDecoder.Decode(path, null, CancellationToken.None));
    }

    [Fact]
    public void TheEnergyPassChartsADecodedFile()
    {
        var path = WriteWav(16000, 1, 30);
        try
        {
            var pcm = TrackDecoder.Decode(path, null, CancellationToken.None);
            var chart = TrackAnalyzer.Energy(pcm, null, CancellationToken.None);
            Assert.Equal(pcm.Hash, chart.Source.Hash);
            Assert.Equal(30.0, chart.Source.DurationSec, 1);
            Assert.NotEmpty(chart.Acts);
        }
        finally { File.Delete(path); }
    }
}
