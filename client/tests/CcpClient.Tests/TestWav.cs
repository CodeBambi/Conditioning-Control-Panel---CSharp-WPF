namespace CcpClient.Tests;

/// <summary>
/// A real, decodable WAV on disk, generated rather than committed — the audio counterpart of
/// <see cref="TestPng"/>. Deliberately NOT silence: the whole point of the audio evidence chain is
/// that the OS's own peak meter reads back a non-zero sample level, and a silent asset would make
/// that fact pass vacuously (peak 0 == peak 0).
/// </summary>
internal static class TestWav
{
    internal const int SampleRate = 48000;
    internal const short Channels = 2;
    internal const short BitsPerSample = 16;

    /// <summary>Writes a 16-bit stereo PCM sine at <paramref name="hz"/> and full-ish scale.</summary>
    internal static string Write(string path, double seconds = 1.0, double hz = 440.0, double amplitude = 0.9)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var frames = (int)(SampleRate * seconds);
        var dataBytes = frames * Channels * (BitsPerSample / 8);

        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                 // PCM chunk size
        w.Write((short)1);           // PCM
        w.Write(Channels);
        w.Write(SampleRate);
        w.Write(SampleRate * Channels * (BitsPerSample / 8)); // byte rate
        w.Write((short)(Channels * (BitsPerSample / 8)));     // block align
        w.Write(BitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * hz * i / SampleRate));
            w.Write(sample);
            w.Write(sample);
        }

        return path;
    }
}
