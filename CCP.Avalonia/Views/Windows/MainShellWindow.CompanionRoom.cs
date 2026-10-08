// PORTED from ConditioningControlPanel/MainWindow/MainWindow.CompanionRoom.cs (410 lines) - the
// Companion tab's write surface after the "Her Room" redesign, sorted member by member.
//
// Every method takes a VALUE rather than reading a control, exactly as on WPF: the redesign moved
// where a thing is ASKED FOR, never what it does.
//
// WHAT IS REAL HERE: the operations whose whole body is settings, the ported avatar tube or the
// ported consent dialog - the avatar's show/hide and mute, awareness on/off with its one-time
// plain-language consent (Views/Dialogs/AwarenessConsentDialog.EnsureConsentAsync), the v2
// upgrader prompt, the Workshop's intensity dial and the daily request limit
// (Views/Dialogs/InputDialog). Three are async where WPF blocked - Avalonia's ShowDialog is awaited.
//
// _roomLoading, not _isLoading: the class-wide field is listed in MainShellWindow.axaml.cs's
// dropped ledger and belongs to that file. A partial-class field is declared exactly once.
//
// EVERY `CompanionRoom?.Sync…()` LINE IS DROPPED, and that is the one thing to restore first:
// CompanionRoomRuntimeVm is at ConditioningControlPanel/Views/Controls/Companion/Runtime/
// CompanionRoomRuntimeVm.cs and its eight zone viewmodels are still WPF-side, so the room's
// dials do not re-read after a write yet. Nothing below depends on that to be CORRECT - the
// settings are written either way - only to be reflected.
//
// STILL HEAD-SIDE, each with the exact symbol and where it lives today:
//   CompanionRoom / SyncCompanionRoom - CompanionRoomRuntimeVm (path above). CompanionTabView on
//                            this head publishes no Vm, deliberately.
//   SetSlutMode / ActivatePersonalityPreset - PersonalityService.Shared, ExplicitContentGate and the
//                            acknowledgement dialog are all reachable now (the tube's Personality
//                            submenu uses them: AvatarTube/AvatarTubeWindow.ContentGates.cs). What is
//                            missing is a CALLER: the room's Z4 chip row / slut-mode cell are inert
//                            (see NO CALLER YET below). Port both WITH the gate when those cells land.
//   SetCustomApiKey        - Services.Auth.SecureStringHelper.Protect
//                            (ConditioningControlPanel/Services/Auth/SecureStringHelper.cs).
//                            CompanionPromptSettings.OpenAiCompatibleApiKey holds a DPAPI blob,
//                            so writing the typed key there on this head would put a BYO API key
//                            in settings.json IN THE CLEAR. CoreSecrets is the seam that fixes
//                            this (CoreSecrets.ApiKey), but nothing READS the key from it yet, so
//                            half the pair is worse than neither. Blocked on purpose.
//   the awareness observer - App.WindowAwareness.Start/Stop
//                            (ConditioningControlPanel/Services/Awareness/). Dropped from
//                            SetAwarenessEnabled and EnsureAwarenessV2Consent below: there is no
//                            observer on this head to start, so the setting write and the consent
//                            record are the whole of what can be honoured.
// RESTORED since (local-providers): ClearCompanionConversation below - Brain.ForgetThread and
// AiServiceStrategy.ClearLocalHistory are both Core now; the Engine Room drawer's EngineRoomVm calls it.
// SetAiProviderMode / TestCloudConnection live in that VM (EngineRoomDrawer.axaml.cs) over Core EngineRoomProviders.
// RESTORED since: the premium bar. TierGate is in Core (CCP.Core/Services/TierGate.cs) over the
// CoreEntitlement seam, so SetAwarenessEnabled's ON-edge gate is the real one. This head seeds no
// entitlement providers, so it denies - the same answer WPF gives with no Patreon service.
//   AwarenessSettingsPanel - the legacy cooldown sliders' host, inside
//                            CCP.Avalonia/Views/Controls/Companion/; CompanionTabView publishes
//                            none of the room's cells (MainShellWindow.CompanionTab.cs).
//
// NO CALLER YET: the room's dials are in Views/Controls/Companion/Runtime/*Cell, whose Avalonia
// twins carry inert handlers, and the tray/dashboard paths are in MainShellWindow.axaml.cs.

