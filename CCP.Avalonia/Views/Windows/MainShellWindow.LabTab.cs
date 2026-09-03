// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (1413 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (79):
//   private bool _webcamDebugSubscribed
//   private int _webcamDebugBlinkCount
//   private int _webcamDebugMouthOpenCount
//   private int _webcamDebugTongueOutCount
//   private GazeSide _webcamDebugLastGaze
//   private bool _webcamDebugLastGazeSet
//   private string _webcamDebugFaceLabel
//   private Action<WebcamTrackingState>? _onDebugStateChanged
//   private Action? _onDebugFaceFound
//   private Action? _onDebugFaceLost
//   private Action? _onDebugBlink
//   private Action? _onDebugMouthOpen
//   private Action? _onDebugTongueOut
//   private Action<GazeSide>? _onDebugGazeSide
//   private Action<WebcamTrackingState>? _onPillStateChanged
//   private EventHandler<bool>? _onMicListeningChanged
//   private EventHandler<bool>? _onWakeListeningChanged
//   private void WireMicActivePill(…)
//   internal void UpdateMicPill(…)
//   private void MicActivePill_Click(…)
//   private void WireWebcamActivePill(…)
//   private const int RapidBlinkRecalCount
//   private const int RapidBlinkRecalWindowMs
//   private readonly Queue<DateTime> _rapidBlinkTimes
//   private Action? _onRapidBlinkRecal
//   private bool _rapidBlinkRecalInProgress
//   private void WireRapidBlinkRecalibrateShortcut(…)
//   private async Task TriggerRapidBlinkRecalibrateAsync(…)
//   private void StopAllForRecalibration(…)
//   private bool _syncingBlinkRecalToggles
//   internal void ChkBlinkRecalShortcut_Changed(…)
//   private void SyncBlinkRecalToggles(…)
//   private void UpdateLabTrackerUi(…)
//   private void UpdateWebcamStatusChips(…)
//   private static string WebcamStateText(…)
//   internal void OpenDeviceSettings(…)
//   internal void RefreshDeviceSettingsLists(…)
//   private string? _quickRecalTooltipBase
//   private void RefreshQuickRecalHotkeyHint(…)
//   private void WebcamActivePill_Click(…)
//   internal async void BtnWebcamDebugStart_Click(…)
//   private async Task<bool> StartWebcamOffUiThreadAsync(…)
//   private WebcamLoadingSplash? _webcamLoadingSplash
//   private Action<double, string>? _onWebcamStartupProgress
//   private Action<WebcamTrackingState>? _onWebcamStartupState
//   private void InstallWebcamLoadingSplash(…)
//   private void EnsureWebcamDebugSubscribed(…)
//   private void UnsubscribeWebcamDebug(…)
//   private void UpdateWebcamDebugCounters(…)
//   internal async void BtnWebcamDebugCalibrate_Click(…)
//   internal void BtnGazeMinigame_Click(…)
//   private bool _focusGazeSyncing
//   private void HookFocusGazeService(…)
//   private void SyncFocusGazeToggle(…)
//   internal async void ChkFocusGaze_Changed(…)
//   private DispatcherTimer? _blinkTrainerTickTimer
//   private void HookBlinkTrainerService(…)
//   private void OnBlinkTrainerServiceStateChanged(…)
//   private void SyncBlinkTrainerCountdownTimer(…)
//   private void BlinkTrainerTick(…)
//   internal void BtnLabBlinkTrainerOpenNew_Click(…)
//   internal async void BtnWebcamDebugTrackerTest_Click(…)
//   internal async void BtnWebcamDebugQuickRecal_Click(…)
//   internal void BtnWebcamReviewPrivacy_Click(…)
//   internal void BtnWebcamRevokeConsent_Click(…)
//   internal void ChkWebcamDebugCursor_Changed(…)
//   internal void ChkWebcamDriftCorrection_Changed(…)
//   internal void ChkRestrictGazeToCalScreen_Changed(…)
//   private bool _webcamDevicePopulating
//   private void PopulateWebcamDeviceCombos(…)
//   private static void PopulateWebcamCombo(…)
//   private void RefreshWebcamDeviceList(…)
//   internal void CmbWebcamDevice_SelectionChanged(…)
//   internal void BtnWebcamDeviceRefresh_Click(…)
//   private bool _webcamMonitorPopulating
//   private void RefreshWebcamMonitorList(…)
//   private static void FillMonitorCombo(…)
//   internal void CmbWebcamMonitor_SelectionChanged(…)
//   private void AppendWebcamDebugLog(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.LabTab.cs; wired when they move to Core.
        private void MicActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

        // ponytail: needs the services in MainWindow.LabTab.cs; wired when they move to Core.
        private void WebcamActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

    }
}
