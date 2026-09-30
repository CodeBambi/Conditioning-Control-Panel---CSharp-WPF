// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs.
//
// The camera-free half now lives in the page itself, CCP.Avalonia/Views/Tabs/BlinkTrainerTabView
// .axaml.cs: tab-show refresh with an _loading guard (settings load pass), demo stage loop (frames
// moved to /Assets/BlinkTrainer/Demo and linked by both heads), premium gate, status row (decided by
// CCP.Core/Services/BlinkTrainerState.cs, which WPF also calls), folder library (StorageProvider),
// session settings editors, opacity fill and slider label polish. The Play wall's Blink Trainer card
// routes here (PlayTabView BtnLabBlinkTrainerOpenNew_Click).
//
// The tracker toggle is live on the page over Platform/WebcamTracker (OpenCV V4L2 capture, Core
// ONNX detectors, Core BlinkDetector); a failed start says why. STILL REFUSED, because each needs
// BlinkTrainerService or WebcamCalibrationWindow: ToggleWebcamFromHotkey, SetBlinkTrainerStatusPulse,
// start/stop session and its countdown, live preview (OnBlink swaps, asset pool, stage video),
// calibrate/quick-recal, the loading splash and the Deeper hub's webcam twins. Those buttons stay
// disabled with a "not available on this build" tooltip.
// Consent manage/revoke DO work on the page (settings half; CoreWebcam.RevokeConsent tears down a
// tracker once one is seeded).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing is restored - see the header. Those refusals are decisions, not a to-do list.
    }
}
