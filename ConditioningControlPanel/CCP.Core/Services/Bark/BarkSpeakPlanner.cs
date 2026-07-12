using System;

namespace ConditioningControlPanel.Core.Services.Bark
{
    /// <summary>
    /// Delivery-kind chosen by the speak path for one decided bark (WPF Speak routing,
    /// BarkService.cs:1595-1624). Pure / thread-safe / no UI: the slice-2 AvatarBarkSpeaker consults
    /// this then marshals the chosen call to the avatar tube on the UI thread. Kept in Core so the
    /// routing + substitution decisions are unit-testable without the Avalonia dispatcher.
    /// </summary>
    public enum BarkDeliveryKind
    {
        /// <summary>Ordinary queued bark (WPF avatar.Giggle, BarkService.cs:1623-1624).</summary>
        Giggle,

        /// <summary>Preempting bark that clears the speech queue (WPF avatar.GigglePriority,
        /// BarkService.cs:1620-1622).</summary>
        GigglePriority,

        /// <summary>Easter-egg bark while master-muted: text-only bubble, no voiceline/audio
        /// (WPF avatar.Giggle silent path, BarkService.cs:1595-1599).</summary>
        SilentMuteEgg,
    }

    /// <summary>
    /// Pure delivery-side helpers for the bark speak path (BARK-1 slice 2). The AvatarTube-backed
    /// speaker (<c>AvatarBarkSpeaker</c> in CCP.Avalonia) computes the display line and the delivery
    /// kind through these so the logic is exercisable from Core unit tests without a UI thread.
    /// </summary>
    public static class BarkSpeakPlanner
    {
        /// <summary>Self-echo mute window applied after every spoken bark so the bubble text cannot
        /// trip the keyword/OCR pipeline off its own output (WPF SelfEchoMuteMs, BarkService.cs:84).</summary>
        public const int SelfEchoMuteMs = 8000;

        /// <summary>Neutral fallback substituted for {0} when no foreground-app name is available
        /// (WPF BarkService.cs:1639 — "that").</summary>
        public const string FocusedAppFallback = "that";

        /// <summary>
        /// Substitute the {0} (focused-app) token. The engine already applied {key}→ctx substitution
        /// (WPF ApplySubstitutions :1643-1648); this completes the {0}→focused-app half (:1635-1641)
        /// that needs the window-awareness read and therefore lives on the delivery side.
        /// <para>
        /// Source precedence mirrors WPF: classified service name → detected display name, then a
        /// (port-extra) raw foreground-window title, then the neutral fallback. Never throws and never
        /// leaves a literal <c>{0}</c> in the result.
        /// </para>
        /// </summary>
        /// <param name="line">Line text after the engine's {key} substitution (may be null/empty).</param>
        /// <param name="serviceName">Awareness-classified service/platform name (WPF
        /// <c>WindowAwareness.CurrentServiceName</c>, BarkService.cs:1637).</param>
        /// <param name="detectedName">Awareness detected display name (WPF
        /// <c>WindowAwareness.CurrentDetectedName</c>, BarkService.cs:1638).</param>
        /// <param name="foregroundTitle">Raw foreground window title from the platform seam
        /// (IForegroundWindowTitleProvider) — port-extra fallback when awareness has no name.</param>
        public static string SubstituteFocusedApp(
            string? line,
            string? serviceName,
            string? detectedName,
            string? foregroundTitle)
        {
            if (string.IsNullOrEmpty(line) || !line.Contains("{0}", StringComparison.Ordinal))
                return line ?? string.Empty;

            var app = serviceName;
            if (string.IsNullOrWhiteSpace(app)) app = detectedName;
            if (string.IsNullOrWhiteSpace(app)) app = foregroundTitle;
            if (string.IsNullOrWhiteSpace(app)) app = FocusedAppFallback;

            return line.Replace("{0}", app, StringComparison.Ordinal);
        }

        /// <summary>
        /// Decide how to deliver a bark: mute-egg silent path (EasterEgg + master-muted), else
        /// preempting GigglePriority vs queued Giggle. Mirrors WPF Speak routing
        /// (BarkService.cs:1595-1624).
        /// </summary>
        /// <param name="ruleClass">The matched rule's class (from <c>BarkContext.RuleClassKey</c>).
        /// Default <see cref="BarkClass.Normal"/> when the engine could not stamp it.</param>
        /// <param name="muted">True when master volume is zero (WPF :1594). Triggers the mute-egg only
        /// for EasterEgg.</param>
        /// <param name="priority">The engine-computed preempt flag (Class!=Normal || Priority&gt;=100,
        /// BarkService.cs:1619).</param>
        public static BarkDeliveryKind PlanDelivery(BarkClass ruleClass, bool muted, bool priority)
        {
            if (ruleClass == BarkClass.EasterEgg && muted) return BarkDeliveryKind.SilentMuteEgg;
            return priority ? BarkDeliveryKind.GigglePriority : BarkDeliveryKind.Giggle;
        }
    }
}
