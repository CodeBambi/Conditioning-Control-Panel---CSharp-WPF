// Namespace matches OverlayService (its only in-folder neighbour), which is
// ConditioningControlPanel.Services rather than ...Services.Notifications.
namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Whether Brain Drain's VISUAL half wants to be on screen at all.
    ///
    /// <para>The blur-strength slider used to floor at 1, and 1 is not "off": the draw-alpha curve
    /// (<c>BrainDrainLayer.AlphaFor</c>) starts at <c>AlphaFloor</c> there, so the gentlest setting
    /// the app offered still laid a visible haze over the screen. That is the whole of an
    /// accessibility report from 2026-09-20 - "screen blur strength doesn't go below 1%, which
    /// still hurts my eyes" - from someone who wanted the Brain Drain AUDIO and none of the
    /// picture. The floor is 0 now and 0 means gone, not faint.</para>
    ///
    /// <para><b>Only the picture.</b> <c>BrainDrainService</c> (the audio half) reads
    /// <c>BrainDrainEnabled</c> and <c>BrainDrainIntensity</c> and has never read the blur
    /// strength, so a zero here silences nothing: the clips keep playing on their own schedule.</para>
    ///
    /// <para>A separate class rather than an <c>&gt; 0</c> in four call sites because it IS four
    /// call sites (the overlay's start, its live refresh, Autonomy's pulse and the melt bubble's
    /// pop), and each of those used to carry its own <c>Math.Max(1, ...)</c> floor that would
    /// quietly put the haze back.</para>
    /// </summary>
    internal static class BrainDrainVisualPolicy
    {
        /// <summary>The strength that means "no picture". Lowest the slider goes.</summary>
        internal const int Off = 0;

        /// <summary>True when this strength draws nothing at all.</summary>
        internal static bool IsSilent(int blurStrength) => blurStrength <= Off;

        /// <summary>
        /// Does the BASE feature want the blur up? Both halves of the answer matter: the feature
        /// has to be on, and its visual dial has to be above zero. Ad-hoc drains (a Deeper band, a
        /// timed effect) are NOT covered - they carry their own strength and their own holds.
        /// </summary>
        internal static bool WantsBlur(bool featureEnabled, int blurStrength) =>
            featureEnabled && !IsSilent(blurStrength);
    }
}
