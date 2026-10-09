// PORTED from ConditioningControlPanel/MainWindow/MainWindow.PresetIO.cs (367 lines).
//
// Export and import are live over Core's Services.PresetFileService, in PresetsTabView
// (BtnExportPreset_Click with a StorageProvider save picker, HandlePresetDrop called from
// Window_Drop in MainShellWindow.axaml.cs for a single *.preset.json). The phrase pair lives in
// Views/Controls/AppSettings/DataSettingsSection.axaml.cs.
//
// Share to catalogue (WPF PresetIO.cs:190-287): SharePresetToCatalogueAsync / ShareSessionToCatalogueAsync
// open AssetSubmitDialog (creator + tags + affirmation), submit through Core App.Catalogue, record the
// id/status and toast the outcome. The share status badges are in MainShellWindow.CatalogueStatus.cs.

using System;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private const string CatalogueSchemaPreset = "ccp-preset/v1";
        private const string CatalogueSchemaSession = "ccp-session/v1";

        /// <summary>WPF MainWindow.PresetIO.cs:198.</summary>
        internal async Task SharePresetToCatalogueAsync(Preset preset)
        {
            if (!CatalogueSignedIn()) return;
            JToken asset;
            try { asset = JToken.Parse(new PresetFileService().SerializePreset(preset)); }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Preset serialize failed");
                App.Notifications.Show(Loc.Get("catalogue_toast_unknown_error"), NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }
            await ShareAssetAsync(CatalogueKindPresets, CatalogueSchemaPreset, preset.Id, preset.Name, asset);
        }

        /// <summary>WPF MainWindow.PresetIO.cs:239: the served download is the pristine .session.json.</summary>
        internal async Task ShareSessionToCatalogueAsync(Session session)
        {
            if (!CatalogueSignedIn()) return;
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
                    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".session.json");
                    new SessionFileService().ExportSession(session, tmp);
                    asset = JToken.Parse(File.ReadAllText(tmp));
                    try { File.Delete(tmp); } catch { }
                    key = session.Id;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] Session serialize failed");
                App.Notifications.Show(Loc.Get("catalogue_toast_unknown_error"), NotificationType.Error, TimeSpan.FromSeconds(8));
                return;
            }
            await ShareAssetAsync(CatalogueKindSessions, CatalogueSchemaSession, key, session.Name, asset);
        }

        private static bool CatalogueSignedIn()
        {
            if (!string.IsNullOrEmpty(CoreSettings.Current.AuthToken)) return true;
            App.Notifications.Show(Loc.Get("catalogue_toast_auth_failed"), NotificationType.Warning, TimeSpan.FromSeconds(8));
            return false;
        }

        // The affirmation modal gates the POST: nothing is sent unless the user ticks it and names a creator.
        private async Task ShareAssetAsync(string kind, string schema, string key, string name, JToken asset)
        {
            var dialog = new AssetSubmitDialog(name, CoreAccount.DisplayName);
            if (!await dialog.ShowDialogSafe<bool>(this) || !dialog.Confirmed) return;

            SubmissionResult result;
            try
            {
                result = await App.Catalogue.SubmitCatalogueAssetAsync(kind, asset, schema,
                    dialog.Creator, dialog.Tags, default).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Catalogue] {Kind} share threw unexpectedly", kind);
                result = new SubmissionResult.UnknownError(0, ex.Message);
            }

            RecordCatalogueSubmission(kind, key, result);
            RefreshCatalogueShareBadges(kind);
            ShowCatalogueSubmissionResultToast(result);
        }
    }
}
