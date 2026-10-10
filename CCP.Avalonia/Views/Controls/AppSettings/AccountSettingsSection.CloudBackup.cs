using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// Cloud settings backup card (ledger G7 / HA8), ported from WPF MainWindow.CloudBackup.cs
    /// (BtnBackupSettingsNow_Click, BtnRestoreSettings_Click, UpdateBackupStatus). The service half is
    /// Core's <see cref="CloudSettingsBackup"/>: what is left out of a backup and what this machine
    /// keeps on a restore are decided there, never here.
    /// </summary>
    public partial class AccountSettingsSection
    {
        /// <summary>The client the card uses; null = no account services on this run (headless tests never
        /// start them), so the card makes no call. Tests swap in a fake-handler client.</summary>
        internal static Func<CloudSettingsBackup?> NewBackupClient =
            () => AccountSeed.Patreon == null ? null : new CloudSettingsBackup();

        private bool _backupBusy;

        private void BtnBackupSettingsNow_Click(object? sender, RoutedEventArgs e) => _ = BackupNowAsync();
        private void BtnRestoreSettings_Click(object? sender, RoutedEventArgs e) => _ = RestoreFromCloudAsync();

        // SetCurrentValue keeps the markup's {loc:Str} binding alive under the busy text.
        private void SetButtonText(string name, string key)
            => Find<TextBlock>(name)?.SetCurrentValue(TextBlock.TextProperty, Loc.Get(key));

        internal async Task BackupNowAsync()
        {
            if (_backupBusy || NewBackupClient() is not { } client) return;
            var btn = Find<Button>("BtnBackupSettingsNow");
            _backupBusy = true;
            if (btn != null) btn.IsEnabled = false;
            SetButtonText("TxtBtnBackupNow", "btn_backing_up");
            try
            {
                var ok = await client.BackupAsync();
                if (ok) await UpdateBackupStatusAsync(client);
                if (OwnerWindow is { } owner)
                    await MessageDialog.ShowAsync(owner,
                        Loc.Get(ok ? "title_backup_complete" : "title_backup_failed"),
                        Loc.Get(ok ? "msg_settings_backed_up_to_cloud_successfully" : "msg_failed_to_backup_settings"));
            }
            catch (Exception ex)
            {
                Log.Warning("Manual settings backup failed: {E}", ex.Message);
            }
            finally
            {
                _backupBusy = false;
                if (btn != null) btn.IsEnabled = true;
                SetButtonText("TxtBtnBackupNow", "btn_backup_now");
            }
        }

        internal async Task RestoreFromCloudAsync()
        {
            if (_backupBusy || NewBackupClient() is not { } client) return;
            // RestoreFrom swaps the whole settings object, so mid-session every prescribed value is
            // gone at once: refused exactly as WPF refuses it ("cloud-restore-settings").
            if (Shell?.RefuseActionIfSessionLocked("cloud-restore-settings") ?? CoreSession.IsSessionRunning) return;
            var owner = OwnerWindow;
            if (owner != null && !await MessageDialog.ConfirmAsync(owner,
                    Loc.Get("title_restore_settings_from_cloud"), Loc.Get("msg_restore_settings_confirm"), defaultToCancel: true))
                return;

            var btn = Find<Button>("BtnRestoreSettings");
            _backupBusy = true;
            if (btn != null) btn.IsEnabled = false;
            SetButtonText("TxtBtnRestore", "btn_restoring");
            try
            {
                var restored = await client.DownloadAsync();
                // A session may have started while the dialog or the download was up.
                if (restored == null || CoreSession.IsSessionRunning || CoreSettings.Service is not { } service)
                {
                    if (owner != null)
                        await MessageDialog.ShowAsync(owner, Loc.Get("title_restore_failed"), Loc.Get("msg_no_cloud_backup_found_or_restore_failed"));
                    return;
                }
                CloudSettingsBackup.ApplyLocalWins(service.Current, restored);
                service.RestoreFrom(restored);   // raises CurrentReplaced: every page re-seeds from it
                this.FindAncestorOfType<Tabs.AppSettingsTabView>()?.RefreshSections();
                RefreshProviderRows();
                if (owner != null)
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_settings_restored"), Loc.Get("msg_settings_restored_from_cloud"));
            }
            catch (Exception ex)
            {
                Log.Warning("Manual settings restore failed: {E}", ex.Message);
                if (owner != null)
                    await MessageDialog.ShowAsync(owner, Loc.Get("title_restore_error"), Loc.GetF("msg_restore_failed_0", ex.Message));
            }
            finally
            {
                _backupBusy = false;
                if (btn != null) btn.IsEnabled = true;
                SetButtonText("TxtBtnRestore", "btn_restore_from_cloud");
            }
        }

        /// <summary>WPF UpdateBackupStatus: the last backup's date and version, "none yet", or "could not check".</summary>
        internal async Task UpdateBackupStatusAsync(CloudSettingsBackup? client = null)
        {
            var line = Find<TextBlock>("TxtCloudBackupStatus");
            if (line == null || !HasUnifiedId) return;
            client ??= NewBackupClient();
            if (client == null) return;
            try
            {
                var info = await client.GetInfoAsync();
                line.Text = info?.BackedUpAt is { } at
                    ? Loc.GetF("label_last_backup_0_v_1", at.ToLocalTime().ToString("MMM d, yyyy h:mm tt"), info.AppVersion)
                    : Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
            }
            catch (Exception ex)
            {
                Log.Debug("Failed to update backup status: {E}", ex.Message);
                line.Text = Loc.Get("label_could_not_check_backup_status");
            }
        }
    }
}
