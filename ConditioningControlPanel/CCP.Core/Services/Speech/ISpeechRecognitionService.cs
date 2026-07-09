using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Core.Services.Speech;

/// <summary>Outcome of a single "say this" listen window.</summary>
public sealed class PhraseResult
{
    /// <summary>True when the spoken phrase was close enough AND loud enough.</summary>
    public bool Matched { get; init; }
    /// <summary>What the recognizer thinks the user said (best effort, may be empty).</summary>
    public string Transcript { get; init; } = "";
    /// <summary>Fuzzy similarity to the target phrase, 0..1.</summary>
    public double Score { get; init; }
    /// <summary>The recognizer's own average per-word acoustic confidence, 0..1.</summary>
    public double Confidence { get; init; }
    /// <summary>Peak loudness during the window cleared the gate.</summary>
    public bool LoudEnough { get; init; }
    /// <summary>The listen window expired before a final utterance.</summary>
    public bool TimedOut { get; init; }
    /// <summary>Service unavailable (no model / no mic / not consented) — caller should skip, not fail.</summary>
    public bool Unavailable { get; init; }

    public static PhraseResult NotAvailable => new() { Unavailable = true };
}

/// <summary>Per-listen tuning. Null fields fall back to the service defaults / settings.</summary>
public sealed class RecognizeOptions
{
    /// <summary>Hard cap on the listen window.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
    /// <summary>
    /// Optional speech-onset deadline. If set, the listen ends early (as silence) when no speech has
    /// begun by this point — but once the user starts talking, the full <see cref="Timeout"/> applies
    /// so a late-starting utterance isn't clipped. Null => the hard cap is the only limit.
    /// </summary>
    public TimeSpan? OnsetTimeout { get; init; }
    /// <summary>Minimum fuzzy similarity (0..1) to count as a match. Null => settings/default.</summary>
    public double? MatchThreshold { get; init; }
    /// <summary>Minimum peak RMS loudness (0..1) to count as "said out loud". Null => settings/default.</summary>
    public double? LoudnessThreshold { get; init; }
}

/// <summary>A selectable microphone. Index -1 = the system default capture device.</summary>
public readonly record struct SpeechInputDevice(int Index, string Name);

/// <summary>
/// Offline speech recognition seam for the "repeat after me" / "Hey Bambi" voice mechanics.
/// Windows provides a Vosk + NAudio implementation; other heads register a no-op fallback whose
/// <see cref="IsAvailable"/> is false and whose recognize calls return <see cref="PhraseResult.NotAvailable"/>.
///
/// Privacy contract (parity with the WPF SpeechService): OFFLINE ONLY, audio buffers stay in memory
/// and are never written to disk or transmitted; <see cref="IsListening"/> is runtime-only.
/// </summary>
public interface ISpeechRecognitionService
{
    /// <summary>Streaming partial hypotheses while a window is open ("I'm hearing ___").</summary>
    event EventHandler<string>? PartialTranscript;
    /// <summary>Live input loudness, 0..1 RMS, for a level meter.</summary>
    event EventHandler<double>? LevelChanged;
    /// <summary>Raised when <see cref="IsListening"/> flips, for UI state.</summary>
    event EventHandler<bool>? ListeningChanged;

    /// <summary>True only while a capture window is open. Runtime-only; never persisted.</summary>
    bool IsListening { get; }

    /// <summary>True when recognition can actually run (model present + a capture device exists). Excludes consent.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether the OS reports at least one audio capture device.</summary>
    bool HasCaptureDevice { get; }

    /// <summary>Enumerate capture devices for the mic picker. First entry is the system default (index -1).</summary>
    IReadOnlyList<SpeechInputDevice> EnumerateInputDevices();

    /// <summary>Cancel any in-flight capture session immediately (UI "stop the mic" / privacy pill). No-op when idle.</summary>
    void StopListening();

    /// <summary>Open the mic, ask the user to say <paramref name="target"/>, and score what comes back.</summary>
    Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default);

    /// <summary>Listen once and return the best-effort transcript constrained to <paramref name="phrases"/> (closed command set).</summary>
    Task<PhraseResult> RecognizeOneOfAsync(IReadOnlyList<string> phrases, RecognizeOptions? options = null, CancellationToken ct = default);

    /// <summary>Listen until one of <paramref name="wakeWords"/> is heard or the token cancels. Returns the matched phrase, or null.</summary>
    Task<string?> WaitForWakeWordAsync(IEnumerable<string> wakeWords, CancellationToken ct = default);
}

/// <summary>
/// Portable phrase normalization + fuzzy scoring shared by the speech engine and the voice-command
/// router. Pure functions — no engine dependency — so command matching can score transcripts without
/// a recognizer. Ported verbatim from the WPF SpeechService so behavior is identical.
/// </summary>
public static class SpeechMatching
{
    /// <summary>Lowercase, strip punctuation, collapse whitespace — so scoring compares words, not noise.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch == '-' || ch == '\'') sb.Append(' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Word-level normalized similarity (0..1): 1 minus word edit distance over the longer phrase.
    /// Robust to a dropped/extra word, which is exactly how "repeat after me" misses look.
    /// </summary>
    public static double Similarity(string target, string heard)
    {
        if (target.Length == 0) return heard.Length == 0 ? 1 : 0;
        if (heard.Length == 0) return 0;
        var a = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var b = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length == 0) return b.Length == 0 ? 1 : 0;
        // Costs are scaled x2 so an "unk" substitution can cost half a word (see
        // WordEditDistance). Denominator scales to match, keeping the ratio identical
        // to the old integer distance for phrases with no unknown words.
        int dist = WordEditDistance(a, b);
        double wordSim = 1.0 - (double)dist / (2.0 * Math.Max(a.Length, b.Length));
        return Math.Clamp(wordSim, 0, 1);
    }

    private static int WordEditDistance(string[] a, string[] b)
    {
        // All costs scaled x2: match=0, real substitution/indel=2. A heard "unk" token
        // (Vosk's out-of-vocabulary marker; Normalize strips the brackets from "[unk]")
        // substitutes for any target word at HALF cost (1). This forgives kink/OOV words
        // like "cockslut" or "gooning" that the small model can't recognize, without
        // letting a fully mumbled all-"unk" transcript pass the match threshold (#505).
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j * 2;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i * 2;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost;
                if (a[i - 1] == b[j - 1]) cost = 0;
                else if (b[j - 1] == "unk") cost = 1;   // out-of-vocab word heard: half penalty
                else cost = 2;
                cur[j] = Math.Min(Math.Min(prev[j] + 2, cur[j - 1] + 2), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
