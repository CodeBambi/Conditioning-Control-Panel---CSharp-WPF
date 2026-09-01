using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// BLIPESE, the seam. The bubble talks to this and never has to know whether there is an audio
/// stack under it (and a test can hand it a recorder instead).
/// </summary>
public interface IEmiVox : IDisposable
{
    /// <summary>Babble one landed line. False when nothing was played.</summary>
    bool Speak(string? text, string? mood);

    /// <summary>One typing tick, for a <c>.</c> / <c>..</c> / <c>...</c> bubble frame.</summary>
    bool Tick();

    /// <summary>The bubble cleared: cut whatever is still sounding.</summary>
    void Stop();
}

/// <summary>
/// THE SOUND SHE MAKES WHILE A LINE IS ON SCREEN, ported from the Arcademy's
/// <c>Resources/web/arcademy/emi/vox.js</c> plus the two recipes its cues resolve to in
/// <c>Resources/web/arcademy/shell/audio.js</c> (<c>emi_blip</c> and <c>emi_tick</c>).
///
/// <para>THE PORT ANSWERS ONE QUESTION FIRST: the web voice is SYNTHESIS, not files. There is no
/// blip.wav anywhere in the Arcademy. <c>vox.js</c> computes a score of {atMs, pitch, gain} and
/// WebAudio builds every blip out of a triangle oscillator gliding 760 Hz down to 680 Hz across
/// 56 ms. So there was nothing to copy across and the synthesis had to come with it: everything
/// below the score is that oscillator, rendered offline.</para>
///
/// <para>ONE BUFFER PER LINE, NEVER ONE DEVICE PER BLIP. A thirteen-blip burst through thirteen
/// <c>PlayOneShot</c> calls is thirteen <c>WaveOutEvent</c>s inside 1.4 s, which is the exact
/// pattern the ChaosSfx history warns about. The whole burst is mixed into ONE mono 44.1 kHz
/// buffer with the gaps baked in as silence, written to a WAV and played as a single clip. The
/// score is deterministic (the seed IS the line text), so the WAV is cached on disk by content
/// hash and a line she has said before costs a file-exists check.</para>
///
/// <para>THE PACE IS THE IDENTITY. Every number in <see cref="VoxDials"/> is the web's, unchanged,
/// including the owner's 2026-08-24 transpose (<c>BasePitch</c> 0.82). Retune here and the desktop
/// stops sounding like the campus.</para>
///
/// <para>WHAT SILENCES HER: master volume at zero, the audio breaker, and any hold or panic
/// silence in <see cref="EmiLineEngine"/>. What does NOT silence her is the avatar mute arbiter.
/// Blips are a texture rather than a second voice, and there is no desk-sound setting to hang
/// them off, so there is no new setting: they are on.</para>
/// </summary>
public sealed class EmiVox : IEmiVox
{
    // ---------------------------------------------------------------- the dials

    /// <summary>
    /// <c>VOX_DIALS</c>, verbatim. A re-tune is a number in here and not a read of the machinery.
    /// </summary>
    public static class VoxDials
    {
        /// <summary>Cue level per blip. Deliberately under every game one-shot.</summary>
        public const double Level = 0.4;

        /// <summary>...and the typing tick is quieter again.</summary>
        public const double TickLevel = 0.22;

        // rhythm. Compression NEVER speeds these up.
        public const double GapSylMs = 62.0;
        public const double GapWordMs = 115.0;
        public const double GapSentMs = 160.0;
        public const double TailRestMs = 180.0;
        public const double JitterGapMs = 10.0;
        public const int MaxBlips = 13;
        public const double BurstMaxMs = 1400.0;

        // prosody, in semitones. BasePitch carries the owner's 2026-08-24 transpose.
        public const double BasePitch = 0.82;
        public const double JitterSemi = 1.2;
        public const double QuestionRise = 3.0;
        public const int QuestionTail = 3;
        public const double BangSemi = 1.0;
        public const double BangGain = 1.25;
        public const double BangGap = 0.85;
        public const double SadTailSemi = -2.0;
        public const double TailGain = 0.8;

