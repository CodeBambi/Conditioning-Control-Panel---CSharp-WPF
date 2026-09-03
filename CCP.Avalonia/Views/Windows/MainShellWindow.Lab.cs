// PORTED-AS-A-STUB from ConditioningControlPanel/MainWindow/MainWindow.Lab.cs (1354 lines).
//
// ponytail: wholesale stub. Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// The handlers named by MainShellWindow.axaml are real (empty) methods, because a
// missing one is a XAML compile error, not a runtime gap.
//
// Members dropped (47):
//   private void InitializeLockdown(…)
//   internal void BtnActivateLockdown_Click(…)
//   internal void BtnStartQuiz_Click(…)
//   internal void BtnStartIntake_Click(…)
//   internal void BtnStartBureau_Click(…)
//   internal void BtnStartGoon_Click(…)
//   internal void BtnStartChaos_Click(…)
//   internal void OpenFypFeed(…)
//   internal void BtnStartArcademy_Click(…)
//   internal void BtnQuickStartChaos_Click(…)
//   private bool _intakePassHooked
//   internal void RefreshGradedIntakeGate(…)
//   private void SetGradedIntakeGateCopy(…)
//   private void EnsureIntakePassHooked(…)
//   private void OnIntakePassStateChanged(…)
//   private void RefreshPastQuizzes(…)
//   internal void ChkPopQuizEnabled_Changed(…)
//   internal void SliderPopQuizFrequency_ValueChanged(…)
//   internal void BtnTestPopQuiz_Click(…)
//   private void OnLockdownActivated(…)
//   private void OnLockdownDeactivated(…)
//   private void OnLockdownTick(…)
//   private static string FormatLockdownClock(…)
//   private void SetLockdownBadge(…)
//   private void LockdownBadge_Click(…)
//   internal void TxtLockdownTimer_Click(…)
//   internal void TxtLockdownExit_KeyDown(…)
//   private Action<PossessionRung>? _possessionRungHandler
//   private static readonly Color PossessionEmber
//   private static readonly Color PossessionEmberDim
//   private void HookPossessionReadout(…)
//   private void DetachPossessionRungHandler(…)
//   private void UnhookPossessionReadout(…)
//   private void UpdatePossessionReadout(…)
//   private Action<string>? _lockdownRestartHandler
//   private void HookLockdownRestart(…)
//   private void UnhookLockdownRestart(…)
//   private void OnLockdownTimerRestarted(…)
//   internal void PreviewShowLockdownActivePanel(…)
//   private void ShowPossessionRulesIfFirstTime(…)
//   private static readonly Color LockdownCrimson
//   private static readonly Color LockdownDarkRed
//   private static readonly Color LockdownPanelBg
//   private static readonly Color LockdownWindowBg
//   private void ApplyLockdownTheme(…)
//   private void RestoreLockdownTheme(…)
//   private void PlayLockdownActivationAnimation(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: needs the services in MainWindow.Lab.cs; wired when they move to Core.
        private void LockdownBadge_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e) { }

    }
}
