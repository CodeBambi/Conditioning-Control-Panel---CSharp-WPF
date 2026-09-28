// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.PresetIO.cs (367 lines).
//
// Export and import are live over Core's Services.PresetFileService, in PresetsTabView
// (BtnExportPreset_Click with a StorageProvider save picker, HandlePresetDrop called from
// Window_Drop in MainShellWindow.axaml.cs for a single *.preset.json). The phrase pair lives in
// Views/Controls/AppSettings/DataSettingsSection.axaml.cs.
//
// Still head-side: BtnSharePreset_Click / SharePresetToCatalogueAsync / ShareSessionToCatalogueAsync
// (App.Catalogue.SubmitCatalogueAssetAsync, App.UserDisplayName, MainShellWindow.CatalogueSubmissions.cs)
// and the share status badges that read GetCatalogueRecord from the same partial.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
