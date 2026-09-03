// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Login.cs (399 lines) - the one
// member of it that is about PAINTING login state rather than performing a login.
//
// WHAT IS REAL HERE: UpdateQuickLoginUI. Signed-in-ness on this head is
// CoreSettings.Current.UnifiedId, which the settings model already carries, so the login button,
// the signed-in strip, the OG star and the two gated tabs' login overlays all paint correctly
// from Core alone. It runs on OnLoaded (MainShellWindow.Marquee.cs, which owns that override)
// and again on every language change, so the gates are right from the first frame.
//
// TxtLoggedInName carries {loc:Str label_username} in the XAML and this writes over it, which
// the porting rules warn about - a language change reasserts the placeholder. There is no key to
// bind instead (it is a person's name), so the LanguageChanged re-run is the fix, not a comment.
//
// WHAT IS NOT, and precisely why. Nothing here is "wired when a service moves to Core":
//
//   internal void BtnUnifiedLogin_Click(…) / private void OpenUnifiedLoginDialog()
//       CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs DOES exist on this head, so the dialog is
//       not the blocker. What OpenUnifiedLoginDialog does AFTER it closes is: App.UnifiedUserId,
//       App.StartupUnifiedId and _lastKnownUnifiedId (account-switch detection),
//       ProfileSyncService (StartHeartbeat, LoadProfileAsync, SyncProfileAsync,
//       ResetLoadedProfileState), App.Achievements.SuppressPopups and App.Quests. None of those
//       has a seam. The click handler itself now belongs to
//       CCP.Avalonia/Views/Tabs/SettingsTabView.axaml.cs:329 (BtnUnifiedLogin_Click), where the
//       button lives - restoring it here would be a second, unreachable copy.
//
//   internal async void BtnQuickLogout_Click(…)
//       Same file, SettingsTabView.axaml.cs:330. Needs ProfileSyncService (a final SyncProfileAsync
//       then StopHeartbeat) and App.Patreon / App.Discord Logout().
//
//   private void ClearAccountData(bool wipeQuestProgress = false)
//       The identity half is pure settings (UnifiedId, AuthToken, UserDisplayName,
//       HasLinkedDiscord, HasLinkedPatreon) and would port verbatim onto CoreSettings. The rest
//       does not: App.SubscribeStar.Logout, App.Quests.StampOwner (the BUG-BN8X9B9SZ5 same-account
//       re-login stamp), ProfileSyncService.ResetLoadedProfileState, App.Descent.Reset. It has no
//       caller here either - only BtnQuickLogout_Click and account deletion reach it - so
//       restoring the settings half alone would be dead code that also silently skips four
//       teardown steps. Wire it WITH the logout path, not before it.
//
//       One line of it is worth naming on its own: AuthToken is nulled on logout because the
//       server rotates it on every auth event (#455). On this head that field is not persisted at
//       all - CoreSecrets is unseeded, which means NO STORE, never plaintext - so there is no
//       stale token to clear in the first place.
//
//   private void ClearProgressionData(bool generateQuests, bool preserveQuestProgress)
//       ~30 CoreSettings field writes (XP, level, skill points, both streak families, streak
//       shields, usage stats, rerolls, season/OG) that would port unchanged, plus four things
//       that would not: Services.ProfileSyncService.ClearXpWatermark (#865 - the ONLY surface
//       allowed to void the server-confirmed XP watermark, and it must go with the rest or the
//       next sync refuses the new account's numbers as a regression), App.Quests.ResetProgress,
//       App.Achievements.ResetProgress and DrawSkillTree. Same dead-code argument as above: its
//       only callers are the login/logout/delete paths.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Paints every surface that changes with signed-in-ness: the login button and the
        /// signed-in strip on the Settings door, and the login overlays that gate the Quests and
        /// Enhancements tabs.
        ///
        /// <para>Controls are found by name through the hosting view rather than through a
        /// generated field, so a view this head does not carry is skipped instead of throwing -
        /// and so it does not matter which XAML loader each tab view happened to use.</para>
        ///
        /// <para>ponytail: WPF also falls back to App.Patreon / App.Discord / App.SubscribeStar
        /// DisplayName, and ORs App.IsLoggedIn into the test. No provider exists on this head, so
        /// a session is "signed in" exactly when the settings model carries a UnifiedId.</para>
        /// </summary>
        internal void UpdateQuickLoginUI()
        {
            try
            {
                var s = CoreSettings.Current;
                var isLoggedIn = !string.IsNullOrEmpty(s.UnifiedId);
                var displayName = string.IsNullOrEmpty(s.UserDisplayName) ? "User" : s.UserDisplayName!;

                var settings = SettingsPage;
                SetVisible(settings?.FindControl<Button>("BtnUnifiedLogin"), !isLoggedIn);
                SetVisible(settings?.FindControl<Border>("LoggedInStatusPanel"), isLoggedIn);

                SetVisible(Named<Tabs.QuestsTabView>("QuestsTab")?.FindControl<Border>("QuestsLoginOverlay"), !isLoggedIn);
                SetVisible(Named<Tabs.EnhancementsTabView>("EnhancementsTab")?.FindControl<Border>("EnhancementsLoginOverlay"), !isLoggedIn);

                if (isLoggedIn && settings?.FindControl<TextBlock>("TxtLoggedInName") is { } name)
                    name.Text = s.IsSeason0Og ? $"⭐ {displayName}" : displayName;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "UpdateQuickLoginUI failed");
            }
        }

        private static void SetVisible(Control? control, bool visible)
        {
            if (control is not null) control.IsVisible = visible;
        }
    }
}
