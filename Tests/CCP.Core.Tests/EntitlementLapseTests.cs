using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF EnforceEntitlementLapse's flag half: lapsed premium flags go off, a patron or the
/// feature's free day keeps them, and it only ever writes false.</summary>
public sealed class EntitlementLapseTests
{
    private static AppSettings AllOn()
    {
        var s = new AppSettings
        {
            KeywordTriggersEnabled = true, AwarenessModeEnabled = true, AutonomyModeEnabled = true,
            SpokenMantrasEnabled = true, SpeechWakeWordEnabled = true, SpeechPushToTalkEnabled = true,
            MantraChantEnabled = true,
        };
        s.Haptics.Enabled = true;
        return s;
    }

    [Fact]
    public void Lapse_ClearsEveryPremiumFlag_UnlessPatronOrFreeToday()
    {
        var old = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            var s = AllOn();
            Assert.Empty(EntitlementLapse.Enforce(s));
            Assert.True(s.KeywordTriggersEnabled && s.Haptics.Enabled && s.MantraChantEnabled);

            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = k => k == "takeover";
            s = AllOn();
            Assert.Equal(new[] { "awareness", "awareness-mode", "voice", "voice-mic", "haptics" }, EntitlementLapse.Enforce(s));
            Assert.False(s.KeywordTriggersEnabled || s.AwarenessModeEnabled || s.SpokenMantrasEnabled
                || s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled || s.Haptics.Enabled);
            Assert.True(s.AutonomyModeEnabled && s.MantraChantEnabled);   // the free day's two

            foreach (var key in new[] { "voice", "haptics", "awareness" })
            {
                CoreEntitlement.IsFreeTodayProvider = k => k == key;
                Assert.DoesNotContain(key, EntitlementLapse.Enforce(AllOn()));
            }

            CoreEntitlement.IsFreeTodayProvider = null;
            s = AllOn();
            Assert.Equal(7, EntitlementLapse.Enforce(s).Count);
            Assert.Empty(EntitlementLapse.Enforce(s));   // idempotent; never writes true
            Assert.False(s.AutonomyModeEnabled || s.MantraChantEnabled);
        }
        finally { (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = old; }
    }
}
