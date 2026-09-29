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
// Members dropped (74; BtnGateUnlock_Click, RefreshPremiumGate, EnforceEntitlementLapse, RefreshEntitlementVeils restored below):
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

using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.Patreon.cs:1312. ponytail: UpdatePhraseCountDisplay skipped - the
        // Companion tab's TxtPhraseCount needs CompanionPhraseService, still head-side.
        internal void BtnManagePhrases_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => _ = new Dialogs.CompanionPhraseEditorDialog().ShowDialog(this);

        /// <summary>WPF MainWindow.Patreon.cs RefreshEntitlementVeils. Called on every navigation, on
        /// sign-in/out (UpdateQuickLoginUI), on either provider's TierChanged and on the ? box's
        /// TodayChanged (App.axaml.cs), as WPF MainWindow.xaml.cs:484 and OnPatreonTierChanged do.</summary>
        internal void RefreshEntitlementVeils() => RefreshEntitlementVeils(persist: false);

        /// <param name="persist">True only for an entitlement EVENT (tier change, day change, sign-in/out),
        /// which saves as WPF does. Startup and navigation clear the lapsed flags in memory only; see
        /// avalonia-decisions.md "Entitlement lapse: startup write deferred".</param>
        internal void RefreshEntitlementVeils(bool persist)
        {
            EnforceEntitlementLapse(persist);
            var haptics = StudioRack?.HapticsPanel;
            var hapticsOpen = CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("haptics");
            if (haptics?.FindControl<Control>("HapticsContentGrid") is { } grid)
            {
                grid.Opacity = hapticsOpen ? 1.0 : 0.3;
                grid.IsHitTestVisible = hapticsOpen;
            }
            foreach (var box in new[] { "HapticsConnectionBox", "HapticsFeatureBox" })
                if (haptics?.FindControl<Control>(box) is { } b) b.IsEnabled = hapticsOpen;

            RefreshPremiumGate(Named<Control>("BambiTakeoverTab"), "BambiTakeoverGate", "takeover");
            RefreshPremiumGate(haptics, "HapticsGate", "haptics");
            RefreshPremiumGate(Named<Control>("RemoteControlTab"), "RemoteControlGate", "remote");
            RefreshPremiumGate(Named<Control>("AwarenessTab"), "AwarenessGate", "awareness");
            RefreshPremiumGate(Named<Control>("LockdownTab"), "LockdownGate");
            RefreshPremiumGate(Named<Control>("SheListeningTab"), "SheListeningGate", "voice");
            try { Named<Tabs.PlayTabView>("PlayTab")?.RefreshPlayCards(); }
            catch (System.Exception ex) { Serilog.Log.Debug("RefreshPlayCards: {E}", ex.Message); } // WPF MainWindow.PlayTab.cs:121
        }

        /// <summary>WPF MainWindow.Patreon.cs:92, over the shared Core flag pass. Skipped while the seam is
        /// unseeded: WPF never runs it before App.Patreon exists, and a head that cannot read the account
        /// must not wipe a patron's settings. ponytail: this head runs none of the lapsed engines (no
        /// keyword hook, OCR, autonomy, mantra chant or haptics mixer), so there is nothing to stop yet.</summary>
        private bool _lapseUnsaved;   // cleared in memory, not yet written by an entitlement event

        private void EnforceEntitlementLapse(bool persist)
        {
            try
            {
                if (CoreEntitlement.HasPremiumProvider is null) return;
                var cleared = Services.EntitlementLapse.Enforce(CoreSettings.Current);
                foreach (var f in cleared) Serilog.Log.Information("Entitlement lapsed: {Feature} switched off", f);
                _lapseUnsaved |= cleared.Count > 0;
                if (persist && _lapseUnsaved)
                {
                    CoreSettings.Save();
                    _lapseUnsaved = false;
                }
                if (cleared.Count == 0) return;
                Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.SyncFromSettings();
                Named<Tabs.AwarenessTabView>("AwarenessTab")?.SyncAwarenessTabUi();
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "EnforceEntitlementLapse failed"); }
        }

        /// <summary>WPF MainWindow.Patreon.cs:63. ponytail: PremiumGateFx (the animated scrim) not ported.</summary>
        private static void RefreshPremiumGate(Control? tab, string gate, string? dailyKey = null)
        {
            if (tab?.FindControl<Control>(gate) is not { } g) return;
            g.IsVisible = !(CoreEntitlement.HasPremium || (dailyKey != null && CoreEntitlement.IsFreeToday(dailyKey)));
        }

        /// <summary>WPF MainWindow.Patreon.cs:46 -> ShowAppInfoPopup -> ShowAccountSettings.</summary>
        internal void BtnGateUnlock_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => OpenAppSettingsSection("account");

    }
}
