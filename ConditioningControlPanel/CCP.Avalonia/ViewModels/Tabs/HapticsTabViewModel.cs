using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Haptics partial.
/// Exposes haptic provider selection, connection, per-event enable/intensity/mode,
/// and video audio-sync controls. The live device service is abstracted by
/// <see cref="IHapticsService"/>; the Avalonia head uses a real Buttplug.io
/// implementation (<see cref="Services.Haptics.AvaloniaHapticsService"/>).
/// </summary>
public partial class HapticsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IHapticsService? _hapticsService;
    private readonly ILogger<HapticsTabViewModel>? _logger;

    public HapticsTabViewModel() : base("haptics", "Haptics", "💜")
    {
        ProviderOptions = new ObservableCollection<string> { "Mock", "Lovense", "Buttplug" };
        ModeOptions = new ObservableCollection<string>(Enum.GetNames(typeof(VibrationMode)));
        AudioSyncLatencyFormatted = FormatLatency(AudioSyncLatency);
        UpdateStatusColor();

        // Design-time default: locked until a real settings/auth service is injected.
        IsPremiumLocked = true;
    }

    public HapticsTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IHapticsService hapticsService,
        ILogger<HapticsTabViewModel> logger) : base("haptics", "Haptics", "💜")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _hapticsService = hapticsService;
        _logger = logger;
        ProviderOptions = new ObservableCollection<string> { "Mock", "Lovense", "Buttplug" };
        ModeOptions = new ObservableCollection<string>(Enum.GetNames(typeof(VibrationMode)));

        _hapticsService.ConnectionStateChanged += OnHapticsConnectionStateChanged;
        _hapticsService.DeviceAdded += OnHapticsDeviceAdded;
        _hapticsService.DeviceRemoved += OnHapticsDeviceRemoved;

        LoadFromSettings();
        AudioSyncLatencyFormatted = FormatLatency(AudioSyncLatency);
        UpdateStatusColor();
        RefreshPremiumLock();

        if (_settingsService?.Current != null)
            _settingsService.Current.PropertyChanged += OnSettingsPropertyChanged;
    }

    [ObservableProperty]
    private bool _hapticsEnabled;

    [ObservableProperty]
    private bool _audioSyncEnabled;

    [ObservableProperty]
    private int _audioSyncLatency;

    [ObservableProperty]
    private int _audioSyncIntensity = 50;

    [ObservableProperty]
    private string _audioSyncLatencyFormatted = "0ms";

    [ObservableProperty]
    private int _selectedProviderIndex;

    [ObservableProperty]
    private string _providerUrl = "";

    [ObservableProperty]
    private string _providerHint = "";

    [ObservableProperty]
    private bool _autoConnect;

    [ObservableProperty]
    private int _globalIntensity = 70;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = Loc.Get("label_disconnected");

    [ObservableProperty]
    private IBrush _statusColor = new SolidColorBrush(Color.Parse("#FF6B6B"));

    [ObservableProperty]
    private string _devicesText = Loc.Get("label_no_devices");

    [ObservableProperty]
    private string _connectButtonText = Loc.Get("btn_connect");

    [ObservableProperty]
    private ObservableCollection<string> _providerOptions;

    [ObservableProperty]
    private ObservableCollection<string> _modeOptions;

    [ObservableProperty]
    private bool _bubbleEnabled;

    [ObservableProperty]
    private int _bubbleIntensity = 50;

    [ObservableProperty]
    private int _bubbleModeIndex;

    [ObservableProperty]
    private bool _flashDisplayEnabled;

    [ObservableProperty]
    private int _flashDisplayIntensity = 50;

    [ObservableProperty]
    private int _flashDisplayModeIndex;

    [ObservableProperty]
    private bool _flashClickEnabled;

    [ObservableProperty]
    private int _flashClickIntensity = 50;

    [ObservableProperty]
    private int _flashClickModeIndex;

    [ObservableProperty]
    private bool _videoEnabled;

    [ObservableProperty]
    private int _videoIntensity = 50;

    [ObservableProperty]
    private int _videoModeIndex;

    [ObservableProperty]
    private bool _targetHitEnabled;

    [ObservableProperty]
    private int _targetHitIntensity = 70;

    [ObservableProperty]
    private int _targetHitModeIndex;

    [ObservableProperty]
    private bool _subliminalEnabled;

    [ObservableProperty]
    private int _subliminalIntensity = 50;

    [ObservableProperty]
    private int _subliminalModeIndex;

    [ObservableProperty]
    private bool _levelUpEnabled;

    [ObservableProperty]
    private int _levelUpIntensity = 50;

    [ObservableProperty]
    private int _levelUpModeIndex;

    [ObservableProperty]
    private bool _achievementEnabled;

    [ObservableProperty]
    private int _achievementIntensity = 50;

    [ObservableProperty]
    private int _achievementModeIndex;

    [ObservableProperty]
    private bool _bouncingTextEnabled;

    [ObservableProperty]
    private int _bouncingTextIntensity = 50;

    [ObservableProperty]
    private int _bouncingTextModeIndex;

    /// <summary>
    /// When true, a premium-gate overlay covers the tab.
    /// Mirrors the cached Patreon premium state from <see cref="AppSettings.HasCachedPremiumAccess"/>.
    /// </summary>
    [ObservableProperty]
    private bool _isPremiumLocked;

    partial void OnHapticsEnabledChanged(bool value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.Enabled = value;
        Save();
    }

    partial void OnAudioSyncEnabledChanged(bool value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.AudioSync.Enabled = value;
        Save();
    }

    partial void OnAudioSyncLatencyChanged(int value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.AudioSync.ManualLatencyOffsetMs = value;
        AudioSyncLatencyFormatted = FormatLatency(value);
        Save();
    }

    partial void OnAudioSyncIntensityChanged(int value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.AudioSync.LiveIntensity = value / 100.0;
        Save();
    }

    partial void OnSelectedProviderIndexChanged(int value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.Provider = value switch
        {
            1 => HapticProviderType.Lovense,
            2 => HapticProviderType.Buttplug,
            _ => HapticProviderType.Mock
        };
        UpdateProviderUi();
        Save();
    }

    partial void OnProviderUrlChanged(string value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        var provider = _settingsService.Current.Haptics.Provider;
        if (provider == HapticProviderType.Lovense)
            _settingsService.Current.Haptics.LovenseUrl = value;
        else if (provider == HapticProviderType.Buttplug)
            _settingsService.Current.Haptics.ButtplugUrl = value;
        Save();
    }

    partial void OnAutoConnectChanged(bool value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.AutoConnect = value;
        Save();
    }

    partial void OnGlobalIntensityChanged(int value)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        _settingsService.Current.Haptics.GlobalIntensity = value / 100.0;
        Save();
    }

    // Per-event saves
    partial void OnBubbleEnabledChanged(bool value) => SaveEventEnabled(nameof(BubbleEnabled), value, s => s.BubblePopEnabled = value);
    partial void OnBubbleIntensityChanged(int value) => SaveEventIntensity(nameof(BubbleIntensity), value, s => s.BubblePopIntensity = value / 100.0);
    partial void OnBubbleModeIndexChanged(int value) => SaveEventMode(nameof(BubbleModeIndex), value, s => s.BubblePopMode = (VibrationMode)value);

    partial void OnFlashDisplayEnabledChanged(bool value) => SaveEventEnabled(nameof(FlashDisplayEnabled), value, s => s.FlashDisplayEnabled = value);
    partial void OnFlashDisplayIntensityChanged(int value) => SaveEventIntensity(nameof(FlashDisplayIntensity), value, s => s.FlashDisplayIntensity = value / 100.0);
    partial void OnFlashDisplayModeIndexChanged(int value) => SaveEventMode(nameof(FlashDisplayModeIndex), value, s => s.FlashDisplayMode = (VibrationMode)value);

    partial void OnFlashClickEnabledChanged(bool value) => SaveEventEnabled(nameof(FlashClickEnabled), value, s => s.FlashClickEnabled = value);
    partial void OnFlashClickIntensityChanged(int value) => SaveEventIntensity(nameof(FlashClickIntensity), value, s => s.FlashClickIntensity = value / 100.0);
    partial void OnFlashClickModeIndexChanged(int value) => SaveEventMode(nameof(FlashClickModeIndex), value, s => s.FlashClickMode = (VibrationMode)value);

    partial void OnVideoEnabledChanged(bool value) => SaveEventEnabled(nameof(VideoEnabled), value, s => s.VideoEnabled = value);
    partial void OnVideoIntensityChanged(int value) => SaveEventIntensity(nameof(VideoIntensity), value, s => s.VideoIntensity = value / 100.0);
    partial void OnVideoModeIndexChanged(int value) => SaveEventMode(nameof(VideoModeIndex), value, s => s.VideoMode = (VibrationMode)value);

    partial void OnTargetHitEnabledChanged(bool value) => SaveEventEnabled(nameof(TargetHitEnabled), value, s => s.TargetHitEnabled = value);
    partial void OnTargetHitIntensityChanged(int value) => SaveEventIntensity(nameof(TargetHitIntensity), value, s => s.TargetHitIntensity = value / 100.0);
    partial void OnTargetHitModeIndexChanged(int value) => SaveEventMode(nameof(TargetHitModeIndex), value, s => s.TargetHitMode = (VibrationMode)value);

    partial void OnSubliminalEnabledChanged(bool value) => SaveEventEnabled(nameof(SubliminalEnabled), value, s => s.SubliminalEnabled = value);
    partial void OnSubliminalIntensityChanged(int value) => SaveEventIntensity(nameof(SubliminalIntensity), value, s => s.SubliminalIntensity = value / 100.0);
    partial void OnSubliminalModeIndexChanged(int value) => SaveEventMode(nameof(SubliminalModeIndex), value, s => s.SubliminalMode = (VibrationMode)value);

    partial void OnLevelUpEnabledChanged(bool value) => SaveEventEnabled(nameof(LevelUpEnabled), value, s => s.LevelUpEnabled = value);
    partial void OnLevelUpIntensityChanged(int value) => SaveEventIntensity(nameof(LevelUpIntensity), value, s => s.LevelUpIntensity = value / 100.0);
    partial void OnLevelUpModeIndexChanged(int value) => SaveEventMode(nameof(LevelUpModeIndex), value, s => s.LevelUpMode = (VibrationMode)value);

    partial void OnAchievementEnabledChanged(bool value) => SaveEventEnabled(nameof(AchievementEnabled), value, s => s.AchievementEnabled = value);
    partial void OnAchievementIntensityChanged(int value) => SaveEventIntensity(nameof(AchievementIntensity), value, s => s.AchievementIntensity = value / 100.0);
    partial void OnAchievementModeIndexChanged(int value) => SaveEventMode(nameof(AchievementModeIndex), value, s => s.AchievementMode = (VibrationMode)value);

    partial void OnBouncingTextEnabledChanged(bool value) => SaveEventEnabled(nameof(BouncingTextEnabled), value, s => s.BouncingTextEnabled = value);
    partial void OnBouncingTextIntensityChanged(int value) => SaveEventIntensity(nameof(BouncingTextIntensity), value, s => s.BouncingTextIntensity = value / 100.0);
    partial void OnBouncingTextModeIndexChanged(int value) => SaveEventMode(nameof(BouncingTextModeIndex), value, s => s.BouncingTextMode = (VibrationMode)value);

    private void SaveEventEnabled(string name, bool value, Action<HapticSettings> apply)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        apply(_settingsService.Current.Haptics);
        Save();
        _logger?.LogInformation("Haptic event {Name} enabled = {Value}", name, value);
    }

    private void SaveEventIntensity(string name, int value, Action<HapticSettings> apply)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        apply(_settingsService.Current.Haptics);
        Save();
    }

    private void SaveEventMode(string name, int value, Action<HapticSettings> apply)
    {
        if (_settingsService?.Current?.Haptics == null) return;
        apply(_settingsService.Current.Haptics);
        Save();
        _logger?.LogInformation("Haptic event {Name} mode = {Mode}", name, (VibrationMode)value);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_hapticsService == null)
        {
            _logger?.LogWarning("Haptic connect requested but no haptics service is available");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_haptic_service_unavailable")) ?? Task.CompletedTask);
            return;
        }

        if (_hapticsService.IsConnected)
        {
            _logger?.LogInformation("Haptic disconnect requested");
            _hapticsService.Disconnect();
            return;
        }

        _logger?.LogInformation("Haptic connect requested for {ProviderUrl}", ProviderUrl);
        var connected = await _hapticsService.ConnectAsync(ProviderUrl);

        if (!connected)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_haptic_connect_failed")) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (_hapticsService == null)
        {
            _logger?.LogWarning("Haptic test requested but no haptics service is available");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_haptic_service_unavailable")) ?? Task.CompletedTask);
            return;
        }

        _logger?.LogInformation("Haptic test requested");
        var success = await _hapticsService.TestAsync(GlobalIntensity, 2000);

        if (success)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                Loc.GetF("msg_haptic_test_success_fmt", GlobalIntensity, 2)) ?? Task.CompletedTask);
        }
        else
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_haptic_test_failed")) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ShowHelpAsync()
    {
        _logger?.LogInformation("Haptics help requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("haptics_help_title"),
            Loc.Get("haptics_help_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ShowVideoSyncHelpAsync()
    {
        _logger?.LogInformation("Video haptic sync help requested");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("label_video_haptic_sync"),
            Loc.Get("haptics_video_sync_help_body")) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        _logger?.LogInformation("Premium unlock requested from haptics gate");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("gate_premium_locked"),
            Loc.Get("label_unlock_haptic_feedback_by_supporting_on_patre")) ?? Task.CompletedTask);
    }

    private void OnHapticsConnectionStateChanged(object? sender, bool connected)
    {
        // Q9 cross-thread brush fix: AvaloniaHapticsService raises ConnectionStateChanged from the
        // Buttplug WebSocket callbacks on BACKGROUND threads (AvaloniaHapticsService.cs:264 OnDeviceAdded,
        // :279 OnDeviceRemoved, :294 OnServerDisconnect). UpdateStatusColor() assigns the bound StatusColor
        // brush, which is live on Ellipse.Fill / TextBlock.Foreground (HapticsTabView.axaml:167/174); an
        // off-thread assignment makes the Avalonia compositor serialize a half-written SolidColorBrush (the
        // rare SolidColorBrush.SerializeChanges -> Compositor.CommitCore crash). Marshal the whole status
        // update (brush + its tightly-coupled sibling bound props) onto the UI thread, mirroring the
        // webcam-event pattern in LabTabViewModel.OnState (LabTabViewModel.cs:605).
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            StatusText = connected ? Loc.Get("label_connected") : Loc.Get("label_disconnected");
            ConnectButtonText = connected ? Loc.Get("btn_disconnect") : Loc.Get("btn_connect");
            DevicesText = connected
                ? string.Join(", ", _hapticsService?.ConnectedDevices ?? Array.Empty<string>())
                : Loc.Get("label_no_devices");
            UpdateStatusColor();
        });
    }

    private void OnHapticsDeviceAdded(object? sender, string device)
    {
        DevicesText = string.Join(", ", _hapticsService?.ConnectedDevices ?? Array.Empty<string>());
    }

    private void OnHapticsDeviceRemoved(object? sender, string device)
    {
        DevicesText = string.Join(", ", _hapticsService?.ConnectedDevices ?? Array.Empty<string>());
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current?.Haptics;
        if (s == null) return;

        HapticsEnabled = s.Enabled;
        AudioSyncEnabled = s.AudioSync.Enabled;
        AudioSyncLatency = s.AudioSync.ManualLatencyOffsetMs;
        AudioSyncIntensity = (int)(s.AudioSync.LiveIntensity * 100);
        SelectedProviderIndex = s.Provider switch
        {
            HapticProviderType.Lovense => 1,
            HapticProviderType.Buttplug => 2,
            _ => 0
        };
        AutoConnect = s.AutoConnect;
        GlobalIntensity = (int)(s.GlobalIntensity * 100);
        UpdateProviderUi();

        BubbleEnabled = s.BubblePopEnabled;
        BubbleIntensity = (int)(s.BubblePopIntensity * 100);
        BubbleModeIndex = (int)s.BubblePopMode;

        FlashDisplayEnabled = s.FlashDisplayEnabled;
        FlashDisplayIntensity = (int)(s.FlashDisplayIntensity * 100);
        FlashDisplayModeIndex = (int)s.FlashDisplayMode;

        FlashClickEnabled = s.FlashClickEnabled;
        FlashClickIntensity = (int)(s.FlashClickIntensity * 100);
        FlashClickModeIndex = (int)s.FlashClickMode;

        VideoEnabled = s.VideoEnabled;
        VideoIntensity = (int)(s.VideoIntensity * 100);
        VideoModeIndex = (int)s.VideoMode;

        TargetHitEnabled = s.TargetHitEnabled;
        TargetHitIntensity = (int)(s.TargetHitIntensity * 100);
        TargetHitModeIndex = (int)s.TargetHitMode;

        SubliminalEnabled = s.SubliminalEnabled;
        SubliminalIntensity = (int)(s.SubliminalIntensity * 100);
        SubliminalModeIndex = (int)s.SubliminalMode;

        LevelUpEnabled = s.LevelUpEnabled;
        LevelUpIntensity = (int)(s.LevelUpIntensity * 100);
        LevelUpModeIndex = (int)s.LevelUpMode;

        AchievementEnabled = s.AchievementEnabled;
        AchievementIntensity = (int)(s.AchievementIntensity * 100);
        AchievementModeIndex = (int)s.AchievementMode;

        BouncingTextEnabled = s.BouncingTextEnabled;
        BouncingTextIntensity = (int)(s.BouncingTextIntensity * 100);
        BouncingTextModeIndex = (int)s.BouncingTextMode;
    }

    private void UpdateProviderUi()
    {
        var provider = _settingsService?.Current?.Haptics?.Provider ?? HapticProviderType.Mock;
        ProviderUrl = provider switch
        {
            HapticProviderType.Lovense => _settingsService!.Current!.Haptics.LovenseUrl,
            HapticProviderType.Buttplug => _settingsService!.Current!.Haptics.ButtplugUrl,
            _ => ""
        };
        ProviderHint = provider switch
        {
            HapticProviderType.Lovense => Loc.Get("label_lovense_hint"),
            HapticProviderType.Buttplug => Loc.Get("label_buttplug_hint"),
            _ => ""
        };
    }

    private void UpdateStatusColor()
    {
        StatusColor = IsConnected
            ? new SolidColorBrush(Color.Parse("#00E676"))
            : new SolidColorBrush(Color.Parse("#FF6B6B"));
    }

    private void RefreshPremiumLock()
    {
        IsPremiumLocked = !(_settingsService?.Current?.HasCachedPremiumAccess == true);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(AppSettings.PatreonPremiumValidUntil)
            || e.PropertyName == nameof(AppSettings.HasLinkedPatreon)
            || e.PropertyName == nameof(AppSettings.HasCachedPremiumAccess))
        {
            RefreshPremiumLock();
        }
    }

    private static string FormatLatency(int ms)
    {
        var sign = ms >= 0 ? "+" : "";
        return $"{sign}{ms}ms";
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save haptics settings"); }
    }
}
