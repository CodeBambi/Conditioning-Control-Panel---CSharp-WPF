// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.ProfileFaucet.cs (1146 lines).
//
// The blanket header this file shipped with is replaced: nothing here reaches WebView2 or Win32,
// and the file is not uniformly blocked.
//
// THE FAUCET IS THE TAP ON THE VAT. It holds the XP the Descent earned and pours it into the glass
// jar when the user presses and HOLDS it - a charge with rungs, a ring that fills, a thud and a
// shiver on release. Two things block it:
//   * VatFaucetHold - ConditioningControlPanel/Services/Descent/VatFaucetHold.cs, still in the WPF
//     head. It owns how much XP is held and what a completed charge is worth. Its siblings
//     VatFillCoordinator and DescentReader are already in CCP.Core/Services/Descent/, so this is
//     the last piece of that fold which is not portable - and re-deriving the hold here would be a
//     second copy of the number the vat's fill is agreed on.
//   * MotionFx.AllowTransitions / AllowAmbientLoops - ConditioningControlPanel/Services/MotionFx.cs.
//     StartFaucetWobble, StartFaucetSparkle and StartChipBreath are ambient loops and must not run
//     under reduced motion; the only copy of that gate on this head is AmbientFxCanvas's private
//     Env, which a non-canvas loop cannot reach (same finding as MainShellWindow.DeeperFx.cs).
// PlayChargeRung and PlayFaucetSfx map onto CoreAudio, FireFaucetPourHaptic onto the haptics
// service, and BuildChargeArc is a StreamGeometry either way; none of those is what holds it up.
//
// ITS CALLERS ARE THE TWO ponytail LINES IN MainShellWindow.ProfileVat.cs. ArmVat and DisarmVat are
// live there and arm/disarm the JAR only; each carries the one-line note saying where
// ArmFaucet(glass, jarW, jarH) and DisarmFaucet() belong. PositionVatTickGlyphs and
// OnFaucetVatOffScreen are named in that file's header too. So restoring this file is an edit in
// exactly two known places once VatFaucetHold moves to Core.
//
// Members dropped (71):
//   private const double FaucetSpoutXInBox
//   private const double FaucetSpoutYInBox
//   private const double FaucetBoxWidth
//   private const double FaucetBoxHeight
//   private const double FaucetRingBox
//   private const double FaucetRingRadius
//   private const double ChargeFloorMs
//   private const double ChargeCapMs
//   private const double ChargeXpPerMs
//   private const int ChargeRungs
//   private const int ChargeCompactAfter
//   private readonly VatFaucetHold _faucetHold
//   private bool _faucetWired
//   private bool _faucetWobbling
//   private bool _faucetSparkling
//   private bool _faucetChipBreathing
//   private DispatcherTimer? _faucetTickTimer
//   private DispatcherTimer? _faucetSettleTimer
//   private DispatcherTimer? _faucetChargeTimer
//   private DateTime _faucetChargeStart
//   private double _faucetChargeBudgetMs
//   private int _faucetChargeRungsPlayed
//   private bool _faucetCharging
//   private bool _faucetChargeCompact
//   private int _faucetPoursToday
//   private string _faucetPourDayUtc
//   private static readonly Random FaucetRng
//   private void ArmFaucet(…)
//   private void PositionVatTickGlyphs(…)
//   private void DisarmFaucet(…)
//   private void OnFaucetVatOffScreen(…)
//   private void WireFaucet(…)
//   private void UpdateFaucetPresentation(…)
//   private static ToolTip BuildFaucetTooltip(…)
//   private void StartFaucetWobble(…)
//   private void StopFaucetWobble(…)
//   private DispatcherTimer CreateFaucetTickTimer(…)
//   private void StartFaucetSparkle(…)
//   private void StopFaucetSparkle(…)
//   private void StartChipBreath(…)
//   private void StopChipBreath(…)
//   private void OnFaucetMouseEnter(…)
//   private void OnFaucetMouseLeave(…)
//   private void AnimateFaucetScale(…)
//   private void OnFaucetMouseDown(…)
//   private void OnFaucetMouseUp(…)
//   private void OnFaucetLostCapture(…)
//   private void OnFaucetKeyDown(…)
//   private void OnFaucetKeyUp(…)
//   private void OnFaucetLostFocus(…)
//   private void BeginFaucetCharge(…)
//   private DispatcherTimer CreateFaucetChargeTimer(…)
//   private void CancelFaucetCharge(…)
//   private void CompleteFaucetCharge(…)
//   private void ShowChargeRing(…)
//   private void UpdateChargeRing(…)
//   private void HideChargeRing(…)
//   private static Geometry BuildChargeArc(…)
//   private void FaucetThud(…)
//   private void FaucetShiver(…)
//   private void FaucetDrip(…)
//   private void SpawnFaucetSparkles(…)
//   private void AnimateFaucetTilt(…)
//   private void StartFaucetSettleWatch(…)
//   private void StopFaucetSettleWatch(…)
//   private DispatcherTimer CreateFaucetSettleTimer(…)
//   private int FaucetPourCountToday(…)
//   private void NoteFaucetPourToday(…)
//   private static void PlayChargeRung(…)
//   private static void PlayFaucetSfx(…)
//   private static void FireFaucetPourHaptic(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