using System;
using System.Linq;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Seeding guard for the room's writes. See the header for why it is not
        /// _isLoading.</summary>
        private bool _roomLoading;

        /// <summary>Asked at most once per process - a declined dialog must not become a nag.</summary>
        private bool _awarenessV2ConsentAsked;

        // =====================================================================================
        //  quick actions (Z1)
        // =====================================================================================

        /// <summary>
        /// Show/hide the avatar tube. The old ChkAvatarEnabled_Changed body, with the checkbox
        /// read replaced by the caller's value.
        /// </summary>
        internal void SetAvatarEnabled(bool enabled)
        {
            if (_roomLoading) return;

            CoreSettings.Current.AvatarEnabled = enabled;
            if (enabled) ShowAvatarTube();
            else HideAvatarTube();
            CoreSettings.Save();
            // Every on/off button reads the hero's copy of the switch, wherever it was flipped from.
            SyncHero();
        }

        /// <summary>WPF CompanionRoom?.SyncHero(): re-read every seated hero card.</summary>
        internal void SyncHero()
        {
            foreach (var hero in this.GetLogicalDescendants().OfType<Controls.Companion.CompanionHeroCard>())
                hero.ViewModel?.Sync();
        }

        /// <summary>
        /// Mute/unmute her voice - the old ChkMuteAvatar_Changed body.
        ///
        /// <para>The setting is written and persisted; the live
        /// <c>AvatarTubeWindow.SetMuteAvatar</c> call is dropped because that method is in the
        /// tube's Speech.cs partial, which did not cross. Nothing on this head speaks yet, so the
        /// stored value is the whole of the behaviour there is to have - and it is the value the
        /// speech pipeline will read the moment it lands.</para>
        /// </summary>
        internal void SetAvatarMuted(bool muted)
        {
            if (_roomLoading) return;
            CoreSettings.Current.AvatarMuted = muted;
            CoreSettings.Save();
        }

        // =====================================================================================
        //  awareness (Z5)
        // =====================================================================================

        /// <summary>
        /// The awareness capability, driven by Z5's dial.
        ///
        /// <para><b>Consent is not silent.</b> Turning it on raises the one-time plain-language
        /// dialog; declining leaves awareness OFF and returns false, and the caller re-reads so
        /// the dial snaps back. This is the only place AwarenessModeEnabled and
        /// AwarenessConsentGiven are written outside settings load, which is what keeps "every
        /// entry point is gated" a fact.</para>
        ///
        /// <para>Returns whether awareness is enabled after the call. async, because the consent
        /// dialog is.</para>
        /// </summary>
        internal async System.Threading.Tasks.Task<bool> SetAwarenessEnabled(bool enabled)
        {
            var s = CoreSettings.Current;
            if (_roomLoading) return s.AwarenessModeEnabled;

            // Turning it OFF is deliberately never gated: whatever her account is doing, the
            // switch that closes her eyes is on screen and it works.
            //
            // The entitlement bar, ON edge only (#1047), restored from MainWindow.CompanionRoom.cs:210
            // now that TierGate is in Core. Asked BEFORE the consent dialog, as it is there and for
            // the reason given there: it is rude to walk someone through a privacy explanation for a
            // door that will not open. Unseeded on this head, so it currently denies - which is the
            // WPF behaviour with no Patreon service, not a new refusal.
            if (enabled && !Services.TierGate.DemandPremium(Loc.Get("tab_awareness"), "awareness"))
                return false;

            if (enabled && !await AwarenessConsentDialog.EnsureConsentAsync(this, s))
            {
                // Declined (or the dialog could not open). Nothing is written and nothing starts.
                return false;
            }

            s.AwarenessModeEnabled = enabled;
            s.AwarenessConsentGiven = enabled;
            CoreSettings.Save();

            // Opening her eyes lifts any running "pause for an hour": the user just said yes on
            // purpose.
            if (enabled) AwarenessPause.Resume();

            // WPF MainWindow.CompanionRoom.cs:236-245: the dial is the on/off call site.
            if (enabled) App.WindowAwareness.Start(); else App.WindowAwareness.Stop();

            Log.Information("Awareness Mode {State} via the awareness dial", enabled ? "enabled" : "disabled");
            return enabled;
        }

        /// <summary>
        /// Raises the v2 consent dialog for an UPGRADER: someone whose awareness was already on
        /// before this version, so the dial they would otherwise have to touch is already in the
        /// "on" position and <see cref="AwarenessConsentDialog.EnsureConsentAsync"/> would never
        /// run.
        ///
        /// <para>Declining leaves them on the legacy pipeline they already consented to, and is
        /// not asked again this session. Turning the dial off remains the way to say no
        /// permanently.</para>
        /// </summary>
        internal async void EnsureAwarenessV2Consent()
        {
            if (_awarenessV2ConsentAsked || _roomLoading) return;

            var s = CoreSettings.Current;
            if (!s.UseAwarenessV2) return;                 // kill switch down: v2 has nothing to ask
            if (s.AwarenessConsentShownV2) return;         // already accepted
            if (!s.AwarenessModeEnabled || !s.AwarenessConsentGiven) return;  // off: the dial asks

            _awarenessV2ConsentAsked = true;

            if (!await AwarenessConsentDialog.EnsureConsentAsync(this, s))
                Log.Information("Awareness: v2 consent declined by an upgrader — staying on the legacy pipeline");
        }

        /// <summary>
        /// The Workshop's intensity dial: how talkative she is, not whether she watches. Writing it
        /// never opens her eyes - that stays Z5's single gated decision - so this needs no consent
        /// gate of its own.
        /// </summary>
        internal void SetAwarenessIntensity(AwarenessIntensity intensity)
        {
            if (_roomLoading) return;
            var s = CoreSettings.Current;
            if (s.AwarenessIntensity == intensity) return;

            s.AwarenessIntensity = intensity;
            // The migration flag is what stops a later start-up from overwriting this choice.
            s.AwarenessIntensityMigrated = true;
            CoreSettings.Save();

            Log.Information("Awareness intensity set to {Intensity}", intensity);
        }

        // =====================================================================================
        //  the engine room (Z7)
        // =====================================================================================

        /// <summary>
        /// The daily request limit, which used to be a bare TextBox on the AI Brain card. Same
        /// parse rules: blank or unparseable means "no limit" (0), negatives are clamped by the
        /// same test. async because Avalonia's ShowDialog is.
        /// </summary>
        internal async System.Threading.Tasks.Task PromptForDailyRequestLimit()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s == null) return;

            var current = s.DailyRequestLimit > 0 ? s.DailyRequestLimit.ToString() : string.Empty;
            var dialog = new InputDialog(
                Loc.Get("label_daily_request_limit"),
                Loc.Get("label_daily_request_limit_hint"),
                current);

            if (await dialog.ShowDialogSafe<bool>(this) != true) return;

            var text = (dialog.ResultText ?? string.Empty).Trim();
            s.DailyRequestLimit = int.TryParse(text, out var value) && value > 0 ? value : 0;
            CoreSettings.Save();
        }

        /// <summary>
        /// "Clear conversation" (WPF MainWindow.CompanionRoom.cs:353): drops the thread, leaves what she
        /// knows. ForgetThread, not ForgetConversation, for the reason WPF gives (pinned and Boundary facts).
        /// The bark echo is not here: this head has no bark engine (CoreBark is a doorbell).
        /// </summary>
        internal async System.Threading.Tasks.Task ClearCompanionConversationAsync()
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine,
                Loc.Get("companion_engine_clear_conversation_confirm"),
                Loc.Get("companion_engine_clear_conversation_confirm_body"),
                Loc.Get("companion_engine_clear_conversation_confirm_warn"));
            if (!await MessageDialog.ConfirmAsync(this, Loc.Get("companion_engine_clear_conversation"), message,
                    defaultToCancel: true)) return;
            ClearCompanionConversationConfirmed();
        }

        /// <summary>The confirmed half, separate so a headless test drives exactly what the button does.</summary>
        internal void ClearCompanionConversationConfirmed()
        {
            try
            {
                App.Brain?.ForgetThread();
                // Backstop for the no-brain case: the legacy local transcript must not survive the button.
                if (App.Brain == null)
                    (App.Ai as ConditioningControlPanel.Services.AIService.AiServiceStrategy)?.ClearLocalHistory();
                _avatarTubeWindow?.ChatHistory.Clear();
                Log.Information("Companion conversation cleared from the Engine Room");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clear companion conversation");
            }
        }
    }
}
