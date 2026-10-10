// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.CloudBackup.cs (285 lines).
// Not "waiting for a service to move to Core" - two structural blockers:
//
// 1. THE BUTTONS LEFT THIS WINDOW. WPF carried the Settings door inline in MainWindow.xaml, so
//    these handlers hung off the shell. The port moved all four WITH their markup to
//    CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml(.cs), where
//    BtnBackupSettingsNow_Click / BtnRestoreSettings_Click / BtnExportData_Click /
//    BtnPrivacyPolicy_Click already exist as that control's own stubs. Restoring them here would
//    be a second copy nothing routes to. Cloud backup gets wired in THAT file.
//
// 2. NOT WIRED BY DECISION (docs/avalonia-decisions.md, 2026-10-09, rows-cloud-remember). The head now
//    has an identity (CCP.Avalonia/Platform/AccountSeed.cs, SecretStore, V2AuthService), but every body
//    needs the backup half of ConditioningControlPanel/Services/Settings/ProfileSyncService.cs
//    (BackupSettingsAsync, GetSettingsBackupInfoAsync, RestoreSettingsFromCloudAsync, ExportDataAsync,
//    ExcludedBackupProperties, SettingsBackupBudget, PreserveLocalOnlyFields). Wiring it would upload
//    Linux settings and restore Windows backups here (and the reverse) with no path translation for
//    CustomAssetsPath / the asset lists, against a server contract still pending owner confirmation.
//    Needs an oracle-deep design first.
//
// Members, so nothing disappears silently:
//   internal void ReloadSettingsUiAfterRestore()
//       Re-seeds the Settings door from the swapped AppSettings. Needs LoadSettings() and its
//       _isLoading guard, which here are spread across Views/Controls/AppSettings/*Section.axaml.cs.
//       No caller either - only the manual restore and App.CheckCloudSettingsRestoreAsync.
//   internal async void BtnBackupSettingsNow_Click(…) / BtnRestoreSettings_Click(…)
//       ProfileSyncService, per (2). The restore path's LOCAL-WINS field list (progression,
//       entitlement windows, OpenRouterApiKey) is settings-model logic that would port unchanged -
//       there is just no restore to run it after.
//   internal async void BtnExportData_Click(…)
//       ExportDataAsync, then Microsoft.Win32.SaveFileDialog. Avalonia's twin is
//       StorageProvider.SaveFilePickerAsync on this window; the export it would save is what's missing.
//   internal void BtnPrivacyPolicy_Click(…)
//       Process.Start(UseShellExecute) on the privacy-policy URL. No browser launcher on this head
//       (WPF's is ConditioningControlPanel/Helpers/BrowserLauncher.cs, whose no-default-browser
//       prompt is its whole point). Cheap once one exists - in AccountSettingsSection, per (1).
//   private async Task UpdateBackupStatus()
//       Writes TxtCloudBackupStatus from GetSettingsBackupInfoAsync. Same blocker; that TextBlock
//       is in AccountSettingsSection.axaml too.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing here on purpose - every member of the WPF twin belongs to
        // CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml.cs on this head.
    }
}
