// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs (2195 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (78):
//   internal void BtnGateUnlock_Click(…)
//   private void RefreshPremiumGate(…)
//   private void EnforceEntitlementLapse(…)
//   internal void RefreshEntitlementVeils(…)
//   private void UpdatePatreonUI(…)
//   internal async void BtnPatreonLogin_Click(…)
//   internal async void BtnDiscordLogin_Click(…)
//   private void UpdateDiscordUI(…)
//   private void UpdateAccountLinkingUI(…)
//   internal async void BtnLinkPatreon_Click(…)
//   internal async void BtnLinkDiscord_Click(…)
//   internal void OfferAchievementSharingAfterDiscordLink(…)
//   internal void ChkShareAchievements_Changed(…)
//   internal void ChkShareLevelUps_Changed(…)
//   internal void ChkShowLevelInPresence_Changed(…)
//   internal async void ChkAllowDiscordDm_Changed(…)
//   internal async void ChkShareProfilePicture_Changed(…)
//   internal async void ChkPublicShareRealAvatar_Changed(…)
//   internal async void ChkGoonShareAvatar_Changed(…)
//   internal async void ChkGoonShareDiscordDm_Changed(…)
//   internal void ChkGoonRichPresence_Changed(…)
//   internal async void ChkShowOnlineStatus_Changed(…)
//   internal void BtnVisitPatreon_Click(…)
//   private void OnPatreonTierChanged(…)
//   private void MaybeShowPremiumCelebration(…)
//   private void InitializePatreonTab(…)
//   internal void BtnDetachCompanion_Click(…)
//   internal void BtnCustomizeCompanion_Click(…)
//   internal void BtnManagePhrases_Click(…)
//   private void UpdatePhraseCountDisplay(…)
//   private void RestoreCompanionSectionStates(…)
//   internal const string CompanionEngineDrawerKey
//   internal const string CompanionWorkshopDrawerKey
//   internal void PersistCompanionDrawerStates(…)
//   internal void SliderIdleInterval_ValueChanged(…)
//   internal void SliderBubbleDuration_ValueChanged(…)
//   internal void ChkTriggerMode_Changed(…)
//   public void SyncTriggerModeUI(…)
//   internal void SliderTriggerInterval_ValueChanged(…)
//   internal void BtnEditTriggers_Click(…)
//   internal void BtnPrivacySpoiler_Click(…)
//   internal void SliderAwarenessCooldown_ValueChanged(…)
//   internal void SliderAwarenessCooldownMax_ValueChanged(…)
//   internal void BtnSwitchCompanion_Click(…)
//   internal void RevealCompanionWorkshopCell(…)
//   private void OnCompanionProviderSelected(…)
//   private async Task MaybeOfferLocalAiSetupAsync(…)
//   internal void BtnSetupLocalAi_Click(…)
//   internal void BtnLabEffectsSetupLocal_Click(…)
//   private void LaunchLocalAiSetupWizard(…)
//   private static void CommitFocusedEdit(…)
//   internal async void BtnTestOllamaConnection_Click(…)
//   internal void BtnOpenAiSamplerSettings_Click(…)
//   internal async void BtnTestOpenAiConnection_Click(…)
//   internal void BtnClearChatMemory_Click(…)
//   internal void ChkChatMemoryEnabled_Changed(…)
//   internal void ChkCapEffects_Changed(…)
//   internal void ChkAllowEffect_Changed(…)
//   internal void SliderMaxHapticIntensity_ValueChanged(…)
//   private void UpdateAiBrainPills(…)
//   private static bool ProviderSupportsEffects(…)
//   private void UpdateLiveActionsPlaceholder(…)
//   internal void SyncLabEffectPermsUI(…)
//   private void SyncAiBrainUI(…)
//   internal void ChkMuteWhispers_Changed(…)
//   internal async void ChkPauseBrowser_Changed(…)
//   internal void ChkVoiceLines_Changed(…)
//   private async Task SetBrowserPaused(…)
//   public void SyncQuickControlsUI(…)
//   public void SyncWhispersUI(…)
//   private int _preMuteMasterVolume
//   public void ApplyVoiceMute(…)
//   public void AdjustMasterVolume(…)
//   internal async void BtnRefreshPrompts_Click(…)
//   internal void BtnDeactivatePrompt_Click(…)
//   internal async void BtnBrowsePrompts_Click(…)
//   internal void BtnImportPrompt_Click(…)
//   internal async void BtnExportPrompt_Click(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.Patreon.cs; wired when they move to Core.
        private void BtnManagePhrases_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
