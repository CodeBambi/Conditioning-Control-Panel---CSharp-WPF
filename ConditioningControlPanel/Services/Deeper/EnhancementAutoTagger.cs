using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Detects hardware-gating tags on an <see cref="Enhancement"/> at save
    /// time. The catalogue browser surfaces these as chips on each card so
    /// downloaders know which equipment they need to plug in before opening
    /// the file.
    ///
    /// v5.9.10 vocabulary is intentionally small (haptics, webcam) — both
    /// answer the question "do I need to plug something in?". Visual effects
    /// like overlay/flash/subliminal are not tagged because they're not
    /// hardware-gated and would just add noise to the chip strip.
    /// </summary>
    public static class EnhancementAutoTagger
    {
        public const string TagHaptics = "haptics";
        public const string TagWebcam = "webcam";

        public static List<string> Detect(Enhancement enh)
        {
            var tags = new HashSet<string>();
            if (enh == null) return new List<string>();

            // -- Haptics ----------------------------------------------------------
            if (enh.HapticTracks != null && enh.HapticTracks.Any(t => t?.Events?.Count > 0))
                tags.Add(TagHaptics);

            if (enh.TimelineItems != null)
            {
                foreach (var item in enh.TimelineItems)
                {
                    if (item == null) continue;
                    if (item.Kind == TimelineItemKind.Effect && item.EffectType == EffectTypes.Haptic)
                    {
                        tags.Add(TagHaptics);
                    }
                    else if (item.Kind == TimelineItemKind.Rule && item.Action is { } action)
                    {
                        if (IsHapticAction(action)) tags.Add(TagHaptics);
                    }
                }
            }

            if (enh.Rules != null)
            {
                foreach (var rule in enh.Rules)
                {
                    if (rule?.Action != null && IsHapticAction(rule.Action))
                        tags.Add(TagHaptics);
                }
            }

            // -- Webcam (superset — covers gaze, blink, attention, mouth) ---------
            foreach (var trigger in CollectTriggers(enh))
            {
                if (trigger is GazeTargetTrigger
                    or GazeAvoidTrigger
                    or BlinkDetectedTrigger
                    or AttentionLostTrigger
                    or MouthOpenTrigger)
                {
                    tags.Add(TagWebcam);
                    break;
                }
            }

            var sorted = tags.ToList();
            sorted.Sort(System.StringComparer.Ordinal);
            return sorted;
        }

        private static bool IsHapticAction(EnhancementAction action) => action switch
        {
            TriggerHapticAction => true,
            TriggerEffectAction te => te.EffectType == EffectTypes.Haptic,
            _ => false
        };

        private static IEnumerable<EnhancementTrigger> CollectTriggers(Enhancement enh)
        {
            if (enh.TimelineItems != null)
            {
                foreach (var item in enh.TimelineItems)
                {
                    if (item?.Kind == TimelineItemKind.Rule && item.Trigger != null)
                        yield return item.Trigger;
                }
            }
            if (enh.Rules != null)
            {
                foreach (var rule in enh.Rules)
                {
                    if (rule?.Trigger != null) yield return rule.Trigger;
                }
            }
        }
    }
}
