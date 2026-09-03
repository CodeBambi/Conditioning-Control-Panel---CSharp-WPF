// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.PresetIO.cs (367 lines) - and for
// its two phrase members, NOT NEEDED: they already ship on this head somewhere better.
//
// THE STALE NOTE THIS FILE USED TO CARRY. BtnExportPhrases_Click and BtnImportPhrases_Click are
// live in Views/Controls/AppSettings/DataSettingsSection.axaml.cs, over the same
// Services.PhraseBackupService (now in Core, CCP.Core/Services/PhraseBackupService.cs), with WPF's
// Microsoft.Win32 Save/OpenFileDialog replaced by TopLevel.StorageProvider and the destructive
// import still confirm-gated. Their axaml Click= handlers name that section, not this window, so
// re-adding them here would be a second copy competing for the same buttons. Deleted deliberately.
//
// The rest of the file is the catalogue share path, and it is head-side. What each member needs:
//   BtnExportPreset_Click     - Services.PresetFileService
//                               (ConditioningControlPanel/Services/PresetFileService.cs) is still in
//                               the head, and the _selectedPreset it exports lives in
//                               MainShellWindow.Presets.cs, another stub this layer does not own.
//                               The save picker itself maps to StorageProvider.SaveFilePickerAsync,
//                               exactly as the phrase export above already does, and the two result
//                               dialogs map to Views/Dialogs/MessageDialog - so once
//                               PresetFileService crosses, this member is a ten-minute port.
//   HandlePresetDrop          - the same service's import half, plus the window drag-and-drop
//                               handlers in MainShellWindow.axaml.cs, which are stubs.
//   BtnSharePreset_Click      - App.Catalogue.SubmitCatalogueAssetAsync
//   SharePresetToCatalogueAsync (ConditioningControlPanel/Services/CatalogueService.cs) plus
//   ShareSessionToCatalogueAsync App.UserDisplayName, and RecordCatalogueSubmission /
//                               ShowCatalogueSubmissionResultToast from
//                               MainShellWindow.CatalogueSubmissions.cs (a stub, not owned here).
//                               The AssetSubmitDialog both open IS ported
//                               (Views/Dialogs/AssetSubmitDialog).
//   CreateCatalogueStatusBadge- builds a coloured pill from a Models.DeeperSubmissionRecord, which
//                               IS in Core (CCP.Core/Models/DeeperSubmissionRecord.cs), so the
//                               builder itself would compile here as a Border + TextBlock. It is not
//                               restored because its status test (IsCatalogueAcceptedStatus) lives
//                               in MainShellWindow.CatalogueSubmissions.cs and its two callers -
//                               UpdatePresetShareStatusBadge and ModManagerDialog - have neither a
//                               record to read nor a host to draw into yet. A pill nothing can hand
//                               a record to is scaffolding, not a port.
//   UpdatePresetShareStatusBadge - GetCatalogueRecord, same unowned partial, and PresetsTabView's
//   RefreshCatalogueShareBadges    PresetShareStatusHost / the session rack repaint
//                                  (MainShellWindow.SessionIO.cs).
//   CatalogueSchemaPreset     - "ccp-preset/v1" and "ccp-session/v1". Constants with no reader.
//   CatalogueSchemaSession

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Deliberately empty - see the header. No member of this partial is referenced from
        // MainShellWindow.axaml.
    }
}
