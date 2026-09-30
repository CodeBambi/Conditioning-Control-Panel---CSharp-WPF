// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Autonomy.cs (728 lines) plus
// the head half of Services/AutonomyService.cs. The decisions (entitlement, cooldown, cadence,
// mood/intensity pick, announce delay, stop generation) are Core's AutonomyScheduler; this file
// seeds it with what this head can actually do and wires start/stop, panic and the state hero.
//
// Performed here: Flash, Subliminal, LockCard, Video (mandatory), Bubbles and Bouncing Text as
// 30 s pulses. Never picked here (CanPerform false, so ticking them does nothing):
// ponytail: Comment (AI/phrase comment), MindWipe (CoreMindWipe unseeded on this head), Pink Filter
// pulse (opacity boost + overlay ownership, #1180), Web Video (no browser media service), Wallpaper
// (Win32 WallpaperService), Spoken Mantra (MantraVoiceService not ported).
// Also not here: the TakeoverAnnouncerOverlay banner (overlay-takeover-announcer), the avatar
// countdown bar, the announcement's event audio (CompanionPhraseService), the diagnostic Test
// dialogs and Force Start (debug). Voice/PTT/wake word: MainShellWindow.VoiceCommands.cs.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>This window's Takeover. Per window so a test's shell never shares one.</summary>
        internal readonly AutonomyScheduler Autonomy = new();

        private bool _bubblesPulse, _bouncingPulse;
        private int _pulseGen;

        private bool _autonomyHooked;

        /// <summary>Seeds the scheduler once (constructor). Nothing starts here.</summary>
        private void HookAutonomy()
        {
            if (_autonomyHooked) return;
            _autonomyHooked = true;
            Autonomy.CanPerform = CanPerformAutonomy;
            // WPF CanTakeAction: never over a fullscreen interaction (InteractionQueue.IsBusy).
            Autonomy.IsBusy = () => CoreEngine.Video?.IsPlaying == true || LockCardWindow.IsAnyOpen()
                                    || BubbleCountWindow.IsAnyOpen();
            Autonomy.Perform = (a, _) => Dispatcher.UIThread.Post(() => PerformAutonomy(a));
            Autonomy.EnabledChanged += (_, on) => Dispatcher.UIThread.Post(() =>
            {
                if (!on) CancelAutonomyPulses();
                SetTakeoverActiveUi(on);
                UpdateAutonomyButtonState(on);
            });
            // WPF AnnounceAction: the tube says it (text only; no event audio here).
            Autonomy.AnnouncementMade += (_, phrase) => Dispatcher.UIThread.Post(() =>
                _avatarTubeWindow?.GigglePriority(phrase, false, aiGenerated: false));
            Closed += (_, _) => { Autonomy.Stop(); StopVoiceInput(); };
            Opened += (_, _) => { ResumeAutonomyOnStartup(); RefreshVoiceInputModes(); };
        }

        private static bool CanPerformAutonomy(AutonomyActionType a) => a switch
        {
            AutonomyActionType.Flash => CoreFlash.ShowProvider != null,
            AutonomyActionType.Subliminal => CoreSubliminal.ShowProvider != null,
            AutonomyActionType.LockCard => CoreLockCard.ShowHandler != null,
            AutonomyActionType.Video => CoreEngine.Video != null,
            AutonomyActionType.StartBubbles => CoreBubbles.StartAction != null,
            AutonomyActionType.BouncingText => CoreBouncingText.StartAction != null,
            _ => false,
        };

        /// <summary>WPF PerformAction for the actions this head has. UI thread.</summary>
        private void PerformAutonomy(AutonomyActionType a)
        {
            if (!Autonomy.IsEnabled) return;   // stopped while the post was queued
            switch (a)
            {
                case AutonomyActionType.Flash: CoreFlash.ShowProvider?.Invoke(); break;
                case AutonomyActionType.Subliminal:
                    if (CoreSubliminal.PickPhrase() is { } phrase) CoreSubliminal.ShowProvider?.Invoke(phrase);
                    break;
                case AutonomyActionType.LockCard: CoreLockCard.Show(); break;
                // No strict override: a Takeover video follows the global StrictLockEnabled (WPF TriggerVideoSafely).
                case AutonomyActionType.Video: CoreEngine.Video?.Trigger(); break;
                case AutonomyActionType.StartBubbles:
                    if (_bubblesPulse || BubbleOverlay.IsRunning) break;
                    _bubblesPulse = true;
                    CoreBubbles.Start();
                    EndPulseAfter30s(() => { if (_bubblesPulse) { _bubblesPulse = false; CoreBubbles.Stop(); } });
                    break;
                case AutonomyActionType.BouncingText:
                    if (_bouncingPulse || BouncingTextOverlay.IsRunning) break;
                    _bouncingPulse = true;
                    CoreBouncingText.Start();
                    EndPulseAfter30s(() => { if (_bouncingPulse) { _bouncingPulse = false; CoreBouncingText.Stop(); } });
                    break;
            }
        }

        private void EndPulseAfter30s(Action end)
        {
            var gen = _pulseGen;
            DispatcherTimer.RunOnce(() => { if (gen == _pulseGen) end(); }, TimeSpan.FromSeconds(30));
        }

        /// <summary>WPF CancelActivePulses: stop only what a Takeover pulse started.</summary>
        internal void CancelAutonomyPulses()
        {
            _pulseGen++;
            if (_bubblesPulse) { _bubblesPulse = false; CoreBubbles.Stop(); }
            if (_bouncingPulse) { _bouncingPulse = false; CoreBouncingText.Stop(); }
        }

        /// <summary>
        /// The master switch (checkbox or Start/Stop button) after consent. WPF ChkAutonomyEnabled_Changed:
        /// a RUNNING Takeover cannot be stopped under Lockdown (#514); without premium / the free day
        /// the setting saves but nothing starts. Returns the state the switch should show.
        /// </summary>
        internal bool SetAutonomyEnabled(bool on)
        {
            var s = CoreSettings.Current;
            // WPF MainWindow.Autonomy.cs:43-51: the message only - no Stop tripwire for Takeover.
            if (!on && Autonomy.IsEnabled && LockdownActive)
            {
                Log.Information("Lockdown: Takeover stop refused");
                _ = MessageDialog.ShowAsync(this, ConditioningControlPanel.Localization.Loc.Get("title_lockdown"),
                    ConditioningControlPanel.Localization.Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_stop_dur"));
                return true;
            }

            s.AutonomyModeEnabled = on;
            CoreSettings.Save();
            if (!on) { Autonomy.Stop(); UpdateAutonomyButtonState(false); return false; }

            if (!AutonomyScheduler.HasEntitlement)
            {
                Log.Warning("Autonomy Mode enabled but Patreon access missing - service will not start");
                _ = MessageDialog.ShowAsync(this, "Patreon Required",
                    "Autonomy Mode requires Patreon access.\n\n" +
                    "The setting has been saved, but the feature will not activate until you have Patreon access.");
                Autonomy.Stop();
                UpdateAutonomyButtonState(false);
                return true;
            }
            Autonomy.Start();
            UpdateAutonomyButtonState(true);
            return true;
        }

        /// <summary>The Test button. WPF TestTrigger explains itself when Takeover is off.</summary>
        internal void TestAutonomy()
        {
            if (Autonomy.IsEnabled) { Autonomy.TestTrigger(); return; }
            var reason = !AutonomyScheduler.HasEntitlement
                ? "Bambi Takeover requires Patreon access."
                : "Click the green \"Start\" button to enable it, then press Test again.";
            _ = MessageDialog.ShowAsync(this, "Autonomy Not Running", $"Autonomy Mode isn't running yet.\n\n{reason}");
        }

        /// <summary>Panic (key or tray): Takeover stops with everything else and its queued
        /// action is dropped. The saved switch stays on; the user restarts her. Lockdown has
        /// already refused the panic before this is reached.</summary>
        internal void StopAutonomyForPanic()
        {
            Autonomy.Stop();
            CancelVoicePrompt();   // decisions "Panic ↔ mic": the capture in flight ends, the loop stays armed
        }

        /// <summary>WPF App.xaml.cs:4284: re-arm only on the resume-on-startup opt-in; otherwise the
        /// stale enabled flag is cleared so the switch reads OFF on a fresh launch.</summary>
        private void ResumeAutonomyOnStartup()
        {
            var s = CoreSettings.Current;
            if (s.AutonomyResumeOnStartup && s.AutonomyModeEnabled && s.AutonomyConsentGiven)
            {
                if (Autonomy.Start()) Log.Information("Re-armed Takeover on startup (AutonomyResumeOnStartup opt-in)");
            }
            else if (s.AutonomyModeEnabled)
            {
                s.AutonomyModeEnabled = false;
                Autonomy.Stop();
                CoreSettings.Save();
                Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.SyncFromSettings();
            }
        }

        /// <summary>WPF UpdateAutonomyButtonState: Stop in the mod accent, Start in light green.</summary>
        private void UpdateAutonomyButtonState(bool on)
        {
            var btn = Named<Control>("BambiTakeoverTab")?.FindControl<Button>("BtnAutonomyStartStop");
            if (btn == null) return;
            // A bound TextBlock, not a string: survives a language change and Avalonia's access keys.
            if (btn.Content is TextBlock tb)
                tb.Bind(TextBlock.TextProperty,
                    (global::Avalonia.Data.Binding)new global::ConditioningControlPanel.Avalonia.Localization.StrExtension(on ? "btn_stop_2" : "btn_start_2").ProvideValue(null!));
            btn.Foreground = new SolidColorBrush(on
                ? (Color.TryParse(CoreMods.AccentColorHex, out var c) ? c : Color.Parse("#FF69B4"))
                : Color.FromRgb(144, 238, 144));
        }
    }
}