        /// <summary>audio.js clamps every cue to this window; we never send one outside it.</summary>
        public const double PitchMin = 0.5;
        public const double PitchMax = 2.0;
    }

    /// <summary>One mood, keyed by body-frame family. <c>Decline</c> is semitones per sentence.</summary>
    public readonly record struct Mood(double Pitch, double Gap, double Gain, double Jitter, double Decline, double Tail);

    /// <summary>The moods, verbatim from <c>VOX_DIALS.MOODS</c>.</summary>
    public static readonly IReadOnlyDictionary<string, Mood> Moods =
        new Dictionary<string, Mood>(StringComparer.Ordinal)
        {
            ["idle"] = new(1.00, 1.00, 1.00, 1.0, 2.0, 0.0),
            ["celebration"] = new(1.15, 0.85, 1.15, 1.4, -1.5, 0.0),
            ["pet"] = new(1.05, 1.10, 0.80, 0.7, 1.4, 0.0),
            ["smug"] = new(0.95, 1.25, 0.90, 0.9, 2.2, -1.5),
            ["sad"] = new(0.85, 1.30, 0.75, 0.5, 0.6, -2.0),
            ["shock"] = new(1.20, 0.60, 1.10, 1.6, 2.0, 0.0),
        };

    /// <summary>One blip: when, at what pitch ratio, at what cue level.</summary>
    public readonly record struct VoxBlip(int AtMs, double Pitch, double Gain);

    // ---------------------------------------------------------------- the score

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static double SemiToRatio(double s) => Math.Pow(2.0, s / 12.0);

    /// <summary>
    /// <c>core/rng.js makeRng</c>: FNV-1a to a 32-bit seed, then mulberry32. Ported rather than
    /// swapped for <see cref="Random"/> because the seed is the LINE TEXT, and the whole point is
    /// that a sentence always sounds like itself, on both platforms, for ever.
    /// </summary>
    internal static Func<double> MakeRng(string? seed)
    {
        uint h = 2166136261u;
        foreach (var ch in seed ?? string.Empty)
        {
            h ^= ch;
            h = unchecked(h * 16777619u);
        }
        double hash01 = h / 4294967295.0;
        uint state = (uint)Math.Floor(hash01 * 4294967295.0);
        return () =>
        {
            state = unchecked(state + 0x6D2B79F5u);
            uint t = unchecked((uint)((int)(state ^ (state >> 15)) * (int)(1u | state)));
            t = unchecked((t + (uint)((int)(t ^ (t >> 7)) * (int)(61u | t))) ^ t);
            return (t ^ (t >> 14)) / 4294967296.0;
        };
    }

