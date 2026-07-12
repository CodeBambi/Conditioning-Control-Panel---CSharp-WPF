using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Awareness;
using ConditioningControlPanel.Core.Services.Settings;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.KeywordTriggers partial.
/// Exposes keyword trigger list management, screen-OCR toggles, cooldown sliders,
/// and import commands. Toggle changes now start/stop the live <see cref="IKeywordTriggerService"/>
/// and <see cref="IScreenOcrService"/> implementations registered in DI.
/// </summary>
public partial class AwarenessTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<AwarenessTabViewModel>? _logger;
    private readonly ILogger<KeywordTriggerViewModel>? _keywordTriggerLogger;
    private readonly IKeywordTriggerService? _keywordService;
    private readonly IScreenOcrService? _ocrService;
    private readonly IAwarenessService? _awarenessService;

    public AwarenessTabViewModel() : base("awareness", "Awareness", "👁")
    {
        Triggers = new ObservableCollection<KeywordTriggerViewModel>();
        VisualEffectOptions = new ObservableCollection<string>(Enum.GetNames(typeof(KeywordVisualEffect)));
        OcrConfirmationOptions = new ObservableCollection<string> { "1 scan", "2 scans", "3 scans" };
        OcrHighlightModeOptions = new ObservableCollection<string> { "All matches", "First match only" };
        IsPremiumLocked = false;
        StatusText = Loc.Get("tab_awareness_status_off");
        StatusColor = "#606060";
        HighlightColorHex = "#FF69B4";
    }

    public AwarenessTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<AwarenessTabViewModel> logger,
        ILogger<KeywordTriggerViewModel> keywordTriggerLogger,
        IKeywordTriggerService keywordService,
        IScreenOcrService? ocrService = null,
        IAwarenessService? awarenessService = null) : base("awareness", "Awareness", "👁")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _keywordTriggerLogger = keywordTriggerLogger;
        _keywordService = keywordService;
        _ocrService = ocrService;
        _awarenessService = awarenessService;
        Triggers = new ObservableCollection<KeywordTriggerViewModel>();
        VisualEffectOptions = new ObservableCollection<string>(Enum.GetNames(typeof(KeywordVisualEffect)));
        OcrConfirmationOptions = new ObservableCollection<string> { "1 scan", "2 scans", "3 scans" };
        OcrHighlightModeOptions = new ObservableCollection<string> { "All matches", "First match only" };
        LoadFromSettings();

        var settings = _settingsService.Current;
        IsPremiumLocked = settings == null || !(settings.HasLinkedPatreon || settings.HasLinkedDiscord);
        if (settings != null)
        {
            settings.PropertyChanged += OnSettingsPropertyChanged;
        }

        UpdateStatus();
    }

    [ObservableProperty]
    private bool _keywordTriggersEnabled;

    [ObservableProperty]
    private bool _screenOcrEnabled;

    [ObservableProperty]
    private int _bufferTimeoutMs = 2000;

    [ObservableProperty]
    private string _bufferTimeoutText = "2.0s";

    [ObservableProperty]
    private int _globalCooldownSeconds = 30;

    [ObservableProperty]
    private string _globalCooldownText = "30s";

    [ObservableProperty]
    private int _sameWordCooldownSeconds = 30;

    [ObservableProperty]
    private string _sameWordCooldownText = "30s";

    [ObservableProperty]
    private double _sessionMultiplier = 1.0;

    [ObservableProperty]
    private string _sessionMultiplierText = "1.0x";

    [ObservableProperty]
    private int _screenOcrIntervalSeconds = 5;

    [ObservableProperty]
    private string _screenOcrIntervalText = "5s";

    [ObservableProperty]
    private bool _keywordHighlightEnabled;

    [ObservableProperty]
    private double _keywordHighlightDurationSeconds = 2.0;

    [ObservableProperty]
    private string _keywordHighlightDurationText = "2.0s";

    [ObservableProperty]
    private int _selectedOcrHighlightModeIndex;

    [ObservableProperty]
    private int _selectedOcrConfirmationIndex;

    [ObservableProperty]
    private bool _highlightVisibleInCapture;

    [ObservableProperty]
    private bool _isAwarenessMasterEnabled;

    [ObservableProperty]
    private bool _ignoreOwnUi;

    [ObservableProperty]
    private bool _loopProtectionEnabled;

    [ObservableProperty]
    private string _highlightColorHex = "#FF69B4";

    [ObservableProperty]
    private bool _isPremiumLocked;

    [ObservableProperty]
    private string _statusText = Loc.Get("tab_awareness_status_off");

    [ObservableProperty]
    private string _statusColor = "#606060";

    [ObservableProperty]
    private ObservableCollection<KeywordTriggerViewModel> _triggers;

    [ObservableProperty]
    private ObservableCollection<string> _visualEffectOptions;

    [ObservableProperty]
    private ObservableCollection<string> _ocrConfirmationOptions;

    [ObservableProperty]
    private ObservableCollection<string> _ocrHighlightModeOptions;

    partial void OnBufferTimeoutMsChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordBufferTimeoutMs = value;
        BufferTimeoutText = $"{value / 1000.0:F1}s";
        Save();
    }

    partial void OnGlobalCooldownSecondsChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordGlobalCooldownSeconds = value;
        GlobalCooldownText = $"{value}s";
        Save();
    }

    partial void OnSameWordCooldownSecondsChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordPerKeywordCooldownSeconds = value;
        SameWordCooldownText = $"{value}s";
        Save();
    }

    partial void OnSessionMultiplierChanged(double value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordSessionMultiplier = value;
        SessionMultiplierText = $"{value:F1}x";
        Save();
    }

    partial void OnScreenOcrIntervalSecondsChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ScreenOcrIntervalMs = value * 1000;
        ScreenOcrIntervalText = $"{value}s";
        Save();
    }

    partial void OnKeywordHighlightEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordHighlightEnabled = value;
        Save();
    }

    partial void OnKeywordHighlightDurationSecondsChanged(double value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordHighlightDurationMs = (int)(value * 1000);
        KeywordHighlightDurationText = $"{value:0.0}s";
        Save();
    }

    partial void OnSelectedOcrHighlightModeIndexChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.OcrHighlightAll = value == 0;
        Save();
    }

    partial void OnSelectedOcrConfirmationIndexChanged(int value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.OcrConfirmationScans = value + 1;
        Save();
    }

    partial void OnHighlightVisibleInCaptureChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.OcrHighlightVisibleInCapture = value;
        Save();
        UpdateStatus();
    }

    partial void OnIsAwarenessMasterEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AwarenessModeEnabled = value;
        // WPF MainWindow.Patreon.cs:1142-1143: enabling via the UI auto-grants consent in the
        // SAME handler (plain bool, no dialog, no consent version). The App-level settings
        // subscription starts/stops the window-awareness engine off these two flags.
        _settingsService.Current.AwarenessConsentGiven = value;
        Save();
        UpdateKeywordServiceState();
        UpdateOcrServiceState();
        UpdateStatus();
        _logger?.LogInformation("Awareness master switch toggled: {Enabled}", value);
    }

    partial void OnIgnoreOwnUiChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AwarenessIgnoreOwnUi = value;
        Save();
    }

    partial void OnLoopProtectionEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.AwarenessLoopProtectionEnabled = value;
        Save();
    }

    partial void OnHighlightColorHexChanged(string value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordHighlightColor = value;
        Save();
    }

    partial void OnKeywordTriggersEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.KeywordTriggersEnabled = value;
        Save();
        _logger?.LogInformation("Awareness keyboard capture toggled: {Enabled}", value);
        UpdateKeywordServiceState();
        UpdateStatus();
    }

    partial void OnScreenOcrEnabledChanged(bool value)
    {
        if (_settingsService?.Current == null) return;
        _settingsService.Current.ScreenOcrEnabled = value;
        Save();
        _logger?.LogInformation("Awareness screen OCR toggled: {Enabled}", value);
        UpdateOcrServiceState();
        UpdateStatus();
    }

    private void UpdateKeywordServiceState()
    {
        if (_keywordService == null) return;
        if (!IsPremiumLocked && IsAwarenessMasterEnabled && KeywordTriggersEnabled)
        {
            try { _keywordService.Start(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to start keyword trigger service"); }
        }
        else
        {
            try { _keywordService.Stop(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to stop keyword trigger service"); }
        }
    }

    private void UpdateOcrServiceState()
    {
        if (_ocrService == null) return;
        if (!IsPremiumLocked && IsAwarenessMasterEnabled && ScreenOcrEnabled)
        {
            try
            {
                _ocrService.UpdateInterval(_settingsService?.Current?.ScreenOcrIntervalMs ?? 5000);
                _ocrService.Start();
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to start screen OCR service"); }
        }
        else
        {
            try { _ocrService.Stop(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "Failed to stop screen OCR service"); }
        }
    }

    private void UpdateStatus()
    {
        // AI-1: the chip reflects the REAL window-awareness engine state (IsRunning) rather
        // than the raw toggle mix - on heads without a foreground-title provider the engine
        // stays off and the chip truthfully shows off. Keyword/OCR liveness still lights it
        // for premium users running those surfaces with the engine unavailable.
        var engineLive = _awarenessService?.IsRunning == true;
        if (engineLive || (IsAwarenessMasterEnabled && (KeywordTriggersEnabled || ScreenOcrEnabled)))
        {
            StatusText = Loc.Get("tab_awareness_status_on");
            StatusColor = "#00E676";
        }
        else
        {
            StatusText = Loc.Get("tab_awareness_status_off");
            StatusColor = "#606060";
        }
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

    [RelayCommand]
    private void SetHighlightColor(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color))
            HighlightColorHex = color;
    }

    [RelayCommand]
    private async Task OpenAdvancedAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("tab_awareness_advanced_title"),
            Loc.Get("tab_awareness_advanced_message"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task OpenTutorialAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("tab_awareness_tutorial_title"),
            Loc.Get("tab_awareness_tutorial_message"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("dialog_title_premium_locked"),
            Loc.Get("dialog_message_awareness_premium_locked"),
            DialogSeverity.Info) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task ToggleKeywordTriggersAsync()
    {
        if (IsPremiumLocked)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_patreon_feature"),
                Loc.Get("msg_keyword_triggers_patreon_only")) ?? Task.CompletedTask);
            return;
        }
        KeywordTriggersEnabled = !KeywordTriggersEnabled;
    }

    [RelayCommand]
    private void AddTrigger()
    {
        var trigger = new KeywordTrigger
        {
            Keyword = "",
            MatchType = KeywordMatchType.PlainText,
            Enabled = true,
            CooldownSeconds = 30,
            AudioVolume = 80,
            VisualEffect = KeywordVisualEffect.SubliminalFlash,
            HapticEnabled = true,
            HapticIntensity = 0.5,
            DuckAudio = true,
            XPAward = 10
        };
        _settingsService?.Current?.KeywordTriggers.Add(trigger);
        Triggers.Add(new KeywordTriggerViewModel(trigger, Save, _keywordTriggerLogger));
        Save();
        _logger?.LogInformation("Added keyword trigger");
    }

    [RelayCommand]
    private async Task ImportFromCustomTriggersAsync()
    {
        if (_keywordService == null)
        {
            _logger?.LogInformation("Import from custom triggers requested but no service is available");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_complete"),
                Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge")) ?? Task.CompletedTask);
            return;
        }

        try
        {
            var imported = _keywordService.ImportFromCustomTriggers();
            if (imported.Count == 0)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_import_complete"),
                    Loc.Get("msg_no_new_triggers_to_import_all_existing_trigge")) ?? Task.CompletedTask);
                return;
            }

            var settings = _settingsService?.Current;
            if (settings != null)
            {
                foreach (var trigger in imported)
                {
                    if (settings.KeywordTriggers.Any(t =>
                        string.Equals(t.Keyword, trigger.Keyword, StringComparison.OrdinalIgnoreCase)
                        && t.MatchType == trigger.MatchType)) continue;

                    settings.KeywordTriggers.Add(trigger);
                    Triggers.Add(new KeywordTriggerViewModel(trigger, Save, _keywordTriggerLogger));
                }
                Save();
            }

            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_complete"),
                Loc.GetF("msg_imported_n_triggers", imported.Count)) ?? Task.CompletedTask);
            _logger?.LogInformation("Imported {Count} custom keyword triggers", imported.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to import custom keyword triggers");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_complete"),
                Loc.Get("msg_import_failed")) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private void DeleteTrigger(KeywordTriggerViewModel? vm)
    {
        if (vm == null) return;
        _settingsService?.Current?.KeywordTriggers.Remove(vm.Model);
        Triggers.Remove(vm);
        Save();
        _logger?.LogInformation("Deleted keyword trigger {Id}", vm.Model.Id);
    }

    [RelayCommand]
    private async Task BrowseAudioAsync(KeywordTriggerViewModel? vm)
    {
        if (vm == null) return;
        var paths = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("title_select_trigger_audio"),
            new[] { new FileFilter("Audio Files", new[] { "mp3", "wav", "ogg" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        var path = paths.FirstOrDefault();
        if (string.IsNullOrEmpty(path)) return;
        vm.Model.AudioFilePath = path;
        Save();
        _logger?.LogInformation("Set trigger audio for {Id}: {Path}", vm.Model.Id, Path.GetFileName(path));
    }

    private void LoadFromSettings()
    {
        var s = _settingsService?.Current;
        if (s == null) return;

        KeywordTriggersEnabled = s.KeywordTriggersEnabled;
        ScreenOcrEnabled = s.ScreenOcrEnabled;
        BufferTimeoutMs = s.KeywordBufferTimeoutMs;
        GlobalCooldownSeconds = s.KeywordGlobalCooldownSeconds;
        SameWordCooldownSeconds = s.KeywordPerKeywordCooldownSeconds;
        SessionMultiplier = s.KeywordSessionMultiplier;
        ScreenOcrIntervalSeconds = s.ScreenOcrIntervalMs / 1000;
        KeywordHighlightEnabled = s.KeywordHighlightEnabled;
        KeywordHighlightDurationSeconds = s.KeywordHighlightDurationMs / 1000.0;
        SelectedOcrHighlightModeIndex = s.OcrHighlightAll ? 0 : 1;
        SelectedOcrConfirmationIndex = Math.Clamp(s.OcrConfirmationScans - 1, 0, 2);
        HighlightVisibleInCapture = s.OcrHighlightVisibleInCapture;

        IsAwarenessMasterEnabled = s.AwarenessModeEnabled;
        IgnoreOwnUi = s.AwarenessIgnoreOwnUi;
        LoopProtectionEnabled = s.AwarenessLoopProtectionEnabled;
        HighlightColorHex = s.KeywordHighlightColor;

        Triggers.Clear();
        foreach (var trigger in s.KeywordTriggers)
        {
            if (trigger.Id.StartsWith("preset:", StringComparison.Ordinal)) continue;
            Triggers.Add(new KeywordTriggerViewModel(trigger, Save, _keywordTriggerLogger));
        }
    }

    private void Save()
    {
        try { _settingsService?.Save(); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to save keyword trigger settings"); }
    }
}

/// <summary>
/// Thin wrapper around <see cref="KeywordTrigger"/> so the Avalonia UI can bind
/// to individual trigger fields without replacing the model type.
/// </summary>
public sealed partial class KeywordTriggerViewModel : ObservableObject
{
    private readonly KeywordTrigger _model;
    private readonly Action _save;

    public KeywordTriggerViewModel(KeywordTrigger model, Action save, ILogger<KeywordTriggerViewModel>? logger)
    {
        _model = model;
        _save = save;
}

    public KeywordTrigger Model => _model;

    public string Id => _model.Id;

    public string Keyword
    {
        get => _model.Keyword;
        set
        {
            if (_model.Keyword == value) return;
            _model.Keyword = value;
            OnPropertyChanged();
            _save();
        }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set
        {
            if (_model.Enabled == value) return;
            _model.Enabled = value;
            OnPropertyChanged();
            _save();
        }
    }

    public int VisualEffectIndex
    {
        get => (int)_model.VisualEffect;
        set
        {
            if ((int)_model.VisualEffect == value) return;
            _model.VisualEffect = (KeywordVisualEffect)value;
            OnPropertyChanged();
            _save();
        }
    }

    public int CooldownSeconds
    {
        get => _model.CooldownSeconds;
        set
        {
            if (_model.CooldownSeconds == value) return;
            _model.CooldownSeconds = value;
            OnPropertyChanged();
            _save();
        }
    }

    public int AudioVolume
    {
        get => _model.AudioVolume;
        set
        {
            if (_model.AudioVolume == value) return;
            _model.AudioVolume = value;
            OnPropertyChanged();
            _save();
        }
    }

    public bool HapticEnabled
    {
        get => _model.HapticEnabled;
        set
        {
            if (_model.HapticEnabled == value) return;
            _model.HapticEnabled = value;
            OnPropertyChanged();
            _save();
        }
    }

    public bool DuckAudio
    {
        get => _model.DuckAudio;
        set
        {
            if (_model.DuckAudio == value) return;
            _model.DuckAudio = value;
            OnPropertyChanged();
            _save();
        }
    }

    public string? AudioFilePath => _model.AudioFilePath;
    public string AudioFileName => string.IsNullOrEmpty(_model.AudioFilePath) ? "No audio" : Path.GetFileName(_model.AudioFilePath);
}
