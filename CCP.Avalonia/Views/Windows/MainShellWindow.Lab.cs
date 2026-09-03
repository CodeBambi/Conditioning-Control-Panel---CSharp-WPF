// PARTLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Lab.cs (1354 lines) - the
// lockdown partial, and the launcher for every Lab activity.
//
// LockdownTabView names this file as its blocker. That is still true of the LOCKDOWN half and this
// layer does not change it, but it is worth being exact about which half, because the two do not
// have the same shape:
//
//   THE ONE MEMBER THAT WAS NEVER BLOCKED is LockdownBadge_Click, restored below. The badge is a
//   signpost, not a control: it navigates to the Lockdown page and cannot end anything. ShowTab is
//   real here, so the badge in the title bar now leads somewhere instead of swallowing the click.
//
//   THE LOCKDOWN LIFECYCLE IS HARD-BLOCKED, and not on a service move. InitializeLockdown,
//   BtnActivateLockdown_Click, OnLockdownActivated/Deactivated/Tick, the exit-phrase path and
//   PreviewShowLockdownActivePanel need ConditioningControlPanel/Services/LockdownService plus the
//   low-level keyboard hook (_keyboardHook.SuppressSystemKeys) - a Win32 WH_KEYBOARD_LL hook whose
//   whole job is suppressing Win/Alt-Tab. There is no cross-platform twin of that, so it is a
//   per-platform reimplementation (CLAUDE.md bucket E), not a port.
//   SetLockdownBadge and FormatLockdownClock would compile here today - they are Named<Border> +
//   TimeSpan.ToString - but they exist only to be driven by OnLockdownTick, so restoring them
//   would add a clock nothing winds. Left named rather than half-built.
//   ApplyLockdownTheme / RestoreLockdownTheme / PlayLockdownActivationAnimation repaint the whole
//   window's brushes from code; on this head those are theme resources, and repainting them for a
//   lockdown that cannot start would be motion with no cause.
//
//   THE POP-QUIZ AND GRADED-INTAKE EDITORS ARE NOT THIS FILE'S ANY MORE. WPF's MainWindow owned
//   ChkPopQuizEnabled_Changed / SliderPopQuizFrequency_ValueChanged / BtnTestPopQuiz_Click because
//   the intake markup was inline in MainWindow.xaml. The port moved that markup to
//   CCP.Avalonia/Views/Tabs/GradedIntakeTabView.axaml, whose Click=/Changed= handlers name THAT
//   view (GradedIntakeTabView.axaml.cs:36-38, still stubs). The first two are one CoreSettings
//   write each - PopQuizEnabled and PopQuizFrequency are both in Core, and the slider needs the
//   `_isLoading` guard because Avalonia raises ValueChanged on a programmatic set too. Restoring
//   them HERE would be a second copy no click can reach; they belong in that view's layer.
//   RefreshGradedIntakeGate / SetGradedIntakeGateCopy / EnsureIntakePassHooked stay blocked
//   wherever they land: IntakePassService and App.Patreon.HasPremiumAccess are the gate.
//
//   THE ACTIVITY LAUNCHERS are all "new SomeWindow(...).Show()" over a service: quiz, intake,
//   bureau, goon, chaos, arcademy, the FYP feed. Windows and services both, so both blocked.
//
// Members dropped (47 - the one now restored is marked RESTORED):
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
//   private void LockdownBadge_Click(…)                RESTORED
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

using System;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// The crimson pill in the title bar's status row. It is a signpost, not a button that
        /// ends anything: it navigates to the Lockdown page, where the Emergency Exit and the
        /// secret phrase both live. Nothing here can end a lockdown, which is what lets it sit one
        /// pixel from the window's close button.
        ///
        /// <para><c>e.Handled = true</c> is kept for the same reason WPF sets it: the badge sits
        /// inside the draggable title bar, and an unhandled press would start a window drag under
        /// the finger that just navigated.</para>
        ///
        /// <para>ponytail: the badge's own visibility and clock still need
        /// <c>SetLockdownBadge</c>/<c>OnLockdownTick</c>, i.e. LockdownService - see the header.
        /// The XAML leaves it hidden, so today this handler is reachable only from a preview.</para>
        /// </summary>
        private void LockdownBadge_Click(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                e.Handled = true;
                ShowTab("lockdown");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lockdown: badge navigation failed");
            }
        }
    }
}
