// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs (1575 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (77):
//   internal async void ChkRemoteControlEnabled_Changed(…)
//   private string GetSelectedRemoteTier(…)
//   private bool ShowRemoteControlWaiver(…)
//   internal async void CmbRemoteTier_SelectionChanged(…)
//   internal void BtnCopyRemoteCode_Click(…)
//   internal void BtnCopyRemoteLink_Click(…)
//   internal async void BtnStopRemote_Click(…)
//   internal void ChkStopEffectsOnRemoteDisconnect_Changed(…)
//   internal async void ChkRemoteShareAvatar_Changed(…)
//   private Models.EmotePreset? _editingPreset
//   internal async void BtnEmotePreset_Click(…)
//   internal async void BtnEmoteCustomSend_Click(…)
//   internal async void TxtEmoteCustom_KeyDown(…)
//   private async void BtnEmotePresetBig_Click(…)
//   private async void BtnEmoteCustomSendBig_Click(…)
//   private async void TxtEmoteCustomBig_KeyDown(…)
//   private async Task SendCustomEmoteAsync(…)
//   internal async Task<bool> SendEmoteAndReportAsync(…)
//   internal void BtnEmoteEdit_Click(…)
//   internal void TxtEditEmoteText_TextChanged(…)
//   internal void BtnEditEmoteSave_Click(…)
//   internal void BtnEditEmoteCancel_Click(…)
//   private bool _availableSubjectsBound
//   internal void BtnAvailableSubjects_Click(…)
//   internal void BtnBecomeASubject_Click(…)
//   private void RefreshBecomeASubjectCta(…)
//   private void EnsureAvailableSubjectsBound(…)
//   private void OnAvailableSubjectsServicePropertyChanged(…)
//   internal void AvailableSubjectsScroller_PreviewMouseWheel(…)
//   private void UpdateAvailableSubjectsEmptyAndError(…)
//   internal async void BtnConnectSubject_Click(…)
//   private const int OptInMaxTags
//   private System.Windows.Controls.CheckBox[] OptInTagCheckBoxes(…)
//   internal void ChkOptIntoDirectory_Changed(…)
//   private void PopulateOptInFormFromSavedSettings(…)
//   internal void TxtOptInStatus_TextChanged(…)
//   private void UpdateOptInStatusCharCount(…)
//   internal void ChkOptInTag_Click(…)
//   private List<string> GetSelectedDirectoryTags(…)
//   private System.Windows.Threading.DispatcherTimer? _optInFeedbackTimer
//   private void ShowOptInFeedback(…)
//   private async Task RunOptInChainAsync(…)
//   private bool _directoryOptedIn
//   private void UpdateDirectoryListingStatus(…)
//   private async Task StopRemoteControl(…)
//   private void OnRemoteControllerChanged(…)
//   private void OnRemoteControllerIdleChanged(…)
//   private void OnRemoteSessionEnded(…)
//   private void UpdateRemoteStatus(…)
//   private void ShowRemoteControlOverlay(…)
//   private void HideRemoteControlOverlay(…)
//   private void WireRemoteSessionCallbacks(…)
//   private void StartRemoteSessionInfoTimer(…)
//   private void UpdateRemoteSessionInfo(…)
//   private void OnRemoteCommandReceived(…)
//   private void AppendRemoteCommandLog(…)
//   private void UpdateRemoteControlUI(…)
//   private string BuildRemotePairingUrl(…)
//   private void RefreshRemoteQrCode(…)
//   private void RefreshTierCardHighlight(…)
//   internal void TierCard_Click(…)
//   private void ShowCommandNotification(…)
//   private void HideCommandNotification(…)
//   private async void BtnEndRemoteSession_Click(…)
//   private Models.Session? _remoteStartedSession
//   internal bool IsSessionRemoteStarted
//   internal async void StartSessionFromRemote(…)
//   internal void PauseSessionFromRemote(…)
//   internal void ResumeSessionFromRemote(…)
//   internal void StopSessionFromRemote(…)
//   internal void StopEngineAndSession(…)
//   internal void TriggerPanicFromRemote(…)
//   internal void MinimizeToTrayForRemote(…)
//   public void MinimizeToTrayForChaos(…)
//   private void NotifyRemoteControllerJoined(…)
//   internal void RestoreFromTrayForRemote(…)
//   public void ShowFromTray(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.RemoteControl.cs; wired when they move to Core.
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.RemoteControl.cs; wired when they move to Core.
        private void BtnEmoteCustomSendBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.RemoteControl.cs; wired when they move to Core.
        private void BtnEmotePresetBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.RemoteControl.cs; wired when they move to Core.
        private void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        // ponytail: needs the services in MainWindow.RemoteControl.cs; wired when they move to Core.
        private void TxtEmoteCustomBig_KeyDown(object? sender, global::Avalonia.Input.KeyEventArgs e) { }

    }
}
