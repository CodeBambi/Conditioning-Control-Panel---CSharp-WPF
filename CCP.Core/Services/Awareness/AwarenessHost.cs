using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.AIService;

namespace ConditioningControlPanel.Services.Awareness
{
    /// <summary>
    /// What the awareness pipeline reached through WPF's <c>App</c> statics: the companion's mouth
    /// (<c>App.AvatarWindow</c>, <c>App.Bark</c>), the keyword engine's echo mute, the AI transport
    /// (<c>App.Ai</c>) and the legacy title observer's current service name. The head fills these in
    /// when it builds the observer; an unset seam reads as "not there", which every caller already
    /// treated as "do not speak".
    /// </summary>
    public static class AwarenessHost
    {
        /// <summary>WPF <c>App.Ai</c>.</summary>
        public static volatile Func<IAiService?>? Ai;

        /// <summary>WPF <c>App.Bark.RaiseAwarenessBark(frame)</c>.</summary>
        public static volatile Func<ContextFrame, bool>? RaiseAwarenessBark;

        /// <summary>WPF <c>App.Bark.NotifyExternalLineSpoken()</c>.</summary>
        public static volatile Action? NotifyExternalLineSpoken;

        /// <summary>WPF <c>App.AvatarWindow != null</c>.</summary>
        public static volatile Func<bool>? HasAvatar;

        /// <summary>
        /// WPF <c>App.AvatarWindow.SpeakAwarenessLine(line, doubleBounce)</c>. False when the avatar
        /// went away between the check and the call.
        /// </summary>
        public static volatile Func<string, bool, bool>? SpeakAwarenessLine;

        /// <summary>WPF <c>App.AvatarWindow.IsCompanionBusy(windowMs)</c>.</summary>
        public static volatile Func<int, bool>? IsCompanionBusy;

        /// <summary>WPF <c>App.KeywordTriggers.MuteKeywordEcho(line, ms)</c>.</summary>
        public static volatile Action<string, int>? MuteKeywordEcho;

        /// <summary>WPF <c>App.KeywordTriggers.GetRecentForegroundApps()</c>.</summary>
        public static volatile Func<IEnumerable<string>?>? RecentForegroundApps;

        /// <summary>WPF <c>App.WindowAwareness.CurrentServiceName</c>.</summary>
        public static volatile Func<string?>? CurrentServiceName;

        /// <summary>
        /// The foreground window's title, for the speaker's staleness fallback (WPF read user32
        /// directly). In memory only: it is classified to an app id and dropped.
        /// </summary>
        public static volatile Func<string?>? ForegroundTitle;
    }
}
