// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Login.cs (399 lines): the login dialog,
// logout and the painting of signed-in state.
//
// Deliberately NOT here yet (units 6-7 of the account plan): ProfileSyncService (heartbeat, profile
// load, sync push), the account-switch progression wipe (ClearProgressionData, XP watermark #865),
// quest/achievement resets and the Descent/V2Purchase caches. Logout clears tokens and identity only.

using System;
using Avalonia.Controls;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
        /// </summary>
        /// <param name="accountChanged">A real sign-in/out: the lapse pass saves, as WPF's does.</param>
        internal void UpdateQuickLoginUI(bool accountChanged = false)
        {
            try
            {
                var s = CoreSettings.Current;
                // Not WPF's `s.UnifiedId ||`: a stored id with nothing to prove it is not restored (AccountSeed.RestoreSession).
                var isLoggedIn = CoreAccount.IsLoggedIn;
                var displayName = s.UserDisplayName ?? AccountSeed.Patreon?.DisplayName ?? AccountSeed.Discord?.DisplayName
                                  ?? AccountSeed.SubscribeStar?.DisplayName ?? "User";

                var settings = SettingsPage;
                SetVisible(settings?.FindControl<Button>("BtnUnifiedLogin"), !isLoggedIn);
                SetVisible(settings?.FindControl<Border>("LoggedInStatusPanel"), isLoggedIn);

                SetVisible(Named<Tabs.QuestsTabView>("QuestsTab")?.FindControl<Border>("QuestsLoginOverlay"), !isLoggedIn);
                SetVisible(Named<Tabs.EnhancementsTabView>("EnhancementsTab")?.FindControl<Border>("EnhancementsLoginOverlay"), !isLoggedIn);

                if (isLoggedIn && settings?.FindControl<TextBlock>("TxtLoggedInName") is { } name)
                    name.Text = s.IsSeason0Og ? $"⭐ {displayName}" : displayName;
                // Sign-in, startup restore and logout all land here; WPF repaints the header on
                // each (Login.cs:198 UpdateLevelDisplay, OnProfileLoaded).
                UpdateLevelDisplay();
                // WPF UpdatePatreonUI -> RefreshEntitlementVeils: an account change moves every veil.
                RefreshEntitlementVeils(persist: accountChanged);
                // WPF Patreon.cs:294: the pass is per-account, so sign-in/out moves the intake door.
                App.IntakePass.RaiseChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "UpdateQuickLoginUI failed");
            }
        }

        private static bool _notRememberedShown;

        /// <summary>WPF OpenUnifiedLoginDialog, up to the sync it starts (units 6-7). Then, once per run,
        /// the "sign-in not remembered" notice when the tokens could only be kept in memory.</summary>
        internal async Task OpenUnifiedLoginDialog(Window? owner = null)
        {
            var dialog = new LoginDialog();
            if (await dialog.ShowDialogSafe<bool>(owner ?? this) && dialog.Result is not null)
            {
                UpdateQuickLoginUI(accountChanged: true);
                if (SecretStore.NotRemembered && !_notRememberedShown)
                {
                    _notRememberedShown = true;
                    await MessageDialog.ShowAsync(owner ?? this, Loc.Get("login_not_remembered_title"), Loc.Get("login_not_remembered_body"));
                }
                // The cloud profile may adopt a different level (WPF OnProfileLoaded -> UpdateLevelDisplay).
                try { await dialog.ProfileLoad; } catch (Exception ex) { Serilog.Log.Debug("ProfileLoad: {E}", ex.Message); }
                UpdateLevelDisplay();
            }
        }

        /// <summary>WPF BtnQuickLogout_Click (AccountSeed.Logout carries the pre-logout sync and progression clear).</summary>
        internal async void Logout() => await LogoutAsync();

        internal async Task LogoutAsync()
        {
            await AccountSeed.Logout();
            UpdateQuickLoginUI(accountChanged: true);
            if (SecretStore.ClearFailed)
                await MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.Get("logout_not_complete_body"));
        }

        private static void SetVisible(Control? control, bool visible)
        {
            if (control is not null) control.IsVisible = visible;
        }
    }
}
