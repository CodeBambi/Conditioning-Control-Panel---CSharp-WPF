using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Preset export + drag-drop import, plus the shared catalogue-share glue
    // (status badges, share handlers) for both Presets and Sessions. The web
    // catalogue serves pristine .preset.json / .session.json files; presets get a
    // brand-new export + import path here, sessions reuse the existing importer.
    public partial class MainWindow
    {
        private const string CatalogueSchemaPreset = "ccp-preset/v1";
        private const string CatalogueSchemaSession = "ccp-session/v1";

        private Services.PresetFileService? _presetFileService;

        // ---- Preset export -------------------------------------------------

        internal void BtnExportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null) return;
            _presetFileService ??= new Services.PresetFileService();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.Get("title_export_preset"),
                Filter = "Preset files (*.preset.json)|*.preset.json",
                FileName = Services.PresetFileService.GetExportFileName(_selectedPreset),
                DefaultExt = ".preset.json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _presetFileService.ExportPreset(_selectedPreset, dialog.FileName);
                ShowStyledDialog(Loc.Get("title_export_complete"),
                    Loc.GetF("msg_preset_exported_to_0", dialog.FileName), "OK", "");
                App.Logger?.Information("Preset exported: {Name} to {Path}", _selectedPreset.Name, dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowStyledDialog(Loc.Get("title_export_failed"),
                    Loc.GetF("msg_failed_to_export_preset_0", ex.Message), "OK", "");
                App.Logger?.Error(ex, "Failed to export preset");
            }
        }

        // ---- Phrase backup export / import ---------------------------------

        private Services.PhraseBackupService? _phraseBackupService;

        internal void BtnExportPhrases_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;
            _phraseBackupService ??= new Services.PhraseBackupService();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Phrases",
                Filter = "Phrase backup (*.ccpphrases.json)|*.ccpphrases.json",
                FileName = Services.PhraseBackupService.GetExportFileName(),
                DefaultExt = ".ccpphrases.json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var count = _phraseBackupService.Export(settings, dialog.FileName);
                ShowStyledDialog("Phrases Exported",
                    $"Saved {count} phrase(s) to:\n{dialog.FileName}", "OK", "");
                App.Logger?.Information("Phrases exported: {Count} to {Path}", count, dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowStyledDialog("Export Failed",
                    $"Could not export phrases:\n{ex.Message}", "OK", "");
                App.Logger?.Error(ex, "Failed to export phrases");
            }
        }

        internal void BtnImportPhrases_Click(object sender, RoutedEventArgs e)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;
            _phraseBackupService ??= new Services.PhraseBackupService();

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Phrases",
                Filter = "Phrase backup (*.ccpphrases.json)|*.ccpphrases.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            if (!_phraseBackupService.Validate(dialog.FileName, out var error))
            {
                ShowStyledDialog("Import Failed",
                    $"That file isn't a valid phrase backup:\n{error}", "OK", "");
                return;
            }

            // Importing replaces the current phrase pools — confirm first.
            var confirm = ShowStyledDialog("Import Phrases?",
                "This replaces your current lock-card phrases, subliminals, mantras and other " +
                "custom text with the ones in the backup file. Continue?", "Import", "Cancel");
            if (!confirm) return;

            try
            {
                var count = _phraseBackupService.Import(settings, dialog.FileName);
                App.Settings?.Save();
                ShowStyledDialog("Phrases Imported",
                    $"Restored {count} phrase(s). You may need to reopen any open phrase editors to see them.",
                    "OK", "");
                App.Logger?.Information("Phrases imported: {Count} from {Path}", count, dialog.FileName);
            }
            catch (Exception ex)
            {
                ShowStyledDialog("Import Failed",
                    $"Could not import phrases:\n{ex.Message}", "OK", "");
                App.Logger?.Error(ex, "Failed to import phrases");
            }
        }

        // ---- Preset drag-drop import ---------------------------------------

        private void HandlePresetDrop(string filePath)
        {
            _presetFileService ??= new Services.PresetFileService();

            if (!_presetFileService.ValidatePresetFile(filePath, out var errorMessage))
            {
                ShowDropZoneStatus($"Invalid: {errorMessage}", isError: true);
                return;
            }

            var preset = _presetFileService.ImportPreset(filePath);
            if (preset == null)
            {
                ShowDropZoneStatus("Failed to read preset", isError: true);
                return;
            }

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // De-dup ids against built-ins + existing user presets so an imported
            // preset never collides with one already present (mirrors sessions).
            var takenIds = Models.Preset.GetDefaultPresets().Select(p => p.Id)
                .Concat(settings.UserPresets.Select(p => p.Id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(preset.Id) || takenIds.Contains(preset.Id))
            {
                preset.Id = Guid.NewGuid().ToString();
            }

            settings.UserPresets.Add(preset);
            try { _presetFileService.CopyToCustomPresets(filePath, preset); }
            catch (Exception ex) { App.Logger?.Debug("[Preset] CopyToCustomPresets failed: {Error}", ex.Message); }
            App.Settings?.Save();

            // Rebuild the carousel + dropdown so the new preset shows immediately.
            RefreshPresetsList();
            RefreshPresetsDropdown();

            ShowDropZoneStatus($"Preset imported: {preset.Name}", isError: false);
            App.Logger?.Information("Preset imported via drag-drop: {Name}", preset.Name);
        }

        // ---- Share to catalogue --------------------------------------------

        internal async void BtnSharePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPreset == null || _selectedPreset.IsDefault) return;
            await SharePresetToCatalogueAsync(_selectedPreset);
        }

        private async Task SharePresetToCatalogueAsync(Models.Preset preset)
        {
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken) || App.Catalogue == null)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            _presetFileService ??= new Services.PresetFileService();

            JToken asset;
            try { asset = JToken.Parse(_presetFileService.SerializePreset(preset)); }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Preset serialize failed");
                App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            var dialog = new AssetSubmitDialog(preset.Name, App.UserDisplayName) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(
                    CatalogueKindPresets, asset, CatalogueSchemaPreset,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Preset share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(CatalogueKindPresets, preset.Id, result);
            ShowCatalogueSubmissionResultToast(result);
        }

        private async Task ShareSessionToCatalogueAsync(Models.Session session)
        {
            if (string.IsNullOrEmpty(App.Settings?.Current?.AuthToken) || App.Catalogue == null)
            {
                App.Notifications?.Show(Loc.Get("catalogue_toast_auth_failed"),
                    Services.NotificationType.Warning, TimeSpan.FromSeconds(8));
                return;
            }

            _sessionFileService ??= new Services.SessionFileService();

            // The served download must be the pristine .session.json. Prefer the
            // on-disk file (custom sessions are file-backed); otherwise serialize.
            JToken asset;
            string key;
            try
            {
                if (!string.IsNullOrEmpty(session.SourceFilePath) && File.Exists(session.SourceFilePath))
                {
                    asset = JToken.Parse(File.ReadAllText(session.SourceFilePath));
                    key = CanonicalCataloguePathKey(session.SourceFilePath);
                }
                else
                {
                    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".session.json");
                    _sessionFileService.ExportSession(session, tmp);
                    asset = JToken.Parse(File.ReadAllText(tmp));
                    try { File.Delete(tmp); } catch { }
                    key = session.Id;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Session serialize failed");
                App.Notifications?.Show(Loc.Get("catalogue_toast_unknown_error"),
                    Services.NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }

            var dialog = new AssetSubmitDialog(session.Name, App.UserDisplayName) { Owner = this };
            if (dialog.ShowDialog() != true || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(
                    CatalogueKindSessions, asset, CatalogueSchemaSession,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Catalogue] Session share threw unexpectedly");
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(CatalogueKindSessions, key, result);
            ShowCatalogueSubmissionResultToast(result);
        }

        // ---- Status badges -------------------------------------------------

        // Builds a small colored status pill for a catalogue submission, or null
        // when the asset hasn't been shared. Same palette/glyphs as the Deeper
        // library badge.
        private Border? CreateCatalogueStatusBadge(DeeperSubmissionRecord? rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.CatalogueId)) return null;

            string glyph, label, bg, fg;
            if (IsCatalogueAcceptedStatus(rec.Status))
            {
                glyph = "✅"; label = Loc.Get("catalogue_status_approved"); bg = "#334CAF50"; fg = "#7BE08A";
            }
            else if (string.Equals(rec.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                glyph = "⚠"; label = Loc.Get("catalogue_status_rejected"); bg = "#33FF6B6B"; fg = "#FF9B9B";
            }
            else
            {
                glyph = "⏳"; label = Loc.Get("catalogue_status_pending"); bg = "#33FFB347"; fg = "#FFC97A";
            }

            var badge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = $"{glyph} {label}",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)),
                FontSize = 10,
                FontWeight = FontWeights.Bold
            };
            return badge;
        }

        private void UpdatePresetShareStatusBadge(Models.Preset preset)
        {
            if (PresetsTab.PresetShareStatusHost == null) return;
            PresetsTab.PresetShareStatusHost.Children.Clear();
            var rec = GetCatalogueRecord(CatalogueKindPresets, preset.Id);
            var badge = CreateCatalogueStatusBadge(rec);
            if (badge != null)
            {
                badge.Margin = new Thickness(0);
                PresetsTab.PresetShareStatusHost.Children.Add(badge);
            }
        }

        // Refresh share badges after a submission resolves / a status poll updates.
        private void RefreshCatalogueShareBadges(string kind)
        {
            try
            {
                if (kind == CatalogueKindPresets)
                {
                    if (_selectedPreset != null) UpdatePresetShareStatusBadge(_selectedPreset);
                }
                else if (kind == CatalogueKindSessions)
                {
                    RefreshCustomSessionCards();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Catalogue] RefreshCatalogueShareBadges failed: {Error}", ex.Message);
            }
        }
    }
}
