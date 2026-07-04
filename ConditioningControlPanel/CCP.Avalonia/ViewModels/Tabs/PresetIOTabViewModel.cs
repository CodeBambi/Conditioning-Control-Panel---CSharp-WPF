using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Catalogue;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.ViewModels.Tabs;

/// <summary>
/// Avalonia port of the WPF MainWindow.PresetIO partial.
/// Handles preset export, drag-drop import, and catalogue sharing.
/// </summary>
public partial class PresetIOTabViewModel : TabItemViewModel
{
    private readonly ISettingsService? _settingsService;
    private readonly IDialogService? _dialogService;
    private readonly ILogger<PresetIOTabViewModel>? _logger;
    private readonly ICatalogueService? _catalogueService;

    public PresetIOTabViewModel() : base("presetio", "Preset IO", "🗂️")
    {
        Presets = new ObservableCollection<Preset>();
    }

    public PresetIOTabViewModel(
        ISettingsService settingsService,
        IDialogService dialogService,
        ILogger<PresetIOTabViewModel> logger,
        ICatalogueService catalogueService) : base("presetio", "Preset IO", "🗂️")
    {
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _catalogueService = catalogueService;
        Presets = new ObservableCollection<Preset>();
        RefreshPresetsList();
    }

    [ObservableProperty]
    private ObservableCollection<Preset> _presets;

    [ObservableProperty]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _dropZoneStatus = "";

    [ObservableProperty]
    private bool _isDropZoneError;

    [ObservableProperty]
    private bool _isDropZoneActive;

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value != null)
        {
            _logger?.LogInformation("Preset selected for IO: {Name}", value.Name);
        }
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
            var json = JsonConvert.SerializeObject(preset, Formatting.Indented);
            await File.WriteAllTextAsync(path, json);

            _logger?.LogInformation("Preset exported: {Name} to {Path}", preset.Name, path);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_success"),
                string.Format(Loc.Get("msg_preset_exported_to_0"), path),
                DialogSeverity.Info) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export preset: {Name}", preset.Name);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                string.Format(Loc.Get("msg_failed_to_export_preset_0"), ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    [RelayCommand]
    private async Task ImportPresetAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var files = await (_dialogService?.ShowOpenFileDialogAsync(
                "Import Preset",
                new[] { new FileFilter("Preset files", new[] { "preset.json" }) }) ?? Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
            filePath = files.FirstOrDefault();
            if (string.IsNullOrEmpty(filePath)) return;
        }
        else if (!filePath.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
        {
            ShowDropZoneStatus(Loc.Get("msg_only_session_json_files_allowed"), isError: true);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var imported = JsonConvert.DeserializeObject<Preset>(json);
            if (imported == null)
            {
                await (_dialogService?.ShowMessageAsync(
                    Loc.Get("title_import_error"),
                    "Failed to import preset. The file may be invalid.",
                    DialogSeverity.Error) ?? Task.CompletedTask);
                return;
            }

            imported.Id = Guid.NewGuid().ToString();
            imported.IsDefault = false;

            if (_settingsService?.Current is { } settings)
            {
                imported.Name = MakeUniquePresetName(settings.UserPresets, imported.Name);
                settings.UserPresets.Add(imported);
                _settingsService.Save();
            }

            RefreshPresetsList();
            SelectedPreset = imported;

            _logger?.LogInformation("Preset imported: {Name} from {Path}", imported.Name, filePath);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_complete"),
                string.Format(Loc.Get("msg_imported_session"), imported.Name),
                DialogSeverity.Info) ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import preset from {Path}", filePath);
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_import_error"),
                string.Format(Loc.Get("msg_import_failed_0"), ex.Message),
                DialogSeverity.Error) ?? Task.CompletedTask);
        }
    }

    private static string MakeUniquePresetName(System.Collections.Generic.List<Preset> userPresets, string name)
    {
        if (!userPresets.Any(p => p.Name == name)) return name;

        var importedName = $"{name} (Imported)";
        if (!userPresets.Any(p => p.Name == importedName)) return importedName;

        int i = 1;
        while (userPresets.Any(p => p.Name == $"{importedName} {i}"))
        {
            i++;
        }
        return $"{importedName} {i}";
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
        if (mainWindow == null)
        {
            await (_dialogService?.ShowMessageAsync(
                Loc.Get("title_error"),
                Loc.Get("catalogue_toast_unknown_error"),
                DialogSeverity.Error) ?? Task.CompletedTask);
            return;
        }

        var dialog = new AssetSubmitDialog(preset.Name, _settingsService.Current.UserDisplayName);
        var confirmed = await dialog.ShowDialog<bool>(mainWindow);
        if (!confirmed) return;

        try
        {
            var result = await (_catalogueService?.SubmitCatalogueAssetAsync(
                CatalogueSubmissionsTabViewModel.CatalogueKindPresets,
                asset,
                "ccp-preset/v1",
                dialog.Creator,
                dialog.Tags,
                default) ?? Task.FromResult<SubmissionResult>(new SubmissionResult.UnknownError(0, "service_unavailable")));
            RecordCatalogueSubmission(preset.Id, result);
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

    private void RecordCatalogueSubmission(string key, SubmissionResult result)
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
            var dict = _settingsService?.Current?.CataloguePresetSubmissions;
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

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    [RelayCommand]
    private void HandlePresetDrop(string[]? files)
    {
        if (files == null || files.Length != 1) return;
        _ = ImportPresetAsync(files[0]);
    }

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
}
