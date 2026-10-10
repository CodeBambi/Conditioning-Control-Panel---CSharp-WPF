// WPF twin: ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs.
//
// The four buttons left this window with their markup: they live on
// CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml, so their handlers do too.
//   BtnBackupSettingsNow_Click / BtnRestoreSettings_Click / UpdateBackupStatus
//       -> AccountSettingsSection.CloudBackup.cs, over Core's CloudSettingsBackup
//          (CCP.Core/Services/Settings/CloudSettingsBackup.cs: what a backup leaves out, what this
//          machine keeps on a restore, the safety floor a restore can not go under).
//   BtnExportData_Click / BtnPrivacyPolicy_Click
//       -> AccountSettingsSection.Providers.cs / AccountSettingsSection.axaml.cs.
//   ReloadSettingsUiAfterRestore
//       -> not needed: SettingsService.RestoreFrom raises CurrentReplaced and every page that shows
//          settings re-seeds from it.
//
// Cross-OS note: CustomAssetsPath never rides a backup and this machine's value is kept on a restore;
// the per-file asset lists are relative paths, and an empty one in a restore keeps this machine's own.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose - see above.
    }
}
