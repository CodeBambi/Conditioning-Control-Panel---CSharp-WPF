using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Windows;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Catalogue;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.Presets and MainWindow.SessionIO partials.
/// Manages preset selection/load/save/delete plus custom session import/export/create/edit/delete.
/// </summary>
public partial class PresetsTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly ISessionManager? _sessionManager;
    private readonly ISessionService? _sessionService;
    private readonly IDialogService? _dialogService;
    private readonly IAppEnvironment? _appEnvironment;
    private readonly ILogger<PresetsTabViewModel>? _logger;
    private readonly ICatalogueService? _catalogueService;

    public PresetsTabViewModel() : base("presets", "Presets", "\ud83d\udccb")
    {
        CustomSessions = new ObservableCollection<Session>();
        Sessions = new ObservableCollection<Session>
        {
            Session.MorningDrift,
            Session.GamerGirl,
            Session.DistantDoll
        };
        Presets = new ObservableCollection<Preset>(Preset.GetDefaultPresets());
        SelectedPreset = Presets.FirstOrDefault();
    }

    public PresetsTabViewModel(
        ISettingsService settingsService,
        ISessionManager sessionManager,
        ISessionService sessionService,
        IDialogService dialogService,
        IAppEnvironment appEnvironment,
        ILogger<PresetsTabViewModel> logger,
        ICatalogueService catalogueService) : base("presets", "Presets", "\ud83d\udccb")
    {
        _settingsService = settingsService;
        _sessionManager = sessionManager;
        _sessionService = sessionService;
        _dialogService = dialogService;
        _appEnvironment = appEnvironment;
        _logger = logger;
        _catalogueService = catalogueService;
        CustomSessions = new ObservableCollection<Session>();
        Presets = new ObservableCollection<Preset>();
        Sessions = new ObservableCollection<Session>();
        LoadCustomSessions();
        RefreshPresetsList();
    }

    #region Sessions

    [ObservableProperty]
    private ObservableCollection<Session> _customSessions;

    [ObservableProperty]
    private ObservableCollection<Session> _sessions;

    [ObservableProperty]
    private Session? _selectedSession;

    partial void OnSelectedSessionChanged(Session? value)
    {
        if (value != null)
        {
            SelectedPreset = null;
            _logger?.LogInformation("Session selected: {Name}", value.Name);
        }

        UpdateSessionDetail(value);
        NotifyCornerGifCommandsChanged();
    }

    private void LoadCustomSessions()
    {
        if (_sessionManager == null) return;
        _sessionManager.LoadAllSessions();
        CustomSessions.Clear();
        foreach (var session in _sessionManager.CustomSessions)
        {
            CustomSessions.Add(session);
        }
        Sessions = _sessionManager.AllSessions;
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
    private void SelectSession(Session? session)
    {
        SelectedSession = session;
    }

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session == null || _sessionService == null) return;

        if (_sessionService.State != SessionState.Idle)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_engine_is_running_stop_and_exit"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        try
        {
            _logger?.LogInformation("Starting session: {Name}", session.Name);
            await _sessionService.StartSessionAsync(session);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start session");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.GetF("msg_failed_to_create_session_0", ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    private bool CanStartSession(Session? session)
    {
        return (session ?? SelectedSession) != null && _sessionService != null;
    }

    [RelayCommand]
    private async Task ImportSessionAsync(string filePath)
    {
        if (_sessionManager == null) return;
        if (string.IsNullOrEmpty(filePath) || !filePath.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
        {
            ShowDropZoneStatus(Loc.Get("msg_only_session_json_files_allowed"), isError: true);
            return;
        }

        try
        {
            var result = _sessionManager.ImportSession(filePath);
            if (result.success && result.session != null)
            {
                ShowDropZoneStatus(Loc.GetF("msg_session_imported_0", result.session.Name), isError: false);
                _logger?.LogInformation("Session imported: {Name}", result.session.Name);
            }
            else
            {
                ShowDropZoneStatus(Loc.GetF("msg_failed_to_import_session_0", result.message), isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Session import failed");
            ShowDropZoneStatus(Loc.GetF("msg_import_failed_0", ex.Message), isError: true);
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ExportSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session == null || _sessionManager == null) return;

        var path = await (_dialogService?.ShowSaveFileDialogAsync(
            Loc.Get("title_export_session"),
            new[] { new FileFilter("Session files", new[] { "session.json" }) },
            _sessionManager.GetExportFileName(session)) ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            _sessionManager.ExportSession(session, path);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_complete"),
                Loc.GetF("msg_session_exported_to_0", path)) ?? Task.CompletedTask);
            _logger?.LogInformation("Session exported: {Name} to {Path}", session.Name, path);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export session");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_failed"),
                Loc.GetF("msg_failed_to_export_session_0", ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task CreateSessionAsync()
    {
        _logger?.LogInformation("Create session requested");

        if (_sessionManager == null || _appEnvironment == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("msg_session_manager_not_available"),
                DialogSeverity.Error) ?? Task.CompletedTask);
            return;
        }

        var session = new Session
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Session",
            Icon = "\U0001f3af",
            DurationMinutes = 30,
            Difficulty = SessionDifficulty.Easy,
            BonusXP = Session.GetDifficultyXP(SessionDifficulty.Easy),
            Description = "",
            Source = SessionSource.Custom,
            IsAvailable = true
        };

        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            _logger?.LogWarning("Cannot show session editor: no main window available.");
            return;
        }

        var editor = new SessionEditorWindow(session);
        var saved = await editor.ShowDialog<bool>(mainWindow);

        if (!saved || editor.ResultSession == null)
        {
            _logger?.LogInformation("Create session cancelled");
            return;
        }

        try
        {
            var resultSession = editor.ResultSession;
            var path = await (_dialogService?.ShowSaveFileDialogAsync(
                Loc.Get("title_save_new_session"),
                new[] { new FileFilter("Session files", new[] { "session.json" }) },
                _sessionManager.GetExportFileName(resultSession)) ?? Task.FromResult<string?>(null));

            if (string.IsNullOrEmpty(path))
            {
                _logger?.LogInformation("Create session save cancelled");
                return;
            }

            _sessionManager.AddNewSession(resultSession, path);
            LoadCustomSessions();
            SelectedSession = resultSession;

            _logger?.LogInformation("Created session: {Name} ({Id})", resultSession.Name, resultSession.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create session");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_create_session_failed"),
                Loc.GetF("msg_failed_to_create_session_0", ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task EditSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session == null || _sessionManager == null) return;

        _logger?.LogInformation("Edit session requested: {Name}", session.Name);

        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            _logger?.LogWarning("Cannot show session editor: no main window available.");
            return;
        }

        var editor = new SessionEditorWindow(session);
        var saved = await editor.ShowDialog<bool>(mainWindow);

        if (!saved || editor.ResultSession == null)
        {
            _logger?.LogInformation("Edit session cancelled for {Name}", session.Name);
            return;
        }

        try
        {
            var resultSession = editor.ResultSession;

            if (session.Source == SessionSource.BuiltIn)
            {
                // Editing a built-in session creates a new custom session
                resultSession.Id = Guid.NewGuid().ToString();

                var path = await (_dialogService?.ShowSaveFileDialogAsync(
                    Loc.Get("title_save_as_new_custom_session"),
                    new[] { new FileFilter("Session files", new[] { "session.json" }) },
                    _sessionManager.GetExportFileName(resultSession)) ?? Task.FromResult<string?>(null));

                if (string.IsNullOrEmpty(path))
                {
                    _logger?.LogInformation("Edit built-in session save cancelled for {Name}", session.Name);
                    return;
                }

                _sessionManager.AddNewSession(resultSession, path);
            }
            else
            {
                // Preserve original ID, source, and file path to save over existing file
                resultSession.Id = session.Id;
                resultSession.Source = session.Source;
                resultSession.SourceFilePath = session.SourceFilePath;
                _sessionManager.UpdateCustomSession(resultSession);
            }

            LoadCustomSessions();
            var updatedId = resultSession.Id;
            SelectedSession = Sessions.FirstOrDefault(s => s.Id == updatedId) ?? CustomSessions.FirstOrDefault(s => s.Id == updatedId);

            _logger?.LogInformation("Updated session: {Name} ({Id})", resultSession.Name, resultSession.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update session");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_update_session_failed"),
                Loc.GetF("msg_failed_to_update_session_0", ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task DeleteSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session == null || _sessionManager == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_delete_session"),
            Loc.GetF("msg_delete_session_confirm_0", session.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        try
        {
            _sessionManager.DeleteSession(session);
            CustomSessions.Remove(session);
            if (SelectedSession == session) SelectedSession = null;
            ShowDropZoneStatus(Loc.GetF("msg_session_deleted_0", session.Name), isError: false);
            _logger?.LogInformation("Session deleted: {Name}", session.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete session");
        }
    }

    [RelayCommand]
    private void HandleSessionDrop(string[]? files)
    {
        if (files == null || files.Length != 1) return;
        _ = ImportSessionAsync(files[0]);
    }

    #endregion

    #region Session Details

    [ObservableProperty]
    private bool _revealSpoilers;

    public bool IsPresetDetailVisible => SelectedPreset != null;
    public bool IsSessionDetailVisible => SelectedSession != null;

    public string DetailTitle => SelectedPreset?.Name
        ?? SelectedSession?.LocalizedName
        ?? Loc.Get("label_select_a_preset");

    public string DetailSubtitle => SelectedPreset?.Description
        ?? SelectedSession?.GetModeAwareDescription()
        ?? Loc.Get("label_click_on_a_preset_or_session_to_see_details");

    [ObservableProperty]
    private string _sessionDurationText = "";

    [ObservableProperty]
    private string _sessionRewardText = "";

    [ObservableProperty]
    private string _sessionDifficultyText = "";

    [ObservableProperty]
    private string _sessionDescription = "";

    [ObservableProperty]
    private bool _sessionHasCornerGif;

    [ObservableProperty]
    private string _cornerGifDescription = "";

    [ObservableProperty]
    private string _sessionSpoilerFlash = "";

    [ObservableProperty]
    private string _sessionSpoilerSubliminal = "";

    [ObservableProperty]
    private string _sessionSpoilerAudio = "";

    [ObservableProperty]
    private string _sessionSpoilerOverlays = "";

    [ObservableProperty]
    private string _sessionSpoilerExtras = "";

    [ObservableProperty]
    private string _sessionSpoilerTimeline = "";

    public bool IsCornerGifSettingsVisible => CornerGifEnabled && SessionHasCornerGif;

    public string CornerGifSizeText => Loc.GetF("label_0_px", CornerGifSize);
    public string CornerGifOpacityText => $"{CornerGifOpacity}%";

    public bool CornerGifEnabled
    {
        get => SelectedSession?.Settings.CornerGifEnabled ?? false;
        set
        {
            if (SelectedSession != null)
            {
                SelectedSession.Settings.CornerGifEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCornerGifSettingsVisible));
            }
        }
    }

    public int CornerGifSize
    {
        get => SelectedSession?.Settings.CornerGifSize ?? 300;
        set
        {
            if (SelectedSession != null)
            {
                SelectedSession.Settings.CornerGifSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CornerGifSizeText));
            }
        }
    }

    public int CornerGifOpacity
    {
        get => SelectedSession?.Settings.CornerGifOpacity ?? 20;
        set
        {
            if (SelectedSession != null)
            {
                SelectedSession.Settings.CornerGifOpacity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CornerGifOpacityText));
            }
        }
    }

    public CornerPosition CornerGifPosition
    {
        get => SelectedSession?.Settings.CornerGifPosition ?? CornerPosition.BottomLeft;
        set
        {
            if (SelectedSession != null)
            {
                SelectedSession.Settings.CornerGifPosition = value;
                OnPropertyChanged();
            }
        }
    }

    private void UpdateSessionDetail(Session? session)
    {
        OnPropertyChanged(nameof(IsPresetDetailVisible));
        OnPropertyChanged(nameof(IsSessionDetailVisible));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));

        RevealSpoilers = false;

        if (session == null)
        {
            SessionDurationText = "";
            SessionRewardText = "";
            SessionDifficultyText = "";
            SessionDescription = "";
            SessionHasCornerGif = false;
            CornerGifDescription = "";
            SessionSpoilerFlash = "";
            SessionSpoilerSubliminal = "";
            SessionSpoilerAudio = "";
            SessionSpoilerOverlays = "";
            SessionSpoilerExtras = "";
            SessionSpoilerTimeline = "";
        }
        else
        {
            SessionDurationText = Loc.GetF("label_0_minutes", session.DurationMinutes);
            SessionRewardText = Loc.GetF("label_0_xp_3", session.BonusXP);
            SessionDifficultyText = session.DifficultyText;
            SessionDescription = session.GetModeAwareDescription();
            SessionHasCornerGif = session.HasCornerGifOption;
            CornerGifDescription = session.LocalizedCornerGifDescription;
            SessionSpoilerFlash = session.GetSpoilerFlash();
            SessionSpoilerSubliminal = session.GetSpoilerSubliminal();
            SessionSpoilerAudio = session.GetSpoilerAudio();
            SessionSpoilerOverlays = session.GetSpoilerOverlays();
            SessionSpoilerExtras = session.GetSpoilerInteractive();
            SessionSpoilerTimeline = session.GetSpoilerTimeline();
        }

        OnPropertyChanged(nameof(CornerGifEnabled));
        OnPropertyChanged(nameof(CornerGifSize));
        OnPropertyChanged(nameof(CornerGifOpacity));
        OnPropertyChanged(nameof(CornerGifPosition));
        OnPropertyChanged(nameof(IsCornerGifSettingsVisible));
        OnPropertyChanged(nameof(CornerGifSizeText));
        OnPropertyChanged(nameof(CornerGifOpacityText));
    }

    [RelayCommand]
    private void ToggleRevealSpoilers()
    {
        RevealSpoilers = true;
    }

    [RelayCommand(CanExecute = nameof(CanSelectCornerGif))]
    private async Task SelectCornerGifAsync()
    {
        if (SelectedSession == null) return;

        var files = await (_dialogService?.ShowOpenFileDialogAsync(
            Loc.Get("btn_select_gif"),
            new[] { new FileFilter("GIF files", new[] { "gif" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));

        if (files == null || files.Count == 0) return;

        SelectedSession.Settings.CornerGifPath = files[0];
        _logger?.LogInformation("Corner GIF selected: {Path}", files[0]);
    }

    private bool CanSelectCornerGif()
    {
        return SelectedSession?.HasCornerGifOption == true;
    }

    private void NotifyCornerGifCommandsChanged()
    {
        SelectCornerGifCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Presets

    [ObservableProperty]
    private ObservableCollection<Preset> _presets;

    [ObservableProperty]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _presetDetailTitle = "";

    [ObservableProperty]
    private string _presetDetailSubtitle = "";

    [ObservableProperty]
    private string _presetDetailFlash = "";

    [ObservableProperty]
    private string _presetDetailVideo = "";

    [ObservableProperty]
    private string _presetDetailSubliminal = "";

    [ObservableProperty]
    private string _presetDetailAudio = "";

    [ObservableProperty]
    private string _presetDetailOverlays = "";

    [ObservableProperty]
    private string _presetDetailAdvanced = "";

    [ObservableProperty]
    private bool _canLoadPreset;

    [ObservableProperty]
    private bool _canSaveOverPreset;

    [ObservableProperty]
    private bool _canDeletePreset;

    [ObservableProperty]
    private bool _canExportPreset;

    [ObservableProperty]
    private bool _canSharePreset;

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value != null)
        {
            SelectedSession = null;
        }

        foreach (var preset in Presets)
        {
            preset.IsSelected = ReferenceEquals(preset, value);
        }

        UpdatePresetDetail(value);
    }

    private void RefreshPresetsList()
    {
        Presets.Clear();
        foreach (var preset in Preset.GetDefaultPresets())
        {
            Presets.Add(preset);
        }

        var userPresets = _settingsService?.Current?.UserPresets;
        if (userPresets != null)
        {
            foreach (var preset in userPresets)
            {
                Presets.Add(preset);
            }
        }

        var currentName = _settingsService?.Current?.CurrentPresetName;
        SelectedPreset = Presets.FirstOrDefault(p => p.Name == currentName);
    }

    private void UpdatePresetDetail(Preset? preset)
    {
        CanLoadPreset = preset != null;
        CanSaveOverPreset = preset != null && !preset.IsDefault;
        CanDeletePreset = preset != null && !preset.IsDefault;
        CanExportPreset = preset != null;
        CanSharePreset = preset != null && !preset.IsDefault;

        OnPropertyChanged(nameof(IsPresetDetailVisible));
        OnPropertyChanged(nameof(IsSessionDetailVisible));
        OnPropertyChanged(nameof(DetailTitle));
        OnPropertyChanged(nameof(DetailSubtitle));

        if (preset == null)
        {
            PresetDetailTitle = "";
            PresetDetailSubtitle = "";
            PresetDetailFlash = "";
            PresetDetailVideo = "";
            PresetDetailSubliminal = "";
            PresetDetailAudio = "";
            PresetDetailOverlays = "";
            PresetDetailAdvanced = "";
            return;
        }

        PresetDetailTitle = preset.Name;
        PresetDetailSubtitle = preset.Description;

        PresetDetailFlash = preset.FlashEnabled
            ? $"Enabled | {preset.FlashFrequency}/hr | \u00d7{preset.SimultaneousImages} | Opacity: {preset.FlashOpacity}%"
            : "Disabled";

        PresetDetailVideo = preset.MandatoryVideosEnabled
            ? $"Enabled | {preset.VideosPerHour}/hr | Strict: {(preset.StrictLockEnabled ? "Yes" : "No")}"
            : "Disabled";

        PresetDetailSubliminal = preset.SubliminalEnabled
            ? $"Enabled | {preset.SubliminalFrequency}/min | Opacity: {preset.SubliminalOpacity}%"
            : "Disabled";

        PresetDetailAudio = $"Whispers: {(preset.SubAudioEnabled ? $"Yes ({preset.SubAudioVolume}%)" : "No")} | Master: {preset.MasterVolume}%";
        PresetDetailOverlays = $"Spiral: {(preset.SpiralEnabled ? "Yes" : "No")} | Pink: {(preset.PinkFilterEnabled ? "Yes" : "No")}";
        PresetDetailAdvanced = $"Bubbles: {(preset.BubblesEnabled ? "Yes" : "No")} | Lock Card: {(preset.LockCardEnabled ? "Yes" : "No")}";
    }

    [RelayCommand]
    private void SelectPreset(Preset? preset)
    {
        SelectedPreset = preset;
    }

    [RelayCommand]
    private async Task LoadPresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null || _settingsService?.Current == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_load_preset"),
            Loc.GetF("msg_load_preset_confirm_0", preset.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        preset.ApplyTo(_settingsService.Current);
        _settingsService.Save();
        _logger?.LogInformation("Loaded preset: {Name}", preset.Name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_preset_loaded"),
            Loc.GetF("msg_preset_0_loaded", preset.Name)) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task SaveNewPresetAsync()
    {
        if (_settingsService?.Current == null) return;

        var name = $"My Preset {DateTime.Now:yyyy-MM-dd HH-mm}";
        if (_settingsService.Current.UserPresets.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_name_taken"),
                Loc.Get("msg_a_preset_with_this_name_already_exists")) ?? Task.CompletedTask);
            return;
        }

        var preset = Preset.FromSettings(_settingsService.Current, name, "Custom preset created by user");
        _settingsService.Current.UserPresets.Add(preset);
        _settingsService.Current.CurrentPresetName = name;
        _settingsService.Save();

        RefreshPresetsList();
        SelectedPreset = preset;

        _logger?.LogInformation("Created new preset: {Name}", name);
        await (_dialogService?.ShowMessageAsync(
            Loc.Get("title_preset_saved"),
            Loc.GetF("msg_preset_0_saved", name)) ?? Task.CompletedTask);
    }

    [RelayCommand]
    private async Task SaveOverPresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null || preset.IsDefault || _settingsService?.Current == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_overwrite_preset"),
            Loc.GetF("msg_overwrite_preset_confirm_0", preset.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        var updated = Preset.FromSettings(_settingsService.Current, preset.Name, preset.Description);
        updated.Id = preset.Id;
        updated.CreatedAt = preset.CreatedAt;

        var index = _settingsService.Current.UserPresets.FindIndex(p => p.Id == preset.Id);
        if (index >= 0)
        {
            _settingsService.Current.UserPresets[index] = updated;
            _settingsService.Save();
            RefreshPresetsList();
            SelectedPreset = updated;
            _logger?.LogInformation("Updated preset: {Name}", updated.Name);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_preset_updated"),
                Loc.GetF("msg_preset_0_updated", updated.Name)) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null || preset.IsDefault || _settingsService?.Current == null) return;

        var confirm = await (_dialogService?.ShowConfirmationAsync(
            Loc.Get("title_delete_preset"),
            Loc.GetF("msg_delete_preset_confirm_0", preset.Name)) ?? Task.FromResult(false));
        if (!confirm) return;

        _settingsService.Current.UserPresets.RemoveAll(p => p.Id == preset.Id);
        _settingsService.Save();
        SelectedPreset = null;
        RefreshPresetsList();
        _logger?.LogInformation("Deleted preset: {Name}", preset.Name);
    }

    [RelayCommand]
    private async Task ExportPresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null) return;

        var path = await (_dialogService?.ShowSaveFileDialogAsync(
            Loc.Get("title_export_preset"),
            new[] { new FileFilter("Preset files", new[] { "preset.json" }) },
            $"{preset.Name}.preset.json") ?? Task.FromResult<string?>(null));

        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(preset, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_complete"),
                Loc.GetF("msg_preset_exported_to_0", path)) ?? Task.CompletedTask);
            _logger?.LogInformation("Preset exported: {Name} to {Path}", preset.Name, path);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export preset");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_export_failed"),
                Loc.GetF("msg_failed_to_export_preset_0", ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task SharePresetAsync(Preset? preset)
    {
        preset ??= SelectedPreset;
        if (preset == null || preset.IsDefault) return;
        if (string.IsNullOrEmpty(_settingsService?.Current?.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        JToken asset;
        try
        {
            asset = JToken.FromObject(preset);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Catalogue] Preset serialize failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
            return;
        }

        var mainWindow = GetMainWindow();
        bool confirmed;
        if (mainWindow != null)
        {
            var dialog = new AssetSubmitDialog(preset.Name, _settingsService.Current.UserDisplayName);
            confirmed = await dialog.ShowDialog<bool>(mainWindow);
            if (confirmed)
            {
                try
                {
                    var result = await (_catalogueService?.SubmitCatalogueAssetAsync(
                        CatalogueSubmissionsTabViewModel.CatalogueKindPresets,
                        asset,
                        "ccp-preset/v1",
                        dialog.Creator,
                        dialog.Tags,
                        default) ?? Task.FromResult<SubmissionResult>(new SubmissionResult.UnknownError(0, "service_unavailable")));
                    RecordCatalogueSubmission(CatalogueSubmissionsTabViewModel.CatalogueKindPresets, preset.Id, result);
                    await ShowCatalogueSubmissionResultAsync(result);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Catalogue] Preset share threw unexpectedly");
                    await (_dialogService?.ShowMessageAsync(
                        Loc.Get("title_error"),
                        Loc.Get("catalogue_toast_unknown_error"),
                        DialogSeverity.Error) ?? Task.CompletedTask);
                }
            }
        }
        else
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ShareSessionAsync(Session? session)
    {
        session ??= SelectedSession;
        if (session == null) return;
        if (string.IsNullOrEmpty(_settingsService?.Current?.AuthToken))
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("catalogue_toast_auth_failed"),
                Loc.Get("msg_catalogue_auth_required"),
                DialogSeverity.Warning) ?? Task.CompletedTask);
            return;
        }

        // The served download must be the pristine .session.json. Prefer the
        // on-disk file (custom sessions are file-backed); otherwise serialize.
        JToken asset;
        string key;
        try
        {
            if (!string.IsNullOrEmpty(session.SourceFilePath) && File.Exists(session.SourceFilePath))
            {
                asset = JToken.Parse(File.ReadAllText(session.SourceFilePath));
                key = session.SourceFilePath;
            }
            else if (_sessionManager != null)
            {
                var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".session.json");
                _sessionManager.ExportSession(session, tmp);
                asset = JToken.Parse(File.ReadAllText(tmp));
                try { File.Delete(tmp); } catch { /* best effort cleanup */ }
                key = session.Id;
            }
            else
            {
                asset = JToken.FromObject(session);
                key = session.Id;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Catalogue] Session serialize failed");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
            return;
        }

        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
            return;
        }

        var dialog = new AssetSubmitDialog(session.Name, _settingsService.Current.UserDisplayName);
        var confirmed = await dialog.ShowDialog<bool>(mainWindow);
        if (!confirmed) return;

        try
        {
            var result = await (_catalogueService?.SubmitCatalogueAssetAsync(
                CatalogueSubmissionsTabViewModel.CatalogueKindSessions,
                asset,
                "ccp-session/v1",
                dialog.Creator,
                dialog.Tags,
                default) ?? Task.FromResult<SubmissionResult>(new SubmissionResult.UnknownError(0, "service_unavailable")));
            RecordCatalogueSubmission(CatalogueSubmissionsTabViewModel.CatalogueKindSessions, key, result);
            await ShowCatalogueSubmissionResultAsync(result);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Catalogue] Session share threw unexpectedly");
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    private void RecordCatalogueSubmission(string kind, string key, SubmissionResult result)
    {
        try
        {
            string id;
            string status;
            switch (result)
            {
                case SubmissionResult.Success s:
                    id = s.Id;
                    status = string.IsNullOrEmpty(s.Status) ? "pending" : s.Status;
                    break;
                case SubmissionResult.Duplicate d:
                    id = d.ExistingId;
                    status = string.IsNullOrEmpty(d.ExistingStatus) ? "pending" : d.ExistingStatus;
                    break;
                default:
                    return;
            }

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(key)) return;
            var dict = kind == CatalogueSubmissionsTabViewModel.CatalogueKindSessions
                ? _settingsService?.Current?.CatalogueSessionSubmissions
                : _settingsService?.Current?.CataloguePresetSubmissions;
            if (dict == null) return;

            dict.TryGetValue(key, out var existing);
            var rec = existing ?? new DeeperSubmissionRecord { SubmittedUtc = DateTime.UtcNow };
            rec.CatalogueId = id;
            rec.Status = status;
            rec.LastCheckedUtc = DateTime.UtcNow;
            if (IsCatalogueAcceptedStatus(status)) rec.AcceptedNotified = true;

            dict[key] = rec;
            _settingsService?.Save();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("[Catalogue] RecordCatalogueSubmission failed: {Error}", ex.Message);
        }
    }

    private static bool IsCatalogueAcceptedStatus(string? status) =>
        string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "published", StringComparison.OrdinalIgnoreCase);

    private async Task ShowCatalogueSubmissionResultAsync(SubmissionResult result)
    {
        switch (result)
        {
            case SubmissionResult.Success:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_success"),
                    Loc.Get("catalogue_toast_success"),
                    DialogSeverity.Info) ?? Task.CompletedTask);
                break;

            case SubmissionResult.Duplicate d:
            {
                var key = d.ExistingStatus switch
                {
                    "approved" => "catalogue_toast_duplicate_approved",
                    "rejected" => "catalogue_toast_duplicate_rejected",
                    _ => "catalogue_toast_duplicate_pending"
                };
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_info"),
                    Loc.Get(key),
                    DialogSeverity.Info) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.ValidationError v:
            {
                var key = v.ErrorCode switch
                {
                    "missing_title" => "catalogue_toast_error_missing_title",
                    "missing_creator" => "catalogue_toast_error_missing_creator",
                    "invalid_media_source" => "catalogue_toast_error_invalid_media_source",
                    "invalid_schema" => "catalogue_toast_error_invalid_schema",
                    "file_too_large" => "catalogue_toast_error_file_too_large",
                    "stale_guidelines_version" => "catalogue_toast_error_stale_guidelines",
                    _ => ""
                };
                var msg = !string.IsNullOrEmpty(key)
                    ? Loc.Get(key)
                    : Loc.GetF("catalogue_toast_error_generic_fmt", v.ErrorCode);
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_warning"),
                    msg,
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.AuthFailed:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("catalogue_toast_auth_failed"),
                    Loc.Get("msg_catalogue_auth_required"),
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;

            case SubmissionResult.TooLarge:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_error"),
                    Loc.Get("catalogue_toast_too_large"),
                    DialogSeverity.Error) ?? Task.CompletedTask);
                break;

            case SubmissionResult.RateLimited r:
            {
                string msg;
                if (r.RetryAfterSeconds.HasValue && r.RetryAfterSeconds.Value > 0)
                {
                    var minutes = Math.Max(1, (int)Math.Ceiling(r.RetryAfterSeconds.Value / 60.0));
                    msg = Loc.GetF("catalogue_toast_rate_limited_minutes_fmt", minutes);
                }
                else
                {
                    msg = Loc.Get("catalogue_toast_rate_limited_unknown");
                }
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_warning"),
                    msg,
                    DialogSeverity.Warning) ?? Task.CompletedTask);
                break;
            }

            case SubmissionResult.UnknownError:
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_error"),
                    Loc.Get("catalogue_toast_unknown_error"),
                    DialogSeverity.Error) ?? Task.CompletedTask);
                break;
        }
    }

    #endregion

    #region Drag / Drop Status

    [ObservableProperty]
    private string _dropZoneStatus = "";

    [ObservableProperty]
    private bool _isDropZoneError;

    [ObservableProperty]
    private bool _isDropZoneActive;

    [RelayCommand]
    private void SetDropZoneActive(bool active)
    {
        IsDropZoneActive = active;
    }

    internal void ShowDropZoneStatus(string message, bool isError)
    {
        DropZoneStatus = message;
        IsDropZoneError = isError;
        IsDropZoneActive = true;
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            // Bound properties must be mutated on the UI thread (L5-23: was throwing "invalid thread").
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                DropZoneStatus = "";
                IsDropZoneError = false;
                IsDropZoneActive = false;
            });
        });
    }

    #endregion
}