    /// <summary>
    /// SYLLABLES, cheaply: vowel groups, clamped 1..4. Not linguistics and it does not need to be,
    /// because the ear reads it as pace rather than as pronunciation.
    /// </summary>
    public static int Syllables(string? word)
    {
        int groups = 0;
        bool inGroup = false;
        foreach (var raw in word ?? string.Empty)
        {
            char c = char.ToLowerInvariant(raw);
            if (c < 'a' || c > 'z') continue;
            bool vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel)
            {
                if (!inGroup) { groups++; inGroup = true; }
            }
            else inGroup = false;
        }
        if (groups < 1) return 1;
        return groups > 4 ? 4 : groups;
    }

    private sealed class Word
    {
        public int Syl;
        public bool Ends, Ellipsis, Question, Bang;
    }

    private const string TrailingPunct = ".,!?;:…)\"']";

    private static List<Word> Tokenize(string? text)
    {
        var words = new List<Word>();
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0) return words;

        foreach (var chunk in raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            int cut = chunk.Length;
            while (cut > 0 && TrailingPunct.IndexOf(chunk[cut - 1]) >= 0) cut--;
            var punct = chunk.Substring(cut);
            var body = cut > 0 ? chunk.Substring(0, cut) : chunk;

            bool ellipsis = punct.Contains("...", StringComparison.Ordinal) || punct.Contains('…');
            words.Add(new Word
            {
                Syl = Syllables(body),
                Ends = ellipsis || punct.Contains('?') || punct.Contains('!') || punct.Contains('.'),
                Ellipsis = ellipsis,
                Question = punct.Contains('?'),
                Bang = punct.Contains('!')
            });
        }

        // A line with no terminator still ENDS: the last word closes its sentence.
        if (words.Count > 0) words[words.Count - 1].Ends = true;
        return words;
    }

    /// <summary>
    /// COMPRESSION, when a line is longer than the burst may be. Word-INTERNAL syllables go first,
    /// so the word count (the rhythm you actually hear) holds; only then whole words, from the
    /// middle out, never the first and never the last.
    /// </summary>
    private static bool DropOne(List<Word> words)
    {
        int best = -1;
        for (int i = 0; i < words.Count; i++)
            if (words[i].Syl > 1 && (best < 0 || words[i].Syl > words[best].Syl)) best = i;
        if (best >= 0) { words[best].Syl -= 1; return true; }
        if (words.Count <= 2) return false;

        int cutAt = words.Count / 2;
        var gone = words[cutAt];
        words.RemoveAt(cutAt);

        // The removed word may have been carrying a sentence end. Hand it backwards rather than
        // losing it: a dropped word must not merge two sentences.
        if (gone.Ends)
        {
            var prev = words[cutAt - 1];
            prev.Ends = true;
            prev.Ellipsis |= gone.Ellipsis;
            prev.Question |= gone.Question;
            prev.Bang |= gone.Bang;
        }
        return true;
    }

    private sealed class Beat
    {
        public double T;
        public int Sentence, Index, Count;
        public bool LastOfWord, Ellipsis, Question;
        public double Pitch, Gain;
    }

    private static List<Beat> Layout(List<Word> words, Mood m, bool bang, string seed)
    {
        var rng = MakeRng("emi-vox|" + seed);
        double gapMul = m.Gap * (bang ? VoxDials.BangGap : 1.0);
        double Jgap(double ms) => Math.Max(16.0, ms * gapMul + (rng() * 2 - 1) * VoxDials.JitterGapMs);

        int s = 0;
        var sentOf = new int[words.Count];
        var counts = new List<int>();
        for (int i = 0; i < words.Count; i++)
        {
            sentOf[i] = s;
            while (counts.Count <= s) counts.Add(0);
            counts[s] += words[i].Syl;
            if (words[i].Ends) s++;
        }

        var blips = new List<Beat>();
        double t = 0;
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (i > 0)
            {
                var prev = words[i - 1];
                double g = VoxDials.GapWordMs;
                if (prev.Ends) g += VoxDials.GapSentMs;
                if (prev.Ellipsis) g += VoxDials.TailRestMs;   // trailing off is a REST, not a rush
                t += Jgap(g);
            }
            for (int k = 0; k < w.Syl; k++)
            {
                if (k > 0) t += Jgap(VoxDials.GapSylMs);
                int si = sentOf[i];
                int j = seen.TryGetValue(si, out var had) ? had : 0;
                seen[si] = j + 1;
                blips.Add(new Beat
                {
                    T = t,
                    Sentence = si,
                    Index = j,
                    Count = counts[si],
                    LastOfWord = k == w.Syl - 1,
                    Ellipsis = w.Ellipsis,
                    Question = w.Question
                });
            }
        }
        if (blips.Count == 0) return blips;

        var asks = new HashSet<int>();
        foreach (var b in blips) if (b.Question) asks.Add(b.Sentence);

        int last = blips.Count - 1;
        for (int i = 0; i <= last; i++)
        {
            var b = blips[i];
            double frac = b.Count > 1 ? b.Index / (double)(b.Count - 1) : 0;
            double semi = 1 - m.Decline * frac;
            bool jitter = true;
            bool lifted = false;

            // THE LIFT IS A GESTURE, SO IT IS CLEAN. A question's last blips step up off a flat
            // base and take NO jitter: a smeared lift reads as a wrong note, not as a question.
            if (asks.Contains(b.Sentence))
            {
                int k = Math.Min(VoxDials.QuestionTail, b.Count);
                int mm = b.Index - (b.Count - k);
                if (mm >= 0)
                {
                    semi = 1 + (VoxDials.QuestionRise * (mm + 1)) / k;
                    jitter = false;
                    lifted = true;
                }
            }
            if (bang) semi += VoxDials.BangSemi;
            if (b.Ellipsis && b.LastOfWord) { semi += VoxDials.SadTailSemi; jitter = false; }

            // A MOOD'S SAG NEVER LANDS ON A QUESTION: the two cancelling out is heard as a wrong
            // note rather than as either feeling, so the lift wins. It is the louder gesture.
            if (i == last && !lifted) semi += m.Tail;
            if (jitter) semi += (rng() * 2 - 1) * VoxDials.JitterSemi * m.Jitter;

            double gain = VoxDials.Level * m.Gain * (bang ? VoxDials.BangGain : 1.0);
            if ((b.Ellipsis && b.LastOfWord) || (i == last && !lifted && m.Tail < 0)) gain *= VoxDials.TailGain;

            b.Pitch = Clamp(VoxDials.BasePitch * m.Pitch * SemiToRatio(semi), VoxDials.PitchMin, VoxDials.PitchMax);
            b.Gain = Clamp(gain, 0.02, 1);
        }
        return blips;
    }

    /// <summary>
    /// The score for one line. PURE and DETERMINISTIC: the same text plus the same mood always
    /// returns the same list. <paramref name="mood"/> is a body-frame family (what
    /// <c>EmiChains.FrameForFace</c> already resolved for the reaction face); anything else rests
    /// at idle, deliberately, because a face nobody paired is a face she has no strong feeling
    /// about.
    /// </summary>
    public static IReadOnlyList<VoxBlip> MakeScore(string? text, string? mood)
    {
        var m = mood != null && Moods.TryGetValue(mood, out var found) ? found : Moods["idle"];
        var seed = text ?? string.Empty;

        var words = Tokenize(seed);
        if (words.Count == 0) return Array.Empty<VoxBlip>();
        bool bang = words.Any(w => w.Bang);

        // FIT THE BURST: compress until it is inside BOTH ceilings, never by moving the clock.
        var score = Layout(words, m, bang, seed);
        for (int guard = 0; guard < 240; guard++)
        {
            double dur = score.Count > 0 ? score[score.Count - 1].T : 0;
            if (score.Count <= VoxDials.MaxBlips && dur <= VoxDials.BurstMaxMs) break;
            if (!DropOne(words)) { score = score.Take(VoxDials.MaxBlips).ToList(); break; }
            score = Layout(words, m, bang, seed);
        }

        var outp = new VoxBlip[score.Count];
        for (int i = 0; i < score.Count; i++)
            outp[i] = new VoxBlip((int)Math.Round(score[i].T), score[i].Pitch, score[i].Gain);
        return outp;
    }

    // ---------------------------------------------------------------- the synthesis

    private const int SampleRate = 44100;

    /// <summary>The <c>voice</c> bus's own trim, so a blip sits where it sits on the campus.</summary>
    private const double VoiceBus = 0.85;

    // shell/audio.js RECIPES, the two EMI ones, verbatim.
    private const double BlipF0 = 760.0, BlipF1 = 680.0, BlipMs = 56.0, BlipGain = 0.35, BlipAttack = 0.3;
    private const double TickF0 = 336.0, TickF1 = 322.0, TickMs = 30.0, TickGain = 0.16, TickAttack = 0.35;

    /// <summary>
    /// Mix one recipe into the buffer at an offset. The envelope is <c>audio.js envelope()</c>
    /// exactly: a linear attack to the peak over <c>dur * attack</c>, then an exponential fall to
    /// 0.0001 at the end. The frequency is an exponential glide f0 to f1 across the same window
    /// (WebAudio's <c>exponentialRampToValueAtTime</c>), which is what makes it a chirp rather
    /// than a beep.
    /// </summary>
    private static void MixTone(float[] buf, int atSample, double f0, double f1, double ms,
                                double recGain, double attackFrac, double level, double pitch)
    {
        double dur = Math.Max(0.02, ms / 1000.0);
        double atk = Math.Max(0.004, dur * attackFrac);
        double peak = Math.Max(0.0002, Math.Sqrt(Clamp(level, 0, 1)) * recGain * VoiceBus);

        double a = Math.Max(20.0, Math.Min(20000.0, f0 * pitch));
        double b = Math.Max(20.0, Math.Min(20000.0, f1 * pitch));

        int n = (int)(dur * SampleRate);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            int at = atSample + i;
            if (at < 0) continue;
            if (at >= buf.Length) break;

            double t = i / (double)SampleRate;
            double f = a * Math.Pow(b / a, t / dur);
            phase += f / SampleRate;

            double env = t < atk
                ? peak * (t / atk)
                : peak * Math.Pow(0.0001 / peak, (t - atk) / Math.Max(1e-6, dur - atk));

            // Triangle, the recipe's own wave. A square reads as a machine; a triangle reads as a
            // small instrument, which is the whole point of BLIPESE.
            double frac = phase - Math.Floor(phase);
            double tri = 4.0 * Math.Abs(frac - 0.5) - 1.0;

            buf[at] += (float)(tri * env);
        }
    }

    /// <summary>Render a whole burst into ONE mono buffer, gaps and all.</summary>
    internal static float[] RenderBurst(IReadOnlyList<VoxBlip> score)
    {
        if (score == null || score.Count == 0) return Array.Empty<float>();

        double endMs = score[score.Count - 1].AtMs + BlipMs + 20.0;
        var buf = new float[(int)(endMs / 1000.0 * SampleRate) + 8];
        foreach (var b in score)
        {
            MixTone(buf, (int)(b.AtMs / 1000.0 * SampleRate),
                    BlipF0, BlipF1, BlipMs, BlipGain, BlipAttack, b.Gain, b.Pitch);
        }
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)Clamp(buf[i], -1.0, 1.0);
        return buf;
    }

    /// <summary>Render the typing tick. Always the same clip: one pitch, one level.</summary>
    internal static float[] RenderTick()
    {
        var buf = new float[(int)((TickMs + 20.0) / 1000.0 * SampleRate) + 8];
        MixTone(buf, 0, TickF0, TickF1, TickMs, TickGain, TickAttack, VoxDials.TickLevel, 1.0);
        return buf;
    }

    /// <summary>Mono 16-bit PCM RIFF/WAVE, the shape NAudio reads with no codec at all.</summary>
    internal static byte[] WriteWav(float[] samples)
    {
        samples ??= Array.Empty<float>();
        int dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];

        void Ascii(int at, string s) { for (int i = 0; i < s.Length; i++) bytes[at + i] = (byte)s[i]; }
        void U32(int at, uint v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); bytes[at + 2] = (byte)(v >> 16); bytes[at + 3] = (byte)(v >> 24); }
        void U16(int at, ushort v) { bytes[at] = (byte)v; bytes[at + 1] = (byte)(v >> 8); }

        Ascii(0, "RIFF");
        U32(4, (uint)(36 + dataBytes));
        Ascii(8, "WAVE");
        Ascii(12, "fmt ");
        U32(16, 16);                       // PCM chunk size
        U16(20, 1);                        // format = PCM
        U16(22, 1);                        // channels = mono
        U32(24, SampleRate);
        U32(28, SampleRate * 2);           // byte rate (1 channel x 2 bytes)
        U16(32, 2);                        // block align
        U16(34, 16);                       // bits per sample
        Ascii(36, "data");
        U32(40, (uint)dataBytes);

        for (int i = 0; i < samples.Length; i++)
        {
            double v = Clamp(samples[i], -1.0, 1.0);
            short sv = (short)Math.Round(v * short.MaxValue);
            bytes[44 + i * 2] = (byte)sv;
            bytes[44 + i * 2 + 1] = (byte)((ushort)sv >> 8);
        }
        return bytes;
    }

    // ---------------------------------------------------------------- the instance

    private static string ClipDir => Path.Combine(CorePaths.UserData, "emi", "vox");

    /// <summary>How many cached line clips to keep before the oldest are swept.</summary>
    private const int CacheCap = 96;

    private readonly object _gate = new();
    private AudioPlaybackHandle? _live;
    private bool _dead;
    private string? _tickPath;

    /// <summary>Diagnostics only: how many lines have gone out.</summary>
    public int Spoke { get; private set; }

    /// <summary>Diagnostics only: how many typing ticks have gone out.</summary>
    public int Ticks { get; private set; }

    /// <summary>
    /// True when a sound may leave this object at all: the app is not muted, the audio breaker is
    /// shut, and nothing safety-shaped is holding her quiet.
    /// </summary>
    private static bool Audible(out float volume)
    {
        volume = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;

            int master = App.Settings?.Current?.MasterVolume ?? 0;
            if (master <= 0) return false;

            // SAFETY IS SILENCE. A hold or the panic tail means she is deliberately quiet, and a
            // texture that keeps chirping through one is the feature reading as broken.
            if (EmiLineEngine.Instance.HoldActive) return false;

            volume = (float)Clamp(master / 100.0, 0, 1);
            return volume > 0f;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox audibility probe failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Speak(string? text, string? mood)
    {
        if (_dead) return false;
        var line = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (!Audible(out var volume)) return false;

        try
        {
            var score = MakeScore(line, mood);
            if (score.Count == 0) return false;

            var path = EnsureClip("line", line + "|" + (mood ?? "idle"), () => RenderBurst(score));
            if (path == null) return false;

            Stop();                                   // ONE VOICE: never two at once
            var handle = App.Audio?.PlayOneShot(path, volume, "emi-vox");
            lock (_gate) _live = handle;
            Spoke++;
            return handle != null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox speak failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool Tick()
    {
        if (_dead) return false;
        if (!Audible(out var volume)) return false;
        try
        {
            _tickPath ??= EnsureClip("tick", "v1", RenderTick);
            if (_tickPath == null) return false;

            // A tick is 30 ms and lands between bursts, so it does NOT take the live slot: cutting
            // a burst on a dot frame would be the typewriter this voice is deliberately not.
            App.Audio?.PlayOneShot(_tickPath, volume, "emi-vox-tick");
            Ticks++;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox tick failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        AudioPlaybackHandle? live;
        lock (_gate) { live = _live; _live = null; }
        if (live == null) return;
        try { live.Stop(); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox stop failed"); }
    }

    /// <summary>
    /// The WAV for this key, rendered once. The score is deterministic, so the file is too: a line
    /// she has said before is a file-exists check and nothing else.
    /// </summary>
    private static string? EnsureClip(string kind, string key, Func<float[]> render)
    {
        try
        {
            var dir = ClipDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, kind + "-" + KeyHash(key) + ".wav");

            var info = new FileInfo(path);
            if (info.Exists && info.Length > 44) return path;

            var samples = render();
            if (samples.Length == 0) return null;
            File.WriteAllBytes(path, WriteWav(samples));
            SweepCache(dir);
            return path;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] vox clip render failed");
            return null;
        }
    }

    /// <summary>FNV-1a again, 64-bit this time. A file name, not a security boundary.</summary>
    private static string KeyHash(string? key)
    {
        ulong h = 14695981039346656037ul;
        foreach (var b in Encoding.UTF8.GetBytes(key ?? string.Empty))
        {
            h ^= b;
            h = unchecked(h * 1099511628211ul);
        }
        return h.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Keep the cache bounded: her vocabulary is finite, but a machine's uptime is not.</summary>
    private static void SweepCache(string dir)
    {
        try
        {
            var files = new DirectoryInfo(dir).GetFiles("line-*.wav");
            if (files.Length <= CacheCap) return;
            foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - CacheCap))
            {
                try { f.Delete(); } catch { /* a cached blip is never worth an exception */ }
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] vox cache sweep failed"); }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_dead) return;
        _dead = true;
        Stop();
    }
}
