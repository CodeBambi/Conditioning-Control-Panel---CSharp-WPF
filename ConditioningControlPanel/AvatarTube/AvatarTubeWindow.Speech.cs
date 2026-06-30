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

        // Companion speech and chat
        // emotionLineId (bark audio-filename stem) rides along so a QUEUED bark still drives the
        // portrait emotion when its bubble finally renders, not when it was enqueued. Null for
        // non-bark speech (AI/triggers/canned) → no emotion change.
        private readonly Queue<(string text, SpeechSource source, string? emotionLineId, string? mood)> _speechQueue = new();
        private bool _isGiggling = false;
        private bool _isWaitingForAi = false; // Blocks other giggles while waiting for AI
        private DispatcherTimer? _speechTimer;
        private DispatcherTimer? _speechDelayTimer; // Delay between speech instances
        private DispatcherTimer? _idleTimer;
        private DispatcherTimer? _triggerTimer; // Random trigger phrases
        private DispatcherTimer? _randomBubbleTimer; // Random bubble spawning
        private DispatcherTimer? _zOrderRefreshTimer; // Keep speech bubble on top
        private DateTime _lastSpeechEndTime = DateTime.MinValue; // Track when last speech ended
        private SpeechSource _lastSpeechSource = SpeechSource.Preset; // Track last speech source for delay calc
        private int _lastSpeechLength = 0; // Track last speech length for delay calc

        // "She's listening" indicator: while a wake-word/push-to-talk listen window is open, the bubble
        // stays up (no auto-hide timer) with trailing dots animating to show she's waiting for a command.
        private bool _isListeningBubble = false;
        private DispatcherTimer? _listeningDotsTimer;
        private Run? _listeningDotsRun; // the trailing "…" Run we mutate each tick

        /// <summary>
        /// Regex to match markdown-style links: [Link Text](url)
        /// Used for clickable links in speech bubbles.
        /// </summary>
        private static readonly Regex MarkdownLinkRegex = new Regex(
            @"\[([^\]]+)\]\((https?://[^\)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private int _presetGiggleCounter = 0; // Counter for 1-in-5 giggle sound on presets
        private bool _isMuted = false; // Mute avatar speech and sounds
        private bool _isMouseOverSpeechBubble = false; // Track mouse over speech bubble to keep it open
        private bool _isShowingAiBubble = false; // Track when AI bubble is visible (presets get discarded)
        // While true a real recorded clip (e.g. the New Year note) owns the bubble — nothing may
        // preempt it. The clip's own bubble renders via ShowGiggle(..., bypassClipLock: true).
        private bool _isPlayingUninterruptibleClip = false;
        private const string NoteClipCaption = "♡"; // ♡ — placeholder caption while the clip plays; replace with real content
        private const double StartupCooldownSeconds = 3.0; // Don't allow non-greeting speech for 3 seconds

        // Speech source for priority/delay calculation
        private enum SpeechSource
        {
            Preset,     // Preset phrases (click reactions, idle, etc.)
            Trigger,    // Random trigger phrases
            AI          // AI-generated responses
        }

        // Chat history: only the conversational pair (user prompts + AI replies),
        // not random preset/trigger chatter. Capped at MaxChatHistorySize entries.
        public class ChatMessage
        {
            public string Text { get; set; } = string.Empty;
            public bool IsUser { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string TimeLabel => Timestamp.ToString("HH:mm");
        }
        private const int MaxChatHistorySize = 100;
        public ObservableCollection<ChatMessage> ChatHistory { get; } = new();

        private void AddToChatHistory(string text, bool isUser)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ChatHistory.Add(new ChatMessage { Text = text, IsUser = isUser });
            while (ChatHistory.Count > MaxChatHistorySize)
                ChatHistory.RemoveAt(0);
        }

        // Thinking animation: rotating phrase + animated dots while waiting for AI reply.
        private DispatcherTimer? _thinkingTimer;
        private string _thinkingPhraseBase = string.Empty;
        private int _thinkingTickCount;
        private int _thinkingGeneration; // bumped on stop so stale ticks bail

        // Typewriter effect: types AI replies char-by-char, then re-renders with hyperlinks.
        private DispatcherTimer? _typewriterTimer;
        private string _typewriterFullText = string.Empty;
        private int _typewriterIndex;
        private int _typewriterGeneration;

        // Lead-in: spoken (non-AI) lines defer their audio + bubble by this much so the talking
        // animation's mouth starts BEFORE the voice instead of lagging behind it.
        private const double SpeechLeadInSeconds = 0.6;
        private DispatcherTimer? _speechLeadInTimer;

        // Speech delay constants
        private const double MinSpeechDelaySeconds = 2.0;      // Minimum delay between any speech
        private const double AiSpeechBonusSeconds = 5.0;       // Extra delay after AI responses (users need time to read)
        private const int LongTextThreshold = 100;             // Characters before adding per-char delay
        private const double PerCharDelaySeconds = 0.02;       // Delay per character over threshold (doubled for readability)

        // Voice lines from flash audio folder (used for idle comments and 50% of triggers)
        private List<string> _voiceLineFiles = new();
        private string _voiceLinesPath = Services.CompanionPhraseService.VoiceLineFolder;

        // Unified "spoken audio" channel — ALL companion voice (barks, event lines, idle voicelines) plays
        // through one stoppable player so a new line cuts off the previous one instead of overlapping.
        private readonly object _spokenLock = new();
        private NAudio.Wave.WaveOutEvent? _spokenPlayer;
        private NAudio.Wave.AudioFileReader? _spokenReader;
        private volatile bool _isSpeakingAudio = false;   // true only while a spoken clip is actually playing

        // ============================================================
        // COMPANION SPEECH & CHAT
        // ============================================================

        /// <summary>
        /// Checks if a new speech bubble can be shown (not currently showing and cooldown passed)
        /// </summary>
        private bool IsSpeechReady()
        {
            // If currently showing a bubble, not ready
            if (_isGiggling) return false;

            // Check cooldown
            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            double requiredDelay = CalculateRequiredDelayAfterLastSpeech();
            return timeSinceLastSpeech >= requiredDelay;
        }

        /// <summary>
        /// Calculates the required delay before showing the next speech.
        /// Delay is based on the PREVIOUS speech's properties - AI responses and long texts
        /// get more time after they end so users can read them.
        /// </summary>
        private double CalculateRequiredDelayAfterLastSpeech()
        {
            double delay = MinSpeechDelaySeconds;

            // Add bonus delay after AI responses (they're more important, give time to read)
            if (_lastSpeechSource == SpeechSource.AI)
            {
                delay += AiSpeechBonusSeconds;
            }

            // Add per-character delay for long texts (so users can read them)
            if (_lastSpeechLength > LongTextThreshold)
            {
                int extraChars = _lastSpeechLength - LongTextThreshold;
                delay += extraChars * PerCharDelaySeconds;
            }

            return delay;
        }

        /// <summary>
        /// Processes the next speech in the queue with proper delay.
        /// </summary>
        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0)
            {
                _isGiggling = false;
                return;
            }

            var (nextText, source, nextEmotionLineId, nextMood) = _speechQueue.Dequeue();
            App.Logger?.Debug("Dequeued speech ({Source}): {Text}", source, nextText);

            // Calculate how long since last speech ended (delay based on PREVIOUS speech properties)
            double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
            double requiredDelay = CalculateRequiredDelayAfterLastSpeech();
            double remainingDelay = Math.Max(0, requiredDelay - timeSinceLastSpeech);

            if (remainingDelay > 0)
            {
                // Wait before showing next speech
                _speechDelayTimer?.Stop();
                _speechDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(remainingDelay) };
                _speechDelayTimer.Tick += (s, e) =>
                {
                    _speechDelayTimer.Stop();
                    ShowSpeechBySource(nextText, source, nextEmotionLineId, nextMood);
                };
                _speechDelayTimer.Start();
                App.Logger?.Debug("Delaying speech by {Delay:F1}s", remainingDelay);
            }
            else
            {
                // Show immediately
                ShowSpeechBySource(nextText, source, nextEmotionLineId, nextMood);
            }
        }

        /// <summary>
        /// Shows speech based on its source (triggers play audio, others don't)
        /// </summary>
        private void ShowSpeechBySource(string text, SpeechSource source, string? emotionLineId = null, string? mood = null)
        {
            if (source == SpeechSource.Trigger)
            {
                // Triggers have their own display method with audio
                ShowTriggerBubbleImmediate(text);
            }
            else
            {
                // Determine sound: always for AI, 1 in 5 for presets
                bool playSound = source == SpeechSource.AI || (source == SpeechSource.Preset && ++_presetGiggleCounter % 5 == 0);
                ShowGiggle(text, playSound, source, emotionLineId: emotionLineId, mood: mood);
            }
        }

        /// <summary>
        /// Queues a speech bubble to be displayed. Bubbles are shown one at a time.
        /// Blocked while waiting for AI response or while AI bubble is visible.
        /// Plays giggle sound 1 in 5 times for preset phrases.
        /// </summary>
        public void Giggle(string text, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return; // an uninterruptible clip is speaking

            // Block if waiting for AI response
            if (_isWaitingForAi)
            {
                App.Logger?.Debug("Giggle blocked - waiting for AI: {Text}", text);
                return;
            }

            // Block (discard) preset phrases while AI bubble is visible - don't queue them
            if (_isShowingAiBubble)
            {
                App.Logger?.Debug("Giggle discarded - AI bubble visible: {Text}", text);
                return;
            }

            // A bark voiceline carries its audio path; its filename stem is the manifest lineId that
            // drives the portrait emotion. Derive it once so both the queued and immediate paths agree.
            string? emotionLineId = phraseAudioPath != null
                ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath)
                : null;

            // Use BeginInvoke for non-blocking UI update
            RunOnAvatar(() =>
            {
                // Double-check AI bubble state on UI thread
                if (_isShowingAiBubble)
                {
                    App.Logger?.Debug("Giggle discarded (UI thread) - AI bubble visible: {Text}", text);
                    return;
                }

                if (_isGiggling)
                {
                    _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                    App.Logger?.Debug("Queued preset speech: {Text}", text);
                    return;
                }

                // Check if we need to delay based on last speech (delay based on PREVIOUS speech properties)
                double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
                double requiredDelay = CalculateRequiredDelayAfterLastSpeech();

                if (timeSinceLastSpeech < requiredDelay)
                {
                    // Queue it and let the delay system handle it. The queue now carries emotionLineId so
                    // the portrait emotion still fires when the bubble renders; audio is still text-only on
                    // this delayed path (barks normally fire idle, so the immediate path below is the norm).
                    _speechQueue.Enqueue((text, SpeechSource.Preset, emotionLineId, mood));
                    _isGiggling = true;
                    ProcessNextSpeech();
                }
                else
                {
                    // Determine if we should play giggle sound (1 in 5 for presets). A bark voiceline
                    // (barkVoice + phraseAudioPath) takes the audio path in ShowGiggle, so the giggle
                    // sound is suppressed automatically.
                    _presetGiggleCounter++;
                    bool playSound = _presetGiggleCounter % 5 == 0;
                    ShowGiggle(text, playSound, SpeechSource.Preset, phraseAudioPath, barkVoice: barkVoice,
                        emotionLineId: emotionLineId, mood: mood);
                }
            });
        }

        // Timestamp of the last genuine AI reply bubble (not bark output). Lets the bark
        // system suppress barks for a window after a chat exchange. See IsCompanionBusy.
        private DateTime _lastAiBubbleUtc = DateTime.MinValue;

        /// <summary>
        /// True while the companion is mid-chat: an AI request is in flight, an AI bubble is
        /// on screen, or a genuine AI reply occurred within <paramref name="windowMs"/>.
        /// The bark system checks this to avoid talking over a conversation (Fork E).
        /// </summary>
        public bool IsCompanionBusy(int windowMs)
        {
            if (_isWaitingForAi || _isShowingAiBubble) return true;
            return windowMs > 0 && (DateTime.UtcNow - _lastAiBubbleUtc).TotalMilliseconds < windowMs;
        }

        public void GigglePriority(string text, bool playSound = true, bool aiGenerated = true, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip) return; // an uninterruptible clip is speaking
            string? emotionLineId = phraseAudioPath != null
                ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath)
                : null;
            RunOnAvatar(() =>
            {
                // Only genuine AI replies anchor the chat-suppression window; bark output
                // (aiGenerated: false) must not suppress subsequent barks via this path.
                if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;

                // Stop any in-flight thinking animation before showing the reply.
                StopThinkingAnimation();

                // Clear AI waiting flag
                _isWaitingForAi = false;

                // Clear the queue - AI response takes priority
                _speechQueue.Clear();

                // Stop any current speech/delay timers
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();

                // Show immediately
                _isGiggling = false;

                // Capture AI reply for chat history
                AddToChatHistory(text, isUser: false);

                // aiGenerated: when true, the bubble shows the "AI" badge. Most call sites that route
                // through GigglePriority are AI replies, but the awareness keyword path can fall back
                // to canned phrases when the AI is unavailable — those callers pass false.
                ShowGiggle(text, playSound: playSound, source: SpeechSource.AI, aiGenerated: aiGenerated,
                    phraseAudioPath: phraseAudioPath, barkVoice: barkVoice, emotionLineId: emotionLineId, mood: mood);

                App.Logger?.Debug("Priority speech (queue cleared): {Text}", text);
            });
        }

        /// <summary>
        /// Renders the moderation-refusal bubble. Called when an LLM input or output
        /// trips <c>ModerationGuard</c>. Shows the POLICY badge (amber) and the localized
        /// refusal string. Part of P1.1 (CCBill substantive moderation).
        /// </summary>
        public void ShowModerationRefusalBubble(ModerationSource source)
        {
            RunOnAvatar(() =>
            {
                try
                {
                    StopThinkingAnimation();
                    _isWaitingForAi = false;
                    _speechQueue.Clear();
                    _speechTimer?.Stop();
                    _speechDelayTimer?.Stop();
                    _isGiggling = false;

                    var locKey = source == ModerationSource.Input
                        ? "moderation_input_refusal"
                        : "moderation_output_refusal";
                    var text = Loc.Get(locKey);
                    if (string.IsNullOrEmpty(text))
                        text = source == ModerationSource.Input
                            ? "This message can't be sent under our content policy."
                            : "AI declined to respond.";

                    AddToChatHistory(text, isUser: false);

                    // Show via the normal pipeline first so sizing/animation runs,
                    // then swap the badges (POLICY visible, AI hidden).
                    ShowGiggle(text, playSound: false, source: SpeechSource.AI, aiGenerated: false);
                    if (AiBadge != null) AiBadge.Visibility = Visibility.Collapsed;
                    if (PolicyBadge != null) PolicyBadge.Visibility = Visibility.Visible;

                    App.Logger?.Information("ShowModerationRefusalBubble: source={Source}", source);
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "ShowModerationRefusalBubble failed");
                }
            });
        }

        /// <summary>
        /// Displays a speech bubble with text.
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="playSound">Whether to play a giggle sound</param>
        /// <param name="source">The source of the speech (for delay calculation)</param>
        private void ShowGiggle(string text, bool playSound = false, SpeechSource source = SpeechSource.Preset, string? phraseAudioPath = null, bool aiGenerated = false, bool bypassClipLock = false, bool barkVoice = false, string? emotionLineId = null, string? mood = null)
        {
            // An uninterruptible recorded clip owns the bubble — only its own (bypassing) call may render.
            if (_isPlayingUninterruptibleClip && !bypassClipLock) return;

            // A real bubble is taking over the listening indicator (e.g. the command confirmation) —
            // stop the dots animation and release the listening flag so HideListeningBubble no-ops.
            if (_isListeningBubble)
            {
                _listeningDotsTimer?.Stop();
                _listeningDotsTimer = null;
                _listeningDotsRun = null;
                _isListeningBubble = false;
            }

            // CCBill AI Addendum: show the "AI" badge only when this bubble's text actually came from
            // an AI inference. Canned/preset phrases (including AI-path fallbacks) leave it hidden.
            // The POLICY badge is mutually exclusive — only ShowModerationRefusalBubble shows it.
            try
            {
                if (AiBadge != null)
                    AiBadge.Visibility = aiGenerated ? Visibility.Visible : Visibility.Collapsed;
                if (PolicyBadge != null)
                    PolicyBadge.Visibility = Visibility.Collapsed;
            }
            catch { /* AiBadge not in template / closing — non-fatal */ }

            // Chat history view owns the bubble — swap back to single-message mode before showing.
            if (_isShowingChatHistory)
            {
                _isShowingChatHistory = false;
                ChatHistoryView.Visibility = Visibility.Collapsed;
                SpeechScroller.Visibility = Visibility.Visible;
                SpeechBubble.MaxWidth = 380;
            }

            // Skip if muted or avatar not visible on screen
            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                // Track timing and properties even when muted/hidden (for delay calculation)
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = source;
                _lastSpeechLength = text.Length;
                ProcessNextSpeech();
                return;
            }

            _isGiggling = true;
            _isShowingAiBubble = (source == SpeechSource.AI);

            // Drive the emotive portrait to this line's emotion (pose A → linger → pose B → idle after
            // the dwell). lineId is the explicit arg or the bark audio filename stem. No-ops when the
            // portrait system is off, the line is unknown, or there's no audio (canned/AI/trigger text).
            PlayEmotionForLine(
                emotionLineId ?? (phraseAudioPath != null
                    ? System.IO.Path.GetFileNameWithoutExtension(phraseAudioPath) : null),
                phraseAudioPath, text, mood);

            // Any new bubble cuts off the previous spoken line (covers the giggle-sound/fallback branches
            // below, which otherwise wouldn't stop an in-flight voiceline → overlap).
            StopSpokenAudio();

            // The spoken part — audio + bubble. For non-AI lines this is deferred behind
            // EmoteAudioLeadInSeconds: ~0 in emote mode (voice-first; the talk clips join late and bow out
            // early), else the flat SpeechLeadInSeconds so the static pose swap leads the voice.
            bool isThinking = source == SpeechSource.AI && _isWaitingForAi;
            bool slowType = source != SpeechSource.AI; // bark/preset/trigger type slower than AI replies
            Action speak = () =>
            {
                if (!_isGiggling) return; // a newer bubble took over during the lead-in

                // Play sound for the speech bubble
                if (phraseAudioPath != null)
                {
                    // Custom phrase audio overrides default sounds. Bark voicelines play through the
                    // companion-voice path (MasterVolume-gated), not the SubAudio-gated phrase path.
                    if (barkVoice)
                        PlayBarkVoice(phraseAudioPath);
                    else
                        PlayPhraseAudio(phraseAudioPath);
                }
                else if (playSound)
                {
                    // Explicitly requested giggle sound (AI responses, etc.)
                    PlayGiggleSound();
                }
                else if (source != SpeechSource.AI)
                {
                    // Fallback sound for regular bubbles (skip for AI thinking — response will play its own)
                    PlayFallbackBubbleSound();
                }

                // Type the text out char-by-char for every bubble (AI replies fast, everything else
                // slower). The AI "thinking" frame skips it — its tick updates the bubble directly.
                // PopulateSpeechBubble runs again at the end of the typewriter so links stay clickable.
                if (!isThinking)
                    StartTypewriter(text, slowType);
                else
                    PopulateSpeechBubble(text);

                // Strip markdown links for size calculation (use plain text length)
                var plainText = MarkdownLinkRegex.Replace(text ?? "", "$1");

                // Adjust bubble size based on text length
                AdjustBubbleSize(plainText);

                // Force layout update before showing to prevent flickering
                ApplyBubbleBackgroundForMod();
                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                // Display duration is user-controlled via Companion tab slider (1-10s, default 2).
                // Long AI replies are still readable: hovering keeps the bubble open, and
                // "Show chat history" preserves the full conversation for re-reading.
                double userSetting = Math.Clamp(App.Settings?.Current?.BubbleDurationSeconds ?? 2.0, 1.0, 10.0);
                double displayDuration = userSetting;

                // The typewriter eats into the visible window. Add its estimated runtime (every bubble
                // types now) so the slider value reflects fully-rendered reading time, not open time.
                if (!isThinking)
                {
                    double typewriterSec = EstimateTypewriterDurationMs((text ?? "").Length, slowType) / 1000.0;
                    displayDuration += typewriterSec;

                    if (source == SpeechSource.AI)
                    {
                        // Floor for long replies: the user's slider value is sensible for the short
                        // trigger-style bubbles it was designed for, but a 200-char AI reply at
                        // userSetting=2 leaves only 2 seconds of reading time after typing (bug #193).
                        // Apply ~12 chars/sec ESL-friendly reading speed as a minimum post-typewriter
                        // dwell, capped at 30s so a runaway long reply doesn't pin the bubble forever.
                        const double charsPerSecond = 12.0;
                        const double maxPostTypeSec = 30.0;
                        double readingFloorSec = Math.Min(maxPostTypeSec, (text ?? "").Length / charsPerSecond);
                        double minTotalSec = typewriterSec + Math.Max(userSetting, readingFloorSec);
                        if (minTotalSec > displayDuration) displayDuration = minTotalSec;
                    }
                }

                // Hide after calculated duration
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };

                // Capture current speech properties for delay calculation
                var currentSource = source;
                var currentLength = (text ?? "").Length;

                _speechTimer.Tick += (s, e) =>
                {
                    // If mouse is over speech bubble, keep it open - recheck in 1 second
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return; // Don't stop timer, keep checking
                    }

                    _speechTimer.Stop();
                    StopZOrderRefreshTimer();
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides

                    // Track this speech's properties for delay calculation on next speech
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = currentSource;
                    _lastSpeechLength = currentLength;

                    // Process next speech with proper delay handling
                    ProcessNextSpeech();
                };
                _speechTimer.Start();

                // Reset idle timer when speaking
                ResetIdleTimer();

                App.Logger?.Debug("Companion says ({Source}, {Chars} chars, {Duration:F1}s): {Text}",
                    source, (text ?? "").Length, displayDuration, text);
            };

            _speechLeadInTimer?.Stop();
            _speechLeadInTimer = null;
            if (source != SpeechSource.AI)
            {
                // Spoken line: in emote mode the voice fires right away (~0 — the talk animation joins the
                // line late instead); otherwise hold audio + bubble behind the flat lead-in so the static
                // pose swap leads the voice.
                _speechLeadInTimer = new DispatcherTimer
                { Interval = TimeSpan.FromSeconds(EmoteAudioLeadInSeconds(SpeechLeadInSeconds)) };
                _speechLeadInTimer.Tick += (s, e) =>
                {
                    _speechLeadInTimer?.Stop();
                    _speechLeadInTimer = null;
                    speak();
                };
                _speechLeadInTimer.Start();
            }
            else
            {
                speak(); // AI replies are already gated by inference latency — no lead-in
            }
        }

        // ── Speech bubble background (per-mod) ────────────────────────────────
        // Mirrors the XAML BubbleGradient (PatreonDarkPurple #FF8E44AD → DarkerBg #FF121220). The
        // sissy variant drops the fill alpha so the bubble reads more transparent (the user found the
        // sissy bubble hard to read); the border + pink text keep full opacity so legibility improves.
        private const double SissyBubbleAlpha = 0.70; // tunable: sissy bubble fill opacity

        // Sissy's mod accent is a muted purple that goes muddy on the now-transparent fill; give the
        // text a brighter, higher-contrast tone so it pops. Other mods keep their accent (PinkBrush).
        private static readonly Brush SissyTextBrush = FreezeBrush(Color.FromRgb(0xFF, 0x9C, 0xE8)); // bright candy pink

        private static Brush FreezeBrush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static Brush? _defaultBubbleBrush;
        private static Brush? _sissyBubbleBrush;

        private static LinearGradientBrush BuildBubbleBrush(byte alpha)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0x8E, 0x44, 0xAD), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, 0x12, 0x12, 0x20), 1));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Applies per-mod speech-bubble styling. In sissy mod the fill is the more-transparent variant
        /// and the text uses a brighter high-contrast color so it pops; other mods keep the opaque fill
        /// and their accent (PinkBrush) text. Cheap and idempotent — called every time the bubble is
        /// shown, so it always reflects the current mod without mod-change wiring.
        /// </summary>
        private void ApplyBubbleBackgroundForMod()
        {
            try
            {
                _defaultBubbleBrush ??= BuildBubbleBrush(0xFF);
                _sissyBubbleBrush ??= BuildBubbleBrush((byte)Math.Round(SissyBubbleAlpha * 255));

                var id = App.Mods?.ActiveModId ?? "";
                bool isSissy = id.IndexOf("sissy", StringComparison.OrdinalIgnoreCase) >= 0;
                SpeechBubble.Background = isSissy ? _sissyBubbleBrush : _defaultBubbleBrush;

                // Sissy → brighter text; otherwise restore the mod-accent dynamic resource (PinkBrush).
                if (isSissy)
                    TxtSpeech.Foreground = SissyTextBrush;
                else
                    TxtSpeech.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "PinkBrush");
            }
            catch { /* non-fatal — keep whatever brush is set */ }
        }

        // ── "She's listening" indicator ───────────────────────────────────────

        /// <summary>
        /// Holds the speech bubble open with animated trailing dots while a wake-word / push-to-talk
        /// listen window is active, so the user can see she's waiting for a command. Self-contained:
        /// it bypasses the auto-hide <see cref="_speechTimer"/> and renders the text directly. Leaves
        /// any in-flight spoken audio (e.g. the wake acknowledgement voiceline) playing.
        /// </summary>
        public void ShowListeningBubble(string text)
        {
            RunOnAvatar(() =>
            {
                try
                {
                    // Don't preempt an uninterruptible recorded clip.
                    if (_isPlayingUninterruptibleClip) return;

                    // Take over any in-flight bubble cleanly (continuous visibility, no gap).
                    StopThinkingAnimation();
                    // Stop the wake-ack's typewriter — otherwise its ticks (and the end-of-type
                    // PopulateSpeechBubble) keep re-rendering TxtSpeech and overwrite our dots,
                    // and the dots Run ends up detached from the bubble so it never shows.
                    StopTypewriter();
                    _isWaitingForAi = false;
                    _isShowingAiBubble = false;
                    _speechQueue.Clear();
                    _speechTimer?.Stop();
                    _speechDelayTimer?.Stop();
                    _speechLeadInTimer?.Stop();
                    _speechLeadInTimer = null;
                    _isGiggling = false;

                    // Hidden/muted: track nothing to show, but still flag listening so Hide is balanced.
                    if (_isMuted || !IsAvatarVisibleOnScreen)
                    {
                        _isListeningBubble = true;
                        return;
                    }

                    // Badges off for the listening indicator.
                    try
                    {
                        if (AiBadge != null) AiBadge.Visibility = Visibility.Collapsed;
                        if (PolicyBadge != null) PolicyBadge.Visibility = Visibility.Collapsed;
                    }
                    catch { }

                    // Chat-history view owns the bubble — swap back to single-message mode.
                    if (_isShowingChatHistory)
                    {
                        _isShowingChatHistory = false;
                        ChatHistoryView.Visibility = Visibility.Collapsed;
                        SpeechScroller.Visibility = Visibility.Visible;
                        SpeechBubble.MaxWidth = 380;
                    }

                    var baseText = text ?? "";
                    TxtSpeech.Inlines.Clear();
                    TxtSpeech.Inlines.Add(new Run(baseText));
                    _listeningDotsRun = new Run("");
                    TxtSpeech.Inlines.Add(_listeningDotsRun);

                    AdjustBubbleSize(baseText + "...");

                    _isListeningBubble = true;
                    ApplyBubbleBackgroundForMod();
                    SpeechBubble.UpdateLayout();
                    SpeechBubble.Visibility = Visibility.Visible;

                    if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                    {
                        StartZOrderRefreshTimer();
                        BringAttachedPairToFront();
                    }

                    // Animate "" → "." → ".." → "..." so it reads as actively waiting.
                    _listeningDotsTimer?.Stop();
                    int step = 0;
                    _listeningDotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    _listeningDotsTimer.Tick += (s, e) =>
                    {
                        if (!_isListeningBubble || _listeningDotsRun == null) return;
                        step = (step + 1) % 4;
                        _listeningDotsRun.Text = new string('.', step);
                    };
                    _listeningDotsTimer.Start();

                    ResetIdleTimer();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "ShowListeningBubble failed"); }
            });
        }

        /// <summary>
        /// Ends the "she's listening" indicator. Stops the dot animation; if the listening bubble is
        /// still the one on screen (no real bubble took over), collapses it. No-ops if a confirmation
        /// or other bubble already replaced it (ShowGiggle clears the flag in that case).
        /// </summary>
        public void HideListeningBubble()
        {
            RunOnAvatar(() =>
            {
                try
                {
                    _listeningDotsTimer?.Stop();
                    _listeningDotsTimer = null;
                    _listeningDotsRun = null;

                    if (!_isListeningBubble) return; // a real bubble already took over
                    _isListeningBubble = false;

                    StopZOrderRefreshTimer();
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    _lastSpeechEndTime = DateTime.Now;
                    ProcessNextSpeech();
                }
                catch (Exception ex) { App.Logger?.Warning(ex, "HideListeningBubble failed"); }
            });
        }

        private DispatcherTimer? _mutedIndicatorTimer;

        /// <summary>
        /// Shows a brief "MUTED" indicator in the speech bubble when the user
        /// double-clicks the avatar while muted, so they know she's not broken.
        /// </summary>
        private void ShowMutedIndicator()
        {
            // Don't spam the indicator
            if (SpeechBubble.Visibility == Visibility.Visible)
                return;

            TxtSpeech.Inlines.Clear();
            TxtSpeech.Inlines.Add(new System.Windows.Documents.Run("MUTED \U0001F509")
            {
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(180, 180, 200))
            });
            TxtSpeech.FontSize = 20;

            SpeechBubble.Visibility = Visibility.Visible;

            _mutedIndicatorTimer?.Stop();
            _mutedIndicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _mutedIndicatorTimer.Tick += (s, e) =>
            {
                _mutedIndicatorTimer.Stop();
                SpeechBubble.Visibility = Visibility.Collapsed;
            };
            _mutedIndicatorTimer.Start();
        }

        /// <summary>
        /// Adjusts the speech bubble font size and position based on text length.
        /// The bubble has fixed width (380) and MaxHeight (420) - ScrollViewer handles overflow.
        /// </summary>
        private void AdjustBubbleSize(string text)
        {
            int charCount = text.Length;

            // Adjust font size for readability based on text length
            // Shorter text can use larger font, longer text uses smaller font
            double fontSize;
            if (charCount <= 50)
            {
                fontSize = 22; // Normal size for short messages
            }
            else if (charCount <= 120)
            {
                fontSize = 20; // Slightly smaller for medium messages
            }
            else if (charCount <= 250)
            {
                fontSize = 18; // Smaller for longer messages
            }
            else
            {
                fontSize = 16; // Smallest for very long AI responses
            }

            TxtSpeech.FontSize = fontSize;

            // Reset scroll position to top when new text is shown
            SpeechScroller?.ScrollToTop();

            // Position bubble next to avatar — align with tube position based on attach state.
            var bubbleUseAttached = _isAttached || ModOverridesAttachedTubeOnly();
            var bubbleDx = bubbleUseAttached
                ? EffAvatarOffsetX()
                : EffAvatarDetachedOffsetX();
            var bubbleRight = bubbleUseAttached ? 125 - bubbleDx : 425 - bubbleDx;
            SpeechBubble.Margin = new Thickness(0, 0, bubbleRight, 550);
        }

        /// <summary>
        /// Populates the speech bubble with text and clickable hyperlinks.
        /// Parses markdown-style [text](url) links and creates Hyperlink inlines.
        /// </summary>
        /// <summary>
        /// Exact HypnoTube video titles mapped to URLs.
        /// Names match exactly as shown on HypnoTube.
        /// Reloaded when the active mod changes via ReloadVideoLinks().
        /// </summary>
        internal static Dictionary<string, string> KnownVideoLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Naughty Bambi", "https://hypnotube.com/video/naughty-bambi-109749.html" },
            { "Bambi Bae", "https://hypnotube.com/video/bambi-bae-113979.html" },
            { "Bambi's Naughty TikTok Collection", "https://hypnotube.com/video/bambis-naughty-tiktok-collection-117314.html" },
            { "TikTok Loop", "https://hypnotube.com/video/tiktok-loop-39245.html" },
            { "Overload", "https://hypnotube.com/video/overload-46422.html" },
            { "Bambi TikTok - In Beat", "https://hypnotube.com/video/bambi-tiktok-in-beat-52730.html" },
            { "Bambi TikTok - In Beat - Longer Version", "https://hypnotube.com/video/bambi-tiktok-in-beat-longer-version-56194.html" },
            { "Bambi TikTok - Good Girls Dont Cum", "https://hypnotube.com/video/bambi-tiktok-good-girls-dont-cum-68081.html" },
            { "Bambi Chastity Overload", "https://hypnotube.com/video/bambi-chastity-overload-75092.html" },
            { "Mommy's In Control Full", "https://hypnotube.com/video/mommys-in-control-full-76043.html" },
            { "Bambi Loves Hentai - OneeKitsune", "https://hypnotube.com/video/bambi-loves-hentai-oneekitsune-78373.html" },
            { "Bubblehead Forever - Iplaywithdolls", "https://hypnotube.com/video/bubblehead-forever-iplaywithdolls-79880.html" },
            { "Dumb Bimbo Brainwash", "https://hypnotube.com/video/dumb-bimbo-brainwash-80780.html" },
            { "Bambi TikTok Eager Slut", "https://hypnotube.com/video/bambi-tiktok-eager-slut-80971.html" },
            { "Mindlocked Cock Zombie", "https://hypnotube.com/video/mindlocked-cock-zombie-87742.html" },
            { "Bambi TikTok Good Girl Academy", "https://hypnotube.com/video/bambi-tiktok-good-girl-academy-92527.html" },
            { "Bambi TikTok Chastity Trainer", "https://hypnotube.com/video/bambi-tiktok-chastity-trainer-96290.html" },
            { "Bambi Slay", "https://hypnotube.com/video/bambi-slay-99609.html" },
            // Batch 2
            { "Yes Brain Loop", "https://hypnotube.com/video/yes-brain-loop-113736.html" },
            { "Bambi Uniform Bliss", "https://hypnotube.com/video/bambi-uniform-bliss-3553.html" },
            { "Bambi Bimbo Dreams Ep 1", "https://hypnotube.com/video/bambi-bimbo-dreams-ep-1-8050.html" },
            { "Day 1", "https://hypnotube.com/video/day-1-11009.html" },
            { "Day 2", "https://hypnotube.com/video/day-2-11011.html" },
            { "Day 4", "https://hypnotube.com/video/day-4-11179.html" },
            { "Day 5", "https://hypnotube.com/video/day-5-11228.html" },
            { "Bimbo Servitude Brainwash", "https://hypnotube.com/video/bimbo-servitude-brainwash-33041.html" },
            { "Bambi Uniform Oblivion", "https://hypnotube.com/video/bambi-uniform-oblivion-34010.html" },
            { "Bambi TikTok 7", "https://hypnotube.com/video/bambi-tiktok-7-42488.html" },
            { "Bambi Tik-Tok Mix 1 - 7 No Pauses", "https://hypnotube.com/video/bambi-tik-tok-mix-1-7-no-pauses-53860.html" },
            { "Bambi's Brain Melts TikTok", "https://hypnotube.com/video/bambi-s-brain-melts-tiktok-56183.html" },
            { "Bimbodoll Seduction - Part I", "https://hypnotube.com/video/bimbodoll-seduction-part-i-62493.html" },
            { "Toms Dangerous Tik Tok", "https://hypnotube.com/video/toms-dangerous-tik-tok-62552.html" },
            { "Bimbodoll Awakened Obedience", "https://hypnotube.com/video/bimbodoll-awakened-obedience-62614.html" },
            { "Bimbdoll Resistance Full", "https://hypnotube.com/video/bimbdoll-resistance-full-63079.html" },
            { "Bambi - I Want Your Cum", "https://hypnotube.com/video/bambi-i-want-your-cum-64715.html" },
            { "Bambi Day 7 Remix", "https://hypnotube.com/video/bambi-day-7-remix-65691.html" },
            { "Bambi Tiktok Wide Remix By Analbambi", "https://hypnotube.com/video/bambi-tiktok-wide-remix-by-analbambi-66055.html" },
            // Sissy Hypno pool
            { "Ultimate Sissy Mindfuck", "https://hypnotube.com/video/ultimate-sissy-mindfuck-106170.html" },
            { "Femboy Heaven - TS PMV", "https://hypnotube.com/video/femboy-heaven-ts-pmv-90699.html" },
            { "Wife Helps You Take Cock", "https://hypnotube.com/video/wife-helps-you-take-cock-91559.html" },
            { "Up and Down", "https://hypnotube.com/video/up-and-down-95541.html" },
            { "Neural Rewire - Mommys Fap Roulette - Devereux", "https://hypnotube.com/video/neural-rewire-mommys-fap-roulette-devereux-115970.html" },
            { "Girly Thoughts Vertical Loop", "https://hypnotube.com/video/girly-thoughts-vertical-loop-118644.html" },
            { "Splitscreen Anal Trainer 4 - By Dildoslut", "https://hypnotube.com/video/splitscreen-anal-trainer-4-by-dildoslut-111004.html" },
            { "Anal Dream - SissyGalJasmine Edition", "https://hypnotube.com/video/anal-dream-sissygaljasmine-edition-90388.html" },
            { "BBC Stoner Goon File", "https://hypnotube.com/video/bbc-stoner-goon-file-89975.html" },
            { "Sissy Desires", "https://hypnotube.com/video/sissy-desires-103899.html" },
            { "A Touch Of Femboy - TS PMV", "https://hypnotube.com/video/a-touch-of-femboy-ts-pmv-110470.html" },
            { "Say Yes To Cock Hypnosis", "https://hypnotube.com/video/say-yes-to-cock-hypnosis-112015.html" },
            { "Pegging Dreams - Full", "https://hypnotube.com/video/pegging-dreams-full-110796.html" },
            { "Hold it Down - Deepthroat Trainer By Whore Factory", "https://hypnotube.com/video/hold-it-down-deepthroat-trainer-by-whore-factory-112708.html" },
            { "Anal Slut Trainer", "https://hypnotube.com/video/anal-slut-trainer-101540.html" },
            { "You Love Cock", "https://hypnotube.com/video/you-love-cock-105890.html" },
            { "Deep Acceptance", "https://hypnotube.com/video/deep-acceptance-113157.html" },
            { "Eat Your Cum", "https://hypnotube.com/video/eat-your-cum-116026.html" },
            { "Trans Love Hypno - CrimsonPMV", "https://hypnotube.com/video/trans-love-hypno-crimsonpmv-121310.html" },

            // BambiCloud playlists (audio, extracted 2026-04-28)
            { "IQ Programming", "https://bambicloud.com/playlist/ff15f538-6e6b-433c-b68b-b4af5ee5d14d" },
            { "Attitude Programming", "https://bambicloud.com/playlist/c0effdad-6002-4269-a982-479d676c8d46" },
            { "Takeover Programming", "https://bambicloud.com/playlist/726403c2-567c-4c30-9f74-8fd750a82ef9" },
            { "Cockslut Programming", "https://bambicloud.com/playlist/10091e87-2243-4f75-85d1-912c39951bc4" },
            { "Uniform Programming", "https://bambicloud.com/playlist/39f0c016-abfb-4a53-a8d3-1c492a86635b" },
            { "Maid Programming", "https://bambicloud.com/playlist/d244e2d6-be21-4e5b-bab1-b1268ade85ce" },
            { "Deep Trance Programming", "https://bambicloud.com/playlist/648f16c8-865b-44e2-bba5-881fc499e0f7" },
            { "Personality Programming", "https://bambicloud.com/playlist/ba1cf73a-5f3e-4ef8-bbc6-67ce2dcae774" },
        };

        // Cached copy of the built-in links for restoring when switching away from custom mods
        private static Dictionary<string, string>? _builtInVideoLinks;

        /// <summary>
        /// Reloads KnownVideoLinks from the active mod's defaultVideoLinks, or restores built-in defaults.
        /// Called on mod switch.
        /// </summary>
        internal static void ReloadVideoLinks()
        {
            // Cache built-in links on first call
            _builtInVideoLinks ??= new Dictionary<string, string>(KnownVideoLinks, StringComparer.OrdinalIgnoreCase);

            var modLinks = App.Mods?.GetVideoLinks();
            if (modLinks != null && modLinks.Count > 0)
            {
                // Filter to HTTPS-only at runtime (defense-in-depth)
                var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in modLinks)
                {
                    if (Uri.TryCreate(kvp.Value, UriKind.Absolute, out var uri) && uri.Scheme == "https")
                        filtered[kvp.Key] = kvp.Value;
                }
                KnownVideoLinks = filtered;
            }
            else
            {
                KnownVideoLinks = new Dictionary<string, string>(_builtInVideoLinks, StringComparer.OrdinalIgnoreCase);
            }
        }

        private void PopulateSpeechBubble(string text)
        {
            BuildLinkedInlines(text, TxtSpeech.Inlines);
        }

        /// <summary>
        /// Strips markdown link syntax, finds known video/playlist titles and raw URLs in
        /// <paramref name="text"/>, and writes Run / Hyperlink inlines into <paramref name="target"/>.
        /// Used by the live speech bubble AND the chat history items so both render the same
        /// clickable pink hyperlinks.
        /// </summary>
        private void BuildLinkedInlines(string text, InlineCollection target)
        {
            target.Clear();

            if (string.IsNullOrEmpty(text))
                return;

            // Pass 1: collapse markdown links into just their text BUT remember the (text, url)
            // pairs so we can re-attach them as real Hyperlink inlines below. We used to drop
            // URLs entirely, which meant the AI couldn't reliably surface clickable links —
            // small models often produce correct URLs (copied verbatim from the prompt) but
            // approximate the title text, so the URL is the more authoritative signal.
            var mdLinks = new List<(string LinkText, string Url)>();
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", m =>
            {
                var linkText = m.Groups[1].Value;
                var url = m.Groups[2].Value.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var u) &&
                    (u.Scheme == "https" || u.Scheme == "http"))
                {
                    mdLinks.Add((linkText, url));
                }
                return linkText; // collapse to plain text in the working buffer
            });

            // Also clean up any malformed markdown like [Url] or (url)
            text = Regex.Replace(text, @"\s*\[Url\]|\s*\(url\)", "", RegexOptions.IgnoreCase);

            // Try to find known video names in the text and make them clickable
            var processedText = text;
            var linkPositions = new List<(int start, int length, string name, string url)>();

            // Pass 2: re-find the markdown link texts (now plain) in the buffer and claim them
            // as the highest-priority link source. Authoritative URL beats name-based guess.
            foreach (var (linkText, url) in mdLinks)
            {
                var idx = processedText.IndexOf(linkText, StringComparison.Ordinal);
                if (idx < 0) continue;
                bool overlaps = linkPositions.Any(lp =>
                    (idx >= lp.start && idx < lp.start + lp.length) ||
                    (idx + linkText.Length > lp.start && idx + linkText.Length <= lp.start + lp.length));
                if (!overlaps)
                    linkPositions.Add((idx, linkText.Length, linkText, url));
            }

            // Match against BOTH the static link table AND the active mod's LIVE video pool — the
            // exact same source the AI prompt drew its suggestions from (App.Mods.GetVideoLinks()).
            // ReloadVideoLinks() is supposed to keep KnownVideoLinks in sync on mod switch, but if
            // it runs before the active mod is set the table lags behind, so a real Sissy pool title
            // the companion was told to say (e.g. "Sissy Dreams 3") isn't in KnownVideoLinks and
            // renders as dead plain text. Merging the live pool here guarantees any title the prompt
            // could offer is clickable. (Static table wins on key collisions — it's the canonical URL.)
            var linkTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var livePool = App.Mods?.GetVideoLinks();
            if (livePool != null)
            {
                foreach (var kvp in livePool)
                {
                    if (Uri.TryCreate(kvp.Value, UriKind.Absolute, out var pu) && pu.Scheme == "https")
                        linkTable[kvp.Key] = kvp.Value;
                }
            }
            foreach (var kvp in KnownVideoLinks)
                linkTable[kvp.Key] = kvp.Value;
            // Also fold in the FULL built-in catalogue (all videos + the BambiCloud audio
            // playlists). The prompt already controls what gets SUGGESTED per mod; the linker
            // should be permissive so anything offered renders clickable. Without this, a mod
            // swap replaces KnownVideoLinks with the mod's video-only pool, dropping the audio
            // playlists — so a bare "IQ Programming" (named without markdown) wouldn't link.
            if (_builtInVideoLinks != null)
            {
                foreach (var kvp in _builtInVideoLinks)
                    if (!linkTable.ContainsKey(kvp.Key))
                        linkTable[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in linkTable.OrderByDescending(k => k.Key.Length)) // Longest first to avoid partial matches
            {
                var idx = processedText.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    // Check if this position overlaps with an already found link
                    bool overlaps = linkPositions.Any(lp =>
                        (idx >= lp.start && idx < lp.start + lp.length) ||
                        (idx + kvp.Key.Length > lp.start && idx + kvp.Key.Length <= lp.start + lp.length));

                    if (!overlaps)
                    {
                        linkPositions.Add((idx, kvp.Key.Length, kvp.Key, kvp.Value));
                    }
                }
            }

            // Also detect raw URLs in the text (the AI sometimes outputs full URLs from the link pool)
            var urlRegex = new Regex(@"https?://[^\s,""'<>]+", RegexOptions.IgnoreCase);
            foreach (Match match in urlRegex.Matches(text))
            {
                bool overlaps = linkPositions.Any(lp =>
                    (match.Index >= lp.start && match.Index < lp.start + lp.length) ||
                    (match.Index + match.Length > lp.start && match.Index + match.Length <= lp.start + lp.length));

                if (!overlaps)
                {
                    linkPositions.Add((match.Index, match.Length, match.Value, match.Value));
                }
            }

            // Sort by position
            linkPositions = linkPositions.OrderBy(lp => lp.start).ToList();

            if (linkPositions.Count == 0)
            {
                // No known videos found - just show plain text
                target.Add(new Run(text));
                return;
            }

            // Build inlines with links
            int lastIndex = 0;
            foreach (var (start, length, name, url) in linkPositions)
            {
                // Add text before the link
                if (start > lastIndex)
                {
                    target.Add(new Run(text.Substring(lastIndex, start - lastIndex)));
                }

                // Get the actual text from the original (preserving case)
                var actualText = text.Substring(start, length);

                // A bare URL is ugly as link text. Replace it with the canonical pool title when
                // the URL is known, else a readable title derived from the slug ("this video" if
                // even that fails) — so a model that emits a URL still gets a clean clickable link.
                var displayText = actualText;
                if (actualText.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var known = linkTable.FirstOrDefault(kvp =>
                        string.Equals(kvp.Value, url, StringComparison.OrdinalIgnoreCase)).Key;
                    displayText = known ?? Helpers.HtUrlHelper.DeriveTitleFromUrl(url);
                }

                try
                {
                    var hyperlink = new Hyperlink(new Run(displayText))
                    {
                        NavigateUri = new Uri(url),
                        Foreground = new SolidColorBrush(Color.FromRgb(255, 176, 224)), // Light pink
                        TextDecorations = TextDecorations.Underline
                    };
                    hyperlink.RequestNavigate += SpeechBubbleHyperlink_RequestNavigate;
                    target.Add(hyperlink);
                    App.Logger?.Information("Auto-linked video: '{Name}' -> {Url}", displayText, url);
                }
                catch
                {
                    target.Add(new Run(displayText));
                }

                lastIndex = start + length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                target.Add(new Run(text.Substring(lastIndex)));
            }
        }

        /// <summary>
        /// Loaded handler for chat history TextBlocks. Pulls the message text from Tag
        /// (we can't bind to Text because then we couldn't write Inlines) and renders it
        /// through the same hyperlink builder used by the live bubble.
        /// </summary>
        private void ChatHistoryText_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock tb && tb.Tag is string text)
                BuildLinkedInlines(text, tb.Inlines);
        }

        /// <summary>
        /// Handles clicks on hyperlinks in the speech bubble.
        /// Routes to the embedded browser with correct tab selection (BambiCloud/HypnoTube).
        /// Opens in fullscreen mode for immersive viewing.
        /// </summary>
        private void SpeechBubbleHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // Block link clicks during remote control — navigating the browser
                // while a web video is playing fullscreen breaks the playback state
                if (App.RemoteControl?.ControllerConnected == true)
                {
                    App.Logger?.Debug("Speech bubble link blocked - remote controller is connected");
                    e.Handled = true;
                    return;
                }

                var url = e.Uri.AbsoluteUri;
                App.Logger?.Information("Speech bubble link clicked - Raw URI: {Uri}, AbsoluteUri: {Url}", e.Uri, url);

                // Find MainWindow - it might not be Application.Current.MainWindow if AvatarTube is detached
                var mainWindow = _parentWindow as MainWindow
                    ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                App.Logger?.Information("MainWindow found: {Found}", mainWindow != null);

                if (mainWindow?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true)
                {
                    App.Logger?.Information("Speech bubble link routed to embedded browser: {Url}", url);
                }
                else
                {
                    // Fallback: open in external browser (HTTPS only for safety)
                    if (Uri.TryCreate(url, UriKind.Absolute, out var fallbackUri) && fallbackUri.Scheme == "https")
                    {
                        App.Logger?.Warning("Embedded browser unavailable, opening externally: {Url}", url);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                }

                e.Handled = true;
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open speech bubble link: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Plays a quick double bounce animation to attract attention.
        /// Used when AI or awareness responses are shown.
        /// </summary>
        private void PlayDoubleBounce()
        {
            // Create a double bounce animation: up-down-up-down
            var bounceAnimation = new DoubleAnimationUsingKeyFrames
            {
                // CRITICAL: Stop after completion so timer-based float animation can resume
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
            };

            // First bounce (larger)
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))));

            // Second bounce (smaller)
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
            bounceAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));

            // Apply to both avatar images (static and animated)
            AvatarTranslate.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
            AvatarAnimatedTranslate.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
        }

        private void StartIdleTimer()
        {
            var interval = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _idleTimer.Tick += OnIdleTick;
            _idleTimer.Start();
        }

        private void ResetIdleTimer()
        {
            _idleTimer?.Stop();
            StartIdleTimer();

            // Also report user activity to autonomy service
            App.Autonomy?.ReportUserActivity();
        }

        /// <summary>
        /// Restart idle timer with the current setting. Call when the user changes
        /// IdleGiggleIntervalSeconds via the slider so the running timer picks up
        /// the new value immediately instead of waiting for an unrelated reset.
        /// </summary>
        public void RestartIdleTimer()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RestartIdleTimer)); return; }
            _idleTimer?.Stop();
            StartIdleTimer();
        }

        private void OnIdleTick(object? sender, EventArgs e)
        {
            // Re-read setting on every tick so slider changes self-heal even if
            // RestartIdleTimer is never called (e.g. slider changed while no
            // speech is happening). DispatcherTimer applies the new Interval
            // after the current tick completes.
            var configured = App.Settings?.Current?.IdleGiggleIntervalSeconds ?? 120;
            if (_idleTimer != null && Math.Abs(_idleTimer.Interval.TotalSeconds - configured) > 0.5)
            {
                _idleTimer.Interval = TimeSpan.FromSeconds(configured);
            }

            // Skip if speech is on cooldown or currently showing
            if (!IsSpeechReady()) return;

            // Idle chatter IS the bark system now: this timer only sets the cadence (the
            // companion-tab slider); the line itself comes from the Idle bark pool with its
            // pool-wide no-repeat rotation. Preset phrases are just the no-barks fallback.
            // (flashes_audio voicelines are flash material and no longer spoken here.)
            if (App.Bark != null) App.Bark.DispatchIdle();
            else Giggle(GetRandomBambiPhrase());
        }

        // ============================================================
        // TRIGGER MODE - Random trigger phrases (free for all)
        // ============================================================

        private void StartTriggerTimer()
        {
            if (App.Settings?.Current?.TriggerModeEnabled != true)
            {
                App.Logger?.Debug("TriggerMode: Not enabled, skipping timer start");
                return;
            }

            var interval = App.Settings?.Current?.TriggerIntervalSeconds ?? 60;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _triggerTimer.Tick += OnTriggerTick;
            _triggerTimer.Start();

            App.Logger?.Information("TriggerMode: Started with {Interval}s interval", interval);

            // Show first trigger immediately (after short delay for window to be ready)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ =>
                {
                    if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                    Dispatcher.BeginInvoke(new Action(() => OnTriggerTick(null, EventArgs.Empty)));   // async: avoid shutdown deadlock
                });
            }));
        }

        private void StopTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            App.Logger?.Debug("TriggerMode: Timer stopped");
        }

        /// <summary>
        /// Restart trigger timer (call when settings change)
        /// </summary>
        public void RestartTriggerTimer()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RestartTriggerTimer)); return; }
            StopTriggerTimer();
            StartTriggerTimer();
        }

        // ============================================================
        // RANDOM BUBBLE TIMER - Spawns clickable bubbles near avatar
        // ============================================================

        private void StartRandomBubbleTimer()
        {
            if (App.Settings?.Current?.RandomBubbleEnabled != true)
            {
                App.Logger?.Debug("RandomBubble: Not enabled, skipping timer start");
                return;
            }

            // Random interval between 3-5 minutes (180-300 seconds)
            var interval = _random.Next(180, 301);
            _randomBubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _randomBubbleTimer.Tick += OnRandomBubbleTick;
            _randomBubbleTimer.Start();

            App.Logger?.Information("RandomBubble: Started with {Interval}s interval", interval);
        }

        private void StopRandomBubbleTimer()
        {
            _randomBubbleTimer?.Stop();
            _randomBubbleTimer = null;
            App.Logger?.Debug("RandomBubble: Timer stopped");
        }

        /// <summary>
        /// Restart random bubble timer (call when settings change)
        /// </summary>
        public void RestartRandomBubbleTimer()
        {
            StopRandomBubbleTimer();
            StartRandomBubbleTimer();
        }

        private void OnRandomBubbleTick(object? sender, EventArgs e)
        {
            // Re-randomize interval for next tick (3-5 minutes)
            if (_randomBubbleTimer != null)
            {
                var nextInterval = _random.Next(180, 301);
                _randomBubbleTimer.Interval = TimeSpan.FromSeconds(nextInterval);
            }

            // Skip if avatar is not in focus (another app is in foreground)
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != _tubeHandle && foregroundWindow != _parentHandle)
            {
                GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
                uint ourPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                if (foregroundPid != ourPid)
                {
                    App.Logger?.Debug("RandomBubble: Skipped - app not in focus");
                    return;
                }
            }

            // Show phrase and spawn bubble
            SpawnRandomBubble();
        }

        private void SpawnRandomBubble()
        {
            // Pick a random phrase (mode-aware, filtered by service)
            GiggleFromCategory("RandomBubble");

            // Spawn a bubble near the avatar after 1 second (speech bubble appears first)
            Task.Delay(1000).ContinueWith(_ =>
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return;
                Dispatcher.BeginInvoke(new Action(() =>   // async: avoid shutdown deadlock
                {
                    try
                    {
                        // Get avatar position in screen coordinates
                        var avatarPos = AvatarBorder.PointToScreen(new Point(
                            AvatarBorder.ActualWidth / 2,
                            AvatarBorder.ActualHeight / 2));

                        // Create and show the bubble
                        var bubble = new AvatarRandomBubble(avatarPos, _random, OnRandomBubblePopped);
                        App.Logger?.Debug("RandomBubble: Spawned at ({X}, {Y})", avatarPos.X, avatarPos.Y);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Warning("RandomBubble: Failed to spawn - {Error}", ex.Message);
                    }
                }));
            });
        }

        private void OnRandomBubblePopped()
        {
            // Play pop sound (use same sound as bubble service)
            PlayBubblePopSound();

            // Award XP
            App.Progression?.AddXP(5, XPSource.AvatarInteraction);

            // Show reaction
            Giggle("Good girl! *giggles*");
        }

        private void PlayBubblePopSound()
        {
            try
            {
                var soundsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "bubbles");
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[_random.Next(popFiles.Length)];
                var popPath = System.IO.Path.Combine(soundsPath, chosenPop);

                if (System.IO.File.Exists(popPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var bubblesVolume = (App.Settings?.Current?.BubblesVolume ?? 50) / 100f;
                    var normalizedVolume = Math.Max(0.05f, (float)Math.Pow(bubblesVolume * masterVolume, 1.5));

                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(popPath);
                            audioFile.Volume = normalizedVolume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("RandomBubble: Failed to play pop sound - {Error}", ex.Message);
            }
        }

        // ============================================================
        // VOICE LINE SYSTEM - Audio files used for idle/trigger comments
        // ============================================================

        /// <summary>
        /// Refreshes the list of voice line files from the flash audio folder
        /// </summary>
        private void RefreshVoiceLines()
        {
            try
            {
                if (!System.IO.Directory.Exists(_voiceLinesPath))
                {
                    _voiceLineFiles.Clear();
                    return;
                }

                var extensions = new[] { "*.mp3", "*.wav", "*.ogg" };
                var files = new List<string>();
                foreach (var ext in extensions)
                {
                    files.AddRange(System.IO.Directory.GetFiles(_voiceLinesPath, ext));
                }
                _voiceLineFiles = files;
                App.Logger?.Debug("VoiceLines: Loaded {Count} voice line files", _voiceLineFiles.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("VoiceLines: Failed to load - {Error}", ex.Message);
                _voiceLineFiles.Clear();
            }
        }

        /// <summary>
        /// Gets a random voice line file path (filtered by phrase manager)
        /// </summary>
        private string? GetRandomVoiceLinePath()
        {
            // Use service filtering if available
            var enabledFiles = App.CompanionPhrases?.GetEnabledVoiceLineFiles();
            if (enabledFiles != null && enabledFiles.Count > 0)
                return enabledFiles[_random.Next(enabledFiles.Count)];

            // Fallback to unfiltered list
            if (_voiceLineFiles.Count == 0)
                RefreshVoiceLines();

            if (_voiceLineFiles.Count == 0)
                return null;

            return _voiceLineFiles[_random.Next(_voiceLineFiles.Count)];
        }

        /// <summary>
        /// Plays a voice line audio file. Follows the master volume only; when MasterVolume is 0 the
        /// bubble + text still show, only the attached voiceline goes silent. (No longer tied to the
        /// subliminal "Mute Whispers" toggle — see bug #436.)
        /// </summary>
        private void PlayVoiceLineAudio(string filePath)
        {
            // Companion voice follows the master volume only (like the giggle/pop SFX) — it is no
            // longer gated behind the subliminal "Mute Whispers" toggle, which defaulted OFF and left
            // the companion silent even with audio up at max (bug #436).
            var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
            if (masterVolume <= 0f) return;
            // Idle voicelines were full-volume (×1.0); -20% so today's hotter v3 lines sit under the barks.
            var volume = (float)Math.Pow(masterVolume, 1.5) * 0.8f;
            PlaySpokenAudio(filePath, volume);
        }

        /// <summary>Stops any currently playing spoken line (kept for external callers).</summary>
        public void StopVoiceLineAudio()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(StopVoiceLineAudio)); return; }
            StopSpokenAudio();
        }

        /// <summary>
        /// True while a spoken companion clip (wake ack, bark voice, idle line, etc.) is actually
        /// playing. The voice-command listener polls this to hold the mic open until she's finished,
        /// so on speakers (not headphones) it doesn't capture her own voice and match it as a command.
        /// </summary>
        public bool IsSpeakingAudio => _isSpeakingAudio;

        /// <summary>
        /// The single companion-voice channel. Cuts off whatever is currently speaking, then plays
        /// <paramref name="filePath"/>. Drives <see cref="_isSpeakingAudio"/> for the speaking wobble/mist,
        /// for exactly the clip's duration. All voice paths (bark / event / idle) route through here so two
        /// lines never overlap.
        /// </summary>
        private void PlaySpokenAudio(string filePath, float volume)
        {
            try
            {
                if (!System.IO.File.Exists(filePath)) return;
                StopSpokenAudio(); // cut off the previous line → no overlap

                NAudio.Wave.AudioFileReader reader;
                NAudio.Wave.WaveOutEvent player;
                try
                {
                    reader = new NAudio.Wave.AudioFileReader(filePath) { Volume = volume };
                    player = new NAudio.Wave.WaveOutEvent();
                    player.Init(reader);
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("PlaySpokenAudio: init failed - {Error}", ex.Message);
                    return;
                }

                lock (_spokenLock) { _spokenReader = reader; _spokenPlayer = player; _isSpeakingAudio = true; }

                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        player.Play();
                        while (player.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            System.Threading.Thread.Sleep(40);
                    }
                    catch { /* ignore audio errors */ }
                    finally
                    {
                        lock (_spokenLock)
                        {
                            if (ReferenceEquals(_spokenPlayer, player)) // not already replaced by a newer line
                            {
                                _spokenPlayer = null;
                                _spokenReader = null;
                                _isSpeakingAudio = false; // stops the speaking wobble/mist exactly at clip end
                            }
                        }
                        try { player.Dispose(); } catch { }
                        try { reader.Dispose(); } catch { }
                    }
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("PlaySpokenAudio failed - {Error}", ex.Message);
            }
        }

        /// <summary>Stop the current spoken line immediately (its play-loop sees Stopped and disposes).</summary>
        public void StopSpokenAudio()
        {
            NAudio.Wave.WaveOutEvent? p;
            lock (_spokenLock) { p = _spokenPlayer; }
            try { p?.Stop(); } catch { }
        }

        /// <summary>
        /// Shows a voice line as a speech bubble with synchronized audio playback
        /// </summary>
        private void ShowVoiceLineBubble(string filePath)
        {
            RunOnAvatar(() =>
            {
                if (_isMuted || !IsAvatarVisibleOnScreen) return;

                var text = App.CompanionPhrases?.GetVoiceLineDisplayText(filePath)
                    ?? System.IO.Path.GetFileNameWithoutExtension(filePath);
                // Activity voicelines bake the "{0}" app placeholder into their
                // filename ("target application"); swap it for the focused app so
                // the bubble names what's actually on screen instead of the literal.
                text = Services.CompanionPhraseService.ResolveVoiceLinePlaceholder(text);
                if (string.IsNullOrWhiteSpace(text)) return;

                // Clear the queue - voice line takes priority
                _speechQueue.Clear();
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop();

                _isGiggling = true;
                PopulateSpeechBubble(text);
                var plainText = MarkdownLinkRegex.Replace(text ?? "", "$1");
                AdjustBubbleSize(plainText);

                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                // Play the voice line audio in sync with the bubble
                PlayVoiceLineAudio(filePath);

                // Drive the portrait emotion: idle affirmations are unmapped → seductive mix
                // (alluring/dreamy/entrancing/teasing), pose count scaled to the line's audio length.
                PlayEmotionForLine(System.IO.Path.GetFileNameWithoutExtension(filePath), filePath, text);

                App.Logger?.Information("VoiceLine: Displayed '{Text}'", text);

                // Calculate display duration based on text length
                double baseDuration = 5.0;
                double perCharDuration = 0.05;
                double calculatedDuration = baseDuration + (text.Length * perCharDuration);
                double displayDuration = Math.Clamp(calculatedDuration, 5.0, 14.0);

                var textLength = text.Length;
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };
                _speechTimer.Tick += (s, e) =>
                {
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return;
                    }
                    _speechTimer.Stop();
                    _isGiggling = false;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    StopZOrderRefreshTimer();

                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = SpeechSource.Preset;
                    _lastSpeechLength = textLength;
                    ProcessNextSpeech();
                };
                _speechTimer.Start();
            });
        }

        private void OnTriggerTick(object? sender, EventArgs e)
        {
            // Skip if speech is on cooldown or currently showing
            if (!IsSpeechReady()) return;

            // Trigger mode speaks triggers only — the flashes_audio voicelines are flash
            // material, no longer borrowed here (they repeated badly: uniform random, no memory).
            var triggers = App.Settings?.Current?.CustomTriggers;
            if (triggers == null || triggers.Count == 0)
            {
                App.Logger?.Debug("TriggerMode: No triggers configured");
                return;
            }

            // Pick a random trigger from subliminal pool
            var trigger = triggers[_random.Next(triggers.Count)];

            // Show it as a speech bubble
            ShowTriggerBubble(trigger);
        }

        private void ShowTriggerBubble(string trigger)
        {
            // Use direct dispatcher invoke to ensure audio plays exactly when bubble shows
            RunOnAvatar(() =>
            {
                // When muted, still trigger haptic+audio but skip visual queue logic
                if (_isMuted)
                {
                    ShowTriggerBubbleImmediate(trigger); // Will handle haptic+audio even when muted
                    return;
                }

                // Check if we need to delay based on last speech (delay based on PREVIOUS speech properties)
                double timeSinceLastSpeech = (DateTime.Now - _lastSpeechEndTime).TotalSeconds;
                double requiredDelay = CalculateRequiredDelayAfterLastSpeech();

                if (_isGiggling || timeSinceLastSpeech < requiredDelay)
                {
                    // Queue the trigger and let delay system handle it
                    _speechQueue.Enqueue((trigger, SpeechSource.Trigger, null, null));
                    App.Logger?.Debug("Queued trigger speech: {Trigger}", trigger);
                    if (!_isGiggling)
                    {
                        _isGiggling = true;
                        ProcessNextSpeech();
                    }
                    return;
                }

                // Show the speech bubble immediately
                ShowTriggerBubbleImmediate(trigger);
            });
        }

        /// <summary>
        /// Internal method to show trigger bubble immediately (called after delay if needed)
        /// </summary>
        private void ShowTriggerBubbleImmediate(string trigger)
        {
            // ALWAYS trigger haptic, even when muted/off-screen
            _ = App.Haptics?.TriggerSubliminalPatternAsync(trigger);

            // Skip visual and audio if muted OR avatar not visible on screen
            if (_isMuted || !IsAvatarVisibleOnScreen)
            {
                _isGiggling = false;
                // Track timing and properties even when hidden (for delay calculation)
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = SpeechSource.Trigger;
                _lastSpeechLength = trigger.Length;
                ProcessNextSpeech();
                App.Logger?.Information("TriggerMode: Haptic only for '{Trigger}' (avatar not visible)", trigger);
                return;
            }

            _isGiggling = true;

            // Defer the trigger audio + bubble by the same lead-in as spoken lines, and type the word
            // out with the slow (non-AI) typewriter.
            Action speak = () =>
            {
                if (!_isGiggling) return; // a newer bubble took over during the lead-in

                // Play trigger audio only when avatar is visible
                App.Subliminal?.PlayTriggerAudio(trigger);

                StartTypewriter(trigger, slow: true);
                var plainText = MarkdownLinkRegex.Replace(trigger ?? "", "$1");
                AdjustBubbleSize(plainText);

                // Force layout update before showing to prevent flickering
                ApplyBubbleBackgroundForMod();
                SpeechBubble.UpdateLayout();
                SpeechBubble.Visibility = Visibility.Visible;

                // Start z-order refresh to keep bubble on top of main window
                // Skip all z-order work when pop quiz is open — must not cover the quiz
                if (!(PopQuizWindow.IsOpen || QuizWindow.IsOpen))
                {
                    StartZOrderRefreshTimer();
                    BringAttachedPairToFront();
                }

                App.Logger?.Information("TriggerMode: Displayed trigger '{Trigger}'", trigger);

                // Calculate display duration based on text length (+ typewriter runtime so the word
                // doesn't vanish before it's finished typing).
                double baseDuration = 5.0;
                double perCharDuration = 0.05;
                double calculatedDuration = baseDuration + (trigger.Length * perCharDuration);
                double displayDuration = Math.Clamp(calculatedDuration, 5.0, 14.0)
                                         + EstimateTypewriterDurationMs(trigger.Length, slow: true) / 1000.0;

                // Hide after calculated duration
                _speechTimer?.Stop();
                _speechTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(displayDuration) };

                // Capture trigger length for delay calculation
                var triggerLength = trigger.Length;

                _speechTimer.Tick += (s, e) =>
                {
                    // If mouse is over speech bubble, keep it open - recheck in 1 second
                    if (_isMouseOverSpeechBubble)
                    {
                        _speechTimer.Interval = TimeSpan.FromSeconds(1);
                        return; // Don't stop timer, keep checking
                    }

                    _speechTimer.Stop();
                    StopZOrderRefreshTimer();
                    SpeechBubble.Visibility = Visibility.Collapsed;
                    _isShowingAiBubble = false; // Clear AI bubble flag when any bubble hides

                    // Track this speech's properties for delay calculation on next speech
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = SpeechSource.Trigger;
                    _lastSpeechLength = triggerLength;

                    // Process next speech with proper delay handling
                    ProcessNextSpeech();
                };
                _speechTimer.Start();

                // Reset idle timer when speaking
                ResetIdleTimer();
            };

            _speechLeadInTimer?.Stop();
            _speechLeadInTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SpeechLeadInSeconds) };
            _speechLeadInTimer.Tick += (s, e) =>
            {
                _speechLeadInTimer?.Stop();
                _speechLeadInTimer = null;
                speak();
            };
            _speechLeadInTimer.Start();
        }

        /// <summary>
        /// Greeting phrases when the app starts
        /// </summary>
        private static readonly string[] GreetingPhrases = new[]
        {
            "Hi Bambi! Ready to get conditioned?~",
            "*bounces* Yay! You're back!",
            "Welcome back, bestie!~",
            "Ooh! Time for some fun~",
            "Hi cutie! Let's get ditzy!",
            "*giggles* There you are!~",
            "Ready to drop, good girl?",
            "Pink thoughts incoming!~"
        };

        /// <summary>
        /// Phrases when the engine stops
        /// </summary>
        private static readonly string[] EngineStopPhrases = new[]
        {
            "I feel dizzy...",
            "Aw... Bambi was having fun...",
            "*blinks* W-what happened?",
            "Mmmm that was nice~",
            "Already? But we were vibing!",
            "My head feels so fuzzy...",
            "*wobbles* Whoa...",
            "Can we do that again soon?~",
            "So floaty right now...",
            "*dreamy sigh* That was good~"
        };

        /// <summary>
        /// Phrases when spawning a random bubble
        /// </summary>
        private static readonly string[] RandomBubblePhrases = new[]
        {
            "Be a good girl and burst that bubble!",
            "Oh... here's a bubble for you~",
            "*Pop* Catch it, Bambi!",
            "Bubble time! Pop it~",
            "Look! A pretty bubble!",
            "*giggles* Pop it quick!",
            "Ooh, get the bubble!",
            "Pop it for me, good girl~"
        };

        /// <summary>
        /// Generic phrases that work for both modes
        /// </summary>
        /// <summary>
        /// Get a random themed phrase based on current content mode
        /// </summary>
        private string GetRandomBambiPhrase()
        {
            var svc = App.CompanionPhrases;

            var genericEnabled = svc?.GetEnabledPhrases("Generic") ?? App.Mods?.GetPhrases("Generic") ?? System.Array.Empty<string>();
            var floatingEnabled = svc?.GetEnabledPhrases("RandomFloating") ?? App.Mods?.GetPhrases("RandomFloating") ?? System.Array.Empty<string>();
            var allPhrases = genericEnabled.Concat(floatingEnabled).ToArray();

            if (allPhrases.Length == 0)
            {
                // Fallback if all phrases disabled
                var fallback = (App.Mods?.GetPhrases("Generic") ?? System.Array.Empty<string>())
                    .Concat(App.Mods?.GetPhrases("RandomFloating") ?? System.Array.Empty<string>()).ToArray();
                if (fallback.Length == 0) return "*giggles*";
                return fallback[_random.Next(fallback.Length)];
            }

            return allPhrases[_random.Next(allPhrases.Length)];
        }

        /// <summary>
        /// Picks a random enabled phrase from a category and giggles it,
        /// playing any custom audio that's been attached to it.
        /// </summary>
        private void GiggleFromCategory(string category)
        {
            var svc = App.CompanionPhrases;
            var enabled = svc?.GetEnabledPhrases(category);

            if (enabled == null || enabled.Length == 0)
                return; // All phrases in this category disabled

            var text = enabled[_random.Next(enabled.Length)];

            // Resolve phrase audio
            string? audioPath = null;
            var phraseId = svc?.GetPhraseId(category, text);
            if (phraseId != null)
            {
                var audioFile = GetPhraseAudioFile(phraseId);
                if (audioFile != null)
                    audioPath = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, audioFile);
            }

            // No manual override? Fall back to a mod-shipped event-audio file keyed by the line text.
            // Sissy voices these; mods without an event_audio folder resolve to null and stay text-only.
            audioPath ??= Services.CompanionPhraseService.ResolveEventAudio(text);

            Giggle(text, audioPath);
        }

        /// <summary>
        /// Gets the audio filename for a phrase ID (checks overrides and custom).
        /// </summary>
        private string? GetPhraseAudioFile(string phraseId)
        {
            var settings = App.Settings?.Current;
            if (settings == null) return null;

            if (settings.PhraseAudioOverrides.TryGetValue(phraseId, out var overrideFile))
            {
                var path = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, overrideFile);
                if (System.IO.File.Exists(path)) return overrideFile;
            }

            var custom = settings.CustomCompanionPhrases?.FirstOrDefault(c => c.Id == phraseId);
            if (custom?.AudioFileName != null)
            {
                var path = System.IO.Path.Combine(Services.CompanionPhraseService.CompanionAudioFolder, custom.AudioFileName);
                if (System.IO.File.Exists(path)) return custom.AudioFileName;
            }

            return null;
        }

        // Counters for feature awareness
        private int _subliminalCounter = 0;
        private int _flashCounter = 0;

        /// <summary>
        /// Get a random Bambi Sleep themed phrase for a specific activity category.
        /// Phrases may include {0} placeholder for the detected app/service name.
        /// </summary>
        private string GetPhraseForCategory(ActivityCategory category, string detectedName = "")
        {
            // Check for special services first
            var lowerName = detectedName?.ToLowerInvariant() ?? "";
            var svc = App.CompanionPhrases;

            // Discord - special phrases
            if (lowerName.Contains("discord"))
            {
                var discordPhrases = svc?.GetEnabledPhrases("Discord") is { Length: > 0 } dp
                    ? dp : App.Mods?.GetPhrases("Discord") ?? System.Array.Empty<string>();
                if (discordPhrases.Length == 0) return "*giggles*";
                return discordPhrases[_random.Next(discordPhrases.Length)];
            }

            // BambiCloud/Hypnotube - positive reinforcement (training sites)
            if (lowerName.Contains("bambicloud") || lowerName.Contains("hypnotube"))
            {
                var sitePhrases = svc?.GetEnabledPhrases("TrainingSite") is { Length: > 0 } sp
                    ? sp : App.Mods?.GetPhrases("TrainingSite") ?? System.Array.Empty<string>();
                if (sitePhrases.Length == 0) return "*giggles*";
                return sitePhrases[_random.Next(sitePhrases.Length)];
            }

            // Hypno content in tab name - congratulate for bimbofication
            if (lowerName.Contains("bambi") || lowerName.Contains("sissy") || lowerName.Contains("hypno"))
            {
                var hypnoPhrases = svc?.GetEnabledPhrases("HypnoContent") is { Length: > 0 } hp
                    ? hp : App.Mods?.GetPhrases("HypnoContent") ?? System.Array.Empty<string>();
                if (hypnoPhrases.Length == 0) return "*giggles*";
                return hypnoPhrases[_random.Next(hypnoPhrases.Length)];
            }

            var categoryName = category switch
            {
                ActivityCategory.Gaming => "Gaming",
                ActivityCategory.Browsing => "Browsing",
                ActivityCategory.Shopping => "Shopping",
                ActivityCategory.Social => "Social",
                ActivityCategory.Working => "Working",
                ActivityCategory.Media => "Media",
                ActivityCategory.Learning => "Learning",
                ActivityCategory.Idle => "WindowAwarenessIdle",
                _ => "RandomFloating"
            };

            var phrases = svc?.GetEnabledPhrases(categoryName) is { Length: > 0 } enabled
                ? enabled
                : App.Mods?.GetPhrases(categoryName) ?? System.Array.Empty<string>();
            if (phrases.Length == 0) phrases = new[] { "*giggles*" };

            var phrase = phrases[_random.Next(phrases.Length)];

            // Replace {0} placeholder with detected name if present
            if (phrase.Contains("{0}") && !string.IsNullOrEmpty(detectedName))
            {
                phrase = string.Format(phrase, detectedName);
            }
            else if (phrase.Contains("{0}"))
            {
                // Remove placeholder if no name detected
                phrase = phrase.Replace("{0} ", "").Replace("{0}", "").Replace("  ", " ").Trim();
            }

            return phrase;
        }

        /// <summary>
        /// Play a random pop sound when clicking the avatar
        /// </summary>
        /// <summary>
        /// Plays a custom phrase audio file (NAudio pattern from PlayGiggleSound).
        /// Suppressed when "Mute Whispers" is on so the phrase bubble still
        /// appears but the attached audio doesn't play.
        /// </summary>
        /// <summary>
        /// Play a real recorded clip (e.g. the New Year note) uninterruptibly: no giggle sound, no
        /// dom voice, just the file. Holds <c>_isPlayingUninterruptibleClip</c> so nothing preempts
        /// it and shows a silent priority bubble for the clip's duration. No-ops gracefully (returns
        /// false) if the file is missing, so it can be wired before the recording ships. Returns true
        /// only if a real clip actually started — callers latch their once-ever flag on true.
        /// </summary>
        public bool PlayNoteClip(string clipPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(clipPath) || !System.IO.File.Exists(clipPath))
                {
                    App.Logger?.Information("PlayNoteClip: no clip at {Path} — skipping gracefully", clipPath);
                    return false;
                }

                RunOnAvatar(() =>
                {
                    _isPlayingUninterruptibleClip = true;
                    // Silent, top-priority bubble for the clip's duration. Source=AI suppresses the
                    // fallback pop; playSound:false means no giggle; bypassClipLock renders past the latch.
                    ShowGiggle(NoteClipCaption, playSound: false, source: SpeechSource.AI,
                        phraseAudioPath: null, aiGenerated: false, bypassClipLock: true);
                });

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.9f; // a touch louder — it's a once-ever moment

                Task.Run(() =>
                {
                    try
                    {
                        using var audioFile = new NAudio.Wave.AudioFileReader(clipPath);
                        audioFile.Volume = volume;
                        using var outputDevice = new NAudio.Wave.WaveOutEvent();
                        outputDevice.Init(audioFile);
                        outputDevice.Play();
                        while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            System.Threading.Thread.Sleep(50);
                    }
                    catch (Exception ex) { App.Logger?.Warning(ex, "PlayNoteClip: playback failed"); }
                    finally
                    {
                        RunOnAvatar(() => _isPlayingUninterruptibleClip = false);
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "PlayNoteClip failed");
                _isPlayingUninterruptibleClip = false;
                return false;
            }
        }

        /// <summary>
        /// Play a companion bark voiceline. Gated on MasterVolume only (master/mute control). It used
        /// to also require the subliminal "Mute Whispers" toggle (SubAudioEnabled), which defaulted OFF
        /// and silenced the companion out of the box even with audio up (bug #436); the bubble still
        /// shows when MasterVolume is 0, it just has no voiceline audio.
        /// </summary>
        private void PlayBarkVoice(string audioPath)
        {
            try
            {
                if (!System.IO.File.Exists(audioPath)) return;

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 0) / 100f;
                if (masterVolume <= 0f) return; // muted
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.85f;
                PlaySpokenAudio(audioPath, volume); // unified channel → no overlap
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play bark voice: {Error}", ex.Message);
            }
        }

        private void PlayPhraseAudio(string audioPath)
        {
            try
            {
                if (!System.IO.File.Exists(audioPath)) return;
                // Companion voice follows master volume only — no longer gated by the subliminal
                // "Mute Whispers" toggle, which left the companion silent by default (bug #436).

                var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                if (masterVolume <= 0f) return;
                // Event lines were ×0.7; -20% (→ ×0.56) so today's hotter v3 lines sit under the barks.
                var volume = (float)Math.Pow(masterVolume, 1.5) * 0.56f;
                PlaySpokenAudio(audioPath, volume); // unified channel → no overlap
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play phrase audio: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Plays a random giggle sound (giggle1-4.mp3) for AI responses or preset phrases
        /// </summary>
        private void PlayGiggleSound()
        {
            try
            {
                // Bambi Sleep mode: suppress the canned "hehehe" giggle SFX entirely — it sounds cheap
                // next to that mod's real voiceline barks, so a clip-less bubble just stays silent.
                if (IsBambiSleepMod()) return;

                // Use giggle sounds 5-8 for AI responses (reserved for special interactions)
                var giggleFiles = new[] {
                    "giggle5.mp3", "giggle6.mp3", "giggle7.mp3", "giggle8.mp3"
                };
                var chosenGiggle = giggleFiles[_random.Next(giggleFiles.Length)];
                var gigglePath = Services.ModResourceResolver.ResolveAudioPath(chosenGiggle);

                if (System.IO.File.Exists(gigglePath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    // Apply volume curve, cap at 70% of master to not be too loud
                    var volume = (float)Math.Pow(masterVolume, 1.5) * 0.7f;

                    // Mute egg / silenced audio (Fork F): MasterVolume == 0 means don't attempt audio.
                    if (masterVolume <= 0f) return;

                    // Use NAudio for async playback
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(gigglePath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play giggle sound: {Error}", ex.Message);
            }
        }

        private void PlayAvatarPopSound()
        {
            try
            {
                var popFiles = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" };
                var chosenPop = popFiles[_random.Next(popFiles.Length)];
                var popPath = Services.ModResourceResolver.ResolveAudioPath("bubbles/" + chosenPop);

                if (System.IO.File.Exists(popPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5);

                    // Use NAudio for async playback
                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(popPath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play avatar pop sound: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Triggered when user clicks avatar 50 times within 1 minute - plays audio and shows trigger
        /// </summary>
        private void TriggerBambiCumAndCollapse()
        {
            App.Logger?.Information("Bambi Cum and Collapse triggered! (50 clicks in 1 minute)");

            // Play the "cum and collapse" audio
            try
            {
                var soundsPath = Services.CompanionPhraseService.VoiceLineFolder;
                var collapseFiles = new[] { "come and coll.mp3", "come and coll (1).mp3", "come and coll (2).mp3" };
                var chosenFile = collapseFiles[_random.Next(collapseFiles.Length)];
                var audioPath = System.IO.Path.Combine(soundsPath, chosenFile);

                if (System.IO.File.Exists(audioPath))
                {
                    var masterVolume = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
                    var volume = (float)Math.Pow(masterVolume, 1.5);

                    Task.Run(() =>
                    {
                        try
                        {
                            using var audioFile = new NAudio.Wave.AudioFileReader(audioPath);
                            audioFile.Volume = volume;
                            using var outputDevice = new NAudio.Wave.WaveOutEvent();
                            outputDevice.Init(audioFile);
                            outputDevice.Play();
                            while (outputDevice.PlaybackState == NAudio.Wave.PlaybackState.Playing)
                            {
                                System.Threading.Thread.Sleep(50);
                            }
                        }
                        catch { /* Ignore audio errors */ }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to play Bambi Cum and Collapse audio: {Error}", ex.Message);
            }

            // Show the trigger message with priority
            GigglePriority("BAMBI CUM AND COLLAPSE", aiGenerated: false);
        }

        private int _bubblePopCounter = 0;

        // GameFailed, BubbleMissed, FlashClicked, LevelUp, MindWipe, BrainDrain
        // phrases provided by App.Mods (ModService)

        // Counters for MindWipe/BrainDrain (not too often)
        private int _mindWipeCounter = 0;
        private int _brainDrainCounter = 0;

        /// <summary>
        /// React to companion switch (v5.3).
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _companionGreetingDebounce;

        /// <summary>
        /// Show a greeting when the app starts
        /// </summary>
        // Ensures the warm welcome-back greeting fires at most once per launch, even if the
        // avatar window is re-created (e.g. attach/detach). Process-lifetime.
        private static bool _absenceGreetingShownThisLaunch = false;

        private void ShowGreeting()
        {
            // Subsequent avatar re-creations within the same launch keep the original
            // startup-greeting behavior; only the first one gets the absence-aware welcome.
            if (_absenceGreetingShownThisLaunch)
            {
                GiggleFromCategory("StartupGreeting");
                return;
            }
            _absenceGreetingShownThisLaunch = true;

            var settings = App.Settings?.Current;

            // Snapshot the previous local "last seen" timestamp BEFORE refreshing it.
            DateTime? lastSeen = settings?.LastSeenUtc;

            // Refresh last-seen on app open (local only). We write on open rather than on
            // close so the value is self-contained to this method and survives crashes /
            // force-kills; the tiny imprecision (open-to-open vs. activity-to-open) is fine
            // for a warm greeting. suppressCloudBackup keeps this purely on-device — the
            // timestamp is never added to any server call, sync payload, or telemetry. (It is
            // also listed in ProfileSyncService.ExcludedBackupProperties as defense in depth.)
            if (settings != null)
            {
                settings.LastSeenUtc = DateTime.UtcNow;
                App.Settings?.Save(suppressCloudBackup: true);
            }

            // Voiced, time-aware welcome via the bark system: the greeting bark shows its line in
            // the bubble AND plays audio (per-mod flavored, no-repeat). We pass an away-bucket so
            // rules can pick a line matching how long the user's been gone. If no greeting bark
            // fired (bark system disabled, or no matching rule) we fall back to the legacy
            // text-only absence greeting so the bubble is never silent.
            bool spoke = App.Bark?.NotifyAppOpened(GreetingAwayBucket(lastSeen)) ?? false;
            if (!spoke)
            {
                var greeting = BuildAbsenceGreeting(lastSeen);
                if (greeting == null)
                    GiggleFromCategory("StartupGreeting"); // first run, no prior timestamp
                else
                    Giggle(greeting);
            }

            // Celebrate a daily-streak milestone once (queues after the welcome line).
            CheckStreakMilestoneGreeting();
        }

        /// <summary>Daily-login-streak day counts the companion calls out on app open. Ascending.</summary>
        private static readonly int[] StreakMilestoneDays = { 7, 14, 30, 60, 100, 365 };

        /// <summary>
        /// Buckets the time-since-last-seen into a coarse label the AppOpened bark rules key off
        /// (mirrors the thresholds in <see cref="BuildAbsenceGreeting"/>). "first" = no prior
        /// timestamp (first run on this device).
        /// </summary>
        private static string GreetingAwayBucket(DateTime? lastSeen)
        {
            if (lastSeen == null) return "first";
            var elapsed = DateTime.UtcNow - lastSeen.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            if (elapsed < TimeSpan.FromHours(6)) return "soon";
            if (elapsed < TimeSpan.FromHours(18)) return "back";
            if (elapsed < TimeSpan.FromDays(3)) return "while";
            return "long";
        }

        /// <summary>
        /// Fires a one-time voiced celebration the first time the daily login streak reaches a
        /// milestone (7/14/30/60/100/365 days). The latch lives in
        /// <see cref="AppSettings.LastAnnouncedStreakMilestone"/>: we announce only when a higher
        /// milestone is newly reached, and silently reset the latch downward if the streak drops
        /// so re-reaching a milestone announces again. Tone is celebratory only — never loss
        /// pressure (matching the welcome greeting's intent).
        /// </summary>
        private void CheckStreakMilestoneGreeting()
        {
            var settings = App.Settings?.Current;
            if (settings == null || App.Bark == null) return;

            int streak = settings.CurrentStreak;
            // Don't touch the latch on a transient/unsynced streak. At app-open CurrentStreak is
            // synced from achievements/cloud SEPARATELY and reads 0 for a beat before that lands
            // (the "streak flashes 0 after re-login" race). If we fell through with streak=0 we'd
            // reset LastAnnouncedStreakMilestone down to 0, and the next launch (streak correct
            // again) would re-announce the highest passed milestone — that's the "30 days replays
            // at a 50-day streak" bug. A real 0 streak has no milestone to celebrate anyway, so bail.
            if (streak <= 0) return;
            int reached = 0;
            foreach (var m in StreakMilestoneDays)
                if (m <= streak) reached = m;

            if (reached == settings.LastAnnouncedStreakMilestone) return;

            bool isNewMilestone = reached > settings.LastAnnouncedStreakMilestone;
            settings.LastAnnouncedStreakMilestone = reached; // also resets the latch on a streak drop
            App.Settings?.Save(suppressCloudBackup: true);

            if (isNewMilestone && reached > 0)
                App.Bark.NotifyStreakMilestone(reached);
        }

        /// <summary>
        /// Builds a warm, in-character welcome-back line varied by how long the user has been
        /// away, reusing the existing display name (<see cref="App.UserDisplayName"/>) if one is
        /// set and a generic line otherwise. Returns null when there is no prior timestamp
        /// (first run) so the caller can fall back to the normal startup greeting.
        ///
        /// Tone is welcoming only — never guilt, anxiety, or streak/loss pressure. A long
        /// absence gets a happy "welcome back", never a reproach.
        /// </summary>
        private string? BuildAbsenceGreeting(DateTime? lastSeen)
        {
            if (lastSeen == null) return null;

            // Guard against clock changes / future timestamps: treat as a quick return.
            var elapsed = DateTime.UtcNow - lastSeen.Value;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

            string[] templates;
            if (elapsed < TimeSpan.FromHours(6))
            {
                templates = new[]
                {
                    "Back already? Hehe~ 💕",
                    "Ooh, you're back so soon! ✨",
                    "Couldn't stay away, {name}? 😘",
                };
            }
            else if (elapsed < TimeSpan.FromHours(18))
            {
                templates = new[]
                {
                    "Welcome back, {name}! 💖",
                    "Yay, you're here again! ✨",
                    "Hi again, cutie~ 💕",
                };
            }
            else if (elapsed < TimeSpan.FromDays(3))
            {
                templates = new[]
                {
                    "There you are! So good to see you~ 💕",
                    "You're back, {name}! ✨",
                    "Hehe, hi again! 💖",
                };
            }
            else if (elapsed < TimeSpan.FromDays(7))
            {
                templates = new[]
                {
                    "Yay, you came back! 💕",
                    "So happy you're here, {name}! ✨",
                    "Hi hi! Let's have some fun~ 💖",
                };
            }
            else if (elapsed < TimeSpan.FromDays(30))
            {
                templates = new[]
                {
                    "Look who's back! 💕",
                    "So good to see you again, {name}! ✨",
                    "Welcome back, gorgeous~ 💖",
                };
            }
            else
            {
                templates = new[]
                {
                    "It's been ages — welcome back! 💕✨",
                    "Yaaay, you're back, {name}! So happy to see you! 💖",
                    "Welcome back! Let's pick up right where we left off~ ✨",
                };
            }

            var template = templates[_random.Next(templates.Length)];
            return FormatGreetingName(template, App.UserDisplayName);
        }

        /// <summary>
        /// Substitutes the user's name into a greeting template. If no name is available the
        /// "{name}" token (and a leading ", "/" " separator, if any) is removed cleanly so the
        /// generic line still reads naturally.
        /// </summary>
        private static string FormatGreetingName(string template, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return template.Replace("{name}", name.Trim());

            return template
                .Replace(", {name}", "")
                .Replace(" {name}", "")
                .Replace("{name}", "")
                .Replace("  ", " ")
                .Trim();
        }

        // Default thinking phrases (used when no mod overrides)
        private static readonly string[] DefaultThinkingPhrases = new[]
        {
            "*POP*",
            "*Poppin bubbles...*",
            "*giggles*",
            "*blink blink*",
            "*~*",
            "*teehee*"
        };

        private string GetRandomThinkingPhrase()
        {
            var modPhrases = App.Mods?.GetPhrases("Thinking");
            var phrases = modPhrases != null && modPhrases.Length > 0 ? modPhrases : DefaultThinkingPhrases;
            return phrases[_random.Next(phrases.Length)];
        }

        // Matches a run of trailing dots/ellipsis at the end of the phrase, even when
        // tucked just inside closing wrapper chars () [] * _ ~ / whitespace. Removing
        // it (while leaving the wrappers themselves in place) is what stops the static
        // dots in phrases like "(thinking...)" or "[PROCESSING...]" from doubling up
        // with the thinking animation's own dots.
        private static readonly Regex TrailingDotsInsideWrappersRegex =
            new Regex(@"[.…]+(?=[)\]\s*_~]*$)", RegexOptions.Compiled);

        // Strips dots, ellipsis, whitespace, AND markdown emphasis chars (* _ ~) from
        // both ends of a thinking phrase so the animation's dots aren't duplicated and
        // wrapping characters like "*Poppin bubbles...*" don't render asterisks around
        // the animated dots. Also strips a trailing dot-run sitting just inside () or []
        // wrappers (e.g. "(thinking...)" -> "(thinking)") while preserving those brackets,
        // since paren/bracket-wrapped phrases would otherwise keep their static dots.
        // Trims both ends because phrases come pre-wrapped in *...*.
        private static string StripTrailingDots(string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return phrase;
            // Drop the trailing dot-run first (handles dots tucked inside ) or ] so the
            // brackets survive), then trim the outer wrapper decoration as before.
            var withoutDots = TrailingDotsInsideWrappersRegex.Replace(phrase, "");
            var trimmed = withoutDots.Trim('.', '…', '*', '_', '~', ' ', '\t');
            // If the phrase was nothing but decoration (e.g. "*~*"), keep the original
            // so we don't end up animating just bare dots.
            return string.IsNullOrEmpty(trimmed) ? phrase : trimmed;
        }

        // ============================================================
        // THINKING ANIMATION (rotating phrases + animated dots)
        // ============================================================

        /// <summary>
        /// Begins the "still thinking" animation in the speech bubble. Picks a phrase,
        /// shows it via ShowGiggle (so the bubble is visible/sized/z-ordered), then
        /// rotates phrase + dots every 500ms until <see cref="StopThinkingAnimation"/>
        /// is called. The dismiss timer that ShowGiggle starts is cancelled — the
        /// bubble stays visible until the AI reply lands.
        /// </summary>
        private void StartThinkingAnimation()
        {
            StopThinkingAnimation(); // clear any prior animation

            _isWaitingForAi = true;
            _thinkingPhraseBase = StripTrailingDots(GetRandomThinkingPhrase());
            _thinkingTickCount = 0;
            var generation = ++_thinkingGeneration;

            // Use ShowGiggle for first frame so bubble is sized + visible. playSound:false
            // because the AI reply will play its own sound when it arrives.
            ShowGiggle(_thinkingPhraseBase, playSound: false, source: SpeechSource.AI);

            // Cancel auto-dismiss — the bubble stays up until the reply pre-empts it.
            _speechTimer?.Stop();

            _thinkingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _thinkingTimer.Tick += (s, e) =>
            {
                if (generation != _thinkingGeneration)
                {
                    (s as DispatcherTimer)?.Stop();
                    return;
                }
                ThinkingTick();
            };
            _thinkingTimer.Start();
        }

        /// <summary>
        /// Cancels any in-flight thinking animation. Safe to call repeatedly.
        /// Bumping the generation counter causes any pending tick callbacks to bail.
        /// </summary>
        private void StopThinkingAnimation()
        {
            _thinkingGeneration++;
            _thinkingTimer?.Stop();
            _thinkingTimer = null;
        }

        private void ThinkingTick()
        {
            _thinkingTickCount++;
            if (_thinkingTickCount > 3)
            {
                // Cycle complete — pick a new phrase, restart dots.
                _thinkingPhraseBase = StripTrailingDots(GetRandomThinkingPhrase());
                _thinkingTickCount = 0;
                RenderSpeechBubbleRaw(_thinkingPhraseBase);
            }
            else
            {
                var dots = new string('.', _thinkingTickCount);
                RenderSpeechBubbleRaw(_thinkingPhraseBase + dots);
            }
        }

        /// <summary>
        /// Paints a single string into the speech bubble without going through
        /// PopulateSpeechBubble's hyperlink/markdown processing. Used by the thinking
        /// animation and the typewriter (which re-renders with full processing on
        /// completion).
        /// </summary>
        private void RenderSpeechBubbleRaw(string text)
        {
            TxtSpeech.Inlines.Clear();
            if (!string.IsNullOrEmpty(text))
            {
                TxtSpeech.Inlines.Add(new Run(text));
            }
        }

        // ============================================================
        // TYPEWRITER EFFECT (AI replies stream in char-by-char)
        // ============================================================

        // Tunable: ~18ms/char default, but auto-speed-up so any reply finishes within budget.
        private const int TypewriterMinStepMs = 8;
        private const int TypewriterMaxStepMs = 30;
        private const int TypewriterTotalBudgetMs = 2000;

        // Slower profile for non-AI bubbles (barks, presets, triggers) — deliberately more leisurely
        // than AI replies so short lines still read as "typed out".
        private const int TypewriterSlowMinStepMs = 24;
        private const int TypewriterSlowMaxStepMs = 75;
        private const int TypewriterSlowTotalBudgetMs = 7000;

        private static (int min, int max, int budget) TypewriterProfile(bool slow) => slow
            ? (TypewriterSlowMinStepMs, TypewriterSlowMaxStepMs, TypewriterSlowTotalBudgetMs)
            : (TypewriterMinStepMs, TypewriterMaxStepMs, TypewriterTotalBudgetMs);

        /// <summary>
        /// Types <paramref name="fullText"/> into the speech bubble character by character.
        /// On completion, calls <see cref="PopulateSpeechBubble"/> for the final pass so
        /// video-name hyperlinks and markdown links become clickable.
        /// </summary>
        /// <summary>
        /// Estimates how long the typewriter will take for a given text length, mirroring
        /// the math in <see cref="StartTypewriter"/> and <see cref="TypewriterTick"/>.
        /// Used by <see cref="ShowGiggle"/> to compensate the bubble timer so the slider
        /// value reflects readable time rather than bubble-open time.
        /// </summary>
        private static int EstimateTypewriterDurationMs(int length, bool slow = false)
        {
            if (length <= 0) return 0;
            var (min, max, budget) = TypewriterProfile(slow);
            int charsPerTick = Math.Max(1, length / 100);
            int stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, length)));
            int ticks = (int)Math.Ceiling((double)length / charsPerTick);
            return stepMs * ticks;
        }

        private void StartTypewriter(string fullText, bool slow = false)
        {
            StopTypewriter();

            _typewriterFullText = fullText ?? string.Empty;
            _typewriterIndex = 0;
            var generation = ++_typewriterGeneration;

            // Strip markdown links the same way PopulateSpeechBubble does, so the typewriter
            // shows the same plain text the final render will. Otherwise the bubble would
            // briefly show "[Naughty Bambi](https://...)" before snapping to "Naughty Bambi".
            _typewriterFullText = MarkdownLinkRegex.Replace(_typewriterFullText, "$1");

            // Render starts empty.
            RenderSpeechBubbleRaw(string.Empty);

            if (_typewriterFullText.Length == 0)
            {
                // Nothing to type — fall through to the final render so links get processed.
                PopulateSpeechBubble(fullText);
                return;
            }

            var (min, max, budget) = TypewriterProfile(slow);
            var stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, _typewriterFullText.Length)));

            _typewriterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
            _typewriterTimer.Tick += (s, e) =>
            {
                if (generation != _typewriterGeneration)
                {
                    (s as DispatcherTimer)?.Stop();
                    return;
                }
                TypewriterTick(fullText);
            };
            _typewriterTimer.Start();
        }

        private void StopTypewriter()
        {
            _typewriterGeneration++;
            _typewriterTimer?.Stop();
            _typewriterTimer = null;
        }

        private void TypewriterTick(string originalFullText)
        {
            // Type 1-2 chars per tick depending on length so very long replies don't drag.
            // (stepMs is already auto-scaled, but we can also batch chars per tick.)
            var charsThisTick = Math.Max(1, _typewriterFullText.Length / 100);

            for (int i = 0; i < charsThisTick && _typewriterIndex < _typewriterFullText.Length; i++)
            {
                _typewriterIndex++;
            }

            var partial = _typewriterFullText.Substring(0, _typewriterIndex);
            RenderSpeechBubbleRaw(partial);

            if (_typewriterIndex >= _typewriterFullText.Length)
            {
                _typewriterTimer?.Stop();
                _typewriterTimer = null;
                // Final pass: re-render with the original (un-stripped) text so PopulateSpeechBubble
                // can wire up clickable video links and URL hyperlinks.
                PopulateSpeechBubble(originalFullText);
            }
        }

        /// <summary>
        /// Truncates text to a maximum number of words, adding "..." if truncated
        /// </summary>
        private static string TruncateToWords(string text, int maxWords)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= maxWords) return text;

            return string.Join(" ", words.Take(maxWords)) + "...";
        }

        // ============================================================
        // SPEECH BUBBLE MOUSE HANDLERS
        // ============================================================

        private void SpeechBubble_MouseEnter(object sender, MouseEventArgs e)
        {
            // Keep speech bubble open while mouse is over it
            _isMouseOverSpeechBubble = true;
        }

        private void SpeechBubble_MouseLeave(object sender, MouseEventArgs e)
        {
            // Allow speech bubble to close after mouse leaves
            _isMouseOverSpeechBubble = false;
        }
    }
}
