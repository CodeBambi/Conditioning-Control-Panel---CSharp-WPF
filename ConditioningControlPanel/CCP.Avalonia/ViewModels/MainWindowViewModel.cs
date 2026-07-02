using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Chaos;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Models;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.ViewModels.Tabs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Companion;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.SessionLog;
using ConditioningControlPanel.Core.Services.Update;
using ConditioningControlPanel.Core.Services.Video;
using ConditioningControlPanel.Core.Services.Webcam;
using Session = ConditioningControlPanel.Models.Session;

using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ISettingsService? _settingsService;
    private readonly ISessionService? _sessionService;
    private readonly ISessionManager? _sessionManager;
    private readonly IDialogService? _dialogService;
    private readonly IUpdateService? _updateService;
    private readonly ILogger<MainWindowViewModel>? _logger;
    private readonly IInputHook? _inputHook;
    private readonly IHotkeyProvider? _hotkeyProvider;
    private readonly ITrayIcon? _trayIcon;
    private readonly IWindowChrome? _windowChrome;
    private readonly IOverlaySurface? _overlaySurface;
    private readonly IPlatformCapabilities? _platformCapabilities;
    private readonly IAvatarWindowService? _avatarWindowService;
    private readonly IBrowserHost? _browserHost;
    private readonly IModService? _modService;
    private readonly IProgressionService? _progressionService;
    private readonly ISkillTreeService? _skillTreeService;
    private readonly IRemoteControlService? _remoteControlService;
    private readonly ISessionEffectOrchestrator? _effectOrchestrator;
    private readonly ISessionLogService? _sessionLog;
    private readonly IWebcamService? _webcamService;
    private readonly ICompanionService? _companionService;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly ISfxPlayer? _sfxPlayer;
    private readonly IMultiMonitorVideoService? _multiMonitor;

    private string? _lastBrowserUrl;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _sessionProgressTimer;
    private DispatcherTimer? _conditioningTimeTimer;
    private DispatcherTimer? _statPillTimer;
    private DispatcherTimer? _bannerTimer;
    private DispatcherTimer? _xpFlashTimer;
    private double _previousXpPercent;
    private DateTime _conditioningStartTime;
    private double _conditioningBaselineMinutes;
    private int _conditioningSecondsCounter;
    private bool _isDisposed;

    /// <summary>
    /// Design-time constructor. Populates the tab shell so the Avalonia designer
    /// has something to render without the DI container.
    /// </summary>
    public MainWindowViewModel()
    {
        InitializeTabs();
        UpdateHeaderFromSettings();
        InitializeHeaderRow1();
        WireSessionEvents();
        HookLocalizationRefresh();
    }

    public MainWindowViewModel(IServiceProvider services)
    {
        Services = services;
        _settingsService = services.GetService<ISettingsService>();
        _sessionService = services.GetService<ISessionService>();
        _sessionManager = services.GetService<ISessionManager>();
        _dialogService = services.GetService<IDialogService>();
        _updateService = services.GetService<IUpdateService>();
        _logger = services.GetRequiredService<ILogger<MainWindowViewModel>>();
        _inputHook = services.GetService<IInputHook>();
        _hotkeyProvider = services.GetService<IHotkeyProvider>();
        _trayIcon = services.GetService<ITrayIcon>();
        _windowChrome = services.GetService<IWindowChrome>();
        _overlaySurface = services.GetService<IOverlaySurface>();
        _platformCapabilities = services.GetService<IPlatformCapabilities>();
        _avatarWindowService = services.GetService<IAvatarWindowService>();
        _browserHost = services.GetService<IBrowserHost>();
        _modService = services.GetService<IModService>();
        _progressionService = services.GetService<IProgressionService>();
        _skillTreeService = services.GetService<ISkillTreeService>();
        _remoteControlService = services.GetService<IRemoteControlService>();
        _effectOrchestrator = services.GetService<ISessionEffectOrchestrator>();
        _sessionLog = services.GetService<ISessionLogService>();
        _webcamService = services.GetService<IWebcamService>();
        _companionService = services.GetService<ICompanionService>();
        _audioPlayer = services.GetService<IAudioPlayer>();
        _sfxPlayer = services.GetService<ISfxPlayer>();
        _multiMonitor = services.GetService<IMultiMonitorVideoService>();

        InitializeTabs();
        UpdateHeaderFromSettings();
        InitializeHeaderRow1();
        WireSessionEvents();
        StartUiTimers();
        RegisterGlobalHotkeys();
        HookInput();
        SubscribeProgressionEvents();
        SubscribeCompanionEvents();
        SubscribeRemoteControlEvents();
        SubscribeBrowserHostEvents();
        HookLocalizationRefresh();
    }

    /// <summary>
    /// Application service provider. Available at runtime; null at design time.
    /// </summary>
    public IServiceProvider Services { get; } = null!;

    [ObservableProperty]
    private string _title = "Conditioning Control Panel";

    [ObservableProperty]
    private string _playerTitle = "Subject";

    [ObservableProperty]
    private string _levelText = "Lv.1";

    [ObservableProperty]
    private string _headerVersionText = "";

    [ObservableProperty]
    private string _updateButtonText = "v6.1.4 is out";

    [ObservableProperty]
    private string _updatePillState = "NoUpdate";

    [ObservableProperty]
    private ObservableCollection<ModListItem> _availableMods = new();

    [ObservableProperty]
    private ModListItem? _selectedMod;

    [ObservableProperty]
    private ObservableCollection<LanguageItem> _availableLanguages = new();

    /// <summary>
    /// Id of the currently selected mod. Mirrors <see cref="SelectedMod"/> for parity with the WPF header.
    /// </summary>
    public string SelectedModId => SelectedMod?.Id ?? BuiltInMods.CCPDefaultId;

    [ObservableProperty]
    private LanguageItem? _selectedLanguage;

    [ObservableProperty]
    private ObservableCollection<PresetItem> _availablePresets = new();

    [ObservableProperty]
    private PresetItem? _selectedPreset;

    [ObservableProperty]
    private int _streakCount;

    [ObservableProperty]
    private bool _streakShieldVisible;

    [ObservableProperty]
    private bool _streakFirePillVisible;

    [ObservableProperty]
    private ObservableCollection<TabItemViewModel> _tabs = new();

    [ObservableProperty]
    private TabItemViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private string _startButtonIcon = "▶";

    [ObservableProperty]
    private string _startButtonLabel = "Start";

    [ObservableProperty]
    private string _sessionStatusText = "";

    [ObservableProperty]
    private string _pauseButtonText = "⏸";

    [ObservableProperty]
    private string _pauseButtonToolTip = "Pause session";

    [ObservableProperty]
    private string _currentTimeText = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    private string _conditioningTimeText = "0h 0m 0s";

    [ObservableProperty]
    private string _remoteControlStatus = "";

    [ObservableProperty]
    private bool _remoteControlConnected;

    [ObservableProperty]
    private string _remoteControlSessionCode = "";

    [ObservableProperty]
    private string _remoteControlPin = "";

    [ObservableProperty]
    private string _remoteControllerSessionName = Loc.Get("label_unknown");

    public string RemoteControlSessionCodeDisplay => Loc.GetF("label_session_code_fmt", RemoteControlSessionCode);
    public string RemoteControllerSessionNameDisplay => Loc.GetF("label_connected_controller_fmt", RemoteControllerSessionName);
    public string RemoteControlStatusDisplay => Loc.GetF("label_remote_control_status_fmt", RemoteControlStatus);
    public string RemoteControlPinDisplay => Loc.GetF("label_pin_fmt", RemoteControlPin);

    partial void OnRemoteControlSessionCodeChanged(string value) => OnPropertyChanged(nameof(RemoteControlSessionCodeDisplay));
    partial void OnRemoteControllerSessionNameChanged(string value) => OnPropertyChanged(nameof(RemoteControllerSessionNameDisplay));
    partial void OnRemoteControlStatusChanged(string value) => OnPropertyChanged(nameof(RemoteControlStatusDisplay));
    partial void OnRemoteControlPinChanged(string value) => OnPropertyChanged(nameof(RemoteControlPinDisplay));

    [ObservableProperty]
    private double _startButtonFlashOpacity = 1.0;

    #region Rotating Banner

    [ObservableProperty]
    private string _bannerPrimaryText = "If you love the project, please consider supporting it.";

    [ObservableProperty]
    private string _bannerSecondaryText = "Welcome back, subject~";

    [ObservableProperty]
    private string _bannerTertiaryText = "A special thanks to Platinum Puppets for providing assets to the project.";

    [ObservableProperty]
    private string _bannerPrimaryUrl = "https://linktr.ee/CodeBambi";

    [ObservableProperty]
    private string _bannerSecondaryUrl = "";

    [ObservableProperty]
    private string _bannerTertiaryUrl = "https://www.patreon.com/c/PlatinumPuppets";

    [ObservableProperty]
    private int _currentBannerIndex;

    [ObservableProperty]
    private bool _isBannerPrimaryVisible = true;

    [ObservableProperty]
    private bool _isBannerSecondaryVisible;

    [ObservableProperty]
    private bool _isBannerTertiaryVisible;

    partial void OnCurrentBannerIndexChanged(int value)
    {
        IsBannerPrimaryVisible = value == 0;
        IsBannerSecondaryVisible = value == 1;
        IsBannerTertiaryVisible = value == 2;
    }

    #endregion

    [ObservableProperty]
    private double _xpPercent;

    [ObservableProperty]
    private string _xpText = "0 / 100 XP";

    [ObservableProperty]
    private string _onlineUsersText = "Online: --";

    [ObservableProperty]
    private string _rankPercentileText = "Rank: --";

    [ObservableProperty]
    private bool _isLoggedIn;

    private bool _xpBarLoginOverlayVisible = true;

    public bool XPBarLoginOverlayVisible
    {
        get => _xpBarLoginOverlayVisible;
        set => SetProperty(ref _xpBarLoginOverlayVisible, value);
    }

    [ObservableProperty]
    private int _playerLevel = 1;

    [ObservableProperty]
    private int _totalSkillPoints = 0;

    [ObservableProperty]
    private bool _conditioningTimeVisible;

    [ObservableProperty]
    private bool _onlineUsersVisible;

    [ObservableProperty]
    private bool _rankPercentileVisible;

    [ObservableProperty]
    private double _xpFlashOpacity;

    [ObservableProperty]
    private ObservableCollection<string> _bonusChips = new() { "+10% XP", "+5% XP" };

    [ObservableProperty]
    private string _displayName = "";

    partial void OnXpPercentChanged(double value)
    {
        var increased = value > _previousXpPercent;
        _previousXpPercent = value;

        if (increased)
        {
            FlashXpBar();
        }
    }

    private void FlashXpBar()
    {
        XpFlashOpacity = 0.8;
        _xpFlashTimer?.Stop();
        _xpFlashTimer = null;

        _xpFlashTimer = StartOneShotTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            Dispatcher.UIThread.Post(() => XpFlashOpacity = 0);
        });
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        XPBarLoginOverlayVisible = !value;
    }

    #region Title-bar status pills

    [ObservableProperty]
    private bool _directoryStatusVisible;

    [ObservableProperty]
    private string _directoryStatusText = "Private only";

    [ObservableProperty]
    private string _directoryStatusDotColor = "#B47BFF";

    [ObservableProperty]
    private bool _webcamActiveVisible;

    [ObservableProperty]
    private string _webcamActiveText = "Camera active";

    #endregion

    #region Overlay Visibility

    [ObservableProperty]
    private bool _remoteControlOverlayVisible;

    [ObservableProperty]
    private bool _dropOverlayVisible;

    [ObservableProperty]
    private bool _tutorialOverlayVisible;

    [ObservableProperty]
    private bool _browserFullscreenOverlayVisible;

    [ObservableProperty]
    private ObservableCollection<Models.NotificationItem> _notifications = new();

    #endregion

    private TabItemViewModel? _previousTab;

    partial void OnSelectedTabChanged(TabItemViewModel? value)
    {
        _previousTab?.OnDeselected();
        _previousTab = value;
        value?.OnSelected();

        if (value is RemoteControlTabViewModel remote)
        {
            remote.RefreshStatus();
        }
    }

    private void InitializeTabs()
    {
        var allTabs = new TabItemViewModel[]
        {
            GetTab<SettingsTabViewModel>() ?? new SettingsTabViewModel(),
            GetTab<PresetsTabViewModel>() ?? new PresetsTabViewModel(),
            GetTab<PresetIOTabViewModel>() ?? new PresetIOTabViewModel(),
            GetTab<QuestsTabViewModel>() ?? new QuestsTabViewModel(),
            GetTab<LevelFeaturesTabViewModel>() ?? new LevelFeaturesTabViewModel(),
            GetTab<PatreonTabViewModel>() ?? new PatreonTabViewModel(),
            GetTab<EnhancementsTabViewModel>() ?? new EnhancementsTabViewModel(),
            GetTab<DeeperTabViewModel>() ?? new DeeperTabViewModel(),
            GetTab<DeeperHubTabViewModel>() ?? new DeeperHubTabViewModel(),
            GetTab<DeeperSubmissionsTabViewModel>() ?? new DeeperSubmissionsTabViewModel(),
            GetTab<AvailableSubjectsTabViewModel>() ?? new AvailableSubjectsTabViewModel(),
            GetTab<AssetsTabViewModel>() ?? new AssetsTabViewModel(),
            GetTab<CatalogueSubmissionsTabViewModel>() ?? new CatalogueSubmissionsTabViewModel(),
            GetTab<AchievementsTabViewModel>() ?? new AchievementsTabViewModel(),
            GetTab<LeaderboardTabViewModel>() ?? new LeaderboardTabViewModel(),
            GetTab<MarqueeTabViewModel>() ?? new MarqueeTabViewModel(),
            GetTab<AnimationsTabViewModel>() ?? new AnimationsTabViewModel(),
            GetTab<CompanionHubTabViewModel>() ?? new CompanionHubTabViewModel(),
            GetTab<CompanionTabViewModel>() ?? new CompanionTabViewModel(),
            GetTab<SheListeningTabViewModel>() ?? new SheListeningTabViewModel(),
            GetTab<ProfileTabViewModel>() ?? new ProfileTabViewModel(),
            GetTab<LabTabViewModel>() ?? new LabTabViewModel(),
            GetTab<BlinkTrainerTabViewModel>() ?? new BlinkTrainerTabViewModel(),
            GetTab<RemoteControlTabViewModel>() ?? new RemoteControlTabViewModel(),
            GetTab<BambiTakeoverTabViewModel>() ?? new BambiTakeoverTabViewModel(),
            GetTab<HapticsTabViewModel>() ?? new HapticsTabViewModel(),
            GetTab<AwarenessTabViewModel>() ?? new AwarenessTabViewModel(),
            GetTab<LockdownTabViewModel>() ?? new LockdownTabViewModel(),
            new AttentionCheckTabViewModel(),
            new BouncingTextTabViewModel(),
            new BubbleCountTabViewModel(),
            new BubblePopTabViewModel(),
            new FlashTabViewModel(),
            new PinkFilterTabViewModel(),
            new IntensityRampTabViewModel(),
            new LockCardTabViewModel(),
            new MindWipeTabViewModel(),
            new SchedulerTabViewModel(),
            new VisualsTabViewModel(),
            new SubliminalTabViewModel(),
            new SystemTabViewModel(),
            new SpiralTabViewModel(),
            new SchedulerRampTabViewModel(),
            new VideoTabViewModel(),
            new WebcamTabViewModel()
        };

        ApplyCapabilityGates(allTabs);

        var visibleTabs = allTabs.Where(t => t.RequiredCapabilities.IsSupported(_platformCapabilities)).ToList();
        Tabs = new ObservableCollection<TabItemViewModel>(visibleTabs);
        // Default to the Settings/Dashboard tab on startup (WPF parity).
        SelectedTab = Tabs.FirstOrDefault(t => t.Key == "settings") ?? Tabs.FirstOrDefault();
    }

    /// <summary>
    /// Sets <see cref="TabItemViewModel.RequiredCapabilities"/> on tabs that rely on
    /// desktop-only platform seams. Mobile heads will hide these tabs automatically.
    /// </summary>
    private static void ApplyCapabilityGates(TabItemViewModel[] tabs)
    {
        foreach (var tab in tabs)
        {
            tab.RequiredCapabilities = tab switch
            {
                LabTabViewModel => TabCapabilityRequirements.Desktop,
                BlinkTrainerTabViewModel => TabCapabilityRequirements.ScreenCapture,
                RemoteControlTabViewModel => TabCapabilityRequirements.Desktop,
                BambiTakeoverTabViewModel => TabCapabilityRequirements.Overlays,
                HapticsTabViewModel => TabCapabilityRequirements.Desktop,
                AwarenessTabViewModel => TabCapabilityRequirements.ScreenCapture,
                LockdownTabViewModel => TabCapabilityRequirements.Overlays,
                AttentionCheckTabViewModel => TabCapabilityRequirements.Overlays,
                BouncingTextTabViewModel => TabCapabilityRequirements.Overlays,
                BubbleCountTabViewModel => TabCapabilityRequirements.Overlays,
                BubblePopTabViewModel => TabCapabilityRequirements.Overlays,
                FlashTabViewModel => TabCapabilityRequirements.Overlays,
                PinkFilterTabViewModel => TabCapabilityRequirements.Overlays,
                IntensityRampTabViewModel => TabCapabilityRequirements.Overlays,
                LockCardTabViewModel => TabCapabilityRequirements.Overlays,
                MindWipeTabViewModel => TabCapabilityRequirements.Overlays,
                SchedulerTabViewModel => TabCapabilityRequirements.Overlays,
                VisualsTabViewModel => TabCapabilityRequirements.Overlays,
                SubliminalTabViewModel => TabCapabilityRequirements.Overlays,
                SystemTabViewModel => TabCapabilityRequirements.Desktop,
                SpiralTabViewModel => TabCapabilityRequirements.Overlays,
                SchedulerRampTabViewModel => TabCapabilityRequirements.Overlays,
                VideoTabViewModel => TabCapabilityRequirements.Overlays,
                WebcamTabViewModel => TabCapabilityRequirements.ScreenCapture,
                _ => tab.RequiredCapabilities
            };
        }
    }

    private T? GetTab<T>() where T : TabItemViewModel
        => Services?.GetService<T>();

    private void UpdateHeaderFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        PlayerTitle = s.PlayerLevel switch
        {
            >= 50 => "Certified Blowdoll",
            >= 30 => "Good Girl",
            >= 15 => "Obedient Puppet",
            >= 5 => "Empty Doll",
            _ => "Subject"
        };

        LevelText = $"Lv.{s.PlayerLevel}";
        PlayerLevel = s.PlayerLevel;
        IsLoggedIn = !string.IsNullOrEmpty(s.UnifiedId);
        DisplayName = s.UserDisplayName ?? "";

        var currentVersion = UpdateService.GetCurrentVersion().ToString(3);
        HeaderVersionText = $"v{currentVersion}";
        UpdateButtonText = Loc.Get($"btn_v{currentVersion.Replace(".", "_")}_is_out") ?? $"v{currentVersion}";
        Title = $"Conditioning Control Panel v{currentVersion}";

        SubscribeUpdateEvents();
        RefreshProgressionHeader();
    }

    private void RefreshProgressionHeader()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        var xpNeeded = _progressionService?.GetXPForLevel(s.PlayerLevel) ?? 100.0;
        XpPercent = Math.Clamp(xpNeeded > 0 ? s.PlayerXP / xpNeeded * 100.0 : 0.0, 0.0, 100.0);
        XpText = $"{s.PlayerXP:F0} / {xpNeeded:F0} XP";
        TotalSkillPoints = s.SkillPoints;

        ConditioningTimeVisible = _skillTreeService?.HasSkill("pink_hours") ?? false;
        OnlineUsersVisible = _skillTreeService?.HasSkill("hive_mind") ?? false;
        RankPercentileVisible = _skillTreeService?.HasSkill("popular_girl") ?? false;
    }

    private void InitializeHeaderRow1()
    {
        // Populate the mod selector from the mod service (built-ins + discovered user mods).
        if (_modService != null)
        {
            AvailableMods = new ObservableCollection<ModListItem>(
                _modService.InstalledMods.Select(m => ModListItem.FromManifest(m.Manifest)));
            _modService.ActiveModChanged += OnActiveModChanged;
        }
        else
        {
            AvailableMods = new ObservableCollection<ModListItem>(
            [
                ModListItem.FromManifest(BuiltInMods.CCPDefault),
                ModListItem.FromManifest(BuiltInMods.BambiSleep),
                ModListItem.FromManifest(BuiltInMods.SissyHypno),
                ModListItem.FromManifest(BuiltInMods.Dronification),
                ModListItem.FromManifest(BuiltInMods.Locked)
            ]);
        }

        AvailableLanguages = new ObservableCollection<LanguageItem>(
            LocalizationManager.AvailableLanguages
                .Select(l => new LanguageItem(l.Code, l.DisplayName, l.ShortName)));

        // Populate the quick preset selector from built-in defaults plus user presets.
        var presets = Preset.GetDefaultPresets()
            .Select(PresetItem.FromPreset)
            .Concat(_settingsService?.Current?.UserPresets?.Select(PresetItem.FromPreset) ?? Enumerable.Empty<PresetItem>())
            .ToList();
        AvailablePresets = new ObservableCollection<PresetItem>(presets);

        var activeModId = _modService?.ActiveMod?.Id ?? _settingsService?.Current?.ActiveModId;
        if (activeModId != null)
        {
            SelectedMod = AvailableMods.FirstOrDefault(m => m.Id == activeModId)
                          ?? AvailableMods.FirstOrDefault();
        }
        else
        {
            SelectedMod = AvailableMods.FirstOrDefault();
        }

        var settings = _settingsService?.Current;
        if (settings != null)
        {
            SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settings.Language)
                               ?? AvailableLanguages.FirstOrDefault();
            SelectedPreset = AvailablePresets.FirstOrDefault(p => p.Name == settings.CurrentPresetName);
            StreakCount = settings.CurrentStreak;
            StreakShieldVisible = settings.StreakShieldsRemaining > 0;
            StreakFirePillVisible = settings.CurrentStreak > 0;
        }
        else
        {
            SelectedLanguage = AvailableLanguages.FirstOrDefault();
        }
    }

    private void OnActiveModChanged(object? sender, ModPackage e)
    {
        var match = AvailableMods.FirstOrDefault(m => m.Id == e.Id);
        if (match != null && SelectedMod?.Id != match.Id)
        {
            SelectedMod = match;
        }
        OnPropertyChanged(nameof(SelectedModId));
    }

    partial void OnSelectedModChanged(ModListItem? value)
    {
        if (value == null) return;

        if (_modService != null)
        {
            if (_modService.ActiveMod?.Id == value.Id) return;
            if (!_modService.ActivateMod(value.Id))
            {
                // Activation failed (e.g. mod removed) — revert to the current active mod.
                var current = AvailableMods.FirstOrDefault(m => m.Id == _modService.ActiveMod?.Id);
                if (current != null)
                    SelectedMod = current;
                return;
            }
            _logger?.LogInformation("Active mod changed to {ModId} from header selector", value.Id);
            return;
        }

        if (_settingsService?.Current is not { } settings) return;
        if (settings.ActiveModId == value.Id) return;

        settings.ActiveModId = value.Id;
        _settingsService.Save();
        OnPropertyChanged(nameof(SelectedModId));
        _logger?.LogInformation("Active mod changed to {ModId} from header selector", value.Id);
    }

    partial void OnSelectedLanguageChanged(LanguageItem? value)
    {
        if (value == null) return;
        if (LocalizationManager.Instance.CurrentLanguage == value.Code) return;

        LocalizationManager.Instance.SetLanguage(value.Code);
        if (_settingsService?.Current is { } settings)
        {
            settings.Language = value.Code;
            _settingsService.Save();
        }

        _logger?.LogInformation("Language changed to {LanguageCode} from header selector", value.Code);
    }

    partial void OnSelectedPresetChanged(PresetItem? value)
    {
        if (value == null || _settingsService?.Current is not { } settings) return;
        if (settings.CurrentPresetName == value.Name) return;

        value.Source.ApplyTo(settings);
        _settingsService.Save(suppressCloudBackup: false);
        _logger?.LogInformation("Applied preset from header selector: {PresetName}", value.Name);
    }

    private void SubscribeProgressionEvents()
    {
        if (_progressionService != null)
            _progressionService.LevelUp += OnLevelUp;

        if (_skillTreeService != null)
            _skillTreeService.SkillUnlocked += OnSkillUnlocked;
    }

    private void SubscribeCompanionEvents()
    {
        if (_companionService == null) return;

        _companionService.XPDrained += OnCompanionXpDrained;
        _companionService.LevelUp += OnCompanionLevelUp;
    }

    private void OnCompanionXpDrained(object? sender, double amount)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateHeaderFromSettings();
            FlashXpBar();
        });
    }

    private void OnCompanionLevelUp(object? sender, (CompanionId Companion, int NewLevel) args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var rawCompanionName = CompanionDefinition.GetById(args.Companion).Name;
            var companionName = _modService?.MakeModAware(rawCompanionName) ?? rawCompanionName;

            if (args.NewLevel == CompanionProgress.MaxLevel)
            {
                AddNotification(Loc.Get("title_companion_max_level"),
                    string.Format(Loc.Get("msg_companion_max_level_fmt"), companionName));
            }
            else if (args.NewLevel % 10 == 0)
            {
                AddNotification(Loc.Get("title_companion_level_up"),
                    string.Format(Loc.Get("msg_companion_level_up_fmt"), companionName, args.NewLevel));
            }

            if (args.NewLevel % 10 == 0 || args.NewLevel == CompanionProgress.MaxLevel)
            {
                _ = PlayLevelUpSoundAsync();
            }
        });
    }

    private async Task PlayLevelUpSoundAsync()
    {
        try
        {
            await Task.Yield();
            _sfxPlayer?.Play("lvup", 0.7f);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to play companion level-up sound");
        }
    }

    private void SubscribeRemoteControlEvents()
    {
        if (_remoteControlService == null) return;

        _remoteControlService.ControllerConnectedChanged += OnRemoteControllerConnectedChanged;
        _remoteControlService.SessionStarted += OnRemoteSessionStarted;
        _remoteControlService.SessionEnded += OnRemoteSessionEnded;
    }

    private void SubscribeBrowserHostEvents()
    {
        if (_browserHost == null) return;

        _browserHost.Navigated += (_, uri) => _lastBrowserUrl = uri.ToString();

        _browserHost.FullscreenChanged += (_, isFullscreen) =>
        {
            Dispatcher.UIThread.Post(() => BrowserFullscreenOverlayVisible = isFullscreen);

            // On Windows, route direct-media fullscreen video to the multi-monitor mirror
            // service. HTML pages (e.g. HypnoTube) remain in the browser; extracting their
            // actual video stream is left to a future enhancement.
            if (!OperatingSystem.IsWindows() || _multiMonitor == null) return;

            if (isFullscreen && IsDirectVideoUrl(_lastBrowserUrl))
            {
                _multiMonitor.PlayUrl(_lastBrowserUrl!);
            }
            else if (!isFullscreen)
            {
                _multiMonitor.Stop();
            }
        };
    }

    private static bool IsDirectVideoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var path = uri.AbsolutePath;
        var directExtensions = new[] { ".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v", ".flv", ".m3u8", ".m3u" };
        return directExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }

    private void SubscribeUpdateEvents()
    {
        if (_updateService == null) return;

        _updateService.UpdateAvailable += (_, update) =>
        {
            Dispatcher.UIThread.Post(() => _ = ShowUpdateNotificationAsync(update));
        };
        _updateService.UpdateFailed += (_, ex) =>
        {
            _logger?.LogWarning(ex, "Update check failed");
        };

        // Background check on startup (fire-and-forget; skip on mobile/dev runs).
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                await _updateService.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Background update check failed");
            }
        });
    }

    private async Task ShowUpdateNotificationAsync(UpdateInfo update)
    {
        try
        {
            UpdatePillState = "UpdateAvailable";
            UpdateButtonText = $"v{update.Version}";

            var dialog = new UpdateNotificationDialog(update);
            var owner = GetCurrentWindow();
            bool? result;
            if (owner != null)
                result = await dialog.ShowDialog<bool?>(owner);
            else
            {
                dialog.Show();
                result = dialog.InstallRequested ? true : null;
            }

            if (result == true)
            {
                UpdateButtonText = Loc.Get("btn_downloading");
                var downloaded = await _updateService!.DownloadUpdateAsync();
                if (downloaded)
                {
                    await _updateService.InstallUpdateAsync();
                }
                else
                {
                    UpdateButtonText = $"v{update.Version}";
                    await (_dialogService?.ShowMessageAsync(
                        Loc.Get("title_update_failed"),
                        Loc.Get("msg_update_download_failed")) ?? Task.CompletedTask);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show update notification");
        }
    }

    private Window? GetCurrentWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private void OnRemoteControllerConnectedChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteControlConnected = _remoteControlService?.ControllerConnected ?? false;
            RemoteControlOverlayVisible = RemoteControlConnected;
            RemoteControlStatus = RemoteControlConnected ? Loc.Get("label_controller_connected") : "";
            UpdateRemoteControlOverlayInfo();
        });
    }

    private void OnRemoteSessionStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteControlStatus = Loc.Get("label_remote_session_started");
            UpdateRemoteControlOverlayInfo();
        });
    }

    private void OnRemoteSessionEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            RemoteControlConnected = false;
            RemoteControlOverlayVisible = false;
            RemoteControlStatus = "";
            RemoteControlSessionCode = "";
            RemoteControlPin = "";
            RemoteControllerSessionName = Loc.Get("label_unknown");
        });
    }

    private void UpdateRemoteControlOverlayInfo()
    {
        RemoteControlSessionCode = _remoteControlService?.SessionCode ?? "";
        RemoteControlPin = _remoteControlService?.ConnectPin ?? "";
        RemoteControllerSessionName = _remoteControlService?.ControllerConnected == true
            ? Loc.Get("label_connected_controller")
            : Loc.Get("label_waiting_for_controller");
    }

    private void HookLocalizationRefresh()
    {
        LocalizationManager.Instance.LanguageChanged += (_, _) =>
        {
            UpdateRemoteControlOverlayInfo();
            OnPropertyChanged(nameof(RemoteControlSessionCodeDisplay));
            OnPropertyChanged(nameof(RemoteControllerSessionNameDisplay));
            OnPropertyChanged(nameof(RemoteControlStatusDisplay));
            OnPropertyChanged(nameof(RemoteControlPinDisplay));
        };
    }

    private void OnLevelUp(object? sender, int level)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PlayerLevel = level;
            LevelText = $"Lv.{level}";
            UpdateHeaderFromSettings();
        });
    }

    private void OnSkillUnlocked(object? sender, string skillId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logger?.LogInformation("Skill unlocked: {SkillId}", skillId);
            RefreshProgressionHeader();
        });
    }

    private void WireSessionEvents()
    {
        if (_sessionService == null) return;

        _sessionService.SessionStarted += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsEngineRunning = true;
                UpdateStartButton();
                StartConditioningTimeTracker();
                if (_sessionService.CurrentSession is { } session)
                    _effectOrchestrator?.StartEffects(session);
            });
        };

        _sessionService.SessionStopped += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsEngineRunning = false;
                UpdateStartButton();
                StopConditioningTimeTracker();
                _effectOrchestrator?.StopEffects();
                // Session log end is owned by Core SessionService (real elapsed/XP/completed flag).
            });
        };

        _sessionService.SessionCompleted += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsEngineRunning = false;
                UpdateStartButton();
                StopConditioningTimeTracker();
                _effectOrchestrator?.StopEffects();
                _progressionService?.AddXP(e.XPEarned, XPSource.Session);
                RefreshProgressionHeader();
                _logger?.LogInformation("Session completed: {Name}, XP: {XP}", e.Session.Name, e.XPEarned);

                try
                {
                    var completeWindow = new Windows.SessionCompleteWindow(e.Session, e.Duration, e.XPEarned);
                    completeWindow.Show();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to show session complete window");
                }
            });
        };

        _sessionService.ProgressUpdated += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                SessionStatusText = $"{e.Elapsed:mm\\:ss} / {e.Elapsed + e.Remaining:mm\\:ss} ({e.ProgressPercent:F0}%)";
            });
        };
    }

    private void StartUiTimers()
    {
        _clockTimer = StartPeriodicTimer(TimeSpan.FromSeconds(1), () =>
        {
            Dispatcher.UIThread.Post(() => CurrentTimeText = DateTime.Now.ToString("HH:mm"));
        });

        _sessionProgressTimer = StartPeriodicTimer(TimeSpan.FromSeconds(1), () =>
        {
            Dispatcher.UIThread.Post(UpdateSessionStatus);
        });

        _statPillTimer = StartPeriodicTimer(TimeSpan.FromSeconds(30), () =>
        {
            Dispatcher.UIThread.Post(UpdateConditioningTimeDisplay);
        });

        _bannerTimer = StartPeriodicTimer(TimeSpan.FromSeconds(7), () =>
        {
            Dispatcher.UIThread.Post(() => CurrentBannerIndex = (CurrentBannerIndex + 1) % 3);
        });
    }

    private void UpdateSessionStatus()
    {
        if (_sessionService?.State != SessionState.Running) return;
        var session = _sessionService.CurrentSession;
        if (session == null) return;
        var elapsed = _sessionService.ElapsedTime;
        var remaining = _sessionService.RemainingTime;
        SessionStatusText = $"{elapsed:mm\\:ss} / {elapsed + remaining:mm\\:ss} ({_sessionService.ProgressPercent:F0}%)";
    }

    private void UpdateStartButton()
    {
        if (IsEngineRunning)
        {
            StartButtonIcon = "■";
            StartButtonLabel = "Stop";
            BeginStartButtonFlashOpacity();
        }
        else
        {
            StartButtonIcon = "▶";
            StartButtonLabel = "Start";
            SessionStatusText = string.Empty;
            StartButtonFlashOpacity = 1.0;
        }

        UpdatePauseButton();
    }

    private void UpdatePauseButton()
    {
        var state = _sessionService?.State ?? SessionState.Idle;
        var paused = state == SessionState.Paused;
        PauseButtonText = paused ? "▶" : "⏸";

        if (paused)
        {
            PauseButtonToolTip = "Resume session";
        }
        else if (state == SessionState.Running)
        {
            var penalty = _sessionService?.XPPenalty ?? 0;
            var pauseCount = _sessionService?.PauseCount ?? 0;
            PauseButtonToolTip = $"Pause session (XP penalty: {penalty} XP per pause, current pauses: {pauseCount})";
        }
        else
        {
            PauseButtonToolTip = "Pause session";
        }
    }

    private void BeginStartButtonFlashOpacity()
    {
        StartButtonFlashOpacity = 0.5;

        _ = StartOneShotTimer(TimeSpan.FromMilliseconds(400), () =>
        {
            Dispatcher.UIThread.Post(() => StartButtonFlashOpacity = 1.0);
        });
    }

    #region Tab Navigation

    [RelayCommand]
    private void SelectTab(string key)
    {
        var tab = Tabs.FirstOrDefault(t => t.Key == key);
        if (tab != null)
        {
            SelectedTab = tab;
        }
    }

    [ObservableProperty]
    private bool _isPatreonPopupOpen;

    [RelayCommand]
    private void TogglePatreonPopup()
    {
        IsPatreonPopupOpen = !IsPatreonPopupOpen;
    }

    [RelayCommand]
    private void SelectPatreonExclusiveTab(string key)
    {
        SelectTab(key);
        IsPatreonPopupOpen = false;
    }

    /// <summary>
    /// Handles global tab-switch hotkeys (Ctrl+1..0 and Ctrl+Tab / Ctrl+Shift+Tab).
    /// Called from the view's key-down handler.
    /// </summary>
    public bool HandleKeyGesture(KeyModifiers modifiers, Key key)
    {
        if ((modifiers & KeyModifiers.Control) == 0) return false;

        var index = key switch
        {
            Key.D1 => 0,
            Key.D2 => 1,
            Key.D3 => 2,
            Key.D4 => 3,
            Key.D5 => 4,
            Key.D6 => 5,
            Key.D7 => 6,
            Key.D8 => 7,
            Key.D9 => 8,
            Key.D0 => 9,
            Key.Tab => -1,
            _ => -2
        };

        if (index == -1)
        {
            if (SelectedTab == null) return false;
            var currentIdx = Tabs.IndexOf(SelectedTab);
            if (currentIdx < 0) return false;
            var next = (modifiers & KeyModifiers.Shift) != 0
                ? (currentIdx - 1 + Tabs.Count) % Tabs.Count
                : (currentIdx + 1) % Tabs.Count;
            SelectedTab = Tabs[next];
            return true;
        }

        if (index >= 0 && index < Tabs.Count)
        {
            SelectedTab = Tabs[index];
            return true;
        }

        return false;
    }

    #endregion

    #region Window Chrome

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    [RelayCommand]
    private void MinimizeWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window)
            {
                window.WindowState = global::Avalonia.Controls.WindowState.Minimized;
            }
        });
    }

    [RelayCommand]
    private void MaximizeWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } window)
            {
                window.WindowState = window.WindowState == global::Avalonia.Controls.WindowState.Maximized
                    ? global::Avalonia.Controls.WindowState.Normal
                    : global::Avalonia.Controls.WindowState.Maximized;
            }
        });
    }

    [RelayCommand]
    private async Task CloseWindow()
    {
        await ExitApplicationAsync();
    }

    [RelayCommand]
    private void ShowChaosOverlaySmokeTest()
    {
        try
        {
            var overlay = new ChaosOverlayWindow();
            overlay.Show();
            overlay.ShowCountdown(() =>
            {
                try { overlay.Close(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Chaos overlay smoke-test close failed"); }
            }, shortFlash: true);
            _logger?.LogInformation("Chaos overlay smoke-test displayed");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show Chaos overlay smoke-test");
        }
    }

    #endregion

    #region Title-bar status pills

    [RelayCommand]
    private void StopWebcam()
    {
        _logger?.LogInformation("Stop webcam requested from title bar pill");
        _webcamService?.StopTracking();
    }

    #endregion

    #region Header Links

    [RelayCommand]
    private async Task OpenSupportAsync()
    {
        try
        {
            if (_browserHost != null)
            {
                await _browserHost.NavigateAsync(new Uri("https://www.patreon.com/codebambi"));
            }
            _logger?.LogInformation("Support link opened");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open support link");
        }
    }

    [RelayCommand]
    private async Task OpenBannerLinkAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            if (_browserHost != null)
            {
                await _browserHost.NavigateAsync(new Uri(url));
            }
            _logger?.LogInformation("Banner link opened: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open banner link");
        }
    }

    [RelayCommand]
    private async Task OpenHelpAsync()
    {
        try
        {
            if (_browserHost != null)
            {
                await _browserHost.NavigateAsync(new Uri("https://cclabs.app/help"));
            }
            _logger?.LogInformation("Help link opened");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open help link");
        }
    }

    #endregion

    #region Start/Stop Session

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (_sessionService == null)
        {
            _logger?.LogInformation("Start session requested but ISessionService is not available.");
            return;
        }

        if (IsEngineRunning || _sessionService.State != SessionState.Idle)
        {
            var stopSession = _sessionService.CurrentSession;
            var elapsed = _sessionService.ElapsedTime;
            var remaining = _sessionService.RemainingTime;
            var potentialXp = stopSession?.BonusXP ?? 0;
            var penalty = _sessionService.XPPenalty;
            var finalXp = Math.Max(0, potentialXp - penalty);

            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("title_confirm_stop"),
                $"{Loc.Get("msg_stop_session_confirm")}\n\n" +
                $"{stopSession?.Icon} {stopSession?.Name}\n" +
                $"{Loc.Get("label_elapsed")}: {elapsed:mm\\:ss}\n" +
                $"{Loc.Get("label_remaining")}: {remaining:mm\\:ss}\n\n" +
                $"{Loc.Get("msg_xp_lost")}: {finalXp} XP") ?? Task.FromResult(false));

            if (!confirmed) return;

            _sessionService.StopSession(completed: false);
            IsEngineRunning = false;
            UpdateStartButton();
            StopConditioningTimeTracker();
            return;
        }

        var session = _sessionService.CurrentSession ?? Session.QuickStartFromSettings(_settingsService.Current);

        try
        {
            await _sessionService.StartSessionAsync(session);
            IsEngineRunning = true;
            UpdateStartButton();
            StartConditioningTimeTracker();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start session");
        }
    }

    [RelayCommand]
    private async Task TogglePauseSessionAsync()
    {
        var sessionService = _sessionService;
        if (sessionService == null || sessionService.State == SessionState.Idle)
            return;

        try
        {
            if (sessionService.State == SessionState.Paused)
            {
                sessionService.ResumeSession();
                _logger?.LogInformation("Session resumed");
            }
            else
            {
                var penalty = sessionService.XPPenalty;
                var confirmed = await (_dialogService?.ShowConfirmationAsync(
                    Loc.Get("title_pause_session_confirm"),
                    Loc.GetF("msg_pause_session_body", penalty, penalty + 100)) ?? Task.FromResult(false));

                if (!confirmed) return;

                sessionService.PauseSession();
                _logger?.LogInformation("Session paused (penalty: {Penalty} XP)", penalty);
            }
            UpdatePauseButton();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle session pause");
        }
    }

    #endregion

    #region Conditioning Time Tracker

    private void StartConditioningTimeTracker()
    {
        _conditioningStartTime = DateTime.Now;
        _conditioningBaselineMinutes = _settingsService?.Current?.TotalConditioningMinutes ?? 0;
        _conditioningSecondsCounter = 0;

        _conditioningTimeTimer?.Stop();
        _conditioningTimeTimer = StartPeriodicTimer(TimeSpan.FromSeconds(1), () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _conditioningSecondsCounter++;
                UpdateConditioningTimeDisplay();

                if (_conditioningSecondsCounter >= 60)
                {
                    _conditioningSecondsCounter = 0;
                    var settings = _settingsService?.Current;
                    if (settings != null)
                    {
                        settings.TotalConditioningMinutes += 1.0;
                        _settingsService?.Save(suppressCloudBackup: false);
                    }
                }
            });
        });
    }

    private void StopConditioningTimeTracker()
    {
        _conditioningTimeTimer?.Stop();
        _conditioningTimeTimer = null;

        var elapsed = DateTime.Now - _conditioningStartTime;
        var expectedTotal = _conditioningBaselineMinutes + elapsed.TotalMinutes;
        var currentStored = _settingsService?.Current?.TotalConditioningMinutes ?? 0;
        var remaining = expectedTotal - currentStored;

        if (remaining > 0 && _settingsService?.Current != null)
        {
            _settingsService.Current.TotalConditioningMinutes += remaining;
            _settingsService.Save(suppressCloudBackup: false);
        }

        UpdateConditioningTimeDisplay();
    }

    private void UpdateConditioningTimeDisplay()
    {
        var settings = _settingsService?.Current;
        double totalMinutes;

        if (IsEngineRunning)
        {
            var sessionElapsed = DateTime.Now - _conditioningStartTime;
            totalMinutes = _conditioningBaselineMinutes + sessionElapsed.TotalMinutes;
        }
        else
        {
            totalMinutes = settings?.TotalConditioningMinutes ?? 0;
        }

        totalMinutes = double.IsFinite(totalMinutes) ? Math.Max(0, totalMinutes) : 0;
        var totalSeconds = (long)(totalMinutes * 60);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        ConditioningTimeText = $"{hours}h {minutes}m {seconds}s";
    }

    #endregion

    #region Login / OAuth

    [RelayCommand]
    private async Task OpenLoginDialogAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            _logger?.LogInformation("Login dialog requested but no main window is available.");
            return;
        }

        _logger?.LogInformation("Unified login dialog requested.");
        var loginDialog = new global::ConditioningControlPanel.Avalonia.Dialogs.LoginDialog();
        var confirmed = await loginDialog.ShowDialog<bool>(mainWindow);
        var result = loginDialog.Result;

        if (!confirmed || result?.Success != true || result.UnifiedId is null)
        {
            _logger?.LogInformation("Login dialog dismissed or failed.");
            return;
        }

        var settings = _settingsService?.Current;
        if (settings != null)
        {
            settings.UnifiedId = result.UnifiedId;
            settings.UserDisplayName = result.DisplayName;
            settings.HasLinkedPatreon = string.Equals(result.Provider, "patreon", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(result.LinkedProvider, "patreon", StringComparison.OrdinalIgnoreCase);
            settings.HasLinkedDiscord = string.Equals(result.Provider, "discord", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(result.LinkedProvider, "discord", StringComparison.OrdinalIgnoreCase);
            _settingsService?.Save();
        }

        UpdateHeaderFromSettings();
        _logger?.LogInformation("User logged in via {Provider}: {DisplayName} ({UnifiedId})", result.Provider, result.DisplayName, result.UnifiedId);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var settings = _settingsService?.Current;
        if (settings == null) return;

        // TODO: Push final profile sync before clearing local data once ProfileSyncService is available in Core.
        settings.UnifiedId = null;
        settings.UserDisplayName = null;
        settings.HasLinkedDiscord = false;
        settings.HasLinkedPatreon = false;
        settings.PlayerXP = 0;
        settings.PlayerLevel = 1;
        settings.SkillPoints = 0;
        settings.UnlockedSkills = new List<string>();
        settings.TotalConditioningMinutes = 0;
        _settingsService?.Save();

        IsLoggedIn = false;
        DisplayName = "";
        UpdateHeaderFromSettings();

        _logger?.LogInformation("User logged out.");
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_logged_out"),
            Loc.Get("msg_logged_out")) ?? Task.CompletedTask);
    }

    #endregion

    #region Global Commands

    [RelayCommand]
    private void ToggleRemoteControlOverlay()
    {
        RemoteControlOverlayVisible = !RemoteControlOverlayVisible;
    }

    [RelayCommand]
    private async Task StopRemoteControlSessionAsync()
    {
        if (_remoteControlService == null)
        {
            _logger?.LogInformation("Stop remote session requested but IRemoteControlService is not available.");
            return;
        }

        try
        {
            await _remoteControlService.StopSessionAsync();
            _logger?.LogInformation("Remote control session stopped from overlay.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop remote control session");
        }
    }

    [RelayCommand]
    private void ToggleDropOverlay()
    {
        DropOverlayVisible = !DropOverlayVisible;
    }

    [RelayCommand]
    private void ToggleTutorialOverlay()
    {
        TutorialOverlayVisible = !TutorialOverlayVisible;
    }

    [RelayCommand]
    private void ToggleBrowserFullscreenOverlay()
    {
        BrowserFullscreenOverlayVisible = !BrowserFullscreenOverlayVisible;
    }

    public void AddNotification(string title, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Notifications.Add(new Models.NotificationItem(title, message));
        });
    }

    [RelayCommand]
    private async Task ManageModsAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null)
        {
            _logger?.LogInformation("Mod manager requested but no main window is available.");
            return;
        }

        _logger?.LogInformation("Mod manager dialog requested.");
        var dialog = new global::ConditioningControlPanel.Avalonia.Dialogs.ModManagerDialog();
        await dialog.ShowDialog<bool>(mainWindow);

        // Refresh the mod list and active mod in case mods were installed/uninstalled/activated.
        if (_modService != null)
        {
            AvailableMods = new ObservableCollection<ModListItem>(
                _modService.InstalledMods.Select(m => ModListItem.FromManifest(m.Manifest)));
        }

        var activeModId = _modService?.ActiveMod?.Id ?? _settingsService?.Current?.ActiveModId;
        if (activeModId != null)
        {
            SelectedMod = AvailableMods.FirstOrDefault(m => m.Id == activeModId)
                          ?? AvailableMods.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            _settingsService?.SaveImmediate(suppressCloudBackup: false);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                Loc.Get("msg_settings_saved")) ?? Task.CompletedTask);
            _logger?.LogInformation("Settings saved from main window");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }

    [RelayCommand]
    private async Task ExitApplicationAsync()
    {
        if (_sessionService?.State == SessionState.Running)
        {
            var confirm = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("title_confirm_exit"),
                Loc.Get("msg_engine_is_running_stop_and_exit")) ?? Task.FromResult(false));
            if (!confirm) return;
            _sessionService.StopSession(completed: false);
        }

        StopUiTimers();
        StopConditioningTimeTracker();
        ReleaseInputHook();

        try
        {
            _settingsService?.SaveImmediate(suppressCloudBackup: false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save settings during exit");
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_updateService == null) return;

        UpdateButtonText = Loc.Get("btn_checking");
        try
        {
            _logger?.LogInformation("Manual update check requested");
            var update = await _updateService.CheckForUpdatesAsync(forceCheck: true);
            if (update == null || !update.IsNewer)
            {
                UpdateButtonText = GetVersionOutText();
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_up_to_date"),
                    Loc.Get("msg_you_are_on_the_latest_version")) ?? Task.CompletedTask);
            }
            // If an update is available, the UpdateAvailable handler shows the dialog.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Update check failed");
            UpdateButtonText = GetVersionOutText();
        }
    }

    private static string GetVersionOutText()
    {
        var currentVersion = UpdateService.GetCurrentVersion().ToString(3);
        return Loc.Get($"btn_v{currentVersion.Replace(".", "_")}_is_out") ?? $"v{currentVersion}";
    }

    #endregion

    #region Hotkeys / Input Hook

    private void RegisterGlobalHotkeys()
    {
        if (_hotkeyProvider == null || _platformCapabilities?.SupportsGlobalHotkeys != true) return;

        try
        {
            _hotkeyProvider.HotkeyPressed += OnGlobalHotkeyPressed;
            RegisterChatGlobalHotkey();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to register global hotkeys");
        }
    }

    private void RegisterChatGlobalHotkey()
    {
        var prompt = _settingsService?.Current?.CompanionPrompt;
        if (prompt == null) return;

        if (prompt.ChatShortcutGlobal
            && AvaloniaKeyInterop.TryGetVirtualKeyCode(prompt.ChatShortcutKey, out var vk))
        {
            var mods = AvaloniaKeyInterop.ParseModifiers(prompt.ChatShortcutModifiers);
            _hotkeyProvider?.RegisterHotkey("chat", mods, vk);
        }
    }

    private void OnGlobalHotkeyPressed(object? sender, string id)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logger?.LogInformation("Global hotkey pressed: {Id}", id);

            if (id == "chat")
            {
                _avatarWindowService?.OpenChatWindow();
            }
        });
    }

    private void HookInput()
    {
        if (_inputHook == null || _platformCapabilities?.SupportsInputHooks != true) return;

        try
        {
            _inputHook.KeyPressed += OnInputKeyPressed;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to hook input");
        }
    }

    private void ReleaseInputHook()
    {
        try
        {
            _inputHook?.Dispose();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to release input hook");
        }
    }

    private void OnInputKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var settings = _settingsService?.Current;
        if (settings == null) return;

        if (settings.PanicKeyEnabled)
        {
            var panicKeyString = settings.PanicKey;
            if (int.TryParse(panicKeyString, out var panicVk) && panicVk == e.VirtualKeyCode)
            {
                Dispatcher.UIThread.Post(HandlePanicKeyPress);
            }
        }
    }

    private async void HandlePanicKeyPress()
    {
        if (_sessionService?.State == SessionState.Running)
        {
            _logger?.LogInformation("Panic key pressed while session running.");
            _sessionService.PauseSession();
            _sessionService.StopSession(completed: false);
            IsEngineRunning = false;
            UpdateStartButton();
            StopConditioningTimeTracker();
        }
        else
        {
            _logger?.LogInformation("Panic key pressed while idle; exiting application.");
            await ExitApplicationAsync();
        }
    }

    #endregion

    private static DispatcherTimer StartPeriodicTimer(TimeSpan interval, Action callback)
    {
        var timer = new DispatcherTimer { Interval = interval };
        timer.Tick += (_, _) => callback();
        timer.Start();
        return timer;
    }

    private static DispatcherTimer StartOneShotTimer(TimeSpan dueTime, Action callback)
    {
        var timer = new DispatcherTimer { Interval = dueTime };
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            timer.Stop();
            timer.Tick -= handler;
            callback();
        };
        timer.Tick += handler;
        timer.Start();
        return timer;
    }

    #region Cleanup

    private void StopUiTimers()
    {
        _clockTimer?.Stop();
        _sessionProgressTimer?.Stop();
        _conditioningTimeTimer?.Stop();
        _statPillTimer?.Stop();
        _bannerTimer?.Stop();
        _xpFlashTimer?.Stop();
        _clockTimer = null;
        _sessionProgressTimer = null;
        _conditioningTimeTimer = null;
        _statPillTimer = null;
        _bannerTimer = null;
        _xpFlashTimer = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopUiTimers();
        StopConditioningTimeTracker();
        ReleaseInputHook();

        if (_hotkeyProvider != null)
        {
            _hotkeyProvider.HotkeyPressed -= OnGlobalHotkeyPressed;
        }

        if (_modService != null)
        {
            _modService.ActiveModChanged -= OnActiveModChanged;
        }

        if (_progressionService != null)
        {
            _progressionService.LevelUp -= OnLevelUp;
        }

        if (_companionService != null)
        {
            _companionService.XPDrained -= OnCompanionXpDrained;
            _companionService.LevelUp -= OnCompanionLevelUp;
        }

        if (_skillTreeService != null)
        {
            _skillTreeService.SkillUnlocked -= OnSkillUnlocked;
        }

        if (_remoteControlService != null)
        {
            _remoteControlService.ControllerConnectedChanged -= OnRemoteControllerConnectedChanged;
            _remoteControlService.SessionStarted -= OnRemoteSessionStarted;
            _remoteControlService.SessionEnded -= OnRemoteSessionEnded;
        }
    }

    #endregion
}
