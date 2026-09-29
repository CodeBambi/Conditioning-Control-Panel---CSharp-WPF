using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The settings half of WPF <c>MainWindow.EnforceEntitlementLapse</c> (MainWindow.Patreon.cs:92):
    /// a premium feature left ON when its entitlement lapses (the ? box rotating out, a subscription
    /// ending) is switched off, so a veil never covers a running feature and its own off switch.
    /// Only ever writes <c>false</c>. Each head stops its own engines for the keys returned.
    /// </summary>
    public static class EntitlementLapse
    {
        /// <summary>Clears every lapsed premium flag on <paramref name="s"/> against
        /// <see cref="CoreEntitlement"/>. Returns the cleared features (their daily-free keys, plus
        /// "awareness-mode", "voice-mic" and "mantra-chant"); empty when nothing changed. Does not save.</summary>
        public static List<string> Enforce(AppSettings s)
        {
            var cleared = new List<string>();
            if (CoreEntitlement.HasPremium) return cleared;
            bool Lapsed(string key) => !CoreEntitlement.IsFreeToday(key);

            if (s.KeywordTriggersEnabled && Lapsed("awareness")) { s.KeywordTriggersEnabled = false; cleared.Add("awareness"); }
            if (s.AwarenessModeEnabled && Lapsed("awareness")) { s.AwarenessModeEnabled = false; cleared.Add("awareness-mode"); }
            if (s.AutonomyModeEnabled && Lapsed("takeover")) { s.AutonomyModeEnabled = false; cleared.Add("takeover"); }
            if (s.SpokenMantrasEnabled && Lapsed("voice")) { s.SpokenMantrasEnabled = false; cleared.Add("voice"); }
            if ((s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled) && Lapsed("voice"))
            {
                s.SpeechWakeWordEnabled = s.SpeechPushToTalkEnabled = false;
                cleared.Add("voice-mic");
            }
            if (s.MantraChantEnabled && Lapsed("takeover")) { s.MantraChantEnabled = false; cleared.Add("mantra-chant"); }
            if (s.Haptics?.Enabled == true && Lapsed("haptics")) { s.Haptics.Enabled = false; cleared.Add("haptics"); }
            return cleared;
        }
    }
}
