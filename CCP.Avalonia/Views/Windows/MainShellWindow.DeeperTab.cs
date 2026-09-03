// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs (1241 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (53):
//   internal void BtnDeeper_Click(…)
//   private void UpdateDeeperWelcomeCardVisibility(…)
//   private void DismissDeeperWelcomeCard(…)
//   internal void BtnDeeperWelcomeTour_Click(…)
//   internal void BtnDeeperWelcomeDemo_Click(…)
//   internal void BtnDeeperWelcomeDismiss_Click(…)
//   internal void BtnDeeperTutorial_Click(…)
//   private void OpenDeeperBundledDemo(…)
//   private void StartDeeperTabTutorial(…)
//   internal void ChkEnableDeeper_Changed(…)
//   private bool _deeperPulseRunning
//   private void StartDeeperTabPulse(…)
//   private void StopDeeperTabPulse(…)
//   internal void BtnDeeperNewEnhancement_Click(…)
//   internal void BtnDeeperOpenPlayer_Click(…)
//   private void OnDeeperBrowserBound(…)
//   private void OnDeeperBrowserUnbound(…)
//   private bool _browserWebcamStateSubscribed
//   private Action<WebcamTrackingState>? _onBrowserWebcamStateChanged
//   private string? _browserWebcamPromptShownForUrl
//   private void EnsureBrowserWebcamStateSubscribed(…)
//   private void RefreshBrowserWebcamButton(…)
//   internal async void BtnWebcamTracking_Click(…)
//   private static bool BrowserEnhancementNeedsWebcam(…)
//   private void MaybePromptBrowserWebcamForEnhancement(…)
//   private bool _mandatoryVideoEnhanceNudgeShown
//   private async void MaybePromptMandatoryVideoEnhancement(…)
//   internal void ToggleEnhanceIfPossible_Changed(…)
//   internal void ChkForceShowBambiCloud_Changed(…)
//   private void OnBrowserEnhanceMatchChanged(…)
//   private void OpenDeeperEditor(…)
//   public void OpenInDeeperPlayer(…)
//   public void OpenDeeperEditorFromPlayer(…)
//   public void OpenDeeperEnhancementInPlayer(…)
//   public void OpenInDeeperEditorForMedia(…)
//   public void HandlePendingFileOpen(…)
//   private void OnDeeperLibraryChanged(…)
//   private void RefreshDeeperLibraryUI(…)
//   private void OpenDeeperFile(…)
//   internal void BtnDeeperOpenLibraryFolder_Click(…)
//   internal void BtnDeeperImport_Click(…)
//   private static bool IsImportableEnhancementPath(…)
//   private void ImportEnhancementFiles(…)
//   private void DeleteDeeperLibraryEntry(…)
//   private void TriggerCatalogueLookupForNavigation(…)
//   private async System.Threading.Tasks.Task RunCatalogueLookupAsync(…)
//   private void ShowCatalogueLookupToast(…)
//   private void OpenCataloguePickerDialog(…)
//   private async System.Threading.Tasks.Task DownloadAndOpenCatalogueEntryAsync(…)
//   private void SwitchToDeeperLibraryTab(…)
//   private static bool IsCatalogueEligible(…)
//   private async Task SubmitDeeperLibraryEntryAsync(…)
//   private void ShowCatalogueSubmissionResultToast(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.DeeperTab.cs; wired when they move to Core.
        private void BtnDeeper_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

    }
}
