using System;
using System.Windows;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Studio rack's <b>haptics</b> module — the re-host of the whole Haptics page.
    ///
    /// <para><b>Why the page moved WHOLE instead of being decomposed.</b> Phase 2 decomposed
    /// Settings into sections and paid for it in passthroughs, one per x:Name a MainWindow
    /// partial dereferences. Haptics has <b>71</b> such names spread over
    /// <c>MainWindow.Haptics.cs</c> (1090 lines of state), <c>.Patreon.cs</c>,
    /// <c>.PremiumRail.cs</c>, <c>.Presets.cs</c>, <c>.Remember.cs</c>,
    /// <c>.SessionFeatureLock.cs</c>, <c>.TabFxTakeoverLabStatus.cs</c> and <c>.xaml.cs</c> —
    /// plus a live <c>IsVisibleChanged</c> subscription and two <c>features/vibe.png</c> repaint
    /// rows. Moving the <see cref="HapticsTabView"/> <i>instance</i> rather than its contents
    /// collapses all 71 into exactly one property (<c>MainWindow.HapticsTab</c>, in
    /// <c>MainWindow.TabNavigation.cs</c>, forwarding to <c>StudioTabView.HapticsPanel</c>), and
    /// leaves the page's own namescope, resource dictionary and DataTemplates untouched. Nothing
    /// inside <c>HapticsTabView.xaml</c> had to be renamed, re-parented or re-grouped — which
    /// matters most for the routing rows built in <c>MainWindow.Haptics.cs:124-129</c>, where the
    /// "Deeper" row silently also gates FunScript (both ride <c>HapticLayer.Pattern</c>). That row
    /// must never be relabelled, reordered or split; hosting the page whole makes it impossible
    /// to do so by accident.</para>
    ///
    /// <para><b>What still fires, and why nothing changed for barks.</b> The nav rail keeps
    /// <c>BtnNavHaptics</c> and it keeps calling <c>ShowTab("haptics")</c>.
    /// <c>NotifyTabNavigated</c> runs at the TOP of <c>ShowTab</c> with the incoming key, before
    /// the switch that re-routes the case to the Studio tab, so the bark key on that path is
    /// still literally <c>"haptics"</c> and never <c>"studio"</c>. The three haptics
    /// <c>TabNavigated</c> rules per built-in mod (and any third-party <c>.ccpmod</c>'s) keep
    /// matching, as does <c>MaybeShowFeatureIntro("haptics")</c> and its
    /// <c>FeatureIntros["haptics"]</c> card.</para>
    ///
    /// <para><b>No FeatureOpened bark for this module.</b> The popup path never had one — Haptics
    /// was a tab, not a <c>Features/*FeatureControl</c>, so no <c>feature_eq</c> rule exists for
    /// it in any mod. The rack table therefore passes a null bark key rather than inventing
    /// <c>"Haptics"</c>, which would fire into silence.</para>
    ///
    /// <para><b>Visibility semantics are preserved, not merely approximated.</b>
    /// <c>HapticsTabView.IsVisible</c> is what drives the 1 Hz live-status poll
    /// (<c>MainWindow.Haptics.cs:145</c>) and the Phase-4a status pulse
    /// (<c>MainWindow.TabFxTakeoverLabStatus.cs:142</c>). <see cref="UIElement.IsVisible"/> is the
    /// EFFECTIVE flag — true only when the element and every ancestor are visible — so it is now
    /// true exactly when the Studio tab is on screen AND the haptics module is the selected rack
    /// entry. That is strictly tighter than before (the poll no longer runs for a tab the user
    /// merely navigated past) and never looser, so no timer can leak.</para>
    /// </summary>
    public partial class StudioTabView
    {
        private bool _hapticsModuleHooked;

        /// <summary>
        /// Live-wires the haptics rack row's state dot.
        ///
        /// <para>Every other dot in the rack reads a top-level <c>AppSettings</c> property and
        /// repaints from the shared <c>PropertyChanged</c> filter. Haptics does not: its enable
        /// lives on the nested <c>AppSettings.Haptics</c> object (<c>HapticSettings</c>), which
        /// raises no INPC of its own, so the generic path can never see it move. Without this the
        /// dot would only ever catch up on the next Studio show or rack selection — which reads
        /// as a broken indicator, since the toggle that changes it is sitting on screen two
        /// inches to the right.</para>
        ///
        /// <para>Hooked from the view's Loaded rather than its constructor because
        /// <c>ChkHapticsEnabled</c> is a generated field of the hosted page's namescope, and
        /// deliberately additive: <c>MainWindow.Haptics.cs</c> owns the checkbox's real handlers
        /// (including the premium-gate deferred revert, which flips IsChecked back). Firing twice
        /// on a reverted toggle is free — <c>RefreshDots</c> only reads state and repaints.</para>
        /// </summary>
        private void HookHapticsModule()
        {
            if (_hapticsModuleHooked) return;

            try
            {
                var chk = PanelHaptics?.ChkHapticsEnabled;
                if (chk == null) return;

                chk.Checked += OnHapticsEnabledToggled;
                chk.Unchecked += OnHapticsEnabledToggled;
                _hapticsModuleHooked = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("StudioTabView.HookHapticsModule: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Repaints at <see cref="System.Windows.Threading.DispatcherPriority.Normal"/> — never
        /// <c>Loaded</c>, which starves under layout work (see the DispatcherPriority note in
        /// PLAN §5.10) — and one beat late, so a handler that vetoes the toggle and reverts
        /// IsChecked has already run by the time the dot reads the value.
        /// </summary>
        private void OnHapticsEnabledToggled(object sender, RoutedEventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                       new Action(RefreshDots));
            }
            catch { /* a state dot must never break a toggle */ }
        }
    }
}
