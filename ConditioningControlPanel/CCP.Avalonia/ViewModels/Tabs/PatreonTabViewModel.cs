using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Patreon and MainWindow.SubscribeStar partials.
/// Drives the Patreon / Discord / SubscribeStar account cards and premium gating UI.
/// </summary>
public partial class PatreonTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly ISettingsBackupProvider? _backupProvider;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<PatreonTabViewModel>? _logger;
    private readonly IEnumerable<IAuthProvider>? _authProviders;
    private readonly IProfileSyncService? _profileSync;

    public PatreonTabViewModel() : base("patreon", "Patreon", "⭐")
    {
    }

    public PatreonTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<PatreonTabViewModel> logger,
        IEnumerable<IAuthProvider> authProviders,
        ISettingsBackupProvider? backupProvider = null,
        IProfileSyncService? profileSync = null) : base("patreon", "Patreon", "⭐")
    {
        _settingsService = settingsService;
        _backupProvider = backupProvider;
        _profileSync = profileSync;
        _dialogService = dialogService;
        _logger = logger;
        _authProviders = authProviders;
        RefreshUi();
        _ = RefreshBackupStatusAsync();
    }

    private IAuthProvider? GetProvider(string providerName)
        => _authProviders?.FirstOrDefault(p =>
            p.ProviderName.Equals(providerName, System.StringComparison.OrdinalIgnoreCase));

    #region Premium

    [ObservableProperty]
    private bool _isPremium;

    #endregion

    #region Patreon

    [ObservableProperty]
    private string _patreonStatusText = Loc.Get("label_not_connected");

    [ObservableProperty]
    private string _patreonTierText = Loc.Get("label_login_to_unlock_exclusive_features");

    [ObservableProperty]
    private string _patreonExpiryText = string.Empty;

    [ObservableProperty]
    private string _patreonButtonText = Loc.Get("btn_login");

    [RelayCommand]
    private async Task PatreonLoginAsync()
    {
        var provider = GetProvider("patreon");
        var settings = _settingsService?.Current;
        if (provider == null || settings == null)
        {
            _logger?.LogWarning("Patreon provider or settings not available");
            return;
        }

        if (settings.HasLinkedPatreon || provider.IsLoggedIn)
        {
            provider.Logout();
            settings.HasLinkedPatreon = false;
            settings.PatreonPremiumValidUntil = null;
            settings.PatreonTier = 0;
            _logger?.LogInformation("Patreon logged out");
        }
        else
        {
            _logger?.LogInformation("Patreon login requested");
            await provider.StartOAuthFlowAsync();
        }

        _settingsService?.Save();
        RefreshUi();
    }

    #endregion

    #region Discord

    [ObservableProperty]
    private string _discordStatusText = Loc.Get("label_not_connected");

    [ObservableProperty]
    private string _discordInfoText = Loc.Get("label_link_discord_for_community_features");

    [ObservableProperty]
    private string _discordButtonText = Loc.Get("btn_login");

    [RelayCommand]
    private async Task DiscordLoginAsync()
    {
        var provider = GetProvider("discord");
        var settings = _settingsService?.Current;
        if (provider == null || settings == null)
        {
            _logger?.LogWarning("Discord provider or settings not available");
            return;
        }

        if (settings.HasLinkedDiscord || provider.IsLoggedIn)
        {
            provider.Logout();
            settings.HasLinkedDiscord = false;
            _logger?.LogInformation("Discord logged out");
        }
        else
        {
            _logger?.LogInformation("Discord login requested");
            await provider.StartOAuthFlowAsync();
        }

        _settingsService?.Save();
        RefreshUi();
    }

    #endregion

    #region SubscribeStar

    [ObservableProperty]
    private string _subscribeStarStatusText = Loc.Get("label_not_connected");

    [ObservableProperty]
    private string _subscribeStarTierText = Loc.Get("label_login_to_unlock_exclusive_features");

    [ObservableProperty]
    private string _subscribeStarButtonText = Loc.Get("btn_login");

    [RelayCommand]
    private async Task SubscribeStarLoginAsync()
    {
        var provider = GetProvider("substar");
        var settings = _settingsService?.Current;
        if (provider == null || settings == null)
        {
            _logger?.LogWarning("SubscribeStar provider or settings not available");
            return;
        }

        if (provider.IsLoggedIn)
        {
            provider.Logout();
            _logger?.LogInformation("SubscribeStar logged out");
        }
        else
        {
            _logger?.LogInformation("SubscribeStar login requested");
            await provider.StartOAuthFlowAsync();
        }

        _settingsService?.Save();
        RefreshUi();
    }

    #endregion

    #region Account Linking

    [ObservableProperty]
    private bool _showLinkingSection;

    [ObservableProperty]
    private bool _showLinkPatreonButton;

    [ObservableProperty]
    private bool _showLinkDiscordButton;

    [ObservableProperty]
    private bool _showCloudBackupSection;

    [ObservableProperty]
    private bool _showDataPrivacySection;

    [RelayCommand]
    private async Task LinkPatreonAsync()
    {
        var settings = _settingsService?.Current;
        if (settings?.HasLinkedPatreon == true)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("tab_patreon_patreon_text"),
                Loc.Get("msg_patreon_already_linked")) ?? Task.CompletedTask);
            return;
        }

        await PatreonLoginAsync();
    }

    [RelayCommand]
    private async Task LinkDiscordAsync()
    {
        var settings = _settingsService?.Current;
        if (settings?.HasLinkedDiscord == true)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("tab_patreon_discord_text"),
                Loc.Get("msg_discord_already_linked")) ?? Task.CompletedTask);
            return;
        }

        await DiscordLoginAsync();
    }

    #endregion

    #region Cloud Backup

    [ObservableProperty]
    private string _backupStatusText = Loc.Get("label_checking_backup_status");

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task BackupNowAsync()
    {
        IsBusy = true;
        try
        {
            // Explicit user action → force past the sync service's 5-minute debounce (WPF
            // BtnBackupSettingsNow_Click, MainWindow.CloudBackup.cs:43). Falls back to the
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
    private async Task RestoreFromCloudAsync()
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
            // then preserve identity/progression fields from the CURRENT settings before applying
            // (the cloud backup strips them via the P0 exclusion list).
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
            await RefreshBackupStatusAsync();

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

    private async Task RefreshBackupStatusAsync()
    {
        try
        {
            if (_backupProvider?.HasCloudIdentity != true)
            {
                BackupStatusText = Loc.Get("label_no_cloud_backup_found_back_up_your_settings_t");
                return;
            }

            // WPF UpdateBackupStatus (MainWindow.CloudBackup.cs:229).
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

    #endregion

    #region Data & Privacy

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
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://cclabs.app/privacy-policy.html",
                UseShellExecute = true
            });
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

    #endregion

    #region Settings Toggles

    public bool DiscordShareAchievements
    {
        get => _settingsService?.Current?.DiscordShareAchievements ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.DiscordShareAchievements = value;
            OnPropertyChanged();
            _settingsService.Save();
        }
    }

    public bool DiscordShareLevelUps
    {
        get => _settingsService?.Current?.DiscordShareLevelUps ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.DiscordShareLevelUps = value;
            OnPropertyChanged();
            _settingsService.Save();
        }
    }

    public bool DiscordShowLevelInPresence
    {
        get => _settingsService?.Current?.DiscordShowLevelInPresence ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.DiscordShowLevelInPresence = value;
            OnPropertyChanged();
            _settingsService.Save();
        }
    }

    public bool AllowDiscordDm
    {
        get => _settingsService?.Current?.AllowDiscordDm ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.AllowDiscordDm = value;
            OnPropertyChanged();
            _settingsService.Save();
            _logger?.LogInformation("Allow Discord DM changed: {Value}", value);
        }
    }

    public bool ShareProfilePicture
    {
        get => _settingsService?.Current?.ShareProfilePicture ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.ShareProfilePicture = value;
            OnPropertyChanged();
            _settingsService.Save();
            _logger?.LogInformation("Share profile picture changed: {Value}", value);
        }
    }

    public bool ShowOnlineStatus
    {
        get => _settingsService?.Current?.ShowOnlineStatus ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.ShowOnlineStatus = value;
            OnPropertyChanged();
            _settingsService.Save();
            _logger?.LogInformation("Online status visibility changed: {Value}", value);
        }
    }

    [RelayCommand]
    private void ShareAchievementsChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordShareAchievements = value;
        _settingsService.Save();
    }

    [RelayCommand]
    private void ShareLevelUpsChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordShareLevelUps = value;
        _settingsService.Save();
    }

    [RelayCommand]
    private void ShowLevelInPresenceChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordShowLevelInPresence = value;
        _settingsService.Save();
    }

    [RelayCommand]
    private async Task AllowDiscordDmChangedAsync(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AllowDiscordDm = value;
        _settingsService.Save();
        _logger?.LogInformation("Allow Discord DM changed: {Value}", value);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ShareProfilePictureChangedAsync(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ShareProfilePicture = value;
        _settingsService.Save();
        _logger?.LogInformation("Share profile picture changed: {Value}", value);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ShowOnlineStatusChangedAsync(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ShowOnlineStatus = value;
        _settingsService.Save();
        _logger?.LogInformation("Online status visibility changed: {Value}", value);
        await Task.CompletedTask;
    }

    #endregion

    #region Help

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_help"),
            Loc.Get("patreon_help_body")) ?? Task.CompletedTask);
    }

    #endregion

    #region Actions

    [RelayCommand]
    private async Task VisitPatreonAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.patreon.com/CodeBambi",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open Patreon page");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task VisitSubscribeStarAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.subscribestar.com/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open SubscribeStar page");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                ex.Message,
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    #endregion

    private void RefreshUi()
    {
        var settings = _settingsService?.Current;
        var hasUnifiedId = !string.IsNullOrEmpty(settings?.UnifiedId);

        IsPremium = settings?.HasCachedPremiumAccess ?? false;

        // Patreon card
        var patreonProvider = GetProvider("patreon");
        var patreonLinked = settings?.HasLinkedPatreon == true || patreonProvider?.IsLoggedIn == true;
        if (patreonLinked)
        {
            PatreonStatusText = !string.IsNullOrEmpty(patreonProvider?.DisplayName)
                ? string.Format(Loc.Get("label_connected_as_0"), patreonProvider.DisplayName)
                : Loc.Get("label_connected");
            PatreonTierText = settings?.PatreonTier switch
            {
                1 => Loc.Get("label_patreon_tier_level1"),
                2 => Loc.Get("label_patreon_tier_level2"),
                _ => Loc.Get("label_patreon_tier_connected")
            };
            PatreonExpiryText = settings?.PatreonPremiumValidUntil.HasValue == true
                ? string.Format(Loc.Get("msg_premium_expires_fmt"), settings.PatreonPremiumValidUntil.Value)
                : string.Empty;
            PatreonButtonText = Loc.Get("btn_logout");
        }
        else
        {
            PatreonStatusText = Loc.Get("label_not_connected");
            PatreonTierText = Loc.Get("label_login_to_unlock_exclusive_features");
            PatreonExpiryText = string.Empty;
            PatreonButtonText = Loc.Get("btn_login");
        }

        // Discord card
        var discordProvider = GetProvider("discord");
        var discordLinked = settings?.HasLinkedDiscord == true || discordProvider?.IsLoggedIn == true;
        if (discordLinked)
        {
            DiscordStatusText = !string.IsNullOrEmpty(discordProvider?.DisplayName)
                ? string.Format(Loc.Get("label_connected_as_0"), discordProvider.DisplayName)
                : Loc.Get("label_connected");
            DiscordInfoText = Loc.Get("msg_discord_already_linked");
            DiscordButtonText = Loc.Get("btn_logout");
        }
        else
        {
            DiscordStatusText = Loc.Get("label_not_connected");
            DiscordInfoText = Loc.Get("label_link_discord_for_community_features");
            DiscordButtonText = Loc.Get("btn_login");
        }

        // SubscribeStar card
        var subscribeStarProvider = GetProvider("substar");
        if (subscribeStarProvider?.IsLoggedIn == true)
        {
            SubscribeStarStatusText = !string.IsNullOrEmpty(subscribeStarProvider.DisplayName)
                ? string.Format(Loc.Get("label_connected_as_0"), subscribeStarProvider.DisplayName)
                : Loc.Get("label_connected");
            SubscribeStarTierText = Loc.Get("msg_subscribestar_linked");
            SubscribeStarButtonText = Loc.Get("btn_logout");
        }
        else
        {
            SubscribeStarStatusText = Loc.Get("label_not_connected");
            SubscribeStarTierText = Loc.Get("label_login_to_unlock_exclusive_features");
            SubscribeStarButtonText = Loc.Get("btn_login");
        }

        // Linking section
        ShowLinkingSection = hasUnifiedId;
        ShowLinkPatreonButton = hasUnifiedId;
        ShowLinkDiscordButton = hasUnifiedId;
        ShowCloudBackupSection = hasUnifiedId;
        ShowDataPrivacySection = hasUnifiedId;
    }
}
