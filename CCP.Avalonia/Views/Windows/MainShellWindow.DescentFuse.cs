// STILL A STUB from ConditioningControlPanel/MainWindow/MainWindow.DescentFuse.cs (275 lines).
// Head-side: the five surfaces exist on this head, the countdown behind them does not.
//
// Present already: FuseSparkHost / FuseSparkGlyph (MainShellWindow.axaml:2027/2033) and
// FuseCornerReadout / FuseCornerDigits / FuseCornerPresence (:3366/3371/3374). The Vigil size bump
// would use the unnamed ScaleTransform already on FuseSparkHost - Avalonia cannot name a transform,
// so WPF's FuseSparkScale is reached as FuseSparkHost.RenderTransform.
//
// Missing, all in the WPF head:
//   App.DescentCountdown            Services/Descent/DescentCountdownService.cs - PhaseChanged,
//                                   Tick, LastAnnouncedPhase, Remaining, VigilCount
//   DescentFusePhase                the phase ladder (Dark/Whisper/Clock/Vigil/Terminal/Zero)
//   DescentFuseChrome.CurrentStep   Services/Descent/DescentFuseChrome.cs
//   DescentFuseCopy.TMinus/Presence Services/Descent/DescentFuseCopy.cs
//   RefreshThemeAwareElements       MainWindow.xaml.cs - the app's single writer of the neutral
//                                   colour family, which the dimming re-derives from
//   MotionFx.GlowBreath / Stop      Services/MotionFx.cs
//   Services.UI.FontGuard.Mono      the tabular font the digits must not reflow in
// CCP.Core/Services/Descent/ carries the SHOW (DescentFuseTimeline, DescentIgnitionTimeline,
// DescentFuseHandoff) but no countdown and no phase, so none of the above is a stale note.
//
// Members still absent (14): FuseGold, FuseGoldBrush, FuseNeutralDigits, _fuseTooltipDigits,
// _fuseLastDimStep, _fuseBreathing, Freeze, RestoreFuseChrome, InitializeDescentFuse,
// OnFusePhaseChanged, OnFuseTick, ApplyFusePhase, ApplyFusePresence, BuildFuseTooltip.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
