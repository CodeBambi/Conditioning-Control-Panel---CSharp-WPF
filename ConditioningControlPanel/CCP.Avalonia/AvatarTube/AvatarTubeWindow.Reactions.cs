using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Core.Services.AIService;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.AvatarTube
{
    public class FlashAudioPlayingEventArgs : EventArgs
    {
        public string FilePath { get; }
        /// <summary>Configured display text for the clip; falls back to the file name when null.</summary>
        public string? Text { get; }
        public FlashAudioPlayingEventArgs(string filePath, string? text = null)
        {
            FilePath = filePath;
            Text = text;
        }
    }

    public partial class AvatarTubeWindow
    {
        private readonly DateTime _startupTime = DateTime.Now;

        /// <summary>
        /// Most-recent awareness payload seen by OnActivityChanged/OnStillOnActivity. Used as a
        /// current-context fallback for the double-click comment path when the engine's live
        /// current-state accessors are unavailable (WPF ChatInput.cs:222-227 reads Current*).
        /// </summary>
        private global::ConditioningControlPanel.Core.Services.Awareness.ActivityChangedEventArgs? _lastAwarenessArgs;

        // Both awareness enums share identical members/order, so an int cast is a safe conversion
        // from the Core seam type to the window's local category enum used by GetPhraseForCategory.
        private static ActivityCategory ToLocalCategory(
            global::ConditioningControlPanel.Core.Services.Awareness.ActivityCategory category)
            => (ActivityCategory)(int)category;

        private async void OnActivityChanged(object? sender, EventArgs e)
        {
            if (e is not global::ConditioningControlPanel.Core.Services.Awareness.ActivityChangedEventArgs args) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnActivityChanged(sender, e)); return; }

            // Cache the latest payload so the double-click comment path has a current-context fallback
            // when the engine's live accessors are unavailable (WPF ChatInput.cs:222-227).
            _lastAwarenessArgs = args;

            // async void: a leaked exception (especially from the post-await continuation) can escape and
            // kill the process — guard the whole body (WPF AvatarTube/AvatarTubeWindow.Reactions.cs:41-44,
            // :92-95, :114-117). Avalonia exposes no HasShutdownStarted; the try/catch plus the close-time
            // unsubscribe (AvatarTubeWindow.axaml.cs:494-498) provide equivalent teardown safety.
            try
            {
                // Don't trigger during startup cooldown — let the greeting show first (WPF Reactions.cs:46-48).
                if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds) return;
                // Wait for the current bubble to clear before reacting (WPF Reactions.cs:50-52).
                if (!IsSpeechReady()) return;

                // WPF also gates on IsCategoryEnabled (Reactions.cs:54-56), but that is hard-wired to
                // always-true in the frozen WPF contract (WindowAwarenessService.cs:725-733; awareness-contract
                // §Engine), so the Core seam intentionally omits it — no category filter applies here.

                // User-configured reaction cooldown (WPF Reactions.cs:58-63).
                if (_awarenessService != null && !_awarenessService.CanReact()) return;
                _awarenessService?.MarkReaction();

                var displayName = string.IsNullOrEmpty(args.ServiceName) ? args.DetectedName : args.ServiceName;
                var pageTitle = args.PageTitle ?? "";

                // Try AI first, fall back to preset phrase (WPF Reactions.cs:70-98).
                string? reaction = null;
                bool isAiResponse = false;
                if (_settings?.Current?.AiChatEnabled == true
                    && App.Services.GetService<IAiService>() is { IsAvailable: true } ai)
                {
                    try
                    {
                        // Only the DERIVED/truncated service+page title reaches the cloud AI here
                        // (awareness-contract §PRIVACY.2); raw titles stay memory-only in the engine.
                        reaction = await ai.GetAwarenessReactionAsync(displayName, args.Category.ToString(), args.ServiceName, pageTitle);
                        if (reaction != null) isAiResponse = true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to get AI awareness reaction");
                    }
                }

                // Preset fallback if AI produced nothing (WPF Reactions.cs:97-98).
                reaction ??= GetPhraseForCategory(ToLocalCategory(args.Category), displayName);

                // AI responses get priority + double bounce; presets queue normally (WPF Reactions.cs:100-109).
                if (isAiResponse)
                {
                    PlayDoubleBounce();
                    GigglePriority(reaction, aiGenerated: true);
                }
                else
                {
                    Giggle(reaction);
                }

                _logger?.LogDebug("Awareness reaction for {DisplayName} ({Category}): {Reaction}", displayName, args.Category, reaction);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OnActivityChanged handler failed");
            }
        }

        private async void OnStillOnActivity(object? sender, EventArgs e)
        {
            if (e is not global::ConditioningControlPanel.Core.Services.Awareness.ActivityChangedEventArgs args) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnStillOnActivity(sender, e)); return; }

            _lastAwarenessArgs = args;

            // Guard the whole body — see OnActivityChanged for the teardown rationale (WPF Reactions.cs:128-131,
            // :183-186, :205-208).
            try
            {
                if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds) return;
                if (!IsSpeechReady()) return;
                // (IsCategoryEnabled always-true — see OnActivityChanged.)
                if (_awarenessService != null && !_awarenessService.CanStillOnReact()) return;
                _awarenessService?.MarkStillOnReaction();

                var duration = _awarenessService?.CurrentActivityDuration ?? TimeSpan.Zero;

                // 50/50 ServiceName vs PageTitle for the display name (WPF Reactions.cs:155-159).
                bool useServiceNameOnly = _random.Next(2) == 0;
                var displayName = useServiceNameOnly || string.IsNullOrEmpty(args.PageTitle)
                    ? args.ServiceName
                    : args.PageTitle;

                // Try AI first, fall back to a preset phrase that includes the elapsed time (WPF Reactions.cs:161-194).
                string? reaction = null;
                bool isAiResponse = false;
                if (_settings?.Current?.AiChatEnabled == true
                    && App.Services.GetService<IAiService>() is { IsAvailable: true } ai)
                {
                    try
                    {
                        reaction = await ai.GetStillOnReactionAsync(displayName, args.Category.ToString(), duration);
                        if (reaction != null) isAiResponse = true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to get AI still-on reaction");
                    }
                }

                if (reaction == null)
                {
                    var minutes = (int)duration.TotalMinutes;
                    var timeText = minutes < 1 ? "a bit" : $"{minutes} min";
                    reaction = $"Still on {displayName}? {timeText} already~ Do your nails instead!";
                }

                // AI responses get priority (no double bounce on this path — WPF Reactions.cs:196-200).
                if (isAiResponse)
                    GigglePriority(reaction, aiGenerated: true);
                else
                    Giggle(reaction);

                _logger?.LogDebug("Still-on reaction for {DisplayName} ({Duration}, useServiceOnly={UseService}): {Reaction}",
                    displayName, duration, useServiceNameOnly, reaction);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OnStillOnActivity handler failed");
            }
        }

        private void OnVideoAboutToStart(object? sender, EventArgs e)
        {
            const string line = "Ooh! Pretty spir-rals...";
            // WPF passes ResolveEventAudio(line) here for a matching voice clip. The port has no
            // event-audio lookup wired yet, so this stays a text-only Giggle (lot-8 C6).
            Giggle(line);
        }

        private async void OnVideoEnded(object? sender, EventArgs e)
        {
            if (_isAttached)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    BringAttachedPairToFront();
                });
            }

            if (_settings?.Current?.AiChatEnabled == true)
            {
                await Task.Delay(100);
                GigglePriority("That was fun~", aiGenerated: false);
            }
        }

        private void OnGameCompleted(object? sender, EventArgs e)
        {
            Giggle("Good girl! So smart!");
        }

        private void OnGameFailed(object? sender, EventArgs e)
        {
            GiggleFromCategory("GameFailed");
        }

        private void OnBubblePopped()
        {
            _bubblePopCounter++;
            if (_bubblePopCounter % 5 == 0) GiggleFromCategory("BubblePop");
        }

        private void OnBubbleMissed()
        {
            if (_random.Next(3) == 0) GiggleFromCategory("BubbleMissed");
        }

        private void OnFlashAboutToDisplay(object? sender, EventArgs e)
        {
            _flashCounter++;
            if (_settings?.Current?.FlashAudioEnabled == true) return;
            if (_flashCounter % 4 == 1) GiggleFromCategory("FlashPre");
        }

        private void OnFlashClicked(object? sender, EventArgs e)
        {
            if (_random.Next(3) == 0) GiggleFromCategory("FlashClicked");
        }

        private void OnFlashAudioPlaying(object? sender, EventArgs e)
        {
            if (e is not FlashAudioPlayingEventArgs args) return;
            // WPF parity (lot-8 C5(c)): FlashService already plays the audio, so show the configured
            // text WITHOUT a second sound (playSound:false), and skip while muted or mid-bubble so the
            // text and audio don't desync.
            var text = string.IsNullOrWhiteSpace(args.Text)
                ? Path.GetFileNameWithoutExtension(args.FilePath)
                : args.Text;
            if (_isMuted || string.IsNullOrWhiteSpace(text)) return;
            if (_isGiggling) return;

            _speechQueue.Clear();
            _speechDelayTimer?.Stop();
            ShowGiggle(text, playSound: false, source: SpeechSource.Preset);
        }

        private void OnSubliminalDisplayed(object? sender, EventArgs e)
        {
            _subliminalCounter++;
            if (_subliminalCounter % 10 == 0) GiggleFromCategory("SubliminalAck");
        }

        private void OnAchievementUnlocked(object? sender, Achievement achievement)
        {
            GigglePriority($"Achievement unlocked: {achievement.Name}! *giggles*", aiGenerated: false);
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            GiggleFromCategory("LevelUp");
        }

        private void OnCompanionLevelUp(object? sender, (CompanionId Companion, int NewLevel) args)
        {
            RefreshCompanionDisplay();
            // Route the roster name through the active mod's terminology map (#325) so a themed mod
            // speaks its own name instead of the Bambi roster name (lot-8 C5(b)).
            var rawName = CompanionDefinition.GetById(args.Companion).Name;
            var companionName = _modService?.MakeModAware(rawName) ?? rawName;
            if (args.NewLevel == CompanionProgress.MaxLevel)
                GigglePriority($"{companionName} reached MAX LEVEL! *sparkles*", aiGenerated: false);
            else if (args.NewLevel % 10 == 0)
                GigglePriority($"{companionName} is now level {args.NewLevel}! Keep going!", aiGenerated: false);
            else
                GiggleFromCategory("LevelUp");
        }

        private void OnCompanionSwitched(object? sender, CompanionId newCompanion)
        {
            RefreshCompanionDisplay();
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;
            _companionGreetingDebounce?.Stop();
            _companionGreetingDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _companionGreetingDebounce.Tick += (_, _) =>
            {
                _companionGreetingDebounce.Stop();
                // Mod-aware greeting name + phrase (#325, lot-8 C5(b)).
                var rawName = CompanionDefinition.GetById(newCompanion).Name;
                var name = _modService?.MakeModAware(rawName) ?? rawName;
                var greeting = $"Hi! {name} is here now~";
                Giggle(_modService?.MakeModAware(greeting) ?? greeting);
            };
            _companionGreetingDebounce.Start();
        }

        private void OnMindWipeTriggered(object? sender, EventArgs e)
        {
            _mindWipeCounter++;
            if (_mindWipeCounter % 6 == 0) GiggleFromCategory("MindWipe");
        }

        private void OnBrainDrainTriggered(object? sender, EventArgs e)
        {
            _brainDrainCounter++;
            if (_brainDrainCounter % 6 == 0) GiggleFromCategory("BrainDrain");
        }

        private void OnSessionStopped(object? sender, EventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnSessionStopped(sender, e));
                return;
            }
            OnEngineStopped(sender, e);
        }

        private void OnEngineStopped(object? sender, EventArgs e)
        {
            GiggleFromCategory("EngineStop");
        }

        public async Task<bool> PlayLockCardAiReactionAsync(object e)
        {
            if (e is not LockCardResultEventArgs args) return false;
            if (_settings?.Current?.AiChatEnabled != true) return false;

            var ai = App.Services.GetService<IAiService>();
            if (ai == null || !ai.IsAvailable) return false;

            try
            {
                var reaction = await ai.GetLockScreenReaction(args.Sentence, args.Mistakes, args.Amount);
                if (!string.IsNullOrWhiteSpace(reaction))
                {
                    GigglePriority(reaction, aiGenerated: true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Lock card AI reaction failed");
            }
            return false;
        }
    }

    public class LockCardResultEventArgs : EventArgs
    {
        public string Sentence { get; }
        public int Mistakes { get; }
        public int Amount { get; }
        public LockCardResultEventArgs(string sentence, int mistakes, int amount)
        {
            Sentence = sentence;
            Mistakes = mistakes;
            Amount = amount;
        }
    }
}
