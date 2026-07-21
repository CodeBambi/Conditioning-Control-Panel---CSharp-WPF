namespace CcpSpike.Audio;

/// <summary>
/// Synthetic test-tone generator. Produces 16-bit PCM mono WAVs with SAMPLE-EXACT durations
/// (frames = sampleRate * ms / 1000, asserted divisible) so expected duration is exact — the
/// completion-window tolerance is declared against this exactness (pre-approach consult item 7).
/// No copyrighted fixtures, no WPF asset copying (packet requirement).
/// </summary>
public static class ToneGen
{
    public const int SampleRate = 48000;

    /// <summary>Voice-style clip: 440 Hz with 20 ms fade in/out. 2500 ms = 120000 frames exact.</summary>
    public const int VoiceMs = 2500;

    /// <summary>SFX-style click: 2 kHz, 300 ms = 14400 frames exact. Long enough that all 8
    /// rapid triggers (30 ms spacing) are still alive when polled — a 120 ms clip finishes
    /// before the first poll of later triggers (measured artifact, Step-2 record).</summary>
    public const int SfxMs = 300;

    /// <summary>Whisper-style clip: 220 Hz, 1500 ms = 72000 frames exact.</summary>
    public const int WhisperMs = 1500;

    public static string EnsureTones(string dir)
    {
        Directory.CreateDirectory(dir);
        var voice = Path.Combine(dir, "voice-2500ms.wav");
        var sfx = Path.Combine(dir, "sfx-300ms.wav");
        var whisper = Path.Combine(dir, "whisper-1500ms.wav");
        if (!File.Exists(voice)) WriteWav(voice, VoiceMs, 440.0, fadeMs: 20);
        if (!File.Exists(sfx)) WriteWav(sfx, SfxMs, 2000.0, fadeMs: 5);        if (!File.Exists(whisper)) WriteWav(whisper, WhisperMs, 220.0, fadeMs: 50);
        return dir;
    }

    public static void WriteWav(string path, int durationMs, double freqHz, int fadeMs)
    {
        int frames = SampleRate * durationMs / 1000;
        if (frames * 1000 != SampleRate * durationMs)
            throw new ArgumentException($"duration {durationMs}ms is not sample-exact at {SampleRate}Hz");
        int fadeFrames = SampleRate * fadeMs / 1000;
        var pcm = new short[frames];
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / SampleRate;
            double env = 1.0;
            if (i < fadeFrames) env = (double)i / fadeFrames;
            else if (i > frames - fadeFrames) env = (double)(frames - i) / fadeFrames;
            pcm[i] = (short)(Math.Sin(2 * Math.PI * freqHz * t) * 0.6 * env * short.MaxValue);
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int dataLen = frames * 2;
        bw.Write("RIFF"u8);
        bw.Write(36 + dataLen);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);            // PCM fmt chunk
        bw.Write((short)1);      // PCM
        bw.Write((short)1);      // mono
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2); // byte rate
        bw.Write((short)2);      // block align
        bw.Write((short)16);     // bits
        bw.Write("data"u8);
        bw.Write(dataLen);
        foreach (var s in pcm) bw.Write(s);
    }

    /// <summary>Read a WAV produced by <see cref="WriteWav"/> back to raw PCM16 mono samples.</summary>
    public static short[] ReadWavPcm16(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Our writer emits a canonical 44-byte header.
        int dataLen = BitConverter.ToInt32(bytes, 40);
        var pcm = new short[dataLen / 2];
        Buffer.BlockCopy(bytes, 44, pcm, 0, dataLen);
        return pcm;
    }
}
