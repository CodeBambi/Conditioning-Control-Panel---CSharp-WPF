// One-shot utility that synthesizes the four bundled Awareness Engine preset
// sound effects using NAudio. Run once during implementation; the resulting
// .wav files are committed under
//   ConditioningControlPanel/Resources/AwarenessPresets/audio/
// and loaded at runtime via KeywordTriggerService.ResolveAudioPath.
//
// Usage:
//   dotnet run --project Tools/GenerateAwarenessSounds
//
// Output: clicker.wav, lock-click.wav, chime.wav, bell.wav
// Format: 16-bit PCM, 22050 Hz mono — all four clips stay well under 30 KB.

using System;
using System.IO;
using NAudio.Wave;

namespace GenerateAwarenessSounds;

internal static class Program
{
    private const int SampleRate = 22050;

    // Output lives next to the preset JSONs so existing csproj content globs
    // pick it up without any project file changes.
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "ConditioningControlPanel", "Resources", "AwarenessPresets", "audio");

    public static int Main()
    {
        Directory.CreateDirectory(OutputDir);

        WriteWav("clicker.wav", GenerateClicker());
        WriteWav("lock-click.wav", GenerateLockClick());
        WriteWav("chime.wav", GenerateChime());
        WriteWav("bell.wav", GenerateBell());

        Console.WriteLine();
        Console.WriteLine("Done. Files written to:");
        Console.WriteLine("  " + Path.GetFullPath(OutputDir));
        return 0;
    }

    // ---- Clip 1: clicker.wav ----------------------------------------------
    // Soft clicker-trainer cue. ~40ms of brown noise with a fast exponential
    // decay envelope. Used by the Puppy Pet preset on "good boy" / "good girl"
    // / "good pup" / "sit" / "stay" / "heel".
    private static float[] GenerateClicker()
    {
        const double durationSec = 0.040;
        int n = (int)(SampleRate * durationSec);
        var samples = new float[n];

        // Brown noise (integrated white noise) for a body-ful click.
        var rng = new Random(1);
        float running = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            running = (running + 0.12f * white) * 0.97f;
            samples[i] = running;
        }

        // Fast attack, fast exp decay envelope.
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double attack = Math.Min(1.0, t / 0.02);
            double decay = Math.Exp(-t * 12.0);
            samples[i] = (float)(samples[i] * attack * decay * 4.0);
        }

        Normalize(samples, 0.85f);
        return samples;
    }

    // ---- Clip 2: lock-click.wav -------------------------------------------
    // Metallic two-burst lock engaging sound. Two short band-limited noise
    // bursts, each ~18ms, separated by ~20ms. Used by Chastity Watcher.
    private static float[] GenerateLockClick()
    {
        const double durationSec = 0.080;
        int n = (int)(SampleRate * durationSec);
        var samples = new float[n];

        int burst1Start = 0;
        int burst1End = (int)(0.018 * SampleRate);
        int burst2Start = (int)(0.040 * SampleRate);
        int burst2End = (int)(0.058 * SampleRate);

        var rng = new Random(2);

        // Simple one-pole high-pass on white noise to get a "tinny" metallic
        // timbre. We also add a small resonance at ~3kHz via a ring modulation.
        float hpPrev = 0f;
        float hpState = 0f;
        for (int i = 0; i < n; i++)
        {
            float white = (float)(rng.NextDouble() * 2 - 1);
            // High-pass: y[n] = 0.95 * (y[n-1] + x[n] - x[n-1])
            hpState = 0.95f * (hpState + white - hpPrev);
            hpPrev = white;

            // Ring-mod with 3 kHz sine to color the noise.
            float ringMod = (float)Math.Sin(2 * Math.PI * 3000.0 * i / SampleRate);
            float shaped = hpState * (0.6f + 0.4f * ringMod);

            bool inBurst = (i >= burst1Start && i < burst1End)
                        || (i >= burst2Start && i < burst2End);

            if (!inBurst)
            {
                samples[i] = 0f;
                continue;
            }

            // Per-burst local envelope (fast attack, fast decay).
            int burstStart = i < burst2Start ? burst1Start : burst2Start;
            int burstEnd = i < burst2Start ? burst1End : burst2End;
            double local = (double)(i - burstStart) / (burstEnd - burstStart);
            double env = Math.Exp(-local * 5.0) * Math.Min(1.0, local / 0.1);

            // Second burst a bit quieter than the first.
            double gain = i < burst2Start ? 1.0 : 0.75;

            samples[i] = (float)(shaped * env * gain);
        }

        Normalize(samples, 0.85f);
        return samples;
    }

    // ---- Clip 3: chime.wav ------------------------------------------------
    // Airy 300ms chime: a sine fifth stacked with its octave, with a slow
    // exponential decay. Used by Bimbo Reinforcement on "smart" / "focus" /
    // "think" / "intelligent" / "work".
    private static float[] GenerateChime()
    {
        const double durationSec = 0.300;
        int n = (int)(SampleRate * durationSec);
        var samples = new float[n];

        // C5 + G5 + C6 — open, airy, non-threatening.
        double[] freqs = { 523.25, 783.99, 1046.50 };
        double[] amps  = { 0.50,   0.40,   0.25   };

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double s = 0;
            for (int k = 0; k < freqs.Length; k++)
                s += amps[k] * Math.Sin(2 * Math.PI * freqs[k] * t);

            // Attack over ~5ms, decay exp over rest.
            double tNorm = (double)i / n;
            double attack = Math.Min(1.0, tNorm / 0.017);
            double decay = Math.Exp(-tNorm * 4.0);
            samples[i] = (float)(s * attack * decay);
        }

        Normalize(samples, 0.80f);
        return samples;
    }

    // ---- Clip 4: bell.wav -------------------------------------------------
    // 500ms soft singing-bowl style bell. 220Hz fundamental with a subtle 3rd
    // harmonic and very slow decay. Used by Trance Induction on "relax" /
    // "deeper" / "sleep" / "drop" / "breathe" / "focus on my voice".
    private static float[] GenerateBell()
    {
        const double durationSec = 0.500;
        int n = (int)(SampleRate * durationSec);
        var samples = new float[n];

        double fundamental = 220.0;
        // Slightly inharmonic partials give a metallic singing-bowl timbre.
        double[] partials = { 1.0, 2.005, 3.01 };
        double[] amps     = { 0.60, 0.30, 0.15 };
        // Higher partials decay faster than the fundamental.
        double[] decays   = { 2.0, 3.5, 6.0 };

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double tNorm = (double)i / n;
            double s = 0;
            for (int k = 0; k < partials.Length; k++)
            {
                s += amps[k]
                     * Math.Sin(2 * Math.PI * fundamental * partials[k] * t)
                     * Math.Exp(-tNorm * decays[k]);
            }

            double attack = Math.Min(1.0, tNorm / 0.01);
            samples[i] = (float)(s * attack);
        }

        Normalize(samples, 0.80f);
        return samples;
    }

    // ---- Helpers -----------------------------------------------------------

    private static void Normalize(float[] samples, float targetPeak)
    {
        float peak = 0f;
        foreach (var s in samples)
        {
            float abs = Math.Abs(s);
            if (abs > peak) peak = abs;
        }
        if (peak < 1e-6f) return;
        float scale = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= scale;
    }

    private static void WriteWav(string fileName, float[] samples)
    {
        var path = Path.Combine(OutputDir, fileName);
        using var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 1));
        // NAudio converts float [-1,1] -> 16-bit PCM under the hood.
        writer.WriteSamples(samples, 0, samples.Length);
        var info = new FileInfo(path);
        Console.WriteLine($"  {fileName,-16} {samples.Length,6} samples  {info.Length,5} bytes");
    }
}
