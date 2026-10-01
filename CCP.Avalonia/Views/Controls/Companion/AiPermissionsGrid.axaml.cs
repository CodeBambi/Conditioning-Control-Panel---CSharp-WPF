using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
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
    /// <para><b>One write is deliberately NOT restored.</b> WPF's <c>UpdateUnlockablesVisibility</c>
    /// force-clear (which unticks <c>ChkCapEffects</c> once an account has lapsed) is not ported:
    /// it is a WRITE decided by an entitlement this head cannot see, so here it would silently
    /// destroy a paid-up user's setting on every launch. Seeding shows the stored truth; only a
    /// head that can read Patreon may repair it.</para>
    ///
    /// <para><see cref="ApplyTierGate"/> and the master toggle's refusal now call the real
    /// <c>TierGate</c> (CCP.Core/Services/TierGate.cs), not a local stand-in. It still fails
    /// closed here - this head seeds no <c>CoreEntitlement</c> providers, so there is no account
    /// to read - but the band copy and the refusal are the shipped ones, and the day a head seeds
    /// an entitlement this control opens with no further edit.</para>
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
            Helpers.ModArt.BindFeaturePlates(this, "features/lab_aimemory_hero.png", null, LabAiMemoryHeroArt);
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

                // The EFFECTIVE value (WPF MainWindow.Patreon.cs:1964, #1307): unticked while Lab
                // access is not live, without touching the saved choice.
                var effectsOn = global::ConditioningControlPanel.Services.Companion.AiEffectControlGate.IsOnNow;
                ChkCapEffects.IsChecked = effectsOn;
                EffectPermsPanel.IsVisible = effectsOn;

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

        /// <summary>
        /// Paints the Tier 2 verdict onto the effects half: disabled and dimmed under a violet
        /// lockband when the account does not clear the bar, untouched when it does. Deliberately
        /// does NOT touch the memory half - chat memory is Tier 1.
        ///
        /// <para>The verdict is <see cref="TierGate.RequiresLab"/> itself now (it is in Core, over
        /// the <c>CoreEntitlement</c> seam), band copy included - so this band and the refusal
        /// toast cannot say different things. Unseeded on this head, so still closed.</para>
        /// </summary>
        internal void ApplyTierGate()
        {
            try
            {
                var verdict = TierGate.RequiresLab(Loc.Get("lab_ai_effects_memory_title"));
                var allowed = verdict.Allowed;

                EffectsGateHost.IsEnabled = allowed;
                EffectsGateHost.Opacity = allowed ? 1.0 : LockedOpacity;
                EffectsLockBand.IsVisible = !allowed;
                TxtEffectsLockCopy.Text = verdict.Reason;
            }
            catch (Exception ex)
            {
                // A lockband that throws would take the whole Companion tab down with it.
                Log.Debug("AiPermissionsGrid.ApplyTierGate failed: {E}", ex.Message);
            }
        }

        private void BtnEffectsLockCta_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnGateUnlock_Click(sender, e);

        /// <summary>WPF MainWindow.Patreon.cs BtnClearChatMemory_Click: confirm, then the brain forgets the
        /// conversation (session.json + live turn log). ponytail: no AiServiceStrategy.ClearLocalHistory
        /// or AiLiveActions feed on this head yet (local providers are slice 7).</summary>
        private async void BtnClearChatMemory_Click(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            if (!await Dialogs.MessageDialog.ConfirmAsync(owner, Loc.Get("btn_forget_everything"),
                    Loc.Get("dialog_forget_everything_prompt"))) return;
            try
            {
                App.Brain?.ForgetConversation();
                await Dialogs.MessageDialog.ShowAsync(owner, Loc.Get("btn_forget_everything"),
                    Loc.Get("dialog_forget_everything_done"));
            }
            catch (Exception ex) { Log.Warning(ex, "BtnClearChatMemory_Click failed"); }
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
            // WPF's own guard, verbatim (MainWindow.Patreon.cs:1662). DemandLab both decides and
            // tells; with no entitlement seeded on this head it denies and only logs, which is a
            // quieter refusal than Windows gives but the same refusal.
            if (on && !TierGate.DemandLab(Loc.Get("lab_ai_effects_memory_title")))
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
            if (p == null) return;
            p.AllowAiToControlEffects = on;
            if (!on) ConditioningControlPanel.Services.Commands.AiCommandService.CancelAll();   // pending follow-ups die with the consent
            CoreSettings.Save();
        }

        /// <summary>
        /// WPF MainWindow.Patreon.cs ChkChatMemoryEnabled_Changed: persist, and turning memory OFF also
        /// erases what is already saved (<c>App.Brain.ForgetConversation</c>: session.json and the live
        /// turn log), not merely stops persisting new turns.
        /// </summary>
        private void ChkChatMemoryEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var p = CoreSettings.Current.CompanionPrompt;
            if (p == null) return;
            var on = ChkChatMemoryEnabled.IsChecked == true;
            if (p.ChatMemoryEnabled == on) return;
            p.ChatMemoryEnabled = on;
            CoreSettings.Save();
            if (on) return;
            try { App.Brain?.ForgetConversation(); }
            catch (Exception ex) { Log.Warning(ex, "ChkChatMemoryEnabled_Changed: brain wipe failed"); }
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
