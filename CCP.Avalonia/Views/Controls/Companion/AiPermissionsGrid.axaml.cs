using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    /// <summary>
    /// "What she's allowed to do" — the single AI-permissions surface, ported from the WPF head.
    ///
    /// <para>On WPF every control here is a one-line shim to the identically named
    /// <c>MainWindow.Patreon.cs</c> handler, which reads and writes
    /// <c>App.Settings.Current.CompanionPrompt</c>. <c>CompanionPromptSettings</c> is in Core now
    /// (CCP.Core/Models/CompanionPromptSettings.cs), so the effect permissions, the master switch
    /// and the haptic cap are restored against <see cref="CoreSettings"/>: seeded under
    /// <c>_isLoading</c> (WPF's <c>SyncLabEffectPermsUI</c>) and compared before writing, because
    /// Avalonia raises <c>IsCheckedChanged</c> on a programmatic set too.</para>
    ///
    /// <para><b>Two writes are deliberately NOT restored.</b> Chat memory is refused - see
    /// <see cref="ChkChatMemoryEnabled_Changed"/>. And WPF's <c>UpdateUnlockablesVisibility</c>
    /// force-clear (which unticks <c>ChkCapEffects</c> once an account has lapsed) is not ported:
    /// it is a WRITE decided by an entitlement this head cannot see, so here it would silently
    /// destroy a paid-up user's setting on every launch. Seeding shows the stored truth; only a
    /// head that can read Patreon may repair it.</para>
    ///
    /// <para><see cref="ApplyTierGate"/> still fails closed, exactly as <c>TierGate.RequiresLab</c>
    /// does with no Patreon service alive, so the effect half stays disabled on this head and the
    /// restored writes below are reachable only once an entitlement is seeded.</para>
    ///
    /// <para>Motion budget: zero.</para>
    /// </summary>
    public partial class AiPermissionsGrid : UserControl
    {
        /// <summary>Opacity of the effects half while it is behind the lockband.</summary>
        private const double LockedOpacity = 0.32;

        /// <summary>Raised while the seed writes the controls, so an echo is not a user edit.</summary>
        private bool _isLoading = true;

        public AiPermissionsGrid()
        {
            InitializeComponent();
            SyncFromSettings();
            Loaded += (_, _) => ApplyTierGate();
        }

        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnCurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnCurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        // A cloud restore or a factory reset swaps the instance; repaint from it, on the UI thread.
        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        /// <summary>
        /// WPF's <c>MainWindow.SyncLabEffectPermsUI</c>: paint every control from the stored
        /// permissions. Without this the grid showed its markup defaults, which is a control
        /// lying about what the companion is actually allowed to do.
        /// </summary>
        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var p = CoreSettings.Current.CompanionPrompt;
                if (p == null) return;

                ChkCapEffects.IsChecked = p.AllowAiToControlEffects;
                EffectPermsPanel.IsVisible = p.AllowAiToControlEffects;

                ChkAllowFlash.IsChecked = p.AllowAiFlash;
                ChkAllowVideo.IsChecked = p.AllowAiVideo;
                ChkAllowAudio.IsChecked = p.AllowAiAudio;
                ChkAllowBubbles.IsChecked = p.AllowAiBubbles;
                ChkAllowSubliminal.IsChecked = p.AllowAiSubliminal;
                ChkAllowOverlay.IsChecked = p.AllowAiOverlay;
                ChkAllowLockCard.IsChecked = p.AllowAiLockCard;
                ChkAllowBounce.IsChecked = p.AllowAiBounce;
                ChkAllowHaptic.IsChecked = p.AllowAiHaptic;
                ChkAllowGetBackToMe.IsChecked = p.AllowAiGetBackToMe;

                SliderMaxHapticIntensity.Value = p.MaxAiHapticIntensity;
                TxtMaxHapticIntensity.Text = $"{(int)(p.MaxAiHapticIntensity * 100)}%";

                ChkChatMemoryEnabled.IsChecked = p.ChatMemoryEnabled;
            }
            catch (Exception ex)
            {
                Log.Debug("AiPermissionsGrid.SyncFromSettings failed: {E}", ex.Message);
            }
            finally
            {
                _isLoading = false;
            }
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
            // ponytail: needs a forget seam on CoreAi (CCP.Core/CoreAi.cs exposes only
            // IsAvailable). The confirm half is ready - CCP.Avalonia/Views/Dialogs/MessageDialog -
            // but the wipe half has nothing to call, so the button must stay inert.
        }

        private void BtnLabEffectsSetupLocal_Click(object? sender, RoutedEventArgs e)
        {
            // ponytail: needs the Engine Room deep link + LocalAiSetupWizard, wired when they are ported
        }

        /// <summary>One handler for the ten effect boxes; the Tag names the permission, as on WPF.</summary>
        private void ChkAllowEffect_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (sender is not CheckBox cb) return;
            var p = CoreSettings.Current.CompanionPrompt;
            if (p == null) return;
            var on = cb.IsChecked == true;
            switch (cb.Tag as string)
            {
                case "Flash":       if (p.AllowAiFlash == on) return; p.AllowAiFlash = on; break;
                case "Video":       if (p.AllowAiVideo == on) return; p.AllowAiVideo = on; break;
                case "Audio":       if (p.AllowAiAudio == on) return; p.AllowAiAudio = on; break;
                case "Bubbles":     if (p.AllowAiBubbles == on) return; p.AllowAiBubbles = on; break;
                case "Subliminal":  if (p.AllowAiSubliminal == on) return; p.AllowAiSubliminal = on; break;
                case "Overlay":     if (p.AllowAiOverlay == on) return; p.AllowAiOverlay = on; break;
                case "LockCard":    if (p.AllowAiLockCard == on) return; p.AllowAiLockCard = on; break;
                case "Bounce":      if (p.AllowAiBounce == on) return; p.AllowAiBounce = on; break;
                case "Haptic":      if (p.AllowAiHaptic == on) return; p.AllowAiHaptic = on; break;
                case "GetBackToMe": if (p.AllowAiGetBackToMe == on) return; p.AllowAiGetBackToMe = on; break;
                default: return;
            }
            CoreSettings.Save();
        }

        private void ChkCapEffects_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var on = ChkCapEffects.IsChecked == true;
            if (on && !IsLabEntitled)
            {
                // A refusal must not write: put the switch back and leave the panel closed. The
                // guard is raised around the snap-back because Avalonia re-enters this handler on
                // a programmatic set, and that re-entry would otherwise persist false.
                _isLoading = true;
                try { ChkCapEffects.IsChecked = false; }
                finally { _isLoading = false; }
                return;
            }
            EffectPermsPanel.IsVisible = on;

            var p = CoreSettings.Current.CompanionPrompt;
            if (p == null || p.AllowAiToControlEffects == on) return;
            p.AllowAiToControlEffects = on;
            CoreSettings.Save();
        }

        /// <summary>
        /// REFUSED, not stubbed. The box is SEEDED from settings so it tells the truth, but the
        /// write is snapped back, because unticking this on WPF is a PROMISE to erase what is
        /// already on disk (<c>App.Brain.ForgetConversation</c> plus
        /// <c>AiServiceStrategy.ClearLocalHistory</c> - companion/session.json and the live turn
        /// log), not merely to stop persisting new turns. <see cref="CoreAi"/> exposes only
        /// <c>IsAvailable</c>; there is no forget seam, so persisting "memory off" here would
        /// leave a switch reading "erased" over a transcript that is still there. Restore the
        /// write the day CoreAi grows a forget action - together with it, never before.
        /// </summary>
        private void ChkChatMemoryEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var p = CoreSettings.Current.CompanionPrompt;
            if (p == null) return;
            if ((ChkChatMemoryEnabled.IsChecked == true) == p.ChatMemoryEnabled) return;
            _isLoading = true;
            try { ChkChatMemoryEnabled.IsChecked = p.ChatMemoryEnabled; }
            finally { _isLoading = false; }
        }

        private void SliderMaxHapticIntensity_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
        {
            TxtMaxHapticIntensity.Text = $"{(int)(e.NewValue * 100)}%";
            if (_isLoading) return;
            var p = CoreSettings.Current.CompanionPrompt;
            if (p == null || Math.Abs(p.MaxAiHapticIntensity - e.NewValue) < 0.0001) return;
            p.MaxAiHapticIntensity = e.NewValue;
            CoreSettings.Save();   // the debounced save: a slider fires per tick
        }
    }
}
