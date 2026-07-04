using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Services.Auth;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Help;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Settings partial top-level actions:
/// save, exit, help, bug report, tutorial launch, feature enable dashboard,
/// and embedded browser launcher.
/// Feature-specific settings live in their respective FeatureControl view-models.
/// </summary>
public partial class SettingsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<SettingsTabViewModel>? _logger;
    private readonly IBrowserHost? _browserHost;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly IAudioDeviceService? _audioDeviceService;
    private readonly IVideoService? _videoService;
    private bool _populatingAudioOutputs;

    /// <summary>
    /// Exposed so the view can request an embedded browser control from the platform host.
    /// </summary>
    public IBrowserHost? BrowserHost => _browserHost;

    public SettingsTabViewModel() : base("settings", "Dashboard", "📊")
    {
        _audioOutputDevices = new ObservableCollection<AudioDeviceInfo>();
    }

    public SettingsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<SettingsTabViewModel> logger,
        IBrowserHost browserHost,
        IAudioPlayer audioPlayer,
        IAudioDeviceService audioDeviceService,
        IVideoService? videoService = null) : base("settings", "Dashboard", "📊")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _browserHost = browserHost;
        _audioPlayer = audioPlayer;
        _audioDeviceService = audioDeviceService;
        _videoService = videoService;
        _audioOutputDevices = new ObservableCollection<AudioDeviceInfo>();
        RefreshAudioOutputDevices();
        RefreshFromSettings();
        SubscribeBrowserEvents();

        if (_settingsService?.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.UnifiedId)
            or nameof(AppSettings.UserDisplayName)
            or nameof(AppSettings.IsSeason0Og))
        {
            RefreshLoginState();
            return;
        }

        // Mirror any feature enable/disable that was changed from a feature popup
        // (or elsewhere) back onto the dashboard card bindings.
        if (e.PropertyName is nameof(AppSettings.FlashEnabled)
            or nameof(AppSettings.SubliminalEnabled)
            or nameof(AppSettings.SpiralEnabled)
            or nameof(AppSettings.PinkFilterEnabled)
            or nameof(AppSettings.BubblesEnabled)
            or nameof(AppSettings.LockCardEnabled)
            or nameof(AppSettings.MandatoryVideosEnabled)
            or nameof(AppSettings.BubbleCountEnabled)
            or nameof(AppSettings.BouncingTextEnabled)
            or nameof(AppSettings.MindWipeEnabled)
            or nameof(AppSettings.BrainDrainEnabled))
        {
            RefreshFromSettings();
        }
    }

    [ObservableProperty]
    private bool _isHelpOverlayVisible;

    [ObservableProperty]
    private string _helpOverlayTitle = Loc.Get("title_help");

    [ObservableProperty]
    private string _helpOverlayIcon = "?";

    [ObservableProperty]
    private string _helpOverlayWhatItDoes = "";

    [ObservableProperty]
    private string _helpOverlayHowItWorks = "";

    [ObservableProperty]
    private System.Collections.Generic.List<string> _helpOverlayTips = new();

    [ObservableProperty]
    private bool _helpOverlayHasTips;

    [ObservableProperty]
    private bool _helpOverlayHasHowItWorks;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isNotLoggedIn = true;

    [ObservableProperty]
    private string _loggedInDisplayName = "";

    #region Feature toggles

    [ObservableProperty]
    private bool _flashEnabled;

    [ObservableProperty]
    private bool _subliminalEnabled;

    [ObservableProperty]
    private bool _spiralEnabled;

    [ObservableProperty]
    private bool _pinkFilterEnabled;

    [ObservableProperty]
    private bool _bubblesEnabled;

    [ObservableProperty]
    private bool _lockCardEnabled;

    [ObservableProperty]
    private bool _videoEnabled;

    [ObservableProperty]
    private bool _bubbleCountEnabled;

    [ObservableProperty]
    private bool _bouncingTextEnabled;

    [ObservableProperty]
    private bool _mindWipeEnabled;

    [ObservableProperty]
    private string _customAssetsPath = "";

    partial void OnCustomAssetsPathChanged(string value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.CustomAssetsPath = value;
        Save();
    }

    [RelayCommand]
    private async Task BrowseCustomAssetsPathAsync()
    {
        var selected = await (_dialogService?.ShowOpenFolderDialogAsync(Loc.Get("title_select_custom_assets_folder")) ?? Task.FromResult<string?>(null));
        if (!string.IsNullOrWhiteSpace(selected))
            CustomAssetsPath = selected;
    }

    partial void OnFlashEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.FlashEnabled = value;
        Save();
    }

    partial void OnSubliminalEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.SubliminalEnabled = value;
        Save();
    }

    partial void OnSpiralEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.SpiralEnabled = value;
        Save();
    }

    partial void OnPinkFilterEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.PinkFilterEnabled = value;
        Save();
    }

    partial void OnBubblesEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BubblesEnabled = value;
        Save();
    }

    partial void OnLockCardEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.LockCardEnabled = value;
        Save();
    }

    partial void OnVideoEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.MandatoryVideosEnabled = value;
        Save();
    }

    partial void OnBubbleCountEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BubbleCountEnabled = value;
        Save();
    }

    partial void OnBouncingTextEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BouncingTextEnabled = value;
        Save();
    }

    partial void OnMindWipeEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.MindWipeEnabled = value;
        Save();
    }

    private void RefreshFromSettings()
    {
        if (_settingsService?.Current == null) return;
        FlashEnabled = _settingsService.Current.FlashEnabled;
        SubliminalEnabled = _settingsService.Current.SubliminalEnabled;
        SpiralEnabled = _settingsService.Current.SpiralEnabled;
        PinkFilterEnabled = _settingsService.Current.PinkFilterEnabled;
        BubblesEnabled = _settingsService.Current.BubblesEnabled;
        LockCardEnabled = _settingsService.Current.LockCardEnabled;
        VideoEnabled = _settingsService.Current.MandatoryVideosEnabled;
        BubbleCountEnabled = _settingsService.Current.BubbleCountEnabled;
        BouncingTextEnabled = _settingsService.Current.BouncingTextEnabled;
        MindWipeEnabled = _settingsService.Current.MindWipeEnabled;
        CustomAssetsPath = _settingsService.Current.CustomAssetsPath ?? "";
        AudioDuckEnabled = _settingsService.Current.AudioDuckingEnabled;
        MasterVolume = _settingsService.Current.MasterVolume;
        _audioPlayer?.SetVolume(MasterVolume / 100.0);
        VideoVolume = _settingsService.Current.VideoVolume;
        DuckVolume = _settingsService.Current.DuckingLevel;
        ExcludeBrowserDucking = _settingsService.Current.ExcludeBambiCloudFromDucking;
        DiscordRichPresenceEnabled = _settingsService.Current.DiscordRichPresenceEnabled;
        BrowserEnhanceIfPossible = _settingsService.Current.BrowserEnhanceIfPossible;
        RefreshEnhanceMatchStatus();

        var audioSync = _settingsService.Current.Haptics?.AudioSync;
        if (audioSync != null)
        {
            AudioSyncLatencyPanelVisible = audioSync.Enabled;
            LegacyAudioSyncLatency = audioSync.ManualLatencyOffsetMs;
            LegacyAudioSyncIntensity = (int)(audioSync.LiveIntensity * 100);
        }

        RefreshLoginState();
    }

    private void RefreshLoginState()
    {
        var settings = _settingsService?.Current;
        if (settings == null) return;

        var displayName = settings.UserDisplayName;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "User";
        if (settings.IsSeason0Og) displayName = $"⭐ {displayName}";

        LoggedInDisplayName = displayName;
        IsLoggedIn = !string.IsNullOrEmpty(settings.UnifiedId);
        IsNotLoggedIn = !IsLoggedIn;
    }

    #endregion

    #region Audio

    [ObservableProperty]
    private bool _audioDuckEnabled;

    partial void OnAudioDuckEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AudioDuckingEnabled = value;
        Save();
    }

    [ObservableProperty]
    private int _masterVolume;

    partial void OnMasterVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.MasterVolume = value;
        MasterVolumeText = $"{value}%";
        _audioPlayer?.SetVolume(value / 100.0);
        _videoService?.UpdateVolume();
        Save();
    }

    [ObservableProperty]
    private string _masterVolumeText = "32%";

    [ObservableProperty]
    private int _videoVolume;

    partial void OnVideoVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.VideoVolume = value;
        VideoVolumeText = $"{value}%";
        _videoService?.UpdateVolume();
        Save();
    }

    [ObservableProperty]
    private string _videoVolumeText = "50%";

    [ObservableProperty]
    private int _duckVolume;

    partial void OnDuckVolumeChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DuckingLevel = value;
        DuckVolumeText = $"{value}%";
        Save();
    }

    [ObservableProperty]
    private string _duckVolumeText = "80%";

    [ObservableProperty]
    private bool _excludeBrowserDucking;

    partial void OnExcludeBrowserDuckingChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ExcludeBambiCloudFromDucking = value;
        Save();
    }

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _audioOutputDevices;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedAudioOutputDevice;

    partial void OnSelectedAudioOutputDeviceChanged(AudioDeviceInfo? value)
    {
        if (_populatingAudioOutputs) return;
        if (_settingsService?.Current == null) return;

        var id = value?.Id ?? "";
        var name = value?.Name ?? "";
        _settingsService.Current.AudioOutputDeviceId = id;
        _settingsService.Current.AudioOutputDeviceName = name;
        _audioDeviceService?.SetPreferredDevice(id);
        _videoService?.UpdateVolume();
        Save();
        _logger?.LogInformation("Audio output device set to '{Name}' (id={Id})",
            string.IsNullOrEmpty(name) ? "System default" : name,
            string.IsNullOrEmpty(id) ? "(default)" : id);
    }

    [RelayCommand]
    private void RefreshAudioOutputDevices()
    {
        try
        {
            _populatingAudioOutputs = true;
            AudioOutputDevices.Clear();

            // Synthetic "System default" entry mirrors the WPF audio picker.
            var systemDefault = new AudioDeviceInfo("", "System default", true);
            AudioOutputDevices.Add(systemDefault);

            foreach (var dev in _audioDeviceService?.GetOutputDevices() ?? System.Linq.Enumerable.Empty<AudioDeviceInfo>())
                AudioOutputDevices.Add(dev);

            var settings = _settingsService?.Current;
            if (settings != null)
            {
                var savedId = settings.AudioOutputDeviceId ?? "";
                var savedName = settings.AudioOutputDeviceName ?? "";
                AudioDeviceInfo? pick = null;
                foreach (var d in AudioOutputDevices)
                {
                    if (!string.IsNullOrEmpty(savedId) && d.Id == savedId) { pick = d; break; }
                }
                if (pick == null && !string.IsNullOrEmpty(savedName))
                {
                    foreach (var d in AudioOutputDevices)
                    {
                        if (string.Equals(d.Name, savedName, StringComparison.OrdinalIgnoreCase)) { pick = d; break; }
                    }
                }
                SelectedAudioOutputDevice = pick ?? systemDefault;
            }
            else
            {
                SelectedAudioOutputDevice = systemDefault;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "RefreshAudioOutputDevices failed");
        }
        finally
        {
            _populatingAudioOutputs = false;
        }
    }

    [RelayCommand]
    private async Task TestAudioAsync()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
            var subAudioDir = Path.Combine(baseDir, "Resources", "sub_audio");
            var awarenessDir = Path.Combine(baseDir, "Resources", "AwarenessPresets", "audio");

            var diagnostics = new System.Text.StringBuilder();
            diagnostics.AppendLine("=== Audio Diagnostics ===");

            if (!Directory.Exists(soundsDir))
                diagnostics.AppendLine("WARNING: Resources/sounds/ directory MISSING");
            else
                diagnostics.AppendLine($"Resources/sounds/: {Directory.GetFiles(soundsDir, "*.*", SearchOption.AllDirectories).Length} files");

            if (!Directory.Exists(subAudioDir))
                diagnostics.AppendLine("WARNING: Resources/sub_audio/ directory MISSING");
            else
                diagnostics.AppendLine($"Resources/sub_audio/: {Directory.GetFiles(subAudioDir, "*.*").Length} files");

            var testFiles = new[]
            {
                Path.Combine(soundsDir, "chime1.mp3"),
                Path.Combine(soundsDir, "lvup.mp3"),
                Path.Combine(soundsDir, "bubbles", "Pop.mp3"),
                Path.Combine(awarenessDir, "chime.wav")
            };

            string? playFile = null;
            foreach (var f in testFiles)
            {
                if (File.Exists(f)) { playFile = f; break; }
            }

            if (playFile == null)
            {
                diagnostics.AppendLine("WARNING: No test sound files found to play");
            }
            else
            {
                await (_audioPlayer?.PlayAsync(playFile) ?? Task.CompletedTask);
                diagnostics.AppendLine($"Playing: {Path.GetFileName(playFile)} at {MasterVolume}% master volume");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1200);
                    _audioPlayer?.Stop();
                });
            }

            var s = _settingsService?.Current;
            if (s != null)
            {
                diagnostics.AppendLine($"\nMaster Volume: {s.MasterVolume}%");
                diagnostics.AppendLine($"Output Device: {SelectedAudioOutputDevice?.Name ?? "System default"}");
            }

            var message = diagnostics.ToString();
            _logger?.LogInformation("[AudioDiag] Test requested:\n{Result}", message);
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_audio_diagnostics"), message) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Audio test failed");
            await (_dialogService?.ShowMessageAsync(Loc.Get("title_audio_diagnostics"), string.Format(Loc.Get("msg_playback_failed_fmt"), ex.Message)) ?? Task.CompletedTask);
        }
    }

    #endregion

    #region Quick Links

    [ObservableProperty]
    private bool _discordRichPresenceEnabled;

    partial void OnDiscordRichPresenceEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.DiscordRichPresenceEnabled = value;
        Save();
        _logger?.LogInformation("Discord Rich Presence {Status}", value ? "enabled" : "disabled");
    }

    [RelayCommand]
    private async Task UnifiedLoginAsync()
    {
        try
        {
            Window? owner = null;
            if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                owner = lifetime.MainWindow;

            var dialog = new LoginDialog();
            await dialog.ShowDialog<bool>(owner);

            if (dialog.Result is { Success: true } result)
            {
                var settings = _settingsService?.Current;
                if (settings == null) return;

                settings.UnifiedId = result.UnifiedId;
                settings.UserDisplayName = result.DisplayName;
                Save();
                RefreshLoginState();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unified login failed");
        }
    }

    [RelayCommand]
    private void Logout()
    {
        try
        {
            var settings = _settingsService?.Current;
            if (settings != null)
            {
                settings.UnifiedId = null;
                settings.UserDisplayName = "";
            }

            // AC-1/AC-2: single shared logout path — revokes every provider's OAuth tokens
            // and cached premium, clears settings.AuthToken, and persists immediately.
            AuthLogoutHelper.LogoutAll(App.Services, _settingsService);
            RefreshLoginState();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Logout failed");
        }
    }

    [RelayCommand]
    private async Task OpenDiscordAsync()
    {
        const string url = "https://discord.gg/YxVAMt4qaZ";
        try
        {
            if (_browserHost != null)
                await _browserHost.NavigateAsync(new Uri(url));
            else
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open Discord");
        }
    }

    #endregion

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _settingsService?.SaveImmediate(suppressCloudBackup: false);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                Loc.Get("msg_settings_saved")) ?? Task.CompletedTask);
            _logger?.LogInformation("Settings saved from settings tab");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }

    [RelayCommand]
    private async Task OpenBugReportAsync()
    {
        _logger?.LogInformation("Bug report requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("bug_report_title"),
            Loc.Get("bug_report_error_toast")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private void ShowHelpOverlay()
    {
        IsHelpOverlayVisible = true;
        _logger?.LogInformation("Help overlay opened");
    }

    [RelayCommand]
    private void ShowHelpSection(string? sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            ShowHelpOverlay();
            return;
        }

        var content = HelpContentService.GetContent(sectionId);
        if (content == null)
        {
            _logger?.LogWarning("Help section not found: {SectionId}", sectionId);
            return;
        }

        HelpOverlayTitle = $"{content.Icon} {content.Title}";
        HelpOverlayWhatItDoes = content.WhatItDoes;
        HelpOverlayHowItWorks = content.HowItWorks;
        HelpOverlayTips = content.Tips ?? new System.Collections.Generic.List<string>();
        HelpOverlayHasTips = content.HasTips;
        HelpOverlayHasHowItWorks = content.HasHowItWorks;
        IsHelpOverlayVisible = true;
        _logger?.LogInformation("Help overlay opened for section {SectionId}", sectionId);
    }

    [RelayCommand]
    private void HideHelpOverlay()
    {
        IsHelpOverlayVisible = false;
    }

    [RelayCommand]
    private async Task StartTutorialAsync(string tutorialType)
    {
        _logger?.LogInformation("Tutorial requested: {Type}", tutorialType);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_tutorial"),
            Loc.GetF("msg_tutorial_starting_0", tutorialType)) ?? Task.CompletedTask);
    }

    #region Browser

    [ObservableProperty]
    private bool _browserInitialized;

    // B5: mute toggle glyph (🔊 on / 🔇 muted).
    [ObservableProperty]
    private string _browserMuteGlyph = "🔊";

    // B6: the live browser URL, tracked via IBrowserHost.Navigated so pop-out detaches
    // the page the user is actually viewing instead of the default site.
    private string? _lastBrowserUrl;

    /// <summary>Wires browser-host events and seeds the mute glyph from persisted settings.</summary>
    private void SubscribeBrowserEvents()
    {
        if (_browserHost != null)
            _browserHost.Navigated += (_, uri) => _lastBrowserUrl = uri.ToString();

        BrowserMuteGlyph = (_settingsService?.Current?.BrowserVideoMuted ?? false) ? "🔇" : "🔊";
    }

    /// <summary>
    /// B5: applies the mute state to the WebView2 host. IBrowserHost has no mute member (it is a
    /// Windows-only WebView2 feature and the interface is frozen), so set it reflectively —
    /// non-WebView2 heads simply no-op.
    /// </summary>
    private void ApplyBrowserMute(bool muted)
    {
        try
        {
            _browserHost?.GetType().GetProperty("IsAudioMuted")?.SetValue(_browserHost, muted);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply browser mute");
        }
    }

    [RelayCommand]
    private void ToggleBrowserMute()
    {
        var muted = !(_settingsService?.Current?.BrowserVideoMuted ?? false);
        if (_settingsService?.Current != null)
            _settingsService.Current.BrowserVideoMuted = muted;
        ApplyBrowserMute(muted);
        BrowserMuteGlyph = muted ? "🔇" : "🔊";
        Save();
    }

    [ObservableProperty]
    private string _browserStatusText = Loc.Get("label_disconnected");

    [ObservableProperty]
    private string _browserLoadingText = Loc.Get("label_initializing_webview2");

    [ObservableProperty]
    private bool _isBambiCloudSelected = true;

    [ObservableProperty]
    private bool _browserEnhanceIfPossible = true;

    partial void OnBrowserEnhanceIfPossibleChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.BrowserEnhanceIfPossible = value;
        Save();
        RefreshEnhanceMatchStatus();
    }

    [ObservableProperty]
    private string _enhanceMatchStatusText = Loc.Get("browser_enhance_match_none");

    [ObservableProperty]
    private bool _deeperBrowserBadgeVisible;

    [ObservableProperty]
    private string _deeperBrowserBadgeText = "🌊 Deeper";

    [ObservableProperty]
    private string _deeperBrowserBadgeToolTip = "";

    [ObservableProperty]
    private bool _audioSyncLatencyPanelVisible;

    [ObservableProperty]
    private int _legacyAudioSyncLatency;

    [ObservableProperty]
    private string _legacyAudioSyncLatencyText = "+0ms";

    [ObservableProperty]
    private int _legacyAudioSyncIntensity = 50;

    [ObservableProperty]
    private string _legacyAudioSyncIntensityText = "50%";

    partial void OnLegacyAudioSyncLatencyChanged(int value)
    {
        if (_settingsService?.Current?.Haptics?.AudioSync == null) return;
        _settingsService.Current.Haptics.AudioSync.ManualLatencyOffsetMs = value;
        LegacyAudioSyncLatencyText = $"{(value >= 0 ? "+" : "")}{value}ms";
        Save();
    }

    partial void OnLegacyAudioSyncIntensityChanged(int value)
    {
        if (_settingsService?.Current?.Haptics?.AudioSync == null) return;
        _settingsService.Current.Haptics.AudioSync.LiveIntensity = value / 100.0;
        LegacyAudioSyncIntensityText = $"{value}%";
        Save();
    }

    private string ResolveBrowserUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "https://bambicloud.com/";

        return url.ToLowerInvariant() switch
        {
            "hypnotube" => "https://hypnotube.com/",
            "bambicloud" => "https://bambicloud.com/",
            _ => url
        };
    }

    private void RefreshEnhanceMatchStatus()
    {
        try
        {
            if (!BrowserEnhanceIfPossible)
            {
                EnhanceMatchStatusText = Loc.Get("browser_enhance_match_off");
                DeeperBrowserBadgeVisible = false;
                return;
            }

            // The Avalonia head does not yet have a live Deeper browser enhancement bridge.
            // Keep the badge hidden until the bridge is ported; the UI bindings are in place.
            EnhanceMatchStatusText = Loc.Get("browser_enhance_match_none");
            DeeperBrowserBadgeVisible = false;
        }
        catch
        {
            EnhanceMatchStatusText = Loc.Get("browser_enhance_match_none");
        }
    }

    [RelayCommand]
    private async Task OpenBrowserAsync(string? url)
    {
        if (_settingsService?.Current?.OfflineMode == true)
        {
            _logger?.LogInformation("Browser launch blocked in offline mode");
            return;
        }

        var targetUrl = ResolveBrowserUrl(url);
        IsBambiCloudSelected = targetUrl.Contains("bambicloud.com", StringComparison.OrdinalIgnoreCase);

        try
        {
            _logger?.LogInformation("Navigating browser to {Url}", targetUrl);
            await (_browserHost?.NavigateAsync(new Uri(targetUrl)) ?? Task.CompletedTask);
            _lastBrowserUrl = targetUrl;
            // B5: re-apply the saved mute preference now that the browser core exists.
            ApplyBrowserMute(_settingsService?.Current?.BrowserVideoMuted ?? false);
            BrowserStatusText = Loc.Get("label_connected");
            BrowserInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to navigate browser to {Url}", targetUrl);
            BrowserStatusText = Loc.Get("label_failed");
        }
    }

    [RelayCommand]
    private async Task InitializeBrowserAsync(string? startUrl)
    {
        if (_settingsService?.Current?.OfflineMode == true)
        {
            _logger?.LogInformation("Browser initialization blocked in offline mode");
            return;
        }

        BrowserStatusText = Loc.Get("label_loading");
        BrowserLoadingText = Loc.Get("label_creating_browser");

        await OpenBrowserAsync(startUrl);

        BrowserLoadingText = "";
    }

    [RelayCommand]
    private async Task NavigateBrowserAsync(string? url)
    {
        if (_settingsService?.Current?.OfflineMode == true) return;
        if (string.IsNullOrEmpty(url)) return;

        await OpenBrowserAsync(url);
    }

    [RelayCommand]
    private async Task BrowserSiteToggleChangedAsync(string? site)
    {
        if (_settingsService?.Current?.OfflineMode == true) return;
        var url = site?.ToLowerInvariant() == "hypnotube"
            ? "https://hypnotube.com/"
            : "https://bambicloud.com/";
        await OpenBrowserAsync(url);
    }

    [RelayCommand]
    private async Task PopOutBrowserAsync()
    {
        if (_settingsService?.Current?.OfflineMode == true) return;

        // B6: pop out the live page the user is viewing, falling back to the default site.
        var targetUrl = !string.IsNullOrWhiteSpace(_lastBrowserUrl)
            ? _lastBrowserUrl!
            : ResolveBrowserUrl(null);
        IsBambiCloudSelected = targetUrl.Contains("bambicloud.com", StringComparison.OrdinalIgnoreCase);

        try
        {
            _logger?.LogInformation("Browser pop-out requested for {Url}", targetUrl);
            await (_browserHost?.PopOutAsync(new Uri(targetUrl)) ?? Task.CompletedTask);
            BrowserStatusText = Loc.Get("label_connected");
            BrowserInitialized = true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to pop-out browser to {Url}", targetUrl);
            BrowserStatusText = Loc.Get("label_failed");
        }
    }

    #endregion

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save settings"); }
    }

}
