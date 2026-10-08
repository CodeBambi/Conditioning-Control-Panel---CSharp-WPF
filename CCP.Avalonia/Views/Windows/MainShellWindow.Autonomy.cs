// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Autonomy.cs (728 lines) plus
// the head half of Services/AutonomyService.cs. The decisions (entitlement, cooldown, cadence,
// mood/intensity pick, announce delay, stop generation) are Core's AutonomyScheduler; this file
// seeds it with what this head can actually do and wires start/stop, panic and the state hero.
//
// Performed here (AutonomyService.PerformAction :935): Flash, Subliminal, LockCard, Video
// (mandatory), Bubbles and Bouncing Text as 30 s pulses, Pink Filter as a 30 s pulse (X11 only -
// only where the compositor can show the tint), Bubble Count (forced game), Comment (AI when chat is on
// and available, else a preset phrase through the tube), Mind Wipe (only once CoreMindWipe is
// seeded). Never picked here (CanPerform false, exactly as WPF skips an unavailable action):
// ponytail: Spiral pulse and Brain Drain pulse (no spiral / blur overlay on this head), Web Video
// (no browser media service), Wallpaper (Win32 WallpaperService). Spoken Mantra runs through the
// voice funnel (MainShellWindow.VoiceCommands.cs, Core SpokenMantra). Add each to CanPerformAutonomy the day its surface lands.
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

        private bool _bubblesPulse, _bouncingPulse, _pinkPulse;
        private bool _pinkWasEnabled;
        private int _pinkBaseOpacity, _pinkAppliedOpacity;
        private int _pulseGen;

        private bool _autonomyHooked;

        // WPF AchievementService.TrackTimeBasedProgress, the Takeover half (ccp-bugs #1327): quest
        // minutes measured on a monotonic clock between 1 s ticks, so a throttled or late tick still
        // counts in full and a sleep-sized gap counts nothing (Core RunningTimeCredit).
        private readonly RunningTimeCredit _takeoverCredit = new();
        private readonly System.Diagnostics.Stopwatch _takeoverWatch = System.Diagnostics.Stopwatch.StartNew();
        private DispatcherTimer? _takeoverQuestTick;
        /// <summary>Monotonic clock for the Takeover quest credit; a test steps it.</summary>
        internal Func<TimeSpan>? TakeoverClock;
        internal bool TakeoverQuestTickRunning => _takeoverQuestTick?.IsEnabled == true;

        /// <summary>One tracking tick: credit the interval since the last tick while Takeover runs.</summary>
        internal void CreditTakeoverTime()
        {
            var minutes = _takeoverCredit.Sample(Autonomy.IsEnabled, TakeoverClock?.Invoke() ?? _takeoverWatch.Elapsed);
            if (minutes > 0) App.Quests?.TrackAutonomyMinutes(minutes);
        }

        /// <summary>The tick runs only while Takeover does (WPF's ticks while off only reset the stamp).</summary>
        private void FollowTakeoverQuestTick(bool on)
        {
            CreditTakeoverTime();   // opens the stamp on start, closes it on stop
            if (!on) { _takeoverQuestTick?.Stop(); return; }
            _takeoverQuestTick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                (_, _) => CreditTakeoverTime());
            _takeoverQuestTick.Start();
        }

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
                FollowTakeoverQuestTick(on);
                SetTakeoverActiveUi(on);
                UpdateAutonomyButtonState(on);
            });
            // WPF AnnounceAction: the tube says it (text only; no event audio here).
            Autonomy.AnnouncementMade += (_, phrase) => Dispatcher.UIThread.Post(() =>
                _avatarTubeWindow?.GigglePriority(phrase, false, aiGenerated: false));
            Closed += (_, _) => { CancelAutonomyPulses(); Autonomy.Stop(); _takeoverQuestTick?.Stop(); StopVoiceInput(); };
            Opened += (_, _) => { ResumeAutonomyOnStartup(); RefreshVoiceInputModes(); };
        }

        private bool CanPerformAutonomy(AutonomyActionType a) => a switch
        {
            AutonomyActionType.PinkFilterPulse => PinkFilterOverlay.CanShowTint(this),
            AutonomyActionType.BubbleCount => CoreEngine.BubbleCount != null,
            AutonomyActionType.Comment => _avatarTubeWindow != null,
            AutonomyActionType.MindWipe => CoreMindWipe.TriggerOnceProvider != null,
            AutonomyActionType.Flash => CoreFlash.ShowProvider != null,
            AutonomyActionType.Subliminal => CoreSubliminal.ShowProvider != null,
            AutonomyActionType.LockCard => CoreLockCard.ShowHandler != null,
            AutonomyActionType.Video => CoreEngine.Video != null,
            AutonomyActionType.StartBubbles => CoreBubbles.StartAction != null,
            AutonomyActionType.BouncingText => CoreBouncingText.StartAction != null,
            // WPF SelectAction's mantraOk: engine up and idle, no prompt in flight, the tube shown and the
            // active mod ships mantras. Consent and "no wake word / PTT" are Core Candidates' half.
            AutonomyActionType.SpokenMantra => VoiceSpeech is { IsAvailable: true, IsListening: false }
                                               && !VoicePromptActive && _avatarTubeWindow != null
                                               && App.MantraVoice.HasMantras(),
            _ => false,
        };

        /// <summary>WPF PerformAction for the actions this head has. UI thread.</summary>
        internal void PerformAutonomy(AutonomyActionType a)
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
                case AutonomyActionType.PinkFilterPulse: PulsePinkFilter(); break;
                // WPF: TriggerGame(forceTest: true) - runs with the engine stopped and the card off.
                case AutonomyActionType.BubbleCount: CoreEngine.BubbleCount?.Trigger(forceTest: true); break;
                case AutonomyActionType.Comment: MakeAutonomyComment(); break;
                case AutonomyActionType.MindWipe: CoreMindWipe.TriggerOnce(); break;
                case AutonomyActionType.SpokenMantra: _ = RequestVoiceCommandAsync(allowCommands: false); break;
            }
        }

        /// <summary>WPF PulsePinkFilter: boost the tint for 30 s, then hand it back. Skipped while a
        /// session runs (it owns the overlays). <see cref="PinkFilterOverlay.PulseHold"/> is WPF's
        /// "pulse started the overlay service" (#1180): it shows the tint with the engine off and
        /// is released, not left on, when the pulse ends.</summary>
        private void PulsePinkFilter()
        {
            if (App.Sessions?.IsRunning == true || _pinkPulse) return;
            var s = CoreSettings.Current;
            _pinkPulse = true;
            _pinkWasEnabled = s.PinkFilterEnabled;
            _pinkBaseOpacity = s.PinkFilterOpacity;
            s.PinkFilterEnabled = true;
            s.PinkFilterOpacity = Math.Max(30, _pinkBaseOpacity + 15);
            _pinkAppliedOpacity = s.PinkFilterOpacity;   // the setter clamps; restore only if untouched (#441a)
            PinkFilterOverlay.PulseHold = true;
            PinkFilterOverlay.Refresh(this);
            EndPulseAfter30s(() => EndPinkPulse(cancelled: false));
        }

        private void EndPinkPulse(bool cancelled)
        {
            if (!_pinkPulse) return;
            _pinkPulse = false;
            var s = CoreSettings.Current;
            s.PinkFilterEnabled = _pinkWasEnabled;
            // WPF CancelActivePulses restores unconditionally; the natural end respects a slider move.
            if (cancelled || s.PinkFilterOpacity == _pinkAppliedOpacity)
                s.PinkFilterOpacity = _pinkBaseOpacity;
            PinkFilterOverlay.PulseHold = false;
            PinkFilterOverlay.Refresh(this);
        }

        private static readonly string[] CommentPhrases =
        {
            "*giggles* I love being with you~", "You're doing so well~", "Such a good {petname}~",
            "Teehee~", "I'm always watching~", "*bounces* Pay attention to me~",
        };

        /// <summary>WPF MakeComment / MakeAICommentAsync: AI through GigglePriority, a preset through
        /// the low-priority Giggle (dropped under an AI request or bubble).</summary>
        private async void MakeAutonomyComment()
        {
            var tube = _avatarTubeWindow;
            if (tube == null) return;
            if (CoreSettings.Current.AiChatEnabled && App.Ai?.IsAvailable == true)
            {
                try
                {
                    var r = await App.Ai.GetBambiReplyExAsync("Say something random and teasing to get attention. Be playful.");
                    // Refusals are dropped silently on this surface (WPF R2-NEW-H-1).
                    if (r.Refusal == null && !string.IsNullOrEmpty(r.Text) && Autonomy.IsEnabled)
                        _avatarTubeWindow?.GigglePriority(r.Text, false, aiGenerated: r.IsAiGenerated);
                }
                catch (Exception ex) { Log.Warning("Autonomy: AI comment failed: {Error}", ex.Message); }
                return;
            }
            var phrase = CommentPhrases[Random.Shared.Next(CommentPhrases.Length)];
            tube.Giggle(ConditioningControlPanel.Localization.VocabTokens.Apply(phrase));
        }

        /// <summary>The 30 s pulse clock; tests step it by hand.</summary>
        internal Action<Action, TimeSpan> PulseTimer = (a, t) => DispatcherTimer.RunOnce(a, t);

        private void EndPulseAfter30s(Action end)
        {
            var gen = _pulseGen;
            PulseTimer(() => { if (gen == _pulseGen) end(); }, TimeSpan.FromSeconds(30));
        }

        /// <summary>WPF CancelActivePulses: stop only what a Takeover pulse started.</summary>
        internal void CancelAutonomyPulses()
        {
            _pulseGen++;
            if (_bubblesPulse) { _bubblesPulse = false; CoreBubbles.Stop(); }
            if (_bouncingPulse) { _bouncingPulse = false; CoreBouncingText.Stop(); }
            EndPinkPulse(cancelled: true);
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
