// PORTED from AvatarTube/AvatarTubeWindow.Reactions.cs:355-631 (WPF 7.1.5) and the subscriptions in
// AvatarTubeWindow.xaml.cs:261-340 / Windowing.cs:1529-1591. Ledger row tube#T5. Same lines, same
// odds (1 in 4 flashes, 1 in 10 subliminals, 1 in 5 pops, 1 in 3 misses / flash clicks, 1 in 6 mind
// wipe / brain drain), same Giggle vs GigglePriority choice. The moments arrive through
// CoreTubeEvents (one hub instead of fourteen services).

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private int _flashCounter, _subliminalCounter, _bubblePopCounter, _mindWipeCounter, _brainDrainCounter;
        private DispatcherTimer? _companionGreetingDebounce;
        private bool _appReactionsAttached;

        /// <summary>When panic last silenced her: a reaction to the stop that panic itself caused never speaks.</summary>
        internal DateTime _panicSilencedUtc = DateTime.MinValue;
        internal const double PanicQuietSeconds = 3.0;

        private bool QuietAfterPanic => (DateTime.UtcNow - _panicSilencedUtc).TotalSeconds < PanicQuietSeconds;

        private void AttachAppReactions()
        {
            if (_appReactionsAttached) return;
            _appReactionsAttached = true;
            CoreTubeEvents.VideoAboutToStart += OnVideoAboutToStart;
            CoreTubeEvents.VideoEnded += OnVideoEnded;
            CoreTubeEvents.FlashAboutToDisplay += OnFlashAboutToDisplay;
            CoreTubeEvents.FlashClicked += OnFlashClicked;
            CoreTubeEvents.FlashAudioPlaying += OnFlashAudioPlaying;
            CoreTubeEvents.SubliminalDisplayed += OnSubliminalDisplayed;
            CoreTubeEvents.BubblePopped += OnBubblePopped;
            CoreTubeEvents.BubbleMissed += OnBubbleMissed;
            CoreTubeEvents.BubbleGameCompleted += OnGameCompleted;
            CoreTubeEvents.BubbleGameFailed += OnGameFailed;
            CoreTubeEvents.AchievementUnlocked += OnAchievementUnlocked;
            CoreTubeEvents.LevelUp += OnLevelUp;
            CoreTubeEvents.CompanionLevelUp += OnCompanionLevelUp;
            CoreTubeEvents.CompanionSwitched += OnCompanionSwitched;
            CoreTubeEvents.MindWipeTriggered += OnMindWipeTriggered;
            CoreTubeEvents.BrainDrainTriggered += OnBrainDrainTriggered;
            CoreTubeEvents.EngineStopped += OnEngineStopped;
        }

        private void DetachAppReactions()
        {
            if (!_appReactionsAttached) return;
            _appReactionsAttached = false;
            CoreTubeEvents.VideoAboutToStart -= OnVideoAboutToStart;
            CoreTubeEvents.VideoEnded -= OnVideoEnded;
            CoreTubeEvents.FlashAboutToDisplay -= OnFlashAboutToDisplay;
            CoreTubeEvents.FlashClicked -= OnFlashClicked;
            CoreTubeEvents.FlashAudioPlaying -= OnFlashAudioPlaying;
            CoreTubeEvents.SubliminalDisplayed -= OnSubliminalDisplayed;
            CoreTubeEvents.BubblePopped -= OnBubblePopped;
            CoreTubeEvents.BubbleMissed -= OnBubbleMissed;
            CoreTubeEvents.BubbleGameCompleted -= OnGameCompleted;
            CoreTubeEvents.BubbleGameFailed -= OnGameFailed;
            CoreTubeEvents.AchievementUnlocked -= OnAchievementUnlocked;
            CoreTubeEvents.LevelUp -= OnLevelUp;
            CoreTubeEvents.CompanionLevelUp -= OnCompanionLevelUp;
            CoreTubeEvents.CompanionSwitched -= OnCompanionSwitched;
            CoreTubeEvents.MindWipeTriggered -= OnMindWipeTriggered;
            CoreTubeEvents.BrainDrainTriggered -= OnBrainDrainTriggered;
            CoreTubeEvents.EngineStopped -= OnEngineStopped;
            _companionGreetingDebounce?.Stop();
        }

        /// <summary>Every handler hops to the tube's thread first, as WPF's Dispatcher.CheckAccess guards.</summary>
        /// Always POSTED: a moment raised inside the panic pass (the engine stop) runs after the pass has
        /// silenced the tube, and then stays unsaid.
        private void OnUi(Action a) => Dispatcher.UIThread.Post(() => { if (!QuietAfterPanic) a(); });

        private void OnVideoAboutToStart() => OnUi(() =>
        {
            const string line = "Ooh! Pretty spir-rals...";
            Giggle(line, Services.Companion.CompanionPhraseAudio.EventClip(line));
        });

        private void OnVideoEnded(string? title) => OnUi(async () =>
        {
            if (!CoreSettings.Current.AiChatEnabled || App.Ai?.IsAvailable != true || string.IsNullOrEmpty(title)) return;
            try
            {
                var reaction = await App.Ai.GetVideoDoneReaction(title!);
                if (!string.IsNullOrWhiteSpace(reaction) && !QuietAfterPanic) GigglePriority(reaction!);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to get AI video-done reaction"); }
        });

        private void OnGameCompleted() => OnUi(() => Giggle("Good girl! So smart!"));

        /// <summary>WPF PlayLockCardAiReactionAsync: the bark system's coin flip asks the tube for the AI
        /// lock-card line; true when she actually spoke (the caller falls back to a pool bark otherwise).</summary>
        public async Task<bool> PlayLockCardAiReactionAsync(string phrase, int mistakes, int repeats)
        {
            if (!CoreSettings.Current.AiChatEnabled || App.Ai?.IsAvailable != true) return false;
            try
            {
                var reaction = await App.Ai.GetLockScreenReaction(phrase, mistakes, repeats);
                if (string.IsNullOrWhiteSpace(reaction) || QuietAfterPanic) return false;
                GigglePriority(reaction!);
                return true;
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to get AI lock-screen reaction"); return false; }
        }

        private void OnFlashAboutToDisplay() => OnUi(() =>
        {
            _flashCounter++;
            if (CoreSettings.Current.FlashAudioEnabled) return;   // the clip's caption speaks instead
            if (_flashCounter % 4 == 1) GiggleFromCategory("FlashPre");
        });

        /// <summary>WPF OnFlashAudioPlaying: the playing clip's caption, silent (the flash plays the audio).</summary>
        private void OnFlashAudioPlaying(string caption) => OnUi(() =>
        {
            if (IsMuted || string.IsNullOrWhiteSpace(caption) || _isGiggling) return;
            _speechQueue.Clear();
            _speechDelayTimer?.Stop(); _speechDelayTimer = null;
            ShowGiggle(caption, playSound: false, SpeechSource.Preset);
        });

        private void OnSubliminalDisplayed() => OnUi(() =>
        {
            if (++_subliminalCounter % 10 == 0) GiggleFromCategory("SubliminalAck");
        });

        private void OnBubblePopped() => OnUi(() =>
        {
            if (++_bubblePopCounter % 5 == 0) GiggleFromCategory("BubblePop");
        });

        private void OnGameFailed() => OnUi(() => GiggleFromCategory("GameFailed"));

        private void OnBubbleMissed() => OnUi(() => { if (_random.Next(3) == 0) GiggleFromCategory("BubbleMissed"); });

        private void OnFlashClicked() => OnUi(() => { if (_random.Next(3) == 0) GiggleFromCategory("FlashClicked"); });

        private void OnAchievementUnlocked(string name) =>
            OnUi(() => GigglePriority($"Achievement unlocked: {name}! *giggles*", aiGenerated: false));

        /// <summary>WPF OnLevelUp: a queued Giggle, never cutting a line off. Also refreshes the level-gated set.</summary>
        private void OnLevelUp(int level) => OnUi(() =>
        {
            OnPlayerLevelChanged(level);
            GiggleFromCategory("LevelUp");
        });

        private void OnCompanionLevelUp(string name, int level, bool isMax) => OnUi(() =>
        {
            RefreshCompanionDisplay();
            name = CoreMods.MakeModAware(name);
            if (isMax) GigglePriority($"{name} reached MAX LEVEL! *sparkles*", aiGenerated: false);
            else if (level % 10 == 0) GigglePriority($"{name} is now level {level}! Keep going!", aiGenerated: false);
            else GiggleFromCategory("LevelUp");
        });

        /// <summary>WPF OnCompanionSwitched: drop the queue, then greet once rapid cycling settles (600 ms).</summary>
        private void OnCompanionSwitched(string name) => OnUi(() =>
        {
            RefreshCompanionDisplay();
            ClearSpeechQueue();
            _speechTimer?.Stop();
            _isGiggling = false;
            _companionGreetingDebounce?.Stop();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                if (QuietAfterPanic) return;
                var n = CoreMods.MakeModAware(name);
                Giggle(CoreMods.MakeModAware($"Hi! {n} is here now~"));
            };
            _companionGreetingDebounce = t;
            t.Start();
        });

        private void OnMindWipeTriggered() => OnUi(() => { if (++_mindWipeCounter % 6 == 0) GiggleFromCategory("MindWipe"); });

        private void OnBrainDrainTriggered() => OnUi(() => { if (++_brainDrainCounter % 6 == 0) GiggleFromCategory("BrainDrain"); });

        private void OnEngineStopped() => OnUi(() => GiggleFromCategory("EngineStop"));
    }
}
