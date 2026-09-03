// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.ChromeFx.cs (914 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (63):
//   private const int TabFadeOutMs
//   private const int TabFadeInMs
//   private const double TabSlidePx
//   private const double NavIconHoverScale
//   private const int NavIconHoverMs
//   private const double NavGlowMinOpacity
//   private const double NavGlowMaxOpacity
//   private const double NavGlowBreathSeconds
//   private const double StartGlowMinOpacity
//   private const double StartGlowMaxOpacity
//   private const double StartGlowBreathSeconds
//   private const double StartSheenSeconds
//   private const int StartSheenIntervalSeconds
//   private const double BannerSheenSeconds
//   private const int BannerSheenMinGapSeconds
//   private const double XpSheenSeconds
//   private bool _chromeFxInitialized
//   private bool _chromeFxWindowActive
//   private string _pendingTabKey
//   private string _activeTabKey
//   private UIElement? _activeTabElement
//   private Button? _navGlowButton
//   private Button? _navGlowDoor
//   private FrameworkElement? _navActiveBar
//   private DispatcherTimer? _startSheenTimer
//   private DateTime _lastBannerSheenUtc
//   private DispatcherTimer? _staggerCleanupTimer
//   private List<FrameworkElement>? _staggeredElements
//   private double _lastXpShown
//   private int _lastXpLevelShown
//   private IEnumerable<Button> NavButtons
//   internal void InitializeChromeFx(…)
//   private void OnChromeFxWindowStateish(…)
//   internal void RefreshChromeFx(…)
//   private bool ChromeAmbientAllowed
//   private void ApplyChromeFxLoops(…)
//   private void AnimateTabIn(…)
//   private void SlideTabIn(…)
//   private void FadeOutgoingTab(…)
//   private static void CollapseOutgoingTab(…)
//   private static TranslateTransform? EnsureTabTranslate(…)
//   private static void ResetTabSlide(…)
//   private void StaggerTabCards(…)
//   private void StaggerCleanupTimer_Tick(…)
//   private void CancelStaggerCleanup(…)
//   private static List<FrameworkElement> FindStaggerTargets(…)
//   private void NavButton_MouseEnter(…)
//   private void NavButton_MouseLeave(…)
//   private void NudgeNavIcon(…)
//   private static ScaleTransform? EnsureIconScale(…)
//   private Button? NavButtonForTab(…)
//   private void ApplyNavActiveGlow(…)
//   private static FrameworkElement? SetNavIndicator(…)
//   private void ApplyNavGlowBreath(…)
//   private void ApplyStartButtonGlow(…)
//   private void StartSheenHost_SizeChanged(…)
//   private void SweepStartSheen(…)
//   private void SweepSheen(…)
//   private static void ParkSheen(…)
//   private void AnimateXpDisplay(…)
//   private void FillXpBarTo(…)
//   private void ApplyXpSheen(…)
//   internal void SweepBannerSheen(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.ChromeFx.cs; wired when they move to Core.
        private void StartSheenHost_SizeChanged(object? sender, global::Avalonia.Controls.SizeChangedEventArgs e) { }

    }
}
