// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs (1,537
// lines) - the Blink Trainer's brain: the webcam tracker, the gaze calibration, the consent gate,
// the overlay session, the folder library and the demo loop.
//
// NOTHING IS RESTORED HERE, and unlike the neighbouring FX partials the reason is a refusal, not a
// shortage. THIS FILE IS THE CAMERA: almost every member opens a webcam, reports whether one is
// open, or stands between the user and one being opened, and a half-ported member of that kind is
// worse than the stub it replaces.
//
// The page is NOT the obstacle, which is why the old blanket header misled:
// CCP.Avalonia/Views/Tabs/BlinkTrainerTabView.axaml is ported and IS hosted
// (MainShellWindow.axaml:2534), so its controls are reachable - with FindControl, never a field,
// because that view loads with AvaloniaXamlLoader.Load. What is missing is underneath it.
//
// REFUSED (not "blocked"), each with the lie it would tell:
//   * the tracker toggles - RefreshBlinkTrainerTrackerButton, ToggleWebcamTrackingAsync,
//     BtnBlinkTrainerStartStopTracker_Click, ToggleWebcamFromHotkey, RefreshDeeperWebcamColumn,
//     BtnDeeperWebcamStartStopTracker_Click. Their portable half is the LABEL, their unportable
//     half is the capture device: restore the half that ports and the button reads "off" over a
//     camera still open. Same shape as the mic toggle an earlier layer refused.
//   * SetBlinkTrainerStatusPulse (MainShellWindow.TabFxTakeoverLabStatus.cs:284 names THIS file as
//     the caller it waits for). It makes BlinkTrainerStatusDot breathe to mean "tracking is live".
//     With no tracker nothing is live, and a light that breathes over a dead camera is exactly the
//     state a user checks that light to rule out.
//   * the consent gate - BtnBlinkTrainerManageConsent_Click, BtnBlinkTrainerRevokeConsent_Click,
//     their two Deeper twins, BtnBlinkTrainerGateUnlock_Click. CCP.Core/Services/Webcam/
//     WebcamConsent.cs carries ConsentVersion and IsCurrent(settings) - enough to READ consent,
//     nowhere near enough to GRANT or REVOKE it (the WPF flow is a dialog, a settings write and a
//     tracker teardown). A gate that opens but cannot close is worse than no gate.
//   * the status row - RefreshBlinkTrainerGate, DetermineBlinkTrainerStageMode,
//     RefreshBlinkTrainerStatusRow, ApplyBlinkTrainerStatusState, DetermineBlinkTrainerStatusState,
//     SetStartButtonState, WireBlinkTrainerStatusAction, the three BlinkTrainerStatusAction_*,
//     HasUsableCalibration, IsMultiMonitorEnvironment, RefreshBlinkTrainerWebcamColumn,
//     _currentBlinkTrainerStageMode, _currentBlinkTrainerStatusState,
//     _blinkTrainerStatusActionHandler. It is the one surface that says WHY the trainer will not
//     start (no consent / no folders / no calibration); deriving it from the two inputs that are
//     portable and guessing the third sends the user to fix the wrong thing.
//
// BLOCKED ON HEAD SERVICES, no judgement involved (Services/BlinkTrainerService.cs and
//   Services/BlinkTrainerAssetPool.cs - the tracker, the calibration, the overlay):
//   BtnBlinkTrainerStartSession_Click, BtnBlinkTrainerCalibrate_Click,
//   BtnBlinkTrainerQuickRecal_Click and the Deeper twins, ApplyBlinkTrainerStageMode,
//   ResetBlinkTrainerStageForLive, Subscribe/UnsubscribeBlinkTrainerLiveBlink,
//   OnBlinkTrainerStagePreviewBlink, ApplyBlinkTrainerLiveImage/LiveVideo,
//   BlinkTrainerStageMedia_MediaEnded, Invalidate/GetOrBuildBlinkTrainerLivePool, the four
//   _blinkTrainerLive* fields.
//
// BLOCKED ON ART: the demo loop (EnsureBlinkTrainerDemoAssetsLoaded, Start/Stop/AdvanceBlinkTrainer
//   Demo(Loop), BlinkTrainerDemoTimer_Tick, the four _blinkTrainerDemo* fields). The frames live at
//   ConditioningControlPanel/assets/BlinkTrainer - inside the WPF head, not the shared /Assets root
//   - and CCP.Avalonia.csproj Links none of them. The loop is a DispatcherTimer cross-fade and
//   ports easily; it would just cross-fade nothing. Moving the frames to /Assets and Linking them
//   is a csproj change this layer may not make.
//
// BLOCKED ON A SETTINGS LOAD PASS - the subtle one, because these look free.
//   ToggleBlinkTrainerIncludeVideos_Changed, SliderBlinkTrainerDuration/OpacityNew_Changed,
//   BlinkTrainerMixOptionSame/Mix_Click, SetMixMode, SetMixModeSelection are pure settings editors
//   and every field they write is already in CCP.Core/Models/AppSettings.cs. They still must NOT
//   be restored first: there is no LoadBlinkTrainerSettingsToUi here, so the controls sit on MARKUP
//   defaults, and Avalonia raises IsCheckedChanged/ValueChanged on a programmatic set as well as a
//   user one - the tab's first layout would write the markup default over the user's saved
//   duration, opacity and mix mode. Data loss on settings set deliberately. Restore the load pass
//   (with an _isLoading guard and compare-before-write) in the same change, or not at all.
//
// ALSO BLOCKED: the folder library (Rebuild/BuildBlinkTrainerFolderCard(s),
//   BtnBlinkTrainerAdd/RemoveFolderCard_Click) needs a folder picker - Avalonia's StorageProvider,
//   not Microsoft.Win32 - plus the same load pass; and the slider drag polish
//   (BlinkTrainerSlider_DragStart/DragEnd/LostCapture, AnimateBlinkTrainerSliderLabel,
//   SliderBlinkTrainerOpacityNew_Loaded, ApplyBlinkTrainerOpacityFillOpacity,
//   _blinkTrainerOpacityFillButton) reaches into the WPF Slider template's RepeatButton, which has
//   no Avalonia part name.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing is restored - see the header. Those refusals are decisions, not a to-do list.
    }
}
