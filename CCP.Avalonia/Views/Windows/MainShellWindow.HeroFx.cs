// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.HeroFx.cs (789 lines).
//
// The blanket header this file shipped with claimed every member reaches App.*, a service, a
// device, WebView2 or Win32. That is FALSE here and is replaced. Nothing in this file touches a
// device or the OS - it is the Start button, the XP bar and the save tick - and every element it
// animates is already in MainShellWindow.axaml: StartRingHost, StartRingExhale, StartRingBurstA,
// StartRingBurstB, BtnStart, XPBar, XPMeniscus, XPBarFlashOverlay, TxtLevelLabel, SaveRipple and
// SaveTick. Two real things block it, and they are the same two for the whole file.
//
// 1. NO CALLER ON THIS HEAD. Every entry point is raised by something that is still a stub:
//      ApplyStartHeroState, ApplyStartCharge, ReleaseStartCharge, TintStartCharge,
//      ApplyStartRingExhale, StopStartRingExhale, FlashStartIgnition, FireStartRing,
//      ApplyStartHeartbeat  <- the engine start/stop path. BtnStart_Click is a stub here
//                              (MainShellWindow.StartStop.cs) and SessionEngine is head-side.
//      AnimateXpMeniscus, ApplyXpMeniscusPulse
//                           <- AnimateXpDisplay, the XP-bar writer (ConditioningControlPanel/
//                              MainWindow/MainWindow.Progression.cs). The header bar on this head
//                              is still the XAML's static "Lvl 1" / "0/70 XP", so there is no fill
//                              width for the meniscus to ride and nothing for it to pulse over.
//      PopLevelChip         <- the level-up beat, from that same progression path.
//      FlashSaveAbsorb      <- the settings-save handler (MainWindow.Settings.cs; the Avalonia
//                              twin MainShellWindow.Settings.cs is a stub for that half).
//      InitializeHeroFx, ApplyHeroFxLoops
//                           <- the window constructor and ApplyChromeFxLoops
//                              (MainShellWindow.ChromeFx.cs, a stub).
//
// 2. THE NAMED TRANSFORMS AND BRUSHES ARE GONE. AVLN2000 forbids x:Name on a Brush or a Transform,
//    so LevelChipScale, LevelChipRotate, the meniscus's TranslateTransform and the Start button's
//    charge gradient have no names to reach. Each IS reachable through the element that owns it -
//    TxtLevelLabel.RenderTransform is a TransformGroup of Scale+Rotate, XPMeniscus.RenderTransform
//    is a TranslateTransform, and BtnStart's gradient must be swapped for a mutable clone at start
//    (CLAUDE.md: "Avalonia cannot name a brush or an effect"). That is the port rather than a
//    blocker, but it is not worth writing under 1.
//
// Three members have no Avalonia equivalent at all and are DROPPED rather than deferred:
// Timeline.SetDesiredFrameRate on the exhale and heartbeat clocks (Avalonia has no per-animation
// frame cap - the same reason AmbientFrameRate went, see MainShellWindow.AmbientFx.cs);
// HandoffBehavior.SnapshotAndReplace on PopLevelChip's re-pop (an Avalonia Animation restarted on
// the same property simply takes over); and _startChargeRestore, which exists only to put back a
// frozen WPF resource brush that this head never freezes.
//
// Members dropped (62):
//   private const double StartChargeDriftSeconds
//   private const double StartExhaleCycleSeconds
//   private const double StartExhaleTravelSeconds
//   private const double StartExhalePeakOpacity
//   private const double StartExhaleGrowPx
//   private const double StartBurstAGrowPx
//   private const double StartBurstBGrowPx
//   private const int StartBurstAMs
//   private const int StartBurstBMs
//   private const int StartBurstStaggerMs
//   private const double StartBurstPeakOpacity
//   private const int StartDipMs
//   private const double StartDipScale
//   private const int StartIgnitionFlashMs
//   private const double StartIgnitionFlashPeak
//   private const double StartHeartbeatGrowPx
//   private const double StartHeartbeatSeconds
//   private const double StartCtaFallbackWidth
//   private const double StartCtaHeight
//   private const int XpMeniscusSlideMs
//   private const double XpMeniscusPulseSeconds
//   private const double XpMeniscusMinOpacity
//   private const double XpMeniscusMaxOpacity
//   private const double XpMeniscusRestOpacity
//   private const double XpMeniscusMinFillPx
//   private const int LevelChipPopMs
//   private const double LevelChipPopScale
//   private const double LevelChipPopDegrees
//   private const int SaveTickDrawMs
//   private const int SaveRippleMs
//   private const double SaveRippleGrowPx
//   private const double SaveAbsorbHoldMs
//   private const double SaveAbsorbFadeMs
//   private const double SaveButtonHeight
//   private LinearGradientBrush? _startChargeBrush
//   private GradientStop[]? _startChargeStops
//   private object? _startChargeRestore
//   private bool _startChargeApplied
//   private bool _startDipInFlight
//   private double _xpMeniscusFillWidth
//   private void InitializeHeroFx(…)
//   private void ApplyHeroFxLoops(…)
//   internal void ApplyStartHeroState(…)
//   private void ApplyStartCharge(…)
//   private void ReleaseStartCharge(…)
//   private LinearGradientBrush? EnsureStartChargeBrush(…)
//   private void TintStartCharge(…)
//   private void ApplyStartRingExhale(…)
//   private static DoubleAnimationUsingKeyFrames ExhaleScale(…)
//   private void StopStartRingExhale(…)
//   private static void ParkRing(…)
//   internal void FlashStartIgnition(…)
//   private static void FireStartRing(…)
//   private void ApplyStartHeartbeat(…)
//   private double StartCtaWidth
//   private static double GrowFactor(…)
//   private void AnimateXpMeniscus(…)
//   private void ApplyXpMeniscusPulse(…)
//   internal void PopLevelChip(…)
//   internal void FlashSaveAbsorb(…)
//   private static(…)
//   private static Color FromHsv(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
