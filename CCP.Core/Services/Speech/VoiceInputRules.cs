using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>
    /// The She's Listening rules both heads share, moved out of WPF MainWindow.SheListening.cs and
    /// AutonomyService.Voice.cs (both keep only the mic/UI half and delegate here): when the mic is
    /// armed, which input modes may run, the wake phrases, and the mic-sensitivity dial.
    /// </summary>
    public static class VoiceInputRules
    {
        /// <summary>The master on/off: consent AND at least one input mode (wake word or push-to-talk).</summary>
        public static bool MicIsArmed(AppSettings s)
            => s.MicConsentGiven && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);

        /// <summary>AutonomyService.RefreshVoiceInputModes' base condition, per mode. Entitlement is
        /// re-read by the caller every reconcile so a lapse closes the mic (premium or voice free day).</summary>
        public static (bool Wake, bool Ptt) ModesToRun(AppSettings s, bool entitled, bool speechAvailable)
        {
            bool baseOk = entitled && s.MicConsentGiven && speechAvailable;
            return (baseOk && s.SpeechWakeWordEnabled && WakeWords(s.SpeechWakeWords).Count > 0,
                    baseOk && s.SpeechPushToTalkEnabled);
        }

        /// <summary>The user's wake phrases: comma / semicolon / newline separated, trimmed, distinct.</summary>
        public static List<string> WakeWords(string? raw)
            => (raw ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(w => w.Trim())
                          .Where(w => w.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase)
                          .ToList();

        // "bambi" (and friends) aren't English dictionary words, so the offline model can't spell them
        // reliably. Feeding the decoder these acoustically-plausible spellings as extra grammar targets
        // lets it return a phrase that fuzzy-matches the canonical wake word. Only the last token varies.
        private static readonly Dictionary<string, string[]> WakeNameVariants =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["bambi"]  = new[] { "bambi", "bamby", "bambie", "bambee", "bombi", "bambit" },
                ["bimbo"]  = new[] { "bimbo", "bimba", "bimbow", "bimboh" },
                ["bambis"] = new[] { "bambis", "bambies" },
            };

        /// <summary>Canonical phrases FIRST (the recognizer matches words[0]), then phonetic spellings
        /// of an OOV trailing name ("hey bambi" -> "hey bamby", ...).</summary>
        public static List<string> ExpandWakeVariants(IReadOnlyList<string> phrases)
        {
            var outp = new List<string>(phrases);
            foreach (var phrase in phrases)
            {
                var toks = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 0) continue;
                if (!WakeNameVariants.TryGetValue(toks[^1], out var variants)) continue;
                var prefix = toks.Length > 1 ? string.Join(' ', toks[..^1]) + " " : "";
                foreach (var v in variants)
                {
                    var cand = prefix + v;
                    if (!outp.Contains(cand, StringComparer.OrdinalIgnoreCase)) outp.Add(cand);
                }
            }
            return outp;
        }

        // "Mic sensitivity" slider <-> loudness gate. 0..100 maps INVERSELY to the RMS threshold:
        // 100% = most sensitive (softest speech OK), 0% = strictest. The useful gate range only.
        private const double LoudThrAtMinSens = 0.045; // slider 0%
        private const double LoudThrAtMaxSens = 0.004; // slider 100%

        public static double SensToThreshold(double sens)
            => LoudThrAtMinSens - (LoudThrAtMinSens - LoudThrAtMaxSens) * (Math.Clamp(sens, 0, 100) / 100.0);

        public static double ThresholdToSens(double thr)
            => Math.Clamp((LoudThrAtMinSens - thr) / (LoudThrAtMinSens - LoudThrAtMaxSens) * 100.0, 0, 100);
    }
}
