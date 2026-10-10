// PORTED from AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.1.5): the speech QUEUE (:35 _speechQueue,
// :141 IsSpeechReady, :157 CalculateRequiredDelayAfterLastSpeech, :180 ProcessNextSpeech, :240 Giggle,
// :325 GigglePriority, :413 ShowGiggle), the typewriter (:3013-3110) and the hover-hold cap / stale
// latch (Services/Companion/SpeechLatchRule.cs, :3151 ClearStaleSpeechLatch). Ledger rows tube#T6,
// tube#T17 (typewriter half), tube#T25.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>Where a line came from: decides its sound, its typing speed and the gap after it.</summary>
        internal enum SpeechSource { Preset, Trigger, AI }

        private readonly Queue<(string Text, SpeechSource Source, string? Audio, bool BarkVoice)> _speechQueue = new();
        private DispatcherTimer? _speechDelayTimer;
        private DispatcherTimer? _speechLeadInTimer;
        private SpeechSource _lastSpeechSource = SpeechSource.Preset;
        private int _lastSpeechLength;

        // WPF Speech.cs:120-126, verbatim.
        private const double AiSpeechBonusSeconds = 5.0;
        private const int LongTextThreshold = 100;
        private const double PerCharDelaySeconds = 0.02;
        private const double SpeechLeadInSeconds = 0.6;
        /// <summary>The bubble's outer width cap (WPF Speech.cs:1111 is 340 in its own window; 380 inside the tube canvas).</summary>
        internal const double SpeechBubbleMaxWidth = 380;

        // WPF Speech.cs:3013-3021.
        private const int TypewriterMinStepMs = 8, TypewriterMaxStepMs = 30, TypewriterTotalBudgetMs = 2000;
        private const int TypewriterSlowMinStepMs = 24, TypewriterSlowMaxStepMs = 75, TypewriterSlowTotalBudgetMs = 7000;

        /// <summary>WPF SpeechLatchRule.MaxHoverHold: a hover can hold a bubble open this long, no longer.</summary>
        internal static readonly TimeSpan MaxHoverHold = TimeSpan.FromMinutes(2);

        private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\((https?://[^\)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Lines render whole and at once (no lead-in, no typewriter). True only in a headless test host
        /// (an Application with no lifetime): those tests read the bubble text right after speaking.
        /// The running app always types, as WPF.
        /// </summary>
        internal static bool SpeechInstant =>
            SpeechInstantOverride ?? global::Avalonia.Application.Current?.ApplicationLifetime is null;

        internal static bool? SpeechInstantOverride;

        private DispatcherTimer? _typewriterTimer;
        private string _typewriterFullText = string.Empty;
        private int _typewriterIndex;
        private int _typewriterGeneration;

        // ------------------------------------------------------------------ pacing rules

        /// <summary>WPF CalculateRequiredDelayAfterLastSpeech: 2 s, +5 s after an AI reply, +0.02 s a char past 100.</summary>
        internal static double RequiredDelayAfter(SpeechSource lastSource, int lastLength)
        {
            double delay = MinSpeechDelaySeconds;
            if (lastSource == SpeechSource.AI) delay += AiSpeechBonusSeconds;
            if (lastLength > LongTextThreshold) delay += (lastLength - LongTextThreshold) * PerCharDelaySeconds;
            return delay;
        }

        private double CalculateRequiredDelayAfterLastSpeech() => RequiredDelayAfter(_lastSpeechSource, _lastSpeechLength);

        private static (int Min, int Max, int Budget) TypewriterProfile(bool slow) => slow
            ? (TypewriterSlowMinStepMs, TypewriterSlowMaxStepMs, TypewriterSlowTotalBudgetMs)
            : (TypewriterMinStepMs, TypewriterMaxStepMs, TypewriterTotalBudgetMs);

        /// <summary>WPF EstimateTypewriterDurationMs: added to the bubble's display time.</summary>
        internal static int EstimateTypewriterDurationMs(int length, bool slow = false)
        {
            if (length <= 0) return 0;
            var (min, max, budget) = TypewriterProfile(slow);
            int charsPerTick = Math.Max(1, length / 100);
            int stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, length)));
            int ticks = (int)Math.Ceiling((double)length / charsPerTick);
            return stepMs * ticks;
        }

        /// <summary>WPF ShowGiggle's display time: the user's 1-10 s plus the typing time; an AI reply
        /// also gets a 12 chars/s reading floor after typing, capped at 30 s (bug #193).</summary>
        internal static double DisplaySeconds(string text, SpeechSource source, double bubbleSetting, bool typed)
        {
            double user = Math.Clamp(bubbleSetting, 1.0, 10.0);
            double seconds = user;
            if (!typed) return source == SpeechSource.AI ? Math.Max(user, Math.Min(30.0, text.Length / 12.0)) : seconds;
            bool slow = source != SpeechSource.AI;
            double typeSec = EstimateTypewriterDurationMs(text.Length, slow) / 1000.0;
            seconds += typeSec;
            if (source == SpeechSource.AI)
            {
                double floor = typeSec + Math.Max(user, Math.Min(30.0, text.Length / 12.0));
                if (floor > seconds) seconds = floor;
            }
            return seconds;
        }

        /// <summary>WPF SpeechLatchRule.IsLatchStale: "speaking" with nothing on screen and nothing pending.</summary>
        internal static bool IsLatchStale(bool isGiggling, bool bubbleVisible, bool waitingForAi, bool queueEmpty,
                                          bool leadInPending, bool delayPending)
            => isGiggling && !bubbleVisible && !waitingForAi && queueEmpty && !leadInPending && !delayPending;

        // ------------------------------------------------------------------ queue

        /// <summary>How many lines wait behind the one on screen (tests, the companion lane).</summary>
        internal int QueuedSpeechCount => _speechQueue.Count;

        /// <summary>WPF ProcessNextSpeech: the next queued line, after the gap the PREVIOUS line earned.</summary>
        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0) { _isGiggling = false; return; }
            var next = _speechQueue.Dequeue();

            double remaining = Math.Max(0, CalculateRequiredDelayAfterLastSpeech() - (DateTime.Now - _lastSpeechEndTime).TotalSeconds);
            if (remaining > 0 && !SpeechInstant)
            {
                _speechDelayTimer?.Stop();
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(remaining) };
                t.Tick += (_, _) => { t.Stop(); if (_speechDelayTimer == t) _speechDelayTimer = null; ShowSpeechBySource(next); };
                _speechDelayTimer = t;
                t.Start();
                return;
            }
            ShowSpeechBySource(next);
        }

        private void ShowSpeechBySource((string Text, SpeechSource Source, string? Audio, bool BarkVoice) line)
        {
            if (line.Source == SpeechSource.Trigger) { ShowTriggerBubbleImmediate(line.Text); return; }
            bool playSound = line.Source == SpeechSource.AI || NextPresetGiggleSound();
            ShowGiggle(line.Text, playSound, line.Source, line.Audio, barkVoice: line.BarkVoice);
        }

        /// <summary>WPF Speech.cs:2047 ShowTriggerBubble: a trigger waits its turn behind a line still up.</summary>
        internal void EnqueueTrigger(string trigger)
        {
            if (IsMuted) { ShowTriggerBubbleImmediate(trigger); return; }   // haptic + skip, as WPF
            if (_isGiggling || (DateTime.Now - _lastSpeechEndTime).TotalSeconds < CalculateRequiredDelayAfterLastSpeech())
            {
                _speechQueue.Enqueue((trigger, SpeechSource.Trigger, null, false));
                if (!_isGiggling) { _isGiggling = true; ProcessNextSpeech(); }
                return;
            }
            ShowTriggerBubbleImmediate(trigger);
        }

        private int _presetGiggleCounter;

        /// <summary>WPF: "1 in 5 for presets".</summary>
        internal bool NextPresetGiggleSound() => ++_presetGiggleCounter % 5 == 0;

        /// <summary>
        /// WPF Giggle (Speech.cs:240): a PRESET line, queued behind whatever she is saying and spaced by
        /// the gap the previous line earned. Dropped while an AI request is in flight or an AI bubble is
        /// up, never logged to chat history, the giggle cue on every fifth. A bark voiceline passes its
        /// clip in <paramref name="phraseAudioPath"/> (with <paramref name="barkVoice"/>); a recorded
        /// phrase clip passes it alone.
        /// </summary>
        public void Giggle(string text, string? phraseAudioPath = null, bool barkVoice = false, string? mood = null)
        {
            if (_isPlayingUninterruptibleClip || _isWaitingForAi || _isShowingAiBubble) return;
            RunOnAvatar(() =>
            {
                if (_isShowingAiBubble || _isWaitingForAi) return;   // re-checked on the UI thread, as WPF
                if (_isGiggling)
                {
                    _speechQueue.Enqueue((text, SpeechSource.Preset, phraseAudioPath, barkVoice));
                    return;
                }
                if ((DateTime.Now - _lastSpeechEndTime).TotalSeconds < CalculateRequiredDelayAfterLastSpeech())
                {
                    _speechQueue.Enqueue((text, SpeechSource.Preset, phraseAudioPath, barkVoice));
                    _isGiggling = true;
                    ProcessNextSpeech();
                    return;
                }
                ShowGiggle(text, NextPresetGiggleSound(), SpeechSource.Preset, phraseAudioPath, barkVoice: barkVoice);
            });
        }

        /// <summary>
        /// WPF GigglePriority (Speech.cs:325): say a line NOW. Clears the queue and every pending timer,
        /// logs the line to chat history and shows it with the AI badge when <paramref name="aiGenerated"/>
        /// (the CCBill visible-labelling rule: a canned line never wears it). <paramref name="onSpoken"/>
        /// fires once her voiced clip has finished (at once when nothing plays): the spoken mantra holds
        /// the mic shut on it.
        /// </summary>
        public void GigglePriority(string text, bool playSound = true, bool aiGenerated = true,
                                   string? phraseAudioPath = null, bool barkVoice = false,
                                   string? mood = null, Action? onSpoken = null)
        {
            if (_isPlayingUninterruptibleClip) { onSpoken?.Invoke(); return; }
            RunOnAvatar(() =>
            {
                if (aiGenerated) _lastAiBubbleUtc = DateTime.UtcNow;
                StopThinkingAnimation();
                _speechQueue.Clear();
                _speechTimer?.Stop();
                _speechDelayTimer?.Stop(); _speechDelayTimer = null;
                _isGiggling = false;
                AddToChatHistory(text, isUser: false);
                ShowGiggle(text, playSound, SpeechSource.AI, phraseAudioPath, aiGenerated, barkVoice, onSpoken);
            });
        }

        /// <summary>WPF ShowGiggle (Speech.cs:413): render one line - voice, typewriter, timed hide, next.</summary>
        private void ShowGiggle(string text, bool playSound, SpeechSource source, string? phraseAudioPath = null,
                                bool aiGenerated = false, bool barkVoice = false, Action? onSpoken = null)
        {
            var spokenHandled = false;
            try
            {
                if (_isPlayingUninterruptibleClip) return;
                SyncAskButtonsFor(text);

                if (_isListeningBubble)
                {
                    _listeningDotsTimer?.Stop();
                    _listeningDotsTimer = null;
                    _isListeningBubble = false;
                }
                _aiBadge.IsVisible = aiGenerated;
                _policyBadge.IsVisible = false;
                if (_isShowingChatHistory)
                {
                    _isShowingChatHistory = false;
                    _chatHistoryView.IsVisible = false;
                    _speechScroller.IsVisible = true;
                }

                // Off screen, or EMI Desk muted her: skip the line but keep the pacing honest and drain.
                if (!IsVisible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
                {
                    _isGiggling = false;
                    _lastSpeechEndTime = DateTime.Now;
                    _lastSpeechSource = source;
                    _lastSpeechLength = text.Length;
                    ProcessNextSpeech();
                    return;
                }

                _isGiggling = true;
                _isShowingAiBubble = source == SpeechSource.AI;
                StopSpokenAudio();   // a new bubble cuts the previous voice: no overlap
                NoteLineStarting(text, phraseAudioPath, source);   // talk clips (Emotes)

                bool slow = source != SpeechSource.AI;
                Action speak = () =>
                {
                    if (!_isGiggling) { onSpoken?.Invoke(); return; }   // a newer bubble took over during the lead-in
                    var handled = false;
                    if (!IsMuted)
                    {
                        PlaySpeechAudio(playSound, phraseAudioPath, barkVoice, source, onSpoken);
                        handled = true;
                    }
                    if (!handled) onSpoken?.Invoke();

                    bool typed = !SpeechInstant;
                    if (typed) StartTypewriter(text, slow); else { StopTypewriter(); _txtSpeech.Text = StripLinks(text); LinkSpeech(text); }
                    _txtSpeech.FontSize = StripLinks(text).Length > 250 ? 14 : 15;   // WPF AdjustBubbleSize
                    _speechScroller.Offset = default;
                    _speechBubble.MaxWidth = SpeechBubbleMaxWidth;
                    ApplySpeechBubblePlacement();
                    _speechBubble.IsVisible = true;

                    double seconds = DisplaySeconds(text, source, CoreSettings.Current.BubbleDurationSeconds, typed);
                    StartSpeechHideTimer(seconds, source, text.Length);
                    RestartIdleTimer();   // WPF ResetIdleTimer when speaking
                    Log.Debug("Companion says ({Source}, {Chars} chars, {Duration:F1}s)", source, text.Length, seconds);
                };
                spokenHandled = true;

                _speechLeadInTimer?.Stop();
                _speechLeadInTimer = null;
                if (source != SpeechSource.AI && !SpeechInstant)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(EmoteAudioLeadInSeconds(SpeechLeadInSeconds)) };
                    t.Tick += (_, _) => { t.Stop(); if (_speechLeadInTimer == t) _speechLeadInTimer = null; speak(); };
                    _speechLeadInTimer = t;
                    t.Start();
                }
                else speak();
            }
            catch (Exception ex) { Log.Warning(ex, "AvatarTube ShowGiggle failed"); }
            finally { if (!spokenHandled) onSpoken?.Invoke(); }
        }

        /// <summary>The timed hide: held while hovered (capped at <see cref="MaxHoverHold"/>), then the next line.</summary>
        private void StartSpeechHideTimer(double seconds, SpeechSource source, int length)
        {
            _speechTimer?.Stop();
            DateTime? hoverSince = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) =>
            {
                hoverSince ??= DateTime.UtcNow;
                if (_isMouseOverSpeechBubble && DateTime.UtcNow - hoverSince.Value < MaxHoverHold)
                {
                    timer.Interval = TimeSpan.FromSeconds(1);
                    return;
                }
                timer.Stop();
                CollapseSpeechBubble();
                _isShowingAiBubble = false;
                _lastSpeechEndTime = DateTime.Now;
                _lastSpeechSource = source;
                _lastSpeechLength = length;
                NoteLineEnded();
                ProcessNextSpeech();
            };
            _speechTimer = timer;
            timer.Start();
        }

        /// <summary>WPF CollapseSpeechBubble: hiding drops the hover latch too (no PointerExited follows).</summary>
        private void CollapseSpeechBubble()
        {
            StopTypewriter();
            _speechBubble.IsVisible = false;
            _isMouseOverSpeechBubble = false;
        }

        /// <summary>WPF ClearStaleSpeechLatch, from the idle tick: a wedged "speaking" flag is dropped.</summary>
        internal bool ClearStaleSpeechLatch()
        {
            if (!IsLatchStale(_isGiggling, _speechBubble.IsVisible, _isWaitingForAi, _speechQueue.Count == 0,
                    _speechLeadInTimer != null, _speechDelayTimer?.IsEnabled == true))
                return false;
            _isGiggling = false;
            _isMouseOverSpeechBubble = false;
            _lastSpeechEndTime = DateTime.Now;
            Log.Warning("[Speech] stale speech latch cleared - the companion was wedged silent");
            return true;
        }

        /// <summary>Panic and companion switch: nothing queued may speak afterwards.</summary>
        private void ClearSpeechQueue()
        {
            _speechQueue.Clear();
            _speechDelayTimer?.Stop(); _speechDelayTimer = null;
            _speechLeadInTimer?.Stop(); _speechLeadInTimer = null;
            StopTypewriter();
        }

        // ------------------------------------------------------------------ typewriter

        internal static string StripLinks(string text) => MarkdownLinkRegex.Replace(text ?? "", "$1");

        private void StartTypewriter(string fullText, bool slow)
        {
            StopTypewriter();
            _typewriterFullText = StripLinks(fullText);
            _typewriterSourceText = fullText;
            _typewriterIndex = 0;
            var generation = ++_typewriterGeneration;
            _txtSpeech.Text = string.Empty;
            if (_typewriterFullText.Length == 0) return;

            var (min, max, budget) = TypewriterProfile(slow);
            var stepMs = Math.Min(max, Math.Max(min, budget / Math.Max(1, _typewriterFullText.Length)));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
            timer.Tick += (s, _) =>
            {
                if (generation != _typewriterGeneration) { (s as DispatcherTimer)?.Stop(); return; }
                TypewriterTick();
            };
            _typewriterTimer = timer;
            timer.Start();
        }

        private void TypewriterTick()
        {
            int chars = Math.Max(1, _typewriterFullText.Length / 100);
            _typewriterIndex = Math.Min(_typewriterFullText.Length, _typewriterIndex + chars);
            _txtSpeech.Text = _typewriterFullText.Substring(0, _typewriterIndex);
            if (_typewriterIndex >= _typewriterFullText.Length)
            {
                _typewriterTimer?.Stop();
                _typewriterTimer = null;
                LinkSpeech(_typewriterSourceText);
            }
        }

        private string _typewriterSourceText = string.Empty;

        /// <summary>WPF PopulateSpeechBubble: once the line is fully on screen, the titles and urls
        /// in it become pink underlined links (markdown, known titles, fuzzy hits, raw urls). While
        /// it types it is plain text; any later write to the bubble's Text drops the links again.</summary>
        private void LinkSpeech(string? sourceText)
        {
            try { ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime.CompanionLinkedText.Apply(_txtSpeech, sourceText); }
            catch (Exception ex) { Log.Warning("[Speech] link pass failed: {Type}", ex.GetType().Name); }
        }

        private void StopTypewriter()
        {
            _typewriterGeneration++;
            _typewriterTimer?.Stop();
            _typewriterTimer = null;
        }

        /// <summary>Test hook: finish the line being typed.</summary>
        internal void FinishTypewriterForTest()
        {
            if (_typewriterTimer == null) return;
            StopTypewriter();
            _txtSpeech.Text = _typewriterFullText;
            LinkSpeech(_typewriterSourceText);
        }

        // ------------------------------------------------------------------ voice

        /// <summary>
        /// The voice for one bubble, WPF's volume curves verbatim: a bark voiceline at master^1.5 * 0.85,
        /// a phrase clip at * 0.56, the requested giggle at * 0.7, the fallback cue at * 0.5. MasterVolume 0
        /// means "attempt no audio at all". "Mute Voice Lines" (#846) silences only the spoken VO and drops
        /// back to the fallback cue, so she still reads as present.
        /// </summary>
        private void PlaySpeechAudio(bool playSound, string? phraseAudioPath, bool barkVoice, SpeechSource source,
                                     Action? onSpoken = null)
        {
            var handedOff = false;
            try
            {
                var master = CoreSettings.Current.MasterVolume / 100f;
                if (master <= 0f) return;
                var curved = (float)Math.Pow(master, 1.5);

                if (!string.IsNullOrEmpty(phraseAudioPath))
                {
                    if (barkVoice && CoreSettings.Current.CompanionVoiceLinesMuted) { PlayFallbackBubbleSound(curved); return; }
                    if (!File.Exists(phraseAudioPath)) return;
                    handedOff = true;
                    StopSpokenAudio();
                    _stopSpoken = CoreAudio.PlayStoppable(phraseAudioPath!, curved * (barkVoice ? 0.85f : 0.56f),
                        barkVoice ? "bark-voice" : "phrase-audio", onFinished: onSpoken);
                    return;
                }
                if (playSound) PlayGiggleSound(curved);
                else if (source != SpeechSource.AI) PlayFallbackBubbleSound(curved);
            }
            catch (Exception ex) { Log.Debug("AvatarTube speech audio failed: {Error}", ex.Message); }
            finally { if (!handedOff) onSpoken?.Invoke(); }
        }

        /// <summary>WPF SuppressGiggleSfx: Bambi Sleep (real barks) and CCP Default stay silent.</summary>
        private static bool SuppressGiggleSfx() => ModAudioPolicy.SuppressesGiggleSfx(CoreMods.ActiveModId);

        /// <summary>One of giggle5-8 (WPF PlayGiggleSound): the mod's override, else the shipped copy.</summary>
        private void PlayGiggleSound(float curvedVolume)
        {
            if (SuppressGiggleSfx()) return;
            var path = ResolveSound($"giggle{5 + _random.Next(4)}.mp3");
            if (path != null) CoreAudio.PlayOneShot(path, curvedVolume * 0.7f, "giggle");
        }

        /// <summary>WPF PlayFallbackBubbleSound (Reactions.cs:322): giggle1-4 at half volume.</summary>
        private void PlayFallbackBubbleSound(float curvedVolume)
        {
            if (SuppressGiggleSfx()) return;
            var path = ResolveSound($"giggle{1 + _random.Next(4)}.MP3");
            if (path != null) CoreAudio.PlayOneShot(path, curvedVolume * 0.5f, "bubble-fallback");
        }

        private static string? ResolveSound(string name)
        {
            var path = CoreModArt.OverridePath($"sounds/{name}")
                       ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", name);
            if (File.Exists(path)) return path;
            // Linux is case sensitive: the shipped giggle1-4 are .MP3 on Windows, try the other case.
            var alt = Path.ChangeExtension(path, Path.GetExtension(path) == ".MP3" ? ".mp3" : ".MP3");
            return File.Exists(alt) ? alt : null;
        }
    }
}
