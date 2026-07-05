using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Autonomy;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Autonomy partial.
/// Exposes autonomy mode toggles, intensity/cooldown sliders, behaviour checkboxes,
/// and test/force commands. Wired to <see cref="IAutonomyService"/> for start/stop,
/// test trigger, and force-start.
/// </summary>
public partial class BambiTakeoverTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly IAutonomyService? _autonomyService;
    private readonly ILogger<BambiTakeoverTabViewModel>? _logger;

    public BambiTakeoverTabViewModel() : base("bambitakeover", "Takeover", "🌀")
    {
        IsPremiumLocked = false;
        StartStopButtonText = Loc.Get("btn_start_2");
        StartStopButtonForeground = "#90EE90";
    }

    public BambiTakeoverTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        IAutonomyService autonomyService,
        ILogger<BambiTakeoverTabViewModel> logger) : base("bambitakeover", "Takeover", "🌀")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _autonomyService = autonomyService;
        _logger = logger;
        LoadFromSettings();

        var settings = _settingsService.Current;
        IsPremiumLocked = settings == null || !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        if (settings != null)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    [ObservableProperty]
    private bool _autonomyEnabled;

    [ObservableProperty]
    private bool _autonomyConsentGiven;

    [ObservableProperty]
    private int _autonomyIntensity = 50;

    [ObservableProperty]
    private int _autonomyCooldown = 60;

    [ObservableProperty]
    private int _autonomyInterval = 300;

    [ObservableProperty]
    private bool _autonomyIdleTrigger;

    [ObservableProperty]
    private bool _autonomyRandomTrigger;

    [ObservableProperty]
    private bool _autonomyTimeAware;

    [ObservableProperty]
    private bool _canTriggerFlash;

    [ObservableProperty]
    private bool _canTriggerVideo;

    [ObservableProperty]
    private bool _canTriggerWebVideo;

    [ObservableProperty]
    private bool _canTriggerWallpaper;

    [ObservableProperty]
    private bool _canTriggerSubliminal;

    [ObservableProperty]
    private bool _canTriggerBubbles;

    [ObservableProperty]
    private bool _canComment;

    [ObservableProperty]
    private bool _canTriggerMindWipe;

    [ObservableProperty]
    private bool _canTriggerLockCard;

    [ObservableProperty]
    private bool _canTriggerSpiral;

    [ObservableProperty]
    private bool _canTriggerPinkFilter;

    [ObservableProperty]
    private bool _canTriggerBouncingText;

    [ObservableProperty]
    private bool _canTriggerBubbleCount;

    [ObservableProperty]
    private bool _showTakeoverCountdownBar = true;

    partial void OnShowTakeoverCountdownBarChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ShowTakeoverCountdownBar = value;
        Save();
    }

    [ObservableProperty]
    private int _announcementChance = 50;

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private string _startStopButtonText = Loc.Get("btn_start_2");

    [ObservableProperty]
    private string _startStopButtonForeground = "#90EE90";

    partial void OnAutonomyEnabledChanged(bool value)
    {
        StartStopButtonText = value ? Loc.Get("btn_stop_2") : Loc.Get("btn_start_2");
        StartStopButtonForeground = value ? "#FF6B6B" : "#90EE90";

        if (_settingsService?.Current == null) return;

        if (value && !AutonomyConsentGiven)
        {
            _ = PromptForConsentAsync();
            return;
        }

        _settingsService.Current.AutonomyModeEnabled = value;
        Save();

        if (value)
            _autonomyService?.Start();
        else
            _autonomyService?.Stop();

        _logger?.LogInformation("Autonomy Mode toggled: {Enabled}", value);
    }

    partial void OnAutonomyIntensityChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyIntensity = value;
        Save();
    }

    partial void OnAutonomyCooldownChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyCooldownSeconds = value;
        Save();
    }

    partial void OnAutonomyIntervalChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyRandomIntervalSeconds = value;
        Save();
    }

    partial void OnAutonomyIdleTriggerChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyIdleTriggerEnabled = value;
        Save();
    }

    partial void OnAutonomyRandomTriggerChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyRandomTriggerEnabled = value;
        Save();
    }

    partial void OnAutonomyTimeAwareChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyTimeAwareEnabled = value;
        Save();
    }

    partial void OnAnnouncementChanceChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AutonomyAnnouncementChance = value;
        Save();
    }

    partial void OnCanTriggerFlashChanged(bool value) => SaveBehavior(nameof(CanTriggerFlash), value, s => s.AutonomyCanTriggerFlash = value);
    partial void OnCanTriggerVideoChanged(bool value) => SaveBehavior(nameof(CanTriggerVideo), value, s => s.AutonomyCanTriggerVideo = value);
    partial void OnCanTriggerWebVideoChanged(bool value) => SaveBehavior(nameof(CanTriggerWebVideo), value, s => s.AutonomyCanTriggerWebVideo = value);
    partial void OnCanTriggerWallpaperChanged(bool value) => SaveBehavior(nameof(CanTriggerWallpaper), value, s => s.AutonomyCanTriggerWallpaper = value);
    partial void OnCanTriggerSubliminalChanged(bool value) => SaveBehavior(nameof(CanTriggerSubliminal), value, s => s.AutonomyCanTriggerSubliminal = value);
    partial void OnCanTriggerBubblesChanged(bool value) => SaveBehavior(nameof(CanTriggerBubbles), value, s => s.AutonomyCanTriggerBubbles = value);
    partial void OnCanCommentChanged(bool value) => SaveBehavior(nameof(CanComment), value, s => s.AutonomyCanComment = value);
    partial void OnCanTriggerMindWipeChanged(bool value) => SaveBehavior(nameof(CanTriggerMindWipe), value, s => s.AutonomyCanTriggerMindWipe = value);
    partial void OnCanTriggerLockCardChanged(bool value) => SaveBehavior(nameof(CanTriggerLockCard), value, s => s.AutonomyCanTriggerLockCard = value);
    partial void OnCanTriggerSpiralChanged(bool value) => SaveBehavior(nameof(CanTriggerSpiral), value, s => s.AutonomyCanTriggerSpiral = value);
    partial void OnCanTriggerPinkFilterChanged(bool value) => SaveBehavior(nameof(CanTriggerPinkFilter), value, s => s.AutonomyCanTriggerPinkFilter = value);
    partial void OnCanTriggerBouncingTextChanged(bool value) => SaveBehavior(nameof(CanTriggerBouncingText), value, s => s.AutonomyCanTriggerBouncingText = value);
    partial void OnCanTriggerBubbleCountChanged(bool value) => SaveBehavior(nameof(CanTriggerBubbleCount), value, s => s.AutonomyCanTriggerBubbleCount = value);

    private void SaveBehavior(string name, bool value, Action<AppSettings> apply)
    {
        if (_settingsService?.Current == null) return;
        apply(_settingsService.Current);
        Save();
        _logger?.LogInformation("Autonomy behavior {Name} = {Value}", name, value);
    }

    private async Task PromptForConsentAsync()
    {
        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("dialog_title_enable_autonomy_mode"),
            Loc.Get("dialog_message_enable_autonomy_mode")) ?? Task.FromResult(false));

        if (confirm && _settingsService?.Current != null)
        {
            AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyModeEnabled = true;
            Save();
            _logger?.LogInformation("Autonomy consent granted and enabled");
        }
        else
        {
            AutonomyEnabled = false;
        }
    }

    [RelayCommand]
    private async Task ToggleAutonomyAsync()
    {
        if (_settingsService?.Current == null) return;

        if (!AutonomyEnabled && !AutonomyConsentGiven)
        {
            var confirm = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("dialog_title_autonomy_mode_consent"),
                Loc.Get("dialog_message_autonomy_mode_consent")) ?? Task.FromResult(false));
            if (!confirm) return;
            AutonomyConsentGiven = true;
            _settingsService.Current.AutonomyConsentGiven = true;
        }

        AutonomyEnabled = !AutonomyEnabled;
        _settingsService.Current.AutonomyModeEnabled = AutonomyEnabled;
        Save();
        _logger?.LogInformation("Autonomy Mode button toggled: {Enabled}", AutonomyEnabled);
    }

    [RelayCommand]
    private void TestAutonomy()
    {
        _logger?.LogInformation("Autonomy test trigger requested");
        _autonomyService?.TestTrigger();
    }

    [RelayCommand]
    private void ForceStartAutonomy()
    {
        _logger?.LogInformation("Autonomy force start requested");
        _autonomyService?.ForceStart();
    }

    [RelayCommand]
    private async Task StartStopAsync()
    {
        await ToggleAutonomyAsync();
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("dialog_title_premium_locked"),
            Loc.Get("dialog_message_takeover_premium_locked"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.HasLinkedPatreon) or nameof(AppSettings.HasLinkedDiscord))
        {
            var settings = _settingsService?.Current;
            if (settings != null)
            {
                IsPremiumLocked = !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
            }
        }
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        // Clamp persisted values into the sliders' ranges BEFORE assigning, and write the clamped
        // value back so the setting and the UI agree: an out-of-range stored value (old-version
        // scale, cloud-restored settings) otherwise silently clamps the slider to its bound while
        // the label keeps the raw value (#485). Mirrors WPF MainWindow.Settings.cs:187-193; bounds
        // match the sliders in BambiTakeoverTabView.axaml (intensity 0-100, cooldown 10-300,
        // interval 60-600, announcement 0-100).
        s.AutonomyIntensity = Math.Clamp(s.AutonomyIntensity, 0, 100);
        s.AutonomyCooldownSeconds = Math.Clamp(s.AutonomyCooldownSeconds, 10, 300);
        s.AutonomyRandomIntervalSeconds = Math.Clamp(s.AutonomyRandomIntervalSeconds, 60, 600);
        s.AutonomyAnnouncementChance = Math.Clamp(s.AutonomyAnnouncementChance, 0, 100);

        AutonomyEnabled = s.AutonomyModeEnabled;
        AutonomyConsentGiven = s.AutonomyConsentGiven;
        AutonomyIntensity = s.AutonomyIntensity;
        AutonomyCooldown = s.AutonomyCooldownSeconds;
        AutonomyInterval = s.AutonomyRandomIntervalSeconds;
        AutonomyIdleTrigger = s.AutonomyIdleTriggerEnabled;
        AutonomyRandomTrigger = s.AutonomyRandomTriggerEnabled;
        ShowTakeoverCountdownBar = s.ShowTakeoverCountdownBar;
        AutonomyTimeAware = s.AutonomyTimeAwareEnabled;
        AnnouncementChance = s.AutonomyAnnouncementChance;
        CanTriggerFlash = s.AutonomyCanTriggerFlash;
        CanTriggerVideo = s.AutonomyCanTriggerVideo;
        CanTriggerWebVideo = s.AutonomyCanTriggerWebVideo;
        CanTriggerWallpaper = s.AutonomyCanTriggerWallpaper;
        CanTriggerSubliminal = s.AutonomyCanTriggerSubliminal;
        CanTriggerBubbles = s.AutonomyCanTriggerBubbles;
        CanTriggerMindWipe = s.AutonomyCanTriggerMindWipe;
        CanTriggerLockCard = s.AutonomyCanTriggerLockCard;
        CanTriggerSpiral = s.AutonomyCanTriggerSpiral;
        CanComment = s.AutonomyCanComment;
        CanTriggerPinkFilter = s.AutonomyCanTriggerPinkFilter;
        CanTriggerBouncingText = s.AutonomyCanTriggerBouncingText;
        CanTriggerBubbleCount = s.AutonomyCanTriggerBubbleCount;
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save autonomy settings"); }
    }
}
