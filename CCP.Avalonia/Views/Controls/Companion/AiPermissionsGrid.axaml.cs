using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// "What she's allowed to do" — the single AI-permissions surface, ported from the WPF head.
    ///
    /// <para>On WPF every control here is a one-line shim to the identically named
    /// <c>MainWindow.Patreon.cs</c> handler, which reads and writes
    /// <c>App.Settings.Current.CompanionPrompt</c>. That host does not exist in this head, so each
    /// handler keeps only the half that touches the view (showing the permissions panel, writing
    /// the slider's percentage) and stubs the persistence. <see cref="ApplyTierGate"/> fails
    /// closed, exactly as <c>TierGate.RequiresLab</c> does with no Patreon service alive.</para>
    ///
    /// <para>Motion budget: zero.</para>
    /// </summary>
    public partial class AiPermissionsGrid : UserControl
    {
        /// <summary>Opacity of the effects half while it is behind the lockband.</summary>
        private const double LockedOpacity = 0.32;

        public AiPermissionsGrid()
        {
            InitializeComponent();
            Loaded += (_, _) => ApplyTierGate();
        }

        /// <summary>Whether this account clears the Tier 2 bar.</summary>
        // ponytail: needs PatreonService.HasLabAccess, wired when it moves to Core. Fails closed.
        internal bool IsLabEntitled => false;

        /// <summary>
        /// Paints the Tier 2 verdict onto the effects half: disabled and dimmed under a violet
        /// lockband when the account does not clear the bar, untouched when it does. Deliberately
        /// does NOT touch the memory half - chat memory is Tier 1.
        /// </summary>
        internal void ApplyTierGate()
        {
            try
            {
                var allowed = IsLabEntitled;
                // Same sentence TierGate.RequiresLab formats, so this band and the toast agree.
                var reason = allowed ? string.Empty
                    : Loc.GetF("tiergate_denied_lab", Loc.Get("lab_ai_effects_memory_title"));

                EffectsGateHost.IsEnabled = allowed;
                EffectsGateHost.Opacity = allowed ? 1.0 : LockedOpacity;
                EffectsLockBand.IsVisible = !allowed;
                TxtEffectsLockCopy.Text = reason;
            }
            catch (Exception ex)
            {
                // A lockband that throws would take the whole Companion tab down with it.
                Log.Debug("AiPermissionsGrid.ApplyTierGate failed: {E}", ex.Message);
            }
        }

        private void BtnEffectsLockCta_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs MainWindow.ShowAppInfoPopup, wired when the shell has one
        }

        private void BtnClearChatMemory_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs CompanionBrain.ForgetConversation + a confirm dialog, wired when it moves to Core
        }

        private void BtnLabEffectsSetupLocal_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs the Engine Room deep link + LocalAiSetupWizard, wired when they are ported
        }

        private void ChkAllowEffect_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs CompanionPromptSettings.AllowAi<Tag>, wired when settings move to Core
        }

        private void ChkCapEffects_Changed(object? sender, RoutedEventArgs e)
        {
            var on = ChkCapEffects.IsChecked == true;
            if (on && !IsLabEntitled)
            {
                // A refusal must not write: put the switch back and leave the panel closed.
                ChkCapEffects.IsChecked = false;
                return;
            }
            EffectPermsPanel.IsVisible = on;
            // ponytail: needs CompanionPromptSettings.AllowAiToControlEffects, wired when settings move to Core
        }

        private void ChkChatMemoryEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs CompanionPromptSettings.ChatMemoryEnabled + the brain wipe, wired when they move to Core
        }

        private void SliderMaxHapticIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtMaxHapticIntensity.Text = $"{(int)(e.NewValue * 100)}%";
            // ponytail: needs CompanionPromptSettings.MaxAiHapticIntensity, wired when settings move to Core
        }
    }
}
