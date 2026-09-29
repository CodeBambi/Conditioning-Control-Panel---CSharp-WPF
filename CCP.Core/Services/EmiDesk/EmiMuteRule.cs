using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The settings half of EMI's "is anything going to talk over her?" probe, shared by every head
/// (WPF <c>EmiDeskService.AnyTalkingFeatureLive</c> adds its live services - Autonomy, remote
/// controller - on top). Pure: no services, no clock.
/// </summary>
public static class EmiMuteRule
{
    /// <summary>Wake word, AI chat, or consented Awareness is switched on.</summary>
    public static bool SettingsTalk(AppSettings s) =>
        s.SpeechWakeWordEnabled || s.AiChatEnabled || (s.AwarenessModeEnabled && s.AwarenessConsentGiven);
}
