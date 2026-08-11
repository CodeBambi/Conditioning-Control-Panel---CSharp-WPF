using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Controls.Companion
{
    /// <summary>
    /// "What she's allowed to do" — the single AI-permissions surface. See the XAML header for
    /// where it came from and why it sits between the Engine Room and the Workshop.
    ///
    /// <para><b>This file owns two things and delegates everything else.</b> The delegation is the
    /// usual shim: every control here fires the handler <c>MainWindow.Patreon.cs</c> already had, so
    /// the move changed the address of these controls and nothing about what they do. The two things
    /// it owns are the Tier 2 lockband (<see cref="ApplyTierGate"/>) and the accessor
    /// <see cref="IsLabEntitled"/> that lets the handler behind the master toggle refuse a write
    /// without having to ask the tier service a second time.</para>
    ///
    /// <para><b>The gate is presentation.</b> Nothing here stops the AI from doing anything — that
    /// is <c>AiCommandService</c>'s master check and per-effect switch, plus the prompt envelope in
    /// the providers, and none of it reads a control. What this class prevents is a Free account
    /// being shown a switch it cannot own.</para>
    ///
    /// <para><b>Motion budget: zero.</b> No storyboards, no ambient canvas. The room's one Forever
    /// clock is the hero's portrait breathe, and <c>CompanionRoomView.ParkClocks</c> has nothing to
    /// park here on purpose.</para>
    /// </summary>
    public partial class AiPermissionsGrid : UserControl
    {
        /// <summary>Opacity of the effects half while it is behind the lockband.</summary>
        private const double LockedOpacity = 0.32;

        public AiPermissionsGrid()
        {
            InitializeComponent();

            // Two hooks, both cheap, because entitlement can change from either direction: the tab
            // can be opened after a validation landed (IsVisibleChanged), or a validation can land
            // while the tab is already open (UpdateUnlockablesVisibility / SyncLabEffectPermsUI call
            // ApplyTierGate directly).
            Loaded += (_, _) => ApplyTierGate();
            IsVisibleChanged += (_, e) =>
            {
                if (e.NewValue is bool visible && visible) ApplyTierGate();
            };
        }

        /// <summary>
        /// Whether this account clears the Tier 2 bar. Read by the master toggle's handler so a
        /// programmatic revert and the visual state cannot disagree.
        /// </summary>
        internal bool IsLabEntitled => App.Patreon?.HasLabAccess == true;

        /// <summary>
        /// Paints the Tier 2 verdict onto the effects half: disabled and dimmed under a violet
        /// lockband when the account does not clear the bar, untouched when it does.
        ///
        /// <para>Idempotent and safe to call from anywhere — the refusal copy comes from
        /// <see cref="TierGate"/> so this band, the toast a blocked click raises, and any future CLI
        /// refusal all say the same sentence. The copy is written here rather than bound because
        /// <see cref="TierVerdict.Reason"/> is formatted with the feature name; the language pass
        /// re-runs it on every tab open.</para>
        ///
        /// <para>Deliberately does NOT touch the memory half. Chat memory works on cloud AI, which
        /// is Tier 1 — gating it here would take a feature away from accounts that pay for it.</para>
        /// </summary>
        internal void ApplyTierGate()
        {
            try
            {
                var verdict = TierGate.RequiresLab(Loc.Get("lab_ai_effects_memory_title"));

                EffectsGateHost.IsEnabled = verdict.Allowed;
                EffectsGateHost.Opacity = verdict.Allowed ? 1.0 : LockedOpacity;
                EffectsLockBand.Visibility = verdict.Allowed ? Visibility.Collapsed : Visibility.Visible;
                TxtEffectsLockCopy.Text = verdict.Allowed ? string.Empty : verdict.Reason;
            }
            catch (Exception ex)
            {
                // A lockband that throws would take the whole Companion tab down with it.
                App.Logger?.Debug("AiPermissionsGrid.ApplyTierGate failed: {E}", ex.Message);
            }
        }

        /// <summary>
        /// The band's one interactive element. Sends people to the same place every other refusal
        /// in the app does (the App Info &amp; Data popup), rather than inventing a second upsell.
        /// </summary>
        private void BtnEffectsLockCta_Click(object sender, RoutedEventArgs e)
        {
            try { App.MainWindowRef?.ShowAppInfoPopup(); }
            catch (Exception ex) { App.Logger?.Debug("AiPermissionsGrid: tier CTA failed: {E}", ex.Message); }
        }

        // =====================================================================================
        //  shims — the handlers stayed in MainWindow.Patreon.cs, only their host moved
        // =====================================================================================

        private void BtnClearChatMemory_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnClearChatMemory_Click(sender, e);
        }

        private void BtnLabEffectsSetupLocal_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnLabEffectsSetupLocal_Click(sender, e);
        }

        private void ChkAllowEffect_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkAllowEffect_Changed(sender, e);
        }

        private void ChkCapEffects_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkCapEffects_Changed(sender, e);
        }

        private void ChkChatMemoryEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ChkChatMemoryEnabled_Changed(sender, e);
        }

        private void SliderMaxHapticIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.SliderMaxHapticIntensity_ValueChanged(sender, e);
        }
    }
}
