using System.Linq;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Shared "what does this enhancement need to run?" queries. Factored out of
    /// the per-window copies (browser hub, Deeper player) so the mandatory-video
    /// engine-start nudge asks the question exactly the same way they do.
    /// </summary>
    public static class EnhancementCapabilities
    {
        /// <summary>
        /// True when at least one rule in the enhancement can only fire while the
        /// webcam tracker is running (gaze / blink / mouth / attention). Trusts the
        /// save-time <see cref="EnhancementAutoTagger.TagWebcam"/> auto-tag first,
        /// then falls back to scanning rule triggers across BOTH the unified
        /// timeline and the legacy Rules collection — covering files authored by
        /// the new editor (webcam rules live only in <c>TimelineItems</c> since the
        /// save back-projection was removed) as well as pre-auto-tag / hand-written
        /// files.
        /// </summary>
        public static bool NeedsWebcam(Enhancement? enh)
        {
            if (enh == null) return false;
            if (enh.Metadata?.AutoTags?.Contains(EnhancementAutoTagger.TagWebcam) == true) return true;

            if (enh.TimelineItems != null)
            {
                foreach (var item in enh.TimelineItems)
                    if (item?.Kind == TimelineItemKind.Rule && IsWebcamTrigger(item.Trigger)) return true;
            }
            if (enh.Rules != null)
            {
                foreach (var rule in enh.Rules)
                    if (IsWebcamTrigger(rule?.Trigger)) return true;
            }
            return false;
        }

        private static bool IsWebcamTrigger(EnhancementTrigger? t) => t is
            GazeTargetTrigger or GazeAvoidTrigger or AttentionLostTrigger
            or BlinkDetectedTrigger or MouthOpenTrigger;
    }
}
