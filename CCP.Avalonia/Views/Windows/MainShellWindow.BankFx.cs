// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.BankFx.cs (923 lines).
//
// The blanket header this file shipped with is replaced: no member here touches WebView2 or Win32,
// and the real blocker is one type plus one missing event source.
//
// THE BANK IS THE XP DRIP. Awards land in an accumulator that holds them back, then flies a burst
// of tokens from wherever the XP was earned to the header's XP counter, which pops when they land.
// Three things hold the whole file up:
//   * BankAccumulator - ConditioningControlPanel/Services/BankAccumulator.cs, still in the WPF
//     head. It owns the hold/release POLICY (what is a drip and what is a flood), so writing one
//     here would be a second copy of the rule that decides how fast XP appears - exactly the
//     duplication CCP.Core exists to prevent. It is a plain service and looks movable, but that is
//     a Core layer's call, not a head layer's.
//   * OnBankXpChanged / OnBankXpAwarded - CCP.Core/CoreProgression.cs is an AddXP provider only
//     and raises no XpChanged or XpAwarded event, so there is nothing on this head to subscribe
//     to. Same source blocker as the profile bubble's live reactions.
//   * TryHoldXpDisplay / ReleaseXpHold / StepBankCounter / PopXpCounter / FlashXpBarOnBankLanding -
//     the counter these drive (MainShellWindow.axaml's TxtXP and XPBar) is still the XAML's static
//     "0/70 XP". Holding and stepping a display nothing else writes would report a drip that is
//     not happening.
//
// What is NOT a blocker, recorded so the next layer does not re-derive it: AmbientFxCanvas is a
// full port on this head and can fly the tokens (EnsureBankLayer, LaunchBankFlight,
// OnBankTokenLanded, AbortBankFlight); PlayBankThud maps onto CoreAudio; DispatcherTimer,
// Stopwatch and the origin/target resolution (TryResolveBankTarget, TryResolveBankOrigin,
// TryBankAnchorCenter, TryBankAnchorBounds, IsFiniteBankPoint) are portable as written, with WPF's
// TransformToAncestor becoming Visual.TranslatePoint. InitializeBankFx and ShutdownBankFx would be
// called from the window constructor and OnClosing.
//
// Members dropped (56):
//   private const double BankBoxPadPx
//   private const double BankBoxMaxPx
//   private const double BankFallbackOriginDx
//   private const int BankPollMs
//   private const double BankWatchdogMs
//   private const double BankPopScale
//   private const double BankPopOutMs
//   private const double BankPopSpringMs
//   private const double BankPopSpringAmplitude
//   private const string BankThudOverride
//   private const string BankThudFallback
//   private const float BankThudScale
//   private const string BankThudTag
//   private BankAccumulator? _bank
//   private DispatcherTimer? _bankPoll
//   private readonly Stopwatch _bankClock
//   private AmbientFxCanvas? _bankLayer
//   private bool _bankLayerFailed
//   private Point _bankLayerOrigin
//   private ScaleTransform? _bankPopTransform
//   private volatile bool _bankAwardPending
//   private bool _bankHolding
//   private double _bankHeldXp
//   private double _bankHeldNeeded
//   private int _bankHeldLevel
//   private bool _bankFlightLive
//   private double _bankFlightStartedMs
//   private double _bankFlightPot
//   private double _bankFlightFrom
//   private double _bankFlightShown
//   private int _bankFlightTokens
//   private int _bankFlightId
//   internal void InitializeBankFx(…)
//   internal void ShutdownBankFx(…)
//   private void OnBankWindowStateish(…)
//   private void OnBankXpChanged(…)
//   private void OnBankXpAwarded(…)
//   internal bool TryHoldXpDisplay(…)
//   private void ReleaseXpHold(…)
//   private void StartBankPoll(…)
//   private DispatcherTimer? CreateBankPoll(…)
//   private void StopBankPoll(…)
//   private void OnBankPollTick(…)
//   private void LaunchBankFlight(…)
//   private void OnBankTokenLanded(…)
//   private void AbortBankFlight(…)
//   private void StepBankCounter(…)
//   private void PopXpCounter(…)
//   private void FlashXpBarOnBankLanding(…)
//   private static void PlayBankThud(…)
//   private bool TryResolveBankTarget(…)
//   private bool TryResolveBankOrigin(…)
//   private bool TryBankAnchorCenter(…)
//   private bool TryBankAnchorBounds(…)
//   private static bool IsFiniteBankPoint(…)
//   private AmbientFxCanvas? EnsureBankLayer(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
