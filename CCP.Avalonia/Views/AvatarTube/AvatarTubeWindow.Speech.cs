using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    /// <summary>
    /// Port of the self-starting half of WPF <c>AvatarTubeWindow.Speech.cs</c>: the four loops the WPF
    /// constructor started (xaml.cs:343-362) so the companion talks on her own - the 2 s greeting
    /// (<c>ShowGreeting</c>, absence-aware, once per launch), the idle chatter beat
    /// (<c>StartIdleTimer</c> / <c>OnIdleTick</c>, cadence = <c>IdleGiggleIntervalSeconds</c>) and
    /// Trigger Mode (<c>StartTriggerTimer</c> / <c>OnTriggerTick</c> / <c>ShowTriggerBubbleImmediate</c>).
    /// The random clickable bubble is <c>AvatarTubeWindow.RandomBubble.cs</c>.
    ///
    /// <para>Started from OnOpened and stopped in OnClosed, never from the constructor: --render-all
    /// constructs ~180 windows in one process and never opens them, so no loop runs there.</para>
    ///
    /// <para>The bark engine is Platform/BarkHead over Core BarkEngine: the idle beat asks
    /// <c>CoreBark.TryDispatchIdle</c> first and the launch greeting <c>CoreBark.TryAppOpened</c>, each
    /// falling back to WPF's own no-bark branch (preset phrase / text-only absence greeting) when no
    /// rule speaks. The streak greeting is <c>CheckStreakMilestoneGreeting</c>. WPF's speech QUEUE (a trigger waits behind a line still speaking) has no twin; a
    /// beat that finds her busy skips, as WPF's IsSpeechReady does.</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        internal const double MinSpeechDelaySeconds = 2.0;   // WPF Speech.cs:116
        private DateTime _lastSpeechEndTime = DateTime.MinValue;
        private DispatcherTimer? _greetingTimer, _idleTimer, _triggerTimer, _triggerFirstTimer;
        private bool _speechLoopsStarted;

        /// <summary>Process-lifetime latch: the warm welcome-back fires once per launch (WPF Speech.cs:2549).</summary>
        private static bool _absenceGreetingShownThisLaunch;

        /// <summary>WPF xaml.cs:343-362, from OnOpened.</summary>
        private void StartSpeechLoops()
        {
            if (_speechLoopsStarted) return;
            _speechLoopsStarted = true;

            _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _greetingTimer.Tick += (_, _) => { _greetingTimer?.Stop(); _greetingTimer = null; ShowGreeting(); };
            _greetingTimer.Start();

            StartIdleTimer();
            StartTriggerTimer();
            StartRandomBubbleTimer();
            CoreSettings.Current.PropertyChanged += OnSpeechSettingChanged;
        }

        private void StopSpeechLoops()
        {
            if (!_speechLoopsStarted) return;
            _speechLoopsStarted = false;
            CoreSettings.Current.PropertyChanged -= OnSpeechSettingChanged;
            _greetingTimer?.Stop(); _greetingTimer = null;
            _idleTimer?.Stop(); _idleTimer = null;
            StopTriggerTimer();
            StopRandomBubbleTimer();
        }

        /// <summary>WPF called RestartTriggerTimer / RestartRandomBubbleTimer from the settings
        /// handlers; this head has no window locator in those handlers, so the tube listens.</summary>
        private void OnSpeechSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Models.AppSettings.TriggerModeEnabled):
                case nameof(Models.AppSettings.TriggerIntervalSeconds):
                    Dispatcher.UIThread.Post(RestartTriggerTimer);
                    break;
                case nameof(Models.AppSettings.RandomBubbleEnabled):
                    Dispatcher.UIThread.Post(RestartRandomBubbleTimer);
                    break;
                case nameof(Models.AppSettings.IdleGiggleIntervalSeconds):
                    Dispatcher.UIThread.Post(RestartIdleTimer);
                    break;
            }
        }

        /// <summary>WPF IsSpeechReady (Speech.cs:141): no bubble up and the 2 s gap since the last one.
        /// ponytail: WPF also adds an AI bonus and a per-character delay after long lines.</summary>
        internal bool IsSpeechReady()
        {
            if (_isGiggling || _isWaitingForAi || _isShowingAiBubble || _isListeningBubble || _isShowingChatHistory) return false;
            return (DateTime.Now - _lastSpeechEndTime).TotalSeconds >= CalculateRequiredDelayAfterLastSpeech();
        }

        // ------------------------------------------------------------------ greeting

        /// <summary>WPF ShowGreeting (Speech.cs:2551), the no-bark branch.</summary>
        internal void ShowGreeting()
        {
            if (_absenceGreetingShownThisLaunch)
            {
                GiggleFromCategory("StartupGreeting");
                return;
            }
            _absenceGreetingShownThisLaunch = true;

            var settings = CoreSettings.Current;
            DateTime? lastSeen = settings.LastSeenUtc;
            // Local only: suppressCloudBackup keeps the timestamp off every sync payload (WPF note).
            settings.LastSeenUtc = DateTime.UtcNow;
            CoreSettings.Save(suppressCloudBackup: true);

            // WPF ShowGreeting: the mod's AppOpened bark (away bucket) wins over the preset greeting.
            if (!CoreBark.TryAppOpened(CoreBark.GreetingAwayBucket(lastSeen)))
            {
                var greeting = BuildAbsenceGreeting(lastSeen);
                if (greeting == null) GiggleFromCategory("StartupGreeting");   // first run, no prior timestamp
                else Giggle(greeting);
            }

            // Celebrate a daily-streak milestone once (queues after the welcome line).
            CheckStreakMilestoneGreeting();
        }

        /// <summary>WPF StreakMilestoneDays: daily-login-streak day counts called out on app open.</summary>
        private static readonly int[] StreakMilestoneDays = { 7, 14, 30, 60, 100, 365 };

        /// <summary>WPF CheckStreakMilestoneGreeting (Speech.cs:2758). A 0 streak is the unsynced beat
        /// at app open, never a reset (the "30 days replays at 50" bug).</summary>
        private static void CheckStreakMilestoneGreeting()
        {
            var settings = CoreSettings.Current;
            int streak = settings.CurrentStreak;
            if (streak <= 0) return;
            int reached = 0;
            foreach (var m in StreakMilestoneDays)
                if (m <= streak) reached = m;
            if (reached == settings.LastAnnouncedStreakMilestone) return;

            bool isNewMilestone = reached > settings.LastAnnouncedStreakMilestone;
            settings.LastAnnouncedStreakMilestone = reached;   // also resets the latch on a streak drop
            CoreSettings.Save(suppressCloudBackup: true);
            if (isNewMilestone && reached > 0)
                CoreBark.Raise("StreakMilestone", new Dictionary<string, object> { ["streak_days"] = (double)reached }, guaranteed: true);
        }

        /// <summary>WPF GiggleFromCategory (Speech.cs:2283): a random ENABLED phrase of the category.
        /// ponytail: WPF also resolves the phrase's recorded clip (PhraseAudioOverrides / custom audio /
        /// the mod's event_audio); without a CompanionPhraseService twin the line is text plus the
        /// preset giggle cue.</summary>
        private void GiggleFromCategory(string category)
        {
            var enabled = AwarenessReactionPhrases.Enabled(category);
            if (enabled.Length == 0) return;   // all phrases in this category disabled
            var text = enabled[Random.Shared.Next(enabled.Length)];
            Giggle(text, ConditioningControlPanel.Services.Companion.CompanionPhraseAudio.ForLine(category, text));
        }

        /// <summary>WPF BuildAbsenceGreeting (Speech.cs:2661), templates verbatim.</summary>
        internal static string? BuildAbsenceGreeting(DateTime? lastSeen, string? name = null, Random? rng = null)
        {
            if (lastSeen == null) return null;
            var elapsed = DateTime.UtcNow - lastSeen.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            string[] templates;
            if (elapsed < TimeSpan.FromHours(6))
                templates = new[] { "Back already? Hehe~ 💕", "Ooh, you're back so soon! ✨", "Couldn't stay away, {name}? 😘" };
            else if (elapsed < TimeSpan.FromHours(18))
                templates = new[] { "Welcome back, {name}! 💖", "Yay, you're here again! ✨", "Hi again, cutie~ 💕" };
            else if (elapsed < TimeSpan.FromDays(3))
                templates = new[] { "There you are! So good to see you~ 💕", "You're back, {name}! ✨", "Hehe, hi again! 💖" };
            else if (elapsed < TimeSpan.FromDays(7))
                templates = new[] { "Yay, you came back! 💕", "So happy you're here, {name}! ✨", "Hi hi! Let's have some fun~ 💖" };
            else if (elapsed < TimeSpan.FromDays(30))
                templates = new[] { "Look who's back! 💕", "So good to see you again, {name}! ✨", "Welcome back, gorgeous~ 💖" };
            else
                templates = new[]
                {
                    "It's been ages - welcome back! 💕✨",
                    "Yaaay, you're back, {name}! So happy to see you! 💖",
                    "Welcome back! Let's pick up right where we left off~ ✨",
                };

            var template = templates[(rng ?? Random.Shared).Next(templates.Length)];
            return FormatGreetingName(template, name ?? CoreSettings.Current.UserDisplayName);
        }

        /// <summary>WPF FormatGreetingName: drop "{name}" and its separator cleanly when nameless.</summary>
        internal static string FormatGreetingName(string template, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return template.Replace("{name}", name.Trim());
            return template.Replace(", {name}", "").Replace(" {name}", "").Replace("{name}", "").Replace("  ", " ").Trim();
        }

        // ------------------------------------------------------------------ idle chatter

        private void StartIdleTimer()
        {
            var interval = CoreSettings.Current.IdleGiggleIntervalSeconds;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval > 0 ? interval : 120) };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
        }

        /// <summary>WPF RestartIdleTimer: the slider's new cadence takes effect at once.</summary>
        public void RestartIdleTimer()
        {
            if (!_speechLoopsStarted) return;
            _idleTimer?.Stop();
            StartIdleTimer();
        }

        /// <summary>WPF OnIdleTick (Speech.cs:1562), the no-bark fallback: a preset phrase.</summary>
        internal void OnIdleTick(object? sender, EventArgs e)
        {
            var configured = CoreSettings.Current.IdleGiggleIntervalSeconds;
            if (_idleTimer != null && configured > 0 && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
                _idleTimer.Interval = TimeSpan.FromSeconds(configured);

            ClearStaleSpeechLatch();   // WPF: the one beat that keeps running while she is wedged
            if (!IsSpeechReady()) return;
            if (CoreBark.TryDispatchIdle()) return;   // WPF: an Idle bark first, the preset phrase is the fallback
            Giggle(RandomBambiPhrase());
        }

        // ------------------------------------------------------------------ trigger mode

        private void StartTriggerTimer()
        {
            if (!CoreSettings.Current.TriggerModeEnabled) return;
            var interval = CoreSettings.Current.TriggerIntervalSeconds;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval > 0 ? interval : 60) };
            _triggerTimer.Tick += OnTriggerTick;
            _triggerTimer.Start();
            Log.Information("TriggerMode: Started with {Interval}s interval", interval);

            // The first trigger 2 s in, so switching it on answers at once (WPF Speech.cs:1607).
            _triggerFirstTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _triggerFirstTimer.Tick += (_, _) => { _triggerFirstTimer?.Stop(); _triggerFirstTimer = null; OnTriggerTick(null, EventArgs.Empty); };
            _triggerFirstTimer.Start();
        }

        private void StopTriggerTimer()
        {
            _triggerTimer?.Stop(); _triggerTimer = null;
            _triggerFirstTimer?.Stop(); _triggerFirstTimer = null;
        }

        public void RestartTriggerTimer()
        {
            if (!_speechLoopsStarted) return;
            StopTriggerTimer();
            StartTriggerTimer();
        }

        /// <summary>WPF OnTriggerTick (Speech.cs:2047): one random custom trigger as a bubble.</summary>
        internal void OnTriggerTick(object? sender, EventArgs e)
        {
            if (!IsSpeechReady()) return;
            var triggers = CoreSettings.Current.CustomTriggers;
            if (triggers == null || triggers.Count == 0) return;
            ShowTriggerBubble(triggers[Random.Shared.Next(triggers.Count)]);
        }

        /// <summary>WPF ShowTriggerBubbleImmediate (Speech.cs:2104): the haptic ALWAYS fires (the toy is
        /// not a second voice); bubble + the trigger's linked clip only when she is visible and unmuted.
        /// Shown for clamp(5 + 0.05 s per char, 5, 14) s, held while hovered.
        /// ponytail: WPF types the word out with the slow typewriter; this bubble shows it whole.</summary>
        internal void ShowTriggerBubble(string trigger) => RunOnAvatar(() => EnqueueTrigger(trigger));

        private void ShowTriggerBubbleImmediate(string trigger)
        {
            _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(trigger);

            if (IsMuted || !IsVisible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted || _isPlayingUninterruptibleClip)
            {
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = SpeechSource.Trigger;
                _lastSpeechLength = trigger.Length;
                ProcessNextSpeech();
                return;
            }

            StopSpokenAudio();
            PlayTriggerAudio(trigger);

            _speechTimer?.Stop();
            if (_isShowingChatHistory)
            {
                _isShowingChatHistory = false;
                _chatHistoryView.IsVisible = false;
                _speechScroller.IsVisible = true;
            }
            _aiBadge.IsVisible = false;
            _policyBadge.IsVisible = false;
            SyncAskButtonsFor(trigger);
            if (SpeechInstant) _txtSpeech.Text = trigger; else StartTypewriter(trigger, slow: true);
            _speechBubble.MaxWidth = SpeechBubbleMaxWidth;
            ApplySpeechBubblePlacement();
            _speechBubble.IsVisible = true;
            _isGiggling = true;
            _isShowingAiBubble = false;

            var seconds = TriggerDisplaySeconds(trigger.Length)
                + (SpeechInstant ? 0 : EstimateTypewriterDurationMs(trigger.Length, slow: true) / 1000.0);
            DateTime? hoverSince = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                hoverSince ??= DateTime.UtcNow;
                if (_isMouseOverSpeechBubble && DateTime.UtcNow - hoverSince.Value < MaxHoverHold) { timer.Interval = TimeSpan.FromSeconds(1); return; }
                timer.Stop();
                CollapseSpeechBubble();
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = SpeechSource.Trigger;
                _lastSpeechLength = trigger.Length;
                NoteLineEnded();
                ProcessNextSpeech();
            };
            _speechTimer = timer;
            timer.Start();
            RestartIdleTimer();   // WPF ResetIdleTimer when speaking
        }

        /// <summary>WPF: 5 s base + 0.05 s per char, clamped 5..14.</summary>
        internal static double TriggerDisplaySeconds(int length) => Math.Clamp(5.0 + length * 0.05, 5.0, 14.0);

        /// <summary>WPF SubliminalService.PlayTriggerAudio (:461): the trigger's linked clip (mod folder,
        /// then Resources/sub_audio, Core's lookup), ducked, at the whisper volume, only when whispers
        /// are audible. A trigger with no recorded clip stays silent (no synthetic speech).</summary>
        private static void PlayTriggerAudio(string trigger)
        {
            try
            {
                var s = CoreSettings.Current;
                if (!s.SubAudioAudible) return;
                var path = SubliminalWhisper.FindLinkedAudio(trigger,
                    SubliminalWhisper.ModAudioDir(App.Mods?.ActiveMod?.InstalledPath),
                    System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio"),
                    App.Mods?.ActiveModId);
                if (path == null) return;
                if (s.AudioDuckingEnabled) CoreAudio.Duck(s.DuckingLevel);
                var gen = CoreAudio.DuckGeneration;
                CoreAudio.PlayStoppable(path, SubliminalWhisper.Volume(s.MasterVolume, s.SubAudioVolume), "trigger",
                    onFinished: () => DispatcherTimer.RunOnce(() => CoreAudio.Unduck(gen),
                        TimeSpan.FromMilliseconds(SubliminalWhisper.UnduckDelayMs)));
            }
            catch (Exception ex) { Log.Debug(ex, "TriggerMode: audio failed"); }
        }
    }
}
