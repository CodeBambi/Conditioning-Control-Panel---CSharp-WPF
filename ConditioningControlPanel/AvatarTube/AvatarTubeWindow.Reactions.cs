using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Moderation;
using XamlAnimatedGif;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel
{
    public partial class AvatarTubeWindow : Window
    {
        private readonly DateTime _startupTime = DateTime.Now; // Track startup to prevent race conditions

        /// <summary>
        /// True while an awareness / still-on LLM call is in flight. The reaction cooldown is
        /// marked on DELIVERY now, not before the call, so it no longer doubles as the
        /// re-entrancy guard it used to be — without this a burst of window switches would
        /// fire overlapping requests against the same daily budget. Only ever touched on the
        /// dispatcher thread (both handlers marshal first), so no locking needed.
        /// </summary>
        private bool _awarenessAiInFlight;

        /// <summary>
        /// Handle activity change from WindowAwarenessService
        /// </summary>
        private async void OnActivityChanged(object? sender, ActivityChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnActivityChanged(sender, e))); return; }
            // async void: a leaked exception (especially from the post-await
            // continuation) escapes to the dispatcher and kills the whole
            // process. Guard the entire body and bail if the app is tearing
            // down. (#386)
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted ?? true)
                    return;

                // Don't trigger during startup cooldown (let greeting show first)
                if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds)
                    return;

                // Don't trigger if speech bubble is still showing - wait for user to clear it
                if (SpeechBubble.Visibility == Visibility.Visible)
                    return;

                // Check if we're allowed to react to this category
                if (!App.WindowAwareness?.IsCategoryEnabled(e.Category) ?? true)
                    return;

                // Check user-configured cooldown from settings
                if (!App.WindowAwareness?.CanReact() ?? true)
                    return;

                // Don't stack requests while one is still running (see field doc).
                if (_awarenessAiInFlight)
                    return;

                // Always use the currently focused window's full context
                // Use service name as primary, with page title for additional context
                string displayName = string.IsNullOrEmpty(e.ServiceName) ? e.DetectedName : e.ServiceName;
                string pageTitle = e.PageTitle ?? "";

                // Try AI first, fall back to preset phrase
                string? reaction = null;
                bool isAiResponse = false;

                if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
                {
                    _awarenessAiInFlight = true;
                    try
                    {
                        // Pass full context from currently focused window. The duration is
                        // near zero on a fresh switch — that's truthful, and beats the
                        // hardcoded "0m" the providers used to send on every path.
                        reaction = await App.Ai.GetAwarenessReactionAsync(displayName, e.Category.ToString(), e.ServiceName, pageTitle,
                            App.WindowAwareness?.CurrentActivityDuration);
                        if (reaction != null)
                        {
                            // No truncation - scrollable speech bubble handles long text
                            isAiResponse = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI awareness reaction");
                    }
                    finally
                    {
                        _awarenessAiInFlight = false;
                    }
                }

                // The await above may have spanned an app shutdown — re-check
                // before touching any UI. (#386)
                if (Application.Current?.Dispatcher?.HasShutdownStarted ?? true)
                    return;

                // Use preset if AI didn't work
                reaction ??= GetPhraseForCategory(e.Category, displayName);

                if (string.IsNullOrWhiteSpace(reaction))
                    return;

                // Mark that we're reacting (resets cooldown timer). Deliberately AFTER the
                // reaction is in hand: this used to run before the LLM call, so a failed,
                // moderated or empty reaction still burned the whole cooldown window and the
                // user got silence instead of the preset phrase they were owed.
                App.WindowAwareness?.MarkReaction();

                // AI responses get priority and double bounce, presets queue normally
                if (isAiResponse)
                {
                    PlayDoubleBounce();
                    GigglePriority(reaction);
                }
                else
                {
                    Giggle(reaction);
                }

                App.Logger?.Debug("Awareness reaction for {DisplayName} ({Category}): {Reaction}",
                    displayName, e.Category, reaction);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OnActivityChanged handler failed");
            }
        }

        /// <summary>
        /// Handle "still on" activity event - user has been on the same activity for a while
        /// </summary>
        private async void OnStillOnActivity(object? sender, ActivityChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnStillOnActivity(sender, e))); return; }
            // async void: guard the whole body so a leaked exception can't kill
            // the process, and bail if the app is tearing down. (#386)
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted ?? true)
                    return;

                // Don't trigger during startup cooldown (let greeting show first)
                if ((DateTime.Now - _startupTime).TotalSeconds < StartupCooldownSeconds)
                    return;

                // Don't trigger if speech bubble is still showing - wait for user to clear it
                if (SpeechBubble.Visibility == Visibility.Visible)
                    return;

                // Check if we're allowed to react to this category
                if (!App.WindowAwareness?.IsCategoryEnabled(e.Category) ?? true)
                    return;

                // Check user-configured cooldown from settings
                if (!App.WindowAwareness?.CanStillOnReact() ?? true)
                    return;

                // Don't stack requests while one is still running (see field doc).
                if (_awarenessAiInFlight)
                    return;

                // Get duration from the awareness service
                var duration = App.WindowAwareness?.CurrentActivityDuration ?? TimeSpan.Zero;

                // 50/50 chance to use just service name vs page title
                bool useServiceNameOnly = _random.Next(2) == 0;
                string displayName = useServiceNameOnly || string.IsNullOrEmpty(e.PageTitle)
                    ? e.ServiceName
                    : e.PageTitle;

                // Try AI first, fall back to preset phrase
                string? reaction = null;
                bool isAiResponse = false;

                if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
                {
                    _awarenessAiInFlight = true;
                    try
                    {
                        // Use the selected display name based on 50/50 choice
                        reaction = await App.Ai.GetStillOnReactionAsync(displayName, e.Category.ToString(), duration);
                        if (reaction != null)
                        {
                            // No truncation - scrollable speech bubble handles long text
                            isAiResponse = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning(ex, "Failed to get AI still-on reaction");
                    }
                    finally
                    {
                        _awarenessAiInFlight = false;
                    }
                }

                // The await above may have spanned an app shutdown — re-check
                // before touching any UI. (#386)
                if (Application.Current?.Dispatcher?.HasShutdownStarted ?? true)
                    return;

                // Use preset if AI didn't work - include time in the fallback
                if (reaction == null)
                {
                    var minutes = (int)duration.TotalMinutes;
                    var timeText = minutes < 1 ? "a bit" : $"{minutes} min";
                    reaction = $"Still on {displayName}? {timeText} already~ Do your nails instead!";
                }

                if (string.IsNullOrWhiteSpace(reaction))
                    return;

                // Mark that we're reacting (resets cooldown timer) — AFTER the reaction is in
                // hand, for the same reason as OnActivityChanged above.
                App.WindowAwareness?.MarkStillOnReaction();

                // AI responses get priority
                if (isAiResponse)
                    GigglePriority(reaction);
                else
                    Giggle(reaction);

                App.Logger?.Debug("Still-on reaction for {DisplayName} ({Duration}, useServiceOnly={UseService}): {Reaction}",
                    displayName, duration, useServiceNameOnly, reaction);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "OnStillOnActivity handler failed");
            }
        }

        /// <summary>
        /// True when the active mod is Bambi Sleep ("BS mode"). Used to suppress the canned giggle
        /// SFX (giggle1-8.mp3), which sounds cheap next to that mod's real voiceline barks.
        /// </summary>
        private static bool IsBambiSleepMod()
            => App.Mods?.ActiveModId?.Contains("bambi", StringComparison.OrdinalIgnoreCase) == true;

        /// <summary>
        /// Plays a fallback sound when no specific audio is connected to a speech bubble.
        /// Randomly chooses between "um" sounds and giggle sounds.
        /// </summary>
        private void PlayFallbackBubbleSound()
        {
            try
            {
                // Bambi Sleep mode: suppress the canned "hehehe" giggle SFX entirely — it sounds cheap
                // next to that mod's real voiceline barks, so a clip-less bubble just stays silent.
                if (IsBambiSleepMod()) return;

                // Use giggle sounds 1-4 for regular speech bubbles
                var fallbackSounds = new[] {
                    "giggle1.MP3", "giggle2.MP3", "giggle3.MP3", "giggle4.MP3"
                };
                var chosenSound = fallbackSounds[_random.Next(fallbackSounds.Length)];
                var soundPath = Services.ModResourceResolver.ResolveAudioPath(chosenSound);

                if (!System.IO.File.Exists(soundPath))
                {
                    App.Logger?.Debug("Fallback sound not found: {Path}", soundPath);
                    return;
                }

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                // Keep fallback sounds quieter (50% of master)
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.5f;

                App.Audio?.PlayOneShot(soundPath, volume, "bubble-fallback");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play fallback bubble sound: {Error}", ex.Message);
            }
        }

        private void OnVideoAboutToStart(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnVideoAboutToStart(sender, e))); return; }
            const string line = "Ooh! Pretty spir-rals...";
            Giggle(line, Services.CompanionPhraseService.ResolveEventAudio(line));
        }

        private async void OnVideoEnded(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnVideoEnded(sender, e))); return; }
            // Z-order after video end: native ownership keeps the attached pair together.

            if (App.Settings?.Current?.AiChatEnabled == true && App.Ai?.IsAvailable == true)
            {
                var title = App.Video?.LastVideoTitle;
                if (string.IsNullOrEmpty(title)) return;

                try
                {
                    var reaction = await App.Ai.GetVideoDoneReaction(title);
                    if (!string.IsNullOrWhiteSpace(reaction))
                        GigglePriority(reaction);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to get AI video-done reaction");
                }
            }
        }

        private void OnGameCompleted(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnGameCompleted(sender, e))); return; }
            Giggle("Good girl! So smart!");
        }

        /// <summary>
        /// Play the AI-generated lock-screen reaction for a completed lock card. Invoked by
        /// BarkService (the sole LockCardCompleted subscriber) on a "heads" coin flip. Returns
        /// true if it actually spoke, so the caller can fall through to a pool bark when the AI
        /// produced nothing — guaranteeing exactly one reaction fires (Fork D).
        /// </summary>
        public async Task<bool> PlayLockCardAiReactionAsync(Services.LockCardCompletedEventArgs e)
        {
            if (App.Settings?.Current?.AiChatEnabled != true || App.Ai?.IsAvailable != true)
                return false;

            try
            {
                var reaction = await App.Ai.GetLockScreenReaction(e.Phrase, e.Mistakes, e.Repeats);
                if (!string.IsNullOrWhiteSpace(reaction))
                {
                    GigglePriority(reaction);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to get AI lock-screen reaction");
                return false;
            }
        }

        /// <summary>
        /// Called just before a flash image is shown - announce it occasionally
        /// </summary>
        private void OnFlashAboutToDisplay(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnFlashAboutToDisplay(sender, e))); return; }
            _flashCounter++;

            // Skip pre-phrase if flash audio is enabled - the audio filename will be shown instead
            if (App.Settings?.Current?.FlashAudioEnabled == true) return;

            // Only announce ~1 in 4 flashes to avoid being annoying
            if (_flashCounter % 4 == 1)
            {
                GiggleFromCategory("FlashPre");
            }
        }

        /// <summary>
        /// Called when flash audio starts playing - show the audio filename as a speech bubble
        /// </summary>
        private void OnFlashAudioPlaying(object? sender, Services.FlashAudioEventArgs e)
        {
            if (_isMuted || string.IsNullOrWhiteSpace(e.Text)) return;

            // Skip if a bubble is currently showing to avoid overlap
            // (audio will play but text won't show - prevents text/audio desync)
            if (_isGiggling)
            {
                App.Logger?.Debug("Flash audio speech skipped (bubble showing): {Text}", e.Text);
                return;
            }

            // Show the audio filename text as a speech bubble (audio is already playing from FlashService)
            RunOnAvatar(() =>
            {
                // Double-check in case state changed
                if (_isGiggling) return;

                // Clear the queue - flash audio text takes priority
                _speechQueue.Clear();
                _speechDelayTimer?.Stop();

                // Show immediately WITHOUT playing sound (FlashService already plays the audio)
                ShowGiggle(e.Text, playSound: false, source: SpeechSource.Preset);

                App.Logger?.Debug("Flash audio speech: {Text}", e.Text);
            });
        }

        /// <summary>
        /// Called after each subliminal is displayed - acknowledge occasionally
        /// </summary>
        private void OnSubliminalDisplayed(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnSubliminalDisplayed(sender, e))); return; }
            _subliminalCounter++;

            // Only acknowledge ~1 in 10 subliminals
            if (_subliminalCounter % 10 == 0)
            {
                GiggleFromCategory("SubliminalAck");
            }
        }

        /// <summary>
        /// Called when user pops a bubble - acknowledge occasionally
        /// </summary>
        private void OnBubblePopped()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnBubblePopped)); return; }
            _bubblePopCounter++;

            // Only acknowledge ~1 in 5 bubble pops
            if (_bubblePopCounter % 5 == 0)
            {
                GiggleFromCategory("BubblePop");
            }
        }

        private void OnGameFailed(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnGameFailed(sender, e))); return; }
            GiggleFromCategory("GameFailed");
        }

        private void OnBubbleMissed()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnBubbleMissed)); return; }
            // Only react occasionally to avoid spam
            if (_random.Next(3) == 0)
            {
                GiggleFromCategory("BubbleMissed");
            }
        }

        private void OnFlashClicked(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnFlashClicked(sender, e))); return; }
            // React to 1 in 3 flash clicks
            if (_random.Next(3) == 0)
            {
                GiggleFromCategory("FlashClicked");
            }
        }

        private void OnAchievementUnlocked(object? sender, Achievement achievement)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnAchievementUnlocked(sender, achievement))); return; }
            GigglePriority($"Achievement unlocked: {achievement.Name}! *giggles*", aiGenerated: false);
        }

        private void OnLevelUp(object? sender, int newLevel)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnLevelUp(sender, newLevel))); return; }
            // Use regular Giggle instead of GigglePriority to avoid cutting off current speech
            // Level up is exciting but shouldn't interrupt active triggers/speech
            GiggleFromCategory("LevelUp");
        }

        /// <summary>
        /// React to companion level up (v5.3).
        /// </summary>
        private void OnCompanionLevelUp(object? sender, (Models.CompanionId Companion, int NewLevel) args)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnCompanionLevelUp(sender, args))); return; }
            RefreshCompanionDisplay();

            // Special level-up phrases based on companion. Route the roster name through
            // the active mod's terminology map so a themed mod (e.g. Circe's Lock) speaks
            // its own name instead of the Bambi roster name like "Synthetic Blowdoll"
            // (#325 — BUG-GLMA287TET). No-op for mod-agnostic modes.
            var rawCompanionName = Models.CompanionDefinition.GetById(args.Companion).Name;
            var companionName = App.Mods?.MakeModAware(rawCompanionName) ?? rawCompanionName;
            if (args.NewLevel == Models.CompanionProgress.MaxLevel)
            {
                GigglePriority($"{companionName} reached MAX LEVEL! *sparkles*", aiGenerated: false);
            }
            else if (args.NewLevel % 10 == 0)
            {
                GigglePriority($"{companionName} is now level {args.NewLevel}! Keep going!", aiGenerated: false);
            }
            else
            {
                // Regular level up - use standard phrases
                GiggleFromCategory("LevelUp");
            }
        }

        private void OnCompanionSwitched(object? sender, Models.CompanionId newCompanion)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnCompanionSwitched(sender, newCompanion))); return; }
            RefreshCompanionDisplay();

            // Clear any queued speech so rapid cycling doesn't stack up greetings
            _speechQueue.Clear();
            _speechTimer?.Stop();
            _speechDelayTimer?.Stop();
            _isGiggling = false;

            // Debounce: delay greeting so only the final companion in a rapid cycle gets one
            _companionGreetingDebounce?.Stop();
            _companionGreetingDebounce = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _companionGreetingDebounce.Tick += (_, _) =>
            {
                _companionGreetingDebounce.Stop();
                var companionName = Models.CompanionDefinition.GetById(newCompanion).Name;
                companionName = App.Mods?.MakeModAware(companionName) ?? companionName;
                var greeting = $"Hi! {companionName} is here now~";
                Giggle(App.Mods?.MakeModAware(greeting) ?? greeting);
            };
            _companionGreetingDebounce.Start();
        }

        /// <summary>
        /// React to MindWipe audio - not too often (1 in 6)
        /// </summary>
        private void OnMindWipeTriggered(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnMindWipeTriggered(sender, e))); return; }
            _mindWipeCounter++;

            // Only react ~1 in 6 times to avoid being annoying
            if (_mindWipeCounter % 6 == 0)
            {
                GiggleFromCategory("MindWipe");
            }
        }

        /// <summary>
        /// React to BrainDrain audio - not too often (1 in 6)
        /// </summary>
        private void OnBrainDrainTriggered(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnBrainDrainTriggered(sender, e))); return; }
            _brainDrainCounter++;

            // Only react ~1 in 6 times to avoid being annoying
            if (_brainDrainCounter % 6 == 0)
            {
                GiggleFromCategory("BrainDrain");
            }
        }

        /// <summary>
        /// React when the engine stops
        /// </summary>
        private void OnEngineStopped(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnEngineStopped(sender, e))); return; }
            GiggleFromCategory("EngineStop");
        }
    }
}
