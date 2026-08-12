using System.Windows;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// Every click on the Play wall, and nothing else.
    ///
    /// <para><b>Shims only — this is the launch-parity contract made literal.</b> Each method
    /// forwards to the <see cref="MainWindow"/> handler the old button already ran, so an entitled
    /// user's click executes the same handler object it always did: the slot picker still opens for
    /// FALL IN, Quick Drop still skips it, the Goon duel is still re-focused rather than relaunched,
    /// the intake still refuses a spent week from its own page. Nothing here re-implements a launch,
    /// and nothing here decides a tier — the hard checks live inside those handlers
    /// (<c>TierGate.DemandLab</c> / <c>DemandPremium</c>), and the lockbands this file's sibling
    /// paints are decoration over the top.</para>
    ///
    /// <para>Split out of PlayTabView.xaml.cs on purpose (same convention as
    /// <see cref="AppSettingsTabView"/> and <see cref="StudioTabView"/>): the host file owns the
    /// page frame and the one ambient loop, this file owns the cards, and two agents never have to
    /// touch one file.</para>
    ///
    /// <para>Four clicks are navigation rather than launch — Remote Control, Lockdown, Blink Trainer
    /// and Graded Intake are <i>pages</i>, and For You is a window that <c>ShowTab</c> intercepts.
    /// Routing them through <c>ShowTab</c> keeps one gate and one bark.</para>
    ///
    /// <para><b>2026-08-12 relayout.</b> Five shims were deleted with their cards — Available
    /// Subjects, Mantras, Deeper, the Inspection Bureau and the Showcase shelf all came off THIS
    /// page. The MainWindow handlers they forwarded to (<c>BtnAvailableSubjects_Click</c>,
    /// <c>StartMantraSession</c>, <c>BtnDeeper_Click</c>, <c>BtnStartBureau_Click</c>,
    /// <c>ShowTab("exclusives")</c>) are all still there and still called from those features' own
    /// doors, so nothing was un-shipped; the Play page simply stopped being a second entrance. Do
    /// not re-add a shim here without re-adding its card, and do not re-add either without deciding
    /// that this page should own that door again.</para>
    /// </summary>
    public partial class PlayTabView
    {
        /// <summary>The one cast every shim makes. Null while the view is being built, or in the
        /// designer — a card that fires then simply does nothing rather than throwing into WPF's
        /// event plumbing.</summary>
        private MainWindow? Owner => Window.GetWindow(this) as MainWindow;

        // ---- DESCENT ---------------------------------------------------------------------

        private void BtnStartChaos_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnStartChaos_Click(sender, e);

        private void BtnQuickStartChaos_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnQuickStartChaos_Click(sender, e);

        // ---- TOGETHER --------------------------------------------------------------------

        private void BtnStartGoon_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnStartGoon_Click(sender, e);

        private void BtnPlayRemoteControl_Click(object sender, RoutedEventArgs e)
            => Owner?.ShowTab("remotecontrol");

        // ---- EYES ------------------------------------------------------------------------

        private void BtnOpenDeviceSettings_Click(object sender, RoutedEventArgs e)
            => Owner?.OpenDeviceSettings();

        private void BtnGazeMinigame_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnGazeMinigame_Click(sender, e);

        // The only Focus Gaze switch in the app since the Lab retired. The handler reads
        // ChkPlayFocusGaze and writes TxtPlayFocusGazeStatus directly, and it now carries its own
        // Tier 2 bar on the ON branch (turning it OFF is never gated).
        private void ChkFocusGaze_Changed(object sender, RoutedEventArgs e)
            => Owner?.ChkFocusGaze_Changed(sender, e);

        // Blink Trainer sits in the EYES zone since the 2026-08-12 relayout (it is a webcam
        // feature), but the click is unchanged - the same page, through the same handler.
        private void BtnLabBlinkTrainerOpenNew_Click(object sender, RoutedEventArgs e)
            => Owner?.BtnLabBlinkTrainerOpenNew_Click(sender, e);

        // ---- SESSIONS --------------------------------------------------------------------

        // All four pass states navigate. The page's gate (RefreshGradedIntakeGate) is what explains
        // a spent week or a missing login, and BtnStartIntake_Click behind it is the belt-and-braces
        // refusal - so a locked click here has to ARRIVE somewhere, not be swallowed.
        private void BtnPlayGradedIntake_Click(object sender, RoutedEventArgs e)
            => Owner?.ShowTab("gradedintake");

        // "Where does a pass come from?" has one honest answer: the Home logo tile's flip ceremony
        // hands them out. "settings" is Home's tab key (the Settings DOOR is "appsettings").
        private void BtnPlayIntakePassHome_Click(object sender, RoutedEventArgs e)
            => Owner?.ShowTab("settings");

        // ShowTab("fyp") - never FypHostService.Launch(). ShowTab intercepts the key and calls
        // OpenFypFeed, which owns the premium gate; it is the same launch path the dashboard rail
        // chip uses, so the two can never drift.
        private void BtnPlayFyp_Click(object sender, RoutedEventArgs e)
            => Owner?.ShowTab("fyp");

        private void BtnPlayLockdown_Click(object sender, RoutedEventArgs e)
            => Owner?.ShowTab("lockdown");

        // ---- MORE ------------------------------------------------------------------------

        // Navigates to the ONE Loom entry (Studio rack -> Spiral module), never launches a second
        // editor. OpenStudioModule is the same helper every dashboard mosaic tile routes through.
        private void BtnPlayLoom_Click(object sender, RoutedEventArgs e)
            => Owner?.OpenStudioModule("spiral");
    }
}
