// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs.
//
// The camera-free half now lives in the page itself, CCP.Avalonia/Views/Tabs/BlinkTrainerTabView
// .axaml.cs: tab-show refresh with an _loading guard (settings load pass), demo stage loop (frames
// moved to /Assets/BlinkTrainer/Demo and linked by both heads), premium gate, status row (decided by
// CCP.Core/Services/BlinkTrainerState.cs, which WPF also calls), folder library (StorageProvider),
// session settings editors, opacity fill and slider label polish. The Play wall's Blink Trainer card
// routes here (PlayTabView BtnLabBlinkTrainerOpenNew_Click).
//
// Live on the page: the tracker toggle (Platform/WebcamTracker), session start/stop with its countdown
// (Views/Overlays/BlinkTrainerSession: click-through overlays per screen, swap per blink, duration
// auto-stop, haptic pulse, quest credit; panic and exit stop it), the live stage preview (images and,
// through a VlcFrameSink, muted looping video), consent manage/revoke (WebcamTracker.RevokeConsent,
// the seeded CoreWebcam verb), Quick Recal over the gaze feed, the 16-point calibration
// (WebcamCalibrationWindow), the help popover, and the status pulse (the page calls
// SetBlinkTrainerStatusPulse in MainShellWindow.TabFxTakeoverLabStatus.cs). STILL REFUSED, each needing
// a missing surface: ToggleWebcamFromHotkey (global hotkey), the loading splash and the Deeper hub's
// webcam twins.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Nothing is restored - see the header. Those refusals are decisions, not a to-do list.
    }
}
