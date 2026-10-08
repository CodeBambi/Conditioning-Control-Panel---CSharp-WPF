namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// ccp-bugs #1352: what the "no videos" dialog should blame. The refill funnel already counts
    /// how many enabled files the duration filter threw out; when that filter is what emptied the
    /// pool, telling the user to "add files" sends them to the wrong place.
    /// </summary>
    public static class NoVideosReason
    {
        /// <summary>
        /// True when there were enabled local videos and the length filter kept none of them.
        /// Negative counts mean the funnel has not run yet this launch, so the old text stands.
        /// </summary>
        public static bool LengthFilterEmptied(int keptEnabled, int keptDuration)
            => keptEnabled > 0 && keptDuration == 0;

        /// <summary>The filter range the way the panel's sliders read: 52s, 2m 50s. An unset side is 0s or ∞.</summary>
        public static string FormatRange(int minSeconds, int maxSeconds)
            => $"{Format(minSeconds, "0s")} - {Format(maxSeconds, "∞")}";

        private static string Format(int seconds, string unset)
        {
            if (seconds <= 0) return unset;
            if (seconds < 60) return $"{seconds}s";
            var rem = seconds % 60;
            return rem == 0 ? $"{seconds / 60}m" : $"{seconds / 60}m {rem}s";
        }
    }
}
