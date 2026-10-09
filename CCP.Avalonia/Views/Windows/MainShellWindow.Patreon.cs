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
// Members dropped (74; BtnGateUnlock_Click, RefreshPremiumGate, EnforceEntitlementLapse, RefreshEntitlementVeils,
// MaybeShowPremiumCelebration and OnPatreonTierChanged's celebration half restored below; UpdatePatreonUI,
// BtnPatreonLogin/Discord/Link*, UpdateAccountLinkingUI and BtnVisitPatreon live in AccountSettingsSection):
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

using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.Patreon.cs:1312. ponytail: UpdatePhraseCountDisplay skipped - the
        // Companion tab's TxtPhraseCount needs CompanionPhraseService, still head-side.
        internal void BtnManagePhrases_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => _ = new Dialogs.CompanionPhraseEditorDialog().ShowDialogSafe(this);

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
                // WPF MainWindow.Patreon.cs:108 - the lapse stops the engine, not just the flag.
                if (cleared.Contains("awareness-mode")) App.WindowAwareness.Stop();
                // WPF Patreon.cs:152 - the mirror case: entitlement resolving late re-arms what the
                // settings (and consent) already ask for. Start() re-checks everything itself.
                var s = CoreSettings.Current;
                if (s.AwarenessModeEnabled && s.AwarenessConsentGiven && ConditioningControlPanel.Services.WindowAwarenessService.HasEntitlement
                    && !App.WindowAwareness.IsRunning)
                    App.WindowAwareness.Start();
                if (cleared.Count == 0) return;
                Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.SyncFromSettings();
                Named<Tabs.AwarenessTabView>("AwarenessTab")?.SyncAwarenessTabUi();
                StudioRack?.HapticsPanel.LoadHapticsSettingsToUi();   // haptics Enabled may just have been cleared
                RefreshSheListeningTab();   // "voice" / "voice-mic" may just have been cleared
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "EnforceEntitlementLapse failed"); }
        }

        /// <summary>WPF MainWindow.Patreon.cs:144, with its PremiumGateFx decoration (fog, padlock
        /// glow, CTA sheen) - idempotent, never touches visibility or entitlement.</summary>
        private static void RefreshPremiumGate(Control? tab, string gate, string? dailyKey = null)
        {
            if (tab?.FindControl<Control>(gate) is not { } g) return;
            g.IsVisible = !(CoreEntitlement.HasPremium || (dailyKey != null && CoreEntitlement.IsFreeToday(dailyKey)));
            global::ConditioningControlPanel.Avalonia.Controls.PremiumGateFx.Attach(g as Border);
        }

        /// <summary>WPF MainWindow.Patreon.cs:48 (main fbe161de2): every padlock opens the vault gate card for the
        /// feature behind it, found from the tab view the button sits in. Graded Intake keeps Account; a patron
        /// whose grant died here is sent to reconnect (ReconnectFromGate) instead.</summary>
        internal void BtnGateUnlock_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (LockdownActive) return;   // PLAYBOOK P05: no veil on this head, so the door refuses itself
            if (TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
            var view = (sender as global::Avalonia.LogicalTree.ILogical)?.FindLogicalAncestorOfType<UserControl>(includeSelf: true)?.GetType().Name;
            if (Services.Vault.VaultOffer.KeepsAccountRoute(view)) { OpenAppSettingsSection("account"); return; }
            ShowVaultGate(Services.Vault.VaultOffer.FeatureKeyForView(view), 1);
        }

        /// <summary>WPF App.xaml.cs:505, seeded into TierGate.ReconnectIsTheAnswerProvider: a patron linked server-side
        /// whose grant on this PC is gone (or dead) and who has no premium now is told to reconnect, not sold a tier.</summary>
        internal static bool ReconnectIsTheAnswerNow()
        {
            var s = CoreSettings.Current;
            var p = Platform.AccountSeed.Patreon;
            return PatreonReconnectRule.Decide(
                hasUnifiedId: !string.IsNullOrEmpty(s.UnifiedId),
                linkedServerSide: s.HasLinkedPatreon,
                desktopAuthenticated: p?.IsAuthenticated == true && !p.GrantLooksDead,
                hasPremiumNow: CoreAccount.HasPremiumAccess,
                whitelisted: p?.IsWhitelisted == true).Prominent;
        }

        /// <summary>WPF StartPatreonReconnectFromGate. This head's Account Patreon button is empty, so the repair is
        /// the unified sign-in (which re-links Patreon). A seam so tests do not open the real dialog.</summary>
        internal System.Func<System.Threading.Tasks.Task>? ReconnectFromGateOverride;
        private System.Threading.Tasks.Task ReconnectFromGate() => ReconnectFromGateOverride?.Invoke() ?? OpenUnifiedLoginDialog();

        /// <summary>WPF App.xaml.cs:512 ShowDeniedHandler: the Reconnect toast for a patron whose grant died here,
        /// otherwise the refusal with "See tiers" opening the vault card at the tier this door needs.</summary>
        internal void ShowTierDenied(TierVerdict verdict, Helpers.NotificationService toasts)
        {
            if (TierGate.ReconnectIsTheAnswer())
                toasts.Show(Loc.Get("tiergate_denied_reconnect"), Helpers.NotificationType.Warning, System.TimeSpan.FromSeconds(10),
                    Loc.Get("tiergate_reconnect_action"), () => { if (!LockdownActive) _ = ReconnectFromGate(); });
            else
                toasts.Show(verdict.Reason, Helpers.NotificationType.Warning, System.TimeSpan.FromSeconds(8),
                    Loc.Get("tiergate_see_tiers"), () => ShowVaultGate(null, verdict.Required >= Models.PatreonTier.Level2 ? 2 : 1));
        }

        /// <summary>WPF ShowVaultGate. Null feature = the tier as a whole (the TierGate toast's "See tiers").</summary>
        internal void ShowVaultGate(string? featureKey, int tier)
        {
            if (LockdownActive) return;   // P05: the toast's "See tiers" is a door too
            try { Dialogs.VaultGateDialog.ShowOffer(this, featureKey, tier, () => OpenAppSettingsSection("account")); }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "[VaultGate] card failed; falling back to Account");
                OpenAppSettingsSection("account");
            }
        }

        /// <summary>WPF MainWindow.xaml.cs:3590 + Patreon.cs:1209: the invite week's last-day card, at launch and on focus.</summary>
        private void InitializeInviteEnding()
        {
            Opened += (_, _) => MaybeShowInviteEnding();
            Activated += (_, _) => MaybeShowInviteEnding();
        }

        private string? _inviteEndingPosted;

        /// <summary>WPF MaybeShowInviteEnding: once per week, through the presenter (an Inbox row when quiet).</summary>
        internal void MaybeShowInviteEnding()
        {
            try
            {
                var s = CoreSettings.Current;
                var key = Services.Vault.VaultOffer.InviteEndingOwed(s.InviteGrantUntil, System.DateTime.UtcNow,
                    ProviderSubscription.IsInviteWeekOnly(Platform.AccountSeed.Patreon, Platform.AccountSeed.SubscribeStar, s), s.SeenFeatureIntros);
                if (key == null || _inviteEndingPosted == key) return;
                _inviteEndingPosted = key;
                var until = s.InviteGrantUntil!.Value;
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:" + key,
                    Glyph = "⏳",
                    Title = Loc.Get("vaultgate_ending_inbox"),
                    Summary = Loc.GetF("vaultgate_ending_until", until.ToLocalTime().ToString("ddd d MMM, HH:mm")),
                    Open = () => { MarkIntroSeen(key); Dialogs.VaultGateDialog.ShowEnding(this, until, () => OpenAppSettingsSection("account")); },
                    Dismiss = () => MarkIntroSeen(key),
                });
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "[VaultGate] invite-ending check failed"); }
        }

        /// <summary>WPF InitializePatreonTab/InitializeSubscribeStarTab (TierChanged -> OnPatreonTierChanged /
        /// OnSubscribeStarTierChanged) and MainWindow_Loaded's launch re-check (MainWindow.xaml.cs:3559), for the
        /// celebration half; the veils already repaint from App.axaml.cs. Unsubscribed when the shell closes (P41).</summary>
        private void InitializePremiumCelebration()
        {
            Opened += (_, _) => MaybeShowPremiumCelebration();
            System.EventHandler<Models.PatreonTier> tier = (_, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(MaybeShowPremiumCelebration);
            var patreon = Platform.AccountSeed.Patreon;
            var substar = Platform.AccountSeed.SubscribeStar;
            if (patreon != null) patreon.TierChanged += tier;
            if (substar != null) substar.TierChanged += tier;
            Closed += (_, _) =>
            {
                if (patreon != null) patreon.TierChanged -= tier;
                if (substar != null) substar.TierChanged -= tier;
            };
        }

        /// <summary>WPF MaybeShowPremiumCelebration (MainWindow.Patreon.cs:1118): the one-time card per tier, re-read from
        /// the combined entitlement, never for an invite week, through the presenter (an Inbox row when quiet). The seen-flag
        /// is spent at open time. ponytail: no live-rise path (EntitlementTierSync.TierRaised, the immediate card and the
        /// profile-bubble fanfare) on this head yet, so every grant takes the presenter route.</summary>
        internal void MaybeShowPremiumCelebration()
        {
            try
            {
                if (!CoreAccount.HasPremiumAccess) return;
                var s = CoreSettings.Current;
                if (ProviderSubscription.IsInviteWeekOnly(Platform.AccountSeed.Patreon, Platform.AccountSeed.SubscribeStar, s)) return;
                var tier = CoreAccount.HasLabAccess ? 2 : 1;
                if (!TierCelebration.IsOwed(s.SeenFeatureIntros, tier, onRise: false)) return;
                var key = TierCelebration.KeyFor(tier)!;
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:" + key,
                    Glyph = "💖",
                    Title = Loc.Get(tier >= 2 ? "premium_celebration_inbox_t2" : "premium_celebration_inbox_t1"),
                    Summary = Loc.Get("premium_celebration_inbox_summary"),
                    Open = () => FeatureIntroPopup.ShowCelebrationIfFirstTime(this, key),
                });
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Premium celebration hook failed"); }
        }

        private static void MarkIntroSeen(string key)
        {
            var s = CoreSettings.Current;
            if (s.SeenFeatureIntros.Contains(key)) return;
            s.SeenFeatureIntros.Add(key);
            CoreSettings.Save();
        }

    }
}
