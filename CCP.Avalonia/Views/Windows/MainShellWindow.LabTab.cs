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
// THE TWO STATUS PILLS ARE DELIBERATELY LEFT AS STUBS, and this is the file's one real judgement
// call rather than a missing dependency:
//
//   MicActivePill_Click is WPF's DisarmVoiceMic - it clears wake-word and push-to-talk, cuts live
//   capture, tears down the audio loop and the keyboard hook, and downgrades any open Voice Lock
//   Card to a typed solve so the lock still holds. It is a PRIVACY STOP.
//   WebcamActivePill_Click is the same affordance for the camera: GazeFocus.Stop, BlinkTrainer.Stop,
//   Webcam.Stop, released together.
//   Half-porting either one is worse than the stub. The restorable half is the part that hides the
//   pill; the unrestorable half is the part that closes the device. A pill that vanishes on click
//   while the capture device stays open is a control that LIES about the most safety-relevant state
//   this app has. Neither pill can be shown at all on this head today (both are driven by
//   App.Webcam/App.Speech state changes that never arrive), so the stub costs nothing and the
//   half-port would cost the user's trust.
//
// The rest is genuinely the device: WebcamTrackingService and its debug counters, the calibration
// and quick-recal flows, the loading splash, GazeSide/FocusGaze, the blink trainer's countdown,
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
//   internal void OpenDeviceSettings(…)               ALREADY LIVE (MainShellWindow.Settings.cs:82)
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
        // REFUSED, not pending - see the header. This is the mic's privacy stop (DisarmVoiceMic).
        // The half that would port is the half that hides the pill; the half that cannot is the
        // half that closes the capture device. Do not "wire" this one until App.Speech's disarm
        // path exists on this head.
        private void MicActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

        // REFUSED for the same reason: the camera's panic stop (GazeFocus/BlinkTrainer/Webcam all
        // released together). A pill that clears while the camera stays open is worse than one
        // that does nothing.
        private void WebcamActivePill_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

    }
}
