using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.BouncingText;
using ConditioningControlPanel.Core.Services.MindWipe;
using ConditioningControlPanel.Core.Services.Overlays;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.LevelFeatures partial.
/// Groups the level-gated mini-features (Bubble Count, Bouncing Text, Mind Wipe, Brain Drain)
/// into a single tab while dedicated feature controls are being ported.
/// </summary>
public partial class LevelFeaturesTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ISessionService? _sessionService;
    private readonly IBubbleCountService? _bubbleCountService;
    private readonly IBouncingTextService? _bouncingTextService;
    private readonly IMindWipeService? _mindWipeService;
    private readonly IOverlayService? _overlayService;
    private readonly ILogger<LevelFeaturesTabViewModel>? _logger;

    private int _playerLevel = 1;

    public const int BubbleCountLevel = 50;
    public const int BouncingTextLevel = 60;
    // Shared with AvaloniaOverlayService's service-side gate (WPF parity) — single source of truth.
    public const int BrainDrainLevel = AppSettings.BrainDrainUnlockLevel;
    public const int MindWipeLevel = 75;

    public LevelFeaturesTabViewModel() : base("levelfeatures", "Level Features", "🎚️")
    {
    }

    public LevelFeaturesTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ISessionService sessionService,
        IBubbleCountService bubbleCountService,
        IBouncingTextService bouncingTextService,
        IMindWipeService mindWipeService,
        IOverlayService overlayService,
        ILogger<LevelFeaturesTabViewModel> logger) : base("levelfeatures", "Level Features", "🎚️")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _sessionService = sessionService;
        _bubbleCountService = bubbleCountService;
        _bouncingTextService = bouncingTextService;
        _mindWipeService = mindWipeService;
        _overlayService = overlayService;
        _logger = logger;

        PlayerLevel = _settingsService?.Current?.PlayerLevel ?? 1;

        if (_settingsService?.Current is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.BubbleCountEnabled)
            or nameof(AppSettings.BouncingTextEnabled)
            or nameof(AppSettings.MindWipeEnabled)
            or nameof(AppSettings.BrainDrainEnabled)
            or nameof(AppSettings.PlayerLevel))
        {
            RefreshFeatureState();
        }
    }

    private void RefreshFeatureState()
    {
        PlayerLevel = _settingsService?.Current?.PlayerLevel ?? PlayerLevel;
        OnPropertyChanged(nameof(BubbleCountEnabled));
        OnPropertyChanged(nameof(BouncingTextEnabled));
        OnPropertyChanged(nameof(MindWipeEnabled));
        OnPropertyChanged(nameof(BrainDrainEnabled));
    }

    private bool IsSessionRunning => _sessionService?.State == SessionState.Running;
    private bool IsInPresetSession => _sessionService?.CurrentSession != null;

    #region Level / Lock State

    public int PlayerLevel
    {
        get => _playerLevel;
        set
        {
            if (SetProperty(ref _playerLevel, value))
            {
                OnPropertyChanged(nameof(IsBubbleCountLocked));
                OnPropertyChanged(nameof(IsBouncingTextLocked));
                OnPropertyChanged(nameof(IsBrainDrainLocked));
                OnPropertyChanged(nameof(IsMindWipeLocked));
                OnPropertyChanged(nameof(BubbleCountUnlockText));
                OnPropertyChanged(nameof(BouncingTextUnlockText));
                OnPropertyChanged(nameof(BrainDrainUnlockText));
                OnPropertyChanged(nameof(MindWipeUnlockText));
            }
        }
    }

    public int BubbleCountLockLevel => BubbleCountLevel;
    public int BouncingTextLockLevel => BouncingTextLevel;
    public int BrainDrainLockLevel => BrainDrainLevel;
    public int MindWipeLockLevel => MindWipeLevel;

    public bool IsBubbleCountLocked => !IsLevelUnlocked(BubbleCountLevel);
    public bool IsBouncingTextLocked => !IsLevelUnlocked(BouncingTextLevel);
    public bool IsBrainDrainLocked => !IsLevelUnlocked(BrainDrainLevel);
    public bool IsMindWipeLocked => !IsLevelUnlocked(MindWipeLevel);

    public string BubbleCountUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BubbleCountLevel);
    public string BouncingTextUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BouncingTextLevel);
    public string BrainDrainUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", BrainDrainLevel);
    public string MindWipeUnlockText => Loc.GetF("feature_unlocks_at_level_fmt", MindWipeLevel);

    private bool IsLevelUnlocked(int requiredLevel)
    {
        if (_settingsService?.Current is not null)
            return _settingsService.Current.IsLevelUnlocked(requiredLevel);

        return PlayerLevel >= requiredLevel;
    }

    #endregion

    #region Bubble Count (Level 50)

    public bool BubbleCountEnabled
    {
        get => _settingsService?.Current?.BubbleCountEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Bubble Count toggled: {Enabled}", value);

            if (IsSessionRunning)
            {
                if (value)
                    _bubbleCountService?.Start();
                else
                    _bubbleCountService?.Stop();
            }
        }
    }

    public int BubbleCountFrequency
    {
        get => _settingsService?.Current?.BubbleCountFrequency ?? 3;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountFrequency = value;
            OnPropertyChanged();
            Save();

            if (IsSessionRunning && _bubbleCountService?.IsRunning == true)
            {
                _bubbleCountService.RefreshSchedule();
            }
        }
    }

    public int BubbleCountDifficulty
    {
        get => _settingsService?.Current?.BubbleCountDifficulty ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountDifficulty = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool BubbleCountStrictLock
    {
        get => _settingsService?.Current?.BubbleCountStrictLock ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BubbleCountStrictLock = value;
            OnPropertyChanged();
            Save();
        }
    }

    [RelayCommand]
    private async Task ToggleBubbleCountStrictAsync()
    {
        if (BubbleCountStrictLock)
        {
            var confirmed = await (_dialogService?.ShowConfirmationAsync(
                Loc.Get("dialog_strict_bubble_count_title"),
                Loc.Get("dialog_strict_bubble_count_body")) ?? Task.FromResult(false));
            if (!confirmed)
            {
                BubbleCountStrictLock = false;
                return;
            }
        }

        Save();
    }

    [RelayCommand]
    private async Task TestBubbleCountAsync()
    {
        _logger?.LogInformation("Test bubble count requested");
        try
        {
            _bubbleCountService?.TriggerGame(forceTest: true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to test bubble count");
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleBubbleCountEnabled()
    {
        BubbleCountEnabled = !BubbleCountEnabled;
    }

    [RelayCommand]
    private async Task OpenBubbleCountDetailsAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("feature_bubble_count"),
            Loc.Get("msg_level_feature_settings_on_dashboard")) ?? Task.CompletedTask);
    }

    #endregion

    #region Bouncing Text (Level 60)

    public bool BouncingTextEnabled
    {
        get => _settingsService?.Current?.BouncingTextEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Bouncing Text toggled: {Enabled}", value);

            if (IsSessionRunning)
            {
                if (value)
                    _bouncingTextService?.Start();
                else
                    _bouncingTextService?.Stop();
            }
        }
    }

    public int BouncingTextSpeed
    {
        get => _settingsService?.Current?.BouncingTextSpeed ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextSpeed = value;
            OnPropertyChanged();
            Save();

            if (_bouncingTextService?.IsRunning == true)
            {
                _bouncingTextService.Refresh();
            }
        }
    }

    public int BouncingTextSize
    {
        get => _settingsService?.Current?.BouncingTextSize ?? 48;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextSize = value;
            OnPropertyChanged();
            Save();

            if (_bouncingTextService?.IsRunning == true)
            {
                _bouncingTextService.Refresh();
            }
        }
    }

    public bool BouncingTextAlwaysOnTop
    {
        get => _settingsService?.Current?.BouncingTextAlwaysOnTop ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BouncingTextAlwaysOnTop = value;
            OnPropertyChanged();
            Save();
        }
    }

    [RelayCommand]
    private async Task EditBouncingTextPhrasesAsync()
    {
        _logger?.LogInformation("Edit bouncing text phrases requested");

        if (_settingsService?.Current is not { } settings) return;

        var owner = GetMainWindow();
        if (owner == null)
        {
            _logger?.LogWarning("Cannot show bouncing text editor: no main window available.");
            return;
        }

        var editor = new TextEditorDialog(Loc.Get("label_bouncing_text"), settings.BouncingTextPool);
        var result = await editor.ShowDialog<bool?>(owner);
        if (result == true && editor.ResultData != null)
        {
            settings.BouncingTextPool = editor.ResultData;
            Save();
            if (_bouncingTextService?.IsRunning == true)
            {
                _bouncingTextService.Refresh();
            }
            _logger?.LogInformation("Bouncing text phrases updated: {Count} items", editor.ResultData.Count);
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    [RelayCommand]
    private async Task TestBouncingTextAsync()
    {
        _logger?.LogInformation("Test bouncing text requested");
        try
        {
            _bouncingTextService?.Stop();
            _bouncingTextService?.Start();

            // Stop the demo after a few seconds so it doesn't linger.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                _bouncingTextService?.Stop();
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to test bouncing text");
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleBouncingTextEnabled()
    {
        BouncingTextEnabled = !BouncingTextEnabled;
    }

    [RelayCommand]
    private async Task OpenBouncingTextDetailsAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("feature_bouncing_text"),
            Loc.Get("msg_level_feature_settings_on_dashboard")) ?? Task.CompletedTask);
    }

    #endregion

    #region Mind Wipe (Level 75)

    public bool MindWipeEnabled
    {
        get => _settingsService?.Current?.MindWipeEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Mind Wipe toggled: {Enabled}", value);

            // Only drive the standalone scheduler when running outside of a preset session.
            if (IsSessionRunning && !IsInPresetSession)
            {
                if (value)
                {
                    _mindWipeService?.Start(
                        _settingsService.Current.MindWipeFrequency,
                        _settingsService.Current.MindWipeVolume / 100.0);
                }
                else
                {
                    _mindWipeService?.Stop();
                }
            }
        }
    }

    public int MindWipeFrequency
    {
        get => _settingsService?.Current?.MindWipeFrequency ?? 2;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeFrequency = value;
            OnPropertyChanged();
            Save();

            if (_mindWipeService?.IsRunning == true)
            {
                _mindWipeService.UpdateSettings(
                    _settingsService.Current.MindWipeFrequency,
                    _settingsService.Current.MindWipeVolume / 100.0);
            }
        }
    }

    public int MindWipeVolume
    {
        get => _settingsService?.Current?.MindWipeVolume ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeVolume = value;
            OnPropertyChanged();
            Save();

            if (_mindWipeService?.IsRunning == true)
            {
                _mindWipeService.UpdateSettings(
                    _settingsService.Current.MindWipeFrequency,
                    _settingsService.Current.MindWipeVolume / 100.0);
            }
        }
    }

    public bool MindWipeLoop
    {
        get => _settingsService?.Current?.MindWipeLoop ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.MindWipeLoop = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Mind Wipe loop toggled: {Looping}", value);

            if (value)
            {
                _mindWipeService?.StartLoop(_settingsService.Current.MindWipeVolume / 100.0);
            }
            else
            {
                _mindWipeService?.StopLoop();
            }
        }
    }

    [RelayCommand]
    private async Task TestMindWipeAsync()
    {
        _logger?.LogInformation("Test mind wipe requested");
        try
        {
            _mindWipeService?.TriggerOnce();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to test mind wipe");
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleMindWipeEnabled()
    {
        MindWipeEnabled = !MindWipeEnabled;
    }

    [RelayCommand]
    private async Task OpenMindWipeDetailsAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("feature_mind_wipe"),
            Loc.Get("msg_level_feature_settings_on_dashboard")) ?? Task.CompletedTask);
    }

    #endregion

    #region Brain Drain (Level 70)

    public bool BrainDrainEnabled
    {
        get => _settingsService?.Current?.BrainDrainEnabled ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainEnabled = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Brain Drain toggled: {Enabled}", value);

            if (IsSessionRunning)
            {
                _overlayService?.RefreshOverlays();
            }
        }
    }

    public int BrainDrainIntensity
    {
        get => _settingsService?.Current?.BrainDrainIntensity ?? 50;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainIntensity = value;
            OnPropertyChanged();
            Save();

            if (_overlayService?.IsRunning == true)
            {
                _overlayService.RefreshOverlays();
            }
        }
    }

    public bool BrainDrainHighRefresh
    {
        get => _settingsService?.Current?.BrainDrainHighRefresh ?? false;
        set
        {
            if (_settingsService?.Current == null) return;
            _settingsService.Current.BrainDrainHighRefresh = value;
            OnPropertyChanged();
            Save();
            _logger?.LogInformation("Brain Drain High Refresh toggled: {Enabled}", value);

            if (_overlayService?.IsRunning == true && BrainDrainEnabled)
            {
                _overlayService.Stop();
                _overlayService.Start();
            }
        }
    }

    [RelayCommand]
    private void ToggleBrainDrainEnabled()
    {
        BrainDrainEnabled = !BrainDrainEnabled;
    }

    [RelayCommand]
    private async Task OpenBrainDrainDetailsAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("feature_brain_drain"),
            Loc.Get("msg_level_feature_settings_on_dashboard")) ?? Task.CompletedTask);
    }

    #endregion

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save settings"); }
    }
}
