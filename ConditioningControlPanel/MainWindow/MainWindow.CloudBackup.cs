using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Cloud settings backup: upload/restore of settings to server.
    public partial class MainWindow
    {
        #region Cloud Settings Backup

        /// <summary>
        /// Re-seeds the whole settings UI from the (just-swapped) <c>App.Settings.Current</c>.
        /// <para>
        /// The manual restore above does this inline; the STARTUP restore prompt
        /// (<c>App.CheckCloudSettingsRestoreAsync</c> -> <c>ApplyRestoredSettings</c>) used to call
        /// only <see cref="ApplySessionSettings"/>, which touches the engine's live values and
        /// leaves every Settings-door control painted from the discarded instance. Same restore,
        /// same refresh.
        /// </para>
        /// </summary>
        internal void ReloadSettingsUiAfterRestore()
        {
            try
            {
                _isLoading = true;
                LoadSettings();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Settings UI reload after a cloud restore failed");
            }
            finally { _isLoading = false; }
        }

        internal async void BtnBackupSettingsNow_Click(object sender, RoutedEventArgs e)
        {
            if (App.ProfileSync == null) return;

            AppSettingsTab.BtnBackupSettingsNow.IsEnabled = false;
            AppSettingsTab.BtnBackupSettingsNow.Content = Loc.Get("btn_backing_up");

            try
            {
                var success = await App.ProfileSync.BackupSettingsAsync(force: true);

                if (success)
                {
                    MessageBox.Show(
                        Loc.Get("msg_settings_backed_up_to_cloud_successfully"),
                        Loc.Get("title_backup_complete"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await UpdateBackupStatus();
                }
                else
                {
                    MessageBox.Show(
                        Loc.Get("msg_failed_to_backup_settings"),
                        Loc.Get("title_backup_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Manual settings backup failed");
                MessageBox.Show(
                    Loc.GetF("msg_backup_failed_0", ex.Message),
                    Loc.Get("title_backup_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                AppSettingsTab.BtnBackupSettingsNow.IsEnabled = true;
                AppSettingsTab.BtnBackupSettingsNow.Content = Loc.Get("btn_backup_now");
            }
        }

        internal async void BtnRestoreSettings_Click(object sender, RoutedEventArgs e)
        {
            if (App.ProfileSync == null) return;
            // SettingsService.RestoreFrom swaps the whole AppSettings object, so mid-session every
            // prescribed value is gone at once - the largest blast radius of any bypass, even
            // though this tab is rarely visited during a session.
            if (RefuseActionIfSessionLocked("cloud-restore-settings")) return;

            var confirm = MessageBox.Show(
                Loc.Get("msg_restore_settings_confirm"),
                Loc.Get("title_restore_settings_from_cloud"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            AppSettingsTab.BtnRestoreSettings.IsEnabled = false;
            AppSettingsTab.BtnRestoreSettings.Content = Loc.Get("btn_restoring");

            try
            {
                var restored = await App.ProfileSync.RestoreSettingsFromCloudAsync();

                if (restored == null)
                {
                    MessageBox.Show(
                        Loc.Get("msg_no_cloud_backup_found_or_restore_failed"),
                        Loc.Get("title_restore_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Preserve identity/progression fields from current settings
                var current = App.Settings?.Current;
                if (current != null)
                {
                    restored.UnifiedId = current.UnifiedId;
                    restored.PlayerLevel = current.PlayerLevel;
                    restored.PlayerXP = current.PlayerXP;
                    restored.SkillPoints = current.SkillPoints;
                    restored.UnlockedSkills = current.UnlockedSkills;
                    restored.HighestLevelEver = current.HighestLevelEver;
                    restored.IsSeason0Og = current.IsSeason0Og;
                    restored.CurrentSeason = current.CurrentSeason;
                    restored.PendingSkillsResetAck = current.PendingSkillsResetAck;
                    restored.UserDisplayName = current.UserDisplayName;
                    restored.PatreonTier = current.PatreonTier;
                    restored.PatreonPremiumValidUntil = current.PatreonPremiumValidUntil;
                    // Tier-2 twin of the line above: a local entitlement window, never the backup's.
                    restored.PatreonLabValidUntil = current.PatreonLabValidUntil;
                    restored.LastPatreonVerification = current.LastPatreonVerification;
                    restored.OpenRouterApiKey = current.OpenRouterApiKey;
                }

                App.Settings?.RestoreFrom(restored);

                _isLoading = true;
                LoadSettings();
                _isLoading = false;

                MessageBox.Show(
                    Loc.Get("msg_settings_restored_from_cloud"),
                    Loc.Get("title_settings_restored"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Manual settings restore failed");
                MessageBox.Show(
                    Loc.GetF("msg_restore_failed_0", ex.Message),
                    Loc.Get("title_restore_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                AppSettingsTab.BtnRestoreSettings.IsEnabled = true;
                AppSettingsTab.BtnRestoreSettings.Content = Loc.Get("btn_restore_from_cloud");
            }
        }

        internal async void BtnExportData_Click(object sender, RoutedEventArgs e)
        {
            if (App.ProfileSync == null) return;

            AppSettingsTab.BtnExportData.IsEnabled = false;
            AppSettingsTab.BtnExportData.Content = Loc.Get("btn_exporting");

            try
            {
                var (success, error, jsonData) = await App.ProfileSync.ExportDataAsync();

                if (!success || jsonData == null)
                {
                    MessageBox.Show(
                        error ?? Loc.Get("msg_failed_to_export_data"),
                        Loc.Get("title_export_failed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"my-data-export-{DateTime.Now:yyyy-MM-dd}.json",
                    Filter = "JSON files (*.json)|*.json",
                    Title = Loc.Get("title_save_data_export")
                };

                if (dialog.ShowDialog() == true)
                {
                    File.WriteAllText(dialog.FileName, jsonData);
                    MessageBox.Show(
                        Loc.GetF("msg_data_exported_to_0", dialog.FileName),
                        Loc.Get("title_export_complete"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Data export failed");
                MessageBox.Show(
                    Loc.GetF("msg_export_failed_0", ex.Message),
                    Loc.Get("title_export_error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                AppSettingsTab.BtnExportData.IsEnabled = true;
                AppSettingsTab.BtnExportData.Content = Loc.Get("btn_export_my_data");
            }
        }

        internal void BtnPrivacyPolicy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://cclabs.app/privacy-policy.html",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to open privacy policy");
            }
        }

        private async Task UpdateBackupStatus()
        {
            try
            {
                if (App.ProfileSync == null || !App.HasCloudIdentity) return;

                var info = await App.ProfileSync.GetSettingsBackupInfoAsync();

                if (info?.BackedUpAt != null)
                {
                    var dateStr = info.BackedUpAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
                    AppSettingsTab.TxtCloudBackupStatus.Text = Loc.GetF("label_last_backup_0_v_1", dateStr, info.AppVersion);
                }
                else
                {
                    AppSettingsTab.TxtCloudBackupStatus.Text = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to update backup status: {Error}", ex.Message);
                AppSettingsTab.TxtCloudBackupStatus.Text = Loc.Get("label_could_not_check_backup_status");
            }
        }

        #endregion
    }
}
