// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (1413 lines) - the
// webcam and microphone partial. Sorted member by member, and unlike its neighbours the blanket
// claim holds here: this file is the camera and the mic. What it does NOT hold for:
//
//   OpenDeviceSettings and RefreshDeviceSettingsLists are listed below as dropped, and one of them
//   is stale. OpenDeviceSettings ALREADY SHIPS on this head - MainShellWindow.Settings.cs:82,
//   ShowTab("appsettings") + AppSettingsPage.FocusSection("devices"), asserted by --nav-check and
//   called from SystemFeatureControl. Do not re-add it here; a second definition is a compile
//   error, and a second copy would be the wrong kind of fix if it were not.
//   RefreshDeviceSettingsLists genuinely is missing, and needs the camera/monitor enumeration
//   below before it means anything.
//
// THE WEBCAM PILL IS DELIBERATELY LEFT AS A STUB. WebcamActivePill_Click is the camera's privacy
// stop (GazeFocus.Stop, BlinkTrainer.Stop, Webcam.Stop, released together); half-porting it - a pill
// that clears while the camera stays open - would lie about the most safety-relevant state this app
// has. The MIC pill is wired (MainShellWindow.SheListening.cs UpdateMicPill/InitializeMicActivePill):
// its click is DisarmVoiceMic, which on this head really closes the capture (StopVoiceInput).
//
// The rest is genuinely the device: WebcamTrackingService and its debug counters, the calibration
// and quick-recal flows, GazeSide/FocusGaze, the blink trainer's countdown,
// the camera and monitor enumerations, and the two consent buttons (WebcamConsent itself IS in
// Core, but "revoke" has to stop a running capture, which is App.Webcam again).
//
// Members dropped (79 - OpenDeviceSettings is already live elsewhere, see above):
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
//   (WireMicActivePill / UpdateMicPill / MicActivePill_Click: PORTED, MainShellWindow.SheListening.cs)
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
//   internal void OpenDeviceSettings(…)               ALREADY LIVE (MainShellWindow.Settings.cs:82)
//   internal void RefreshDeviceSettingsLists(…)
//   private string? _quickRecalTooltipBase
//   private void RefreshQuickRecalHotkeyHint(…)
//   private void WebcamActivePill_Click(…)
//   internal async void BtnWebcamDebugStart_Click(…)
//   private async Task<bool> StartWebcamOffUiThreadAsync(…)
//   private void InstallWebcamLoadingSplash(…)          PORTED: MainShellWindow.WebcamSplash.cs
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
        // WPF MainWindow.LabTab.cs MicActivePill_Click: the mic's privacy stop. Not Lockdown-gated, as WPF.
        private void MicActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is global::Avalonia.Visual v && !e.GetCurrentPoint(v).Properties.IsLeftButtonPressed) return;   // WPF: left button
            try { DisarmVoiceMic(); } catch (System.Exception ex) { Serilog.Log.Warning(ex, "MicActivePill_Click failed"); }
        }

        // REFUSED (see the header): the camera's panic stop (GazeFocus/BlinkTrainer/Webcam all
        // released together). A pill that clears while the camera stays open is worse than one
        // that does nothing.
        private void WebcamActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

    }
}
