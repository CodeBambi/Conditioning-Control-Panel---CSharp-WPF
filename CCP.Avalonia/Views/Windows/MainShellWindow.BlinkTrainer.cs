// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs.
//
// The camera-free half now lives in the page itself, CCP.Avalonia/Views/Tabs/BlinkTrainerTabView
// .axaml.cs: tab-show refresh with an _loading guard (settings load pass), demo stage loop (frames
// moved to /Assets/BlinkTrainer/Demo and linked by both heads), premium gate, status row (decided by
// CCP.Core/Services/BlinkTrainerState.cs, which WPF also calls), folder library (StorageProvider),
// session settings editors, opacity fill and slider label polish. The Play wall's Blink Trainer card
// routes here (PlayTabView BtnLabBlinkTrainerOpenNew_Click).
//
// STILL REFUSED, because each needs a capture device this head does not have
// (WebcamTrackingService, BlinkTrainerService, WebcamCalibrationWindow): the tracker toggles and
// ToggleWebcamFromHotkey, SetBlinkTrainerStatusPulse, start/stop session and its countdown, live
// preview (OnBlink swaps, asset pool, stage video), calibrate/quick-recal, consent manage/revoke
// (revoke must tear the tracker down), and the Deeper hub's webcam twins. A control that reads
// "running" over a closed camera is worse than a dead one.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing is restored - see the header. Those refusals are decisions, not a to-do list.
    }
}
