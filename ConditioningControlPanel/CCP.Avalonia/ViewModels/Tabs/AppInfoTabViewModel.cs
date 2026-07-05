using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Update;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.AccountShell and MainWindow.CloudBackup partials.
/// Hosts account actions, update checks, cloud backup/export, and support links.
/// </summary>
public partial class AppInfoTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly ISettingsBackupProvider? _backupProvider;
    private readonly IProfileSyncService? _profileSync;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<AppInfoTabViewModel>? _logger;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly IUpdateService? _updateService;

    public AppInfoTabViewModel() : base("appinfo", "App Info", "ℹ️")
    {
        LanguageOptions = new ObservableCollection<LanguageOption>();
        InitializeLanguages();
        VersionText = $"v{Core.Services.Update.UpdateService.GetCurrentVersion()}";
    }

    public AppInfoTabViewModel(
        ISettingsService settingsService,
        ISettingsBackupProvider backupProvider,
        IDialogService dialogService,
        ILogger<AppInfoTabViewModel> logger,
        IAudioPlayer? audioPlayer = null,
        IUpdateService? updateService = null,
        IProfileSyncService? profileSync = null) : base("appinfo", "App Info", "ℹ️")
    {
        _settingsService = settingsService;
        _backupProvider = backupProvider;
        _profileSync = profileSync;
        _dialogService = dialogService;
        _logger = logger;
        _audioPlayer = audioPlayer;
        _updateService = updateService;
        LanguageOptions = new ObservableCollection<LanguageOption>();
        InitializeLanguages();
        VersionText = $"v{Core.Services.Update.UpdateService.GetCurrentVersion()}";
        _ = RefreshBackupStatusAsync();
    }

    [ObservableProperty]
    private string _patreonButtonText = Loc.Get("btn_login_patreon");

    [ObservableProperty]
    private string _discordButtonText = Loc.Get("btn_login_discord");

    [ObservableProperty]
    private string _backupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");

    [ObservableProperty]
    private bool _isRichPresenceEnabled;

    [ObservableProperty]
    private ObservableCollection<LanguageOption> _languageOptions;

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _versionText = $"v{Core.Services.Update.UpdateService.GetCurrentVersion()}";

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null || _settingsService?.Current == null) return;
        var current = _settingsService.Current;
        if (current.Language == value.Code) return;

        current.Language = value.Code;
        LocalizationManager.Instance.SetLanguage(value.Code);
        _settingsService.Save();
        _logger?.LogInformation("Language changed to {Language}", value.Code);
    }

    private void InitializeLanguages()
    {
        var current = _settingsService?.Current?.Language ?? LocalizationManager.Instance.CurrentLanguage ?? "en";
        foreach (var (code, displayName, shortName) in LocalizationManager.AvailableLanguages)
        {
            var option = new LanguageOption(code, $"🌐 {shortName}", displayName);
            LanguageOptions.Add(option);
            if (code == current) SelectedLanguage = option;
        }
    }

    [RelayCommand]
    private async Task QuickPatreonLoginAsync()
    {
        // Legacy OAuth flow is WPF-only. Avalonia uses a unified login dialog (future work).
        _logger?.LogInformation("Patreon login requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_patreon_login_dialog_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task QuickDiscordLoginAsync()
    {
        _logger?.LogInformation("Discord login requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_not_implemented"),
            Loc.Get("msg_discord_login_dialog_not_yet_ported")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenDiscordInviteAsync()
    {
        try
        {
            OpenUrl("https://discord.gg/YxVAMt4qaZ");
            _logger?.LogInformation("Opened Discord invite link");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open Discord link");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ToggleRichPresenceAsync()
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordRichPresenceEnabled = IsRichPresenceEnabled;
        _settingsService.Save();
        _logger?.LogInformation("Discord Rich Presence {Status}", IsRichPresenceEnabled ? "enabled" : "disabled");
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task BackupSettingsAsync()
    {
        IsBusy = true;
        try
        {
            // Explicit user action → force past the sync service's 5-minute debounce
            // (WPF BtnBackupSettingsNow_Click, MainWindow.CloudBackup.cs:43). Falls back to the
            // composed provider (local snapshot + debounced cloud) when sync is unavailable.
            bool success;
            if (_profileSync != null)
            {
                success = await _profileSync.BackupSettingsAsync(force: true);
            }
            else
            {
                await (_backupProvider?.BackupSettingsAsync() ?? Task.CompletedTask);
                success = _backupProvider != null;
            }

            await RefreshBackupStatusAsync();
            if (success)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_backup_complete"),
                    Loc.Get("msg_settings_backed_up_to_cloud_successfully")) ?? Task.CompletedTask);
            }
            else
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_backup_failed"),
                    Loc.Get("msg_failed_to_backup_settings"),
                    DialogSeverity.Warning) ?? Task.CompletedTask);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Manual settings backup failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_backup_failed"),
                Loc.Get("msg_failed_to_backup_settings"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreSettingsAsync()
    {
        if (_settingsService == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_restore_settings_from_cloud"),
            Loc.Get("msg_restore_settings_confirm")) ?? Task.FromResult(false));
        if (!confirm) return;

        IsBusy = true;
        try
        {
            // WPF BtnRestoreSettings_Click (MainWindow.CloudBackup.cs:82): download + decompress,
            // then preserve identity/progression fields from the CURRENT settings before applying —
            // the cloud backup strips them (P0 exclusion list), so the restored object would
            // otherwise zero them out.
            var restored = _profileSync != null ? await _profileSync.RestoreSettingsFromCloudAsync() : null;
            if (restored == null)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_restore_failed"),
                    Loc.Get("msg_no_cloud_backup_found_or_restore_failed"),
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            var current = _settingsService.Current;
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
                restored.LastPatreonVerification = current.LastPatreonVerification;
                restored.OpenRouterApiKey = current.OpenRouterApiKey;
            }

            _settingsService.RestoreFrom(restored);

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_settings_restored"),
                Loc.Get("msg_settings_restored_from_cloud")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Manual settings restore failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_restore_error"),
                Loc.GetF("msg_restore_failed_0", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportDataAsync()
    {
        if (_settingsService?.Current == null) return;

        IsBusy = true;
        try
        {
            // GDPR export (WPF BtnExportData_Click, MainWindow.CloudBackup.cs:160): all
            // SERVER-held data as pretty JSON. The previous local-settings serialization stub is
            // gone — it was not the user's server data and serialized secret-bearing fields.
            var (success, error, jsonData) = _profileSync != null
                ? await _profileSync.ExportDataAsync()
                : (false, (string?)Loc.Get("msg_failed_to_export_data"), (string?)null);

            if (!success || jsonData == null)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_export_failed"),
                    error ?? Loc.Get("msg_failed_to_export_data"),
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            var path = await (_dialogService?.ShowSaveFileDialogAsync(
                Loc.Get("title_save_data_export"),
                new[] { new FileFilter("JSON files", new[] { "json" }) },
                $"my-data-export-{DateTime.Now:yyyy-MM-dd}.json") ?? Task.FromResult<string?>(null));

            if (string.IsNullOrEmpty(path)) return;

            await File.WriteAllTextAsync(path, jsonData);

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_complete"),
                Loc.GetF("msg_data_exported_to_0", path)) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Data export failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_error"),
                Loc.GetF("msg_export_failed_0", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenPrivacyPolicyAsync()
    {
        try
        {
            OpenUrl("https://cclabs.app/privacy-policy.html");
            _logger?.LogInformation("Opened privacy policy");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open privacy policy");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
    }

    private async Task RefreshBackupStatusAsync()
    {
        try
        {
            if (_backupProvider?.HasCloudIdentity != true)
            {
                BackupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
                return;
            }

            // WPF UpdateBackupStatus (MainWindow.CloudBackup.cs:229): show the cloud backup's
            // timestamp + app version when one exists.
            var info = _profileSync != null ? await _profileSync.GetSettingsBackupInfoAsync() : null;
            if (info?.BackedUpAt != null)
            {
                var dateStr = info.BackedUpAt.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
                BackupStatusText = Loc.GetF("label_last_backup_0_v_1", dateStr, info.AppVersion);
            }
            else
            {
                BackupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to update backup status");
            BackupStatusText = Loc.Get("label_could_not_check_backup_status");
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task SmokeTestAudioAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "AwarenessPresets", "audio", "chime.wav");
            if (!File.Exists(path))
            {
                _logger?.LogWarning("Smoke-test audio sample not found: {Path}", path);
                await (_dialogService?.ShowMessageAsync(Loc.Get("title_audio_smoke_test"), Loc.Get("msg_sample_not_found"), DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            await (_audioPlayer?.PlayAsync(path) ?? Task.CompletedTask);
            await Task.Delay(1200);
            _audioPlayer?.Stop();
            _logger?.LogInformation("Audio smoke-test completed");
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_audio_smoke_test"), Loc.Get("msg_played_sample_successfully")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Audio smoke-test failed");
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_audio_smoke_test"), string.Format(Loc.Get("msg_smoke_test_failed_fmt"), ex.Message), DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task SmokeTestVideoAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos", "_test_loop.mp4");
            if (!File.Exists(path))
            {
                _logger?.LogWarning("Smoke-test video sample not found: {Path}", path);
                await (_dialogService?.ShowMessageAsync(Loc.Get("title_video_smoke_test"), Loc.Get("msg_sample_not_found"), DialogSeverity.Warning) ?? Task.CompletedTask);
                return;
            }

            var videoService = App.Services?.GetService<IVideoService>();
            videoService?.PlaySpecificVideo(path, false);
            await Task.Delay(3000);
            videoService?.Stop();
            _logger?.LogInformation("Video smoke-test completed");
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_video_smoke_test"), Loc.Get("msg_played_sample_successfully")) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Video smoke-test failed");
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_video_smoke_test"), string.Format(Loc.Get("msg_smoke_test_failed_fmt"), ex.Message), DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        try
        {
            IsBusy = true;
            _logger?.LogInformation("Manual update check requested");
            var update = _updateService != null
                ? await _updateService.CheckForUpdatesAsync(forceCheck: true)
                : null;
            if (update?.IsNewer == true)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("update_available_title"),
                    Loc.GetF("update_available_body_0", update.Version)) ?? Task.CompletedTask);
            }
            else
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("up_to_date_title"),
                    Loc.Get("up_to_date_body")) ?? Task.CompletedTask);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Manual update check failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.GetF("update_check_failed_0", ex.Message),
                DialogSeverity.Warning) ?? Task.CompletedTask);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenBugReportAsync()
    {
        _logger?.LogInformation("Bug report requested from App Info");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("bug_report_title"),
            Loc.Get("bug_report_error_toast")) ?? Task.CompletedTask);
    }
}

public sealed record LanguageOption(string Code, string DisplayName, string ToolTip);
