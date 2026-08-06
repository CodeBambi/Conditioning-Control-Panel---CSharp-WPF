using System;
using System.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Companion tab's write surface after the "Her Room" redesign.
    ///
    /// <para>The old tab was driven by poking named controls: <c>ChkSlutMode.IsChecked = …</c>, and
    /// a <c>Checked</c> handler that read the checkbox back out. Those controls are gone, so the
    /// operations they carried live here as plain methods that take a value — which is what they
    /// always were underneath. Every one of them keeps the side effects the handler had (the
    /// content gates, the service start/stop, the settings save), because the redesign moves where
    /// a thing is ASKED FOR, never what it does.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The room's viewmodel, or null before the tab has been constructed.</summary>
        private CompanionRoomRuntimeVm? CompanionRoom => CompanionTab?.Vm;

        /// <summary>Re-reads the whole Companion page from settings and services.</summary>
        internal void SyncCompanionRoom() => CompanionRoom?.Sync();

        // =====================================================================================
        //  quick actions (Z1)
        // =====================================================================================

        /// <summary>
        /// Show/hide the avatar tube. The old <c>ChkAvatarEnabled_Changed</c> body, with the
        /// checkbox read replaced by the caller's value.
        /// </summary>
        internal void SetAvatarEnabled(bool enabled)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;

            App.Settings.Current.AvatarEnabled = enabled;
            if (enabled) ShowAvatarTube();
            else HideAvatarTube();
            App.Settings.Save();
        }

        /// <summary>Mute/unmute her voice. The old <c>ChkMuteAvatar_Changed</c> body.</summary>
        internal void SetAvatarMuted(bool muted)
        {
            if (_isLoading) return;
            _avatarTubeWindow?.SetMuteAvatar(muted);
            if (App.Settings?.Current == null) return;
            App.Settings.Current.AvatarMuted = muted;
            App.Settings.Save();
        }

        // =====================================================================================
        //  personality (Z4)
        // =====================================================================================

        /// <summary>
        /// Slut Mode, with the CCBill explicit-content acknowledgement gate intact.
        ///
        /// <para>Returns the value that actually took effect — the acknowledgement dialog is
        /// cancellable, and the caller has to be able to show what happened rather than what was
        /// clicked. The old handler expressed the same thing by reverting the checkbox.</para>
        /// </summary>
        internal bool SetSlutMode(bool enabled)
        {
            if (_isLoading) return App.Settings?.Current?.SlutModeEnabled == true;

            var s = App.Settings?.Current;
            if (s == null) return false;
            if (s.SlutModeEnabled == enabled) return enabled;

            if (enabled)
            {
                var activePreset = App.Personality?.GetActivePreset();
                if (Services.ExplicitContentGate.RequiresAcknowledgement(activePreset, slutModeOn: true) &&
                    !Services.ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt))
                {
                    var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                    if (dlg.ShowDialog() != true) return s.SlutModeEnabled;
                    Services.ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
                }
            }

            s.SlutModeEnabled = enabled;
            App.Settings?.Save();
            return enabled;
        }

        /// <summary>
        /// Activates a personality preset from Z4's chip row, behind the same explicit-content gate
        /// the avatar's right-click menu uses.
        ///
        /// <para>The gate is the reason this is a MainWindow method and not two lines in the
        /// viewmodel: a second activation path that skipped the acknowledgement would be a
        /// compliance hole rather than a shortcut.</para>
        /// </summary>
        internal bool ActivatePersonalityPreset(string? presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return false;

            var preset = App.Personality?.GetPresetById(presetId);
            if (preset == null) return false;

            var slutModeOn = App.Settings?.Current?.SlutModeEnabled == true;
            if (Services.ExplicitContentGate.RequiresAcknowledgement(preset, slutModeOn))
            {
                var promptSettings = App.Settings?.Current?.CompanionPrompt;
                if (!Services.ExplicitContentGate.IsAlreadyAcknowledged(promptSettings))
                {
                    var dlg = new ExplicitContentAcknowledgementDialog { Owner = this };
                    if (dlg.ShowDialog() != true) return false;
                    if (promptSettings != null)
                    {
                        Services.ExplicitContentGate.MarkAcknowledged(promptSettings);
                        App.Settings?.Save();
                    }
                }
            }

            if (App.Personality?.SetActivePreset(presetId) != true) return false;

            var shownName = App.Mods?.GetPersonalityDisplayName(preset.Name) ?? preset.Name;
            App.Logger?.Information("Companion room: personality preset switched to {Name}", shownName);
            return true;
        }

        // =====================================================================================
        //  awareness (Z5)
        // =====================================================================================

        /// <summary>
        /// The awareness capability, driven by Z5's dial instead of the old checkbox.
        ///
        /// <para>Body unchanged from <c>ChkAwarenessMode_Changed</c>, including the auto-consent
        /// (turning it on IS the consent, which is how the toggle always behaved) and the
        /// service start/stop. The cooldown panel it used to reveal now lives in the Workshop and
        /// still hides while her eyes are closed.</para>
        /// </summary>
        internal void SetAwarenessEnabled(bool enabled)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;

            CompanionTab.AwarenessSettingsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

            App.Settings.Current.AwarenessModeEnabled = enabled;
            App.Settings.Current.AwarenessConsentGiven = enabled;
            App.Settings.Save();

            if (enabled)
            {
                App.WindowAwareness?.Start();
                App.Logger?.Information("Awareness Mode enabled via the awareness dial");
            }
            else
            {
                App.WindowAwareness?.Stop();
                App.Logger?.Information("Awareness Mode disabled via the awareness dial");
            }

            CompanionRoom?.SyncHero();
        }

        // =====================================================================================
        //  the engine room (Z7)
        // =====================================================================================

        /// <summary>
        /// The provider segment. Replaces the four <c>RadioAi*_Checked</c> handlers with one call
        /// that writes the same settings and has the same side effects.
        /// </summary>
        internal void SetAiProviderMode(CompanionProviderMode mode)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s?.CompanionPrompt == null) return;

            var (enabled, provider) = EngineRoomRuntimeVm.SettingsFor(mode);
            s.AiChatEnabled = enabled;
            if (enabled) s.CompanionPrompt.AiProvider = provider;
            App.Settings?.Save();

            // Off drops any stale Live Actions — with the brain off nothing populates that feed.
            if (!enabled) App.AiLiveActions?.Clear();

            App.Logger?.Information("Companion room: provider set to {Mode}", mode);
            OnCompanionProviderSelected(mode);
            CompanionRoom?.SyncBrain();
        }

        /// <summary>
        /// Stores the BYO API key. One-way by design: the box hands the typed value over and
        /// nothing ever reads it back out (see the Engine Room's PasswordBox).
        /// </summary>
        internal void SetCustomApiKey(string? key)
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null) return;
            // DPAPI-protected on the way in, exactly as TxtOpenAiApiKey_PasswordChanged did. The
            // settings file must never hold a BYO key in the clear.
            s.OpenAiCompatibleApiKey = Services.SecureStringHelper.Protect(key ?? string.Empty);
            App.Settings?.Save();
        }

        /// <summary>
        /// The daily request limit, which used to be a bare TextBox on the AI Brain card. Same
        /// parse rules: blank or unparseable means "no limit" (0), negatives are clamped.
        /// </summary>
        internal void PromptForDailyRequestLimit()
        {
            var s = App.Settings?.Current?.CompanionPrompt;
            if (s == null) return;

            var current = s.DailyRequestLimit > 0 ? s.DailyRequestLimit.ToString() : string.Empty;
            var dialog = new InputDialog(
                Loc.Get("label_daily_request_limit"),
                Loc.Get("label_daily_request_limit_hint"),
                current)
            { Owner = this };

            if (dialog.ShowDialog() != true) return;

            var text = (dialog.ResultText ?? string.Empty).Trim();
            s.DailyRequestLimit = int.TryParse(text, out var value) && value > 0 ? value : 0;
            App.Settings?.Save();
        }

        /// <summary>
        /// Cloud has no local endpoint to probe, so "Test connection" reports the client's own view
        /// of availability rather than pretending to have made a round trip. It is the same fact the
        /// status line carries; the button exists because the segment has one for every provider.
        /// </summary>
        internal void TestCloudConnection()
        {
            var vm = CompanionRoom?.EngineVm;
            if (vm == null) return;

            bool available = App.Ai?.IsAvailable == true;
            vm.SetStatus(available
                ? Loc.Get("label_status_connected")
                : Loc.Get("label_login_required"), available);
        }

        /// <summary>
        /// "Clear conversation" — the legacy <c>BtnResetCompanionMemory</c> scope, now in the Engine
        /// Room. Doc 01 §2.4 gives the diary's "Forget everything" the all-or-nothing wipe; this one
        /// drops the thread and leaves the durable memory alone, which is what the old button's own
        /// copy promised ("she'll start fresh on the next message").
        /// </summary>
        internal void ClearCompanionConversation()
        {
            try { App.Bark?.NotifyUiAction("reset_memory"); } catch { }

            // Three loc keys joined here rather than one multi-paragraph string: the house rule
            // bans literal line breaks in language files, and a translated value carrying its own
            // paragraph breaks is the exact shape that broke eight of the nine files once already.
            var message = string.Join(Environment.NewLine + Environment.NewLine,
                Loc.Get("companion_engine_clear_conversation_confirm"),
                Loc.Get("companion_engine_clear_conversation_confirm_body"),
                Loc.Get("companion_engine_clear_conversation_confirm_warn"));

            var confirm = MessageBox.Show(
                this,
                message,
                Loc.Get("companion_engine_clear_conversation"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // The brain owns conversation state for every provider since Train 1 — clearing the
                // live turn log is the load-bearing half, because deleting session.json alone is
                // undone by the very next reply.
                App.Brain?.ForgetConversation();

                // Legacy local-Ollama history file, still owned by LocalAiService whenever
                // UseCompanionBrain=false.
                (App.Ai as Services.AIService.AiServiceStrategy)?.ClearLocalHistory();

                // And the on-screen bubble log, which is a separate store.
                _avatarTubeWindow?.ChatHistory.Clear();

                App.Logger?.Information("Companion conversation cleared from the Engine Room");
                CompanionRoom?.SyncBrain();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to clear companion conversation");
            }
        }
    }
}
