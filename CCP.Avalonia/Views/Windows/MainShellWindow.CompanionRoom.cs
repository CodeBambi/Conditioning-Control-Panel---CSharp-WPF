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
//   SetSlutMode            - App.Personality.GetActivePreset
//                            (ConditioningControlPanel/Services/Companion/PersonalityService.cs).
//                            ExplicitContentGate IS in Core and the acknowledgement dialog IS on
//                            this head, but RequiresAcknowledgement(null, true) returns FALSE - so
//                            shipping it without the preset lookup would be a SKIPPED gate, not a
//                            degraded one. Blocked whole, deliberately.
//   ActivatePersonalityPreset - the same PersonalityService, plus GetPersonalityDisplayName.
//   SetAiProviderMode      - EngineRoomRuntimeVm.SettingsFor / ClearsLiveActions
//                            (…/Views/Controls/Companion/Runtime/EngineRoomRuntimeVm.cs) and
//                            App.AiLiveActions.
//   SetCustomApiKey        - Services.Auth.SecureStringHelper.Protect
//                            (ConditioningControlPanel/Services/Auth/SecureStringHelper.cs).
//                            CompanionPromptSettings.OpenAiCompatibleApiKey holds a DPAPI blob,
//                            so writing the typed key there on this head would put a BYO API key
//                            in settings.json IN THE CLEAR. CoreSecrets is the seam that fixes
//                            this (CoreSecrets.ApiKey), but nothing READS the key from it yet, so
//                            half the pair is worse than neither. Blocked on purpose.
//   TestCloudConnection    - EngineRoomRuntimeVm.SetStatus. CoreAi.IsAvailable already answers
//                            the fact it reports; only the status line to write it to is missing.
//   ClearCompanionConversation - App.Brain.ForgetThread
//                            (ConditioningControlPanel/Services/Companion/Brain/) and
//                            AiServiceStrategy.ClearLocalHistory. The confirm dialog is portable;
//                            a confirmed button that then forgets nothing is not.
//   the awareness observer - App.WindowAwareness.Start/Stop
//                            (ConditioningControlPanel/Services/Awareness/). Dropped from
//                            SetAwarenessEnabled and EnsureAwarenessV2Consent below: there is no
//                            observer on this head to start, so the setting write and the consent
//                            record are the whole of what can be honoured.
//   the premium bar        - Services.TierGate.DemandPremium
//                            (ConditioningControlPanel/Services/TierGate.cs), the ON-edge gate in
//                            SetAwarenessEnabled.
//   AwarenessSettingsPanel - the legacy cooldown sliders' host, inside
//                            CCP.Avalonia/Views/Controls/Companion/; CompanionTabView publishes
//                            none of the room's cells (MainShellWindow.CompanionTab.cs).
//
// NO CALLER YET: the room's dials are in Views/Controls/Companion/Runtime/*Cell, whose Avalonia
// twins carry inert handlers, and the tray/dashboard paths are in MainShellWindow.axaml.cs.

using System;
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
        internal async void PromptForDailyRequestLimit()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s == null) return;

            var current = s.DailyRequestLimit > 0 ? s.DailyRequestLimit.ToString() : string.Empty;
            var dialog = new InputDialog(
                Loc.Get("label_daily_request_limit"),
                Loc.Get("label_daily_request_limit_hint"),
                current);

            if (await dialog.ShowDialog<bool>(this) != true) return;

            var text = (dialog.ResultText ?? string.Empty).Trim();
            s.DailyRequestLimit = int.TryParse(text, out var value) && value > 0 ? value : 0;
            CoreSettings.Save();
        }
    }
}
